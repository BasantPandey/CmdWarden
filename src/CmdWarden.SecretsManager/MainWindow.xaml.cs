using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Grpc.Core;
using CmdWarden.Contracts;
using CmdWarden.Contracts.Scan;
using CmdWarden.Ui;

namespace CmdWarden.SecretsManager;

public partial class MainWindow : Window
{
    public const string WindowTitle = "CmdWarden Vault";

    private const double ExpandedSidebarWidth = 228;
    private const double CollapsedSidebarWidth = 64;

    private readonly ObservableCollection<VaultSecretListItem> _secrets = [];
    private readonly DispatcherTimer _pollTimer;
    private readonly Dictionary<string, FrameworkElement> _pages;
    private readonly Dictionary<string, Button> _navButtons;
    private readonly Dictionary<string, TextBlock> _navLabels;
    private readonly Dictionary<string, (string Title, string Primary, string Key)> _pageChrome;

    private bool _sidebarCollapsed;
    private string _currentPage = "secrets";

    public MainWindow()
    {
        InitializeComponent();
        SecretsList.ItemsSource = _secrets;

        _pages = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal)
        {
            ["gates"] = PageGates,
            ["detectors"] = PageDetectors,
            ["tools"] = PageTools,
            ["secrets"] = PageSecrets,
            ["usage"] = PageUsage,
            ["doctor"] = PageDoctor,
        };
        _navButtons = new Dictionary<string, Button>(StringComparer.Ordinal)
        {
            ["gates"] = NavGates,
            ["detectors"] = NavDetectors,
            ["tools"] = NavTools,
            ["secrets"] = NavSecrets,
            ["usage"] = NavUsage,
            ["doctor"] = NavDoctor,
        };
        _navLabels = new Dictionary<string, TextBlock>(StringComparer.Ordinal)
        {
            ["gates"] = NavGatesLabel,
            ["detectors"] = NavDetectorsLabel,
            ["tools"] = NavToolsLabel,
            ["secrets"] = NavSecretsLabel,
            ["usage"] = NavUsageLabel,
            ["doctor"] = NavDoctorLabel,
        };
        _pageChrome = new Dictionary<string, (string, string, string)>(StringComparer.Ordinal)
        {
            ["gates"] = ("Secret Gates", "Refresh", "F5"),
            ["detectors"] = ("Detectors", "Run scan", "F5"),
            ["tools"] = ("Hardened Tools", "Refresh", "F5"),
            ["secrets"] = ("Secrets", "+ Add secret", "Ctrl+N"),
            ["usage"] = ("Secret Usage", "Refresh", "F5"),
            ["doctor"] = ("Doctor", "Refresh", "F5"),
        };

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _pollTimer.Tick += async (_, _) =>
        {
            if (_currentPage == "secrets")
                await RefreshAsync().ConfigureAwait(true);
        };

