# Graph Report - CmdWarden  (2026-09-14)

## Corpus Check
- 214 files · ~120,756 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 5 file(s) not represented in the graph (top: (none) 1, .nuspec 1, .cmd 1)

## Summary
- 2723 nodes · 6538 edges · 143 communities (126 shown, 15 thin omitted)
- Extraction: 89% EXTRACTED · 11% INFERRED · 0% AMBIGUOUS · INFERRED: 687 edges (avg confidence: 0.83)
- Token cost: 388,349 input · 43,153 output

## Community Hubs (Navigation)
- Scan Detectors
- Vault UI View Models
- Machine PATH Editor
- Launcher Resolution
- Audit Log Formatting
- Policy Store
- Agent Health Client
- Vault Secret Form
- Docker Credential Helper
- Approval Memory
- Solution And Packages
- Test Project Files
- Vault Secret RPC
- Vault Window Controls
- Docker Strong Harden Tests
- Approval Gate Window
- Git Strong Harden Tests
- Git Strong Harden
- Agent Migrate Client
- Git Credential Protocol
- Git Vault Names
- Policy Evaluator
- Windows Credential Vault
- Vault Card Renderers
- Helper Credential Tests
- Agent Endpoints And App
- Native Process Methods
- Docker Strong Harden
- Git Helper Process Tests
- Gh Strong Harden Tests
- Git Command Classifier
- Tool Pin Store
- Approval Presentation
- Helper Chain Rule Tests
- Approval Gate Factory
- Session Allow Process Tests
- Agent Sessions Client
- Git Global Config
- Gh Command Classifier
- Gh Strong Store
- Approval Gate App
- Image Identity
- Gh Strong Store Tests
- Doctor Report
- Authorize Process Tests
- Agent Call And Az Tests
- Harden Options
- Tool Pin Document
- Domain Glossary
- CLI Harden Modules
- Project Structure
- Git Authorize Tests
- Agent Lifecycle
- Vault Names And Tests
- Session Agent Service
- Hardened Tools Screenshot
- Launcher Identity Resolver
- Policy Read Model
- Install Script
- Process Approval Gate
- Agent Authorize Client
- Az Command Classifier
- Gh Hosts File
- Inject Runner
- Secret Gates Screenshot
- Vault Secret List
- CLI Policy Commands
- CLI Agent Commands
- Docker Command Classifier
- Git Helper Credential Tests
- Az Authorize Tests
- Docker Authorize Tests
- Start Menu Shortcut
- Agent Locator
- Approval Memory Fixture
- Docker Harden Tests
- Detectors Screenshot
- Doctor Screenshot
- Gh Vault Names
- Git Shim
- Git Harden Tests
- Release And Packaging
- Az Shim
- Policy Concepts
- Product Specs
- Session Agent Concepts
- Agent Project Files
- Helper Chain Rule
- Product Paths And Discoverers
- Docker Strong Results
- Az Auth Fixture
- Git Auth Fixture
- Approval Gate Spec
- Az And Git Harden
- Process Node
- Gh Strong Harden
- Agent Vault Client
- Gh Shim
- Approval Dialog Screenshot
- Secret Usage Screenshot
- Git HTTP Test Server
- Gh Harden
- Vault Nav Buttons
- Vault Status Pills
- Docker Shim
- Approval Gate Tests
- Approval Gate Locator
- Docker Harden
- Policy Store Tests
- Secrets Manager Locator
- Gh Harden Tests
- Icon Render Script
- Pin Check Result
- Approval Request Types
- Docker Harden Result
- Gh Harden Options
- Gh Harden Result
- Git Harden Result
- Session Allow Display
- Agent Channel Factory
- Az Harden Result
- Docker Auth Fixture
- Dotnet Tool Package Tests
- Git Harden Options
- Command Class Enum
- Policy Level Enum
- Vault Page Scrollers
- Pill Kind Enum
- Secrets Manager Code Behind
- Inject Options
- Vault Empty Panels
- Vault Page Panels
- Gh Classifier Theories
- Scan Engine Concept
- Approval Gate Project
- Product Info
- Agent Helper Client
- Gate Decisions
- Policy Reason Codes
- Sidebar Column
- Agent Process Collection

## God Nodes (most connected - your core abstractions)
1. `CmdWarden.Contracts` - 147 edges
2. `Window` - 112 edges
3. `ToolPinStore` - 96 edges
4. `CmdWarden.Tests` - 68 edges
5. `CredentialVault` - 65 edges
6. `MainWindow` - 54 edges
7. `PolicyStore` - 52 edges
8. `ApprovalMemory` - 50 edges
9. `CliApp` - 48 edges
10. `SessionAgentService` - 40 edges

## Surprising Connections (you probably didn't know these)
- `winget locale manifest` --references--> `CmdWarden`  [EXTRACTED]
  packaging/winget/BasantPandey.CmdWarden.locale.en-US.yaml → CONTEXT.md
- `AI harness rules prompt` --references--> `AI Harness`  [EXTRACTED]
  docs/prompts/ai-harness-rules.md → CONTEXT.md
- `release workflow` --references--> `Shim`  [INFERRED]
  .github/workflows/release.yml → CONTEXT.md
- `release workflow` --references--> `Credential Helper`  [INFERRED]
  .github/workflows/release.yml → CONTEXT.md
