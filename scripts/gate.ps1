#Requires -Version 7.0
<#
.SYNOPSIS
    The merge gate: check the solution lists every project, restore, build x64
    Debug with warnings as errors, run every test, build x64 Release the same way.

.DESCRIPTION
    Replaces CI (CLAUDE.md, Build process). Run it from anywhere:

        pwsh -File scripts/gate.ps1

    Prints one line per step. On a failure it prints that step's output and
    exits 1; when everything passes it exits 0.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'TenLapse.slnx'

# The .NET 10 SDK is a user install. A shell started before it may not have it
# on PATH, or may find a machine-wide dotnet first that has runtimes but no SDK,
# so the user install wins whenever it exists.
$userDotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
if (Test-Path (Join-Path $userDotnet 'dotnet.exe')) {
    $env:PATH = "$userDotnet;$env:PATH"
    $env:DOTNET_ROOT = $userDotnet
}
elseif (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "gate: dotnet is not on PATH and not in $userDotnet. Install the .NET 10 SDK (global.json pins the version)." -ForegroundColor Red
    exit 1
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
# The test counts below are parsed from English output.
$env:DOTNET_CLI_UI_LANGUAGE = 'en'

$configuration = @('-c', 'Debug', '-p:Platform=x64')
$release = @('-c', 'Release', '-p:Platform=x64')
$quiet = @('-v:q', '-tl:off', '-nologo')

# Every csproj Git tracks must be in the solution, or the gate would not build or test it.
function Test-SolutionCoversProjects {
    $listed = & dotnet sln $solution list 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) { return @{ Ok = $false; Output = $listed } }
    $inSolution = @($listed | Where-Object { $_ -match '\.csproj\s*$' } |
            ForEach-Object { ($_.Trim() -replace '\\', '/').ToLowerInvariant() })
    $tracked = @(& git -C $root ls-files '*.csproj' | ForEach-Object { ($_.Trim() -replace '\\', '/') })
    $missing = @($tracked | Where-Object { $inSolution -notcontains $_.ToLowerInvariant() })
    if ($missing.Count -gt 0) {
        return @{ Ok = $false; Output = @($missing | ForEach-Object { "Project not in TenLapse.slnx: $_ (add it to the solution, with the same platform mappings as its neighbours)" }) }
    }
    return @{ Ok = $true; Output = @() }
}

$steps = @(
    @{ Name = 'solution'; Check = { Test-SolutionCoversProjects } }
    @{ Name = 'restore'; Args = @('restore', $solution, '-p:Platform=x64') + $quiet }
    @{ Name = 'build'; Args = @('build', $solution, '--no-restore', '-warnaserror') + $configuration + $quiet }
    @{ Name = 'test'; Args = @('test', '--solution', $solution, '--no-build') + $configuration }
    # Release restores itself: it is self-contained, so it needs the runtime pack the Debug restore skips.
    @{ Name = 'release'; Args = @('build', $solution, '-warnaserror') + $release + $quiet }
)

$started = Get-Date
foreach ($step in $steps) {
    $stepStarted = Get-Date
    if ($step.ContainsKey('Check')) {
        $result = & $step.Check
        $exitCode = [int](-not $result.Ok)
        $output = $result.Output
    }
    else {
        $output = & dotnet @($step.Args) 2>&1 | ForEach-Object { "$_" }
        $exitCode = $LASTEXITCODE
    }
    $seconds = ((Get-Date) - $stepStarted).TotalSeconds

    if ($exitCode -ne 0) {
        Write-Host ('gate: {0,-8} FAILED ({1:N1} s, exit {2})' -f $step.Name, $seconds, $exitCode) -ForegroundColor Red
        $output | Where-Object { $_.Trim() } | Write-Host
        exit 1
    }

    $detail = ''
    if ($step.Name -eq 'test') {
        $counts = @{}
        foreach ($line in $output) {
            if ($line -match '^\s*(total|failed|succeeded|skipped):\s*(\d+)') { $counts[$Matches[1]] = [int]$Matches[2] }
        }
        if (-not $counts.ContainsKey('total') -or $counts['total'] -eq 0) {
            Write-Host 'gate: test     FAILED (no tests ran)' -ForegroundColor Red
            $output | Where-Object { $_.Trim() } | Write-Host
            exit 1
        }
        $detail = '  {0} passed, {1} failed, {2} skipped' -f $counts['succeeded'], $counts['failed'], $counts['skipped']
    }
    Write-Host ('gate: {0,-8} ok ({1:N1} s){2}' -f $step.Name, $seconds, $detail)
}

Write-Host ('gate: green in {0:N1} s' -f ((Get-Date) - $started).TotalSeconds) -ForegroundColor Green
exit 0
