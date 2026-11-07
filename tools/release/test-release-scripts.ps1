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

    # --- check-release-files: the files of the draft, downloaded back ---
    $checkFiles = Join-Path $PSScriptRoot 'check-release-files.ps1'
    $filesNumber = 0
    function New-ReleaseFiles([string]$ManifestVersion = '1.2.3', [string]$Repository = 'owner/name') {
        $script:filesNumber++
        $folder = Join-Path $temp "release-files-$($script:filesNumber)"
        New-Item -ItemType Directory -Path $folder | Out-Null
        $file = Join-Path $folder 'Hindsound-1.2.3-Setup.exe'
        [IO.File]::WriteAllBytes($file, [byte[]](1..200 | ForEach-Object { $_ % 7 }))
        & $writeManifest -Version $ManifestVersion -SetupPath $file -Repository $Repository -OutFile (Join-Path $folder 'hindsound-update-win-x64.json')
        [IO.File]::WriteAllText((Join-Path $folder 'lame-3.100.tar.gz'), 'fake source')
        $folder
    }
    $releaseFiles = New-ReleaseFiles
    $fakeLameSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $releaseFiles 'lame-3.100.tar.gz')).Hash.ToLowerInvariant()
    function Test-ReleaseFiles([string]$Folder) {
        & $checkFiles -Folder $Folder -Version '1.2.3' -Repository 'owner/name' -LameSha256 $fakeLameSha256
    }
    Assert-Accepted 'the files of the release pass' { Test-ReleaseFiles $releaseFiles }
    Assert-Refused 'another LAME source refused' { & $checkFiles -Folder $releaseFiles -Version '1.2.3' -Repository 'owner/name' } 'lame-3.100.tar.gz on the release'
    $releaseFiles = New-ReleaseFiles
    [IO.File]::WriteAllText((Join-Path $releaseFiles 'Hindsound-1.2.2-Setup.exe'), 'left by an earlier run')
    Assert-Refused 'a file more on the release refused' { Test-ReleaseFiles $releaseFiles } 'expected'
    $releaseFiles = New-ReleaseFiles
    Remove-Item -LiteralPath (Join-Path $releaseFiles 'lame-3.100.tar.gz')
    Assert-Refused 'a file missing on the release refused' { Test-ReleaseFiles $releaseFiles } 'expected'
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
    function Test-Mp3Folder([string]$Name, [hashtable]$Sizes, [string]$Reason) {
        $script:mp3Number++
        $folder = Join-Path $temp "mp3-$($script:mp3Number)"
        New-Item -ItemType Directory -Path $folder | Out-Null
        foreach ($file in $Sizes.Keys) {
            [IO.File]::WriteAllBytes((Join-Path $folder $file), [byte[]](@(1) * $Sizes[$file]))
        }
        if ($Reason) {
            Assert-Refused $Name { & $checkSetup -CheckMp3Folder $folder } $Reason
        }
        else {
            Assert-Accepted $Name { & $checkSetup -CheckMp3Folder $folder }
        }
    }
    Test-Mp3Folder 'one MP3 from the installed exe passes' @{ 'clip.mp3' = 100 } ''
    Test-Mp3Folder 'a WAV in place of the MP3 refused' @{ 'clip.wav' = 100 } 'WAV in place of MP3'
    Test-Mp3Folder 'a WAV beside the MP3 refused' @{ 'clip.mp3' = 100; 'clip.wav' = 100 } 'WAV in place of MP3'
    Test-Mp3Folder 'an empty MP3 refused' @{ 'clip.mp3' = 0 } 'expected one MP3'
    Test-Mp3Folder 'two MP3 refused' @{ 'a.mp3' = 100; 'b.mp3' = 100 } 'expected one MP3'
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
    $lame = @(Get-Content -LiteralPath (Join-Path $workflowFolder 'lame.yml'))
    $releaseText = $release -join "`n"
    $lameText = $lame -join "`n"

    $stepOrder = @(
        'Check out', 'Check the tag', 'Extract the release notes', 'Set up .NET', 'Test', 'Test the release scripts', 'Publish the app',
        'Build LAME', 'Replace the DLL in the published folder', 'Prove the encoding with the compiled DLL', 'Build the Setup', 'Check the Setup',
        'Write the update manifest', 'Create the draft release', 'Attach the files', 'Publish the release'
    )
    $releaseSteps = @($release | ForEach-Object { if ($_ -match '^\s+- name: (.+)$') { $Matches[1] } })
    Assert-Equal 'release.yml has its steps in order' ($stepOrder -join ' | ') ($releaseSteps -join ' | ')
    foreach ($step in @(@('Build LAME', 'build-lame'), @('Prove the encoding with the compiled DLL', 'prove-encoding'), @('Check the Setup', 'check-setup'))) {
        Report "the step $($step[0]) runs $($step[1])" ($releaseText -cmatch "- name: $($step[0])\n(?:\s+timeout-minutes: \d+\n)?\s+run: \./tools/release/$($step[1])\.ps1 ")
    }
    Report 'the compiled DLL replaces the one in the published folder' ($releaseText -cmatch '- name: Replace the DLL in the published folder\n\s+run: Copy-Item -LiteralPath "\$env:RUNNER_TEMP/lame/libmp3lame\.64\.dll" -Destination "\$env:RUNNER_TEMP/publish/libmp3lame\.64\.dll" -Force')
    Report 'the lame workflow builds and proves' (($lameText -cmatch '\./tools/release/build-lame\.ps1 ') -and ($lameText -cmatch '\./tools/release/prove-encoding\.ps1 '))
    Report 'the release sees all the tags'($releaseText -match '(?m)^\s+fetch-depth: 0$')
    Report 'the release checks the version jump, with the confirmation' ($releaseText -cmatch 'check-tag\.ps1 -Tag \$env:TAG -MainRef origin/main -CheckVersionJump -ConfirmVersion \$env:RELEASE_CONFIRM_VERSION')
    Report 'the confirmation comes from a repository variable, through env' ($releaseText -cmatch 'RELEASE_CONFIRM_VERSION: \$\{\{ vars\.RELEASE_CONFIRM_VERSION \}\}')
    Report 'the release attaches the Setup, the manifest and the LAME source' ($releaseText -cmatch 'gh release upload [^\n]*Setup\.exe[^\n]*hindsound-update-win-x64\.json[^\n]*lame-3\.100\.tar\.gz')
    Report 'the release downloads its files back and checks them, before publishing' ($releaseText -cmatch '--clobber\n\s+if \(\$LASTEXITCODE -ne 0\) \{ exit \$LASTEXITCODE \}\n\s+gh release download \$env:TAG --dir "\$env:RUNNER_TEMP/verify"\n\s+if \(\$LASTEXITCODE -ne 0\) \{ exit \$LASTEXITCODE \}\n\s+\./tools/release/check-release-files\.ps1 -Folder "\$env:RUNNER_TEMP/verify" -Version \$env:VERSION -Repository \$env:GITHUB_REPOSITORY\n\n\s+# A rerun')
    Report 'the release does not give check-release-files its LAME hash' (-not ($releaseText -match '-LameSha256'))
    Assert-Equal 'the token goes to the three steps of the release' 3 ([regex]::Matches($releaseText, 'GH_TOKEN:')).Count
    $firstToken = @($release | Select-String 'GH_TOKEN:')[0].LineNumber
    $draftStep = @($release | Select-String '- name: Create the draft release')[0].LineNumber
    Report 'no step before the draft gets the token' ($firstToken -gt $draftStep)
    Report 'no step of the release is conditional' (-not ($releaseText -match '(?m)^\s+if:'))
    Report 'the release runs the tests' ($releaseText -cmatch '- name: Test\n\s+run: dotnet test Hindsound\.slnx -c Release --filter "Category!=Device"')
    Report 'the release tests its scripts' ($releaseText -cmatch '- name: Test the release scripts\n\s+run: \./tools/release/test-release-scripts\.ps1')
    Report 'the release starts as a verified draft' ($releaseText.Contains('gh release create $env:TAG --draft --verify-tag '))
    Report 'a published release is refused' ($releaseText.Contains('if ($isDraft -eq ''false'') { throw "$env:TAG is already published." }'))
    Report 'a draft is reused' ($releaseText.Contains('if ($isDraft -eq ''true'') {'))
    Report 'the token stays out of git' ($releaseText -match '(?m)^\s+persist-credentials: false$')
    Report 'the workflow starts with no permission' ($releaseText -match '(?m)^permissions: \{\}$')
    Report 'the version comes from check-tag' ($releaseText.Contains('"VERSION=$version" >> $env:GITHUB_ENV'))
    Report 'the releases run one at a time, none cancelled' ($releaseText -cmatch '(?m)^concurrency:\n  group: release\n  cancel-in-progress: false$')
    Report 'the release checks the tag on the remote just before publishing' ($releaseText -cmatch '- name: Publish the release\n\s+env:\n[^\n]+\n[^\n]+\n\s+run: \|\n\s+\./tools/release/check-remote-tag\.ps1 -Tag \$env:TAG -Commit \(git rev-parse HEAD\)\n\s+gh release edit \$env:TAG --draft=false')
    Report 'the lame workflow only reads' (($lameText -match '(?m)^permissions:\n  contents: read$') -and -not ($lameText -match 'contents: write|GH_TOKEN|gh release'))

    foreach ($workflow in @(@('release.yml', $release), @('lame.yml', $lame))) {
        $text = $workflow[1] -join "`n"
        $runText = @(Get-RunBlocks $workflow[1]) -join "`n"
        Report "$($workflow[0]) has run blocks" ($runText.Length -gt 0)
        Report "$($workflow[0]) puts no expression in a run block" (-not $runText.Contains('${{'))
        Report "$($workflow[0]) lets no step fail silently" (-not ($text -match 'continue-on-error'))
        Report "$($workflow[0]) does not use the test parameters of build-lame" (-not ($text -match '-ExpectedSha256|-VsDevCmd'))
        Report "$($workflow[0]) does not give build-lame its addresses" (-not ($text -match '-Urls'))
        foreach ($uses in @($workflow[1] | Where-Object { $_ -match '^\s*(- )?uses:' })) {
            Report "$($workflow[0]) pins $($uses.Trim())" ($uses -cmatch '@[0-9a-f]{40} # v[0-9]')
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