- `CmdWarden product and architecture specification` --references--> `Spike Vertical`  [EXTRACTED]
  docs/spec/cmdwarden.md → CONTEXT.md

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Secret release gate flow: Shim -> Session Agent -> Launcher identity -> Policy -> Approval Gate -> Vault -> Audit** — context_shim, context_session_agent, context_launcher, context_policy, context_approval_gate, context_vault, docs_spec_cmdwarden_audit_log [EXTRACTED 1.00]
- **CmdWarden Vault six pages** — docs_spec_management_shell_tabs_doctor_tab, docs_spec_management_shell_tabs_hardened_tools_tab, docs_spec_management_shell_tabs_secret_usage_tab, docs_spec_management_shell_tabs_detectors_tab, docs_spec_management_shell_tabs_secret_gates_tab, docs_spec_vault_secrets_ui_vault_secrets_ui_handoff [EXTRACTED 1.00]
- **Release distribution chain: release workflow -> nupkg/zip -> installer script, winget, chocolatey** — _github_workflows_release_release_workflow, docs_install_install_cmdwarden_ps1, packaging_winget_basantpandey_cmdwarden_installer_installer_manifest, packaging_chocolatey_readme_chocolatey_package, docs_install_dotnet_tool_install [INFERRED 0.85]
- **Approval Gate Decision Flow** — docs_images_approval_gate_launcher_identity, docs_images_approval_gate_command_context, docs_images_approval_gate_secret_request, docs_images_approval_gate_deny_button, docs_images_approval_gate_allow_for_session_button, docs_images_approval_gate_approve_once_button [INFERRED 0.85]
- **Detector finding to harden remediation flow** — docs_images_vault_detectors_run_scan, docs_images_vault_detectors_found_not_hardened, docs_images_vault_detectors_finding_card, docs_images_vault_detectors_cw_harden, docs_images_vault_detectors_path_shim [INFERRED 0.85]
- **Doctor Page Health Checks** — docs_images_vault_doctor_session_agent_check, docs_images_vault_doctor_vault_ui_binary_check, docs_images_vault_doctor_start_menu_shortcut_check, docs_images_vault_doctor_details_panel [EXTRACTED 1.00]
- **Vault UI Sidebar Sections** — docs_images_vault_doctor_sidebar_navigation, docs_images_vault_doctor_doctor_page, docs_images_vault_doctor_vault_ui [EXTRACTED 1.00]
- **Hardened Tools Card List (gh, git, az, docker)** — docs_images_vault_hardened_tools_hardened_tools_page, docs_images_vault_hardened_tools_github_cli, docs_images_vault_hardened_tools_git, docs_images_vault_hardened_tools_azure_cli, docs_images_vault_hardened_tools_docker_cli [EXTRACTED 1.00]
- **CmdWarden Vault Sidebar Pages** — docs_images_vault_hardened_tools_sidebar_navigation, docs_images_vault_hardened_tools_secret_gates_page, docs_images_vault_hardened_tools_detectors_page, docs_images_vault_hardened_tools_hardened_tools_page, docs_images_vault_hardened_tools_secrets_page, docs_images_vault_hardened_tools_secret_usage_page, docs_images_vault_hardened_tools_doctor_page [EXTRACTED 1.00]
- **Policy Resolution: kind default, per-tool override, session allow, Approval Gate** — docs_images_vault_secret_gates_defaults, docs_images_vault_secret_gates_per_tool_override, docs_images_vault_secret_gates_active_session_allows, docs_images_vault_secret_gates_approval_gate [INFERRED 0.85]
- **Vault Secret Gates UI Layout** — docs_images_vault_secret_gates_cmdwarden_vault, docs_images_vault_secret_gates_sidebar_navigation, docs_images_vault_secret_gates_secret_gates_page [EXTRACTED 1.00]
- **Secret Usage Audit Record** — docs_images_vault_secret_usage_audit_trail_entry, docs_images_vault_secret_usage_launcher_identity_hash, docs_images_vault_secret_usage_secret_operation, docs_images_vault_secret_usage_access_decision [EXTRACTED 1.00]

## Communities (143 total, 15 thin omitted)

### Community 0 - "Scan Detectors"
Cohesion: 0.06
Nodes (46): CmdWarden.Contracts.Scan, ScanFormatter, IReadOnlyList, AzAmbientSpSecretDetector, Id, AzNotHardenedDetector, Id, DockerAmbientAuthConfigDetector (+38 more)

### Community 1 - "Vault UI View Models"
Cohesion: 0.04
Nodes (74): CanDelete, Evidence, FullPath, Hint, HintVisibility, Id, IsSelected, Key (+66 more)

### Community 2 - "Machine PATH Editor"
Cohesion: 0.08
Nodes (18): RegistryKey, MachinePathEditor, DllImport, IntPtr, HardenedToolStatus, HardenState, Degraded, Hardened (+10 more)

### Community 3 - "Launcher Resolution"
Cohesion: 0.07
Nodes (28): EnvName, GateResult, GitHit, SessionAgentBase, LauncherResolution, AutoApproveEligible, Chain, ClientPid (+20 more)

### Community 4 - "Audit Log Formatting"
Cohesion: 0.06
Nodes (33): JsonElement, Regex, AgentHost, AgentRuntimeInfo, AuditFormatter, AuditGateRecord, ClientPid, Decision (+25 more)

### Community 5 - "Policy Store"
Cohesion: 0.09
Nodes (31): Fixture, PolicyChange, PolicyLevel, LauncherEnrollmentKind, AiHarness, Terminal, Unknown, LauncherEntryDto (+23 more)

