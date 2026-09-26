using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using CmdWarden.Contracts;
using CmdWarden.Contracts.Grpc;
using Forms = System.Windows.Forms;

namespace CmdWarden.SecretsManager;

/// <summary>
/// #43: the tray icon. It lists the live session allows (the rows of cw policy sessions), revokes
/// one or all, and shows a toast when a deny cooldown or a canary blocks a launcher.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    public const string Argument = "--tray";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly Forms.NotifyIcon _icon;
    private readonly DispatcherTimer _timer;
    private readonly TrayAlertFeed _alerts = new(DateTimeOffset.UtcNow);
    private IReadOnlyList<SessionAllowRow> _rows = [];
    private bool _agentUp;
    private bool _polling;

    public TrayIcon()
    {
        _icon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            Text = ProductInfo.Name,
            ContextMenuStrip = new Forms.ContextMenuStrip(),
            Visible = true,
        };
        _icon.ContextMenuStrip.Opening += (_, _) => BuildMenu();
        // A left click opens the same menu as a right click.
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                typeof(Forms.NotifyIcon).GetMethod("ShowContextMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.Invoke(_icon, null);
        };
        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += async (_, _) => await PollAsync();
        _timer.Start();
        _ = PollAsync();
    }

    private async Task PollAsync()
    {
        if (_polling)
            return;
        _polling = true;
        try
        {
            try
            {
                _rows = await AgentSessionsClient.ListAsync().ConfigureAwait(true);
                _agentUp = true;
            }
            catch (Exception)
            {
                _rows = [];
                _agentUp = false;
            }
            _icon.Text = !_agentUp ? $"{ProductInfo.Name}: Session Agent is not running"
                : _rows.Count == 0 ? $"{ProductInfo.Name}: no session allows"
                : $"{ProductInfo.Name}: {_rows.Count} session allow{(_rows.Count == 1 ? "" : "s")}";

            IReadOnlyList<AuditGateRecord> records;
            try
            {
                records = await Task.Run(() => new AuditLog().ReadRecentRecords(200).Records).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                records = [];
            }
            foreach (var alert in _alerts.Next(records, DateTimeOffset.UtcNow))
                _icon.ShowBalloonTip(8000, alert.Title, alert.Text, Forms.ToolTipIcon.Warning);
        }
        finally
        {
            _polling = false;
        }
    }

    private void BuildMenu()
    {
        var menu = _icon.ContextMenuStrip!;
        menu.Items.Clear();
        menu.Items.Add(new Forms.ToolStripLabel(ProductInfo.Name) { Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold) });
        if (!_agentUp)
            menu.Items.Add(new Forms.ToolStripMenuItem("Session Agent is not running") { Enabled = false });
        else if (_rows.Count == 0)
            menu.Items.Add(new Forms.ToolStripMenuItem("No active session allows") { Enabled = false });
        foreach (var row in _rows)
        {
            var item = new Forms.ToolStripMenuItem(MenuLine(row)) { ToolTipText = $"{row.LauncherPolicyKey} ({row.LauncherKind})" };
            var id = row.Id;
            item.DropDownItems.Add("Revoke", null, async (_, _) => await RevokeAsync(id, all: false));
            menu.Items.Add(item);
        }
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Revoke all", null, async (_, _) => await RevokeAsync(null, all: true))
        {
            Enabled = _rows.Count > 0,
        });
        menu.Items.Add("Open CmdWarden Vault", null, (_, _) => OpenVault());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit tray icon", null, (_, _) => System.Windows.Application.Current.Shutdown());
    }

    /// <summary>"gh / GH_TOKEN · write · claude (pid 1234) · until the launcher exits".</summary>
    private static string MenuLine(SessionAllowRow row)
    {
        string launcher;
        try
        {
            using var p = Process.GetProcessById(row.Pid);
            launcher = $"{p.ProcessName} (pid {row.Pid})";
        }
        catch (Exception)
        {
            launcher = $"pid {row.Pid}";
        }
        return $"{row.Tool} / {row.SecretName}  ·  {row.CommandClass}  ·  {launcher}  ·  {SessionAllowDisplay.Ends(row.EndsUtc)}";
    }

    private async Task RevokeAsync(string? id, bool all)
    {
        try
        {
            await AgentSessionsClient.RevokeAsync(id, all).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _icon.ShowBalloonTip(5000, ProductInfo.Name, "Revoke failed: " + VaultSecretFormValidation.SanitizeError(ex.Message), Forms.ToolTipIcon.Error);
        }
        await PollAsync().ConfigureAwait(true);
    }

    private static void OpenVault()
    {
        // The Vault is single instance: a second start brings the open window to the front.
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false })?.Dispose();
    }

    public void Dispose()
    {
        _timer.Stop();
        _icon.Visible = false;
        _icon.Dispose();
    }
}
