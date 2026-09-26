using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CmdWarden.Contracts;

namespace CmdWarden.Ui;

/// <summary>
/// WPF images for tool and launcher logos. Linked into the Approval Gate and the Vault.
/// </summary>
internal static class BrandImages
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? ForTool(string? tool)
    {
        if (BrandMarks.ForTool(tool) is not { } mark)
            return null;
        try
        {
            return FromMark(mark);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            // A user tool pack can carry a bad logo. The card then shows no logo.
            return null;
        }
    }

    /// <summary>Known harness logo first, then the exe icon from <paramref name="path"/>, else null.</summary>
    public static ImageSource? ForLauncher(string? displayName, string? path)
    {
        if (BrandMarks.ForLauncher(displayName, path) is { } mark)
            return FromMark(mark);
        if (string.IsNullOrWhiteSpace(path))
            return null;
        lock (Cache)
        {
            if (!Cache.TryGetValue(path, out var icon))
                Cache[path] = icon = ExtractExeIcon(path);
            return icon;
        }
    }

    public static ImageSource FromMark(BrandMark mark)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(mark.Name, out var cached) && cached is not null)
                return cached;
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(mark.Color));
            brush.Freeze();
            var group = new DrawingGroup();
            // The clear 24x24 box keeps every logo on the same scale and centered.
            group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
            group.Children.Add(new GeometryDrawing(brush, null, Geometry.Parse(mark.PathData)));
            var image = new DrawingImage(group);
            image.Freeze();
            Cache[mark.Name] = image;
            return image;
        }
    }

    private static ImageSource? ExtractExeIcon(string path)
    {
        if (!File.Exists(path))
            return null;
        const int size = 256;
        try
        {
            var hr = SHDefExtractIconW(path, 0, 0, out var large, out var small, size);
            try
            {
                if (hr != 0 || large == IntPtr.Zero)
                    return null;
                var source = Imaging.CreateBitmapSourceFromHIcon(large, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                if (large != IntPtr.Zero)
                    DestroyIcon(large);
                if (small != IntPtr.Zero)
                    DestroyIcon(small);
            }
        }
        catch (Exception ex) when (ex is COMException or ExternalException or ArgumentException)
        {
            return null;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHDefExtractIconW(string iconFile, int iconIndex, uint flags,
        out IntPtr large, out IntPtr small, uint iconSize);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
