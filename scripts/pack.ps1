#Requires -Version 7.0
<#
.SYNOPSIS
    Builds the Store upload: one .msixupload holding an .msixbundle of the x64
    and ARM64 Release packages.

.DESCRIPTION
    Run it from anywhere:

        pwsh -File scripts/pack.ps1

    The output goes to dist/store (git-ignored) and is unsigned, because the
    Store signs uploads. The script prints the path of the .msixupload to hand to
    Partner Center. It exits 1, with the build output, when no upload was produced.

    Packing needs no Visual Studio. The build prints the warning "Path to
    mspdbcmf.exe could not be found": only the symbols package is skipped, the
    packages are complete. AppxSymbolPackageEnabled=false is required: with the
    symbols package on, the StoreUpload step fails with MSB6011 (it needs
    mspdbcmf.exe) after the upload file is already written, and the build exits 1.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\TenLapse.App\TenLapse.App.csproj'
$outDir = Join-Path $root 'dist\store'

# Same handling as gate.ps1: the user install of the .NET 10 SDK wins when it exists.
$userDotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
if (Test-Path (Join-Path $userDotnet 'dotnet.exe')) {
    $env:PATH = "$userDotnet;$env:PATH"
    $env:DOTNET_ROOT = $userDotnet
}
elseif (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "pack: dotnet is not on PATH and not in $userDotnet. Install the .NET 10 SDK (global.json pins the version)." -ForegroundColor Red
    exit 1
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_UI_LANGUAGE = 'en'

# Start from an empty folder so a stale upload from an earlier version is never picked up.
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Path $outDir | Out-Null

# A trailing separator is required: the packaging targets concatenate paths onto it.
$packageDir = $outDir.TrimEnd('\') + '\'

$started = Get-Date
$buildArgs = @(
    'build', $project, '-c', 'Release', '-v:m', '-tl:off', '-nologo',
    '-p:GenerateAppxPackageOnBuild=true',
    '-p:UapAppxPackageBuildMode=StoreUpload',
    '-p:AppxBundle=Always',
    '-p:AppxBundlePlatforms=x64|arm64',
    '-p:AppxSymbolPackageEnabled=false',
    "-p:AppxPackageDir=$packageDir"
)
$output = & dotnet @buildArgs 2>&1 | ForEach-Object { "$_" }
$exitCode = $LASTEXITCODE

$upload = @(Get-ChildItem $outDir -Filter '*.msixupload' -File -Recurse)
if ($exitCode -ne 0 -or $upload.Count -ne 1) {
    Write-Host ('pack: FAILED (exit {0}, {1} .msixupload files)' -f $exitCode, $upload.Count) -ForegroundColor Red
    $output | Where-Object { $_.Trim() } | Write-Host
    exit 1
}

# The upload wraps the bundle; check the bundle holds both architectures.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$bundle = Get-ChildItem $outDir -Filter '*.msixbundle' -File -Recurse | Select-Object -First 1
$zip = [System.IO.Compression.ZipFile]::OpenRead($bundle.FullName)
try {
    $parts = @($zip.Entries | Where-Object { $_.FullName -like '*.msix' } | ForEach-Object { $_.FullName })
}
finally { $zip.Dispose() }
foreach ($arch in 'x64', 'arm64') {
    if (-not ($parts | Where-Object { $_ -like "*_$arch.msix" })) {
        Write-Host "pack: FAILED, the bundle has no $arch package" -ForegroundColor Red
        exit 1
    }
}

Write-Host ('pack: ok in {0:N1} s' -f ((Get-Date) - $started).TotalSeconds)
Write-Host ('pack: bundle   {0:N1} MB  {1}' -f ($bundle.Length / 1MB), $bundle.FullName)
Write-Host ('pack: upload   {0:N1} MB  {1}' -f ($upload[0].Length / 1MB), $upload[0].FullName) -ForegroundColor Green
exit 0
