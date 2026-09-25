using System.Runtime.Versioning;
using System.Security.Principal;
using Google.Protobuf;
using Grpc.Core;
using CmdWarden.Agent.Approval;
using CmdWarden.Agent.Identity;
using CmdWarden.Contracts;
using CmdWarden.Contracts.Grpc;

namespace CmdWarden.Agent;

[SupportedOSPlatform("windows")]
public sealed class SessionAgentService : SessionAgent.SessionAgentBase
{
    public const string DefaultGhSecretName = "GH_TOKEN";
    /// <summary>Optional child-only registry auth JSON for docker grants (#42).</summary>
    public const string DefaultDockerAuthConfigSecretName = "DOCKER_AUTH_CONFIG";
    /// <summary>Child env key so nested go-gh helpers use the pinned real binary (#39).</summary>
    public const string GhPathEnvName = "GH_PATH";

    private readonly string _pipeName;
    private readonly CredentialVault _vault;
    private readonly LauncherIdentityResolver _identity;
    private readonly PolicyStore _policy;
    private readonly IApprovalGate _approvalGate;
    private readonly ApprovalMemory _memory;
    private readonly object _promptLock = new();
    private readonly ToolPinStore _pins;
    private readonly AuditLog _audit;

    public SessionAgentService(
        AgentRuntimeInfo runtime,
        CredentialVault vault,
        LauncherIdentityResolver identity,
        PolicyStore policy,
        IApprovalGate approvalGate,
        ApprovalMemory memory,
        ToolPinStore pins,
        AuditLog audit)
    {
        _pipeName = runtime.PipeName;
        _vault = vault;
        _identity = identity;
        _policy = policy;
        _approvalGate = approvalGate;
        _memory = memory;
        _pins = pins;
        _audit = audit;
    }

    public override Task<HealthResponse> GetHealth(HealthRequest request, ServerCallContext context)
    {
        var identity = WindowsIdentity.GetCurrent();
        var launcher = ResolveSafe(context);
        var response = new HealthResponse
        {
            Product = ProductInfo.Name,
            Version = ProductInfo.Version,
            Alive = true,
            ProcessId = Environment.ProcessId,
            UserName = Environment.UserName,
            MachineName = Environment.MachineName,
            PipeName = _pipeName,
            SessionId = identity.User?.Value ?? string.Empty,
            ClientPid = launcher.ClientPid,
            LauncherPolicyKey = launcher.Selected.PolicyKey,
            LauncherKind = launcher.Selected.Kind,
            AutoApproveEligible = launcher.AutoApproveEligible,
            LauncherPath = launcher.Selected.Path ?? "",
        };
        return Task.FromResult(response);
    }

    public override Task<CallerIdentityResponse> ResolveCallerIdentity(
        CallerIdentityRequest request,
        ServerCallContext context)
    {
        var launcher = ResolveSafe(context);
        var response = new CallerIdentityResponse
        {
            ClientPid = launcher.ClientPid,
            ClientPidFromPipe = launcher.ClientPidFromPipe,
            SelectedPolicyKey = launcher.Selected.PolicyKey,
            SelectedKind = launcher.Selected.Kind,
            AutoApproveEligible = launcher.AutoApproveEligible,
            SelectedPath = launcher.Selected.Path ?? "",
            Notes = launcher.Notes,
        };

        foreach (var node in launcher.Chain)
        {
            response.Chain.Add(new ProcessIdentity
            {
                Pid = node.Pid,
                Path = node.Path ?? "",
                FileName = node.FileName ?? "",
                Kind = node.Kind,
                PolicyKey = node.PolicyKey,
                Publisher = node.Publisher ?? "",
                Thumbprint = node.Thumbprint ?? "",
                Sha256 = node.Sha256 ?? "",
                PidReuseSuspected = node.PidReuseSuspected,
            });
        }

        return Task.FromResult(response);
    }