### Community 6 - "Agent Health Client"
Cohesion: 0.05
Nodes (34): IAsyncDisposable, AgentHealthClient, CancellationToken, HealthResponse, Task, TimeSpan, AgentProcess, AuthorizeFixture (+26 more)

### Community 7 - "Vault Secret Form"
Cohesion: 0.06
Nodes (32): Result, CryptographicOperations, Result, VaultSecretFormValidation, IEnumerable, NameBox, NameError, SaveButton (+24 more)

### Community 8 - "Docker Credential Helper"
Cohesion: 0.07
Nodes (19): Credentials, Out, Credentials, JsonSerializerOptions, Task, TextReader, TextWriter, DockerHelperApp (+11 more)

### Community 9 - "Approval Memory"
Cohesion: 0.12
Nodes (15): RunRecord, ApprovalMemory, SessionIdle, RunRecord, SessionGrant, TransientEntry, ConcurrentDictionary, DateTime (+7 more)

### Community 10 - "Solution And Packages"
Cohesion: 0.06
Nodes (34): coverlet.collector (6.0.4), Google.Protobuf (3.31.1), Grpc.AspNetCore (2.71.0), Grpc.Net.Client (2.71.0), Grpc.Tools (2.72.0), Microsoft.Extensions.Logging.Abstractions (10.0.0), Microsoft.NET.Test.Sdk (17.14.1), Microsoft.Win32.SystemEvents (10.0.0) (+26 more)

### Community 12 - "Vault Secret RPC"
Cohesion: 0.14
Nodes (21): DeleteSecretRequest, FileSystemAccessRule, ListSecretNamesRequest, ReleaseSecretRequest, SaveSecretRequest, TimeSpan, CancellationToken, IReadOnlyList (+13 more)

### Community 13 - "Vault Window Controls"
Cohesion: 0.10
Nodes (19): Border, Button, CancelEventArgs, DispatcherTimer, FrameworkElement, KeyEventArgs, ObservableCollection, Primary (+11 more)

### Community 14 - "Docker Strong Harden Tests"
Cohesion: 0.13
Nodes (18): Secret, DockerStrongOptions, ConfigPath, ProductRoot, UrlPrefix, DockerStrongHardenTests, Sandbox, ConfigPath (+10 more)

### Community 15 - "Approval Gate Window"
Cohesion: 0.11
Nodes (30): ApproveButton, BrandTitle, CommandLine, DenyButton, DetailCommandClass, DetailEnrollment, DetailIdentity, DetailLauncherPath (+22 more)

### Community 16 - "Git Strong Harden Tests"
Cohesion: 0.09
Nodes (19): IEnumerable, KeyValuePair, GitStrongHardenTests, Sandbox, Config, GlobalConfig, HelperExe, HostSuffix (+11 more)

### Community 17 - "Git Strong Harden"
Cohesion: 0.09
Nodes (24): Config, GhBlocks, Helpers, GitStrongHarden, GitStrongResult, HelperValue, MigratedKeys, Namespace (+16 more)

### Community 18 - "Agent Migrate Client"
Cohesion: 0.07
Nodes (22): MigrateToolStoreRequest, AgentMigrateClient, CancellationToken, IEnumerable, MigrateToolStoreResponse, Task, TimeSpan, Fixture (+14 more)

### Community 19 - "Git Credential Protocol"
Cohesion: 0.11
Nodes (14): GitCredentialAttrs, ServerUrl, GitCredentialProtocol, IReadOnlyList, TextReader, TextWriter, RpcException, Task (+6 more)

### Community 20 - "Git Vault Names"
Cohesion: 0.12
Nodes (14): Context, Ctx, Context, AccountKey, HostKey, RefreshKey, GitVaultNames, Prefix (+6 more)

### Community 21 - "Policy Evaluator"
Cohesion: 0.10
Nodes (14): CommandClass, CommandClassNames, PolicyDecision, AutoAllow, NeedsApproval, PolicyEvaluator, InlineData, Theory (+6 more)

### Community 22 - "Windows Credential Vault"
Cohesion: 0.15
Nodes (12): CREDENTIAL, CREDENTIAL_ATTRIBUTE, FILETIME, CREDENTIAL, CREDENTIAL_ATTRIBUTE, CredentialVault, VaultTarget, DllImport (+4 more)

### Community 23 - "Vault Card Renderers"
Cohesion: 0.11
Nodes (18): Bg, Brush, Fg, FindingCard, LauncherCard, LevelRow, PillKind, FindingCard (+10 more)

### Community 24 - "Helper Credential Tests"
Cohesion: 0.19
Nodes (12): HelperCredentialRequest, CancellationToken, HelperCredentialResponse, Task, TimeSpan, HelperCredentialProcessTests, Fact, InlineData (+4 more)

### Community 25 - "Agent Endpoints And App"
Cohesion: 0.11
Nodes (17): MarshalAs, Mutex, AgentEndpoints, GrpcChannelAddress, PipeName, Application, bd, App (+9 more)

### Community 26 - "Native Process Methods"
Cohesion: 0.15
Nodes (13): SafePipeHandle, NativeMethods, PROCESSENTRY32, DllImport, IntPtr, PROCESSENTRY32, ProcessChainWalker, Dictionary (+5 more)

### Community 27 - "Docker Strong Harden"
Cohesion: 0.12
Nodes (12): Entries, InlineAuths, Legacy, DockerStrongHarden, Exception, JsonObject, List, LabeledEntry (+4 more)

### Community 28 - "Git Helper Process Tests"
Cohesion: 0.22
Nodes (8): ProcessStartInfo, Stderr, GitCredentialHelperProcessTests, Exit, Fact, RpcException, Stdout, Task

