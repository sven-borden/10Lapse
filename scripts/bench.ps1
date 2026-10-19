#Requires -Version 7.0
<#
.SYNOPSIS
    The benchmark (#34): time to the first thumbnails, index time, export speed and memory, preview pacing and
    filmstrip sweep, on generated photos. A person runs it on demand; it is not part of the gate.

.DESCRIPTION
        pwsh -File scripts/bench.ps1 -Quick          # 500 photos of 12 MP: QUICKTIME, writes about 5 GB, needs 11 GB free
        pwsh -File scripts/bench.ps1 -Full           # 5,000 photos of 24 MP: FULLTIME, writes about 52 GB, needs 56 GB free
        pwsh -File scripts/bench.ps1 -Photos 2000 -Size 5472x3648 -Keep
        pwsh -File scripts/bench.ps1 -Full -TightMemory   # the export with 4 frames decoded ahead and 3 threads

    Without a profile it runs -Quick. It builds the Spike (Release) and the app (Debug configuration, optimised, because
    the Debug switches are compiled out of Release), writes the photos with the Spike's bench-photos command (EXIF capture
    times and an embedded picture, as a camera writes them), then measures:

      1. the first thumbnails of a 1,000-photo folder (app, --import-bench). Target: the filmstrip shows within 2 s.
      2. the full index time, import done and metadata read, for 1,000 and for all the photos (app, --import-bench).
      3. export frames per second, all the photos to 4K H.264 (Spike, export-project).
      4. peak working set and private bytes during that export. Target: under 1 GB on PCs with 8 GB of RAM or less or an
         integrated GPU, which get 4 frames decoded ahead and 3 decode threads. Larger PCs decode 8 ahead on up to 6
         threads and may use more memory for speed, so their default-profile private bytes are reported as information.
         -TightMemory runs the export with 4 and 3 to measure the profile the target applies to, on any PC.
      5. preview playback pacing and the filmstrip sweep (app, --preview-bench and --filmstrip-bench, on 1,000 sample
         photos of 2400 x 1600; 300 with -Quick).

    It prints a Markdown section (machine, each measure against its target) and a one-line row for the table at the top of
    docs/benchmarks.md. The generated photos are deleted at the end unless -Keep is given (a later run with the same -Folder
    then reuses them). The app opens its window while it measures and closes it itself; touch nothing while it runs, and
    close other GPU work first: the export is the most sensitive (the script prints the GPU load it saw before it).

    Photos, the exported video, caches and the bench logs are written only under -Folder. The app also writes its
    normal log to %LOCALAPPDATA%Lapse\Logs, and the builds write bin and obj folders in the repository.

    A failed app start (some runs of the Debug app die at start-up, see issue #90) is reported with its exit code and the
    last line of the app log, and that run is tried once more. If it fails twice, rerun the script.

    The GPU load check before the export uses nvidia-smi, so it only works with an NVIDIA GPU; with any other GPU the
    script says nothing about it, so close other GPU work yourself.
#>
[CmdletBinding()]
param(
    # 500 photos of 12 MP.
    [switch]$Quick,
    # 5,000 photos of 24 MP.
    [switch]$Full,
    # Run the export with 4 frames decoded ahead and 3 decode threads, the profile of PCs with 8 GB of RAM or less or an
    # integrated GPU (the default picks 8 and up to 6 on bigger PCs).
    [switch]$TightMemory,
    # Overrides the profile's number of photos.
    [int]$Photos = 0,
    # Overrides the profile's photo size, like 6000x4000.
    [string]$Size = '',
    # Where the photos, the exported video and the logs go. Only the subfolder 10Lapse-bench-data is ever deleted.
    [string]$Folder = (Join-Path $env:LOCALAPPDATA 'Temp\10Lapse-bench'),
    # Keep the generated photos (a later run with the same -Folder reuses them).
    [switch]$Keep,
    [switch]$SkipBuild,
    # Leave steps out, comma separated (pwsh -File passes one string): import, noembedded, export, preview, filmstrip.
    [string]$SkipSteps = '',
    # How many times the 1,000-photo import is measured (the median counts).
    [int]$Runs = 3,
    # The release the results belong to.
    [string]$Release = '3.0-dev'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture

if ($Quick -and $Full) { throw 'Pick -Quick or -Full, not both.' }
$profileName = if ($Full) { 'Full' } else { 'Quick' }
if ($Photos -le 0) { $Photos = if ($Full) { 5000 } else { 500 } }
if (-not $Size) { $Size = if ($Full) { '6000x4000' } else { '4000x3000' } }
if ($Size -notmatch '^(\d+)x(\d+)$') { throw "-Size must look like 6000x4000, not '$Size'." }
$width = [int]$Matches[1]; $height = [int]$Matches[2]
$megapixels = $width * $height / 1e6
$firstCount = [Math]::Min(1000, $Photos)
$sampleFrames = if ($Full) { 1000 } else { 300 }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$Skip = @($SkipSteps -split '[,; ]+' | Where-Object { $_ })
foreach ($step in $Skip) { if ($step -notin 'import', 'noembedded', 'export', 'preview', 'filmstrip') { throw "-SkipSteps: unknown step '$step'." } }

$root = Split-Path -Parent $PSScriptRoot
$data = Join-Path $Folder '10Lapse-bench-data'
$logs = Join-Path $Folder "10Lapse-bench-logs\$stamp"

$userDotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
if (Test-Path (Join-Path $userDotnet 'dotnet.exe')) {
    $env:PATH = "$userDotnet;$env:PATH"
    $env:DOTNET_ROOT = $userDotnet
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_UI_LANGUAGE = 'en'

function Write-Step([string]$Text) { Write-Host "bench: $Text" -ForegroundColor Cyan }

# ---------------------------------------------------------------- disk

# 0.36 byte a pixel is what the Spike's grain 25 weighs (8.6 MB at 24 MP); the rest is the exported video (about 1 GB for
# 5,000 frames at 4K), the sample photos and a spare 5 GB.
$photoBytes = [long]($Photos * $width * $height * 0.36)
$extraBytes = if ($Skip -contains 'noembedded') { 0 } else { [long]($firstCount * $width * $height * 0.36) }
$needBytes = $photoBytes + $extraBytes + 2GB + 5GB
$drive = [System.IO.Path]::GetPathRoot([System.IO.Path]::GetFullPath($Folder))
$freeBytes = (New-Object System.IO.DriveInfo $drive).AvailableFreeSpace
Write-Step ("{0}: {1:N0} photos of {2}x{3} ({4:0.#} MP). Needs about {5:0.0} GB free on {6}; {7:0.0} GB free." -f $profileName, $Photos, $width, $height, $megapixels, ($needBytes / 1GB), $drive, ($freeBytes / 1GB))
if ($freeBytes -lt $needBytes) {
    $existing = if (Test-Path $data) { (Get-ChildItem $data -Recurse -File | Measure-Object Length -Sum).Sum } else { 0 }
    if (($freeBytes + $existing) -lt $needBytes) {
        throw ("Not enough disk space on {0}: {1:0.0} GB needed, {2:0.0} GB free. Use -Quick, a smaller -Photos or another -Folder." -f $drive, ($needBytes / 1GB), ($freeBytes / 1GB))
    }
}
# Only now, so a refusal leaves nothing behind.
New-Item -ItemType Directory -Force -Path $logs | Out-Null

# ---------------------------------------------------------------- helpers

function Invoke-Dotnet([string]$What, [string[]]$Arguments) {
    Write-Step $What
    $output = & dotnet @Arguments 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) {
        $output | Where-Object { $_.Trim() } | Select-Object -Last 40 | Write-Host
        throw "$What failed (exit $LASTEXITCODE)."
    }
}

function ConvertTo-Argument([string]$Text) { if ($Text -match '\s') { '"' + $Text + '"' } else { $Text } }

# Starts the app with the given Debug switches, waits for it to close its own window (the bench does), and kills only
# the process it started if it takes longer than $TimeoutMinutes. Some starts of the Debug app die before the bench
# reports (issue #90): when the app exits nonzero and wrote no report, the exit code and the last line of its log are
# printed and the run is tried once more.
function Invoke-App([string[]]$Arguments, [string]$ReportPath, [int]$TimeoutMinutes) {
    $quoted = $Arguments | ForEach-Object { ConvertTo-Argument $_ }
    for ($attempt = 1; $attempt -le 2; $attempt++) {
        if (Test-Path $ReportPath) { Remove-Item $ReportPath -Force }
        $process = Start-Process -FilePath $script:appExe -ArgumentList $quoted -PassThru
        try {
            if (-not $process.WaitForExit($TimeoutMinutes * 60 * 1000)) {
                throw "The app did not finish within $TimeoutMinutes minutes."
            }
        }
        finally {
            if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit(10000) | Out-Null }
        }
        if (Test-Path $ReportPath) { return (Get-Content $ReportPath -Raw) }

        $exitCode = $process.ExitCode
        $logLine = '(no log file found)'
        $logFile = Get-ChildItem (Join-Path $env:LOCALAPPDATA '10Lapse\Logs') -Filter "*.p$($process.Id).log" -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($logFile) { $logLine = "$logFile`: " + (Get-Content $logFile.FullName -Tail 5 | Where-Object { $_.Trim() } | Select-Object -Last 1) }
        $message = "The app wrote no report to $ReportPath (exit code $exitCode, hex 0x$('{0:X8}' -f $exitCode)). Last line of its log, $logLine"
        if ($attempt -eq 1 -and $exitCode -ne 0) {
            Write-Host "bench: WARNING $message" -ForegroundColor Yellow
            Write-Host 'bench: the app died at start-up (known issue #90); trying this run once more.' -ForegroundColor Yellow
            continue
        }
        throw $message
    }
}

function ConvertFrom-BenchLine([string]$Text, [string]$Kind) {
    $line = ($Text -split "`r?`n" | Where-Object { $_ -match "^BENCH $Kind " } | Select-Object -Last 1)
    if (-not $line) { return $null }
    $pairs = @{}
    foreach ($pair in ($line -replace "^BENCH $Kind ", '') -split ' ') {
        $index = $pair.IndexOf('=')
        if ($index -gt 0) { $pairs[$pair.Substring(0, $index)] = $pair.Substring($index + 1) }
    }
    return $pairs
}

function Get-Number($Pairs, [string]$Key) { [double]::Parse($Pairs[$Key], [System.Globalization.CultureInfo]::InvariantCulture) }

function Get-Median([double[]]$Values) {
    $sorted = @($Values | Sort-Object)
    if ($sorted.Count -eq 0) { return [double]::NaN }
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return $sorted[$middle] }
    return ($sorted[$middle - 1] + $sorted[$middle]) / 2
}

