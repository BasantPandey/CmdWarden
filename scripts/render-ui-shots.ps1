<#
.SYNOPSIS
Render the Approval Gate and CmdWarden Vault screenshots for the docs.

.DESCRIPTION
Run: pwsh scripts/render-ui-shots.ps1
Writes docs/images/approval-gate.png and docs/images/vault-*.png, including the Enroll and Set level dialogs.

The script uses demo data in %LOCALAPPDATA%\CmdWarden-docs and a private pipe.
Your real policy, audit trail, and agent stay as they are.
The Secrets page lists the real names in Windows Credential Manager, never values.
The script adds DEMO_TOKEN for the dialog shots and removes it at the end.
It never confirms a delete.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$out = Join-Path $repo "docs\images"
$root = Join-Path $env:LOCALAPPDATA "CmdWarden-docs"
$pipe = "CmdWarden-docs"

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Shot {
    [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Rt, B; }
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int hh, uint f);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out R r, int size);
    [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, int f, int e);
    // Windows lets a process take the focus only after a key press. An Alt tap counts.
    public static void Focus(IntPtr h) { keybd_event(0x12, 0, 0, 0); SetForegroundWindow(h); keybd_event(0x12, 0, 2, 0); }
}
"@
# Physical pixels, so window sizes and captures match the screen.
[void][Shot]::SetProcessDpiAwarenessContext([IntPtr]-4)

$bin = "bin\$Configuration"
$agentDll = Join-Path $repo "src\CmdWarden.Agent\$bin\net10.0\CmdWarden.Agent.dll"
$cwDll = Join-Path $repo "src\CmdWarden.Cli\$bin\net10.0\cw.dll"
$vaultExe = Join-Path $repo "src\CmdWarden.SecretsManager\$bin\net10.0-windows\CmdWarden.SecretsManager.exe"
$gateExe = Join-Path $repo "src\CmdWarden.ApprovalGate\$bin\net10.0-windows\CmdWarden.ApprovalGate.exe"
$contractsDll = Join-Path $repo "src\CmdWarden.Contracts\$bin\net10.0\CmdWarden.Contracts.dll"

foreach ($proj in "CmdWarden.Agent", "CmdWarden.Cli", "CmdWarden.SecretsManager", "CmdWarden.ApprovalGate") {
    dotnet build (Join-Path $repo "src\$proj") -c $Configuration -nologo -v q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $proj" }
}
Add-Type -Path $contractsDll

function Save-Window([IntPtr]$hwnd, [string]$name) {
    $r = New-Object Shot+R
    [void][Shot]::GetWindowRect($hwnd, [ref]$r)
    # 9 = DWMWA_EXTENDED_FRAME_BOUNDS: the visible frame, without the hidden resize border.
    $f = New-Object Shot+R
    [void][Shot]::DwmGetWindowAttribute($hwnd, 9, [ref]$f, 16)
    $full = New-Object System.Drawing.Bitmap ($r.Rt - $r.L), ($r.B - $r.T)
    $g = [System.Drawing.Graphics]::FromImage($full)
    $dc = $g.GetHdc()
    # 2 = PW_RENDERFULLCONTENT: capture a WPF window even when another window covers it.
    [void][Shot]::PrintWindow($hwnd, $dc, 2)
    $g.ReleaseHdc($dc)
    $g.Dispose()
    $crop = New-Object System.Drawing.Rectangle ($f.L - $r.L), ($f.T - $r.T), ($f.Rt - $f.L), ($f.B - $f.T)
    $bmp = $full.Clone($crop, $full.PixelFormat)
    $full.Dispose()
    $path = Join-Path $out $name
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "wrote $path"
}

function Wait-Window([int]$processId, [string]$title, $owner = $null) {
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $title)))
    for ($i = 0; $i -lt 100; $i++) {
        $w = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
        if (-not $w -and $owner) { $w = $owner.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond) }
        if ($w) { return $w }
        Start-Sleep -Milliseconds 100
    }
    throw "Window '$title' did not open."
}

