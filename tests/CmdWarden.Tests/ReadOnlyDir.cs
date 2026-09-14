using System.Security.AccessControl;
using System.Security.Principal;

namespace CmdWarden.Tests;

/// <summary>Deny file creation and writes in one directory for the current user until disposed.</summary>
internal sealed class ReadOnlyDir : IDisposable
{
    private readonly DirectoryInfo _info;
    private readonly FileSystemAccessRule _rule;

    public ReadOnlyDir(string dir)
    {
        Directory.CreateDirectory(dir);
        _info = new DirectoryInfo(dir);
        _rule = new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!,
            FileSystemRights.CreateFiles | FileSystemRights.WriteData | FileSystemRights.AppendData,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Deny);
        var acl = _info.GetAccessControl();
        acl.AddAccessRule(_rule);
        _info.SetAccessControl(acl);
    }

    public void Dispose()
    {
        var acl = _info.GetAccessControl();
        acl.RemoveAccessRule(_rule);
        _info.SetAccessControl(acl);
    }
}
