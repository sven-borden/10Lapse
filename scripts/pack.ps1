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

    Every pack then checks what it built and stays quiet when all is well. It
    exits 1, listing each problem, when the x64 or ARM64 package (or the bundle) has the
    wrong identity or version, a target other than Windows.Desktop 10.0.22000.0,
    any capability but runFullTrust, no Windows App Runtime 2 dependency, a
    file type association other than .10lapse named ms-resource:ProjectFileTypeName (#52), a
    resources.pri without the AppDescription string of every language, a
    language list other than the src/TenLapse.App/Strings/<lang> folders, a
    missing <lang>/<Assembly>.resources.dll for an X.<lang>.resx (every
    language stays in the main package, #113), or Debug-only code (the
    Debugging and Gallery types, or any of their launch switches, in the
    TenLapse assemblies).

    Every flight or Store upload needs a higher version than the last one, and a
    last digit of 0 (the Store reserves it):

        pwsh -File scripts/pack.ps1 -Version 3.0.1.0

    writes it into Package.appxmanifest, then packs. It refuses a version that is
    not four numbers, does not end in .0, or is not above the manifest's.
    -BumpOnly writes the version and stops (no build). -ManifestPath points the
    version change at another manifest; it exists for the self-check
    (tests/TenLapse.Tests/Code/PackVersionTests.cs) and is not needed otherwise.
#>
[CmdletBinding()]
param(
    [string]$Version,
    [switch]$BumpOnly,
    [string]$ManifestPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\TenLapse.App\TenLapse.App.csproj'
$outDir = Join-Path $root 'dist\store'
$manifestPath = if ($ManifestPath) { $ManifestPath } else { Join-Path $root 'src\TenLapse.App\Package.appxmanifest' }

function Stop-Pack([string]$message) {
    Write-Host "pack: FAILED, $message" -ForegroundColor Red
    exit 1
}

# Version rules of a Store upload: four numbers, a last digit of 0, above the last one.
$versionPattern = '(?s)(<Identity\b[^>]*?\sVersion=")([^"]*)(")'
function Test-UploadVersion([string]$candidate, [version]$current) {
    $parsed = $null
    if ($candidate -notmatch '^\d+\.\d+\.\d+\.\d+$' -or -not [version]::TryParse($candidate, [ref]$parsed)) {
        return "'$candidate' is not four numbers like 3.0.1.0"
    }
    if ($parsed.Revision -ne 0) { return "'$candidate' must end in .0 (the Store reserves the fourth part)" }
    if ($parsed.Major -gt 65535 -or $parsed.Minor -gt 65535 -or $parsed.Build -gt 65535) { return "'$candidate' has a part above 65535" }
    if ($parsed -le $current) { return "'$candidate' is not above the current version $current" }
    return $null
}

$manifestText = [System.IO.File]::ReadAllText($manifestPath)
$versionMatch = [regex]::Match($manifestText, $versionPattern)
$currentVersion = $null
if (-not $versionMatch.Success -or -not [version]::TryParse($versionMatch.Groups[2].Value, [ref]$currentVersion)) {
    Stop-Pack "$manifestPath has no readable Identity Version"
}
if ($Version) {
    $problem = Test-UploadVersion $Version $currentVersion
    if ($problem) { Stop-Pack "-Version $problem" }
    $bytes = [System.IO.File]::ReadAllBytes($manifestPath)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    # Write the parsed form, so 3.00.1.0 or 00003.0.1.0 reach the manifest as 3.0.1.0 (the Store may reject leading zeros).
    $normalized = ([version]$Version).ToString()
    $newText = $manifestText.Remove($versionMatch.Groups[2].Index, $versionMatch.Groups[2].Length).Insert($versionMatch.Groups[2].Index, $normalized)
    [System.IO.File]::WriteAllText($manifestPath, $newText, [System.Text.UTF8Encoding]::new($hasBom))
    Write-Host "pack: version $currentVersion -> $normalized in $manifestPath"
    $manifestText = $newText
    $currentVersion = [version]$normalized
}
if ($BumpOnly) {
    if (-not $Version) { Stop-Pack '-BumpOnly needs -Version' }
    exit 0
}
$manifestVersion = $currentVersion.ToString()

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

# What a Store upload must hold, checked in each architecture's package (the bundle's .msix parts). The function
# returns the problems it found as strings; none means the package is what the Store listing and 3.0 need.
$minWindows = '10.0.22000.0'
$latin1 = [System.Text.Encoding]::Latin1

function Read-ZipBytes([System.IO.Compression.ZipArchiveEntry]$zipEntry) {
    $buffer = [System.IO.MemoryStream]::new()
    $stream = $zipEntry.Open()
    try { $stream.CopyTo($buffer) } finally { $stream.Dispose() }
    return , $buffer.ToArray()
}

# The string literals the Debug-only code reads (the Debugging and Gallery folders), e.g. "--sample-frames". Release
# removes those folders from the build (TenLapse.App.csproj), so none may be left in a shipped assembly.
# Only whole "--x" literals are found: a switch built by concatenation or from a const prefix would be invisible.
function Get-DebugLiteral {
    $folders = Get-ChildItem (Join-Path $root 'src') -Directory -Recurse |
        Where-Object { $_.Name -in 'Debugging', 'Gallery' -and $_.FullName -notmatch '[\\/](obj|bin)[\\/]' }
    $literals = foreach ($folder in $folders) {
        foreach ($file in Get-ChildItem $folder.FullName -Filter '*.cs' -File -Recurse) {
            foreach ($m in [regex]::Matches([System.IO.File]::ReadAllText($file.FullName), '"(--[A-Za-z0-9][^"\s]*)"')) { $m.Groups[1].Value }
        }
    }
    return @($literals | Sort-Object -Unique)
}

# True when a managed assembly holds the exact string literal (the #US heap: a length byte, UTF-16, a flag byte), so a
# Release log line that merely mentions a switch does not count. $text is the assembly's bytes as Latin-1 characters.
function Test-ExactLiteral([string]$text, [string]$literal) {
    $utf16 = (($literal.ToCharArray() | ForEach-Object { "$_" }) -join "`0") + "`0"
    $length = 2 * $literal.Length + 1
    $prefix = if ($length -lt 0x80) { [string][char]$length } else { ([string][char](0x80 -bor ($length -shr 8))) + [char]($length -band 0xFF) }
    return $text.Contains($prefix + $utf16, [System.StringComparison]::Ordinal)
}

# The languages the sources hold (#113): each src/TenLapse.App/Strings/<lang>/Resources.resw, with its AppDescription text,
# and each <lang>/<Assembly>.resources.dll the X.<lang>.resx files under src/<Assembly>/Strings must produce.
function Get-AppDescription([string]$reswPath) {
    $node = ([xml](Get-Content $reswPath -Raw)).SelectSingleNode("//data[@name='AppDescription']/value")
    if (-not $node -or -not $node.InnerText.Trim()) { Stop-Pack "$reswPath has no AppDescription string" }
    return $node.InnerText.Trim()
}

function Get-SourceLanguage {
    $descriptions = [ordered]@{}
    foreach ($folder in Get-ChildItem (Join-Path $root 'src\TenLapse.App\Strings') -Directory | Sort-Object Name) {
        $resw = Join-Path $folder.FullName 'Resources.resw'
        if (Test-Path $resw) { $descriptions[$folder.Name] = Get-AppDescription $resw }
    }
    $satellites = foreach ($resx in Get-ChildItem (Join-Path $root 'src') -Filter '*.resx' -File -Recurse) {
        if ($resx.FullName -match '[\\/](obj|bin)[\\/]' -or $resx.Directory.Name -ne 'Strings') { continue }
        $parts = $resx.BaseName.Split('.')
        if ($parts.Count -lt 2) { continue }
        # The assembly is the project folder's name (src/TenLapse.Presentation/Strings/ProjectText.fr.resx).
        '{0}/{1}.resources.dll' -f $parts[-1], $resx.Directory.Parent.Name
    }
    return @{ Descriptions = $descriptions; Satellites = @($satellites | Sort-Object -Unique) }
}

function Get-ArchPackageProblem([System.IO.Compression.ZipArchive]$package, [string]$arch, [string[]]$debugLiterals, [System.Collections.IDictionary]$descriptions, [string[]]$satellites) {
    $problems = [System.Collections.Generic.List[string]]::new()
    $manifestEntry = $package.GetEntry('AppxManifest.xml')
    if (-not $manifestEntry) { return @("the $arch package has no AppxManifest.xml") }
    $built = [xml]::new()
    $built.Load([System.IO.MemoryStream]::new((Read-ZipBytes $manifestEntry)))
    $ns = [System.Xml.XmlNamespaceManager]::new($built.NameTable)
    $ns.AddNamespace('m', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
    $ns.AddNamespace('uap', 'http://schemas.microsoft.com/appx/manifest/uap/windows10')

    $id = $built.SelectSingleNode('//m:Identity', $ns)
    if ($id.GetAttribute('Name') -ne $expectedName -or $id.GetAttribute('Publisher') -ne $expectedPublisher) {
        $problems.Add("the $arch package Identity is $($id.GetAttribute('Name')) / $($id.GetAttribute('Publisher')), expected $expectedName / $expectedPublisher")
    }
    if ($id.GetAttribute('Version') -ne $manifestVersion) {
        $problems.Add("the $arch package Version is $($id.GetAttribute('Version')), the manifest says $manifestVersion")
    }
    $families = @($built.SelectNodes('//m:Dependencies/m:TargetDeviceFamily', $ns))
    if ($families.Count -ne 1 -or $families[0].GetAttribute('Name') -ne 'Windows.Desktop' -or $families[0].GetAttribute('MinVersion') -ne $minWindows) {
        $found = ($families | ForEach-Object { "$($_.GetAttribute('Name')) $($_.GetAttribute('MinVersion'))" }) -join ', '
        $problems.Add("the $arch package targets '$found'; it must be exactly Windows.Desktop $minWindows")
    }
    $capabilities = @($built.SelectNodes('//m:Capabilities/*', $ns) | ForEach-Object { $_.GetAttribute('Name') })
    if (($capabilities -join ',') -ne 'runFullTrust') {
        $problems.Add("the $arch package capabilities are '$($capabilities -join ', ')'; they must be exactly runFullTrust")
    }
    if (-not $built.SelectSingleNode("//m:PackageDependency[@Name='Microsoft.WindowsAppRuntime.2']", $ns)) {
        $problems.Add("the $arch package has no Microsoft.WindowsAppRuntime.2 dependency")
    }
    # The project file association (#52): .10lapse, named from the resources, so double-clicking a project opens it.
    $fileTypes = @($built.SelectNodes('//uap:Extension[@Category="windows.fileTypeAssociation"]//uap:FileType', $ns) | ForEach-Object { $_.InnerText.Trim() })
    if (($fileTypes -join ',') -ne '.10lapse') {
        $problems.Add("the $arch package associates the file types '$($fileTypes -join ', ')'; it must be exactly .10lapse")
    }
    $typeName = $built.SelectSingleNode('//uap:FileTypeAssociation/uap:DisplayName', $ns)
    if (-not $typeName -or $typeName.InnerText.Trim() -ne 'ms-resource:ProjectFileTypeName') {
        $problems.Add("the $arch package's .10lapse file type is not named ms-resource:ProjectFileTypeName")
    }
    $description = $built.SelectSingleNode('//uap:VisualElements', $ns).GetAttribute('Description')
    if ($description -ne 'ms-resource:AppDescription') {
        $problems.Add("the $arch package Description is '$description', expected ms-resource:AppDescription")
    }

    # Every language in the main package (#113): the manifest declares exactly the Strings/<lang> folders, and each
    # language's satellite assemblies sit beside the exe, where .NET looks (a language resource package would hide them).
    $declared = @($built.SelectNodes('//m:Resources/m:Resource[@Language]', $ns) | ForEach-Object { $_.GetAttribute('Language').ToUpperInvariant() } | Sort-Object -Unique)
    $expectedLanguages = @($descriptions.Keys | ForEach-Object { $_.ToUpperInvariant() } | Sort-Object -Unique)
    if (($declared -join ',') -ne ($expectedLanguages -join ',')) {
        $problems.Add("the $arch package declares the languages '$($declared -join ', ')'; the Strings folders are '$($expectedLanguages -join ', ')'")
    }
    foreach ($satellite in $satellites) {
        if (-not $package.GetEntry($satellite)) {
            $problems.Add("the $arch package has no $satellite, so that language would show in English")
        }
    }

    # resources.pri: the AppDescription resource exists and carries the text of every Strings/<lang>/Resources.resw.
    $pri = $package.GetEntry('resources.pri')
    if (-not $pri) {
        $problems.Add("the $arch package has no resources.pri, so ms-resource:AppDescription cannot resolve")
    }
    else {
        # Searched as bytes (the PRI and each needle as Latin-1, one char per byte): MRT stores a string as UTF-8 or as
        # UTF-16LE, and a translated description (é, ’, œ, a narrow no-break space) only matches in its real encoding.
        $priText = $latin1.GetString((Read-ZipBytes $pri))
        foreach ($needle in @('AppDescription') + @($descriptions.Values)) {
            $utf8 = $latin1.GetString([System.Text.Encoding]::UTF8.GetBytes($needle))
            $utf16 = $latin1.GetString([System.Text.Encoding]::Unicode.GetBytes($needle))
            if (-not $priText.Contains($utf8, [System.StringComparison]::Ordinal) -and -not $priText.Contains($utf16, [System.StringComparison]::Ordinal)) {
                $problems.Add("the $arch resources.pri does not hold '$needle'")
            }
        }
    }

    # No Debug-only code: the Debugging and Gallery namespaces and their launch switches, in our own assemblies.
    foreach ($assembly in $package.Entries | Where-Object { $_.FullName -match '^TenLapse[^/\\]*\.(dll|exe)$' }) {
        $assemblyText = $latin1.GetString((Read-ZipBytes $assembly))
        foreach ($namespace in 'TenLapse.Debugging', 'TenLapse.Gallery') {
            # The metadata strings heap holds a namespace as one string, so a sub-namespace (TenLapse.Gallery.Pages) is
            # found by the prefix.
            if ($assemblyText.Contains("`0$namespace`0", [System.StringComparison]::Ordinal) -or
                $assemblyText.Contains("`0$namespace.", [System.StringComparison]::Ordinal)) {
                $problems.Add("$arch $($assembly.FullName) holds the Debug-only namespace $namespace")
            }
        }
        foreach ($literal in $debugLiterals) {
            if (Test-ExactLiteral $assemblyText $literal) {
                $problems.Add("$arch $($assembly.FullName) holds the Debug switch '$literal'")
            }
        }
    }
    return $problems
}

# The upload wraps the bundle; check it holds both architectures and the Store identity, then each package in it.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$bundle = Get-ChildItem $outDir -Filter '*.msixbundle' -File -Recurse | Select-Object -First 1
$debugLiterals = Get-DebugLiteral
if ($debugLiterals.Count -eq 0) { Stop-Pack 'found no Debug launch switches in the sources, so the Debug-only code check cannot work; has the Debugging folder moved?' }
$sourceLanguages = Get-SourceLanguage
if (-not $sourceLanguages.Descriptions.Contains('en-US')) { Stop-Pack 'src\TenLapse.App\Strings\en-US\Resources.resw is missing' }
$packageProblems = [System.Collections.Generic.List[string]]::new()
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
    foreach ($arch in 'x64', 'arm64') {
        $partName = $parts | Where-Object { $_ -like "*_$arch.msix" } | Select-Object -First 1
        if (-not $partName) { continue }
        $partStream = [System.IO.MemoryStream]::new((Read-ZipBytes $zip.GetEntry($partName)))
        $package = [System.IO.Compression.ZipArchive]::new($partStream, [System.IO.Compression.ZipArchiveMode]::Read)
        try { foreach ($problem in (Get-ArchPackageProblem $package $arch $debugLiterals $sourceLanguages.Descriptions $sourceLanguages.Satellites)) { $packageProblems.Add($problem) } }
        finally { $package.Dispose() }
    }
}
finally { $zip.Dispose() }
foreach ($arch in 'x64', 'arm64') {
    if (-not ($parts | Where-Object { $_ -like "*_$arch.msix" })) {
        Write-Host "pack: FAILED, the bundle has no $arch package" -ForegroundColor Red
        exit 1
    }
}
if ($packageProblems.Count -gt 0) {
    Write-Host "pack: FAILED, $($packageProblems.Count) problem(s) in the packages:" -ForegroundColor Red
    $packageProblems | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
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
if (-not [version]::TryParse($bundleVersionText, [ref]$bundleVersion) -or $bundleVersion.Revision -ne 0 -or $bundleVersion -le $legacyVersion -or $bundleVersionText -ne $manifestVersion) {
    Write-Host "pack: FAILED, the bundle Version is '$bundleVersionText'; it must be $manifestVersion (the manifest), above $legacyVersion and ending in .0 (the Store reserves the fourth part)" -ForegroundColor Red
    exit 1
}

Write-Host ('pack: ok in {0:N1} s' -f ((Get-Date) - $started).TotalSeconds)
Write-Host "pack: version  $bundleVersionText"
Write-Host ('pack: bundle   {0:N1} MB  {1}' -f ($bundle.Length / 1MB), $bundle.FullName)
Write-Host ('pack: upload   {0:N1} MB  {1}' -f ($upload[0].Length / 1MB), $upload[0].FullName) -ForegroundColor Green
exit 0