function Format-Ms([double]$Value) { if ([double]::IsNaN($Value) -or $Value -lt 0) { 'never' } elseif ($Value -ge 10000) { '{0:0.0} s' -f ($Value / 1000) } else { '{0:N0} ms' -f $Value } }

function Get-GpuLoad {
    $smi = Get-Command nvidia-smi -ErrorAction SilentlyContinue
    if (-not $smi) { return $null }
    try {
        $values = 1..3 | ForEach-Object { $load = & $smi.Source --query-gpu=utilization.gpu --format=csv,noheader,nounits 2>$null | Select-Object -First 1; Start-Sleep -Milliseconds 700; [int]"$load".Trim() }
        return ($values | Measure-Object -Maximum).Maximum
    }
    catch { return $null }
}

function Get-MachineDescription {
    $cpu = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name -replace '\s+', ' '
    $gpus = @(Get-CimInstance Win32_VideoController | Where-Object { $_.Name -notmatch 'Basic|Remote|Virtual|Parsec|Meta' } | ForEach-Object { "$($_.Name) (driver $($_.DriverVersion))" })
    $ramGb = [Math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB)
    $version = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    $os = "Windows $($version.DisplayVersion), build $($version.CurrentBuildNumber).$($version.UBR)"
    $disk = ''
    try {
        $letter = $script:drive.Substring(0, 1)
        $physical = Get-Partition -DriveLetter $letter | Get-Disk | Get-PhysicalDisk
        $disk = "$($physical.BusType) $($physical.MediaType)".Trim()
    }
    catch { }
    return [pscustomobject]@{ Cpu = $cpu; Gpu = ($gpus -join '; '); GpuShort = ($gpus | ForEach-Object { ($_ -replace ' \(driver.*', '') }) -join ' + '; RamGb = $ramGb; Os = $os; Disk = $disk }
}

