using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using CmdWarden.Contracts;

namespace CmdWarden.ApprovalGate;

/// <summary>
/// Low-level keyboard and mouse hooks for this process (#23). They feed the guard with each
/// hardware event and its injected flag. The hook callbacks run on the UI thread.
/// </summary>
internal sealed class RealInputHooks : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int WmLButtonUp = 0x0202;
    private const uint LlkhfInjected = 0x10;
    private const uint LlmhfInjected = 0x01;
    private const uint GaRoot = 2;

    private readonly ApprovalInputGuard _guard;
    private readonly Func<IntPtr> _window;
    private readonly HookProc _keyboardProc;
    private readonly HookProc _mouseProc;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;

    public RealInputHooks(ApprovalInputGuard guard, Func<IntPtr> window)
    {
        _guard = guard;
        _window = window;
        // Keep the delegates alive while the hooks exist.
        _keyboardProc = OnKeyboard;
        _mouseProc = OnMouse;
        var module = GetModuleHandle(null);
        _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, module, 0);
        _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseProc, module, 0);
        if (_keyboardHook == IntPtr.Zero || _mouseHook == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowsHookEx failed.");
    }

    private IntPtr OnKeyboard(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && (message == WmKeyDown || message == WmSysKeyDown))
        {
            var info = Marshal.PtrToStructure<KbdLlHookStruct>(data);
            _guard.OnKeyDown((int)info.VkCode, (info.Flags & LlkhfInjected) != 0,
                GetForegroundWindow() == _window(), DateTime.UtcNow);
        }
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    private IntPtr OnMouse(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && message == WmLButtonUp)
        {
            var info = Marshal.PtrToStructure<MsLlHookStruct>(data);
            // The hook point is in physical pixels, so WindowFromPhysicalPoint needs no DPI math.
            var hit = WindowFromPhysicalPoint(info.Pt);
            var overWindow = hit != IntPtr.Zero && GetAncestor(hit, GaRoot) == _window();
            _guard.OnLeftButtonUp((info.Flags & LlmhfInjected) != 0, overWindow, DateTime.UtcNow);
        }
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    public void Dispose()
    {
        if (_keyboardHook != IntPtr.Zero)
            UnhookWindowsHookEx(_keyboardHook);
        if (_mouseHook != IntPtr.Zero)
            UnhookWindowsHookEx(_mouseHook);
        _keyboardHook = _mouseHook = IntPtr.Zero;
    }

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsLlHookStruct
    {
        public Point Pt;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPhysicalPoint(Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}

/// <summary>
/// A button that UI Automation can read but not press (#23). Screen readers still get the name.
/// </summary>
public sealed class GatedButton : Button
{
    protected override AutomationPeer OnCreateAutomationPeer() => new GatedButtonPeer(this);

    private sealed class GatedButtonPeer(Button owner) : ButtonAutomationPeer(owner)
    {
        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Invoke ? null : base.GetPattern(patternInterface);
    }
}