    public override Task<SaveSecretResponse> SaveSecret(SaveSecretRequest request, ServerCallContext context)
    {
        try
        {
            var bytes = request.Value.ToByteArray();
            _vault.Save(request.Name, bytes);
            Array.Clear(bytes);
            return Task.FromResult(new SaveSecretResponse
            {
                TargetName = VaultNames.TargetName(request.Name),
            });
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (Exception ex)
        {
            throw new RpcException(new Status(StatusCode.Internal, "SaveSecret failed: " + ex.Message));
        }
    }

    public override Task<ReleaseSecretResponse> ReleaseSecret(
        ReleaseSecretRequest request,
        ServerCallContext context)
    {
        var launcher = ResolveSafe(context);
        var tool = string.IsNullOrWhiteSpace(request.Tool) ? "inject" : request.Tool.Trim();
        var commandClass = CommandClassNames.ParseOrUnknown(request.CommandClass);

        // Reload so CLI enroll/set takes effect without agent restart (spike).
        ApplyPolicyChanges(_policy.Load());
        var resolved = _policy.ResolveLevel(
            tool,
            launcher.Selected.PolicyKey,
            launcher.AutoApproveEligible);

        var levelName = PolicyLevelNames.Format(resolved.Level);
        var className = CommandClassNames.Format(commandClass);
        var decision = PolicyEvaluator.Decide(resolved.Level, commandClass);
        var decisionLabel = GateDecisions.AutoAllow;
        string? decisionReason = null;
        var secretName = VaultNames.EnvVarName(request.Name);

        if (decision != PolicyDecision.AutoAllow)
        {
            var gate = GateOrThrow(launcher, resolved, commandClass, BuildApprovalRequest(
                tool: tool,
                className: className,
                levelName: levelName,
                launcher: launcher,
                secretName: secretName,
                purpose: request.Purpose,
                enrollmentKind: LauncherEnrollmentKindNames.Format(resolved.EnrollmentKind),
                policyNote: resolved.ReasonCode,
                commandLine: string.IsNullOrWhiteSpace(request.CommandLine) ? null : request.CommandLine,
                toolPath: null,
                workingDirectory: null), verb: "release", auditSecretName: secretName, purpose: request.Purpose);
            decisionLabel = gate.Decision;
            decisionReason = gate.Reason;
        }

        // Audit before vault read (fail closed, #199).
        AppendOrThrow(GateRecord(decisionLabel, decisionReason, tool, className, levelName,
            launcher, resolved, secretName, request.Purpose));

        try
        {
            var bytes = _vault.Read(request.Name);
            var response = new ReleaseSecretResponse
            {
                Name = secretName,
                Value = ByteString.CopyFrom(bytes),
                TargetName = VaultNames.TargetName(request.Name),
                LauncherPolicyKey = launcher.Selected.PolicyKey,
                LauncherKind = launcher.Selected.Kind,
                Tool = tool,
                CommandClass = className,
                PolicyLevel = levelName,
                Decision = decisionLabel,
                EnrollmentKind = LauncherEnrollmentKindNames.Format(resolved.EnrollmentKind),
            };
            Array.Clear(bytes);
            return Task.FromResult(response);
        }
        catch (KeyNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (Exception ex)
        {
            throw new RpcException(new Status(StatusCode.Internal, "ReleaseSecret failed: " + ex.Message));
        }
    }

    public override Task<DeleteSecretResponse> DeleteSecret(
        DeleteSecretRequest request,
        ServerCallContext context)
    {
        try
        {
            var deleted = _vault.Delete(request.Name);
            return Task.FromResult(new DeleteSecretResponse { Deleted = deleted });
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (Exception ex)
        {
            throw new RpcException(new Status(StatusCode.Internal, "DeleteSecret failed: " + ex.Message));
        }
    }

    public override Task<ListSecretNamesResponse> ListSecretNames(
        ListSecretNamesRequest request,
        ServerCallContext context)
    {
        try
        {
            var response = new ListSecretNamesResponse();
            response.Names.AddRange(_vault.ListNames());
            return Task.FromResult(response);
        }
        catch (Exception ex)
        {
            throw new RpcException(new Status(StatusCode.Internal, "ListSecretNames failed: " + ex.Message));
        }
    }

    public override Task<AuthorizeResponse> Authorize(AuthorizeRequest request, ServerCallContext context)
    {
        var tool = string.IsNullOrWhiteSpace(request.Tool) ? "gh" : request.Tool.Trim().ToLowerInvariant();
        // git/az never inject vault secrets (#40 / #41). docker optionally injects DOCKER_AUTH_CONFIG (#42).
        var neverInjectVault = tool is "git" or "az";
        var secretName = string.IsNullOrWhiteSpace(request.SecretName)
            ? tool switch
            {
                "docker" => DefaultDockerAuthConfigSecretName,
                _ => DefaultGhSecretName,
            }
            : VaultNames.EnvVarName(request.SecretName);
        var argv = request.Argv.ToList();
        // gh auth login/refresh/logout/switch refuse GH_TOKEN; the grant carries none (#198).
        var ghKeyringAuth = tool == "gh" && GhCommandClassifier.IsKeyringAuthMutation(argv);
        // Audit secret id only when a vault value may be released (gh always; docker when present).
        var auditSecretName = neverInjectVault || ghKeyringAuth ? null : secretName;

        var launcher = ResolveSafe(context);
        ApplyPolicyChanges(_policy.Load());
        if (_pins.DetectRepin(tool))
            _memory.ClearForTool(tool);

        var strongPin = _pins.TryGet(tool)?.IsStrong == true;
        var commandClass = tool switch
        {
            "gh" => GhCommandClassifier.Classify(argv),
            "git" => GitCommandClassifier.Classify(argv),
            "az" => AzCommandClassifier.Classify(argv),
            "docker" => DockerCommandClassifier.Classify(argv),
            _ => CommandClass.Unknown,
        };
        // Strong git: a credential.* key in GIT_CONFIG_KEY_<n> reads like -c credential.* (#207).
        if (tool == "git" && strongPin && GitCommandClassifier.HasSecretAdjacentConfigEnv(request.CallerEnv))
            commandClass = CommandClass.SecretReveal;
        var className = CommandClassNames.Format(commandClass);

        var resolved = _policy.ResolveLevel(tool, launcher.Selected.PolicyKey, launcher.AutoApproveEligible);
        var levelName = PolicyLevelNames.Format(resolved.Level);
        var decision = PolicyEvaluator.Decide(resolved.Level, commandClass);
        var decisionLabel = GateDecisions.AutoAllow;
        string? decisionReason = null;

        if (decision != PolicyDecision.AutoAllow)
        {
            var note = resolved.ReasonCode;
            // Display pin path for the card when known; hard pin enforcement still runs after approval.
            var toolPathForUi = _pins.TryGet(tool)?.Path;
            var commandLine = FormatCommandLine(tool, argv);
            var gate = GateOrThrow(launcher, resolved, commandClass, BuildApprovalRequest(
                tool: tool,
                className: className,
                levelName: levelName,
                launcher: launcher,
                secretName: auditSecretName ?? "",
                purpose: "authorize",
                enrollmentKind: LauncherEnrollmentKindNames.Format(resolved.EnrollmentKind),
                policyNote: note,
                commandLine: commandLine,
                toolPath: toolPathForUi,
                workingDirectory: null), verb: "authorize", auditSecretName: auditSecretName, purpose: "authorize");
            decisionLabel = gate.Decision;
            decisionReason = gate.Reason;
        }

        var pinCheck = _pins.Check(tool);
        if (pinCheck.IsMissing)
        {
            TryAudit(GateDecisions.Deny, PolicyReasonCodes.PinMissing, tool, className, levelName, launcher, resolved, auditSecretName);
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition,
                $"{PolicyReasonCodes.PinMissing}: no pin for tool '{tool}'. Run cw harden {tool}."));
        }

        if (!pinCheck.IsOk || pinCheck.Pin is null)
        {
            TryAudit(GateDecisions.Deny, PolicyReasonCodes.PinMismatch, tool, className, levelName, launcher, resolved, auditSecretName);
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition,
                $"{PolicyReasonCodes.PinMismatch}: {pinCheck.Error}"));
        }