### Community 29 - "Gh Strong Harden Tests"
Cohesion: 0.11
Nodes (16): GhStrongHardenTests, Sandbox, ConfigDir, FakeGh, Host1, Host2, HostsPath, Root (+8 more)

### Community 30 - "Git Command Classifier"
Cohesion: 0.20
Nodes (7): GitCommandClassifier, HashSet, IReadOnlyList, List, GitCommandClassifierTests, InlineData, Theory

### Community 31 - "Tool Pin Store"
Cohesion: 0.19
Nodes (8): ToolPinStore, PinsDirectory, ConcurrentDictionary, JsonSerializerOptions, Task, ToolPinStoreTests, Fact, ToolPinDocument

### Community 32 - "Approval Presentation"
Cohesion: 0.15
Nodes (8): FileDescription, ProductName, ApprovalPresentation, FileName, ApprovalPresentationTests, Fact, InlineData, Theory

### Community 34 - "Approval Gate Factory"
Cohesion: 0.13
Nodes (15): ApprovalGateFactory, IApprovalGate, NativeApprovalGate, DllImport, IntPtr, TimeSpan, ScriptedApprovalGate, UnavailableApprovalGate (+7 more)

### Community 35 - "Session Allow Process Tests"
Cohesion: 0.25
Nodes (7): SessionAllowProcessTests, Fact, Task, TransientReuseProcessTests, Fact, RpcException, Task

### Community 36 - "Agent Sessions Client"
Cohesion: 0.16
Nodes (14): ExitCode, ListSessionAllowsRequest, Output, RevokeSessionAllowRequest, SessionAllowRow, AgentSessionsClient, CancellationToken, IReadOnlyList (+6 more)

### Community 37 - "Git Global Config"
Cohesion: 0.22
Nodes (5): GitGlobalConfig, Exit, IReadOnlyList, List, Stdout

### Community 38 - "Gh Command Classifier"
Cohesion: 0.20
Nodes (7): GhCommandClassifier, HashSet, IReadOnlyList, List, GhCommandClassifierTests, InlineData, Theory

### Community 39 - "Gh Strong Store"
Cohesion: 0.21
Nodes (8): GhMigrateResult, GhStrongStore, HostsPath, StockPrefix, VaultPrefix, IReadOnlyList, List, ReadOnlySpan

### Community 40 - "Approval Gate App"
Cohesion: 0.12
Nodes (13): Application, Application, bd, App, UserChoseOutcome, StartupEventArgs, Border, ApprovalHelperExitCodes (+5 more)

### Community 41 - "Image Identity"
Cohesion: 0.14
Nodes (12): FileInfo, Identity, Identity, ImageIdentity, ConcurrentDictionary, DateTime, LauncherKinds, PolicyKeyUnknown (+4 more)

### Community 42 - "Gh Strong Store Tests"
Cohesion: 0.15
Nodes (11): GhStrongStoreTests, Sandbox, FakeGh, HostsPath, Id, Root, Store, Vault (+3 more)

### Community 43 - "Doctor Report"
Cohesion: 0.14
Nodes (13): AgentHandle, DoctorReport, VersionMismatch, CancellationToken, HealthResponse, Task, TimeSpan, AgentHandle (+5 more)

### Community 44 - "Authorize Process Tests"
Cohesion: 0.31
Nodes (6): AuthorizeFixture, CallerIdentityResponse, AuthorizeProcessTests, Fact, RpcException, Task

### Community 45 - "Agent Call And Az Tests"
Cohesion: 0.14
Nodes (11): IDisposable, AgentCall, Client, Token, CancellationToken, CancellationTokenSource, GrpcChannel, AzHardenTests (+3 more)

### Community 46 - "Harden Options"
Cohesion: 0.12
Nodes (14): AzHardenOptions, ProductRoot, RealAzPath, ShimSourceDir, SkipUserPath, DockerHardenOptions, ProductRoot, RealDockerPath (+6 more)

### Community 47 - "Tool Pin Document"
Cohesion: 0.11
Nodes (18): GhHostState, ActiveUser, Host, Users, StrongState, GhHelperBlocks, Hosts, PreviousHelpers (+10 more)

### Community 48 - "Domain Glossary"
Cohesion: 0.14
Nodes (19): Compat Mode, Credential Helper, First Catalog (gh, git, az, docker), Harden, Shim, Spike Vertical, Strong Mode, Deny reason codes (UserDenied, NotEnrolled, UnknownLauncher, ApprovalUnavailable, PinMissing, PinMismatch) (+11 more)

### Community 49 - "CLI Harden Modules"
Cohesion: 0.11
Nodes (3): CmdWarden.Cli.Scan, CmdWarden.Cli.Harden, UserPathEditor

### Community 50 - "Project Structure"
Cohesion: 0.16
Nodes (3): CmdWarden.Cli, CmdWarden.Agent.Approval, SessionAgentServiceNames

### Community 51 - "Git Authorize Tests"
Cohesion: 0.36
Nodes (5): GitAuthFixture, GitAuthorizeProcessTests, Fact, RpcException, Task

### Community 52 - "Agent Lifecycle"
Cohesion: 0.26
Nodes (9): HealthRequest, AgentLifecycle, StatusResult, CancellationToken, Exception, Task, TimeSpan, StatusResult (+1 more)

### Community 53 - "Vault Names And Tests"
Cohesion: 0.18
Nodes (7): KeyNotFoundException, VaultNames, CredentialVaultTests, Fact, VaultNamesTests, ArgumentException, Fact

