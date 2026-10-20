<#
.SYNOPSIS
    Captures the main screens with every resource string pseudo-localized (#39), to find clipping and text that does
    not come from the resource files.

.DESCRIPTION
    Runs the Debug app with --pseudo-loc, which makes every string from the .resx files about 40 % longer, accented and
    bracketed ("[Åðð þĥöţöš !]"); text that stays plain English is not in a resource file. The app is driven through UI
    Automation patterns on its own window and Debug launch switches only (no synthetic input), and each screen is
    captured from the app's own window with PrintWindow, into %LOCALAPPDATA%\Temp\claude\<Branch>\pseudo:

        01 first launch, with the missing-extension InfoBar and its Store button (--missing-codec)
        02 Settings
        03 the skipped-photos InfoBar of an import of the broken test photos, and 04 its list
        05 editing, wide; 06 the inspector scrolled to Output
        07-09 narrow (900 px): the Timing, Frame and Output tabs; 10 the smallest window (640 x 480)
        11 the export panel; 12 the close-during-export question; 13 the finished state; 14 finished, narrow
        15 the New project question at the smallest window
        16 the drop question (--motion-drop onto a project with photos), 17 the same at the smallest window

    Pseudo mode also sets a German regional format (#114), so a number written with the invariant culture (a dot
    decimal, "1,240") stands out next to the localized ones ("1.240", "24,7 s"). Not captured, because they need what
    UI Automation cannot give without real input or real files: the tooltips (a hover) and the OneDrive question (files
    on demand). Their texts are in the same resource files and pass the same tests.

    The export is fast on a GPU encoder: when it has finished before the close step, 12 is skipped and the run goes
    on, and any capture after the app has exited is skipped too. The end lists what was skipped and why.

    -Plain captures the same screens in English (no --pseudo-loc) into ...\plain, to compare with them.

    Build the app Debug x64 first. It prints the logs' warnings and errors at the end; "The preview could not read"
    for the broken test photos is expected. -Clean deletes the branch folder afterwards (screenshots included).

.EXAMPLE
    pwsh -File scripts/pseudo-check.ps1 -Branch feat-39-strings
#>
param(
    [string]$Branch = 'feat-39-strings',
    [int]$ExportPhotos = 400,
    [switch]$Plain,
    [switch]$Clean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'src/TenLapse.App/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/TenLapse.App.exe'
if (-not (Test-Path $exe)) { throw "Build the app Debug x64 first: $exe" }

$base = Join-Path $env:LOCALAPPDATA "Temp\claude\$Branch"
$shots = Join-Path $base $(if ($Plain) { 'plain' } else { 'pseudo' })
$samples = Join-Path $base 'samples'
$broken = Join-Path $base 'broken-photos'
New-Item -ItemType Directory -Force $base, $shots, $samples, $broken | Out-Null
foreach ($name in 'empty', 'editing', 'drop', 'export') { Remove-Item (Join-Path $base "logs-$name") -Recurse -Force -ErrorAction SilentlyContinue }
Copy-Item (Join-Path $root 'tests/TenLapse.Tests/Fixtures/Photos/*') $broken -Force

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing.Common, System.Drawing.Primitives, System.Private.Windows.GdiPlus, System.Private.Windows.Core -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class PseudoCapture
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    // The window's own pixels (PW_RENDERFULLCONTENT), even when another window covers it.
    public static void Save(IntPtr window, string path)
    {
        SetThreadDpiAwarenessContext(new IntPtr(-4));
        Rect rect;
        GetWindowRect(window, out rect);
        using (var bitmap = new Bitmap(rect.Right - rect.Left, rect.Bottom - rect.Top))
        {
            using (var graphics = Graphics.FromImage(bitmap))
            {
                var dc = graphics.GetHdc();
                PrintWindow(window, dc, 2);
                graphics.ReleaseHdc(dc);
            }

            bitmap.Save(path, ImageFormat.Png);
        }
    }
}
'@

$AE = [System.Windows.Automation.AutomationElement]
$Tree = [System.Windows.Automation.TreeScope]

function Common([string]$name) {
    @(if (-not $Plain) { '--pseudo-loc' }) + @(
        "--settings-folder=$(Join-Path $base "settings-$name")",
        "--cache-folder=$(Join-Path $base "cache-$name")",
        "--log-folder=$(Join-Path $base "logs-$name")",
        "--encoder-cache=$(Join-Path $base 'encoder-cache.json')",
        "--sample-folder=$samples",
        '--sample-clean'
    )
}

function Start-App([string[]]$appArgs) {
    $quoted = $appArgs | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
    $p = Start-Process -FilePath $exe -ArgumentList $quoted -PassThru
    $window = $null
    $until = (Get-Date).AddSeconds(30)
    while (-not $window -and (Get-Date) -lt $until) {
        Start-Sleep -Milliseconds 300
        $condition = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)
        $window = $AE::RootElement.FindFirst($Tree::Children, $condition)
    }
    if (-not $window) { Stop-Process -Id $p.Id -Force; throw 'The window did not appear.' }
    @{ Process = $p; Window = $window }
}

