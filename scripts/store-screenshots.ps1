<#
.SYNOPSIS
    Makes the Microsoft Store screenshots for the 3.0 listing (#42) from the Debug app's own window.

.DESCRIPTION
    Starts the Debug app once per screenshot with fresh settings, sets its client area to the Store size through UI
    Automation (TransformPattern.Resize on its own window), drives it only through UI Automation patterns and Debug launch
    switches (no synthetic input, no Windows setting changed), and captures the client area with PrintWindow
    (PW_CLIENTONLY | PW_RENDERFULLCONTENT), so neither the desktop nor another window can show in the picture. Each
    PNG is checked for its exact size before it is kept.

        01-first-launch     the start screen, dark
        02-editing          a sequence imported, playhead in the golden part, 4K output, dark
        03-vertical         the same set framed 9:16 for a phone, light
        04-exporting        the export running (the film gate, the percent, time left), dark
        05-finished         the finished state, the video premiering over the gate, dark
        06-per-photo        Per photo timing for a slideshow (24 photos, 3 s each), light
        07-settings         the Settings page, light
        08-narrow           a narrow window (portrait, -NarrowSize) framed 4:5, the Frame tab under the preview, light

    The screenshots to upload come from a real shoot: -PhotoFolder imports that folder (Debug --motion-import, through
    the same view model as Choose folder) for every shot with photos, and -SlideshowFolder (about 24 photos) feeds the
    Per photo shot. Without -SlideshowFolder that shot always uses 24 generated photos, since a whole shoot at 3 s a
    photo would not read as a slideshow. Without -PhotoFolder the photos are generated sunsets (Debug --sample-frames
    with --sample-plain) at 4500 x 3000, a camera's size, so the export shots show realistic speeds; they check the
    layout and are not for upload. They take about 2 GB under the test folder, deleted at the end.

    The Store takes desktop screenshots from 1366 x 768 (768 x 1366 portrait) up to 3840 x 2160, PNG. -Size 3840x2160
    needs a monitor whose largest window holds that client area (the script skips a shot it cannot size and says so).
    Build Debug x64 first (dotnet build TenLapse.slnx -c Debug -p:Platform=x64). Output goes to dist/store-screenshots
    (git-ignored). The test data under %LOCALAPPDATA%\Temp\claude\<Branch> is deleted at the end unless -KeepTestData.

.EXAMPLE
    pwsh -File scripts/store-screenshots.ps1

.EXAMPLE
    pwsh -File scripts/store-screenshots.ps1 -PhotoFolder 'D:\Shoots\Harbor sunset' -SlideshowFolder 'D:\Shoots\Trip'
#>
param(
    [string]$Branch = 'docs-42-store-listing',
    [string]$OutDir = '',
    [string]$PhotoFolder = '',
    [string]$SlideshowFolder = '',
    [ValidatePattern('^\d+x\d+$')]
    [string]$Size = '1920x1080',
    [ValidatePattern('^\d+x\d+$')]
    [string]$NarrowSize = '768x1366',
    [ValidateRange(60, 5000)]
    [int]$Frames = 480,
    [string[]]$Only = @(),
    [switch]$KeepTestData
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'src/TenLapse.App/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/TenLapse.App.exe'
if (-not (Test-Path $exe)) { throw "Build Debug x64 first: $exe" }
if (-not $OutDir) { $OutDir = Join-Path $root 'dist/store-screenshots' }
if ($PhotoFolder) { $PhotoFolder = (Resolve-Path $PhotoFolder).Path }
if ($SlideshowFolder) { $SlideshowFolder = (Resolve-Path $SlideshowFolder).Path }

# The Store's desktop range: at least 1366 x 768 landscape or 768 x 1366 portrait, at most 3840 x 2160 (turned for
# portrait).
foreach ($check in @(@('-Size', $Size), @('-NarrowSize', $NarrowSize))) {
    [int]$w, [int]$h = $check[1] -split 'x'
    $long, $short = [Math]::Max($w, $h), [Math]::Min($w, $h)
    if ($long -lt 1366 -or $short -lt 768 -or $long -gt 3840 -or $short -gt 2160) {
        throw "$($check[0]) $($check[1]) is outside the Store's range: 1366x768 (or 768x1366) up to 3840x2160."
    }
}

# Generated photos at a camera's size, so the export runs (and reads) at a real speed.
$sampleSize = '4500x3000'

$base = Join-Path $env:LOCALAPPDATA "Temp\claude\$Branch\store"
$samples = Join-Path $base 'samples'
# The finished state shows the last two folders of the path ("Videos › Timelapses").
$exportFolder = Join-Path $base 'Videos\Timelapses'
$exportFile = Join-Path $exportFolder 'Harbor sunset.mp4'
New-Item -ItemType Directory -Force $base, $samples, $exportFolder, $OutDir | Out-Null

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]
$Tree = [System.Windows.Automation.TreeScope]

# PrintWindow of the client area, read in physical pixels (the thread is made per-monitor aware, so the sizes are not
# scaled for a DPI-unaware caller).
if (-not ('StoreShot' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class StoreShot
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    private const uint ClientOnly = 1, RenderFullContent = 2;
    private static readonly IntPtr PerMonitorAware2 = new IntPtr(-4);

    public static int[] Client(IntPtr window)
    {
        var before = SetThreadDpiAwarenessContext(PerMonitorAware2);
        try { Rect r; GetClientRect(window, out r); return new[] { r.Right - r.Left, r.Bottom - r.Top }; }
        finally { SetThreadDpiAwarenessContext(before); }
    }

    public static int[] Outer(IntPtr window)
    {
        var before = SetThreadDpiAwarenessContext(PerMonitorAware2);
        try { Rect r; GetWindowRect(window, out r); return new[] { r.Right - r.Left, r.Bottom - r.Top }; }
        finally { SetThreadDpiAwarenessContext(before); }
    }

    // Draws the client area into a device context (a bitmap's, made in PowerShell).
    public static bool PrintClient(IntPtr window, IntPtr dc)
    {
        var before = SetThreadDpiAwarenessContext(PerMonitorAware2);
        try { return PrintWindow(window, dc, ClientOnly | RenderFullContent); }
        finally { SetThreadDpiAwarenessContext(before); }
    }
}
'@
}
Add-Type -AssemblyName System.Drawing

# Captures the client area to a PNG and returns its size as WxH, or why it could not.
function Save-Client([IntPtr]$handle, [string]$path) {
    $client = [StoreShot]::Client($handle)
    $bitmap = New-Object System.Drawing.Bitmap($client[0], $client[1], [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $dc = $graphics.GetHdc()
        try { $printed = [StoreShot]::PrintClient($handle, $dc) }
        finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
        if (-not $printed) { return 'PrintWindow failed' }
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        "$($bitmap.Width)x$($bitmap.Height)"
    }
    finally { $bitmap.Dispose() }
}

$Only = @($Only | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
function Want([string]$id) { $Only.Count -eq 0 -or $Only -contains $id }

$made = [System.Collections.Generic.List[string]]::new()
$missed = [System.Collections.Generic.List[string]]::new()
$run = 0

function Common([string]$theme) {
    $script:run++
    @(
        "--theme=$theme",
        "--settings-folder=$(Join-Path $base "settings-$script:run")",
        "--cache-folder=$(Join-Path $base 'cache')",
        "--log-folder=$(Join-Path $base 'logs')",
        "--encoder-cache=$(Join-Path $base 'encoder-cache.json')",
        "--sample-folder=$samples"
    )
}

# The photos: a folder imported as Choose folder would, or N generated plain sunsets at a camera's size.
function Photos([int]$count, [string]$folder = $PhotoFolder) {
    if ($folder) { @("--motion-import=$folder", '--motion-import-delay=0.5') }
    else { @("--sample-frames=$count", '--sample-plain', "--sample-size=$sampleSize") }
}

function Start-App([string[]]$appArgs) {
    $quoted = $appArgs | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
    $p = Start-Process -FilePath $exe -ArgumentList $quoted -PassThru
    $window = $null
    $until = (Get-Date).AddSeconds(30)
    while (-not $window -and (Get-Date) -lt $until) {
        Start-Sleep -Milliseconds 300
        $window = $AE::RootElement.FindFirst($Tree::Children, (New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)))
    }
    if (-not $window) { Stop-App @{ Process = $p }; throw 'The window did not appear in 30 s.' }
    @{ Process = $p; Window = $window; Handle = [IntPtr]$window.Current.NativeWindowHandle }
}

# Stops only the process this script started (its own PID and start time).
function Stop-App($app) {
    $p = $app.Process
    if (-not $p.HasExited) {
        $same = Get-Process -Id $p.Id -ErrorAction SilentlyContinue | Where-Object { $_.StartTime -eq $p.StartTime }
        if ($same) { Stop-Process -Id $p.Id -Force; $p.WaitForExit() }
    }
}

function Find-Id($app, [string]$id, [double]$seconds = 10) {
    $until = (Get-Date).AddSeconds($seconds)
    do {
        if ($app.Process.HasExited) { return $null }
        $condition = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
        foreach ($element in @($app.Window.FindAll($Tree::Descendants, $condition))) {
            if (-not $element.Current.IsOffscreen) { return $element }
        }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $until)
    $null
}

function Invoke-Element($element) {
    if ($element) { $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
}

# Sets the client area to exactly WxH through UIA's TransformPattern on the app's own window (a few rounds: the frame
# around the client area is measured, not assumed). Moves the window to the top left first, so a tall one fits.
function Set-ClientSize($app, [string]$size) {
    [int]$w, [int]$h = $size -split 'x'
    $transform = $null
    if (-not $app.Window.TryGetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern, [ref]$transform)) { return $false }
    if ($transform.Current.CanMove) { $transform.Move(0, 0) }
    for ($round = 0; $round -lt 5; $round++) {
        $client = [StoreShot]::Client($app.Handle)
        if ($client[0] -eq $w -and $client[1] -eq $h) { return $true }
        $outer = [StoreShot]::Outer($app.Handle)
        $transform.Resize($outer[0] + $w - $client[0], $outer[1] + $h - $client[1])
        Start-Sleep -Milliseconds 700
    }
    $client = [StoreShot]::Client($app.Handle)
    Write-Host "  client area is $($client[0])x$($client[1]), not $size"
    $client[0] -eq $w -and $client[1] -eq $h
}

# Waits until the project holds the photos (the scrubber's RangeValue maximum is the frame count) and the Output panel
# has its answer (Export is enabled), then seeks through the scrubber's RangeValue pattern to that fraction of the
# photos the import actually brought in (0.36: about a third in, the golden light).
function Wait-Photos($app, [int]$count, [double]$seekFraction = 0, [double]$seconds = 240) {
    $until = (Get-Date).AddSeconds($seconds)
    $scrubber = Find-Id $app 'ScrubberBar' $seconds
    if (-not $scrubber) { return $false }
    $range = $scrubber.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
    $last = -1
    $steady = 0
    while ((Get-Date) -lt $until -and -not $app.Process.HasExited) {
        $max = [int]$range.Current.Maximum
        # With -PhotoFolder the count is unknown: the import is done when the count holds for 3 s.
        if ($count -gt 0 -and $max -ge $count) { break }
        if ($count -le 0 -and $max -gt 0 -and $max -eq $last) { if (++$steady -ge 10) { break } } else { $steady = 0 }
        $last = $max
        Start-Sleep -Milliseconds 300
    }
    $export = Find-Id $app 'ExportButton' 30
    while ($export -and -not $export.Current.IsEnabled -and (Get-Date) -lt $until) { Start-Sleep -Milliseconds 300 }
    $max = [int]$range.Current.Maximum
    if ($seekFraction -gt 0 -and $max -gt 0) {
        $range.SetValue([Math]::Max(1, [Math]::Min($max, [int][Math]::Round($max * $seekFraction))))
    }
    # Thumbnails, the proxy of the frame on screen, the arrival motion.
    Start-Sleep -Seconds 5
    $true
}

function Save-Shot($app, [string]$name, [string]$size, [string]$what, [int]$settle = 1200) {
    if ($app.Process.HasExited) { $missed.Add("$name (the app exited)"); return }
    if (-not (Set-ClientSize $app $size)) { $missed.Add("$name (the window cannot reach $size here)"); return }
    Start-Sleep -Milliseconds $settle
    $path = Join-Path $OutDir "$name.png"
    $result = Save-Client $app.Handle $path
    if ($result -ne $size) {
        Remove-Item $path -ErrorAction SilentlyContinue
        $missed.Add("$name ($result)")
        return
    }
    $made.Add("$name.png  $size  $what")
    Write-Host "  $name.png ($size): $what"
}

# Runs one shot. Its PNGs (the shot's own, and any other it owns, like 04's 05) are deleted first, so a failed shot
# never leaves an old picture behind.
function Shot([string]$id, [string]$name, [scriptblock]$body, [string[]]$alsoOwns = @()) {
    if (-not (Want $id)) { return }
    Write-Host "Shot $id"
    foreach ($owned in @($name) + $alsoOwns) { Remove-Item (Join-Path $OutDir "$owned.png") -ErrorAction SilentlyContinue }
    try { & $body }
    catch { $missed.Add("$name ($($_.Exception.Message))") }
}

# Sizes the window right after the start and says whether the shot can go on; a size this monitor cannot reach skips
# the shot (and the shots it owns) before any waiting.
function Sized($app, [string]$size, [string[]]$names) {
    if (Set-ClientSize $app $size) { return $true }
    foreach ($name in $names) { $missed.Add("$name (the window cannot reach $size here)") }
    $false
}

# With a folder the count is unknown (0): Wait-Photos waits for the import to settle instead.
function Count([int]$count) { if ($PhotoFolder) { 0 } else { $count } }

try {
    Shot '01' '01-first-launch' {
        $app = Start-App (Common 'dark')
        try {
            if (-not (Sized $app $Size '01-first-launch')) { return }
            Find-Id $app 'ChooseFolderButton' 20 | Out-Null
            Start-Sleep -Seconds 4
            Save-Shot $app '01-first-launch' $Size 'the start screen, dark'
        }
        finally { Stop-App $app }
    }

    Shot '02' '02-editing' {
        $app = Start-App ((Common 'dark') + (Photos $Frames) + '--output=2160')
        try {
            if (-not (Sized $app $Size '02-editing')) { return }
            if (Wait-Photos $app (Count $Frames) 0.36) { Save-Shot $app '02-editing' $Size 'a sequence imported, 16:9, 4K output, dark' }
            else { $missed.Add('02-editing (the photos did not arrive)') }
        }
        finally { Stop-App $app }
    }

    Shot '03' '03-vertical' {
        $app = Start-App ((Common 'light') + (Photos $Frames) + '--output=1080' + '--frame=9:16,fill')
        try {
            if (-not (Sized $app $Size '03-vertical')) { return }
            if (Wait-Photos $app (Count $Frames) 0.46) { Save-Shot $app '03-vertical' $Size 'the same set framed 9:16 for a phone, light' }
            else { $missed.Add('03-vertical (the photos did not arrive)') }
        }
        finally { Stop-App $app }
    }

    # The export runs to the end: the running panel is taken once it is about 40 % through, the finished state after its
    # check has drawn and the premiere has started.
    Shot '04' '04-exporting' {
        Remove-Item $exportFile -ErrorAction SilentlyContinue
        $app = Start-App ((Common 'dark') + (Photos $Frames) + '--output=2160' + "--export-to=$exportFile")
        try {
            if (-not (Sized $app $Size '04-exporting', '05-finished')) { return }
            if (Find-Id $app 'CancelButton' 300) {
                $bar = $null
                $until = (Get-Date).AddSeconds(120)
                while (-not $bar -and (Get-Date) -lt $until) {
                    $bar = $app.Window.FindFirst($Tree::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ProgressBar)))
                    if (-not $bar) { Start-Sleep -Milliseconds 100 }
                }
                $range = if ($bar) { $bar.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern) }
                while ($range -and $range.Current.Value -lt 40 -and (Get-Date) -lt $until -and (Find-Id $app 'CancelButton' 0.1)) { Start-Sleep -Milliseconds 50 }
                if (Find-Id $app 'CancelButton' 0.1) { Save-Shot $app '04-exporting' $Size 'the export running: film gate, percent, time left, dark' 0 }
                else { $missed.Add('04-exporting (the export finished before the capture; try more -Frames)') }
            }
            else { $missed.Add('04-exporting (the export did not start)') }
            if (Find-Id $app 'FinishedTitle' 300) {
                # The premiere starts about 1.5 s after the title and plays at the default 30 fps; wait until it is about
                # 0.36 of the way through the clip, the golden part the scrubber also shows.
                $scrubber = Find-Id $app 'ScrubberBar' 5
                $count = if ($scrubber) { [int]$scrubber.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).Current.Maximum } else { $Frames }
                Start-Sleep -Milliseconds ([int](1500 + 0.36 * $count / 30 * 1000))
                Save-Shot $app '05-finished' $Size 'the finished state, the video premiering over the gate, dark' 0
            }
            else { $missed.Add('05-finished (the export did not finish)') }
        }
        finally { Stop-App $app }
        Remove-Item $exportFile -ErrorAction SilentlyContinue
    } -alsoOwns '05-finished'

    # A slideshow is a handful of photos: -SlideshowFolder, or always 24 generated ones (a whole shoot at 3 s a photo
    # would read as half an hour).
    Shot '06' '06-per-photo' {
        $app = Start-App ((Common 'light') + (Photos 24 $SlideshowFolder) + '--output=1080' + '--timing=photo' + '--timing-value=3')
        try {
            if (-not (Sized $app $Size '06-per-photo')) { return }
            if (Wait-Photos $app $(if ($SlideshowFolder) { 0 } else { 24 }) 0.4) { Save-Shot $app '06-per-photo' $Size 'Per photo timing for a slideshow, 3 s a photo, light' }
            else { $missed.Add('06-per-photo (the photos did not arrive)') }
        }
        finally { Stop-App $app }
    }

    Shot '07' '07-settings' {
        $app = Start-App (Common 'light')
        try {
            if (-not (Sized $app $Size '07-settings')) { return }
            Invoke-Element (Find-Id $app 'SettingsButton' 20)
            if (Find-Id $app 'BackButton' 10) {
                Start-Sleep -Seconds 2
                Save-Shot $app '07-settings' $Size 'the Settings page, light'
            }
            else { $missed.Add('07-settings (the Settings page did not open)') }
        }
        finally { Stop-App $app }
    }

    Shot '08' '08-narrow' {
        $app = Start-App ((Common 'light') + (Photos $Frames) + '--output=1080' + '--frame=4:5,fill')
        try {
            if (-not (Sized $app $NarrowSize '08-narrow')) { return }
            if (Wait-Photos $app (Count $Frames) 0.26) {
                # The Frame tab of the panel under the preview (SelectionItem on the tab row's second radio button).
                $tabs = Find-Id $app 'InspectorTabsControl' 8
                $radios = @(if ($tabs) { $tabs.FindAll($Tree::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::RadioButton))) })
                if ($radios.Count -ge 2) { $radios[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() }
                Save-Shot $app '08-narrow' $NarrowSize 'a narrow window framed 4:5, the Frame tab under the preview, light'
            }
            else { $missed.Add('08-narrow (the photos did not arrive)') }
        }
        finally { Stop-App $app }
    }
}
finally {
    # The generated photos at 4500 x 3000 take about 2 GB: they go even when a shot throws or the run is stopped.
    if (-not $KeepTestData) {
        Remove-Item $base -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "Removed the test data under $base"
    }
}

Write-Host ''
Write-Host "Screenshots in $OutDir"
$made | ForEach-Object { Write-Host "  $_" }
if ($missed.Count -gt 0) {
    Write-Host "Not made: $($missed -join '; ')"
}
if ($missed.Count -gt 0) { exit 1 }