### Community 54 - "Session Agent Service"
Cohesion: 0.16
Nodes (10): CallerIdentityRequest, DeleteSecretResponse, ListSecretNamesResponse, ListSessionAllowsResponse, RevokeSessionAllowResponse, ServerCallContext, CallerIdentityResponse, HealthResponse (+2 more)

### Community 55 - "Hardened Tools Screenshot"
Cohesion: 0.14
Nodes (18): Vault Hardened Tools Screenshot, Azure CLI (az), CmdWarden Vault Desktop App, cw harden Command, Dark Theme Card Layout Rationale, Detectors Page, Docker CLI (docker), Doctor Page (+10 more)

### Community 56 - "Launcher Identity Resolver"
Cohesion: 0.17
Nodes (10): FromPipe, HttpContext, IConnectionNamedPipeFeature, IReadOnlySet, LauncherIdentityResolver, Func, IReadOnlyList, Notes (+2 more)

### Community 57 - "Policy Read Model"
Cohesion: 0.23
Nodes (8): JsonException, PolicyLauncherEntry, PolicyReadModel, PolicyToolLevel, IReadOnlyList, PolicyReadModelTests, PolicyPath, Fact

### Community 58 - "Install Script"
Cohesion: 0.27
Nodes (15): Assert-DotNet(), Clear-CmdWardenToolStore(), Download-ReleaseAsset(), Ensure-DotNetToolsOnPath(), Get-DotNetToolsPath(), Get-LatestReleaseTag(), Install-AsDotNetTool(), Install-FromZip() (+7 more)

### Community 59 - "Process Approval Gate"
Cohesion: 0.21
Nodes (9): ProcessApprovalGate, Func, Process, TimeSpan, ProcessApprovalGateTests, Fact, InlineData, Process (+1 more)

### Community 60 - "Agent Authorize Client"
Cohesion: 0.17
Nodes (12): AgentAuthorizeClient, AuthorizeResponse, CancellationToken, Exception, IEnumerable, IReadOnlyDictionary, Task, TimeSpan (+4 more)

### Community 61 - "Az Command Classifier"
Cohesion: 0.24
Nodes (7): AzCommandClassifier, HashSet, IReadOnlyList, List, AzCommandClassifierTests, InlineData, Theory

### Community 62 - "Gh Hosts File"
Cohesion: 0.17
Nodes (9): GhHostEntry, ActiveUser, Tokens, Users, GhHostsFile, Dictionary, IEnumerable, IReadOnlyList (+1 more)

### Community 63 - "Inject Runner"
Cohesion: 0.13
Nodes (11): Arguments, SecretNames, InjectRunner, FileName, IReadOnlyDictionary, IReadOnlyList, List, ReadOnlySpan (+3 more)

### Community 64 - "Secret Gates Screenshot"
Cohesion: 0.17
Nodes (17): Vault Secret Gates Screenshot, Active Session Allows, AI Harness Launcher Kind, AI Harness Gets Lower Default Than Terminal, Approval Gate, Block When No UI Available, CmdWarden Vault Desktop App, cw policy sessions --revoke Command (+9 more)

### Community 65 - "Vault Secret List"
Cohesion: 0.16
Nodes (10): INotifyPropertyChanged, MouseButtonEventArgs, VaultSecretListItem, CanDelete, IsSelected, Name, VaultSecretListSelection, IEnumerable (+2 more)

### Community 67 - "CLI Agent Commands"
Cohesion: 0.22
Nodes (3): Exception, HealthResponse, Task

### Community 68 - "Docker Command Classifier"
Cohesion: 0.24
Nodes (7): DockerCommandClassifier, HashSet, IReadOnlyList, List, DockerCommandClassifierTests, InlineData, Theory

### Community 69 - "Git Helper Credential Tests"
Cohesion: 0.49
Nodes (4): GitHelperCredentialProcessTests, Fact, RpcException, Task

### Community 70 - "Az Authorize Tests"
Cohesion: 0.38
Nodes (5): AzAuthFixture, AzAuthorizeProcessTests, Fact, RpcException, Task

### Community 71 - "Docker Authorize Tests"
Cohesion: 0.38
Nodes (5): DockerAuthFixture, DockerAuthorizeProcessTests, Fact, RpcException, Task

### Community 72 - "Start Menu Shortcut"
Cohesion: 0.20
Nodes (3): SecretsManagerStartMenu, DesktopShortcutPath, ShortcutPath

### Community 73 - "Agent Locator"
Cohesion: 0.29
Nodes (4): AgentLocator, AgentLifecycleTests, Fact, Task

### Community 74 - "Approval Memory Fixture"
Cohesion: 0.18
Nodes (10): ApprovalMemoryFixture, PipeName, PolicyPath, ProductRoot, SelectedPolicyKey, IReadOnlyDictionary, IReadOnlyList, Process (+2 more)

### Community 75 - "Docker Harden Tests"
Cohesion: 0.24
Nodes (6): AgentProcess, DockerHardenTests, Fact, Process, Task, ValueTask

### Community 76 - "Detectors Screenshot"
Cohesion: 0.18
Nodes (14): CmdWarden Vault Detectors Screenshot, az CLI (Azure), cw harden Command (pin real binary, install PATH shim), Detectors Page, Detector Finding Card (title, tool id, severity badge, detail, evidence, remediation, cw command), Found But Not Hardened Detector, gh CLI (GitHub), git CLI (+6 more)