# ---------------------------------------------------------------- build

$spikeExe = Join-Path $root 'tools\TenLapse.Spike\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\TenLapse.Spike.exe'
$appFolder = Join-Path $root 'src\TenLapse.App\bin\x64\Debug'
if (-not $SkipBuild) {
    Invoke-Dotnet 'building the Spike (Release, x64)' @('build', (Join-Path $root 'tools\TenLapse.Spike'), '-c', 'Release', '-p:Platform=x64', '-v:q', '-tl:off', '-nologo')
    # The Debug switches are compiled out of Release. Optimize=true makes the Debug configuration run as fast as Release
    # (the numbers are the product's, not the debugger's); --no-incremental because a changed property alone does not
    # recompile. The next plain Debug build recompiles again.
    Invoke-Dotnet 'building the app (Debug configuration, optimised, x64)' @('build', (Join-Path $root 'src\TenLapse.App'), '-c', 'Debug', '-p:Platform=x64', '-p:Optimize=true', '--no-incremental', '-v:q', '-tl:off', '-nologo')
}
if (-not (Test-Path $spikeExe)) { throw "The Spike is not built: $spikeExe" }
$script:appExe = (Get-ChildItem $appFolder -Recurse -Filter 'TenLapse.App.exe' -ErrorAction SilentlyContinue | Select-Object -First 1).FullName
if (-not $script:appExe) { throw "The app is not built under $appFolder." }

