<#
.SYNOPSIS
    Tries check-tag, extract-changelog and write-manifest on their accepted and refused cases.
    Writes only in a new temporary folder, and fails if any case fails.
.EXAMPLE
    ./tools/release/test-release-scripts.ps1
#>

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$checkTag = Join-Path $PSScriptRoot 'check-tag.ps1'
$extract = Join-Path $PSScriptRoot 'extract-changelog.ps1'
$writeManifest = Join-Path $PSScriptRoot 'write-manifest.ps1'

$script:failures = 0

function Report([string]$Name, [bool]$Passed, [string]$Detail = '') {
    if ($Passed) {
        Write-Output "pass  $Name"
    }
    else {
        Write-Output "FAIL  $Name  $Detail"
        $script:failures++
    }
}

function Assert-Equal([string]$Name, $Expected, $Actual) {
    Report $Name ("$Expected" -ceq "$Actual") "expected <$Expected>, got <$Actual>"
}

function Assert-Refused([string]$Name, [scriptblock]$Action, [string]$Reason) {
    try {
        $null = & $Action
        Report $Name $false 'it was accepted'
    }
    catch {
        Report $Name ($_.Exception.Message -like "*$Reason*") "wrong reason: $($_.Exception.Message)"
    }
}

function Invoke-Git {
    # A runner has no git identity.
    & git -c user.name=test -c user.email=test@example.invalid @args
    if ($LASTEXITCODE -ne 0) { throw "git $args failed" }
}