### Community 77 - "Doctor Screenshot"
Cohesion: 0.20
Nodes (14): Vault Doctor Screenshot, Doctor Details Panel, Doctor Page, Installed as .NET Global Tool, Per-User Named Pipe CmdWarden-Basant, CmdWarden.SecretsManager.exe, Self-Diagnosis of Install Health, CmdWarden Session Agent (+6 more)

### Community 78 - "Gh Vault Names"
Cohesion: 0.23
Nodes (6): Host, User, GhVaultNames, Prefix, User, StockTarget

### Community 79 - "Git Shim"
Cohesion: 0.21
Nodes (8): Exception, IEnumerable, IReadOnlyDictionary, IReadOnlyList, KeyValuePair, Task, TimeSpan, GitShimApp

### Community 80 - "Git Harden Tests"
Cohesion: 0.23
Nodes (6): AgentProcess, GitHardenTests, Fact, Process, Task, ValueTask

### Community 81 - "Release And Packaging"
Cohesion: 0.22
Nodes (13): build workflow, release workflow, Required binaries check after publish, cw shortcut install / remove / status, dotnet tool install vehicle, Install-CmdWarden.ps1 installer script, Chocolatey package cmdwarden, Package manager templates (Chocolatey, winget, Scoop) (+5 more)

### Community 82 - "Az Shim"
Cohesion: 0.21
Nodes (8): AuthorizeRequest, SessionAgentClient, AzShimApp, Exception, IReadOnlyDictionary, IReadOnlyList, Task, TimeSpan

### Community 83 - "Policy Concepts"
Cohesion: 0.21
Nodes (13): AI Harness, Command Class (read / write / secret-reveal / unknown), Launcher, Policy, Policy Level (Deny / Read / Trusted / Full), Policy profiles (Recommended / Cautious / Open), Wildcard tool row "*", Launcher display name priority (ProductName > FileDescription > file name > Unknown app) (+5 more)

### Community 84 - "Product Specs"
Cohesion: 0.33
Nodes (13): CmdWarden, Inspired Twin (of Automic Vault), Install guide, Policy quick start, AI harness rules prompt, CmdWarden product and architecture specification, Contracts seam (all probing, parsing, scanning in CmdWarden.Contracts), Management shell tabs implement plan (+5 more)

### Community 85 - "Session Agent Concepts"
Cohesion: 0.19
Nodes (13): Session Agent, Vault, cw doctor, cw inject, gRPC over named pipe IPC (CurrentUserOnly ACL), Doctor tab, Agent-down banner (actions disabled), ListSecretNames RPC (CredEnumerateW, names only) (+5 more)

### Community 87 - "Helper Chain Rule"
Cohesion: 0.22
Nodes (7): HelperChainRule, HashSet, IReadOnlyList, ToolPin, IsStrong, InlineData, Theory

### Community 88 - "Product Paths And Discoverers"
Cohesion: 0.23
Nodes (3): AzDiscoverer, GitDiscoverer, ProductPaths

### Community 89 - "Docker Strong Results"
Cohesion: 0.17
Nodes (12): DockerStrongResult, ConfigPath, MigratedUrls, PreviousCredsStore, Warnings, DockerUnhardenResult, HelperRemoved, PinRemoved (+4 more)

### Community 90 - "Az Auth Fixture"
Cohesion: 0.15
Nodes (9): AgentProcess, AzAuthFixture, PipeName, PolicyPath, ProductRoot, SelectedPolicyKey, AgentProcess, Process (+1 more)

### Community 91 - "Git Auth Fixture"
Cohesion: 0.15
Nodes (9): AgentProcess, GitAuthFixture, PipeName, PolicyPath, ProductRoot, SelectedPolicyKey, AgentProcess, Process (+1 more)

### Community 92 - "Approval Gate Spec"
Cohesion: 0.23
Nodes (12): Approval Gate, Allow for session, Approval Gate UI implement handoff, ApprovalOutcome (AllowOnce / AllowForSession / Deny / Unavailable), ApprovalRequest payload (secret names only), CW_APPROVAL_MODE env switch, Details expander (collapsed, forensic-lite), Lintel product mark and native title bar (+4 more)

### Community 93 - "Az And Git Harden"
Cohesion: 0.27
Nodes (3): DirectoryInfo, AzHarden, GitHarden

### Community 94 - "Process Node"
Cohesion: 0.17
Nodes (12): ProcessNode, CreateTimeUtc, FileName, Kind, ParentPid, Path, PidReuseSuspected, PolicyKey (+4 more)

### Community 95 - "Gh Strong Harden"
Cohesion: 0.21
Nodes (9): GhStrongHarden, GhStrongOptions, Hostname, ProductRoot, Store, TokenOverride, GhStrongResult, GhUnhardenResult (+1 more)

### Community 96 - "Agent Vault Client"
Cohesion: 0.26
Nodes (3): Exception, AgentVaultClient, Exception

### Community 97 - "Gh Shim"
Cohesion: 0.27
Nodes (6): Exception, IReadOnlyDictionary, IReadOnlyList, Task, TimeSpan, GhShimApp

### Community 98 - "Approval Dialog Screenshot"
Cohesion: 0.22
Nodes (11): Approval Gate Dialog Screenshot, Allow for Session Button, CmdWarden Approval Dialog, Approve Once Button, Command Context Panel (command, CWD, KEYS), Deny Button, Inject Mode (secret injected as env var), Launcher Identity (Claude Code, pid) (+3 more)