function Stop-App($app) {
    if (-not $app.Process.HasExited) { Stop-Process -Id $app.Process.Id -Force; $app.Process.WaitForExit() }
}

$skipped = [System.Collections.Generic.List[string]]::new()

function Skip([string]$name, [string]$why) {
    $skipped.Add("$name ($why)")
    Write-Host "  skipped $name`: $why"
}

function Shot($app, [string]$name) {
    Start-Sleep -Milliseconds 700
    if ($app.Process.HasExited) { Skip $name 'the app has exited'; return }
    $path = Join-Path $shots "$name.png"
    try { [PseudoCapture]::Save([IntPtr]$app.Window.Current.NativeWindowHandle, $path) }
    catch { if ($app.Process.HasExited) { Skip $name 'the app exited during the capture'; return } else { throw } }
    Write-Host "  captured $name"
}

# The first element on screen with this automation id (x:Name), waiting up to $seconds.
function Find-Id($app, [string]$id, [double]$seconds = 10) {
    $until = (Get-Date).AddSeconds($seconds)
    do {
        if ($app.Process.HasExited) { return $null }
        $condition = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
        foreach ($element in @($app.Window.FindAll($Tree::Descendants, $condition))) {
            if (-not $element.Current.IsOffscreen) { return $element }
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $until)
    Write-Host "  not found: $id"
    $null
}

function Invoke-Element($element) {
    if ($element) { $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
}

function Resize($app, [double]$width, [double]$height) {
    $transform = $null
    if ($app.Process.HasExited) { return }
    if ($app.Window.TryGetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern, [ref]$transform) -and $transform.Current.CanResize) {
        $transform.Resize($width, $height)
        Start-Sleep -Milliseconds 800
    }
}

# 1: first launch and Settings.
Write-Host 'Run: first launch'
$app = Start-App ((Common 'empty') + '--missing-codec=heif')
try {
    Start-Sleep -Seconds 3
    Shot $app '01-first-launch'
    Invoke-Element (Find-Id $app 'SettingsButton')
    Start-Sleep -Seconds 1
    Shot $app '02-settings'
}
finally { Stop-App $app }

# 2: an import of the broken test photos, editing wide and narrow.
Write-Host 'Run: import and editing'
$app = Start-App ((Common 'editing') + "--motion-import=$broken" + '--motion-import-delay=1')
try {
    $bar = Find-Id $app 'ShellInfoBar' 30
    Start-Sleep -Seconds 1
    Shot $app '03-skipped-infobar'
    if ($bar) {
        $buttons = $bar.FindAll($Tree::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
        $toggle = @($buttons) | Where-Object { $_.Current.AutomationId -ne 'CloseButton' } | Select-Object -First 1
        Invoke-Element $toggle
        Shot $app '04-skipped-list'
        Invoke-Element (@($buttons) | Where-Object { $_.Current.AutomationId -eq 'CloseButton' } | Select-Object -First 1)
    }
    Resize $app 1280 800
    Start-Sleep -Seconds 1
    Shot $app '05-editing-wide'
    $scroll = Find-Id $app 'InspectorScroll' 2
    if ($scroll) {
        $scroll.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(-1, 100)
        Shot $app '06-editing-wide-output'
    }
    Resize $app 900 760
    $tabs = Find-Id $app 'InspectorTabsControl' 3
    $radios = if ($tabs) { @($tabs.FindAll($Tree::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::RadioButton)))) } else { @() }
    $names = '07-narrow-timing', '08-narrow-frame', '09-narrow-output'
    for ($i = 0; $i -lt [Math]::Min($radios.Count, 3); $i++) {
        $radios[$i].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Shot $app $names[$i]
    }
    Resize $app 640 480
    Shot $app '10-smallest-window'

    # The New project question, its two buttons at the smallest window; Cancel (the dialog's close button) keeps the project.
    Invoke-Element (Find-Id $app 'NewProjectButton' 3)
    if (Find-Id $app 'PrimaryButton' 5) {
        Shot $app '15-new-project-question'
        Invoke-Element (Find-Id $app 'CloseButton' 2)
    }
    else { Skip '15-new-project-question' 'the question did not open' }
}
finally { Stop-App $app }

