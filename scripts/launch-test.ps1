<#
.SYNOPSIS
    Launches the Debug app many times, optionally under load, and records how each launch ended (#90).

.DESCRIPTION
    Each launch runs the app under tools/TenLapse.CrashCatcher (a debugger of its own child process: no admin, no
    registry change), waits for its window through UI Automation, lets it run -Dwell seconds and closes it with
    UIA WindowPattern.Close. No synthetic input. A launch that ends with a non-zero code keeps its app log and the
    catcher's report (the stowed exception, the last WinRT errors, the stacks); a window that has not closed 10 s
    after Close is asked once more, then its stacks are taken and it is stopped.

    The settings, sample photos, proxy cache and encoder cache are the test's own (Debug switches), so the user's
    are never touched. -Load starts a CPU burner (pwsh) and/or a GPU encode loop (TenLapse.Spike), and stops them
    at the end; nothing the script did not start is stopped. Results: results.csv in -Out, one row per launch.
    -Out keeps the sample photos and caches for the next run; delete it when done.

.EXAMPLE
    pwsh -File scripts/launch-test.ps1 -Count 100 -Load both -ColdProbe
    pwsh -File scripts/launch-test.ps1 -Count 50 -SampleFrames 24 -FreshCopy
#>
param(
    [int] $Count = 100,
    # Fill the project with N generated photos (--sample-frames), so start-up shows the Editing state.
    [int] $SampleFrames = 0,
    [ValidateSet('none', 'cpu', 'gpu', 'both')] [string] $Load = 'none',
    # Every launch probes the encoders cold, as the first launch of a new build does (--encoder-cache).
    [switch] $ColdProbe,
    # Every launch runs from a copy of the build in a new folder, as a fresh build does.
    [switch] $FreshCopy,
    [double] $Dwell = 3,
    [string] $Out,
    [string] $Exe
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$branch = (& git -C $repo rev-parse --abbrev-ref HEAD 2>$null) -replace '[\\/]', '-'
if (-not $Out) { $Out = Join-Path $env:LOCALAPPDATA "Temp\claude\launch-test-$branch" }
$tfm = 'net10.0-windows10.0.26100.0'
if (-not $Exe) { $Exe = Join-Path $repo "src\TenLapse.App\bin\x64\Debug\$tfm\win-x64\TenLapse.App.exe" }
$catcher = Join-Path $repo 'tools\TenLapse.CrashCatcher\bin\x64\Debug\net10.0-windows\TenLapse.CrashCatcher.exe'
$spike = Join-Path $repo "tools\TenLapse.Spike\bin\x64\Release\$tfm\win-x64\TenLapse.Spike.exe"
foreach ($needed in @($Exe, $catcher) + $(if ($Load -in 'gpu', 'both') { $spike } else { @() })) {
    if (-not (Test-Path $needed)) { throw "Not built: $needed (build the app Debug x64, tools/TenLapse.CrashCatcher Debug, tools/TenLapse.Spike Release)." }
}

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$data = Join-Path $Out 'data'
$folders = @{ settings = 'settings'; samples = 'samples'; cache = 'cache'; fresh = 'fresh' }
foreach ($name in $folders.Values) { New-Item -ItemType Directory -Force (Join-Path $data $name) | Out-Null }
$encoderCache = Join-Path $data 'encoders.json'
$results = Join-Path $Out 'results.csv'
if (-not (Test-Path $results)) { 'time,launch,load,samples,coldProbe,freshCopy,pid,windowMs,exit,outcome,lastLog' | Set-Content $results }
$logs = Join-Path $env:LOCALAPPDATA '10Lapse\Logs'

$appArgs = @("--settings-folder=$(Join-Path $data 'settings')", "--sample-folder=$(Join-Path $data 'samples')", "--cache-folder=$(Join-Path $data 'cache')")
if ($SampleFrames -gt 0) { $appArgs += "--sample-frames=$SampleFrames" }
if ($ColdProbe) { $appArgs += "--encoder-cache=$encoderCache" }

function Find-Window([int] $id) {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $id)
    [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
}

function Close-Window([int] $id) {
    try {
        $window = Find-Window $id
        if ($window) { $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() }
    }
    catch { }
}

# Load generators: this script's own processes, stopped in the finally block with what they started.
$loaders = @()
$burn = Join-Path $Out 'burn.ps1'
$gpu = Join-Path $Out 'gpu.ps1'
'param([int]$Threads) 1..$Threads | ForEach-Object -Parallel { $x = 0; while ($true) { for ($k = 0; $k -lt 100000; $k++) { $x = $x * 31 + $k } } } -ThrottleLimit $Threads' | Set-Content $burn
'param([string]$Spike, [string]$File) while ($true) { & $Spike encode --source synthetic --frames 1500 --output-size 3840x2160 --fps 60 --out $File | Out-Null; Remove-Item $File -ErrorAction SilentlyContinue }' | Set-Content $gpu

$crashes = 0
$run = Get-Date -Format 'yyyyMMdd-HHmmss'
try {
    if ($Load -in 'cpu', 'both') { $loaders += Start-Process pwsh -ArgumentList '-NoProfile', '-File', $burn, '-Threads', ([Environment]::ProcessorCount - 2) -PassThru -WindowStyle Hidden }
    if ($Load -in 'gpu', 'both') { $loaders += Start-Process pwsh -ArgumentList '-NoProfile', '-File', $gpu, '-Spike', $spike, '-File', (Join-Path $data 'gpu-load.mp4') -PassThru -WindowStyle Hidden }

    for ($i = 1; $i -le $Count; $i++) {
        $pidFile = Join-Path $Out 'app.pid'
        $report = Join-Path $Out "catch-$run-$i.txt"
        Remove-Item $pidFile, "$pidFile.walk" -ErrorAction SilentlyContinue
        if ($ColdProbe) { Remove-Item $encoderCache -ErrorAction SilentlyContinue }
        $runExe = $Exe
        if ($FreshCopy) {
            $fresh = Join-Path $data "fresh\$i"
            Copy-Item -Recurse (Split-Path $Exe) $fresh -Force
            $runExe = Join-Path $fresh (Split-Path $Exe -Leaf)
        }

        $p = Start-Process -FilePath $catcher -ArgumentList (@($report, $pidFile, $runExe) + $appArgs) -PassThru -WindowStyle Hidden
        $started = [Diagnostics.Stopwatch]::StartNew()
        $appPid = 0
        $window = $null
        while (-not $p.HasExited -and $started.Elapsed.TotalSeconds -lt 40) {
            if ($appPid -eq 0 -and (Test-Path $pidFile)) { $appPid = [int](Get-Content $pidFile) }
            if ($appPid) { try { $window = Find-Window $appPid } catch { $window = $null } }
            if ($window) { break }
            Start-Sleep -Milliseconds 100
        }

        $windowMs = if ($window) { [int]$started.Elapsed.TotalMilliseconds } else { -1 }
        $outcome = 'closed'
        if ($window) { $p.WaitForExit([int]($Dwell * 1000)) | Out-Null }
        if ($p.HasExited) {
            $outcome = if ($window) { 'exited-early' } else { 'died-before-window' }
        }
        else {
            Close-Window $appPid
            if (-not $p.WaitForExit(10000)) {
                # A first Close the window did not act on happens under load (the UI thread is idle); ask once more.
                Close-Window $appPid
                $outcome = if ($p.WaitForExit(10000)) { 'closed-on-retry' } else { 'hung' }
            }

            if (-not $p.HasExited) {
                New-Item -ItemType File "$pidFile.walk" -Force | Out-Null
                Start-Sleep -Seconds 4
                Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
                if ($appPid) { Stop-Process -Id $appPid -Force -ErrorAction SilentlyContinue }
            }
        }

        $p.WaitForExit()
        $exit = $p.ExitCode
        $log = if ($appPid) { Get-ChildItem $logs -Filter "*.p$appPid.log" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1 }
        $last = if ($log) { ((Get-Content $log.FullName | Where-Object { $_ -match '^\d{4}-' } | Select-Object -Last 1) -replace '^\S+\s+\S+\s+', '') -replace ',', ';' } else { '' }
        if ($exit -ne 0 -and $outcome -ne 'hung') {
            $crashes++
            if ($log) { Copy-Item $log.FullName (Join-Path $Out "crash-$run-$i-p$appPid.log") }
            Write-Host ("Launch {0}: exit 0x{1:X8} ({2}); last log line: {3}" -f $i, $exit, $outcome, $last)
        }
        elseif ($log) {
            # This launch's own log, written by a process this script started.
            Remove-Item $log.FullName -ErrorAction SilentlyContinue
        }

        '{0:s},{1},{2},{3},{4},{5},{6},{7},0x{8:X8},{9},{10}' -f (Get-Date), $i, $Load, $SampleFrames, [bool]$ColdProbe, [bool]$FreshCopy, $appPid, $windowMs, $exit, $outcome, $last | Add-Content $results
        if ($FreshCopy) { Remove-Item -Recurse -Force $fresh -ErrorAction SilentlyContinue }
    }
}
finally {
    foreach ($loader in $loaders) {
        Get-CimInstance Win32_Process -Filter "ParentProcessId=$($loader.Id)" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        Stop-Process -Id $loader.Id -Force -ErrorAction SilentlyContinue
    }

    Remove-Item (Join-Path $data 'gpu-load.mp4') -ErrorAction SilentlyContinue

    # A copy is often still locked for a moment after its app exits (a scan, the loader); what was left goes now.
    Remove-Item -Recurse -Force (Join-Path $data 'fresh\*') -ErrorAction SilentlyContinue
}

Write-Host "$Count launches ($Load load, $SampleFrames sample frames, cold probe $([bool]$ColdProbe), fresh copy $([bool]$FreshCopy)): $crashes ended with a non-zero code. Results: $results"
if ($crashes -gt 0) { exit 1 }
