<#
.SYNOPSIS
    Prints the body of the "## [X.Y.Z]" section of a changelog, and refuses a section that is missing or empty.
.PARAMETER Version
    The version whose section is wanted, for example 0.1.0.
.PARAMETER Path
    The changelog to read.
.PARAMETER OutFile
    When given, the body goes to this file, in UTF-8 without a BOM, instead of the output.
.EXAMPLE
    ./tools/release/extract-changelog.ps1 -Version 0.1.0 -OutFile notes.md
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Path = 'CHANGELOG.md',
    [string]$OutFile
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$lines = [IO.File]::ReadAllLines((Resolve-Path -LiteralPath $Path).ProviderPath)

$heading = '^## \[' + [regex]::Escape($Version) + '\](\s|$)'
$start = -1
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match $heading) {
        $start = $i + 1
        break
    }
}
if ($start -lt 0) {
    throw "$Path has no section ## [$Version]."
}

$end = $start
while ($end -lt $lines.Length -and $lines[$end] -notmatch '^## ') {
    $end++
}

# The last section of a Keep a Changelog file is followed by its link references: they are not notes.
while ($end -gt $start -and $lines[$end - 1] -match '^\s*$|^\[[^\]]+\]:\s') {
    $end--
}
while ($start -lt $end -and $lines[$start] -match '^\s*$') {
    $start++
}

$body = @()
if ($end -gt $start) {
    $body = @($lines[$start..($end - 1)])
}

# A section with only "### Added" and no entry says nothing either.
if (-not ($body | Where-Object { $_ -notmatch '^\s*$|^#+\s' })) {
    throw "The section ## [$Version] of $Path is empty."
}

if ($OutFile) {
    $text = ($body -join "`n") + "`n"
    [IO.File]::WriteAllText($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutFile), $text, (New-Object Text.UTF8Encoding $false))
}
else {
    $body
}