# 2b: the drop question, three buttons sharing the dialog's width, wide and at the smallest window.
Write-Host 'Run: drop question'
$app = Start-App ((Common 'drop') + '--sample-frames=12' + "--motion-drop=$broken" + '--motion-drop-delay=8')
try {
    Resize $app 1280 800
    if (Find-Id $app 'SecondaryButton' 40) {
        Shot $app '16-drop-question'
        Resize $app 640 480
        Shot $app '17-drop-question-smallest'
        Invoke-Element (Find-Id $app 'CloseButton' 2)
    }
    else { Skip '16-drop-question' 'the question did not open' }
}
finally { Stop-App $app }

# 3: the export panel, the close question, the finished state.
Write-Host 'Run: export'
$export = Join-Path $base 'pseudo-export.mp4'
Remove-Item $export -ErrorAction SilentlyContinue
$app = Start-App ((Common 'export') + "--sample-frames=$ExportPhotos" + '--output=2160' + "--export-to=$export")
try {
    Resize $app 1280 800
    $cancel = Find-Id $app 'CancelButton' 240
    Start-Sleep -Seconds 1
    Shot $app '11-export-panel'
    # The close question only comes while the export runs; a finished export just closes the app.
    if (-not $app.Process.HasExited -and (Find-Id $app 'CancelButton' 0.5)) {
        $windowPattern = $app.Window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
        $windowPattern.Close()
        $keep = Find-Id $app 'CloseButton' 5
        Shot $app '12-close-question'
        Invoke-Element $keep
    }
    else { Skip '12-close-question' 'the export had finished before the close step' }
    $play = Find-Id $app 'PlayButton' 240
    Start-Sleep -Seconds 2
    Shot $app '13-finished'
    Resize $app 900 760
    Shot $app '14-finished-narrow'
}
finally { Stop-App $app }

Write-Host "Screenshots in $shots"
if ($skipped.Count -gt 0) { Write-Host "Skipped: $($skipped -join '; ')" }
foreach ($name in 'empty', 'editing', 'drop', 'export') {
    Get-ChildItem (Join-Path $base "logs-$name") -Filter '10Lapse-*.log' -ErrorAction SilentlyContinue |
        ForEach-Object { Select-String -Path $_.FullName -Pattern ' (WARN|ERROR|CRIT) ' | ForEach-Object { "$name`: $($_.Line)" } }
}

if ($Clean) {
    Remove-Item $base -Recurse -Force
    Write-Host "Removed $base"
}