$temp = Join-Path ([IO.Path]::GetTempPath()) ('hindsound-release-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null

try {
    # --- check-tag: format ---
    Assert-Equal 'tag v1.2.3' '1.2.3' (& $checkTag -Tag 'v1.2.3')
    Assert-Equal 'tag v0.0.10' '0.0.10' (& $checkTag -Tag 'v0.0.10')
    Assert-Equal 'tag v10.20.30' '10.20.30' (& $checkTag -Tag 'v10.20.30')
    $arabicDigit = [string][char]0x0661
    foreach ($bad in '1.2.3', 'v1.2', 'v1.2.3.4', 'v1.2.3-beta', 'V1.2.3', 'v01.2.3', 'v1.02.3', 'v1.2.x', 'xv1.2.3', "v1.2.3`n", "v$arabicDigit.2.3") {
        Assert-Refused "tag '$($bad.Trim())' refused" { & $checkTag -Tag $bad } 'not in the form'
    }

    # --- check-tag: the commit must be on main ---
    $repo = Join-Path $temp 'repo'
    New-Item -ItemType Directory -Path $repo | Out-Null
    Push-Location $repo
    try {
        Invoke-Git init -q -b main
        Invoke-Git commit -q --allow-empty -m first
        Invoke-Git tag -a v1.0.0 -m annotated
        Invoke-Git tag v1.0.1
        Invoke-Git checkout -q -b side
        Invoke-Git commit -q --allow-empty -m side
        Invoke-Git tag -a v1.0.2 -m 'not on main'
        Invoke-Git checkout -q main

        Assert-Equal 'annotated tag on main' '1.0.0' (& $checkTag -Tag 'v1.0.0' -MainRef main)
        Assert-Equal 'light tag on main' '1.0.1' (& $checkTag -Tag 'v1.0.1' -MainRef main)
        Assert-Refused 'tag off main refused' { & $checkTag -Tag 'v1.0.2' -MainRef main } 'not a commit of main'
        Assert-Refused 'unknown tag refused' { & $checkTag -Tag 'v9.9.9' -MainRef main } 'does not exist'
    }
    finally {
        Pop-Location
    }

    # --- extract-changelog ---
    $changelogLines = @(
        '# Changelog'
        ''
        '## [Unreleased]'
        ''
        '### Added'
        ''
        '- Not out yet.'
        ''
        '## [11.0.0] - 2026-12-01'
        ''
        '### Added'
        ''
        '- The wrong version.'
        ''
        '## [1.2.0] - 2026-11-06'
        ''
        '### Added'
        ''
        '- First entry.'
        ''
        '### Fixed'
        ''
        '- Second entry.'
        ''
        '## [1.1.0] - 2026-10-01'
        ''
        ''
        '## [1.0.1] - 2026-09-01'
        ''
        '### Fixed'
        ''
        '## [1.0.0] - 2026-08-01'
        ''
        '### Added'
        ''
        '- Last section.'
        ''
        '[1.2.0]: https://example.invalid/1.2.0'
        '[1.0.0]: https://example.invalid/1.0.0'
    )
    $expectedPresent = "### Added`n`n- First entry.`n`n### Fixed`n`n- Second entry."
    $expectedLast = "### Added`n`n- Last section."

    foreach ($eol in "`n", "`r`n") {
        $label = if ($eol -eq "`n") { 'LF' } else { 'CRLF' }
        $changelog = Join-Path $temp "changelog-$label.md"
        [IO.File]::WriteAllText($changelog, ($changelogLines -join $eol) + $eol)

        Assert-Equal "$label section present" $expectedPresent ((& $extract -Version '1.2.0' -Path $changelog) -join "`n")
        Assert-Equal "$label last section, without its link references" $expectedLast ((& $extract -Version '1.0.0' -Path $changelog) -join "`n")
        Assert-Refused "$label section absent" { & $extract -Version '9.9.9' -Path $changelog } 'has no section'
        Assert-Refused "$label section empty" { & $extract -Version '1.1.0' -Path $changelog } 'is empty'
        Assert-Refused "$label section with only headings" { & $extract -Version '1.0.1' -Path $changelog } 'is empty'
        Assert-Refused "$label partial version" { & $extract -Version '1.2' -Path $changelog } 'has no section'
        Assert-Refused "$label dot is not a wildcard" { & $extract -Version '1x2x0' -Path $changelog } 'has no section'
    }

    $emptyAtEnd = Join-Path $temp 'changelog-empty-end.md'
    [IO.File]::WriteAllText($emptyAtEnd, "# Changelog`n`n## [2.0.0]`n`n- Entry.`n`n## [1.0.0]`n`n[2.0.0]: https://example.invalid/2.0.0`n")
    Assert-Refused 'last section empty, with link references' { & $extract -Version '1.0.0' -Path $emptyAtEnd } 'is empty'

    $headingAtEnd = Join-Path $temp 'changelog-heading-end.md'
    [IO.File]::WriteAllText($headingAtEnd, "# Changelog`n`n## [1.0.0]")
    Assert-Refused 'heading on the very last line' { & $extract -Version '1.0.0' -Path $headingAtEnd } 'is empty'

    $bom = Join-Path $temp 'changelog-bom.md'
    [IO.File]::WriteAllText($bom, "## [1.0.0]`n- Entry with an accent: caf$([char]0xE9).`n", (New-Object Text.UTF8Encoding $true))
    $notes = Join-Path $temp 'notes.md'
    & $extract -Version '1.0.0' -Path $bom -OutFile $notes
    $notesBytes = [IO.File]::ReadAllBytes($notes)
    Assert-Equal 'notes file starts without a BOM' ([int][char]'-') $notesBytes[0]
    Assert-Equal 'notes file keeps the UTF-8 text' "- Entry with an accent: caf$([char]0xE9).`n" ([Text.Encoding]::UTF8.GetString($notesBytes))

    # --- write-manifest ---
    $setup = Join-Path $temp 'Hindsound-1.2.3-Setup.exe'
    [IO.File]::WriteAllBytes($setup, [byte[]](1..200 | ForEach-Object { $_ % 7 }))
    $manifestFile = Join-Path $temp 'hindsound-update-win-x64.json'
    & $writeManifest -Version '1.2.3' -SetupPath $setup -Repository 'owner/name' -OutFile $manifestFile

    $manifestBytes = [IO.File]::ReadAllBytes($manifestFile)
    Assert-Equal 'manifest starts without a BOM' ([int][char]'{') $manifestBytes[0]
    $manifest = [Text.Encoding]::UTF8.GetString($manifestBytes) | ConvertFrom-Json
    Assert-Equal 'manifest fields' 'product version url sha256 size published' (($manifest.PSObject.Properties | ForEach-Object Name) -join ' ')
    Assert-Equal 'manifest product' 'Hindsound' $manifest.product
    Assert-Equal 'manifest version' '1.2.3' $manifest.version
    Assert-Equal 'manifest url' 'https://github.com/owner/name/releases/download/v1.2.3/Hindsound-1.2.3-Setup.exe' $manifest.url
    Assert-Equal 'manifest size is the file size' (Get-Item -LiteralPath $setup).Length $manifest.size
    Assert-Equal 'manifest sha256 is the file hash' (Get-FileHash -Algorithm SHA256 -LiteralPath $setup).Hash.ToLowerInvariant() $manifest.sha256
    Report 'manifest sha256 is lower case' ($manifest.sha256 -cmatch '\A[0-9a-f]{64}\z')
    Report 'manifest published is UTC' ([Text.Encoding]::UTF8.GetString($manifestBytes) -cmatch'"published":\s*"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z"')
    Report 'manifest fits the 16 KB limit of the app' ($manifestBytes.Length -lt 16384)

    Assert-Refused 'missing Setup refused' { & $writeManifest -Version '1.2.3' -SetupPath (Join-Path $temp 'none.exe') -Repository 'owner/name' -OutFile (Join-Path $temp 'none.json') } 'none.exe'
    Report 'no manifest left after a refusal' (-not (Test-Path -LiteralPath (Join-Path $temp 'none.json')))
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force
}

if ($script:failures -gt 0) {
    throw "$($script:failures) release script case(s) failed."
}
Write-Output 'All release script cases passed.'
# The refused git cases leave a failure in $LASTEXITCODE, and a workflow step ends on that value.
exit 0