        // Help/meta: grant path without vault secret when possible (issue #35 / #40 / #41 / #42).
        var helpOnly = tool switch
        {
            "gh" => GhCommandClassifier.IsHelpOnly(argv),
            "git" => GitCommandClassifier.IsHelpOnly(argv),
            "az" => AzCommandClassifier.IsHelpOnly(argv),
            "docker" => DockerCommandClassifier.IsHelpOnly(argv),
            _ => false,
        };
        // gh requires vault token (except help); docker optionally injects DOCKER_AUTH_CONFIG when present.
        // Strong docker serves registry credentials through the helper, so the overlay stops (#204).
        // Strong gh serves per-host entries from CmdWarden/gh/ and ignores the compat GH_TOKEN (#208).
        var strongGh = tool == "gh" && pinCheck.Pin.IsStrong;
        var requireVaultSecret = tool == "gh" && !helpOnly && !ghKeyringAuth && !strongGh;
        var optionalDockerAuth = tool == "docker" && !helpOnly && !pinCheck.Pin.IsStrong;
        var ghReleases = strongGh && !helpOnly && !ghKeyringAuth
            ? StrongGhReleases(argv, request.CallerEnv.TryGetValue("GH_HOST", out var ghHost) ? ghHost : null)
            : Array.Empty<(string EnvName, string Target)>();

        // Audit before any vault value read (fail closed, #199).
        // Docker rows name the secret only when the vault holds it; ListNames reads no value.
        var auditedSecretName = requireVaultSecret
            || (optionalDockerAuth && _vault.ListNames().Contains(secretName, StringComparer.OrdinalIgnoreCase))
            ? secretName
            : null;
        AppendOrThrow(GateRecord(decisionLabel, decisionReason, tool, className, levelName,
            launcher, resolved, auditedSecretName, helpOnly ? "authorize-help" : "authorize"));
        // One row per released strong-gh entry, vault name only (#208).
        foreach (var (_, target) in ghReleases)
            AppendOrThrow(GateRecord(decisionLabel, decisionReason, tool, className, levelName,
                launcher, resolved, target, "authorize"));

        // A live shim run covers helper reads from its chain (#202). Help-only and secret-reveal never do.
        if (!helpOnly && commandClass != CommandClass.SecretReveal)
            _memory.RecordRun(launcher.ClientPid, launcher.ClientPidFromPipe, launcher.Selected.PolicyKey, tool);

        try
        {
            var response = new AuthorizeResponse
            {
                Allowed = true,
                RealPath = pinCheck.Pin.Path,
                RealSha256 = pinCheck.Pin.Sha256,
                Decision = decisionLabel,
                CommandClass = className,
                PolicyLevel = levelName,
                LauncherPolicyKey = launcher.Selected.PolicyKey,
                LauncherKind = launcher.Selected.Kind,
                EnrollmentKind = LauncherEnrollmentKindNames.Format(resolved.EnrollmentKind),
                Tool = tool,
                ReasonCode = decisionReason ?? "",
                Message = "",
            };

            // Child-only: point nested tooling at the real binary, not the PATH shim (#39).
            if (tool == "gh")
                response.Env[GhPathEnvName] = pinCheck.Pin.Path;

            // Strong git: the child reads the real global config only (#207).
            if (tool == "git" && pinCheck.Pin.IsStrong)
                response.StripEnv.AddRange(GitCommandClassifier.StrongStripEnv);

            // Strong gh: login/refresh/logout through the shim end in MigrateToolStore (#208).
            response.MigrateAfterRun = strongGh && GhCommandClassifier.KeyringAuthVerb(argv) is "login" or "refresh" or "logout";
            foreach (var (envName, target) in ghReleases)
            {
                var bytes = _vault.ReadTarget(target)?.Blob ?? throw new KeyNotFoundException($"{target} not found in vault.");
                response.Env[envName] = System.Text.Encoding.UTF8.GetString(bytes);
                Array.Clear(bytes);
            }

            if (requireVaultSecret)
            {
                var bytes = _vault.Read(secretName);
                var token = System.Text.Encoding.UTF8.GetString(bytes);
                Array.Clear(bytes);
                response.Env[secretName] = token;
            }
            else if (optionalDockerAuth)
            {
                // Child-only DOCKER_AUTH_CONFIG; never mutates parent process env (#42).
                try
                {
                    var bytes = _vault.Read(secretName);
                    response.Env[DefaultDockerAuthConfigSecretName] = System.Text.Encoding.UTF8.GetString(bytes);
                    Array.Clear(bytes);
                }
                catch (KeyNotFoundException)
                {
                    // Compat: gate-only grant; ambient Desktop/wincred store remains (#42).
                }
            }

            return Task.FromResult(response);
        }
        catch (KeyNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
        catch (Exception ex)
        {
            throw new RpcException(new Status(StatusCode.Internal, "Authorize failed: " + ex.Message));
        }
    }

