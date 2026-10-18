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
    So the upload has no symbols package (mspdbcmf.exe ships only with Visual
    Studio): Store crash reports in Partner Center Health will not be symbolicated.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\TenLapse.App\TenLapse.App.csproj'
$outDir = Join-Path $root 'dist\store'
$manifestPath = Join-Path $root 'src\TenLapse.App\Package.appxmanifest'

# The identity of the existing Store listing. Partner Center rejects any other, so
# these literals are checked against the manifest and the built bundle on purpose.
$storeName = '62394glaglasven.10Lapse'
$storePublisher = 'CN=A4CDA1AE-2891-435C-B73F-FCA7B39485F4'
$legacyVersion = [version]'2.1.1.0'

$manifestIdentity = ([xml](Get-Content $manifestPath -Raw)).SelectSingleNode("//*[local-name()='Package']/*[local-name()='Identity']")
if ($manifestIdentity.GetAttribute('Name') -ne $storeName -or $manifestIdentity.GetAttribute('Publisher') -ne $storePublisher) {
    Write-Host ("pack: FAILED, Package.appxmanifest Identity is {0} / {1}, expected {2} / {3}" -f $manifestIdentity.GetAttribute('Name'), $manifestIdentity.GetAttribute('Publisher'), $storeName, $storePublisher) -ForegroundColor Red
    exit 1
}
$expectedName = $manifestIdentity.GetAttribute('Name')
$expectedPublisher = $manifestIdentity.GetAttribute('Publisher')

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
# Build from the repo root, wherever the script is started: dotnet resolves global.json
# (the SDK pin) from the working directory.
Push-Location $root
try {
    $output = & dotnet @buildArgs 2>&1 | ForEach-Object { "$_" }
    $exitCode = $LASTEXITCODE
}
finally { Pop-Location }

$upload = @(Get-ChildItem $outDir -Filter '*.msixupload' -File -Recurse)
if ($exitCode -ne 0 -or $upload.Count -ne 1) {
    Write-Host ('pack: FAILED (exit {0}, {1} .msixupload files)' -f $exitCode, $upload.Count) -ForegroundColor Red
    $output | Where-Object { $_.Trim() } | Write-Host
    exit 1
}

# The upload wraps the bundle; check it holds both architectures and the Store identity.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$bundle = Get-ChildItem $outDir -Filter '*.msixbundle' -File -Recurse | Select-Object -First 1
$zip = [System.IO.Compression.ZipFile]::OpenRead($bundle.FullName)
try {
    $parts = @($zip.Entries | Where-Object { $_.FullName -like '*.msix' } | ForEach-Object { $_.FullName })
    $entry = $zip.GetEntry('AppxMetadata/AppxBundleManifest.xml')
    if (-not $entry) {
        Write-Host 'pack: FAILED, the bundle has no AppxMetadata/AppxBundleManifest.xml' -ForegroundColor Red
        exit 1
    }
    $reader = [System.IO.StreamReader]::new($entry.Open())
    try { $bundleManifest = [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
}
finally { $zip.Dispose() }
foreach ($arch in 'x64', 'arm64') {
    if (-not ($parts | Where-Object { $_ -like "*_$arch.msix" })) {
        Write-Host "pack: FAILED, the bundle has no $arch package" -ForegroundColor Red
        exit 1
    }
}

$identity = $bundleManifest.SelectSingleNode("//*[local-name()='Identity']")
$bundleName = $identity.GetAttribute('Name')
$bundlePublisher = $identity.GetAttribute('Publisher')
$bundleVersionText = $identity.GetAttribute('Version')
$bundleVersion = $null
if ($bundleName -ne $expectedName) {
    Write-Host "pack: FAILED, the bundle Identity Name is '$bundleName', expected '$expectedName'" -ForegroundColor Red
    exit 1
}
if ($bundlePublisher -ne $expectedPublisher) {
    Write-Host "pack: FAILED, the bundle Identity Publisher is '$bundlePublisher', expected '$expectedPublisher'" -ForegroundColor Red
    exit 1
}
if (-not [version]::TryParse($bundleVersionText, [ref]$bundleVersion) -or $bundleVersion.Revision -ne 0 -or $bundleVersion -le $legacyVersion) {
    Write-Host "pack: FAILED, the bundle Version is '$bundleVersionText'; it must be above $legacyVersion and end in .0 (the Store reserves the fourth part)" -ForegroundColor Red
    exit 1
}

Write-Host ('pack: ok in {0:N1} s' -f ((Get-Date) - $started).TotalSeconds)
Write-Host "pack: version  $bundleVersionText"
Write-Host ('pack: bundle   {0:N1} MB  {1}' -f ($bundle.Length / 1MB), $bundle.FullName)
Write-Host ('pack: upload   {0:N1} MB  {1}' -f ($upload[0].Length / 1MB), $upload[0].FullName) -ForegroundColor Green
exit 0
