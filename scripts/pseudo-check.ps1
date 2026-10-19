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
foreach ($name in 'empty', 'editing', 'export') { Remove-Item (Join-Path $base "logs-$name") -Recurse -Force -ErrorAction SilentlyContinue }
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

function Shot($app, [string]$name) {
    Start-Sleep -Milliseconds 700
    $path = Join-Path $shots "$name.png"
    [PseudoCapture]::Save([IntPtr]$app.Window.Current.NativeWindowHandle, $path)
    Write-Host "  captured $name"
}

# The first element on screen with this automation id (x:Name), waiting up to $seconds.
function Find-Id($app, [string]$id, [double]$seconds = 10) {
    $until = (Get-Date).AddSeconds($seconds)
    do {
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
    $windowPattern = $app.Window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
    $windowPattern.Close()
    $keep = Find-Id $app 'CloseButton' 5
    Shot $app '12-close-question'
    Invoke-Element $keep
    $play = Find-Id $app 'PlayButton' 240
    Start-Sleep -Seconds 2
    Shot $app '13-finished'
    Resize $app 900 760
    Shot $app '14-finished-narrow'
}
finally { Stop-App $app }

Write-Host "Screenshots in $shots"
foreach ($name in 'empty', 'editing', 'export') {
    Get-ChildItem (Join-Path $base "logs-$name") -Filter '10Lapse-*.log' -ErrorAction SilentlyContinue |
        ForEach-Object { Select-String -Path $_.FullName -Pattern ' (WARN|ERROR|CRIT) ' | ForEach-Object { "$name`: $($_.Line)" } }
}

if ($Clean) {
    Remove-Item $base -Recurse -Force
    Write-Host "Removed $base"
}