    /// <summary>
    /// Strong gh host classes (#208): GH_TOKEN is the github.com active slot. GH_ENTERPRISE_TOKEN is
    /// the one GHES slot in the vault, or the GHES slot argv/GH_HOST names. Two GHES slots and no
    /// named host release none. Targets only; no value is read here.
    /// </summary>
    private static IReadOnlyList<(string EnvName, string Target)> StrongGhReleases(IReadOnlyList<string> argv, string? ghHostEnv)
    {
        var store = new GhStrongStore();
        var slots = store.Keys().Where(k => k.User.Length == 0).Select(k => k.Host).Distinct().ToList();
        var releases = new List<(string, string)>();
        if (slots.Contains(GhVaultNames.DefaultHost))
            releases.Add(("GH_TOKEN", store.Target("", GhVaultNames.DefaultHost)));
        var ghes = slots.Where(GhVaultNames.IsEnterprise).ToList();
        var named = GhCommandClassifier.NamedHost(argv, ghHostEnv);
        var pick = named is not null && ghes.Contains(named) ? named : ghes.Count == 1 ? ghes[0] : null;
        if (pick is not null)
            releases.Add(("GH_ENTERPRISE_TOKEN", store.Target("", pick)));
        return releases;
    }

    /// <summary>
    /// Strong gh (#208): after a granted login/refresh/logout run exits 0, the shim asks the Agent
    /// to move stock gh tokens into the vault (or drop the logged-out entry). Served only in strong
    /// mode and only from a live covered run; no second prompt.
    /// </summary>
    public override Task<MigrateToolStoreResponse> MigrateToolStore(MigrateToolStoreRequest request, ServerCallContext context)
    {
        var tool = request.Tool.Trim().ToLowerInvariant();
        if (tool != "gh")
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"MigrateToolStore: unsupported tool '{tool}'."));
        var launcher = ResolveSafe(context);
        ApplyPolicyChanges(_policy.Load());
        var resolved = _policy.ResolveLevel(tool, launcher.Selected.PolicyKey, launcher.AutoApproveEligible);
        var levelName = PolicyLevelNames.Format(resolved.Level);
        var className = CommandClassNames.Format(CommandClass.Write);
        const string purpose = "migrate-store";
        var secretName = GhVaultNames.Prefix + "*";

        var pinCheck = _pins.Check(tool);
        if (!pinCheck.IsOk || pinCheck.Pin is null || !pinCheck.Pin.IsStrong)
        {
            TryAudit(GateDecisions.Deny, PolicyReasonCodes.PinMismatch, tool, className, levelName, launcher, resolved, secretName, purpose);
            throw new RpcException(new Status(StatusCode.FailedPrecondition,
                "MigrateToolStore: gh is not in strong mode. Run cw harden gh --strong."));
        }
        if (!_memory.IsRunCovered(launcher.Chain.Select(n => n.Pid), tool))
        {
            TryAudit(GateDecisions.Deny, PolicyReasonCodes.HelperParentMissing, tool, className, levelName, launcher, resolved, secretName, purpose);
            throw new RpcException(new Status(StatusCode.PermissionDenied,
                $"{PolicyReasonCodes.HelperParentMissing}: MigrateToolStore needs a live granted gh run."));
        }

        AppendOrThrow(GateRecord(GateDecisions.AutoAllow, PolicyReasonCodes.RunCovered, tool, className, levelName,
            launcher, resolved, secretName, purpose));
        try
        {
            var store = new GhStrongStore();
            var response = new MigrateToolStoreResponse();
            if (GhCommandClassifier.KeyringAuthVerb(request.Argv) == "logout")
            {
                response.Deleted.AddRange(store.Reconcile());
            }
            else
            {
                var result = store.Migrate(pinCheck.Pin.Path);
                response.Migrated.AddRange(result.Migrated);
                response.Deleted.AddRange(result.Deleted);
            }
            return Task.FromResult(response);
        }
        catch (Exception ex)
        {
            throw new RpcException(new Status(StatusCode.Internal, "MigrateToolStore failed: " + ex.Message));
        }
    }

    /// <summary>
    /// Credential helper gate (#202, #205). The Agent owns the vault name and the class
    /// (get, list read; store, erase write). Only the pinned real tool may call.
    /// </summary>
    public override Task<HelperCredentialResponse> HelperCredential(
        HelperCredentialRequest request,
        ServerCallContext context)
    {
        var tool = request.Tool.Trim().ToLowerInvariant();
        if (tool is not "docker" and not "git")
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"HelperCredential: unsupported tool '{tool}'."));
        var action = request.Action.Trim().ToLowerInvariant();
        var commandClass = action switch
        {
            "get" or "list" => CommandClass.Read,
            "store" or "erase" => CommandClass.Write,
            _ => throw new RpcException(new Status(StatusCode.InvalidArgument, $"HelperCredential: unknown action '{action}'.")),
        };
        var serverUrl = request.ServerUrl.Trim();
        if (action != "list" && serverUrl.Length == 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "HelperCredential: server_url is required."));
        var prefix = VaultNames.HelperTargetPrefix(tool);
        // Audit names the URL, never a value. list names the whole prefix.
        var secretName = action == "list" ? prefix + "*" : serverUrl;
        var purpose = "helper-" + action;
        var isGit = tool == GitVaultNames.Tool;

        var launcher = ResolveSafe(context);
        ApplyPolicyChanges(_policy.Load());
        var resolved = _policy.ResolveLevel(tool, launcher.Selected.PolicyKey, launcher.AutoApproveEligible);
        var levelName = PolicyLevelNames.Format(resolved.Level);
        var className = CommandClassNames.Format(commandClass);

        var pinCheck = _pins.Check(tool);
        var chainError = pinCheck.IsOk && pinCheck.Pin is not null
            ? HelperChainRule.Check(launcher.Chain, pinCheck.Pin)
            : PolicyReasonCodes.HelperParentMissing;
        if (chainError is not null)
        {
            TryAudit(GateDecisions.Deny, chainError, tool, className, levelName, launcher, resolved, secretName, purpose);
            throw new RpcException(new Status(
                StatusCode.PermissionDenied,
                $"{chainError}: no pinned {tool} above the credential helper (pin: {pinCheck.Error ?? "ok"})."));
        }

        var gitEqual = isGit && action is "store" or "erase"
            && GitEntryEquals(serverUrl, request.Username, request.Secret, request.PasswordExpiryUtc, request.OauthRefreshToken);
        var gitNoOp = isGit && (
            (action == "store" && (request.Ephemeral || gitEqual))
            || (action == "erase" && !gitEqual));

        var decisionLabel = GateDecisions.AutoAllow;
        string? decisionReason = null;
        if (gitNoOp)
        {
            decisionReason = PolicyReasonCodes.Unchanged;
        }
        else if (PolicyEvaluator.Decide(resolved.Level, commandClass) != PolicyDecision.AutoAllow)
        {
            if (commandClass == CommandClass.Read && _memory.IsRunCovered(launcher.Chain.Select(n => n.Pid), tool))
            {
                decisionReason = PolicyReasonCodes.RunCovered;
            }
            else
            {
                var gate = GateOrThrow(launcher, resolved, commandClass, BuildApprovalRequest(
                    tool: tool,
                    className: className,
                    levelName: levelName,
                    launcher: launcher,
                    secretName: secretName,
                    purpose: purpose,
                    enrollmentKind: LauncherEnrollmentKindNames.Format(resolved.EnrollmentKind),
                    policyNote: resolved.ReasonCode,
                    commandLine: $"{tool} credential {action} {serverUrl}".TrimEnd(),
                    toolPath: pinCheck.Pin!.Path,
                    workingDirectory: null), verb: "release", auditSecretName: secretName, purpose: purpose);
                decisionLabel = gate.Decision;
                decisionReason = gate.Reason;
            }
        }

        var record = GateRecord(decisionLabel, decisionReason, tool, className, levelName,
            launcher, resolved, secretName, purpose);
        if (gitNoOp)
            TryAudit(decisionLabel, decisionReason, tool, className, levelName, launcher, resolved, secretName, purpose);
        else
            AppendOrThrow(record);

        var response = new HelperCredentialResponse
        {
            Decision = decisionLabel,
            ReasonCode = decisionReason ?? "",
            CommandClass = className,
        };
        try
        {
            switch (action)
            {
                case "get":
                    if (isGit)
                    {
                        var gitHit = ReadGit(serverUrl, request.Username)
                            ?? throw new RpcException(new Status(StatusCode.NotFound, $"No {tool} credential for '{serverUrl}'."));
                        response.Username = gitHit.Entry.UserName;
                        response.Secret = CredentialVault.Utf8(gitHit.Entry.Blob);
                        response.PasswordExpiryUtc = gitHit.Entry.Comment;
                        response.OauthRefreshToken = gitHit.Refresh ?? "";
                        Array.Clear(gitHit.Entry.Blob);
                    }
                    else
                    {
                        var entry = _vault.ReadTarget(VaultNames.HelperTargetName(tool, serverUrl))
                            ?? throw new RpcException(new Status(StatusCode.NotFound, $"No {tool} credential for '{serverUrl}'."));
                        response.Username = entry.UserName;
                        response.Secret = CredentialVault.Utf8(entry.Blob);
                        Array.Clear(entry.Blob);
                    }
                    break;
                case "store":
                    if (isGit)
                    {
                        if (!request.Ephemeral && !gitEqual)
                            SaveGit(serverUrl, request.Username, request.Secret, request.PasswordExpiryUtc, request.OauthRefreshToken);
                    }
                    else
                    {
                        _vault.SaveTarget(VaultNames.HelperTargetName(tool, serverUrl), request.Username, CredentialVault.Utf8Bytes(request.Secret));
                    }
                    break;
                case "erase":
                    if (isGit)
                    {
                        if (gitEqual)
                            EraseGit(serverUrl, request.Username);
                    }
                    else
                    {
                        _vault.DeleteTarget(VaultNames.HelperTargetName(tool, serverUrl));
                    }
                    break;
                default:
                    foreach (var target in _vault.ListTargets(prefix))
                        response.Entries.Add(new HelperCredentialEntry
                        {
                            ServerUrl = target.Target[prefix.Length..],
                            Username = target.UserName,
                        });
                    break;
            }
            return Task.FromResult(response);
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (Exception ex) when (ex is not RpcException)
        {
            throw new RpcException(new Status(StatusCode.Internal, "HelperCredential failed: " + ex.Message));
        }
    }

    public override Task<ListSessionAllowsResponse> ListSessionAllows(
        ListSessionAllowsRequest request,
        ServerCallContext context)
    {
        var response = new ListSessionAllowsResponse();
        foreach (var grant in _memory.ListSessions())
        {
            response.Rows.Add(new SessionAllowRow
            {
                Id = grant.Id,
                LauncherPolicyKey = grant.LauncherPolicyKey,
                LauncherKind = grant.LauncherKind,
                Pid = grant.LauncherPid,
                Tool = grant.Tool,
                SecretName = grant.SecretName,
                CommandClass = CommandClassNames.Format(grant.GrantedClass),
                GrantedAtUtc = grant.GrantedAtUtc.ToString("o"),
                LastUsedUtc = grant.LastUsedUtc.ToString("o"),
                IdleExpiresUtc = _memory.IdleExpiresUtc(grant).ToString("o"),
            });
        }
        return Task.FromResult(response);
    }

    public override Task<RevokeSessionAllowResponse> RevokeSessionAllow(
        RevokeSessionAllowRequest request,
        ServerCallContext context)
    {
        var removed = request.All
            ? _memory.RevokeAllSessions()
            : _memory.RevokeSession(request.Id);
        return Task.FromResult(new RevokeSessionAllowResponse { Removed = removed });
    }

    private static AuditGateRecord GateRecord(
        string decisionLabel,
        string? reason,
        string tool,
        string className,
        string levelName,
        LauncherResolution launcher,
        PolicyResolveResult resolved,
        string? secretName,
        string? purpose) => new()
    {
        Decision = decisionLabel,
        ReasonCode = reason,
        Tool = tool,
        CommandClass = className,
        PolicyLevel = levelName,
        LauncherPolicyKey = launcher.Selected.PolicyKey,
        LauncherKind = launcher.Selected.Kind,
        EnrollmentKind = LauncherEnrollmentKindNames.Format(resolved.EnrollmentKind),
        SecretName = secretName,
        Purpose = purpose,
        LauncherPath = launcher.Selected.Path,
        ClientPid = launcher.ClientPid,
    };

    /// <summary>Grant path: no value leaves before the row persists (#199).</summary>
    private void AppendOrThrow(AuditGateRecord record)
    {
        try
        {
            _audit.AppendGateDecision(record);
        }
        catch (Exception ex)
        {
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition,
                $"{PolicyReasonCodes.ApprovalUnavailable}: audit write failed; release blocked: {ex.Message}"));
        }
    }

    /// <summary>Deny and unavailable paths: best-effort, no value leaves either way.</summary>
    private void TryAudit(
        string decisionLabel,
        string? reason,
        string tool,
        string className,
        string levelName,
        LauncherResolution launcher,
        PolicyResolveResult resolved,
        string? secretName,
        string? purpose = "authorize")
    {
        try
        {
            _audit.AppendGateDecision(GateRecord(decisionLabel, reason, tool, className, levelName,
                launcher, resolved, secretName, purpose));
        }
        catch
        {
            // Deny path is best-effort. The grant path fails closed in AppendOrThrow.
        }
    }

    /// <summary>Drop approval memory that a policy edit (set or unenroll) just invalidated (#133).</summary>
    private bool IsHarnessKey(string policyKey) =>
        _policy.Launchers.TryGetValue(policyKey, out var entry)
        && LauncherEnrollmentKindNames.TryParse(entry.Kind, out var kind)
        && kind == LauncherEnrollmentKind.AiHarness;

    private void ApplyPolicyChanges(IReadOnlyList<PolicyStore.PolicyChange> changes)
    {
        foreach (var change in changes)
        {
            if (change.Tool is null)
                _memory.ClearForLauncherKey(change.LauncherPolicyKey);
            else
                _memory.ClearForPolicyChange(change.LauncherPolicyKey, change.Tool);
        }
    }

    private sealed record GateResult(ApprovalOutcome Outcome, string Decision, string? Reason);

    /// <summary>
    /// Gate, then return only an allow. Deny and unavailable write a best-effort row and throw
    /// PermissionDenied; no value leaves on those paths.
    /// </summary>
    private GateResult GateOrThrow(
        LauncherResolution launcher,
        PolicyResolveResult resolved,
        CommandClass commandClass,
        ApprovalRequest request,
        string verb,
        string? auditSecretName,
        string? purpose)
    {
        var gate = Gate(launcher, resolved, commandClass, request);
        var context = $"tool='{request.Tool}' class='{request.CommandClass}' level='{request.PolicyLevel}' " +
                      $"launcher='{launcher.Selected.PolicyKey}'";
        switch (gate.Outcome)
        {
            case ApprovalOutcome.AllowOnce:
                return gate;
            case ApprovalOutcome.Deny:
                TryAudit(GateDecisions.Deny, gate.Reason ?? PolicyReasonCodes.UserDenied, request.Tool, request.CommandClass,
                    request.PolicyLevel, launcher, resolved, auditSecretName, purpose);
                throw new RpcException(new Status(
                    StatusCode.PermissionDenied,
                    $"{PolicyReasonCodes.UserDenied}: user denied {verb} for {context}." + ReuseSuffix(gate.Reason)));
            default:
                // Unavailable / fail closed - preserve policy context when useful.
                var note = resolved.ReasonCode;
                var failReason = note is PolicyReasonCodes.NotEnrolled or PolicyReasonCodes.UnknownLauncher
                    ? note
                    : PolicyReasonCodes.ApprovalUnavailable;
                TryAudit(GateDecisions.Unavailable, failReason, request.Tool, request.CommandClass,
                    request.PolicyLevel, launcher, resolved, auditSecretName, purpose);
                throw new RpcException(new Status(
                    StatusCode.PermissionDenied,
                    $"{failReason}: Approval Gate unavailable; {verb} blocked for {context}."));
        }
    }

    /// <summary>
    /// Lookup order when policy needs approval (#131, #132): transient entry, session allow,
    /// then the Approval Gate. Human outcomes are remembered; Unavailable never is.
    /// </summary>
    private GateResult Gate(
        LauncherResolution launcher,
        PolicyResolveResult resolved,
        CommandClass commandClass,
        ApprovalRequest request)
    {
        var transientKey = ApprovalMemory.TransientKey(request);
        if (_memory.TryGetTransient(transientKey) is { } remembered)
        {
            return new GateResult(remembered, DecisionFor(remembered), PolicyReasonCodes.TransientReuse);
        }

        var selected = launcher.Selected;
        if (_memory.TryUseSession(selected.Pid, request.Tool, request.SecretName, commandClass) is not null)
            return new GateResult(ApprovalOutcome.AllowOnce, GateDecisions.SessionAllow, PolicyReasonCodes.SessionAllow);

        ApprovalAnswer answer;
        // One popup at a time. A request that waits here sees the decision of the popup before it.
        lock (_promptLock)
        {
            if (_memory.IsDenyCoolingDown(selected.Pid, request.Tool))
                return new GateResult(ApprovalOutcome.Deny, GateDecisions.Deny, PolicyReasonCodes.DenyCooldown);
            if (_memory.TryGetTransient(transientKey) is { } decided)
                return new GateResult(decided, DecisionFor(decided), PolicyReasonCodes.TransientReuse);
            if (_memory.TryUseSession(selected.Pid, request.Tool, request.SecretName, commandClass) is not null)
                return new GateResult(ApprovalOutcome.AllowOnce, GateDecisions.SessionAllow, PolicyReasonCodes.SessionAllow);

            answer = _approvalGate.Prompt(request);
            if (answer.Outcome == ApprovalOutcome.Deny)
                _memory.RememberDeny(selected.Pid, selected.PolicyKey, request.Tool);
        }

        var outcome = answer.Outcome;
        if (outcome is not (ApprovalOutcome.AllowOnce or ApprovalOutcome.AllowForSession))
        {
            _memory.RememberTransient(transientKey, outcome, selected.PolicyKey, request.Tool);
            return new GateResult(outcome, DecisionFor(outcome), answer.HelloReason);
        }

        // A session grant is honored only when the card would have offered one - same rule the
        // helper used to decide whether to show the button - otherwise the click covers this call
        // only. One shared check keeps the agent and the card from ever disagreeing.
        // Approve Once lasts the session too, but for this command class alone (#205).
        var grant = ApprovalPresentation.IsSessionAllowOffered(request.EnrollmentKind, request.CommandClass)
            ? _memory.Grant(selected.Pid, selected.CreateTimeUtc, selected.PolicyKey, selected.Kind,
                request.Tool, request.SecretName, commandClass,
                exactClass: outcome == ApprovalOutcome.AllowOnce)
            : null;
        _memory.RememberTransient(transientKey, ApprovalOutcome.AllowOnce, selected.PolicyKey, request.Tool);
        return new GateResult(
            ApprovalOutcome.AllowOnce,
            grant is null || outcome == ApprovalOutcome.AllowOnce
                ? GateDecisions.AllowOnce
                : GateDecisions.SessionGrant,
            answer.HelloReason);
    }

    private static string DecisionFor(ApprovalOutcome outcome) => outcome switch
    {
        ApprovalOutcome.AllowOnce => GateDecisions.AllowOnce,
        ApprovalOutcome.Deny => GateDecisions.Deny,
        _ => GateDecisions.Unavailable,
    };

    private static string ReuseSuffix(string? reason) => reason switch
    {
        null => "",
        PolicyReasonCodes.HelloCanceled => " (Windows Hello was cancelled)",
        _ => $" (reused decision: {reason})",
    };

    private LauncherResolution ResolveSafe(ServerCallContext context)
    {
        try
        {
            var http = context.GetHttpContext();
            ApplyPolicyChanges(_policy.Load());
            return LauncherIdentityResolver.PreferHarnessAncestor(_identity.Resolve(http), IsHarnessKey);
        }
        catch (Exception ex)
        {
            return new LauncherResolution
            {
                ClientPid = 0,
                ClientPidFromPipe = false,
                Selected = new ProcessNode
                {
                    Pid = 0,
                    ParentPid = 0,
                    Kind = LauncherKinds.Unknown,
                    PolicyKey = LauncherKinds.PolicyKeyUnknown,
                },
                Chain = Array.Empty<ProcessNode>(),
                AutoApproveEligible = false,
                Notes = "identity resolution failed: " + ex.Message,
            };
        }
    }

    /// <summary>
    /// Build a card-ready approval request (secret names only — never values).
    /// </summary>
    private ApprovalRequest BuildApprovalRequest(
        string tool,
        string className,
        string levelName,
        LauncherResolution launcher,
        string secretName,
        string? purpose,
        string enrollmentKind,
        string? policyNote,
        string? commandLine,
        string? toolPath,
        string? workingDirectory)
    {
        var selected = launcher.Selected;
        var (product, description, fileName) =
            ApprovalPresentation.TryReadImageVersionInfo(selected.Path);
        if (string.IsNullOrWhiteSpace(fileName) && !string.IsNullOrWhiteSpace(selected.FileName))
            fileName = selected.FileName;

        return new ApprovalRequest(
            Tool: tool,
            CommandClass: className,
            PolicyLevel: levelName,
            LauncherPolicyKey: selected.PolicyKey,
            LauncherKind: selected.Kind,
            LauncherPath: selected.Path,
            SecretName: secretName,
            Purpose: purpose,
            EnrollmentKind: enrollmentKind,
            PolicyNote: policyNote,
            CommandLine: commandLine,
            ToolPath: toolPath,
            WorkingDirectory: workingDirectory,
            LauncherProductName: product,
            LauncherFileDescription: description,
            LauncherFileName: fileName,
            LauncherPublisher: selected.Publisher,
            RequestedAt: DateTimeOffset.UtcNow,
            LauncherPid: selected.Pid,
            HelloRequired: WindowsHelloPolicy.Requires(_policy.HelloMode, CommandClassNames.ParseOrUnknown(className)));
    }

    private static string FormatCommandLine(string tool, IReadOnlyList<string> argv)
    {
        if (argv.Count == 0)
            return tool;
        return tool + " " + string.Join(' ', argv);
    }

    private sealed record GitHit(VaultEntry Entry, string Key, string? Refresh);

    private IReadOnlyList<string> GitExistingKeys()
    {
        var prefix = GitVaultNames.Prefix;
        return _vault.ListTargets(prefix)
            .Select(t => t.Target.Length > prefix.Length ? t.Target[prefix.Length..] : t.Target)
            .ToList();
    }

    private GitHit? ReadGit(string serverUrl, string username)
    {
        foreach (var key in GitVaultNames.LookupKeys(serverUrl, username, GitExistingKeys()))
        {
            var entry = _vault.ReadTarget(GitVaultNames.Target(key));
            if (entry is null)
                continue;
            var refreshTarget = GitVaultNames.Target(GitVaultNames.Parse(serverUrl, username).RefreshKey);
            var refreshEntry = _vault.ReadTarget(refreshTarget);
            var refresh = refreshEntry is null ? null : CredentialVault.Utf8(refreshEntry.Blob);
            return new GitHit(entry, key, refresh);
        }

        return null;
    }

    private bool GitEntryEquals(string serverUrl, string username, string secret, string expiry, string refresh)
    {
        var hit = ReadGit(serverUrl, username);
        if (hit is null)
            return false;
        try
        {
            if (CredentialVault.Utf8(hit.Entry.Blob) != secret)
                return false;
            if ((hit.Entry.Comment ?? "") != (expiry ?? ""))
                return false;
            return (hit.Refresh ?? "") == (refresh ?? "");
        }
        finally
        {
            Array.Clear(hit.Entry.Blob);
        }
    }

    private void SaveGit(string serverUrl, string username, string secret, string expiry, string refresh)
    {
        var ctx = GitVaultNames.Parse(serverUrl, username);
        _vault.SaveTarget(
            GitVaultNames.Target(ctx.AccountKey),
            username,
            CredentialVault.Utf8Bytes(secret),
            comment: expiry ?? "");
        var refreshTarget = GitVaultNames.Target(ctx.RefreshKey);
        if (string.IsNullOrEmpty(refresh))
            _vault.DeleteTarget(refreshTarget);
        else
            _vault.SaveTarget(refreshTarget, username, CredentialVault.Utf8Bytes(refresh), comment: "");
    }

    private void EraseGit(string serverUrl, string username)
    {
        var hit = ReadGit(serverUrl, username);
        if (hit is null)
            return;
        _vault.DeleteTarget(GitVaultNames.Target(hit.Key));
        _vault.DeleteTarget(GitVaultNames.Target(GitVaultNames.Parse(serverUrl, username).RefreshKey));
        Array.Clear(hit.Entry.Blob);
    }
}
