namespace CmdWarden.Tests;

/// <summary>
/// Seam: the Approval Gate helper WinExe that Windows uses for the native caption icon
/// (ticket Native title bar shows the Lintel product mark).
/// </summary>
public class ApprovalGateIconTests
{
    [Fact]
    public void Helper_exe_embeds_a_win32_icon()
    {
        var exe = TestPaths.FindApprovalGateExe();
        Assert.True(Win32Pe.HasGroupIcon(exe),
            "CmdWarden.ApprovalGate.exe has no RT_GROUP_ICON. Set ApplicationIcon to a multi-size .ico.");
    }

    [Fact]
    public void Product_mark_ico_ships_caption_sizes()
    {
        var ico = Path.Combine(TestPaths.RepoRoot, "src", "CmdWarden.ApprovalGate", "CmdWarden.ico");
        Assert.True(File.Exists(ico));
        Assert.Equal(new[] { 16, 24, 32, 48, 256 }, IcoFile.Sizes(ico));
    }
}

internal static class IcoFile
{
    public static int[] Sizes(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var count = BitConverter.ToUInt16(bytes, 4);
        var sizes = new int[count];
        for (var i = 0; i < count; i++)
        {
            var w = bytes[6 + i * 16];
            sizes[i] = w == 0 ? 256 : w;
        }

        return sizes;
    }
}

/// <summary>
/// Minimal PE resource walk. Windows reads RT_GROUP_ICON from the exe for the caption.
/// </summary>
internal static class Win32Pe
{
    private const int RtGroupIcon = 14;

    public static bool HasGroupIcon(string exePath)
    {
        var bytes = File.ReadAllBytes(exePath);
        if (bytes.Length < 64 || bytes[0] != (byte)'M' || bytes[1] != (byte)'Z')
            return false;

        var pe = BitConverter.ToInt32(bytes, 0x3C);
        if (pe < 0 || pe + 24 + 2 > bytes.Length)
            return false;
        if (BitConverter.ToUInt32(bytes, pe) != 0x4550)
            return false;

        var optMagic = BitConverter.ToUInt16(bytes, pe + 24);
        var ddOff = optMagic switch
        {
            0x10B => pe + 24 + 96,
            0x20B => pe + 24 + 112,
            _ => -1
        };
        if (ddOff < 0 || ddOff + 24 > bytes.Length)
            return false;

        var resRva = BitConverter.ToInt32(bytes, ddOff + 16);
        var resSize = BitConverter.ToInt32(bytes, ddOff + 20);
        if (resRva == 0 || resSize == 0)
            return false;

        var numberOfSections = BitConverter.ToUInt16(bytes, pe + 6);
        var sizeOfOptional = BitConverter.ToUInt16(bytes, pe + 20);
        var sectionStart = pe + 24 + sizeOfOptional;
        var resFile = RvaToOffset(bytes, sectionStart, numberOfSections, resRva);
        if (resFile < 0)
            return false;

        return DirectoryHasType(bytes, resFile, RtGroupIcon);
    }

    private static int RvaToOffset(byte[] bytes, int sectionStart, int sectionCount, int rva)
    {
        for (var i = 0; i < sectionCount; i++)
        {
            var s = sectionStart + i * 40;
            if (s + 40 > bytes.Length)
                return -1;
            var virt = BitConverter.ToInt32(bytes, s + 12);
            var rawSize = BitConverter.ToInt32(bytes, s + 16);
            var rawPtr = BitConverter.ToInt32(bytes, s + 20);
            if (rva >= virt && rva < virt + Math.Max(rawSize, BitConverter.ToInt32(bytes, s + 8)))
                return rawPtr + (rva - virt);
        }

        return -1;
    }

    private static bool DirectoryHasType(byte[] bytes, int dirFile, int typeId)
    {
        if (dirFile < 0 || dirFile + 16 > bytes.Length)
            return false;
        var named = BitConverter.ToUInt16(bytes, dirFile + 12);
        var ids = BitConverter.ToUInt16(bytes, dirFile + 14);
        var count = named + ids;
        for (var i = 0; i < count; i++)
        {
            var e = dirFile + 16 + i * 8;
            if (e + 8 > bytes.Length)
                return false;
            var name = BitConverter.ToUInt32(bytes, e);
            var nameIsString = (name & 0x80000000) != 0;
            if (nameIsString)
                continue;
            if ((int)name != typeId)
                continue;
            return true;
        }

        return false;
    }
}