### Community 99 - "Secret Usage Screenshot"
Cohesion: 0.24
Nodes (11): Vault Secret Usage Screenshot, Access Decision Badge, Audit Trail Entry, CmdWarden Vault Desktop App, cw audit Command, Launcher Identity SHA1 Hash, Vault Online Status Indicator, Secret Operation Kind (+3 more)

### Community 100 - "Git HTTP Test Server"
Cohesion: 0.22
Nodes (6): HttpListener, GitHttpBasicServer, AuthenticatedGets, Host, RepoUrl, CancellationTokenSource

### Community 101 - "Gh Harden"
Cohesion: 0.29
Nodes (4): GhHarden, CancellationToken, Task, GhDiscoverer

### Community 102 - "Vault Nav Buttons"
Cohesion: 0.29
Nodes (9): AddSecretButton, EmptyAddButton, NavDetectors, NavDoctor, NavGates, NavSecrets, NavTools, NavUsage (+1 more)

### Community 103 - "Vault Status Pills"
Cohesion: 0.18
Nodes (11): AgentBadge, AgentDownBanner, DoctorAgentPill, DoctorShortcutPill, DoctorVaultPill, DoctorVersionPill, GatesDefaultsCard, GatesErrorBanner (+3 more)

### Community 104 - "Docker Shim"
Cohesion: 0.25
Nodes (6): Exception, IReadOnlyDictionary, IReadOnlyList, Task, TimeSpan, DockerShimApp

### Community 105 - "Approval Gate Tests"
Cohesion: 0.33
Nodes (4): ApprovalGateTests, Fact, InlineData, Theory

### Community 106 - "Approval Gate Locator"
Cohesion: 0.31
Nodes (3): ApprovalGateLocator, ApprovalGateLocatorTests, Fact

### Community 109 - "Secrets Manager Locator"
Cohesion: 0.36
Nodes (3): SecretsManagerLocator, SecretsManagerLocatorTests, Fact

### Community 110 - "Gh Harden Tests"
Cohesion: 0.24
Nodes (6): AgentProcess, GhHardenTests, Fact, Process, Task, ValueTask

### Community 111 - "Icon Render Script"
Cohesion: 0.36
Nodes (7): Image, Path, lerp(), main(), Render the accepted Lintel product mark to a multi-size .ico. Geometry is the…, render(), write_ico()

### Community 112 - "Pin Check Result"
Cohesion: 0.31
Nodes (5): PinCheckResult, Error, IsMissing, IsOk, Pin

### Community 113 - "Approval Request Types"
Cohesion: 0.25
Nodes (6): ApprovalPromptText, Caption, ApprovalRequest, SecretNames, DateTimeOffset, IReadOnlyList

### Community 114 - "Docker Harden Result"
Cohesion: 0.25
Nodes (7): DockerHardenResult, CredentialNote, PinSha256, RealDockerPath, ShimExePath, ShimsDir, UserPathUpdated

### Community 115 - "Gh Harden Options"
Cohesion: 0.25
Nodes (8): GhHardenOptions, PipeName, ProductRoot, RealGhPath, ShimSourceDir, SkipTokenImport, SkipUserPath, TokenOverride

### Community 116 - "Gh Harden Result"
Cohesion: 0.25
Nodes (8): GhHardenResult, PinSha256, RealGhPath, ShimExePath, ShimsDir, TokenImported, TokenImportNote, UserPathUpdated

### Community 117 - "Git Harden Result"
Cohesion: 0.25
Nodes (8): GitHardenResult, CredentialNote, HelperExePath, PinSha256, RealGitPath, ShimExePath, ShimsDir, UserPathUpdated

### Community 118 - "Session Allow Display"
Cohesion: 0.32
Nodes (3): SessionAllowDisplay, SessionAllowDisplayTests, Fact

### Community 119 - "Agent Channel Factory"
Cohesion: 0.33
Nodes (4): NamedPipeClientStream, AgentChannelFactory, GrpcChannel, TimeSpan

### Community 120 - "Az Harden Result"
Cohesion: 0.29
Nodes (7): AzHardenResult, CredentialNote, PinSha256, RealAzPath, ShimExePath, ShimsDir, UserPathUpdated

### Community 121 - "Docker Auth Fixture"
Cohesion: 0.29
Nodes (6): DockerAuthFixture, PipeName, PolicyPath, ProductRoot, SelectedPolicyKey, AgentProcess

### Community 122 - "Dotnet Tool Package Tests"
Cohesion: 0.38
Nodes (3): DotnetToolPackageTests, Fact, Task

### Community 123 - "Git Harden Options"
Cohesion: 0.33
Nodes (6): GitHardenOptions, HelperSourceDir, ProductRoot, RealGitPath, ShimSourceDir, SkipUserPath

### Community 124 - "Command Class Enum"
Cohesion: 0.33
Nodes (5): CommandClass, Read, SecretReveal, Unknown, Write

### Community 125 - "Policy Level Enum"
Cohesion: 0.33
Nodes (5): PolicyLevel, Deny, Full, Read, Trusted

### Community 126 - "Vault Page Scrollers"
Cohesion: 0.33
Nodes (6): ListScrollViewer, PageDetectors, PageDoctor, PageTools, UsageScroll, ScrollViewer

### Community 127 - "Pill Kind Enum"
Cohesion: 0.33
Nodes (6): PillKind, Danger, Info, Muted, Ok, Warn

### Community 129 - "Inject Options"
Cohesion: 0.40
Nodes (4): InjectOptions, Options, Remainder, InjectOptions

