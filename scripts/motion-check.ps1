<#
.SYNOPSIS
    Plays the app's transitions without real input and prints the motion log (#35).

.DESCRIPTION
    Runs the Debug app twice, once as Windows has it and once with --reduce-motion, each time with --motion-log, and
    drives it through UI Automation patterns on its own window and Debug launch switches only (no synthetic input):
    the InfoBar (--infobar: View list, Dismiss), the arrival (--motion-import of generated sample photos), the export
    panel, film gate, finished state and premiere (--export-to), Back to editing, a new speed (the 12 fps preset), a new
    shape and fit (9:16, Fit), Settings and Back, and two theme crossfades (--theme-flip-after). Every file goes under
    %LOCALAPPDATA%\Temp\claude\<Branch>; -Clean deletes it afterwards. Build the app Debug x64 first.

.EXAMPLE
    pwsh -File scripts/motion-check.ps1 -Clean
#>
param(
    [string]$Branch = 'feat-35-motion',
    [int]$Photos = 36,
    [switch]$Clean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'src/TenLapse.App/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/TenLapse.App.exe'
if (-not (Test-Path $exe)) { throw "Build the app Debug x64 first: $exe" }

$base = Join-Path $env:LOCALAPPDATA "Temp\claude\$Branch"
$samples = Join-Path $base 'samples'
New-Item -ItemType Directory -Force $base, $samples | Out-Null

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]
$Tree = [System.Windows.Automation.TreeScope]

function Common([string]$name) {
    @(
        # The checks find controls by their English UIA names, so the app speaks English whatever Windows says (#113).
        '--lang=en-US',
        "--settings-folder=$(Join-Path $base "settings-$name")",
        "--cache-folder=$(Join-Path $base "cache-$name")",
        "--log-folder=$(Join-Path $base "logs-$name")",
        "--encoder-cache=$(Join-Path $base 'encoder-cache.json')",
        "--sample-folder=$samples"
    )
}

function Window([int]$id) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $id)
    $AE::RootElement.FindFirst($Tree::Children, $condition)
}

# The pattern $pattern of the first element of the window named $name that has it, waiting up to $seconds.
function Find($window, [string]$name, $pattern, [double]$seconds = 10) {
    $until = (Get-Date).AddSeconds($seconds)
    do {
        $condition = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)
        # Buttons and radio buttons before anything else of that name (a filmstrip item, a text).
        $elements = @($window.FindAll($Tree::Descendants, $condition)) | Sort-Object { $_.Current.ControlType.ProgrammaticName -notin 'ControlType.Button', 'ControlType.RadioButton', 'ControlType.Hyperlink' }
        foreach ($element in $elements) {
            $found = $null
            if ($element.TryGetCurrentPattern($pattern, [ref]$found)) { return $found }
        }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $until)
    Write-Host "  not found: $name"
    $null
}

function Invoke-Named($window, [string]$name, [double]$seconds = 10) {
    $invoke = Find $window $name ([System.Windows.Automation.InvokePattern]::Pattern) $seconds
    if ($invoke) {
        $invoke.Invoke()
        Write-Host "  invoked $name"
    }
}