$commit = (& git -C $root rev-parse --short HEAD).Trim()
$dirty = (& git -C $root status --porcelain -- . ':!docs/benchmarks.md') -join ''
if ($dirty) { $commit += '+changes' }
$machine = Get-MachineDescription
$notes = [System.Collections.Generic.List[string]]::new()
$results = [ordered]@{}
$started = Get-Date

try {
    # ------------------------------------------------------------ photos

    function New-Photos([string]$Path, [int]$Count, [string[]]$Extra) {
        $have = if (Test-Path $Path) { @(Get-ChildItem $Path -Filter 'IMG_*.JPG').Count } else { 0 }
        if ($have -eq $Count) { Write-Step "reusing $Count photos in $Path"; return }
        Write-Step "writing $Count photos of $width x $height to $Path"
        & $spikeExe bench-photos --dir $Path --count $Count --size "${width}x${height}" @Extra 2>&1 | ForEach-Object { "$_" } | Tee-Object -FilePath (Join-Path $logs 'bench-photos.log') -Append | Select-Object -Last 3 | Write-Host
        if ($LASTEXITCODE -ne 0) { throw "bench-photos failed (exit $LASTEXITCODE)." }
    }

    $allFolder = Join-Path $data "photos-$Photos"
    $needPhotos = ($Skip -notcontains 'import') -or ($Skip -notcontains 'export')
    if ($needPhotos) { New-Photos $allFolder $Photos @() }

    # The first 1,000 photos again, as hard links of the same files (no disk), for the 1,000-photo measures.
    $firstFolder = $allFolder
    if (($firstCount -lt $Photos) -and ($Skip -notcontains 'import')) {
        $firstFolder = Join-Path $data "photos-first-$firstCount"
        if (-not (Test-Path $firstFolder) -or @(Get-ChildItem $firstFolder -Filter 'IMG_*.JPG').Count -ne $firstCount) {
            Write-Step "linking the first $firstCount photos into $firstFolder"
            New-Item -ItemType Directory -Force -Path $firstFolder | Out-Null
            Get-ChildItem $allFolder -Filter 'IMG_*.JPG' | Sort-Object Name | Select-Object -First $firstCount | ForEach-Object {
                $link = Join-Path $firstFolder $_.Name
                if (-not (Test-Path $link)) { New-Item -ItemType HardLink -Path $link -Target $_.FullName | Out-Null }
            }
        }
    }

    function Invoke-ImportBench([string]$PhotoFolder, [string]$Label) {
        $cache = Join-Path $data "cache-$Label"
        if (Test-Path $cache) { Remove-Item $cache -Recurse -Force }
        $report = Join-Path $logs "import-$Label.txt"
        $settings = Join-Path $data "settings-$Label"
        $text = Invoke-App @("--import-bench=$PhotoFolder", "--import-bench-out=$report", "--settings-folder=$settings", "--cache-folder=$cache") $report 15
        $pairs = ConvertFrom-BenchLine $text 'import'
        if (-not $pairs) { throw "The import bench ($Label) reported no result: $text" }
        return $pairs
    }

    # ------------------------------------------------------------ 1 and 2: first thumbnails, index

    if ($Skip -notcontains 'import') {
        $importRuns = @()
        for ($i = 1; $i -le $Runs; $i++) {
            Write-Step "import of $firstCount photos, run $i of $Runs"
            $importRuns += , (Invoke-ImportBench $firstFolder "first-$i")
        }
        $results['first'] = [pscustomobject]@{
            Count         = $firstCount
            FirstThumb    = Get-Median ($importRuns | ForEach-Object { Get-Number $_ 'firstThumbnailMs' })
            ViewFilled    = Get-Median ($importRuns | ForEach-Object { Get-Number $_ 'viewFilledMs' })
            ViewFilledMin = ($importRuns | ForEach-Object { Get-Number $_ 'viewFilledMs' } | Measure-Object -Minimum).Minimum
            ViewFilledMax = ($importRuns | ForEach-Object { Get-Number $_ 'viewFilledMs' } | Measure-Object -Maximum).Maximum
            FirstFrames   = Get-Median ($importRuns | ForEach-Object { Get-Number $_ 'firstFramesMs' })
            Done          = Get-Median ($importRuns | ForEach-Object { Get-Number $_ 'importDoneMs' })
            WithTime      = [int](Get-Number $importRuns[0] 'withCaptureTime')
            WithSize      = [int](Get-Number $importRuns[0] 'withSize')
            PeakPrivateMB = Get-Median ($importRuns | ForEach-Object { Get-Number $_ 'peakPrivateMB' })
            Runs          = $Runs
        }

        if ($Photos -gt $firstCount) {
            Write-Step "import of all $Photos photos"
            $all = Invoke-ImportBench $allFolder 'all'
            $results['all'] = [pscustomobject]@{
                Count         = $Photos
                Done          = Get-Number $all 'importDoneMs'
                FirstThumb    = Get-Number $all 'firstThumbnailMs'
                ViewFilled    = Get-Number $all 'viewFilledMs'
                WithTime      = [int](Get-Number $all 'withCaptureTime')
                WithSize      = [int](Get-Number $all 'withSize')
                PeakPrivateMB = Get-Number $all 'peakPrivateMB'
                SettledMB     = Get-Number $all 'settledPrivateMB'
            }
        }
    }

    # ------------------------------------------------------------ 1 again, with no embedded pictures

    if (($Skip -notcontains 'import') -and ($Skip -notcontains 'noembedded')) {
        $bare = Join-Path $data "photos-bare-$firstCount"
        New-Photos $bare $firstCount @('--no-thumbnail')
        Write-Step "import of $firstCount photos with no embedded picture"
        $pairs = Invoke-ImportBench $bare 'bare'
        $results['bare'] = [pscustomobject]@{
            Count      = $firstCount
            FirstThumb = Get-Number $pairs 'firstThumbnailMs'
            ViewFilled = Get-Number $pairs 'viewFilledMs'
            Done       = Get-Number $pairs 'importDoneMs'
        }
    }

    # ------------------------------------------------------------ 3 and 4: export

    if ($Skip -notcontains 'export') {
        $gpu = Get-GpuLoad
        if ($null -ne $gpu -and $gpu -gt 15) {
            $notes.Add("The GPU was $gpu % busy before the export started (other work on this PC): export speed is lower than on a quiet GPU. Rerun when it is idle.")
            Write-Host "bench: WARNING the GPU is $gpu % busy; the export speed will be low. Close other GPU work and rerun for a clean number." -ForegroundColor Yellow
        }
        elseif ($null -eq $gpu) {
            $notes.Add('The GPU load before the export was not measured (the check uses nvidia-smi, which only NVIDIA GPUs have): the speed assumes no other GPU work.')
        }
        $video = Join-Path $data 'export.mp4'
        Write-Step "exporting $Photos photos to 4K H.264 (Spike, ProjectExporter)"
        $exportLog = Join-Path $logs 'export.log'
        $exportArguments = @('export-project', '--photos', $allFolder, '--resolution', '2160', '--fps', '30', '--quality', 'High', '--out', $video)
        if ($TightMemory) {
            Write-Step 'tight-memory profile: 4 frames decoded ahead, 3 decode threads'
            $exportArguments += @('--decode-ahead', '4', '--decode-threads', '3')
        }
        $lines = @(& $spikeExe @exportArguments 2>&1 | ForEach-Object { "$_" } | Tee-Object -FilePath $exportLog)
        if ($LASTEXITCODE -ne 0) { $lines | Select-Object -Last 20 | Write-Host; throw "The export failed (exit $LASTEXITCODE)." }
        $lines | Select-Object -Last 4 | Write-Host
        if (Test-Path $video) { Remove-Item $video -Force }
        $pairs = ConvertFrom-BenchLine ($lines -join "`n") 'export'
        if (-not $pairs) { throw 'The export reported no BENCH line.' }
        $results['export'] = [pscustomobject]@{
            Frames    = [int](Get-Number $pairs 'frames')
            Seconds   = Get-Number $pairs 'seconds'
            Fps       = Get-Number $pairs 'fps'
            PeakWsMB  = Get-Number $pairs 'peakWorkingSetMB'
            PeakPvtMB = Get-Number $pairs 'peakPrivateMB'
            BaselineMB = Get-Number $pairs 'baselinePrivateMB'
            SampledPvtMB = Get-Number $pairs 'sampledPrivateMB'
            ManagedMB = Get-Number $pairs 'peakManagedMB'
            FileMB    = Get-Number $pairs 'fileMB'
            Encoder   = $pairs['encoder'] -replace '_', ' '
            Hardware  = $pairs['hardware']
            Width     = $pairs['width']
            Height    = $pairs['height']
            GpuBefore = $gpu
            Tight     = [bool]$TightMemory
        }
    }

    # ------------------------------------------------------------ 5: preview and filmstrip on sample photos

    $samples = Join-Path $data 'samples'
    $sampleCache = Join-Path $data 'cache-samples'
    if ($Skip -notcontains 'preview') {
        Write-Step "preview bench on $sampleFrames sample photos (about 4 minutes)"
        if (Test-Path $sampleCache) { Remove-Item $sampleCache -Recurse -Force }
        $report = Join-Path $logs 'preview.txt'
        $text = Invoke-App @("--sample-frames=$sampleFrames", "--sample-folder=$samples", '--preview-bench', "--preview-bench-out=$report", "--settings-folder=$(Join-Path $data 'settings-preview')", "--cache-folder=$sampleCache") $report 40
        $results['preview'] = $text
    }
    if ($Skip -notcontains 'filmstrip') {
        Write-Step "filmstrip bench on $sampleFrames sample photos (about 2 minutes)"
        if (Test-Path $sampleCache) { Remove-Item $sampleCache -Recurse -Force }
        $report = Join-Path $logs 'filmstrip.txt'
        $text = Invoke-App @("--sample-frames=$sampleFrames", "--sample-folder=$samples", '--filmstrip-bench', '--filmstrip-bench-seconds=20', "--filmstrip-bench-out=$report", "--settings-folder=$(Join-Path $data 'settings-filmstrip')", "--cache-folder=$sampleCache") $report 20
        $results['filmstrip'] = $text
    }
}
finally {
    if ($Keep) {
        Write-Step "keeping the generated photos in $data (pass the same -Folder next time to reuse them)"
    }
    elseif (Test-Path $data) {
        Write-Step "deleting the generated data in $data"
        Remove-Item $data -Recurse -Force -ErrorAction Continue
    }
}

