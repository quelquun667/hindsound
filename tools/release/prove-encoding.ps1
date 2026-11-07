<#
.SYNOPSIS
    Writes real MP3 files with a given libmp3lame DLL, through the tests already built, and reads them back.
.DESCRIPTION
    Puts the DLL in place of the one in the tests' output folder, then runs with --no-build the tests below. All of them
    write an MP3 with LAME through NAudio.Lame and open it with Mp3FileReader. Mp3_written_with_lame also decodes the
    whole file and compares its level with the sine that went in.
    The script fails if a test fails, if fewer tests ran than expected, or if the DLL in place changed during the run.
.PARAMETER DllPath
    The DLL to prove.
.PARAMETER TestsOutput
    The output folder of the test project.
.PARAMETER CheckTrx
    Only reads a results file, for test-release-scripts.ps1.
.EXAMPLE
    ./tools/release/prove-encoding.ps1 -DllPath lame/libmp3lame.64.dll -ResultsDir encoding-results
#>
param(
    [string]$DllPath,
    [string]$TestsOutput,
    [string]$ResultsDir,
    [string]$CheckTrx
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# 1 + 3 (128, 192 and 320 kbps) + 1 + 1.
$expectedPassed = 6
$filter = @(
    'FullyQualifiedName~EncodersTests.Mp3_starts_with_a_filled_lame_header'
    'FullyQualifiedName~EncodersTests.Mp3_written_with_lame_is_decoded'
    'FullyQualifiedName~EncodersTests.Mono_mp3_is_decoded_as_mono'
    'FullyQualifiedName~ClipSaverTests.A_snapshot_comes_out_as_an_mp3'
) -join '|'

function Assert-TrxResults([string]$Path) {
    [xml]$results = Get-Content -Raw -LiteralPath $Path
    $counters = $results.TestRun.ResultSummary.Counters
    Write-Output "Tests: $($counters.total) run, $($counters.passed) passed, $($counters.failed) failed."
    if ([int]$counters.failed -ne 0) {
        throw "$($counters.failed) encoding test(s) failed."
    }
    if ([int]$counters.passed -ne $expectedPassed) {
        throw "$($counters.passed) encoding test(s) passed, expected ${expectedPassed}: the filter no longer matches them."
    }
}

if ($CheckTrx) {
    Assert-TrxResults $CheckTrx
    return
}

if (-not $DllPath) { throw 'Give -DllPath.' }
$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).ProviderPath
if (-not $TestsOutput) {
    $TestsOutput = Join-Path $repository 'tests\Hindsound.Tests\bin\Release'
}
if (-not $ResultsDir) {
    $ResultsDir = Join-Path ([IO.Path]::GetTempPath()) ('hindsound-encoding-' + [Guid]::NewGuid().ToString('N'))
}

$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $DllPath).Hash

$inPlace = @(Get-ChildItem -LiteralPath $TestsOutput -Recurse -Filter 'libmp3lame.64.dll' -ErrorAction SilentlyContinue)
if ($inPlace.Count -eq 0) {
    throw "No libmp3lame.64.dll under ${TestsOutput}: build the tests first."
}
foreach ($file in $inPlace) {
    Copy-Item -LiteralPath $DllPath -Destination $file.FullName -Force
    Write-Output "In place: $($file.FullName)"
}

$project = Join-Path $repository 'tests\Hindsound.Tests\Hindsound.Tests.csproj'
& dotnet test $project -c Release --no-build -m:2 --filter $filter --logger 'trx;LogFileName=encoding.trx' --results-directory $ResultsDir
$testExit = $LASTEXITCODE

$trx = Join-Path $ResultsDir 'encoding.trx'
if (-not (Test-Path -LiteralPath $trx)) {
    throw "dotnet test left no results file (exit code $testExit)."
}
Assert-TrxResults $trx
if ($testExit -ne 0) {
    throw "dotnet test failed with exit code $testExit."
}

foreach ($file in $inPlace) {
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName).Hash -cne $hash) {
        throw "$($file.FullName) is not the DLL to prove any more: the run replaced it."
    }
}
Write-Output "The encoding works with $DllPath."