        ShowPage("secrets");
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _pollTimer.Start();
        await RefreshAsync().ConfigureAwait(true);
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) => _pollTimer.Stop();

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        var none = Keyboard.Modifiers == ModifierKeys.None;
        var onSecrets = _currentPage == "secrets";
        if (none && e.Key is Key.OemOpenBrackets or Key.OemCloseBrackets)
        {
            e.Handled = true;
            ToggleSidebar();
        }
        else if (none && e.Key == Key.F5)
        {
            e.Handled = true;
            if (onSecrets)
                await RefreshAsync().ConfigureAwait(true);
            else if (PagePrimaryButton.IsEnabled)
                PagePrimaryButton_Click(PagePrimaryButton, new RoutedEventArgs());
        }
        else if (ctrl && e.Key == Key.N && onSecrets && AddSecretButton.IsEnabled)
        {
            e.Handled = true;
            await AddSecretFlowAsync().ConfigureAwait(true);
        }
        else if (none && onSecrets && e.Key is Key.Down or Key.Up)
        {
            e.Handled = true;
            if (VaultSecretListSelection.Move(_secrets, e.Key == Key.Down ? 1 : -1) is { } item)
                BringIntoView(item);
        }
        else if (none && onSecrets && e.Key == Key.Escape)
        {
            e.Handled = true;
            VaultSecretListSelection.Clear(_secrets);
        }
        else if (none && onSecrets && e.Key == Key.Delete
                 && _secrets.FirstOrDefault(s => s.CanDelete) is { } selected)
        {
            e.Handled = true;
            await DeleteSecretAsync(selected).ConfigureAwait(true);
        }
    }

    private void BringIntoView(VaultSecretListItem item) =>
        (SecretsList.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement)?.BringIntoView();

    private StackPanel KeyedContent(string label, string key) => new()
    {
        Orientation = Orientation.Horizontal,
        Children =
        {
            new TextBlock { Text = label },
            new Border
            {
                Style = (Style)FindResource("KeyChipStyle"),
                Child = new TextBlock { Text = key, Style = (Style)FindResource("KeyChipTextStyle") },
            },
        },
    };

    private void CollapseButton_Click(object sender, RoutedEventArgs e) => ToggleSidebar();

    private void ToggleSidebar()
    {
        _sidebarCollapsed = !_sidebarCollapsed;
        SidebarColumn.Width = new GridLength(_sidebarCollapsed ? CollapsedSidebarWidth : ExpandedSidebarWidth);
        BrandText.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        foreach (var label in _navLabels.Values)
            label.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;

        CollapseButton.Content = _sidebarCollapsed ? "\u00BB" : "\u00AB";
        CollapseButton.ToolTip = _sidebarCollapsed ? "Expand nav (show labels)" : "Collapse nav to icon rail";
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string page })
            ShowPage(page);
    }

    private void ShowPage(string page)
    {
        if (!_pages.ContainsKey(page))
            return;

        _currentPage = page;
        foreach (var (key, element) in _pages)
            element.Visibility = key == page ? Visibility.Visible : Visibility.Collapsed;

        var selectedStyle = (Style)FindResource("NavButtonSelectedStyle");
        var normalStyle = (Style)FindResource("NavButtonStyle");
        foreach (var (key, button) in _navButtons)
            button.Style = key == page ? selectedStyle : normalStyle;

        var chrome = _pageChrome[page];
        PageTitleText.Text = chrome.Title;
        PagePrimaryButton.Content = KeyedContent(chrome.Primary, chrome.Key);
        PagePrimaryButton.ToolTip = $"{chrome.Primary.TrimStart('+', ' ')} ({chrome.Key})";
        PagePrimaryButton.IsEnabled = page is "secrets" or "usage" or "doctor" or "tools" or "detectors" or "gates";
        PageSubtitleText.Visibility = page == "detectors" && PageSubtitleText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Banner only relevant on Secrets (agent list).
        if (page != "secrets")
            AgentDownBanner.Visibility = Visibility.Collapsed;

        if (page == "doctor")
            _ = DoctorRefreshAsync();
        if (page == "tools")
            _ = ToolsRefreshAsync();
        if (page == "usage")
            _ = UsageRefreshAsync();
        if (page == "detectors")
            _ = DetectorsScanAsync();
        if (page == "gates")
            _ = GatesRefreshAsync();
    }

    private async void PagePrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        switch (_currentPage)
        {
            case "secrets":
                await AddSecretFlowAsync().ConfigureAwait(true);
                break;
            case "doctor":
                await DoctorRefreshAsync().ConfigureAwait(true);
                break;
            case "tools":
                await ToolsRefreshAsync().ConfigureAwait(true);
                break;
            case "usage":
                await UsageRefreshAsync().ConfigureAwait(true);
                break;
            case "detectors":
                await DetectorsScanAsync().ConfigureAwait(true);
                break;
            case "gates":
                await GatesRefreshAsync().ConfigureAwait(true);
                break;
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        await RefreshAsync().ConfigureAwait(true);

    private async void AddSecretButton_Click(object sender, RoutedEventArgs e) =>
        await AddSecretFlowAsync().ConfigureAwait(true);

    private async Task AddSecretFlowAsync()
    {
        var dialog = new AddSecretWindow(_secrets.Select(s => s.Name)) { Owner = this };
        if (dialog.ShowDialog() == true)
            await RefreshAsync().ConfigureAwait(true);
    }

    private void SecretCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: VaultSecretListItem item })
            return;

        VaultSecretListSelection.SelectOnly(_secrets, item);
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { DataContext: VaultSecretListItem item })
            await DeleteSecretAsync(item).ConfigureAwait(true);
    }

    private async Task DeleteSecretAsync(VaultSecretListItem item)
    {
        if (!item.CanDelete)
            return;

        var confirm = new ConfirmWindow(
            title: "Delete secret",
            body: $"Permanently delete {item.Name} from the CmdWarden Vault? This cannot be undone. The value is not shown.",
            confirmLabel: "Delete",
            isDanger: true)
        {
            Owner = this,
        };
        if (confirm.ShowDialog() != true)
            return;

        try
        {
            var deleted = await AgentVaultClient.DeleteAsync(item.Name).ConfigureAwait(true);
            if (!deleted)
            {
                MessageBox.Show(
                    this,
                    $"Secret '{item.Name}' was not present (it may have been removed already).",
                    WindowTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                "Delete failed: " + VaultSecretFormValidation.SanitizeError(ex.Message),
                WindowTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    private async Task RefreshAsync()
    {
        try
        {
            var status = await AgentLifecycle.StatusAsync().ConfigureAwait(true);
            if (!status.Up)
            {
                ApplyDegraded(status.AccessDenied
                    ? status.Detail + " Actions disabled."
                    : "Session Agent not running - actions disabled. From the repo: cw agent stop && cw agent start (use the local Debug build, not an old installed tool).");
                return;
            }

            var names = await AgentVaultClient.ListSecretNamesAsync().ConfigureAwait(true);
            ApplyNames(names);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unimplemented)
        {
            ApplyDegraded(
                "Session Agent is running but too old for CmdWarden Vault (ListSecretNames missing). Stop it and start the agent from this repo's Debug build.");
        }
        catch
        {
            ApplyDegraded("Session Agent not reachable - actions disabled.");
        }
    }

    private void ApplyDegraded(string bannerText)
    {
        SetPill(AgentBadge, AgentBadgeText, "Agent down", PillKind.Danger);
        _secrets.Clear();
        AgentDownBannerText.Text = bannerText;
        if (_currentPage == "secrets")
            AgentDownBanner.Visibility = Visibility.Visible;
        SetAddEnabled(false);
        RefreshButton.IsEnabled = false;
        EmptyStatePanel.Visibility = Visibility.Collapsed;
        ListScrollViewer.Visibility = Visibility.Collapsed;
        SecretCountText.Text = "Agent unreachable";
    }

    private void ApplyNames(IReadOnlyList<string> names)
    {
        SetPill(AgentBadge, AgentBadgeText, "Online", PillKind.Ok);
        AgentDownBanner.Visibility = Visibility.Collapsed;
        SetAddEnabled(true);
        RefreshButton.IsEnabled = true;

        var selected = _secrets.FirstOrDefault(s => s.IsSelected)?.Name;
        _secrets.Clear();
        foreach (var name in names)
        {
            var item = new VaultSecretListItem(name);
            if (selected is not null && string.Equals(selected, name, StringComparison.Ordinal))
                item.IsSelected = true;
            _secrets.Add(item);
        }

        SecretCountText.Text = names.Count == 1 ? "1 secret" : $"{names.Count} secrets";

        var empty = names.Count == 0;
        EmptyStatePanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ListScrollViewer.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetAddEnabled(bool enabled)
    {
        AddSecretButton.IsEnabled = enabled;
        EmptyAddButton.IsEnabled = enabled;
        if (_currentPage == "secrets")
            PagePrimaryButton.IsEnabled = enabled;
    }

    // ---- Doctor (read-only; issue #115) ----

    private bool _doctorBusy;

    private async Task DoctorRefreshAsync()
    {
        if (_doctorBusy)
            return;
        _doctorBusy = true;
        if (_currentPage == "doctor")
            PagePrimaryButton.IsEnabled = false;
        SetPill(DoctorAgentPill, DoctorAgentPillText, "Checking...", PillKind.Muted);
        try
        {
            var report = await Task.Run(() => DoctorReport.GatherAsync()).ConfigureAwait(true);
            ApplyDoctor(report);
        }
        catch (Exception ex)
        {
            SetPill(DoctorAgentPill, DoctorAgentPillText, "Error", PillKind.Danger);
            DoctorAgentDetail.Text = VaultSecretFormValidation.SanitizeError(ex.Message);
            DoctorAgentDetail.Visibility = Visibility.Visible;
        }
        finally
        {
            _doctorBusy = false;
            if (_currentPage == "doctor")
                PagePrimaryButton.IsEnabled = true;
        }
    }

    private void ApplyDoctor(DoctorReport r)
    {
        var h = r.Health;
        SetPill(AgentBadge, AgentBadgeText, r.AgentUp ? "Online" : "Agent down", r.AgentUp ? PillKind.Ok : PillKind.Danger);
        DoctorAgentPipe.Text = "Pipe " + r.ExpectedPipe;
        SetPill(DoctorAgentPill, DoctorAgentPillText, r.AgentUp ? "Healthy" : "Down", r.AgentUp ? PillKind.Ok : PillKind.Danger);
        DoctorVersionPill.Visibility = r.VersionMismatch ? Visibility.Visible : Visibility.Collapsed;
        DoctorAgentDetail.Text = r.AgentDetail ?? "";
        DoctorAgentDetail.Visibility = !r.AgentUp && !string.IsNullOrWhiteSpace(r.AgentDetail) ? Visibility.Visible : Visibility.Collapsed;
        DoctorAgentHint.Visibility = r.AgentUp ? Visibility.Collapsed : Visibility.Visible;

        DoctorVaultPath.Text = r.VaultUiPath ?? "Not found next to the CLI, in agent/secrets-manager, or via CW_SECRETS_MANAGER_PATH.";
        SetPill(DoctorVaultPill, DoctorVaultPillText, r.VaultUiPath is null ? "Not found" : "Found", r.VaultUiPath is null ? PillKind.Danger : PillKind.Ok);

        DoctorShortcutPath.Text = r.ShortcutPath;
        SetPill(DoctorShortcutPill, DoctorShortcutPillText, r.ShortcutPresent ? "Present" : "Missing", r.ShortcutPresent ? PillKind.Ok : PillKind.Danger);
        DoctorShortcutHint.Visibility = r.ShortcutPresent ? Visibility.Collapsed : Visibility.Visible;

        const string dash = "-";
        DoctorDetails.ItemsSource = new List<KeyValuePair<string, string>>
        {
            new("Product", $"{r.ProductName} {r.ProductVersion}"),
            new("Product root", r.ProductRoot),
            new("Expected pipe", r.ExpectedPipe),
            new("Agent binary", r.AgentBinaryPath ?? "(not found)"),
            new("Agent version", h?.Version ?? dash),
            new("Agent pid", h is null ? dash : h.ProcessId.ToString()),
            new("Agent user", h?.UserName ?? dash),
            new("Machine", h?.MachineName ?? dash),
            new("Caller pid", h is null ? dash : h.ClientPid.ToString()),
            new("Launcher kind", h?.LauncherKind ?? dash),
            new("Launcher policy key", h?.LauncherPolicyKey ?? dash),
            new("Auto-approve eligible", h is null ? dash : (h.AutoApproveEligible ? "yes" : "no")),
        };
    }

    // ---- Hardened Tools (read-only; issue #116) ----

    private sealed record ToolCard(
        string Name, string Id, string Path, string Pill,
        System.Windows.Media.Brush PillBg, System.Windows.Media.Brush PillFg,
        string Note, Visibility NoteVisibility, System.Windows.Media.ImageSource? Icon);

    private bool _toolsBusy;

    private async Task ToolsRefreshAsync()
    {
        if (_toolsBusy)
            return;
        _toolsBusy = true;
        if (_currentPage == "tools")
            PagePrimaryButton.IsEnabled = false;
        ToolsList.ItemsSource = ToolCatalog.Tools
            .Select(t => MakeToolCard(t, "", "Checking...", PillKind.Muted, ""))
            .ToList();
        try
        {
            var statuses = await Task.Run(() =>
                ToolCatalog.Tools.Select(t => HardenedToolStatus.Probe(t.Id)).ToList()).ConfigureAwait(true);
            ToolsList.ItemsSource = ToolCatalog.Tools.Zip(statuses, (t, s) => s.State switch
            {
                HardenState.Hardened => MakeToolCard(t, s.PinnedPath ?? "", "Hardened", PillKind.Ok, s.Note ?? ""),
                HardenState.Degraded => MakeToolCard(t, s.PinnedPath ?? "", "Degraded", PillKind.Warn, s.Reason ?? ""),
                _ => MakeToolCard(t, "", "Not hardened", PillKind.Muted, $"Run cw harden {t.Id}."),
            }).ToList();
        }
        catch (Exception ex)
        {
            ToolsList.ItemsSource = new[]
            {
                MakeToolCard(new ToolCatalog.Entry("", "Error"), "-", "Error", PillKind.Danger,
                    VaultSecretFormValidation.SanitizeError(ex.Message)),
            };
        }
        finally
        {
            _toolsBusy = false;
            if (_currentPage == "tools")
                PagePrimaryButton.IsEnabled = true;
        }
    }

    private ToolCard MakeToolCard(ToolCatalog.Entry t, string path, string pill, PillKind kind, string note)
    {
        var (bg, fg) = PillBrushes(kind);
        return new ToolCard(t.DisplayName, t.Id, path, pill, bg, fg, note,
            note.Length == 0 ? Visibility.Collapsed : Visibility.Visible, BrandImages.ForTool(t.Id));
    }

    // ---- Secret Usage (read-only; issue #117) ----

    private sealed record UsageRow(
        string Time, string Launcher, string LauncherKind, string ToolClass, string Secret,
        string Pill, System.Windows.Media.Brush PillBg, System.Windows.Media.Brush PillFg, string Reason,
        System.Windows.Media.ImageSource? ToolIcon, System.Windows.Media.ImageSource? LauncherIcon,
        string AgentSays, Visibility AgentSaysVisibility);

    private const int UsageMaxRows = 200;
    private bool _usageBusy;

    private async Task UsageRefreshAsync()
    {
        if (_usageBusy)
            return;
        _usageBusy = true;
        if (_currentPage == "usage")
            PagePrimaryButton.IsEnabled = false;
        UsageErrorBanner.Visibility = Visibility.Collapsed;
        UsageFooter.Text = "Checking...";
        try
        {
            var page = await Task.Run(() => new AuditLog().ReadRecentRecords(UsageMaxRows)).ConfigureAwait(true);
            var now = DateTime.Now;
            UsageList.ItemsSource = page.Records.Select(r => MakeUsageRow(r, now)).ToList();
            var empty = page.Records.Count == 0;
            UsageEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            UsageScroll.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            var footer = empty ? "" : $"Showing newest {page.Records.Count} of the 30-day trail. Run cw audit -n <count> for more.";
            if (page.Skipped > 0)
                footer = (footer.Length == 0 ? "" : footer + " ") + $"{page.Skipped} entries could not be read.";
            UsageFooter.Text = footer;
        }
        catch (Exception ex)
        {
            UsageList.ItemsSource = null;
            UsageEmpty.Visibility = Visibility.Collapsed;
            UsageScroll.Visibility = Visibility.Collapsed;
            UsageFooter.Text = "";
            UsageErrorText.Text = "Could not read the audit trail: " + VaultSecretFormValidation.SanitizeError(ex.Message);
            UsageErrorBanner.Visibility = Visibility.Visible;
        }
        finally
        {
            _usageBusy = false;
            if (_currentPage == "usage")
                PagePrimaryButton.IsEnabled = true;
        }
    }

    private UsageRow MakeUsageRow(AuditGateRecord r, DateTime now)
    {
        var (pill, kind) = r.Decision switch
        {
            "auto-allow" => ("Auto-allow", PillKind.Ok),
            "allow-once" => ("Approved", PillKind.Warn),
            "session-grant" => ("Session granted", PillKind.Warn),
            "session-allow" => ("Session allow", PillKind.Ok),
            "deny" => ("Denied", PillKind.Danger),
            _ => (char.ToUpperInvariant(r.Decision[0]) + r.Decision[1..], PillKind.Danger),
        };
        var (bg, fg) = PillBrushes(kind);
        // #36: an agent account reads better than its SID key.
        var launcher = r.AgentAccount is { Length: > 0 } account ? account + " (agent account)"
            : r.LauncherPolicyKey.Length == 0 ? "-" : r.LauncherPolicyKey;
        var launcherKind = r.LauncherKind.Length > 0 && !string.Equals(r.LauncherKind, r.LauncherPolicyKey, StringComparison.Ordinal)
            ? r.LauncherKind
            : "";
        var toolClass = r.CommandClass.Length == 0 ? r.Tool : r.Tool + " \u00B7 " + r.CommandClass;
        var says = AgentReason.Clean(r.AgentReason);
        return new UsageRow(r.LocalTimeLabel(now), launcher, launcherKind, toolClass,
            string.IsNullOrEmpty(r.SecretName) ? "-" : r.SecretName, pill, bg, fg, r.ReasonCode ?? "",
            BrandImages.ForTool(r.Tool), BrandImages.ForLauncher(null, r.LauncherPath),
            says is null ? "" : $"{AgentReason.Label} \u201C{says}\u201D",
            says is null ? Visibility.Collapsed : Visibility.Visible);
    }

    // ---- Detectors (read-only, in-process scan; issue #118) ----

    private sealed record FindingCard(
        string Title, string Tool, string Pill,
        System.Windows.Media.Brush PillBg, System.Windows.Media.Brush PillFg,
        string Summary, string Evidence,
        string Remediation, Visibility RemediationVisibility,
        string Hint, Visibility HintVisibility, System.Windows.Media.ImageSource? Icon = null,
        string? Fix = null, Visibility FixVisibility = Visibility.Collapsed);

    private const string ScanScopeNote =
        " Scanned from the app's environment. Run cw scan in a terminal to check that session's variables.";

    private bool _detectorsBusy;

    private async Task DetectorsScanAsync()
    {
        if (_detectorsBusy)
            return;
        _detectorsBusy = true;
        if (_currentPage == "detectors")
            PagePrimaryButton.IsEnabled = false;
        DetectorsList.ItemsSource = null;
        DetectorsHeadlineSub.Visibility = Visibility.Collapsed;
        DetectorsHeadline.Text = "Scanning...";
        DetectorsHeadline.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush");
        try
        {
            var findings = await Task.Run(() => ScanEngine.Order(new ScanEngine().Run(new ScanContext()))).ConfigureAwait(true);
            var count = findings.Count(f => f.Id != SystemScanBannerDetector.FindingId);
            DetectorsHeadline.Text = count switch { 0 => "No findings", 1 => "1 finding", _ => $"{count} findings" };
            DetectorsHeadline.Foreground = (System.Windows.Media.Brush)FindResource(count == 0 ? "OnlineBrush" : "TextSecondaryBrush");
            DetectorsHeadlineSub.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
            DetectorsList.ItemsSource = findings.Select(MakeFindingCard).ToList();
            PageSubtitleText.Text = $"Last scan: {DateTime.Now:HH:mm}";
            PageSubtitleText.Visibility = _currentPage == "detectors" ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            DetectorsHeadline.Text = "Scan failed";
            DetectorsHeadline.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
            var (bg, fg) = PillBrushes(PillKind.Danger);
            DetectorsList.ItemsSource = new[]
            {
                new FindingCard("Scan error", "", "Error", bg, fg,
                    VaultSecretFormValidation.SanitizeError(ex.Message), "",
                    "", Visibility.Collapsed, "", Visibility.Collapsed),
            };
        }
        finally
        {
            _detectorsBusy = false;
            if (_currentPage == "detectors")
                PagePrimaryButton.IsEnabled = true;
        }
    }

    private FindingCard MakeFindingCard(ScanFinding f)
    {
        var (label, kind) = f.Severity switch
        {
            ScanSeverity.High => ("High", PillKind.Danger),
            ScanSeverity.Medium => ("Medium", PillKind.Warn),
            ScanSeverity.Low => ("Low", PillKind.Muted),
            _ => ("Info", PillKind.Info),
        };
        var (bg, fg) = PillBrushes(kind);
        var summary = f.Id == SystemScanBannerDetector.FindingId ? f.Summary + ScanScopeNote : f.Summary;
        var remediation = f.Remediation ?? "";
        var hint = string.IsNullOrWhiteSpace(f.HardenHint) ? "" : $"Run {f.HardenHint}.";
        var canMove = McpSecretLocation.FromFix(f.Fix) is not null;
        return new FindingCard(f.Title, f.Tool, label, bg, fg, summary, f.Evidence,
            remediation, remediation.Length == 0 ? Visibility.Collapsed : Visibility.Visible,
            canMove ? "" : hint, !canMove && hint.Length > 0 ? Visibility.Visible : Visibility.Collapsed, BrandImages.ForTool(f.Tool),
            f.Fix, canMove ? Visibility.Visible : Visibility.Collapsed);
    }

    /// <summary>#28: save the value in the vault, put a placeholder in the file, start the server through cw inject.</summary>
    private async void MoveToVault_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string fix } button || McpSecretLocation.FromFix(fix) is not { } location)
            return;
        button.IsEnabled = false;
        try
        {
            var name = await Task.Run(() => McpSecretMover.Move(location, new CredentialVault(), CwCommand())).ConfigureAwait(true);
            MessageBox.Show(this,
                $"{name} is now in the vault. {Path.GetFileName(location.File)} holds a placeholder, and the server starts through cw inject. Restart the harness.",
                WindowTitle, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Move to vault failed: " + VaultSecretFormValidation.SanitizeError(ex.Message),
                WindowTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        await DetectorsScanAsync().ConfigureAwait(true);
    }

    /// <summary>cw on PATH (the dotnet tool shim), else the bare name for the harness to find.</summary>
    private static IReadOnlyList<string> CwCommand()
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "cw.exe");
                if (File.Exists(candidate))
                    return [Path.GetFullPath(candidate)];
            }
            catch (ArgumentException)
            {
                // Bad PATH entry.
            }
        }
        return ["cw"];
    }

    // ---- Secret Gates (read-only policy browser; issue #119) ----

    private sealed record LevelRow(
        string Label, string Pill, System.Windows.Media.Brush PillBg, System.Windows.Media.Brush PillFg,
        string Tip, string Note, Visibility NoteVisibility, System.Windows.Media.ImageSource? Icon = null);

    private sealed record LauncherCard(
        string Key, string Kind, System.Windows.Media.Brush KindBg, System.Windows.Media.Brush KindFg,
        string Path, string FullPath, Visibility PathVisibility, IReadOnlyList<LevelRow> Tools, string Hint,
        System.Windows.Media.ImageSource? Icon);

    private bool _gatesBusy;

    private async Task GatesRefreshAsync()
    {
        if (_gatesBusy)
            return;
        _gatesBusy = true;
        if (_currentPage == "gates")
            PagePrimaryButton.IsEnabled = false;
        GatesErrorBanner.Visibility = Visibility.Collapsed;
        GatesEmpty.Visibility = Visibility.Collapsed;
        GatesList.ItemsSource = null;
        GatesDefaultsCard.Visibility = Visibility.Visible;
        GatesDefaults.ItemsSource = new[] { CheckingRow("AI Harness"), CheckingRow("Terminal") };
        try
        {
            var m = await Task.Run(() => PolicyReadModel.Load()).ConfigureAwait(true);
            GatesDefaults.ItemsSource = new[]
            {
                MakeLevelRow("AI Harness", m.AiHarnessDefault, LevelMatrix(m.AiHarnessDefault)),
                MakeLevelRow("Terminal", m.TerminalDefault, LevelMatrix(m.TerminalDefault)),
            };
            GatesList.ItemsSource = m.Launchers.Select(MakeLauncherCard).ToList();
            GatesEmpty.Visibility = m.Launchers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            GatesSessionsCard.Visibility = Visibility.Visible;
            await GatesSessionsRefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // No silent fallback: the agent fails closed on this same file, so hide the cards.
            GatesDefaultsCard.Visibility = Visibility.Collapsed;
            GatesSessionsCard.Visibility = Visibility.Collapsed;
            GatesErrorText.Text = "Policy file could not be read. The Session Agent fails closed on the same file.\n"
                + VaultSecretFormValidation.SanitizeError(ex.Message) + "\n" + PolicyStore.DefaultPath();
            GatesErrorBanner.Visibility = Visibility.Visible;
        }
        finally
        {
            _gatesBusy = false;
            if (_currentPage == "gates")
                PagePrimaryButton.IsEnabled = true;
        }
    }

    private sealed record SessionRow(string Id, string Scope, string Times);

    // Read-only mirror of `cw policy sessions` (#135). Off the UI thread, no timer;
    // driven by the same GatesRefreshAsync as the policy cards.
    private async Task GatesSessionsRefreshAsync()
    {
        GatesSessions.ItemsSource = null;
        GatesSessionsStatus.Text = "Checking...";
        GatesSessionsStatus.Visibility = Visibility.Visible;
        try
        {
            var rows = await AgentSessionsClient.ListAsync().ConfigureAwait(true);
            var cards = rows.Select(r => new SessionRow(
                r.Id,
                $"{r.LauncherPolicyKey} ({r.LauncherKind})  ·  pid {r.Pid}  ·  {r.Tool} / {r.SecretName}  ·  {r.CommandClass}",
                $"granted {SessionAllowDisplay.LocalTime(r.GrantedAtUtc)}   ·   "
                    + $"last used {SessionAllowDisplay.LocalTime(r.LastUsedUtc)}   ·   "
                    + $"expires {SessionAllowDisplay.LocalTime(r.IdleExpiresUtc)}")).ToList();
            GatesSessions.ItemsSource = cards;
            GatesSessionsStatus.Text = "No active session allows";
            GatesSessionsStatus.Visibility = cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception)
        {
            // Grants live only in the agent's memory; if it is unreachable there is nothing to show.
            GatesSessions.ItemsSource = null;
            GatesSessionsStatus.Text = "-";
            GatesSessionsStatus.Visibility = Visibility.Visible;
        }
    }

    private LevelRow CheckingRow(string label)
    {
        var (bg, fg) = PillBrushes(PillKind.Muted);
        return new LevelRow(label, "Checking...", bg, fg, "", "", Visibility.Collapsed);
    }

    private LevelRow MakeLevelRow(string label, PolicyLevel level, string note, string? tool = null)
    {
        var kind = level switch
        {
            PolicyLevel.Full => PillKind.Ok,
            PolicyLevel.Trusted => PillKind.Warn,
            PolicyLevel.Read => PillKind.Info,
            _ => PillKind.Danger,
        };
        var (bg, fg) = PillBrushes(kind);
        return new LevelRow(label, PolicyLevelNames.Format(level), bg, fg, LevelMatrix(level), note,
            note.Length == 0 ? Visibility.Collapsed : Visibility.Visible, BrandImages.ForTool(tool));
    }

    private LauncherCard MakeLauncherCard(PolicyLauncherEntry l)
    {
        var (kindLabel, kindPill) = l.Kind switch
        {
            LauncherEnrollmentKind.AiHarness => ("AI Harness", PillKind.Info),
            LauncherEnrollmentKind.Terminal => ("Terminal", PillKind.Muted),
            _ => ("Unknown", PillKind.Muted),
        };
        var (kbg, kfg) = PillBrushes(kindPill);
        var names = ToolCatalog.Tools.ToDictionary(t => t.Id, t => t.DisplayName);
        var rows = l.Tools
            .Select(t => MakeLevelRow(names.GetValueOrDefault(t.Tool, t.Tool), t.Level, t.IsOverride ? "" : "(kind default)", t.Tool))
            .ToList();
        var full = l.DisplayPath ?? "";
        return new LauncherCard(l.PolicyKey, kindLabel, kbg, kfg, LeftTruncate(full), full,
            full.Length == 0 ? Visibility.Collapsed : Visibility.Visible, rows,
            $"Override a tool: cw policy set {l.PolicyKey} <tool> <Deny|Read|Trusted|Full>",
            BrandImages.ForLauncher(null, l.DisplayPath));
    }

    // ponytail: fixed character budget with the full path in the tooltip; width-aware trimming if it ever looks off.
    private static string LeftTruncate(string path, int max = 64) =>
        path.Length <= max ? path : "…" + path[^(max - 1)..];

    /// <summary>Class matrix for one level, derived from the evaluator so the text cannot drift from enforcement.</summary>
    private static string LevelMatrix(PolicyLevel level)
    {
        var all = new[] { CommandClass.Read, CommandClass.Write, CommandClass.SecretReveal, CommandClass.Unknown };
        var allowed = all.Where(c => PolicyEvaluator.IsAutoAllowed(level, c)).Select(CommandClassNames.Format).ToList();
        var gated = all.Where(c => !PolicyEvaluator.IsAutoAllowed(level, c)).Select(CommandClassNames.Format).ToList();
        var auto = allowed.Count == 0 ? "auto-allow: none" : "auto-allow: " + string.Join(", ", allowed);
        return gated.Count == 0 ? auto : auto + " / Approval Gate: " + string.Join(", ", gated);
    }

    private enum PillKind { Ok, Warn, Danger, Muted, Info }

    private (System.Windows.Media.Brush Bg, System.Windows.Media.Brush Fg) PillBrushes(PillKind kind)
    {
        var (bg, fg) = kind switch
        {
            PillKind.Ok => ("OnlineDimBrush", "OnlineBrush"),
            PillKind.Warn => ("WarnDimBrush", "WarnBrush"),
            PillKind.Danger => ("DangerDimBrush", "DangerBrush"),
            PillKind.Info => ("AccentDimBrush", "AccentBrush"),
            _ => ("MutedPillBrush", "TextSecondaryBrush"),
        };
        return ((System.Windows.Media.Brush)FindResource(bg), (System.Windows.Media.Brush)FindResource(fg));
    }

    private void SetPill(Border pill, TextBlock text, string label, PillKind kind)
    {
        var (bg, fg) = PillBrushes(kind);
        pill.Background = bg;
        text.Foreground = fg;
        text.Text = label;
    }
}
