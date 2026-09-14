using CmdWarden.Agent.Approval;

namespace CmdWarden.Tests;

/// <summary>
/// Locator for packaged Approval Gate helper under agent/approval-gate/ (ticket #81).
/// </summary>
public class ApprovalGateLocatorTests
{
    [Fact]
    public void Finds_helper_under_agent_approval_gate_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-ag-" + Guid.NewGuid().ToString("N"));
        var helperDir = Path.Combine(root, "approval-gate");
        Directory.CreateDirectory(helperDir);
        var helper = Path.Combine(helperDir, ApprovalGateLocator.HelperExeName);
        File.WriteAllBytes(helper, new byte[] { 0x4D, 0x5A });
        try
        {
            var found = ApprovalGateLocator.FindHelperPath(root);
            Assert.NotNull(found);
            Assert.True(File.Exists(found));
            Assert.Equal(Path.GetFullPath(helper), Path.GetFullPath(found!));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Finds_helper_under_cli_agent_approval_gate_layout()
    {
        // When base is tool root (…/any/), helper lives at agent/approval-gate/
        var root = Path.Combine(Path.GetTempPath(), "cw-ag-cli-" + Guid.NewGuid().ToString("N"));
        var helperDir = Path.Combine(root, "agent", "approval-gate");
        Directory.CreateDirectory(helperDir);
        var helper = Path.Combine(helperDir, ApprovalGateLocator.HelperExeName);
        File.WriteAllBytes(helper, new byte[] { 0x4D, 0x5A });
        try
        {
            var found = ApprovalGateLocator.FindHelperPath(root);
            Assert.NotNull(found);
            Assert.Equal(Path.GetFullPath(helper), Path.GetFullPath(found!));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Missing_helper_returns_null()
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-ag-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Null(ApprovalGateLocator.FindHelperPath(root));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }
}
