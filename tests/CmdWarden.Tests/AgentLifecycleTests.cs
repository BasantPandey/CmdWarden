using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// Agent start/status/stop and agent-down path (issue #36).
/// </summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class AgentLifecycleTests
{
    [Fact]
    public async Task Status_reports_down_when_pipe_missing()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var pipe = $"{AgentEndpoints.PipeNamePrefix}-life-missing-{Guid.NewGuid():N}";
        var st = await AgentLifecycle.StatusAsync(pipe, TimeSpan.FromMilliseconds(600));
        Assert.False(st.Up);
        Assert.Equal(pipe, st.PipeName);
    }

    [Fact]
    public async Task Status_reports_access_denied_when_the_pipe_rejects_this_user()
    {
        if (!OperatingSystem.IsWindows())
            return;

        // Same shape as an elevated agent seen from a normal shell: pipe exists, connect is denied.
        var pipe = $"{AgentEndpoints.PipeNamePrefix}-life-denied-{Guid.NewGuid():N}";
        var me = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var security = new System.IO.Pipes.PipeSecurity();
        security.SetOwner(me);
        security.AddAccessRule(new System.IO.Pipes.PipeAccessRule(
            me, System.IO.Pipes.PipeAccessRights.FullControl, System.Security.AccessControl.AccessControlType.Deny));
        using var server = System.IO.Pipes.NamedPipeServerStreamAcl.Create(
            pipe, System.IO.Pipes.PipeDirection.InOut, 1, System.IO.Pipes.PipeTransmissionMode.Byte,
            System.IO.Pipes.PipeOptions.Asynchronous, 0, 0, security);

        var st = await AgentLifecycle.StatusAsync(pipe, TimeSpan.FromMilliseconds(800));
        Assert.False(st.Up);
        Assert.True(st.AccessDenied, st.Detail);
        Assert.Equal(AgentLifecycle.AccessDeniedDetail, st.Detail);
    }

    [Fact]
    public async Task Start_status_stop_round_trip()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var pipe = $"{AgentEndpoints.PipeNamePrefix}-life-{Guid.NewGuid():N}";
        var root = Path.Combine(Path.GetTempPath(), "cw-life-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("CW_PIPE_NAME", pipe);
        Environment.SetEnvironmentVariable(ProductPaths.EnvVar, root);
        Environment.SetEnvironmentVariable("CW_APPROVAL_MODE", "off");
        try
        {
            var binary = AgentLocator.FindAgentBinary();
            Assert.NotNull(binary);
            Assert.True(File.Exists(binary));

            var started = await AgentLifecycle.StartAsync(pipe, binary, root, TimeSpan.FromSeconds(25));
            Assert.True(started.Up, started.Detail ?? "start failed");

            var status = await AgentLifecycle.StatusAsync(pipe);
            Assert.True(status.Up);
            Assert.NotNull(status.ProcessId);

            var stopped = await AgentLifecycle.StopAsync(pipe, root);
            Assert.False(stopped.Up, stopped.Detail);

            var after = await AgentLifecycle.StatusAsync(pipe, TimeSpan.FromMilliseconds(800));
            Assert.False(after.Up);
        }
        finally
        {
            try { await AgentLifecycle.StopAsync(pipe, root); } catch { /* ignore */ }
            Environment.SetEnvironmentVariable("CW_PIPE_NAME", null);
            Environment.SetEnvironmentVariable(ProductPaths.EnvVar, null);
            Environment.SetEnvironmentVariable("CW_APPROVAL_MODE", null);
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void FindAgentBinary_locates_dev_or_bundled_agent()
    {
        var binary = AgentLocator.FindAgentBinary();
        // After solution build, agent should be discoverable from test host or repo layout.
        Assert.NotNull(binary);
        Assert.True(File.Exists(binary!), "Agent binary missing; build CmdWarden.Agent first.");
    }

    [Fact]
    public async Task Start_with_missing_binary_fails_closed_with_actionable_detail()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var pipe = $"{AgentEndpoints.PipeNamePrefix}-life-nobin-{Guid.NewGuid():N}";
        var root = Path.Combine(Path.GetTempPath(), "cw-life-nobin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var missing = Path.Combine(root, "does-not-exist-CmdWarden.Agent.exe");
            var started = await AgentLifecycle.StartAsync(
                pipe,
                agentBinary: missing,
                productRoot: root,
                readyTimeout: TimeSpan.FromSeconds(2));

            Assert.False(started.Up);
            Assert.NotNull(started.Detail);
            Assert.Contains("not found", started.Detail!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task EnsureRunning_when_already_up_is_idempotent()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var pipe = $"{AgentEndpoints.PipeNamePrefix}-life-ensure-{Guid.NewGuid():N}";
        var root = Path.Combine(Path.GetTempPath(), "cw-life-ensure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("CW_PIPE_NAME", pipe);
        Environment.SetEnvironmentVariable(ProductPaths.EnvVar, root);
        Environment.SetEnvironmentVariable("CW_APPROVAL_MODE", "off");
        try
        {
            var binary = AgentLocator.FindAgentBinary();
            Assert.NotNull(binary);

            var first = await AgentLifecycle.EnsureRunningAsync(pipe, binary, root, TimeSpan.FromSeconds(25));
            Assert.True(first.Up, first.Detail ?? "ensure start failed");

            var second = await AgentLifecycle.EnsureRunningAsync(pipe, binary, root, TimeSpan.FromSeconds(5));
            Assert.True(second.Up);
            Assert.Equal(first.ProcessId, second.ProcessId);
        }
        finally
        {
            try { await AgentLifecycle.StopAsync(pipe, root); } catch { /* ignore */ }
            Environment.SetEnvironmentVariable("CW_PIPE_NAME", null);
            Environment.SetEnvironmentVariable(ProductPaths.EnvVar, null);
            Environment.SetEnvironmentVariable("CW_APPROVAL_MODE", null);
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void FindAgentBinary_finds_agent_next_to_the_secrets_manager_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-loc-" + Guid.NewGuid().ToString("N"));
        var ui = Path.Combine(root, "secrets-manager");
        var agent = Path.Combine(root, "agent");
        Directory.CreateDirectory(ui);
        Directory.CreateDirectory(agent);
        var dll = Path.Combine(agent, AgentLocator.AgentDllName);
        File.WriteAllText(dll, "");
        try
        {
            Assert.Equal(dll, AgentLocator.FindAgentBinary(ui));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void FindAgentBinary_respects_LG_AGENT_PATH_env()
    {
        var previous = Environment.GetEnvironmentVariable(AgentLocator.EnvAgentPath);
        var temp = Path.Combine(Path.GetTempPath(), "cw-agent-path-" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            File.WriteAllBytes(temp, [0]);
            Environment.SetEnvironmentVariable(AgentLocator.EnvAgentPath, temp);
            var found = AgentLocator.FindAgentBinary();
            Assert.Equal(Path.GetFullPath(temp), found);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AgentLocator.EnvAgentPath, previous);
            try { File.Delete(temp); } catch { /* ignore */ }
        }
    }
}