### Community 130 - "Vault Empty Panels"
Cohesion: 0.40
Nodes (5): BrandPanel, EmptyStatePanel, GatesEmpty, UsageEmpty, StackPanel

### Community 131 - "Vault Page Panels"
Cohesion: 0.50
Nodes (4): PageGates, PageSecrets, PageUsage, DockPanel

### Community 133 - "Scan Engine Concept"
Cohesion: 0.67
Nodes (3): Scan, Scan engine (coded C# detectors), Detectors tab

## Ambiguous Edges - Review These
- `Hardened Tools Page` → `Secret Gates Page`  [AMBIGUOUS]
  docs/images/vault-hardened-tools.png · relation: conceptually_related_to

## Knowledge Gaps
- **392 isolated node(s):** `SessionIdle`, `net10.0`, `Grpc.AspNetCore (2.71.0)`, `Microsoft.Win32.SystemEvents (10.0.0)`, `Microsoft.NET.Sdk.Web` (+387 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 697 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **15 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **What is the exact relationship between `Hardened Tools Page` and `Secret Gates Page`?**
  _Edge tagged AMBIGUOUS (relation: conceptually_related_to) - confidence is low._
- **Why does `CmdWarden.Contracts` connect `Test Project Files` to `Secrets Manager Code Behind`, `Machine PATH Editor`, `Launcher Resolution`, `Audit Log Formatting`, `Policy Store`, `Approval Gate Project`, `Agent Health Client`, `Product Info`, `Approval Memory`, `Agent Helper Client`, `Gate Decisions`, `Policy Reason Codes`, `Vault Secret Form`, `Docker Credential Helper`, `Agent Migrate Client`, `Git Credential Protocol`, `Git Vault Names`, `Policy Evaluator`, `Windows Credential Vault`, `Agent Endpoints And App`, `Docker Strong Harden`, `Git Command Classifier`, `Approval Gate Factory`, `Agent Sessions Client`, `Git Global Config`, `Gh Command Classifier`, `Gh Strong Store`, `Approval Gate App`, `Image Identity`, `Doctor Report`, `Agent Call And Az Tests`, `Tool Pin Document`, `CLI Harden Modules`, `Project Structure`, `Agent Lifecycle`, `Vault Names And Tests`, `Launcher Identity Resolver`, `Policy Read Model`, `Az Command Classifier`, `Gh Hosts File`, `Vault Secret List`, `Docker Command Classifier`, `Start Menu Shortcut`, `Agent Locator`, `Gh Vault Names`, `Git Shim`, `Az Shim`, `Agent Project Files`, `Product Paths And Discoverers`, `Docker Strong Results`, `Gh Strong Harden`, `Agent Vault Client`, `Gh Shim`, `Gh Harden`, `Docker Shim`, `Approval Gate Locator`, `Secrets Manager Locator`, `Approval Request Types`, `Docker Harden Result`, `Session Allow Display`, `Agent Channel Factory`, `Dotnet Tool Package Tests`, `Command Class Enum`, `Policy Level Enum`?**
  _High betweenness centrality (0.241) - this node is a cross-community bridge._
- **Why does `ToolPinStore` connect `Tool Pin Store` to `Scan Detectors`, `Machine PATH Editor`, `Launcher Resolution`, `Audit Log Formatting`, `Policy Store`, `Agent Health Client`, `Docker Credential Helper`, `Vault Secret RPC`, `Docker Strong Harden Tests`, `Git Strong Harden Tests`, `Git Strong Harden`, `Agent Migrate Client`, `Helper Credential Tests`, `Docker Strong Harden`, `Git Helper Process Tests`, `Gh Strong Harden Tests`, `Session Allow Process Tests`, `Git Global Config`, `Authorize Process Tests`, `Agent Call And Az Tests`, `Harden Options`, `Tool Pin Document`, `Git Authorize Tests`, `Launcher Identity Resolver`, `Agent Authorize Client`, `Git Helper Credential Tests`, `Az Authorize Tests`, `Docker Authorize Tests`, `Docker Harden Tests`, `Git Harden Tests`, `Az Auth Fixture`, `Git Auth Fixture`, `Az And Git Harden`, `Gh Strong Harden`, `Gh Harden`, `Docker Harden`, `Gh Harden Tests`, `Pin Check Result`, `Docker Auth Fixture`?**
  _High betweenness centrality (0.116) - this node is a cross-community bridge._
- **Why does `MainWindow` connect `Vault Window Controls` to `Secrets Manager Code Behind`, `Vault UI View Models`, `Vault Secret List`, `Scan Detectors`, `Vault Nav Buttons`, `Vault Secret Form`, `Vault Card Renderers`, `Agent Endpoints And App`, `Pill Kind Enum`?**
  _High betweenness centrality (0.093) - this node is a cross-community bridge._
- **Are the 78 inferred relationships involving `ToolPinStore` (e.g. with `.Build()` and `.Run()`) actually correct?**
  _`ToolPinStore` has 78 INFERRED edges - model-reasoned connections that need verification._
- **Are the 28 inferred relationships involving `CredentialVault` (e.g. with `.ProbeDockerStrong()` and `.ProbeGhStrong()`) actually correct?**
  _`CredentialVault` has 28 INFERRED edges - model-reasoned connections that need verification._
- **What connects `SessionIdle`, `net10.0`, `Grpc.AspNetCore (2.71.0)` to the rest of the system?**
  _392 weakly-connected nodes found - possible documentation gaps or missing edges._