# ---------------------------------------------------------------- report

function Get-Match([string]$Text, [string]$Pattern) { if ($Text -and $Text -match $Pattern) { return $Matches } return $null }

$cold = $null; $warm = $null; $fast = $null; $slow = $null; $filmSweep = $null; $filmJumps = $null; $filmMemory = $null; $previewMemory = $null
if ($results.Contains('preview')) {
    $text = $results['preview']
    $pattern = '{0}[^\r\n]*?: (\d+) of (\d+) frames shown, (\d+) dropped, (\d+) held[^\r\n]*late mean [\d.]+, min [\d.]+, p5 [\d.]+, median ([\d.]+), p95 ([\d.]+)'
    $cold = Get-Match $text ($pattern -f 'Cold playback')
    $warm = Get-Match $text ($pattern -f 'Warm playback')
    $fast = Get-Match $text 'Fast sweep[^\r\n]*?: (\d+) seeks, (\d+) shown while current \(([\d.]+) %\), seek to picture mean [\d.]+, min [\d.]+, p5 [\d.]+, median ([\d.]+), p95 ([\d.]+)'
    $previewMemory = Get-Match $text 'Memory: private bytes peak during the pass (\d+) MB, 2 s after it \(paused\) (\d+) MB;[^\r\n]*: (within|OVER)'
}
if ($results.Contains('filmstrip')) {
    $text = $results['filmstrip']
    $filmSweep = Get-Match $text 'Sweep over all (\d+) frames in (\d+) s[^\r\n]*?interval mean ([\d.]+), min [\d.]+, p5 [\d.]+, median ([\d.]+), p95 ([\d.]+), max ([\d.]+) \(n (\d+)\) ms, over 33 ms (\d+), over 50 ms (\d+)'
    $filmJumps = Get-Match $text 'Jumps of four views \((\d+)\): view filled after mean ([\d.]+), min [\d.]+, p5 [\d.]+, median ([\d.]+), p95 ([\d.]+)'
    $filmMemory = Get-Match $text 'After: settled private bytes (\d+) MB[^\r\n]*\(([+-]?\d+) MB against the baseline\)'
}

