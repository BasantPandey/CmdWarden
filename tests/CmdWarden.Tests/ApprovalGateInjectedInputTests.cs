using System.Diagnostics;
using System.Runtime.InteropServices;
using CmdWarden.Contracts;
using Xunit.Abstractions;

namespace CmdWarden.Tests;

/// <summary>
/// #23: an agent that runs as the same user must not approve its own request. The real popup
/// ignores SendInput, PostMessage, and UI Automation for Approve and Allow for session.
/// Deny works with every input type.
/// </summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class ApprovalGateInjectedInputTests(ITestOutputHelper output)
{
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmChar = 0x0102;
    private const int VkEscape = 0x1B;

    [Fact]
    public async Task Injected_approve_attempts_give_no_answer_and_posted_Esc_denies()
    {
        if (!OperatingSystem.IsWindows() || !Environment.UserInteractive)
            return;

        // The test moves the real pointer and sends keys. A person at this PC can answer the popup for real.
        using var person = new RealInputWatch();
        try
        {
            await InjectedApproveAttemptsAsync();
            output.WriteLine($"Real input from a person: {(person.Seen ? "yes" : "no")}");
        }
        catch (Exception ex) when (person.Seen)
        {
            output.WriteLine("Inconclusive: a person used the keyboard or mouse during the test. " + ex.Message);
        }
    }

    private async Task InjectedApproveAttemptsAsync()
    {
        await using var gate = await GateProcess.StartAsync();

        // PostMessage: Enter (default button) and A (Allow for session).
        foreach (var vk in new[] { ApprovalInputGuard.VkReturn, ApprovalInputGuard.VkA })
        {
            PostMessage(gate.Hwnd, WmKeyDown, vk, 1);
            PostMessage(gate.Hwnd, WmChar, vk, 1);
            PostMessage(gate.Hwnd, WmKeyUp, vk, unchecked((int)0xC0000001));
        }
        await gate.AssertNoAnswerAsync("PostMessage Enter / A");

        // UI Automation: Invoke is not offered on Approve or Allow for session; the names stay readable.
        var uia = await RunUiaAsync(gate.Pid, "Approve Once") + await RunUiaAsync(gate.Pid, "Allow for session");
        Assert.Contains("name:Approve Once", uia);
        Assert.Contains("name:Allow for session", uia);
        Assert.DoesNotContain("invoked:", uia);
        await gate.AssertNoAnswerAsync("UI Automation Invoke");

        // SendInput: only safe while the popup has the keyboard focus, so the key reaches nothing else.
        if (gate.TryForeground())
        {
            SendKey(ApprovalInputGuard.VkReturn);
            SendKey(ApprovalInputGuard.VkA);
            await gate.AssertNoAnswerAsync("SendInput Enter / A");
            output.WriteLine("SendInput keys: tried");
        }
        else
        {
            output.WriteLine("SendInput keys: skipped (popup is not the foreground window)");
        }

        // SendInput mouse click on Approve Once, when nothing covers the button.
        var click = await RunUiaAsync(gate.Pid, "Approve Once", click: true);
        output.WriteLine("SendInput click: " + click.Trim().Replace(Environment.NewLine, " "));
        if (click.Contains("clicked:", StringComparison.Ordinal))
            await gate.AssertNoAnswerAsync("SendInput click");

        PostMessage(gate.Hwnd, WmKeyDown, VkEscape, 1);
        Assert.Equal(ApprovalHelperExitCodes.Deny, await gate.WaitExitAsync());
    }

    [Fact]
    public async Task UI_Automation_can_press_Deny()
    {
        if (!OperatingSystem.IsWindows() || !Environment.UserInteractive)
            return;

        await using var gate = await GateProcess.StartAsync();
        var uia = await RunUiaAsync(gate.Pid, "Deny");
        Assert.Contains("invoked:Deny", uia);
        Assert.Equal(ApprovalHelperExitCodes.Deny, await gate.WaitExitAsync());
    }

    [Fact]
    public async Task SendInput_Esc_denies()
    {
        if (!OperatingSystem.IsWindows() || !Environment.UserInteractive)
            return;

        await using var gate = await GateProcess.StartAsync();
        if (!gate.TryForeground())
            return;
        SendKey(VkEscape);
        Assert.Equal(ApprovalHelperExitCodes.Deny, await gate.WaitExitAsync());
    }

    /// <summary>
    /// Windows PowerShell has the UI Automation client. It prints "name:" when the button exists
    /// and "invoked:" when Invoke worked. With click it sends a SendInput click on the
    /// button center, in the same DPI context that read the rectangle.
    /// </summary>
    private static async Task<string> RunUiaAsync(int pid, string name, bool click = false)
    {
        var script = Path.Combine(Path.GetTempPath(), "cw-uia-" + Guid.NewGuid().ToString("N") + ".ps1");
        await File.WriteAllTextAsync(script, """
            param([int]$ProcessId, [string]$Name, [switch]$Click)
            Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
            Add-Type @'
            using System; using System.Runtime.InteropServices;
            public static class U {
              [StructLayout(LayoutKind.Sequential)] public struct P { public int X; public int Y; }
              [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
              [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, UIntPtr e);
              [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(P p);
              [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint f);
            }
            '@
            $root = [System.Windows.Automation.AutomationElement]::RootElement
            $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $ProcessId)
            $win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
            foreach ($n in @($Name)) {
              $nc = New-Object System.Windows.Automation.AndCondition(
                (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $n)),
                (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
              $btn = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nc)
              if ($null -eq $btn) { continue }
              "name:$n"
              if ($Click) {
                $r = $btn.Current.BoundingRectangle
                $p = New-Object U+P
                $p.X = [int]($r.X + $r.Width / 2); $p.Y = [int]($r.Y + $r.Height / 2)
                $top = [U]::GetAncestor([U]::WindowFromPoint($p), 2)
                if ($top -ne [IntPtr]$win.Current.NativeWindowHandle) { "covered:$n"; continue }
                [U]::SetCursorPos($p.X, $p.Y) | Out-Null
                [U]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); [U]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
                "clicked:$n"
                continue
              }
              $pat = $null
              if ($btn.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pat)) {
                try { $pat.Invoke(); "invoked:$n" } catch { "invoke-failed:$n" }
              }
            }
            """);
        try
        {
            var psi = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-ProcessId", pid.ToString(), "-Name", name })
                psi.ArgumentList.Add(a);
            if (click)
                psi.ArgumentList.Add("-Click");
            using var ps = Process.Start(psi)!;
            var output = await ps.StandardOutput.ReadToEndAsync();
            await ps.WaitForExitAsync();
            return output;
        }
        finally
        {
            File.Delete(script);
        }
    }

    private sealed class GateProcess : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly string _payload;

        private GateProcess(Process process, string payload, IntPtr hwnd)
        {
            _process = process;
            _payload = payload;
            Hwnd = hwnd;
        }

        public IntPtr Hwnd { get; }
        public int Pid => _process.Id;

        public static async Task<GateProcess> StartAsync()
        {
            var payload = Path.Combine(Path.GetTempPath(), "cw-gate-" + Guid.NewGuid().ToString("N") + ".json");
            var request = new ApprovalRequest(
                Tool: "gh", CommandClass: CommandClassNames.Write, PolicyLevel: PolicyLevelNames.Read,
                LauncherPolicyKey: "pathhash:sha256:test", LauncherKind: LauncherKinds.PathHash,
                LauncherPath: null, SecretName: "GH_TOKEN", Purpose: "authorize",
                EnrollmentKind: LauncherEnrollmentKindNames.AiHarness, PolicyNote: null,
                CommandLine: "gh pr create --fill", LauncherPid: Environment.ProcessId);
            await File.WriteAllTextAsync(payload, ApprovalHelperJson.Serialize(ApprovalPresentation.ToHelperPayload(request)));

            var psi = new ProcessStartInfo(TestPaths.FindApprovalGateExe()) { UseShellExecute = false };
            psi.ArgumentList.Add("--payload");
            psi.ArgumentList.Add(payload);
            var process = Process.Start(psi)!;
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                process.Refresh();
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    // Past the key arm delay, so only the input guard can stop an approve.
                    await Task.Delay(1200);
                    return new GateProcess(process, payload, process.MainWindowHandle);
                }
                if (process.HasExited)
                    break;
                await Task.Delay(100);
            }
            throw new TimeoutException("Approval Gate window did not open.");
        }

        public bool TryForeground()
        {
            SetForegroundWindow(Hwnd);
            Thread.Sleep(200);
            return GetForegroundWindow() == Hwnd;
        }

        public async Task AssertNoAnswerAsync(string attempt)
        {
            await Task.Delay(1000);
            Assert.False(_process.HasExited, $"{attempt} ended the popup with exit code {(_process.HasExited ? _process.ExitCode : -1)}.");
        }

        public async Task<int> WaitExitAsync()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _process.WaitForExitAsync(cts.Token);
            return _process.ExitCode;
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                if (!_process.HasExited)
                    _process.Kill();
            }
            catch { /* ignore */ }
            _process.Dispose();
            try { File.Delete(_payload); } catch { /* ignore */ }
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Low-level hooks on their own thread. Seen is true after a real key or a real mouse button.</summary>
    private sealed class RealInputWatch : IDisposable
    {
        private const int WmMouseMove = 0x0200;
        private const int WmMouseWheel = 0x020A;
        private const uint WmQuit = 0x0012;
        private readonly Thread _thread;
        private readonly HookProc _mouse;
        private readonly HookProc _keyboard;
        private uint _threadId;
        private volatile bool _seen;

        public RealInputWatch()
        {
            // MSLLHOOKSTRUCT flags are at offset 12 (bit 0x01 injected); KBDLLHOOKSTRUCT flags at offset 8 (bit 0x10).
            _mouse = (code, message, data) =>
            {
                if (code >= 0 && (int)message != WmMouseMove && (int)message != WmMouseWheel && (Marshal.ReadInt32(data, 12) & 0x01) == 0)
                    _seen = true;
                return CallNextHookEx(IntPtr.Zero, code, message, data);
            };
            _keyboard = (code, message, data) =>
            {
                if (code >= 0 && (Marshal.ReadInt32(data, 8) & 0x10) == 0)
                    _seen = true;
                return CallNextHookEx(IntPtr.Zero, code, message, data);
            };
            using var ready = new ManualResetEventSlim();
            _thread = new Thread(() =>
            {
                _threadId = GetCurrentThreadId();
                var module = GetModuleHandle(null);
                var mouseHook = SetWindowsHookEx(14, _mouse, module, 0);
                var keyboardHook = SetWindowsHookEx(13, _keyboard, module, 0);
                ready.Set();
                while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
                {
                }
                UnhookWindowsHookEx(mouseHook);
                UnhookWindowsHookEx(keyboardHook);
            }) { IsBackground = true };
            _thread.Start();
            ready.Wait();
        }

        public bool Seen => _seen;

        public void Dispose()
        {
            PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
            _thread.Join(2000);
        }
    }

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg msg, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    private static void SendKey(int vk)
    {
        var inputs = new[]
        {
            new Input { Type = 1, Ki = new KeybdInput { Vk = (ushort)vk } },
            new Input { Type = 1, Ki = new KeybdInput { Vk = (ushort)vk, Flags = 2 } },
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        Thread.Sleep(100);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public KeybdInput Ki;
    }

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint msg, int wParam, int lParam);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
