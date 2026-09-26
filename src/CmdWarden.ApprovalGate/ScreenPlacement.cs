using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CmdWarden.ApprovalGate;

/// <summary>
/// Place a window in the work area of its monitor, in physical pixels. WPF gives
/// <see cref="SystemParameters.WorkArea"/> and CenterScreen in the units of the DPI at sign-in.
/// After a scale change (for example 100% to 150%) those units are wrong, and a window can go
/// below or right of the screen.
/// </summary>
internal static class ScreenPlacement
{
    public static void Center(Window window) =>
        Place(window, (work, w, h) => (work.Left + Math.Max(0, (work.Width - w) / 2), work.Top + Math.Max(0, (work.Height - h) / 2)));

    public static void BottomRight(Window window) =>
        Place(window, (work, w, h) => (work.Right - w, work.Bottom - h));

    private static void Place(Window window, Func<Rect32, int, int, (int X, int Y)> position)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, MonitorDefaultToNearest), ref info) || !GetWindowRect(hwnd, out var rect))
            return;
        var (x, y) = position(info.Work, rect.Width, rect.Height);
        SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    private const uint MonitorDefaultToNearest = 2;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect32 Monitor;
        public Rect32 Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect32 rect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