$profileLabel = if ($results.Contains('export') -and $results['export'].Tight) { "$profileName, tight-memory profile" } else { $profileName }
$targetMs = 2000
$limitMb = 1024
$date = Get-Date -Format 'yyyy-MM-dd'
$out = [System.Text.StringBuilder]::new()
function Add-Line([string]$Text = '') { [void]$out.AppendLine($Text) }

$rows = [System.Collections.Generic.List[string]]::new()
function Add-Row([string]$Measure, [string]$Target, [string]$Result, [string]$Verdict) { $rows.Add("| $Measure | $Target | $Result | $Verdict |") }

if ($results.Contains('first')) {
    $f = $results['first']
    $verdict = if ($f.Count -lt 1000) { "n/a ($($f.Count) photos; the target is for 1,000)" } elseif ($f.ViewFilled -le $targetMs) { 'met' } else { 'MISSED' }
    $spread = if ($f.Runs -gt 1) { ", median of $($f.Runs) (first view $(Format-Ms $f.ViewFilledMin) to $(Format-Ms $f.ViewFilledMax))" } else { '' }
    Add-Row "First thumbnails, $($f.Count) photos" 'filmstrip shows within 2 s' "first thumbnail $(Format-Ms $f.FirstThumb), first view filled $(Format-Ms $f.ViewFilled)$spread" $verdict
    Add-Row "Index, $($f.Count) photos (import done, metadata read)" 'none set' "$(Format-Ms $f.Done); capture time on $($f.WithTime), size on $($f.WithSize) of $($f.Count)" 'info'
}
if ($results.Contains('all')) {
    $a = $results['all']
    Add-Row "Index, $($a.Count) photos (import done, metadata read)" 'none set' "$(Format-Ms $a.Done); capture time on $($a.WithTime), size on $($a.WithSize) of $($a.Count); app private bytes peak $($a.PeakPrivateMB) MB, $($a.SettledMB) MB settled" 'info'
}
if ($results.Contains('bare')) {
    $b = $results['bare']
    Add-Row "First thumbnails, $($b.Count) photos with no embedded picture" 'none set' "first thumbnail $(Format-Ms $b.FirstThumb), first view filled $(Format-Ms $b.ViewFilled), import done $(Format-Ms $b.Done)" 'info'
}
if ($results.Contains('export')) {
    $e = $results['export']
    Add-Row "Export, $($e.Frames) photos x $([Math]::Round($megapixels)) MP to $($e.Width) x $($e.Height) H.264" 'none set' ("{0:0.0} frames/s ({1:0} s), {2}{3}, file {4:N0} MB" -f $e.Fps, $e.Seconds, $e.Encoder, $(if ($e.Hardware -eq 'True') { ' (hardware)' } else { ' (software)' }), $e.FileMB) 'info'
    $memoryText = "working set {0:N0} MB, private bytes {1:N0} MB (the process held {2:N0} MB before the export; managed heap peak {3:N0} MB)" -f $e.PeakWsMB, $e.PeakPvtMB, $e.BaselineMB, $e.ManagedMB
    if ($e.Tight) {
        # The 1 GB target is for PCs with 8 GB of RAM or less or an integrated GPU, which run 4 ahead and 3 threads.
        $memoryVerdict = if ($e.PeakWsMB -lt $limitMb -and $e.PeakPvtMB -lt $limitMb) { 'met' } else { 'MISSED' }
        $margin = ($limitMb - $e.PeakPvtMB) / $limitMb * 100
        Add-Row 'Export peak memory, tight-memory profile (4 ahead, 3 threads)' 'under 1 GB' ("$memoryText; {0:0} % under the limit" -f $margin) $memoryVerdict
    }
    else {
        # Bigger PCs decode 8 ahead on up to 6 threads and may use more memory for speed: information, not a verdict.
        Add-Row 'Export peak memory, default profile (8 ahead, up to 6 threads on this PC)' 'under 1 GB on 8 GB RAM or less or an integrated GPU only' $memoryText 'info'
    }
}
if ($cold) { Add-Row "Preview, cold playback (5 s, proxies being built), $sampleFrames sample photos" 'none set' "$($cold[1]) of $($cold[2]) frames shown, $($cold[3]) dropped; lateness median $($cold[5]) ms, p95 $($cold[6]) ms" 'info' }
if ($warm) { Add-Row "Preview, warm playback (10 s, proxies built), $sampleFrames sample photos" 'none set' "$($warm[1]) of $($warm[2]) frames shown, $($warm[3]) dropped; lateness median $($warm[5]) ms, p95 $($warm[6]) ms" 'info' }
if ($fast) { Add-Row 'Preview, fast sweep over the whole project in 4 s' 'none set' "$($fast[2]) of $($fast[1]) seeks shown while current ($($fast[3]) %); seek to picture median $($fast[4]) ms, p95 $($fast[5]) ms" 'info' }
if ($previewMemory) { Add-Row 'Preview, memory after the proxy pass' 'within the image budget' "private bytes peak $($previewMemory[1]) MB, $($previewMemory[2]) MB after: $($previewMemory[3])" $(if ($previewMemory[3] -eq 'within') { 'met' } else { 'MISSED' }) }
if ($filmSweep) { Add-Row "Filmstrip, sweep over $($filmSweep[1]) frames in $($filmSweep[2]) s" 'none set' "UI frame interval median $($filmSweep[4]) ms, p95 $($filmSweep[5]) ms, max $($filmSweep[6]) ms; $($filmSweep[8]) over 33 ms, $($filmSweep[9]) over 50 ms of $($filmSweep[7])" 'info' }
if ($filmJumps) { Add-Row 'Filmstrip, jumps of four views' 'none set' "view filled median $($filmJumps[3]) ms, p95 $($filmJumps[4]) ms ($($filmJumps[1]) jumps)" 'info' }
if ($filmMemory) { Add-Row 'Filmstrip, memory after the sweep' 'none set' "settled private bytes $($filmMemory[1]) MB ($($filmMemory[2]) MB against its baseline)" 'info' }