function Select-Named($window, [string]$name) {
    $item = Find $window $name ([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if ($item) {
        $item.Select()
        Write-Host "  selected $name"
    }
}

# Sample photos the arrival imports: generated once by the app's own --sample-frames, reused after.
$folder = Get-ChildItem $samples -Directory -Filter "Harbor sunset $Photos grain *" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $folder -or @(Get-ChildItem $folder.FullName -File).Count -lt $Photos) {
    Write-Host "Generating $Photos sample photos"
    $p = Start-Process -FilePath $exe -ArgumentList ((Common 'samples') + "--sample-frames=$Photos") -PassThru
    $until = (Get-Date).AddMinutes(2)
    do {
        Start-Sleep -Milliseconds 500
        $folder = Get-ChildItem $samples -Directory -Filter "Harbor sunset $Photos grain *" -ErrorAction SilentlyContinue | Select-Object -First 1
    } while ((-not $folder -or @(Get-ChildItem $folder.FullName -File -Filter *.jpg).Count -lt $Photos) -and (Get-Date) -lt $until)
    Start-Sleep -Seconds 2
    Stop-Process -Id $p.Id -Force
    $p.WaitForExit()
}

foreach ($run in @(@{ Name = 'on'; Extra = @() }, @{ Name = 'reduced'; Extra = @('--reduce-motion') })) {
    $name = $run.Name
    Write-Host "Run: animations $name"
    $logs = Join-Path $base "logs-$name"
    Remove-Item $logs -Recurse -Force -ErrorAction SilentlyContinue

    # Fresh remembered settings, so 12 fps, 9:16 and Fit are changes (an earlier run remembers them).
    Remove-Item (Join-Path $base "settings-$name") -Recurse -Force -ErrorAction SilentlyContinue
    $export = Join-Path $base "export-$name.mp4"
    Remove-Item $export -ErrorAction SilentlyContinue
    $appArgs = (Common $name) + $run.Extra + @(
        '--motion-log',
        '--infobar',
        "--motion-import=$($folder.FullName)",
        '--motion-import-delay=5',
        "--export-to=$export",
        '--theme-flip-after=40,43'
    )
    $quoted = $appArgs | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
    $p = Start-Process -FilePath $exe -ArgumentList $quoted -PassThru
    try {
        $window = $null
        $until = (Get-Date).AddSeconds(30)
        while (-not $window -and (Get-Date) -lt $until) { Start-Sleep -Milliseconds 300; $window = Window $p.Id }
        if (-not $window) { throw 'The window did not appear.' }

        # The InfoBar: open its detail list, then dismiss it and the one behind it.
        Invoke-Named $window 'View list'
        Start-Sleep -Milliseconds 800
        Invoke-Named $window 'Dismiss'
        Start-Sleep -Milliseconds 800
        Invoke-Named $window 'Dismiss'

        # The arrival plays at 5 s; the export starts by itself once the photos are in, and finishes.
        Invoke-Named $window 'Back to editing' 90
        Start-Sleep -Seconds 2

        # Retime, reframe, Settings and back.
        Select-Named $window '12'
        Start-Sleep -Seconds 1
        Select-Named $window '9:16'
        Start-Sleep -Seconds 1
        Select-Named $window 'Fit'
        Start-Sleep -Seconds 1
        Invoke-Named $window 'Settings'
        Start-Sleep -Seconds 1.5
        Invoke-Named $window 'Back'
        Start-Sleep -Seconds 1.5

        # Interrupted: Settings and Back again at once, so the second change overtakes the first's animations; then
        # across the 1,000 DIP breakpoint and back (TransformPattern on the app's own window).
        Invoke-Named $window 'Settings'
        Invoke-Named $window 'Back'
        Start-Sleep -Seconds 1
        $transform = $null
        if ($window.TryGetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern, [ref]$transform) -and $transform.Current.CanResize) {
            $bounds = $window.Current.BoundingRectangle
            $transform.Resize(900, $bounds.Height)
            Start-Sleep -Seconds 1
            $transform.Resize($bounds.Width, $bounds.Height)
            Write-Host '  resized to 900 px and back'
        }

        $settings = Find $window 'Settings' ([System.Windows.Automation.InvokePattern]::Pattern) 2
        Write-Host "  editing screen at rest: $([bool]$settings)"

        # The theme flips at 40 and 43 s after launch.
        $wait = 46 - ((Get-Date) - $p.StartTime).TotalSeconds
        if ($wait -gt 0) { Start-Sleep -Seconds $wait }
    }
    finally {
        if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force; $p.WaitForExit() }
    }

    $log = Join-Path $logs 'motion.log'
    Write-Host "--- motion.log ($name)"
    if (Test-Path $log) { Get-Content $log } else { Write-Host '(none)' }
    Write-Host "--- app log warnings ($name)"
    Get-ChildItem $logs -Filter '10Lapse-*.log' | ForEach-Object { Select-String -Path $_.FullName -Pattern ' (WARN|ERROR|CRIT) ' | ForEach-Object { $_.Line } }
}

if ($Clean) {
    Remove-Item $base -Recurse -Force
    Write-Host "Removed $base"
}