function Find-Id($parent, [string]$id) {
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Invoke-Id($parent, [string]$id) {
    $el = Find-Id $parent $id
    if (-not $el) { throw "No element '$id'." }
    $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Send-Key([IntPtr]$hwnd, [int]$vk) {
    [void][Shot]::PostMessage($hwnd, 0x100, [IntPtr]$vk, [IntPtr]0)
    [void][Shot]::PostMessage($hwnd, 0x101, [IntPtr]$vk, [IntPtr]0xC0000000)
}

function Hwnd($el) { [IntPtr]$el.Current.NativeWindowHandle }

# ---- demo data ----
if (Test-Path $root) { Remove-Item -Recurse -Force $root }
New-Item -ItemType Directory -Force $root | Out-Null
$policyPath = Join-Path $root "policy.json"

$claude = "auth:sha1:0d7581d2c51c59df686c3000c70bf543f9f6c6cb"
$codex = "pathhash:sha256:9f2c4e1ab7d05e3c8a41f6b2d9e07c13"
$cursor = "auth:sha1:5be1a0c4f2e94d0b8c6a3f1e7d2b9c4a8e6f0d13"
$terminal = "auth:sha1:ab172913a2960a2248c1f5e0d3b7a9c6e4f2d815"
$claudePath = "C:\Users\dev\.local\bin\claude.exe"
$terminalPath = (Get-Command pwsh).Source

$store = New-Object CmdWarden.Contracts.PolicyStore($policyPath)
[void]$store.Load()
$store.Enroll($claude, [CmdWarden.Contracts.LauncherEnrollmentKind]::AiHarness, $claudePath)
$store.SetLevel($claude, "gh", [CmdWarden.Contracts.PolicyLevel]::Trusted)
$store.Enroll($terminal, [CmdWarden.Contracts.LauncherEnrollmentKind]::Terminal, $terminalPath)
$store.Save()

$audit = New-Object CmdWarden.Contracts.AuditLog($root)
$now = [DateTime]::UtcNow
$rows = @(
    @(42, $terminal, "Terminal", $terminalPath, "docker", "write", "auto-allow", $null, "DOCKER_TOKEN"),
    @(35, $cursor, "AiHarness", "C:\Users\dev\AppData\Local\Programs\cursor\Cursor.exe", "git", "read", "auto-allow", $null, $null),
    @(28, $cursor, "AiHarness", "C:\Users\dev\AppData\Local\Programs\cursor\Cursor.exe", "git", "write", "session-grant", $null, "GIT_TOKEN"),
    @(21, $codex, "AiHarness", "C:\Users\dev\AppData\Roaming\npm\codex.cmd", "az", "write", "deny", "UserDenied", "AZURE_SP"),
    @(14, $claude, "AiHarness", $claudePath, "gh", "read", "auto-allow", $null, "GH_TOKEN"),
    @(7, $claude, "AiHarness", $claudePath, "docker", "secret-reveal", "deny", "UserDenied", "DOCKER_TOKEN"),
    @(0, $claude, "AiHarness", $claudePath, "gh", "write", "allow-once", $null, "GH_TOKEN", "create the release PR")
)
foreach ($r in $rows) {
    $audit.AppendGateDecision([CmdWarden.Contracts.AuditGateRecord]@{
        Ts = $now.AddMinutes(-$r[0]).ToString("o")
        LauncherPolicyKey = $r[1]
        LauncherKind = $r[2]
        EnrollmentKind = $r[2]
        LauncherPath = $r[3]
        Tool = $r[4]
        CommandClass = $r[5]
        Decision = $r[6]
        ReasonCode = $r[7]
        SecretName = $r[8]
        AgentReason = $r[9]
        PolicyLevel = "Read"
    })
}

# ---- approval gate ----
$payload = Join-Path $root "gate.json"
@{
    windowTitle = "CmdWarden"; launcherDisplayName = "Claude Code"; subtitle = "wants to run"
    commandLine = "gh pr create --fill"; toolPath = "C:\Program Files\GitHub CLI\gh.exe"
    agentReason = "create the release PR"
    workingDirectory = "C:\src\my-app"; secretNames = @("GH_TOKEN")
    reasonHeading = "GH_TOKEN requested"; reasonLine = "gh needs GH_TOKEN"
    enrollmentKind = "AiHarness"; identityKind = "Authenticode"; launcherPath = $claudePath
    policyLevel = "Read"; commandClass = "write"; policyKey = $claude
    requestedAt = $now.ToString("o"); tool = "gh"; sessionAllowOffered = $true
    sessionScopeLine = "Approve Once covers write commands until Claude Code (pid 15188) exits. Allow for session ends at the time you choose, or when the launcher exits."
} | ConvertTo-Json | Set-Content -Encoding utf8 $payload

$gate = Start-Process $gateExe "--payload `"$payload`"" -PassThru
try {
    $w = Wait-Window $gate.Id "CmdWarden"
    Start-Sleep -Milliseconds 800
    Save-Window (Hwnd $w) "approval-gate.png"
}
finally {
    if (-not $gate.HasExited) { $gate.Kill() }
}

# ---- vault ----
$env:CW_PIPE_NAME = $pipe
$env:CW_PRODUCT_ROOT = $root
$env:CW_POLICY_PATH = $policyPath
$env:CW_APPROVAL_MODE = "off"
$agent = Start-Process dotnet "`"$agentDll`"" -PassThru -WindowStyle Hidden
$vault = $null
try {
    for ($i = 0; $i -lt 50; $i++) {
        & dotnet $cwDll doctor *> $null
        if ($LASTEXITCODE -eq 0) { break }
        Start-Sleep -Milliseconds 300
    }
    "demo-value" | & dotnet $cwDll save DEMO_TOKEN | Out-Null

    $vault = Start-Process $vaultExe -PassThru
    $w = Wait-Window $vault.Id "CmdWarden Vault"
    $hwnd = Hwnd $w
    # 1561 x 972 physical pixels: the size of the earlier shots.
    [void][Shot]::SetWindowPos($hwnd, [IntPtr]::Zero, 40, 40, 1561, 972, 0x0044)
    Start-Sleep -Seconds 2

    $pages = [ordered]@{
        NavGates = "vault-secret-gates.png"
        NavDetectors = "vault-detectors.png"
        NavTools = "vault-hardened-tools.png"
        NavUsage = "vault-secret-usage.png"
        NavDoctor = "vault-doctor.png"
    }
    foreach ($nav in $pages.Keys) {
        Invoke-Id $w $nav
        Start-Sleep -Seconds 3
        Save-Window $hwnd $pages[$nav]
    }

    # Enroll and Set level dialogs. Both close unsaved: the demo policy stays as it is.
    Invoke-Id $w "NavGates"
    Start-Sleep -Seconds 2
    Invoke-Id $w "PagePrimaryButton"
    $enroll = Wait-Window $vault.Id "Enroll launcher" $w
    $seen = $enroll.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::RadioButton)))
    $seen.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 400
    Save-Window (Hwnd $enroll) "vault-enroll-launcher.png"
    $enroll.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Start-Sleep -Milliseconds 400
    $pill = $w.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "Set the GitHub CLI level")))
    $pill.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $set = Wait-Window $vault.Id "Set level" $w
    Start-Sleep -Milliseconds 400
    Save-Window (Hwnd $set) "vault-set-level.png"
    $set.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Start-Sleep -Milliseconds 400

    Invoke-Id $w "NavSecrets"
    Start-Sleep -Seconds 2
    [Shot]::Focus($hwnd)
    # Down selects the first card, so Delete shows its Del key.
    Send-Key $hwnd 0x28
    Start-Sleep -Milliseconds 600
    Save-Window $hwnd "vault-secrets.png"

    # Ctrl+N opens Add secret. The shot shows the Esc and Enter keys, then the dialog closes unsaved.
    Invoke-Id $w "AddSecretButton"
    $add = Wait-Window $vault.Id "Add secret" $w
    $nameBox = Find-Id $add "NameBox"
    $nameBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue("NPM_TOKEN")
    (Find-Id $add "ValueBox").SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait("demo")
    Start-Sleep -Milliseconds 400
    Save-Window (Hwnd $add) "vault-add-secret.png"
    $add.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Start-Sleep -Milliseconds 400

    # Select DEMO_TOKEN and press Del. The dialog closes without a choice: nothing is deleted.
    Invoke-Id $w "RefreshButton"
    Start-Sleep -Seconds 1
    [Shot]::Focus($hwnd)
    $demoText = $w.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "DEMO_TOKEN")))
    if (-not $demoText) { throw "DEMO_TOKEN card not found." }
    $demoDelete = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($demoText).FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
    # Esc clears the selection. Then Down moves one card at a time until the DEMO_TOKEN Delete button is on.
    Send-Key $hwnd 0x1B
    for ($i = 0; $i -lt 20 -and -not $demoDelete.Current.IsEnabled; $i++) {
        Send-Key $hwnd 0x28
        Start-Sleep -Milliseconds 300
    }
    if (-not $demoDelete.Current.IsEnabled) { throw "Could not select DEMO_TOKEN." }
    Send-Key $hwnd 0x2E
    $confirm = Wait-Window $vault.Id "Delete secret" $w
    Start-Sleep -Milliseconds 400
    Save-Window (Hwnd $confirm) "vault-delete-secret.png"
    $confirm.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
}
finally {
    if ($vault -and -not $vault.HasExited) { $vault.Kill() }
    & dotnet $cwDll delete DEMO_TOKEN *> $null
    if (-not $agent.HasExited) { $agent.Kill($true) }
    foreach ($v in "CW_PIPE_NAME", "CW_PRODUCT_ROOT", "CW_POLICY_PATH", "CW_APPROVAL_MODE") {
        Remove-Item "Env:$v" -ErrorAction SilentlyContinue
    }
}