Add-Line "### $Release, $date, commit $commit ($profileLabel)"
Add-Line
Add-Line "- Machine: $($machine.Cpu); $($machine.Gpu); $($machine.RamGb) GB RAM; $($machine.Os)$(if ($machine.Disk) { "; photos on $($machine.Disk)" })."
Add-Line ("- Photos: {0:N0} JPEGs of {1} x {2} ({3:0.#} MP), EXIF capture times 5 s apart and an embedded picture, written by the Spike's bench-photos." -f $Photos, $width, $height, $megapixels)
Add-Line "- App benches ran on the Debug configuration built with Optimize=true (the switches are not in Release; Debug keeps asserts and unoptimised dependencies, so these numbers are an upper bound for Release); the export ran in the Spike, Release."
foreach ($note in $notes) { Add-Line "- $note" }
Add-Line
Add-Line '| Measure | Target | Result | Verdict |'
Add-Line '|---|---|---|---|'
foreach ($row in $rows) { Add-Line $row }
Add-Line

# One line for the table at the top of docs/benchmarks.md.
$firstText = if ($results.Contains('first')) { "$(Format-Ms $results['first'].ViewFilled) ($($results['first'].Count))" } else { '-' }
$index1 = if ($results.Contains('first')) { "$(Format-Ms $results['first'].Done) ($($results['first'].Count))" } else { '-' }
$indexAll = if ($results.Contains('all')) { "$(Format-Ms $results['all'].Done) ($($results['all'].Count))" } else { '-' }
$exportText = if ($results.Contains('export')) { '{0:0.0} fps' -f $results['export'].Fps } else { '-' }
$memText = if ($results.Contains('export')) { '{0:N0} / {1:N0} MB' -f $results['export'].PeakWsMB, $results['export'].PeakPvtMB } else { '-' }
$summary = "| $date | $Release | $commit | $($machine.GpuShort), $($machine.RamGb) GB | $profileLabel | $firstText | $index1 | $indexAll | $exportText | $memText |"

$report = $out.ToString()
Set-Content -Path (Join-Path $logs 'result.md') -Value ($report + "`n" + $summary + "`n")
Write-Host
Write-Host $report
Write-Host 'Row for the table at the top of docs/benchmarks.md:'
Write-Host $summary
Write-Host
Write-Host ("bench: done in {0:N1} minutes. Logs and result.md are in {1}." -f ((Get-Date) - $started).TotalMinutes, $logs)
