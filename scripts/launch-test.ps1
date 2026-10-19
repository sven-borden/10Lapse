<#
.SYNOPSIS
    Launches the Debug app many times, optionally under load, and records how each launch ended (#90).

.DESCRIPTION
    Each launch waits for the app's window through UI Automation, lets it run -Dwell seconds and closes it with UIA
    WindowPattern.Close. No synthetic input. By default each launch runs under tools/TenLapse.CrashCatcher (a debugger
    of its own child process: no admin, no registry change), which keeps the stowed exception and the last WinRT errors
    of a crash. -Plain launches the app directly instead, as a user does: no report, but start-up timing as without a
    debugger. Run both: a race at start-up may show in one and not the other.

    A launch that ends any other way than closing keeps its app log and the catcher's report: a non-zero exit, a
    window that never appears ("no-window"), and a window that has not closed 10 s after a second Close ("hung", whose
    stacks the catcher takes before it is stopped).

    Everything the app writes is the test's own, through Debug switches: settings, sample photos, proxy cache, encoder
    cache (always: with -ColdProbe a new file per launch, otherwise one the launches share) and the log folder (one per
    launch). The user's %LOCALAPPDATA%\10Lapse is not written to. -Load starts a CPU burner (pwsh) and/or a GPU encode
    loop (TenLapse.Spike) and stops them at the end. Only processes this script started are stopped, each checked by
    its start time first.

    Results: results.csv in -Out, one row per launch, with the time to the window and, from the app's start-up trace,
    the first frame, whether the probe ran cold, and when its answer reached the Output panel (milliseconds since the
    process started). -Out keeps the sample photos and caches for the next run; delete it when done.

.EXAMPLE
    pwsh -File scripts/launch-test.ps1 -Count 100 -Load both -ColdProbe
    pwsh -File scripts/launch-test.ps1 -Count 100 -Load both -ColdProbe -Plain
    pwsh -File scripts/launch-test.ps1 -Count 10 -ColdProbe -Plain -WaitForProbe
#>
param(
    [int] $Count = 100,
    # Fill the project with N generated photos (--sample-frames), so start-up shows the Editing state.
    [int] $SampleFrames = 0,
    [ValidateSet('none', 'cpu', 'gpu', 'both')] [string] $Load = 'none',
    # Every launch probes the encoders cold, as the first launch of a new build does (a new --encoder-cache file each).
    [switch] $ColdProbe,
    # Every launch runs from a copy of the build in a new folder, as a fresh build does.
    [switch] $FreshCopy,
    # Launch the app directly, without the catcher.
    [switch] $Plain,
    # Keep the window open until the probe's answer is in the Output panel (at most 30 s), then dwell.
    [switch] $WaitForProbe,
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
$catcher = Join-Path $repo "tools\TenLapse.CrashCatcher\bin\x64\Debug\$tfm\TenLapse.CrashCatcher.exe"
$spike = Join-Path $repo "tools\TenLapse.Spike\bin\x64\Release\$tfm\win-x64\TenLapse.Spike.exe"
$needed = @($Exe) + $(if (-not $Plain) { $catcher } else { @() }) + $(if ($Load -in 'gpu', 'both') { $spike } else { @() })
foreach ($file in $needed) {
    if (-not (Test-Path $file)) { throw "Not built: $file (build the app Debug x64, tools/TenLapse.CrashCatcher Debug, tools/TenLapse.Spike Release)." }
}

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$data = Join-Path $Out 'data'
foreach ($name in 'settings', 'samples', 'cache', 'fresh', 'probe', 'logs') { New-Item -ItemType Directory -Force (Join-Path $data $name) | Out-Null }
$results = Join-Path $Out 'results.csv'
if (-not (Test-Path $results)) { 'time,mode,launch,load,samples,coldProbe,freshCopy,pid,windowMs,firstFrameMs,probe,probeAppliedMs,exit,outcome,closeError,lastLog' | Set-Content $results }
$mode = if ($Plain) { 'plain' } else { 'catcher' }

function Find-Window([int] $id) {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $id)
    [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
}

# Asks the window to close through UIA. Returns '' or what went wrong (no window, or the exception), for results.csv.
function Close-Window([int] $id) {
    try {
        $window = Find-Window $id
        if (-not $window) { return 'no window to close' }
        $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
        return ''
    }
    catch { return ($_.Exception.GetType().Name + ': ' + $_.Exception.Message) -replace '[,\r\n]', ' ' }
}

# Stops a process this script started: only while it still runs and is the same process (its start time).
function Stop-Own($process, [datetime] $started) {
    if ($null -eq $process) { return }
    try {
        $process.Refresh()
        if (-not $process.HasExited -and $process.StartTime -eq $started) { $process.Kill() }
    }
    catch { Write-Host "Could not stop process $($process.Id): $($_.Exception.Message)" }
}

function Trace-Ms([string] $text, [string] $phase) {
    if ($text -match ('Start-up: ' + [regex]::Escape($phase) + ' \((\d+) ms\)')) { [int]$Matches[1] } else { -1 }
}

# Load generators: this script's own processes, stopped in the finally block with what they started.
$loaders = @()
$burn = Join-Path $Out 'burn.ps1'
$gpu = Join-Path $Out 'gpu.ps1'
'param([int]$Threads) 1..$Threads | ForEach-Object -Parallel { $x = 0; while ($true) { for ($k = 0; $k -lt 100000; $k++) { $x = $x * 31 + $k } } } -ThrottleLimit $Threads' | Set-Content $burn
'param([string]$Spike, [string]$File) while ($true) { & $Spike encode --source synthetic --frames 1500 --output-size 3840x2160 --fps 60 --out $File | Out-Null; Remove-Item $File -ErrorAction SilentlyContinue }' | Set-Content $gpu

$crashes = 0
$noWindow = 0
$hung = 0
$retries = 0
$run = Get-Date -Format 'yyyyMMdd-HHmmss'
try {
    if ($Load -in 'cpu', 'both') { $loaders += Start-Process pwsh -ArgumentList '-NoProfile', '-File', $burn, '-Threads', ([Environment]::ProcessorCount - 2) -PassThru -WindowStyle Hidden }
    if ($Load -in 'gpu', 'both') { $loaders += Start-Process pwsh -ArgumentList '-NoProfile', '-File', $gpu, '-Spike', $spike, '-File', (Join-Path $data 'gpu-load.mp4') -PassThru -WindowStyle Hidden }
    $loaderStarts = @($loaders | ForEach-Object { $_.StartTime })

    $warmCache = Join-Path $data 'encoders.json'
    for ($i = 1; $i -le $Count; $i++) {
        $pidFile = Join-Path $Out 'app.pid'
        $report = Join-Path $Out "catch-$run-$i.txt"
        $logFolder = Join-Path $data "logs\$run-$i"
        $encoderCache = if ($ColdProbe) { Join-Path $data "probe\encoders-$run-$i.json" } else { $warmCache }
        Remove-Item $pidFile, "$pidFile.walk" -ErrorAction SilentlyContinue
        $runExe = $Exe
        if ($FreshCopy) {
            $fresh = Join-Path $data "fresh\$i"
            Copy-Item -Recurse (Split-Path $Exe) $fresh -Force
            $runExe = Join-Path $fresh (Split-Path $Exe -Leaf)
        }

        $appArgs = @(
            "--settings-folder=$(Join-Path $data 'settings')",
            "--sample-folder=$(Join-Path $data 'samples')",
            "--cache-folder=$(Join-Path $data 'cache')",
            "--encoder-cache=$encoderCache",
            "--log-folder=$logFolder")
        if ($SampleFrames -gt 0) { $appArgs += "--sample-frames=$SampleFrames" }

        # $p is what this script waits on (the catcher, or the app when plain); $app is the app itself.
        $p = if ($Plain) { Start-Process -FilePath $runExe -ArgumentList $appArgs -PassThru } else { Start-Process -FilePath $catcher -ArgumentList (@($report, $pidFile, $runExe) + $appArgs) -PassThru -WindowStyle Hidden }
        $pStarted = $p.StartTime
        $started = [Diagnostics.Stopwatch]::StartNew()
        $app = if ($Plain) { $p } else { $null }
        $appStarted = $pStarted
        $appPid = if ($Plain) { $p.Id } else { 0 }
        $window = $null
        while (-not $p.HasExited -and $started.Elapsed.TotalSeconds -lt 40) {
            if (-not $app -and (Test-Path $pidFile)) {
                $appPid = [int](Get-Content $pidFile)
                try { $app = Get-Process -Id $appPid; $appStarted = $app.StartTime } catch { $app = $null }
            }
            if ($appPid) { try { $window = Find-Window $appPid } catch { $window = $null } }
            if ($window) { break }
            Start-Sleep -Milliseconds 100
        }

        $windowMs = if ($window) { [int]$started.Elapsed.TotalMilliseconds } else { -1 }
        $closeError = ''
        $outcome = 'closed'
        if ($window -and $WaitForProbe) {
            $waited = [Diagnostics.Stopwatch]::StartNew()
            while (-not $p.HasExited -and $waited.Elapsed.TotalSeconds -lt 30) {
                $text = Get-ChildItem $logFolder -Filter '*.log' -ErrorAction SilentlyContinue | Get-Content -Raw -ErrorAction SilentlyContinue
                if ($text -match 'Start-up: encoder answer applied') { break }
                Start-Sleep -Milliseconds 200
            }
        }
        if ($window) { $p.WaitForExit([int]($Dwell * 1000)) | Out-Null }

        if ($p.HasExited) {
            $outcome = if ($window) { 'exited-early' } else { 'died-before-window' }
        }
        elseif (-not $window) {
            $outcome = 'no-window'
        }
        else {
            $closeError = Close-Window $appPid
            if (-not $p.WaitForExit(10000)) {
                # A first Close the window did not act on (reported under load); ask once more, and say why if it fails.
                $second = Close-Window $appPid
                if ($second) { $closeError = ($closeError + ' / ' + $second).Trim(' /') }
                $outcome = if ($p.WaitForExit(10000)) { 'closed-on-retry' } else { 'hung' }
            }
        }

        if (-not $p.HasExited) {
            if (-not $Plain) {
                # The catcher writes every thread's stack, then the app and the catcher are stopped.
                New-Item -ItemType File "$pidFile.walk" -Force | Out-Null
                Start-Sleep -Seconds 4
            }
            Stop-Own $app $appStarted
            if (-not $Plain) { Stop-Own $p $pStarted }
        }

        $p.WaitForExit()
        $exit = $p.ExitCode
        $logFile = Get-ChildItem $logFolder -Filter '*.log' -ErrorAction SilentlyContinue | Select-Object -First 1
        $text = if ($logFile) { Get-Content $logFile.FullName -Raw } else { '' }
        $last = if ($logFile) { ((Get-Content $logFile.FullName | Where-Object { $_ -match '^\d{4}-' } | Select-Object -Last 1) -replace '^\S+\s+\S+\s+', '') -replace ',', ';' } else { '' }
        $firstFrameMs = Trace-Ms $text 'first frame rendering'
        $probeAppliedMs = Trace-Ms $text 'encoder answer applied to the Output panel'
        $probe = if ($text -match 'answered after a cold probe') { 'cold' } elseif ($text -match 'answered from its cache') { 'cache' } else { 'none' }

        # A hang's exit code is the stop's; any other non-zero exit is a crash, a crash on close included.
        if ($outcome -eq 'closed-on-retry') { $retries++ }
        if ($outcome -eq 'no-window') { $noWindow++ }
        elseif ($outcome -eq 'hung') { $hung++ }
        elseif ($exit -ne 0) { $crashes++ }
        $kept = $outcome -notin 'closed', 'closed-on-retry' -or $exit -ne 0
        if ($kept) {
            if ($logFile) { Copy-Item $logFile.FullName (Join-Path $Out "log-$run-$i-$outcome-p$appPid.log") }
            Write-Host ("Launch {0}: {1}, exit 0x{2:X8}; last log line: {3}" -f $i, $outcome, $exit, $last)
        }
        elseif (Test-Path $report) {
            Remove-Item $report -ErrorAction SilentlyContinue
        }

        # This launch's own log folder and, when cold, its own encoder cache.
        Remove-Item -Recurse -Force $logFolder -ErrorAction SilentlyContinue
        if ($ColdProbe) { Remove-Item $encoderCache -ErrorAction SilentlyContinue }

        '{0:s},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},0x{12:X8},{13},{14},{15}' -f (Get-Date), $mode, $i, $Load, $SampleFrames, [bool]$ColdProbe, [bool]$FreshCopy, $appPid, $windowMs, $firstFrameMs, $probe, $probeAppliedMs, $exit, $outcome, $closeError, $last | Add-Content $results
        if ($FreshCopy) { Remove-Item -Recurse -Force $fresh -ErrorAction SilentlyContinue }
    }
}
finally {
    for ($k = 0; $k -lt $loaders.Count; $k++) {
        $loader = $loaders[$k]
        $since = $loaderStarts[$k]
        # The loader's children, found while it still runs (so its process ID is its own), started after it did.
        $children = @()
        $loader.Refresh()
        if (-not $loader.HasExited) {
            $children = @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$($loader.Id)" | Where-Object { $_.CreationDate -ge $since } | ForEach-Object {
                try { $child = Get-Process -Id $_.ProcessId; [pscustomobject]@{ Process = $child; Started = $child.StartTime } } catch { }
            })
        }
        Stop-Own $loader $since
        foreach ($child in $children) { Stop-Own $child.Process $child.Started }
    }

    Remove-Item (Join-Path $data 'gpu-load.mp4') -ErrorAction SilentlyContinue

    # A copy is often still locked for a moment after its app exits (a scan, the loader); what was left goes now.
    Remove-Item -Recurse -Force (Join-Path $data 'fresh\*') -ErrorAction SilentlyContinue
}

$hangs = $noWindow + $hung
Write-Host "$Count $mode launches ($Load load, $SampleFrames sample frames, cold probe $([bool]$ColdProbe), fresh copy $([bool]$FreshCopy)): $crashes crashed (non-zero exit), $hangs hung ($noWindow with no window, $hung not closing), $retries closed on the second Close. Results: $results"
if ($crashes + $hangs -gt 0) { exit 1 }
