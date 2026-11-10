<#
.SYNOPSIS
    Tries the release scripts on their accepted and refused cases, and reads the workflows for their rules.
    Writes only in a new temporary folder, and fails if any case fails. Needs the NAudio.Lame package: run after a restore.
    Nothing here compiles, downloads, installs or starts a Setup: build-lame gets a fake compiler and check-setup a fake folder.
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

function Assert-Accepted([string]$Name, [scriptblock]$Action) {
    try {
        $null = & $Action
        Report $Name $true
    }
    catch {
        Report $Name $false "it was refused: $($_.Exception.Message)"
    }
}

# A refusal here is one failed case, not the end of the run.
function Assert-Returns([string]$Name, $Expected, [scriptblock]$Action) {
    try {
        Assert-Equal $Name $Expected (& $Action)
    }
    catch {
        Report $Name $false "it was refused: $($_.Exception.Message)"
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

    # --- check-remote-tag: the tag on the remote must still point at the commit built ---
    $checkRemote = Join-Path $PSScriptRoot 'check-remote-tag.ps1'
    $remoteRepo = Join-Path $temp 'remote.git'
    Invoke-Git init -q --bare $remoteRepo
    $local = Join-Path $temp 'local'
    New-Item -ItemType Directory -Path $local | Out-Null
    Push-Location $local
    try {
        Invoke-Git init -q -b main
        Invoke-Git remote add origin $remoteRepo
        Invoke-Git commit -q --allow-empty -m built
        $built = "$(& git rev-parse HEAD)"
        Invoke-Git commit -q --allow-empty -m fix
        $fix = "$(& git rev-parse HEAD)"
        Invoke-Git tag -a v1.0.0 -m annotated $built
        Invoke-Git tag v1.0.1 $built
        Invoke-Git push -q origin v1.0.0 v1.0.1

        Assert-Accepted 'an annotated tag still on the commit built passes' { & $checkRemote -Tag 'v1.0.0' -Commit $built }
        Assert-Accepted 'a light tag still on the commit built passes' { & $checkRemote -Tag 'v1.0.1' -Commit $built }
        Assert-Refused 'a commit other than the tag refused' { & $checkRemote -Tag 'v1.0.0' -Commit $fix } 'now points at'
        Invoke-Git tag -f -a v1.0.0 -m moved $fix
        Invoke-Git tag -f v1.0.1 $fix
        Invoke-Git push -q -f origin v1.0.0 v1.0.1
        Assert-Refused 'an annotated tag moved on the remote refused' { & $checkRemote -Tag 'v1.0.0' -Commit $built } 'now points at'
        Assert-Refused 'a light tag moved on the remote refused' { & $checkRemote -Tag 'v1.0.1' -Commit $built } 'now points at'
        Invoke-Git push -q origin :refs/tags/v1.0.1
        Assert-Refused 'a tag deleted on the remote refused' { & $checkRemote -Tag 'v1.0.1' -Commit $fix } 'not on origin'
        Assert-Refused 'a remote that cannot be read refused' { & $checkRemote -Tag 'v1.0.0' -Commit $fix -Remote (Join-Path $temp 'no-remote') } 'cannot be read'
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
    }

    # A heading that only a dot taken as a wildcard would match.
    $wildcard = Join-Path $temp 'changelog-wildcard.md'
    [IO.File]::WriteAllText($wildcard, "# Changelog`n`n## [1x2x0] - 2026-11-06`n`n- Other.`n")
    Assert-Refused 'dot is not a wildcard' { & $extract -Version '1.2.0' -Path $wildcard } 'has no section'

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

    # --- write-announce-manifest: the manifest of the Linux app and of the portable zip ---
    $writeAnnounce = Join-Path $PSScriptRoot 'write-announce-manifest.ps1'
    $announceFile = Join-Path $temp 'hindsound-update-linux-x64.json'
    & $writeAnnounce -Version '1.2.3' -Repository 'owner/name' -OutFile $announceFile
    $announceBytes = [IO.File]::ReadAllBytes($announceFile)
    Assert-Equal 'announce manifest starts without a BOM' ([int][char]'{') $announceBytes[0]
    $announce = [Text.Encoding]::UTF8.GetString($announceBytes) | ConvertFrom-Json
    Assert-Equal 'announce manifest fields' 'product version url published' (($announce.PSObject.Properties | ForEach-Object Name) -join ' ')
    Assert-Equal 'announce manifest product' 'Hindsound' $announce.product
    Assert-Equal 'announce manifest version' '1.2.3' $announce.version
    Assert-Equal 'announce manifest names the page of the release' 'https://github.com/owner/name/releases/tag/v1.2.3' $announce.url
    Report 'announce manifest published is UTC' ([Text.Encoding]::UTF8.GetString($announceBytes) -cmatch '"published":\s*"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z"')
    Assert-Refused 'announce manifest of a bad version refused' { & $writeAnnounce -Version '1.2' -Repository 'owner/name' -OutFile (Join-Path $temp 'none-announce.json') } 'Not a version'
    Assert-Refused 'announce manifest of a bad repository refused' { & $writeAnnounce -Version '1.2.3' -Repository 'owner/name/../x' -OutFile (Join-Path $temp 'none-announce.json') } 'Not owner/name'
    Report 'no announce manifest left after a refusal' (-not (Test-Path -LiteralPath (Join-Path $temp 'none-announce.json')))

    # --- build-portable-zip ---
    $buildZip = Join-Path $PSScriptRoot 'build-portable-zip.ps1'
    $repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
    $fakePublish = Join-Path $temp 'portable-publish'
    New-Item -ItemType Directory -Path $fakePublish | Out-Null
    [IO.File]::WriteAllText((Join-Path $fakePublish 'Hindsound.exe'), 'fake exe')
    [IO.File]::WriteAllText((Join-Path $fakePublish 'libmp3lame.64.dll'), 'fake dll')
    [IO.File]::WriteAllText((Join-Path $fakePublish 'Hindsound.pdb'), 'symbols, which the Setup does not install')
    function Get-ZipEntries([string]$Path) {
        Add-Type -AssemblyName System.IO.Compression
        $stream = [IO.File]::OpenRead($Path)
        try {
            $zip = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Read)
            try {
                foreach ($entry in $zip.Entries) {
                    $read = $entry.Open()
                    $memory = New-Object IO.MemoryStream
                    try { $read.CopyTo($memory) } finally { $read.Dispose() }
                    [pscustomobject]@{ Name = $entry.FullName; Bytes = $memory.ToArray() }
                }
            }
            finally {
                $zip.Dispose()
            }
        }
        finally {
            $stream.Dispose()
        }
    }
    function Get-BytesSha256([byte[]]$Bytes) {
        $sha = [Security.Cryptography.SHA256]::Create()
        try { ([BitConverter]::ToString($sha.ComputeHash($Bytes)) -replace '-', '').ToLowerInvariant() } finally { $sha.Dispose() }
    }
    $zipFile = Join-Path $temp 'zip-1\Hindsound-1.2.3-win-x64-portable.zip'
    $null = & $buildZip -PublishDir $fakePublish -Version '1.2.3' -OutFile $zipFile
    $zipEntries = @(Get-ZipEntries $zipFile)
    $top = 'Hindsound-1.2.3-win-x64-portable'
    [string[]]$expectedEntries = @("$top/Hindsound.exe", "$top/libmp3lame.64.dll", "$top/LICENSE", "$top/THIRD-PARTY-NOTICES.md", "$top/portable.txt", "$top/README.txt") +
        @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'licenses') -Filter '*.txt' -File | ForEach-Object { "$top/licenses/$($_.Name)" })
    [Array]::Sort($expectedEntries, [StringComparer]::Ordinal)
    Assert-Equal 'the zip holds one folder with the files of the Setup, portable.txt and README.txt' ($expectedEntries -join ' | ') (($zipEntries | ForEach-Object Name) -join ' | ')
    $sourceOf = @{
        "$top/Hindsound.exe" = Join-Path $fakePublish 'Hindsound.exe'
        "$top/libmp3lame.64.dll" = Join-Path $fakePublish 'libmp3lame.64.dll'
        "$top/LICENSE" = Join-Path $repoRoot 'LICENSE'
        "$top/THIRD-PARTY-NOTICES.md" = Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md'
        "$top/portable.txt" = Join-Path $repoRoot 'packaging\windows\portable.txt'
        "$top/README.txt" = Join-Path $repoRoot 'packaging\windows\README.txt'
    }
    foreach ($entry in $zipEntries) {
        $source = if ($sourceOf.ContainsKey($entry.Name)) { $sourceOf[$entry.Name] } else { Join-Path $repoRoot ('licenses\' + $entry.Name.Substring("$top/licenses/".Length)) }
        Assert-Equal "the zip entry $($entry.Name) has the bytes of its source" (Get-FileHash -Algorithm SHA256 -LiteralPath $source).Hash.ToLowerInvariant() (Get-BytesSha256 $entry.Bytes)
    }
    # What the Setup installs, read from its script, is in the zip too; and the Setup never installs portable.txt.
    $issText = [IO.File]::ReadAllText((Join-Path $repoRoot 'installer\Hindsound.iss'))
    $issSources = @([regex]::Matches($issText, '(?m)^Source: "\{#(?:RepoRoot|PublishDir)\}\\([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
    Assert-Equal 'the Setup installs five kinds of file' 5 $issSources.Count
    foreach ($issSource in $issSources) {
        $pattern = "$top/" + [regex]::Escape($issSource.Replace('\', '/')).Replace('\*', '[^/]+')
        Report "the zip holds what the Setup installs from $issSource" (@($zipEntries | Where-Object { $_.Name -cmatch "\A$pattern\z" }).Count -ge 1)
    }
    Report 'the Setup never installs portable.txt' (-not ($issText -match 'portable'))
    $zipAgain = Join-Path $temp 'zip-2\Hindsound-1.2.3-win-x64-portable.zip'
    $null = & $buildZip -PublishDir $fakePublish -Version '1.2.3' -OutFile $zipAgain
    Assert-Equal 'the same commit gives the same zip' (Get-FileHash -LiteralPath $zipFile).Hash (Get-FileHash -LiteralPath $zipAgain).Hash
    Assert-Refused 'a zip of another name refused' { & $buildZip -PublishDir $fakePublish -Version '1.2.3' -OutFile (Join-Path $temp 'zip-3\Hindsound-1.2.4-win-x64-portable.zip') } 'must be named'
    Assert-Refused 'a zip of a bad version refused' { & $buildZip -PublishDir $fakePublish -Version '1.2' -OutFile (Join-Path $temp 'zip-3\Hindsound-1.2-win-x64-portable.zip') } 'Not a version'
    $noExe = Join-Path $temp 'portable-publish-no-exe'
    New-Item -ItemType Directory -Path $noExe | Out-Null
    [IO.File]::WriteAllText((Join-Path $noExe 'libmp3lame.64.dll'), 'fake dll')
    Assert-Refused 'a published folder without the exe refused' { & $buildZip -PublishDir $noExe -Version '1.2.3' -OutFile (Join-Path $temp 'zip-3\Hindsound-1.2.3-win-x64-portable.zip') } 'Missing for the zip'
    Report 'no zip left after a refusal' (-not (Test-Path -LiteralPath (Join-Path $temp 'zip-3\Hindsound-1.2.3-win-x64-portable.zip')))
    # The read-back: a source changed after the zip is written leaves an entry with other bytes, and the zip is refused.
    $changedPublish = Join-Path $temp 'portable-publish-changed'
    New-Item -ItemType Directory -Path $changedPublish | Out-Null
    foreach ($name in 'Hindsound.exe', 'libmp3lame.64.dll') {
        [IO.File]::WriteAllText((Join-Path $changedPublish $name), "fake $name")
    }
    $changedZip = Join-Path $temp 'zip-4\Hindsound-1.2.3-win-x64-portable.zip'
    Assert-Refused 'a zip whose dll differs from its source refused' { & $buildZip -PublishDir $changedPublish -Version '1.2.3' -OutFile $changedZip -AfterWrite { [IO.File]::WriteAllText((Join-Path $changedPublish 'libmp3lame.64.dll'), 'another dll') } } 'libmp3lame.64.dll in the zip has the SHA-256'
    Report 'no zip left after a read-back refusal' (-not (Test-Path -LiteralPath $changedZip))
    Report 'no partial zip left after a read-back refusal' (-not (Test-Path -LiteralPath "$changedZip.partial"))
    [IO.File]::WriteAllText((Join-Path $changedPublish 'libmp3lame.64.dll'), 'fake libmp3lame.64.dll')
    Assert-Refused 'a zip whose exe differs from its source refused' { & $buildZip -PublishDir $changedPublish -Version '1.2.3' -OutFile $changedZip -AfterWrite { [IO.File]::WriteAllText((Join-Path $changedPublish 'Hindsound.exe'), 'another exe') } } 'Hindsound.exe in the zip has the SHA-256'
    Assert-Accepted 'a zip whose sources stay the same accepted with the hook' { & $buildZip -PublishDir $changedPublish -Version '1.2.3' -OutFile (Join-Path $temp 'zip-5\Hindsound-1.2.3-win-x64-portable.zip') -AfterWrite { } }

    # --- write-release-notes ---
    $writeNotes = Join-Path $PSScriptRoot 'write-release-notes.ps1'
    $changelogPart = Join-Path $temp 'changelog-part.md'
    [IO.File]::WriteAllText($changelogPart, "### Added`n`n- Something new.`n")
    $releaseNotes = Join-Path $temp 'release-notes-out.md'
    & $writeNotes -Version '1.2.3' -ChangelogFile $changelogPart -OutFile $releaseNotes
    $releaseNotesText = [IO.File]::ReadAllText($releaseNotes)
    Report 'the notes start with the changelog' ($releaseNotesText.StartsWith("### Added`n`n- Something new.`n`n## Downloads`n"))
    foreach ($name in 'Hindsound-1.2.3-Setup.exe', 'Hindsound-1.2.3-win-x64-portable.zip', 'hindsound-1.2.3-linux-x64.AppImage', 'hindsound-1.2.3-linux-x64.tar.gz', 'hindsound-update-win-x64.json', 'hindsound-update-win-x64-portable.json', 'hindsound-update-linux-x64.json', 'lame-3.100.tar.gz', 'SHA256SUMS') {
        Report "the notes say whom $name is for" ($releaseNotesText -cmatch "(?m)^\| [^\n]*``$([regex]::Escape($name))``[^\n]*\| [^|\n]+\. \|$")
    }
    Report 'the notes have no BOM and no carriage return' (([IO.File]::ReadAllBytes($releaseNotes)[0] -eq [byte][char]'#') -and -not $releaseNotesText.Contains("`r"))
    [IO.File]::WriteAllText((Join-Path $temp 'changelog-empty.md'), "`n")
    Assert-Refused 'notes without a changelog refused' { & $writeNotes -Version '1.2.3' -ChangelogFile (Join-Path $temp 'changelog-empty.md') -OutFile (Join-Path $temp 'notes-none.md') } 'is empty'

    # --- assemble-release and check-release-files: the files of the release, gathered, then downloaded back ---
    $assemble = Join-Path $PSScriptRoot 'assemble-release.ps1'
    $checkFiles = Join-Path $PSScriptRoot 'check-release-files.ps1'
    $filesNumber = 0
    function Get-OrdinalNames([string]$Folder) {
        [string[]]$names = @(Get-ChildItem -LiteralPath $Folder | ForEach-Object Name)
        [Array]::Sort($names, [StringComparer]::Ordinal)
        , $names
    }
    function Write-Sums([string]$Folder, [string[]]$Names) {
        $text = ($Names | ForEach-Object { "$((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $Folder $_)).Hash.ToLowerInvariant())  $_`n" }) -join ''
        [IO.File]::WriteAllText((Join-Path $Folder 'SHA256SUMS'), $text, (New-Object Text.UTF8Encoding $false))
    }
    # The folders of the two jobs, as their artifacts hold them.
    function New-JobFiles([string]$ManifestVersion = '1.2.3', [string]$Repository = 'owner/name') {
        $script:filesNumber++
        $root = Join-Path $temp "release-files-$($script:filesNumber)"
        $windows = Join-Path $root 'windows'
        $linux = Join-Path $root 'linux'
        New-Item -ItemType Directory -Path $windows, $linux | Out-Null
        $file = Join-Path $windows 'Hindsound-1.2.3-Setup.exe'
        [IO.File]::WriteAllBytes($file, [byte[]](1..200 | ForEach-Object { $_ % 7 }))
        & $writeManifest -Version $ManifestVersion -SetupPath $file -Repository $Repository -OutFile (Join-Path $windows 'hindsound-update-win-x64.json')
        Copy-Item -LiteralPath $zipFile -Destination (Join-Path $windows 'Hindsound-1.2.3-win-x64-portable.zip')
        [IO.File]::WriteAllText((Join-Path $windows 'lame-3.100.tar.gz'), 'fake source')
        [IO.File]::WriteAllText((Join-Path $linux 'hindsound-1.2.3-linux-x64.AppImage'), 'fake AppImage')
        [IO.File]::WriteAllText((Join-Path $linux 'hindsound-1.2.3-linux-x64.tar.gz'), 'fake archive')
        Write-Sums $linux @('hindsound-1.2.3-linux-x64.AppImage', 'hindsound-1.2.3-linux-x64.tar.gz')
        [IO.File]::WriteAllText((Join-Path $linux 'lame-3.100.tar.gz'), 'fake source')
        $root
    }
    function Invoke-Assemble([string]$Root) {
        & $assemble -Version '1.2.3' -Repository 'owner/name' -WindowsFolder (Join-Path $Root 'windows') -LinuxFolder (Join-Path $Root 'linux') -OutFolder (Join-Path $Root 'release')
    }
    function New-ReleaseFiles([string]$ManifestVersion = '1.2.3', [string]$Repository = 'owner/name') {
        $root = New-JobFiles $ManifestVersion $Repository
        $null = Invoke-Assemble $root
        Join-Path $root 'release'
    }
    $releaseFiles = New-ReleaseFiles
    $fakeLameSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $releaseFiles 'lame-3.100.tar.gz')).Hash.ToLowerInvariant()
    function Test-ReleaseFiles([string]$Folder, [string]$Expected = '') {
        & $checkFiles -Folder $Folder -Version '1.2.3' -Repository 'owner/name' -LameSha256 $fakeLameSha256 -Expected $Expected
    }
    $allNames = @('Hindsound-1.2.3-Setup.exe', 'Hindsound-1.2.3-win-x64-portable.zip', 'SHA256SUMS', 'hindsound-1.2.3-linux-x64.AppImage', 'hindsound-1.2.3-linux-x64.tar.gz', 'hindsound-update-linux-x64.json', 'hindsound-update-win-x64-portable.json', 'hindsound-update-win-x64.json', 'lame-3.100.tar.gz')
    Assert-Equal 'the release gathers the files of both jobs, the announce manifests and SHA256SUMS' ($allNames -join ' ') ((Get-OrdinalNames $releaseFiles) -join ' ')
    Assert-Accepted 'the files of the release pass' { Test-ReleaseFiles $releaseFiles }
    $sumsBytes = [IO.File]::ReadAllBytes((Join-Path $releaseFiles 'SHA256SUMS'))
    $sumsLines = [Text.Encoding]::UTF8.GetString($sumsBytes).TrimEnd("`n").Split("`n")
    Assert-Equal 'SHA256SUMS lists every other file, in ordinal order' (($allNames | Where-Object { $_ -cne 'SHA256SUMS' }) -join ' ') (($sumsLines | ForEach-Object { $_.Substring(66) }) -join ' ')
    Report 'SHA256SUMS is in the form of sha256sum, with LF only and no BOM' (($sumsBytes[0] -ne 0xEF) -and -not ($sumsBytes -contains 13) -and ($sumsBytes[-1] -eq 10) -and @($sumsLines | Where-Object { $_ -cnotmatch '\A[0-9a-f]{64}  \S+\z' }).Count -eq 0)
    Assert-Equal 'the release names the page of the release for the Linux app' 'https://github.com/owner/name/releases/tag/v1.2.3' ([IO.File]::ReadAllText((Join-Path $releaseFiles 'hindsound-update-linux-x64.json')) | ConvertFrom-Json).url
    Assert-Equal 'the release names the page of the release for the portable zip' 'https://github.com/owner/name/releases/tag/v1.2.3' ([IO.File]::ReadAllText((Join-Path $releaseFiles 'hindsound-update-win-x64-portable.json')) | ConvertFrom-Json).url

    # assemble-release refuses what the jobs should not have made.
    $jobFiles = New-JobFiles
    [IO.File]::WriteAllText((Join-Path $jobFiles 'linux\hindsound-1.2.3-linux-x64.AppImage'), 'damaged AppImage')
    Assert-Refused 'a Linux package that does not match its SHA256SUMS refused' { Invoke-Assemble $jobFiles } 'the Linux SHA256SUMS gives'
    $jobFiles = New-JobFiles
    [IO.File]::WriteAllText((Join-Path $jobFiles 'linux\lame-3.100.tar.gz'), 'another source')
    Assert-Refused 'two different LAME sources refused' { Invoke-Assemble $jobFiles } 'differs between the jobs'
    $jobFiles = New-JobFiles
    [IO.File]::WriteAllText((Join-Path $jobFiles 'windows\Hindsound.pdb'), 'not a file of the release')
    Assert-Refused 'a file more from the Windows job refused' { Invoke-Assemble $jobFiles } 'The Windows files are'
    $jobFiles = New-JobFiles
    Remove-Item -LiteralPath (Join-Path $jobFiles 'linux\SHA256SUMS')
    Assert-Refused 'Linux packages without their SHA256SUMS refused' { Invoke-Assemble $jobFiles } 'The Linux files are'
    $jobFiles = New-JobFiles
    New-Item -ItemType Directory -Path (Join-Path $jobFiles 'release') | Out-Null
    [IO.File]::WriteAllText((Join-Path $jobFiles 'release\left.txt'), 'left by an earlier run')
    Assert-Refused 'a folder of the release that is not empty refused' { Invoke-Assemble $jobFiles } 'is not empty'

    # check-release-files refuses a release that is not exactly right.
    Assert-Refused 'another LAME source refused' { & $checkFiles -Folder $releaseFiles -Version '1.2.3' -Repository 'owner/name' } 'lame-3.100.tar.gz on the release'
    $releaseFiles = New-ReleaseFiles
    [IO.File]::WriteAllText((Join-Path $releaseFiles 'Hindsound-1.2.2-Setup.exe'), 'left by an earlier run')
    Assert-Refused 'a file more on the release refused' { Test-ReleaseFiles $releaseFiles } 'expected'
    $releaseFiles = New-ReleaseFiles
    Remove-Item -LiteralPath (Join-Path $releaseFiles 'lame-3.100.tar.gz')
    Assert-Refused 'a file missing on the release refused' { Test-ReleaseFiles $releaseFiles } 'expected'
    $releaseFiles = New-ReleaseFiles
    Remove-Item -LiteralPath (Join-Path $releaseFiles 'hindsound-1.2.3-linux-x64.AppImage')
    Assert-Refused 'a release without the AppImage refused' { Test-ReleaseFiles $releaseFiles } 'expected'
    $releaseFiles = New-ReleaseFiles '1.2.4'
    Assert-Refused 'a manifest of another version refused' { Test-ReleaseFiles $releaseFiles } 'the version 1.2.4'
    $releaseFiles = New-ReleaseFiles '1.2.3' 'other/name'
    Assert-Refused 'a manifest with another address refused' { Test-ReleaseFiles $releaseFiles } 'the address'
    $releaseFiles = New-ReleaseFiles
    [IO.File]::AppendAllText((Join-Path $releaseFiles 'Hindsound-1.2.3-Setup.exe'), 'x')
    Assert-Refused 'a Setup longer than in the manifest refused' { Test-ReleaseFiles $releaseFiles } 'the size'
    $releaseFiles = New-ReleaseFiles
    $damaged = Join-Path $releaseFiles 'Hindsound-1.2.3-Setup.exe'
    $damagedBytes = [IO.File]::ReadAllBytes($damaged)
    $damagedBytes[0] = $damagedBytes[0] -bxor 0xFF
    [IO.File]::WriteAllBytes($damaged, $damagedBytes)
    Assert-Refused 'a Setup of the same size with other bytes refused' { Test-ReleaseFiles $releaseFiles } 'the SHA-256'
    foreach ($announceName in 'hindsound-update-linux-x64.json', 'hindsound-update-win-x64-portable.json') {
        $releaseFiles = New-ReleaseFiles
        & $writeAnnounce -Version '1.2.3' -Repository 'other/name' -OutFile (Join-Path $releaseFiles $announceName)
        Assert-Refused "$announceName naming another page refused" { Test-ReleaseFiles $releaseFiles } "$announceName on the release gives the address"
        $releaseFiles = New-ReleaseFiles
        & $writeAnnounce -Version '1.2.4' -Repository 'owner/name' -OutFile (Join-Path $releaseFiles $announceName)
        Assert-Refused "$announceName of another version refused" { Test-ReleaseFiles $releaseFiles } 'the version 1.2.4'
        $releaseFiles = New-ReleaseFiles
        $wrongSetup = Join-Path $releaseFiles 'Hindsound-1.2.3-Setup.exe'
        & $writeManifest -Version '1.2.3' -SetupPath $wrongSetup -Repository 'owner/name' -OutFile (Join-Path $releaseFiles $announceName)
        Assert-Refused "$announceName naming the Setup refused" { Test-ReleaseFiles $releaseFiles } 'has the fields'
    }
    $releaseFiles = New-ReleaseFiles
    $noPortable = Join-Path $releaseFiles 'Hindsound-1.2.3-win-x64-portable.zip'
    Remove-Item -LiteralPath $noPortable
    $noPortableStream = [IO.File]::Open($noPortable, [IO.FileMode]::CreateNew)
    try {
        $noPortableZip = New-Object IO.Compression.ZipArchive($noPortableStream, [IO.Compression.ZipArchiveMode]::Create)
        foreach ($name in "$top/Hindsound.exe", "$top/libmp3lame.64.dll") { $null = $noPortableZip.CreateEntry($name) }
        $noPortableZip.Dispose()
    }
    finally {
        $noPortableStream.Dispose()
    }
    Assert-Refused 'a portable zip without portable.txt refused' { Test-ReleaseFiles $releaseFiles } "has no $top/portable.txt"
    $releaseFiles = New-ReleaseFiles
    [IO.File]::AppendAllText((Join-Path $releaseFiles 'hindsound-1.2.3-linux-x64.tar.gz'), 'x')
    Assert-Refused 'a file that does not match SHA256SUMS refused' { Test-ReleaseFiles $releaseFiles } 'and SHA256SUMS gives'
    $releaseFiles = New-ReleaseFiles
    $sumsPath = Join-Path $releaseFiles 'SHA256SUMS'
    [IO.File]::WriteAllText($sumsPath, (@([IO.File]::ReadAllLines($sumsPath) | Where-Object { $_ -notmatch 'AppImage' }) -join "`n") + "`n")
    Assert-Refused 'SHA256SUMS without a file refused' { Test-ReleaseFiles $releaseFiles } 'SHA256SUMS on the release lists'
    $releaseFiles = New-ReleaseFiles
    $sumsPath = Join-Path $releaseFiles 'SHA256SUMS'
    [IO.File]::WriteAllText($sumsPath, [IO.File]::ReadAllText($sumsPath).Replace("`n", "`r`n"))
    Assert-Refused 'SHA256SUMS with carriage returns refused' { Test-ReleaseFiles $releaseFiles } 'carriage return'

    # Downloaded back: each file against the one sent.
    $releaseFiles = New-ReleaseFiles
    $sent = Join-Path $temp 'release-files-sent'
    Copy-Item -LiteralPath $releaseFiles -Destination $sent -Recurse
    Assert-Accepted 'the files downloaded back are those sent' { Test-ReleaseFiles $releaseFiles $sent }
    [IO.File]::AppendAllText((Join-Path $sent 'hindsound-1.2.3-linux-x64.AppImage'), 'x')
    Assert-Refused 'a file served with another size than the one sent refused' { Test-ReleaseFiles $releaseFiles $sent } 'and the file sent'
    Remove-Item -LiteralPath $sent -Recurse -Force
    Copy-Item -LiteralPath $releaseFiles -Destination $sent -Recurse
    $sentArchive = Join-Path $sent 'hindsound-1.2.3-linux-x64.tar.gz'
    $sentBytes = [IO.File]::ReadAllBytes($sentArchive)
    $sentBytes[0] = $sentBytes[0] -bxor 0xFF
    [IO.File]::WriteAllBytes($sentArchive, $sentBytes)
    Assert-Refused 'a file served with other bytes than the one sent refused' { Test-ReleaseFiles $releaseFiles $sent } 'and the file sent'
    Remove-Item -LiteralPath $sent -Recurse -Force
    Copy-Item -LiteralPath $releaseFiles -Destination $sent -Recurse
    [IO.File]::WriteAllText((Join-Path $sent 'extra.txt'), 'sent but not served')
    Assert-Refused 'a file sent but not served refused' { Test-ReleaseFiles $releaseFiles $sent } 'The files sent were'

    # --- check-tag: version jumps ---
    $outside = Join-Path $temp 'not-a-repository'
    New-Item -ItemType Directory -Path $outside | Out-Null
    Push-Location $outside
    try {
        Assert-Refused 'tags unreadable refused, not taken as no tag' { & $checkTag -Tag 'v0.0.9' -CheckVersionJump } 'git tag failed'
    }
    finally {
        Pop-Location
    }

    $jumps = Join-Path $temp 'jumps'
    New-Item -ItemType Directory -Path $jumps | Out-Null
    Push-Location $jumps
    try {
        Invoke-Git init -q -b main
        Invoke-Git commit -q --allow-empty -m first
        $jump = { param($Tag, $Confirm = '') & $checkTag -Tag $Tag -CheckVersionJump -ConfirmVersion $Confirm }

        Assert-Returns 'first tag, 0.x passes' '0.0.9' { & $jump 'v0.0.9' }
        Assert-Returns 'first tag, any 0.x passes' '0.50.3' { & $jump 'v0.50.3' }
        Assert-Refused 'first tag, 1.0.0 refused' { & $jump 'v1.0.0' } 'not a 0.x'
        Assert-Returns 'first tag, 1.0.0 confirmed' '1.0.0' { & $jump 'v1.0.0' '1.0.0' }

        Invoke-Git tag nightly
        Invoke-Git tag v9.0
        Invoke-Git tag v9.0.0-beta
        Invoke-Git tag V9.0.0
        Invoke-Git tag v09.0.0
        Assert-Returns 'tags in another form are no versions' '0.1.0' { & $jump 'v0.1.0' }

        Invoke-Git tag v0.0.9
        Assert-Returns 'the next patch passes' '0.0.10' { & $jump 'v0.0.10' }
        Assert-Returns 'the next minor with patch 0 passes' '0.1.0' { & $jump 'v0.1.0' }
        Assert-Refused 'the next minor with patch 1 refused' { & $jump 'v0.1.1' } 'is a jump'
        Assert-Refused 'a skipped patch refused' { & $jump 'v0.0.11' } 'is a jump'
        Assert-Refused 'a skipped minor refused' { & $jump 'v0.2.0' } 'is a jump'
        Assert-Refused 'a new major refused' { & $jump 'v1.0.0' } 'is a jump'
        Assert-Refused 'a new major with the next minor refused' { & $jump 'v1.1.0' } 'is a jump'
        Assert-Refused 'a new major with the next patch refused' { & $jump 'v1.0.10' } 'is a jump'
        Assert-Refused 'the next minor with the next patch refused' { & $jump 'v0.1.10' } 'is a jump'
        Assert-Refused 'the 9.0.0 of the mistake refused' { & $jump 'v9.0.0' } 'is a jump'
        Assert-Returns 'a new major confirmed' '1.0.0' { & $jump 'v1.0.0' '1.0.0' }
        Assert-Returns 'a skipped minor confirmed' '0.2.0' { & $jump 'v0.2.0' '0.2.0' }
        Assert-Refused 'the confirmation of another version refused' { & $jump 'v1.0.0' '1.0.1' } 'RELEASE_CONFIRM_VERSION'
        Assert-Refused 'a confirmation with a v refused' { & $jump 'v1.0.0' 'v1.0.0' } 'RELEASE_CONFIRM_VERSION'
        Assert-Refused 'a confirmation with a space refused' { & $jump 'v1.0.0' '1.0.0 ' } 'RELEASE_CONFIRM_VERSION'

        Assert-Returns 'a rerun of the only tag passes' '0.0.9' { & $jump 'v0.0.9' }
        Invoke-Git tag v0.0.10
        Assert-Returns 'a rerun of the highest tag passes' '0.0.10' { & $jump 'v0.0.10' }
        Assert-Refused 'a rerun below the highest refused' { & $jump 'v0.0.9' } 'not above'
        Assert-Refused 'a lower version refused' { & $jump 'v0.0.8' } 'not above'
        Assert-Refused 'a lower version refused, even confirmed' { & $jump 'v0.0.8' '0.0.8' } 'not above'
        Assert-Refused 'a lower version names the tag to delete if it was a mistake' { & $jump 'v0.0.8' } 'If v0.0.10 was a mistake'

        # As text, 0.9.0 is above 0.10.0.
        Invoke-Git tag v0.9.0
        Assert-Returns 'versions compare as numbers, upward' '0.10.0' { & $jump 'v0.10.0' }
        Invoke-Git tag v0.10.0
        Assert-Refused 'versions compare as numbers, downward' { & $jump 'v0.9.1' } 'not above'
        Assert-Returns 'the highest is the highest, wherever it is listed' '0.10.1' { & $jump 'v0.10.1' }
    }
    finally {
        Pop-Location
    }

    # --- check-lame-dll ---
    $nuget = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $HOME '.nuget\packages' }
    $packageDll = Join-Path $nuget 'naudio.lame\2.1.0\build\libmp3lame.64.dll'
    $package32 = Join-Path $nuget 'naudio.lame\2.1.0\build\libmp3lame.32.dll'
    if (-not (Test-Path -LiteralPath $packageDll)) {
        throw "No $packageDll. Run dotnet restore first."
    }
    $dlls = Join-Path $temp 'dlls'
    New-Item -ItemType Directory -Path $dlls | Out-Null

    # Same length, so that every address in the file stays right.
    function New-PatchedDll([string]$Name, [string]$Find, [string]$Replace) {
        $latin1 = [Text.Encoding]::GetEncoding(28591)
        $text = $latin1.GetString([IO.File]::ReadAllBytes($packageDll))
        if (-not $text.Contains($Find) -or $Find.Length -ne $Replace.Length) { throw "Cannot patch $Find." }
        $path = Join-Path $dlls $Name
        [IO.File]::WriteAllBytes($path, $latin1.GetBytes($text.Replace($Find, $Replace)))
        $path
    }

    $checkDll = Join-Path $PSScriptRoot 'check-lame-dll.ps1'
    Assert-Accepted 'the package DLL passes' { & $checkDll -DllPath $packageDll -ReferenceDll $packageDll }
    Assert-Refused 'a 32-bit DLL refused' { & $checkDll -DllPath $package32 } 'not an x64'
    $notPe = Join-Path $dlls 'text.dll'
    [IO.File]::WriteAllText($notPe, ('not a program ' * 20))
    Assert-Refused 'a file that is no PE refused' { & $checkDll -DllPath $notPe } 'not a PE'

    $withCrt = New-PatchedDll 'crt.dll' 'KERNEL32.dll' 'MSVCP140.dll'
    Assert-Refused 'a DLL that needs a redistributable refused' { & $checkDll -DllPath $withCrt } 'MSVCP140.dll'

    # Entry 13 of the data directories, 8 bytes each from offset 112 of the optional header: the delay-loaded imports.
    $delayBytes = [IO.File]::ReadAllBytes($packageDll)
    $delayEntry = [BitConverter]::ToInt32($delayBytes, 0x3C) + 24 + 112 + 8 * 13
    Assert-Equal 'the package DLL has no delay-loaded imports' 0 ([BitConverter]::ToInt32($delayBytes, $delayEntry))
    [BitConverter]::GetBytes([int]0x1000).CopyTo($delayBytes, $delayEntry)
    $withDelay = Join-Path $dlls 'delay.dll'
    [IO.File]::WriteAllBytes($withDelay, $delayBytes)
    Assert-Refused 'a DLL with delay-loaded imports refused' { & $checkDll -DllPath $withDelay } 'delay-loaded'
    $withoutClose = New-PatchedDll 'noclose.dll' "lame_close`0" "lame_clxse`0"
    Assert-Refused 'a missing export that NAudio.Lame calls refused' { & $checkDll -DllPath $withoutClose } 'lame_close'
    $withoutOther = New-PatchedDll 'noother.dll' "id3tag_set_textinfo_utf16`0" "id3tag_set_textinfo_utfxx`0"
    Assert-Accepted 'an export that NAudio.Lame does not call may go, without a reference' { & $checkDll -DllPath $withoutOther }
    Assert-Refused 'an export of the package DLL may not go' { & $checkDll -DllPath $withoutOther -ReferenceDll $packageDll } 'id3tag_set_textinfo_utf16'
    $otherVersion = New-PatchedDll 'version.dll' '3.100' '3.101'
    Assert-Refused 'another version refused' { & $checkDll -DllPath $otherVersion } 'expected 3.100'

    # IMAGE_FILE_DLL is bit 13 of the characteristics, 22 bytes after the PE signature.
    $notDll = Join-Path $dlls 'notdll.dll'
    $notDllBytes = [IO.File]::ReadAllBytes($packageDll)
    $notDllBytes[[BitConverter]::ToInt32($notDllBytes, 0x3C) + 23] = $notDllBytes[[BitConverter]::ToInt32($notDllBytes, 0x3C) + 23] -band 0xDF
    [IO.File]::WriteAllBytes($notDll, $notDllBytes)
    Assert-Refused 'an x64 program that is no DLL refused' { & $checkDll -DllPath $notDll } 'not a DLL'

    # An empty list of exports would let any DLL through. check-lame-dll reads the list next to itself.
    $emptyList = Join-Path $temp 'empty-list'
    New-Item -ItemType Directory -Path $emptyList | Out-Null
    Copy-Item -LiteralPath $checkDll -Destination $emptyList
    [IO.File]::WriteAllText((Join-Path $emptyList 'lame-exports.txt'), "# nothing`n")
    Assert-Refused 'an empty list of exports refused' { & (Join-Path $emptyList 'check-lame-dll.ps1') -DllPath $packageDll } 'lists nothing'

    # --- build-lame, with a fake archive and a fake compiler ---
    $buildLame = Join-Path $PSScriptRoot 'build-lame.ps1'
    $systemTar = Join-Path $env:SystemRoot 'System32\tar.exe'
    $fake = Join-Path $temp 'fake-build'
    $fakeBin = Join-Path $fake 'bin'
    $fakeTree = Join-Path $fake 'tree'
    New-Item -ItemType Directory -Path $fakeBin, (Join-Path $fakeTree 'lame-3.100\include'), (Join-Path $fake 'emptytree\lame-3.100') | Out-Null
    foreach ($name in 'Makefile.MSVC', 'configMS.h', 'include\lame.def') {
        [IO.File]::WriteAllText((Join-Path $fakeTree "lame-3.100\$name"), "fake $name`n")
    }
    $fakeArchive = Join-Path $fake 'lame-3.100.tar.gz'
    & $systemTar -czf $fakeArchive -C $fakeTree 'lame-3.100'
    $fakeSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $fakeArchive).Hash.ToLowerInvariant()
    $emptyArchive = Join-Path $fake 'empty.tar.gz'
    & $systemTar -czf $emptyArchive -C (Join-Path $fake 'emptytree') 'lame-3.100'
    $emptySha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $emptyArchive).Hash.ToLowerInvariant()

    $fakeArgs = Join-Path $fake 'nmake-args.txt'
    foreach ($tool in 'cl', 'link') {
        [IO.File]::WriteAllText((Join-Path $fakeBin "$tool.cmd"), "@exit /b 0`r`n")
    }
    [IO.File]::WriteAllLines((Join-Path $fakeBin 'nmake.cmd'), [string[]]@(
            '@echo off'
            "echo %* > `"$fakeArgs`""
            'if "%FAKE_NMAKE%"=="fail" (echo fake compiler error C2000 & exit /b 2)'
            'mkdir output'
            'if "%FAKE_NMAKE%"=="nooutput" exit /b 0'
            'if "%FAKE_NMAKE%"=="changesource" echo changed>> Makefile.MSVC'
            "if `"%FAKE_NMAKE%`"==`"x86`" (copy /y `"$package32`" output\libmp3lame.dll > nul & exit /b 0)"
            "copy /y `"$packageDll`" output\libmp3lame.dll > nul"
            'exit /b 0'
        ))
    $fakeVs = Join-Path $fake 'VsDevCmd.bat'
    [IO.File]::WriteAllText($fakeVs, "@set `"PATH=$fakeBin;%PATH%`"`r`n")
    # This one finds no tools. PATH holds the system only, for "where" itself.
    $fakeVsEmpty = Join-Path $fake 'VsDevCmdEmpty.bat'
    [IO.File]::WriteAllText($fakeVsEmpty, "@set `"PATH=$env:SystemRoot\System32`"`r`n")

    $buildNumber = 0
    function New-BuildFolder {
        $script:buildNumber++
        Join-Path $temp "build-$($script:buildNumber)"
    }
    function Invoke-FakeBuild([string]$Work, [string]$Mode = '') {
        $env:FAKE_NMAKE = $Mode
        try {
            & $buildLame -WorkDir $Work -OutFile (Join-Path $Work 'out\libmp3lame.64.dll') -ArchivePath $fakeArchive -ExpectedSha256 $fakeSha256 -VsDevCmd $fakeVs
        }
        finally {
            $env:FAKE_NMAKE = $null
        }
    }

    $work = New-BuildFolder
    Assert-Refused 'an archive with another hash refused' { & $buildLame -WorkDir $work -OutFile (Join-Path $work 'out.dll') -ArchivePath $fakeArchive } 'SHA-256'
    Report 'nothing extracted after a wrong hash' (-not (Test-Path -LiteralPath (Join-Path $work 'src')))
    $work = New-BuildFolder
    Assert-Accepted 'the download alone needs no compiler' { & $buildLame -WorkDir $work -OutFile (Join-Path $work 'out.dll') -ArchivePath $fakeArchive -ExpectedSha256 $fakeSha256 -DownloadOnly -VsDevCmd (Join-Path $fake 'none.bat') }
    Report 'the download alone extracts nothing' (-not (Test-Path -LiteralPath (Join-Path $work 'src')))
    Assert-Refused 'the download alone still verifies the hash' { & $buildLame -WorkDir $work -OutFile (Join-Path $work 'out.dll') -ArchivePath $fakeArchive -DownloadOnly } 'SHA-256'

    # The download, from file addresses: curl reads them as it reads https.
    $notArchive = Join-Path $fake 'page.html'
    [IO.File]::WriteAllText($notArchive, '<html>a web page</html>')
    function ConvertTo-FileUrl([string]$Path) { 'file:///' + $Path.Replace('\', '/') }
    $work = New-BuildFolder
    $downloadOutput = New-Object Collections.ArrayList
    Assert-Accepted 'a web page at the first address: the second one gives the archive' { & $buildLame -WorkDir $work -OutFile (Join-Path $work 'out.dll') -Urls (ConvertTo-FileUrl $notArchive), (ConvertTo-FileUrl $fakeArchive) -ExpectedSha256 $fakeSha256 -DownloadOnly | ForEach-Object { [void]$downloadOutput.Add("$_") } }
    Report 'the web page is named in the run log' (@($downloadOutput | Where-Object { $_ -like '*not the archive*' }).Count -eq 1)
    $work = New-BuildFolder
    Assert-Refused 'no address gives the archive refused' { & $buildLame -WorkDir $work -OutFile (Join-Path $work 'out.dll') -Urls (ConvertTo-FileUrl $notArchive), (ConvertTo-FileUrl $notArchive) -ExpectedSha256 $fakeSha256 -DownloadOnly } 'from any address'
    $work = New-BuildFolder
    Assert-Refused 'no address answers refused' { & $buildLame -WorkDir $work -OutFile (Join-Path $work 'out.dll') -Urls (ConvertTo-FileUrl (Join-Path $fake 'none.tar.gz')) -ExpectedSha256 $fakeSha256 -DownloadOnly } 'from any address'
    $work = New-BuildFolder
    Assert-Refused 'an archive that is not the expected tree refused' { & $buildLame -WorkDir $work -OutFile (Join-Path $work 'out.dll') -ArchivePath $emptyArchive -ExpectedSha256 $emptySha256 -VsDevCmd $fakeVs } 'has no lame-3.100'
    $work = New-BuildFolder
    Assert-Refused 'no VsDevCmd refused' { & $buildLame -WorkDir $work -OutFile (Join-Path $work 'out.dll') -ArchivePath $fakeArchive -ExpectedSha256 $fakeSha256 -VsDevCmd (Join-Path $fake 'none.bat') } 'VsDevCmd is not at'
    $work = New-BuildFolder
    Assert-Refused 'no compiler after VsDevCmd refused' { & $buildLame -WorkDir $work -OutFile (Join-Path $work 'out.dll') -ArchivePath $fakeArchive -ExpectedSha256 $fakeSha256 -VsDevCmd $fakeVsEmpty } 'not on the PATH'

    $work = New-BuildFolder
    Assert-Accepted 'a working build passes' { Invoke-FakeBuild $work }
    Assert-Equal 'the built DLL is copied' (Get-FileHash -Algorithm SHA256 -LiteralPath $packageDll).Hash (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $work 'out\libmp3lame.64.dll')).Hash
    Assert-Equal 'nmake gets the 64-bit macros and the DLL target, on the command line' '/NOLOGO -f Makefile.MSVC MSVCVER=Win64 MACHINE=/machine:X64 ADDL_OBJ=kernel32.lib libmp3lame.dll' ((Get-Content -LiteralPath $fakeArgs) -join ' ').Trim()
    Assert-Refused 'a used work folder refused' { Invoke-FakeBuild $work } 'already exists'

    $work = New-BuildFolder
    $failedOutput = New-Object Collections.ArrayList
    Assert-Refused 'a failed nmake refused' { Invoke-FakeBuild $work 'fail' | ForEach-Object { [void]$failedOutput.Add("$_") } } 'nmake failed with exit code 2'
    Report 'a failed nmake shows its error in the run log' (@($failedOutput | Where-Object { $_ -like 'fake compiler error C2000*' }).Count -eq 1)
    Report 'a failed nmake shows the output folder in the run log' (@($failedOutput | Where-Object { $_ -like '*output ---' }).Count -eq 1)
    Report 'no DLL after a failed nmake' (-not (Test-Path -LiteralPath (Join-Path $work 'out\libmp3lame.64.dll')))
    $work = New-BuildFolder
    Assert-Refused 'no DLL in the output refused' { Invoke-FakeBuild $work 'nooutput' } 'no output\libmp3lame.dll'
    $work = New-BuildFolder
    Assert-Refused 'a changed source file refused' { Invoke-FakeBuild $work 'changesource' } 'changed a source file'
    $work = New-BuildFolder
    Assert-Refused 'a built DLL that fails its checks refused' { Invoke-FakeBuild $work 'x86' } 'not an x64'
    Report 'no DLL copied after failed checks' (-not (Test-Path -LiteralPath (Join-Path $work 'out\libmp3lame.64.dll')))

    # --- check-setup: the folder part, on a fake folder. The package DLL stands in for the exe: it has a version resource. ---
    $checkSetup = Join-Path $PSScriptRoot 'check-setup.ps1'
    $packageInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($packageDll)
    $packageVersion = '{0}.{1}.{2}' -f $packageInfo.FileMajorPart, $packageInfo.FileMinorPart, $packageInfo.FileBuildPart
    Assert-Equal 'the stand-in exe has a usable version' 0 $packageInfo.FilePrivatePart

    $fakeRepo = Join-Path $temp 'fake-repo'
    New-Item -ItemType Directory -Path (Join-Path $fakeRepo 'licenses') | Out-Null
    foreach ($name in 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'licenses\a.txt', 'licenses\b.txt') {
        [IO.File]::WriteAllText((Join-Path $fakeRepo $name), "text of $name`n")
    }
    $noLicences = Join-Path $temp 'fake-repo-no-licences'
    New-Item -ItemType Directory -Path (Join-Path $noLicences 'licenses') | Out-Null
    foreach ($name in 'LICENSE', 'THIRD-PARTY-NOTICES.md') {
        [IO.File]::WriteAllText((Join-Path $noLicences $name), "text of $name`n")
    }
    $compiledDll = Join-Path $temp 'compiled.dll'
    Copy-Item -LiteralPath $withoutOther -Destination $compiledDll

    $installNumber = 0
    function New-FakeInstall {
        $script:installNumber++
        $folder = Join-Path $temp "install-$($script:installNumber)"
        New-Item -ItemType Directory -Path (Join-Path $folder 'licenses') | Out-Null
        Copy-Item -LiteralPath $packageDll -Destination (Join-Path $folder 'Hindsound.exe')
        Copy-Item -LiteralPath $compiledDll -Destination (Join-Path $folder 'libmp3lame.64.dll')
        foreach ($name in 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'licenses\a.txt', 'licenses\b.txt') {
            Copy-Item -LiteralPath (Join-Path $fakeRepo $name) -Destination (Join-Path $folder $name)
        }
        $folder
    }
    function Assert-FolderRefused([string]$Name, [scriptblock]$Damage, [string]$Reason) {
        $folder = New-FakeInstall
        & $Damage $folder
        Assert-Refused $Name { & $checkSetup -CheckFolder $folder -Version $packageVersion -DllPath $compiledDll -RepoRoot $fakeRepo } $Reason
    }

    $goodFolder = New-FakeInstall
    Assert-Accepted 'a right installed folder passes' { & $checkSetup -CheckFolder $goodFolder -Version $packageVersion -DllPath $compiledDll -RepoRoot $fakeRepo }
    Assert-Refused 'another exe version refused' { & $checkSetup -CheckFolder $goodFolder -Version '1.2.3' -DllPath $compiledDll -RepoRoot $fakeRepo } 'file version'
    Assert-Refused 'no licence in the repository refused' { & $checkSetup -CheckFolder $goodFolder -Version $packageVersion -DllPath $compiledDll -RepoRoot $noLicences } 'no licenses'
    Assert-FolderRefused 'no exe refused' { Remove-Item -LiteralPath (Join-Path $args[0] 'Hindsound.exe') } 'has no*Hindsound.exe'
    Assert-FolderRefused 'a portable.txt in the installed folder refused' { [IO.File]::WriteAllText((Join-Path $args[0] 'portable.txt'), 'portable') } 'has a portable.txt'
    Assert-FolderRefused 'no DLL refused' { Remove-Item -LiteralPath (Join-Path $args[0] 'libmp3lame.64.dll') } 'has no*libmp3lame.64.dll'
    Assert-FolderRefused 'the DLL of the package in place of the compiled one refused' { Copy-Item -LiteralPath $packageDll -Destination (Join-Path $args[0] 'libmp3lame.64.dll') -Force } 'differs'
    Assert-FolderRefused 'no LICENSE refused' { Remove-Item -LiteralPath (Join-Path $args[0] 'LICENSE') } 'has no*LICENSE'
    Assert-FolderRefused 'another LICENSE refused' { [IO.File]::WriteAllText((Join-Path $args[0] 'LICENSE'), 'other') } 'differs'
    Assert-FolderRefused 'no notices refused' { Remove-Item -LiteralPath (Join-Path $args[0] 'THIRD-PARTY-NOTICES.md') } 'has no*THIRD-PARTY-NOTICES.md'
    Assert-FolderRefused 'other notices refused' { [IO.File]::WriteAllText((Join-Path $args[0] 'THIRD-PARTY-NOTICES.md'), 'other') } 'differs'
    Assert-FolderRefused 'a licence file missing refused' { Remove-Item -LiteralPath (Join-Path $args[0] 'licenses\b.txt') } 'has no*b.txt'
    Assert-FolderRefused 'another licence file refused' { [IO.File]::WriteAllText((Join-Path $args[0] 'licenses\a.txt'), 'other') } 'differs'

    # What --prove-mp3 of the installed exe leaves, on fake files.
    $mp3Number = 0
    function Test-Mp3Folder([string]$Name, [hashtable]$Sizes, [string]$Reason, [byte[]]$Header = @(0xFF, 0xFB)) {
        $script:mp3Number++
        $folder = Join-Path $temp "mp3-$($script:mp3Number)"
        New-Item -ItemType Directory -Path $folder | Out-Null
        foreach ($file in $Sizes.Keys) {
            $path = Join-Path $folder $file
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path) | Out-Null
            $bytes = [byte[]](@(1) * $Sizes[$file])
            # The start of an MPEG audio frame, as the encoder writes it.
            if ($file.EndsWith('.mp3') -and $bytes.Length -ge 2) { $bytes[0] = $Header[0]; $bytes[1] = $Header[1] }
            [IO.File]::WriteAllBytes($path, $bytes)
        }
        if ($Reason) {
            Assert-Refused $Name { & $checkSetup -CheckMp3Folder $folder } $Reason
        }
        else {
            Assert-Accepted $Name { & $checkSetup -CheckMp3Folder $folder }
        }
    }
    Test-Mp3Folder 'one MP3 from the installed exe passes' @{ 'clip.mp3' = 25000 } ''
    Test-Mp3Folder 'a WAV in place of the MP3 refused' @{ 'clip.wav' = 100 } 'WAV in place of MP3'
    Test-Mp3Folder 'a WAV beside the MP3 refused' @{ 'clip.mp3' = 25000; 'clip.wav' = 100 } 'WAV in place of MP3'
    Test-Mp3Folder 'an empty MP3 refused' @{ 'clip.mp3' = 0 } 'expected one MP3'
    Test-Mp3Folder 'two MP3 refused' @{ 'a.mp3' = 25000; 'b.mp3' = 25000 } 'expected one MP3'
    Test-Mp3Folder 'an MP3 without the start of a frame refused' @{ 'clip.mp3' = 25000 } 'MPEG audio frame' @(0x49, 0x44)
    Test-Mp3Folder 'an MP3 whose second byte is not a frame sync refused' @{ 'clip.mp3' = 25000 } 'MPEG audio frame' @(0xFF, 0x00)
    Test-Mp3Folder 'a truncated MP3 refused' @{ 'clip.mp3' = 5000 } 'expected 20 000 to 30 000'
    Test-Mp3Folder 'a far too long MP3 refused' @{ 'clip.mp3' = 100000 } 'expected 20 000 to 30 000'
    Test-Mp3Folder 'an MP3 in the rescue folder refused'  @{ 'rescued\clip.mp3' = 25000 } 'rescue folder'
    Test-Mp3Folder 'an empty MP3 folder refused' @{} 'expected one MP3'
    Assert-Refused 'no MP3 folder refused' { & $checkSetup -CheckMp3Folder (Join-Path $temp 'no-mp3') } 'expected one MP3'
    $setupText = [IO.File]::ReadAllText($checkSetup)
    Report 'check-setup has the installed exe write an MP3, then checks it' ($setupText.Contains('--prove-mp3 `"$mp3`"" 120') -and $setupText.Contains("`n    Assert-Mp3Folder `$mp3"))

    # A text file: if the guard were gone, Windows could not start it. Never a real Setup here.
    $notSetup = Join-Path $temp 'not-a-setup.exe'
    [IO.File]::WriteAllText($notSetup, 'not a program')
    $savedActions = $env:GITHUB_ACTIONS
    $savedRunner = $env:RUNNER_ENVIRONMENT
    try {
        foreach ($case in @(@($null, $null), @('true', $null), @('true', 'self-hosted'), @($null, 'github-hosted'))) {
            $env:GITHUB_ACTIONS = $case[0]
            $env:RUNNER_ENVIRONMENT = $case[1]
            Assert-Refused "no installation with GITHUB_ACTIONS=$($case[0]) RUNNER_ENVIRONMENT=$($case[1])" { & $checkSetup -SetupPath $notSetup -Version '1.2.3' -DllPath $compiledDll -WorkDir (Join-Path $temp 'setup-check') -RepoRoot $fakeRepo } 'GitHub-hosted runner only'
        }
    }
    finally {
        $env:GITHUB_ACTIONS = $savedActions
        $env:RUNNER_ENVIRONMENT = $savedRunner
    }
    Report 'nothing written after the refused installation' (-not (Test-Path -LiteralPath (Join-Path $temp 'setup-check')))

    # --- prove-encoding: what it accepts from a test run ---
    $proveEncoding = Join-Path $PSScriptRoot 'prove-encoding.ps1'
    function New-Trx([int]$Passed, [int]$Failed) {
        $path = Join-Path $temp "results-$Passed-$Failed.trx"
        $total = $Passed + $Failed
        [IO.File]::WriteAllText($path, "<?xml version=`"1.0`" encoding=`"UTF-8`"?><TestRun xmlns=`"http://microsoft.com/schemas/VisualStudio/TeamTest/2010`"><ResultSummary outcome=`"Completed`"><Counters total=`"$total`" passed=`"$Passed`" failed=`"$Failed`" /></ResultSummary></TestRun>")
        $path
    }
    Assert-Accepted 'all the encoding tests passed' { & $proveEncoding -CheckTrx (New-Trx 6 0) }
    Assert-Refused 'a failed encoding test refused' { & $proveEncoding -CheckTrx (New-Trx 6 1) } 'failed'
    Assert-Refused 'too few encoding tests refused' { & $proveEncoding -CheckTrx (New-Trx 5 0) } 'expected 6'
    Assert-Refused 'no encoding test refused' { & $proveEncoding -CheckTrx (New-Trx 0 0) } 'expected 6'
    $noTests = Join-Path $temp 'no-tests-output'
    New-Item -ItemType Directory -Path $noTests | Out-Null
    Assert-Refused 'no DLL to replace in the tests output refused' { & $proveEncoding -DllPath $compiledDll -TestsOutput $noTests -ResultsDir (Join-Path $temp 'encoding') } 'build the tests first'

    # --- the workflows ---
    function Get-RunBlocks([string[]]$Lines) {
        $inRun = $false
        $indent = 0
        foreach ($line in $Lines) {
            if ($inRun) {
                if ($line -match '^\s*$') { continue }
                if (($line.Length - $line.TrimStart().Length) -gt $indent) {
                    $line
                    continue
                }
                $inRun = $false
            }
            if ($line -match '^( *)(?:- )?run:(.*)$') {
                $indent = $Matches[1].Length
                $inRun = $true
                $Matches[2]
            }
        }
    }

    $workflowFolder = Join-Path $PSScriptRoot '..\..\.github\workflows'
    $release = @(Get-Content -LiteralPath (Join-Path $workflowFolder 'release.yml'))
    $files = @(Get-Content -LiteralPath (Join-Path $workflowFolder 'release-files.yml'))
    $ci = @(Get-Content -LiteralPath (Join-Path $workflowFolder 'ci.yml'))
    $lame = @(Get-Content -LiteralPath (Join-Path $workflowFolder 'lame.yml'))
    $releaseText = $release -join "`n"
    $filesText = $files -join "`n"
    $ciText = $ci -join "`n"
    $lameText = $lame -join "`n"

    $stepOrder = @(
        'Check out', 'Check the tag', 'Extract the release notes', 'Set up .NET', 'Test', 'Test the release scripts',
        'Check out', 'Write the release notes', 'Take the files of the release', 'Check the files of the release', 'Create the draft release', 'Attach the files', 'Publish the release',
        'Say that nothing was published'
    )
    $releaseSteps = @($release | ForEach-Object { if ($_ -match '^\s+- name: (.+)$') { $Matches[1] } })
    Assert-Equal 'release.yml has its steps in order' ($stepOrder -join ' | ') ($releaseSteps -join ' | ')
    $filesStepOrder = @(
        'Check out', 'Set up .NET', 'Build the tests', 'Publish the app', 'Build LAME', 'Replace the DLL in the published folder', 'Prove the encoding with the compiled DLL', 'Build the Setup', 'Check the Setup',
        'Write the update manifest', 'Make the portable zip', 'Add the LAME source', 'Keep the Windows files',
        'Check out', 'Set up .NET', 'Publish the Linux app', 'Build the MP3 encoder', 'Install the checker of desktop entries', 'Make the AppImage and the tar.gz', 'Check the packages', 'Add the LAME source', 'Keep the packages',
        'Check out', 'Take the Windows files', 'Take the Linux files', 'Gather the files of the release', 'Check the files of the release', 'Keep the files of the release'
    )
    $filesSteps = @($files | ForEach-Object { if ($_ -match '^\s+- name: (.+)$') { $Matches[1] } })
    Assert-Equal 'release-files.yml has its steps in order' ($filesStepOrder -join ' | ') ($filesSteps -join ' | ')
    foreach ($step in @(@('Build LAME', 'build-lame'), @('Prove the encoding with the compiled DLL', 'prove-encoding'), @('Check the Setup', 'check-setup'), @('Make the portable zip', 'build-portable-zip'), @('Gather the files of the release', 'assemble-release'), @('Check the files of the release', 'check-release-files'))) {
        Report "the step $($step[0]) runs $($step[1])" ($filesText -cmatch "- name: $($step[0])\n(?:\s+timeout-minutes: \d+\n)?\s+run: \./tools/release/$($step[1])\.ps1 ")
    }
    Report 'the tests the proof of the encoding runs are built first' ($filesText -cmatch '- name: Build the tests\n\s+run: dotnet build tests/Hindsound\.Tests/Hindsound\.Tests\.csproj -c Release\n')
    Report 'the compiled DLL replaces the one in the published folder' ($filesText -cmatch '- name: Replace the DLL in the published folder\n\s+run: Copy-Item -LiteralPath "\$env:RUNNER_TEMP/lame/libmp3lame\.64\.dll" -Destination "\$env:RUNNER_TEMP/publish/libmp3lame\.64\.dll" -Force')
    Report 'the zip is made from the folder the Setup is built from' (($filesText -cmatch '"/DPublishDir=\$env:RUNNER_TEMP/publish"') -and ($filesText -cmatch 'build-portable-zip\.ps1 -PublishDir "\$env:RUNNER_TEMP/publish" '))
    Report 'the lame workflow builds and proves' (($lameText -cmatch '\./tools/release/build-lame\.ps1 ') -and ($lameText -cmatch '\./tools/release/prove-encoding\.ps1 '))
    Report 'the release sees all the tags'($releaseText -match '(?m)^\s+fetch-depth: 0$')
    Report 'the release checks the version jump, with the confirmation' ($releaseText -cmatch 'check-tag\.ps1 -Tag \$env:TAG -MainRef origin/main -CheckVersionJump -ConfirmVersion \$env:RELEASE_CONFIRM_VERSION')
    Report 'the confirmation comes from a repository variable, through env' ($releaseText -cmatch 'RELEASE_CONFIRM_VERSION: \$\{\{ vars\.RELEASE_CONFIRM_VERSION \}\}')
    Report 'the release attaches the Setup, the manifest and the LAME source' ($releaseText -cmatch 'gh release upload [^\n]*Setup\.exe[^\n]*hindsound-update-win-x64\.json[^\n]*lame-3\.100\.tar\.gz')
    $uploadLine = @($release | Where-Object { $_ -cmatch 'gh release upload ' })
    Assert-Equal 'the release uploads in one command' 1 $uploadLine.Count
    foreach ($name in 'Hindsound-$env:VERSION-Setup.exe', 'Hindsound-$env:VERSION-win-x64-portable.zip', 'hindsound-$env:VERSION-linux-x64.AppImage', 'hindsound-$env:VERSION-linux-x64.tar.gz', 'hindsound-update-win-x64.json', 'hindsound-update-win-x64-portable.json', 'hindsound-update-linux-x64.json', 'lame-3.100.tar.gz', 'SHA256SUMS') {
        Report "the release attaches $name" ($uploadLine[0].Contains("`"`$env:RUNNER_TEMP/out/$name`""))
    }
    Report 'the release downloads its files back and checks them against those sent, before publishing' ($releaseText -cmatch '--clobber\n\s+if \(\$LASTEXITCODE -ne 0\) \{ exit \$LASTEXITCODE \}\n\s+gh release download \$env:TAG --dir "\$env:RUNNER_TEMP/verify"\n\s+if \(\$LASTEXITCODE -ne 0\) \{ exit \$LASTEXITCODE \}\n\s+\./tools/release/check-release-files\.ps1 -Folder "\$env:RUNNER_TEMP/verify" -Version \$env:VERSION -Repository \$env:GITHUB_REPOSITORY -Expected "\$env:RUNNER_TEMP/out"\n\n\s+# A rerun')
    Report 'the release publishes only after the check of the files downloaded back' ($releaseText.IndexOf('-Folder "$env:RUNNER_TEMP/verify"') -gt 0 -and $releaseText.IndexOf('-Folder "$env:RUNNER_TEMP/verify"') -lt $releaseText.IndexOf('--draft=false') -and ([regex]::Matches($releaseText, '--draft=false')).Count -eq 1)
    Report 'the release sends the files the release-files workflow checked' ($releaseText -cmatch '- name: Take the files of the release\n\s+uses: actions/download-artifact@[0-9a-f]{40} # v[0-9.]+\n\s+with:\n\s+name: release-files\n\s+path: \$\{\{ runner\.temp \}\}/out\n' -and $filesText -cmatch '\n\s+name: release-files\n\s+path: \$\{\{ runner\.temp \}\}/release/\n')
    Report 'the release does not give check-release-files its LAME hash' (-not ($releaseText -match '-LameSha256') -and -not ($filesText -match '-LameSha256'))
    Report 'get-iscc verifies its package before extracting it' ([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'get-iscc.ps1')) -cmatch 'if \(\$actual -ne \$packageSha256\) \{\s+throw [^\n]+\n\}\s+\$content = ')
    Assert-Equal 'the token goes to the three steps of the release' 3 ([regex]::Matches($releaseText, 'GH_TOKEN:')).Count
    $firstToken = @($release | Select-String 'GH_TOKEN:')[0].LineNumber
    $draftStep = @($release | Select-String '- name: Create the draft release')[0].LineNumber
    Report 'no step before the draft gets the token' ($firstToken -gt $draftStep)
    Report 'the release job ends by itself after 60 minutes' ($releaseText -cmatch '(?m)^    timeout-minutes: 60$')
    # The one condition: the job that reports a failure, which has no permission, no token and runs no script.
    Assert-Equal 'no step of the release is conditional, only the job that reports a failure' '    if: failure()' (@($release | Where-Object { $_ -match '^\s+if:' }) -join ' / ')
    $reportJob = $releaseText.Substring($releaseText.IndexOf("`n  report-failure:`n"))
    Report 'the job that reports a failure is the last, without permission or token, and runs no script' (($reportJob -cmatch '\n    permissions: \{\}\n') -and -not ($reportJob -match 'GH_TOKEN|gh |\./tools/|uses:') -and -not ($reportJob.Substring(1) -cmatch '\n  [a-z-]+:\n'))
    Report 'the job that reports a failure waits for every other job' ($reportJob -cmatch '\n    needs: \[check, files, release\]\n')
    Report 'no step of release-files is conditional' (-not ($filesText -match '(?m)^\s+if:'))
    Report 'the release runs the tests' ($releaseText -cmatch '- name: Test\n\s+run: dotnet test Hindsound\.slnx -c Release --filter "Category!=Device&Category!=Linux"')
    Report 'the release tests its scripts' ($releaseText -cmatch '- name: Test the release scripts\n\s+run: \./tools/release/test-release-scripts\.ps1')
    Report 'the release waits for the checks and for the files of Windows and Linux' (($releaseText -cmatch '\n  release:\n    needs: \[check, files\]\n') -and ($releaseText -cmatch '\n  files:\n    needs: check\n    permissions:\n      contents: read\n    uses: \./\.github/workflows/release-files\.yml\n    with:\n      version: \$\{\{ needs\.check\.outputs\.version \}\}\n'))
    Report 'the files of the release wait for the Windows and the Linux jobs' (($filesText -cmatch '\n  assemble:\n    needs: \[windows, linux\]\n') -and ($filesText -cmatch '\n  linux:\n    runs-on: ubuntu-22\.04\n'))
    Report 'release-files is called only, reads only, and publishes nothing' (($filesText -cmatch '\non:\n  workflow_call:\n') -and ($filesText -match '(?m)^permissions:\n  contents: read$') -and -not ($filesText -match 'contents: write|GH_TOKEN|github\.token|gh release|secrets\.'))
    Report 'the CI rehearses the release with the same workflow, and publishes nothing' (($ciText -cmatch '\n    uses: \./\.github/workflows/release-files\.yml\n') -and ($ciText -match '(?m)^permissions:\n  contents: read$') -and -not ($ciText -match 'contents: write|GH_TOKEN|github\.token|gh release|secrets\.'))
    Report 'the release starts as a verified draft' ($releaseText.Contains('gh release create $env:TAG --draft --verify-tag '))
    Report 'a failure of gh release view other than "release not found" stops the step' ($releaseText.Contains('2>&1' + "`n" + '          $viewExit = $LASTEXITCODE') -and $releaseText.Contains('if ($viewExit -ne 0 -and "$view" -notmatch ''release not found'') { throw'))
    Report 'a published release is refused' ($releaseText.Contains('if ($isDraft -eq ''false'') { throw "$env:TAG is already published." }'))
    Report 'a draft is reused' ($releaseText.Contains('if ($isDraft -eq ''true'') {'))
    Report 'the token stays out of git' ($releaseText -match '(?m)^\s+persist-credentials: false$')
    Report 'the workflow starts with no permission' ($releaseText -match '(?m)^permissions: \{\}$')
    Report 'the version comes from check-tag' ($releaseText.Contains('"VERSION=$version" >> $env:GITHUB_ENV') -and $releaseText.Contains('"version=$version" >> $env:GITHUB_OUTPUT') -and $releaseText.Contains('version: ${{ steps.tag.outputs.version }}'))
    Report 'the releases run one at a time, none cancelled' ($releaseText -cmatch '(?m)^concurrency:\n  group: release\n  cancel-in-progress: false$')
    Report 'the release is published as the latest, which the app reads' ($releaseText.Contains('gh release edit $env:TAG --draft=false --latest'))
    Report 'the release checks the tag on the remote just before publishing' ($releaseText -cmatch '- name: Publish the release\n\s+env:\n[^\n]+\n[^\n]+\n\s+run: \|\n\s+\./tools/release/check-remote-tag\.ps1 -Tag \$env:TAG -Commit \(git rev-parse HEAD\)\n\s+gh release edit \$env:TAG --draft=false')
    Report 'the lame workflow only reads' (($lameText -match '(?m)^permissions:\n  contents: read$') -and -not ($lameText -match 'contents: write|GH_TOKEN|gh release'))

    foreach ($workflow in @(@('release.yml', $release), @('release-files.yml', $files), @('ci.yml', $ci), @('lame.yml', $lame))) {
        $text = $workflow[1] -join "`n"
        $runText = @(Get-RunBlocks $workflow[1]) -join "`n"
        Report "$($workflow[0]) has run blocks" ($runText.Length -gt 0)
        Report "$($workflow[0]) puts no expression in a run block" (-not $runText.Contains('${{'))
        Report "$($workflow[0]) lets no step fail silently" (-not ($text -match 'continue-on-error'))
        Report "$($workflow[0]) does not use the test parameters of build-lame" (-not ($text -match '-ExpectedSha256|-VsDevCmd'))
        Report "$($workflow[0]) does not use the test parameter of build-portable-zip" (-not ($text -match '-AfterWrite'))
        Report "$($workflow[0]) does not give build-lame its addresses" (-not ($text -match '-Urls'))
        foreach ($uses in @($workflow[1] | Where-Object { $_ -match '^\s*(- )?uses:' })) {
            # A workflow of this repository runs at the commit of the run: its path is its pin.
            $local = [regex]::Match($uses, '\A    uses: \./\.github/workflows/([a-z-]+\.yml)\z')
            if ($local.Success) {
                Report "$($workflow[0]) calls $($local.Groups[1].Value) of this commit" (Test-Path -LiteralPath (Join-Path $workflowFolder $local.Groups[1].Value) -PathType Leaf)
            }
            else {
                Report "$($workflow[0]) pins $($uses.Trim())" ($uses -cmatch '@[0-9a-f]{40} # v[0-9]')
            }
        }
    }
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
