<#
.SYNOPSIS
    Undoes the guards of the release scripts and workflows one at a time, on a copy, and fails if a test run does not notice.
.DESCRIPTION
    Each entry replaces one piece of text, found exactly once, in a new copy of what test-release-scripts.ps1 reads: tools/release,
    tools/mac, .github/workflows, licenses, LICENSE, THIRD-PARTY-NOTICES.md, packaging/windows/portable.txt and README.txt, packaging/mac, and
    installer/Hindsound.iss, in a git repository of one empty commit (the date of the zip). It then runs test-release-scripts.ps1 from that copy. Nothing in the repository changes. Each entry
    takes about 20 to 60 s, depending on the load of the PC.
.PARAMETER WorkFolder
    Where the copies are made, one at a time and removed after each entry. By default, the temporary folder of the user.
.PARAMETER From
    The first entry to run, from 1.
.PARAMETER To
    The last entry to run. By default, the last one.
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\release\mutate-release-guards.ps1
#>
param(
    [int]$From = 1,
    [int]$To = [int]::MaxValue,
    [string]$WorkFolder = [IO.Path]::GetTempPath()
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$tag = 'tools/release/check-tag.ps1'
$dll = 'tools/release/check-lame-dll.ps1'
$build = 'tools/release/build-lame.ps1'
$setup = 'tools/release/check-setup.ps1'
$prove = 'tools/release/prove-encoding.ps1'
$remote = 'tools/release/check-remote-tag.ps1'
$assets = 'tools/release/check-release-files.ps1'
$iscc = 'tools/release/get-iscc.ps1'
$release = '.github/workflows/release.yml'
$lame = '.github/workflows/lame.yml'

$mutations = @(
    # The version guard
    @($tag, 'if ($CheckVersionJump) {', 'if ($false) {')
    @($tag, 'if ($name -ceq $Tag) { continue }', '')
    @($tag, 'if ($null -eq $other) { continue }', 'if ($null -eq $other) { $other = [bigint[]]@(99, 0, 0) }')
    @($tag, '(Compare-VersionParts $other $highest) -gt 0', '(Compare-VersionParts $other $highest) -lt 0')
    @($tag, 'if ($Left[$i] -gt $Right[$i]) { return 1 }', 'if ("$($Left[$i])" -gt "$($Right[$i])") { return 1 }')
    @($tag, 'if ($parts[0] -ne 0) {', 'if ($false) {')
    @($tag, '(Compare-VersionParts $parts $highest) -le 0', '$false')
    @($tag, '$isNextPatch = $sameMinor -and $parts[2] -eq ($highest[2] + 1)', '$isNextPatch = $sameMinor')
    @($tag, '-and $parts[1] -eq ($highest[1] + 1) -and $parts[2] -eq 0', '-and $parts[1] -eq ($highest[1] + 1)')
    @($tag, '-and $parts[1] -eq ($highest[1] + 1) -and', '-and $parts[1] -gt $highest[1] -and')
    @($tag, '$isNextMinor = $parts[0] -eq $highest[0] -and', '$isNextMinor =')
    @($tag, '$ConfirmVersion -cne $version', '-not $ConfirmVersion')
    @($tag, "throw 'git tag failed: the earlier versions are unknown.'", '$names = @()')

    # The checks of the DLL
    @($dll, 'if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {', 'if ($false) {')
    @($dll, 'if ($summary.Machine -ne 0x8664 -or $summary.Magic -ne 0x20B) {', 'if ($false) {')
    @($dll, 'if (-not $summary.IsDll) {', 'if ($false) {')
    @($dll, 'if ($badImports.Count -gt 0) {', 'if ($false) {')
    @($dll, 'if ($missing.Count -gt 0) {', 'if ($false) {')
    @($dll, 'if ($lost.Count -gt 0) {', 'if ($false) {')
    @($dll, 'if ($version -cne $expectedVersion) {', 'if ($false) {')

    # The build of the DLL
    @($build, 'if ($actualSha256 -cne $ExpectedSha256) {', 'if ($false) {')
    @($build, 'if (-not (Test-Path -LiteralPath $VsDevCmd)) {', 'if ($false) {')
    @($build, 'if ($DownloadOnly) { return }', '')
    @($build, 'if (Test-Path -LiteralPath $sourceRoot) {', 'if ($false) {')
    @($build, 'if (-not (Test-Path -LiteralPath (Join-Path $source $needed))) {', 'if ($false) {')
    @($build, 'if ($buildExit -eq 101) {', 'if ($false) {')
    @($build, 'if ($buildExit -ne 0) {', 'if ($false) {')
    @($build, 'if ($changed.Count -gt 0) {', 'if ($false) {')
    @($build, 'if (-not (Test-Path -LiteralPath $built)) {', 'if ($false) {')
    @($build, "& (Join-Path `$PSScriptRoot 'check-lame-dll.ps1') @checkArguments", '')
    @($build, 'Makefile.MSVC MSVCVER=Win64 MACHINE', 'Makefile.MSVC MACHINE')
    @($build, 'MSVCVER=Win64 MACHINE=/machine:X64 ADDL_OBJ', 'MSVCVER=Win64 ADDL_OBJ')
    @($build, 'MACHINE=/machine:X64 ADDL_OBJ=kernel32.lib libmp3lame', 'MACHINE=/machine:X64 libmp3lame')
    @($build, 'Get-Content -LiteralPath $log | ForEach-Object { Write-Output $_ }', '')

    # The check of the Setup
    @($setup, "if (`$env:GITHUB_ACTIONS -cne 'true' -or `$env:RUNNER_ENVIRONMENT -cne 'github-hosted') {", 'if ($false) {')
    @($setup, 'if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {', 'if ($false) {')
    @($setup, 'if ($fileVersion -cne "$Version.0") {', 'if ($false) {')
    @($setup, "Assert-SameFile (Join-Path `$Folder 'libmp3lame.64.dll') `$DllPath", '')
    @($setup, 'if (-not (Test-Path -LiteralPath $Installed -PathType Leaf)) {', 'if ($false) {')
    @($setup, 'if ((Get-Sha256 $Installed) -cne (Get-Sha256 $Expected)) {', 'if ($false) {')
    @($setup, "foreach (`$name in 'LICENSE', 'THIRD-PARTY-NOTICES.md') {", 'foreach ($name in @()) {')
    @($setup, 'if ($expectedLicences.Count -eq 0) {', 'if ($false) {')
    @($setup, 'foreach ($licence in $expectedLicences) {', 'foreach ($licence in @()) {')

    # The proof of the encoding
    @($prove, 'if ([int]$counters.failed -ne 0) {', 'if ($false) {')
    @($prove, 'if ([int]$counters.passed -ne $expectedPassed) {', 'if ($false) {')
    @($prove, 'if ($inPlace.Count -eq 0) {', 'if ($false) {')

    # The workflows
    @($release, 'fetch-depth: 0', 'fetch-depth: 1')
    @($release, '-MainRef origin/main -CheckVersionJump', '-MainRef origin/main')
    @($release, '${{ vars.RELEASE_CONFIRM_VERSION }}', '"1.0.0"')
    @($release, ' "$env:RUNNER_TEMP/lame/lame-3.100.tar.gz"', '')
    @($release, './tools/release/check-setup.ps1 -SetupPath', 'Write-Output -SetupPath')
    @($release, './tools/release/prove-encoding.ps1', 'Write-Output')
    @($release, './tools/release/build-lame.ps1 -WorkDir', 'Write-Output -WorkDir')
    @($release, 'run: Copy-Item -LiteralPath "$env:RUNNER_TEMP/lame/libmp3lame.64.dll"', 'run: Write-Output -LiteralPath "$env:RUNNER_TEMP/lame/libmp3lame.64.dll"')
    @($release, '- name: Check the Setup', '- name: Check it')
    @($release, '- name: Check the Setup', "- name: Check the Setup`n        continue-on-error: true")
    @($release, '- name: Check the Setup', "- name: Check the Setup`n        if: always()")
    @($release, '-Tag $env:TAG -MainRef', '-Tag ${{ github.ref_name }} -MainRef')
    @($release, '- name: Publish the app', "- name: Publish the app`n        env:`n          GH_TOKEN: x")
    @($release, '-OutFile "$env:RUNNER_TEMP/lame/libmp3lame.64.dll"', '-ExpectedSha256 x -OutFile "$env:RUNNER_TEMP/lame/libmp3lame.64.dll"')
    @($release, 'actions/setup-dotnet@0f4a354dd5547a28c70b7853dbd95d5b67886e73 # v6.1.0', 'actions/setup-dotnet@v6')
    @($lame, 'contents: read', 'contents: write')
    @($lame, './tools/release/build-lame.ps1', 'Write-Output')
    @($lame, './tools/release/prove-encoding.ps1', 'Write-Output')

    # The MP3 of the installed exe
    @($setup, 'if ($wav.Count -gt 0) {', 'if ($false) {')
    @($setup, 'if ($mp3.Count -ne 1 -or $files.Count -ne 1) {', 'if ($false) {')
    @($setup, '$_.Extension -eq ''.mp3'' -and $_.Length -gt 0', '$_.Extension -eq ''.mp3''')
    @($setup, '--prove-mp3 `"$mp3`"', '--dump-notifications `"$mp3`"')
    @($setup, "`n    Assert-Mp3Folder `$mp3", '')

    # One release at a time
    @($release, 'cancel-in-progress: false', 'cancel-in-progress: true')
    @($release, '  group: release', '  group: release-${{ github.ref_name }}')

    # The tag on the remote, just before publishing
    @($remote, 'if ($LASTEXITCODE -ne 0) {', 'if ($false) {')
    @($remote, 'if ($found.ContainsKey("refs/tags/$Tag^{}")) {', 'if ($false) {')
    @($remote, 'if ($null -eq $remoteCommit) {', 'if ($false) {')
    @($remote, 'if ($remoteCommit -cne $Commit) {', 'if ($false) {')
    @($release, './tools/release/check-remote-tag.ps1 -Tag $env:TAG -Commit (git rev-parse HEAD)', 'Write-Output check')

    # The files of the draft, downloaded back
    @($assets, 'if (($names -join '', '') -cne ($expectedNames -join '', '')) {', 'if ($false) {')
    @($assets, 'if ("$($manifest.version)" -cne $Version) {', 'if ($false) {')
    @($assets, 'if ("$($manifest.url)" -cne $url) {', 'if ($false) {')
    @($assets, 'if ("$($manifest.size)" -cne "$($setup.Length)") {', 'if ($false) {')
    @($assets, 'if ("$($manifest.sha256)" -cne $setupSha256) {', 'if ($false) {')
    @($assets, 'if ($lameActual -cne $LameSha256) {', 'if ($false) {')
    @($release, 'gh release download $env:TAG --dir', 'Write-Output $env:TAG --dir')
    @($release, './tools/release/check-release-files.ps1 -Folder', 'Write-Output -Folder')

    # The download of the LAME archive
    @($build, 'elseif ((Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash.ToLowerInvariant() -ceq $ExpectedSha256) {', 'elseif ($true) {')
    @($build, 'if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive }', '')
    @($build, 'if (-not (Test-Path -LiteralPath $archive)) {', 'if ($false) {')

    # The guards of the release workflow itself
    @($release, "if (`$isDraft -eq 'false') {", 'if ($false) {')
    @($release, "if (`$isDraft -eq 'true') {", 'if ($false) {')
    @($release, '--draft --verify-tag', '--verify-tag')
    @($release, '--draft --verify-tag', '--draft')
    @($release, "fetch-depth: 0`n          persist-credentials: false", "fetch-depth: 0`n          persist-credentials: true")
    @($release, "persist-credentials: false`n`n      - name: Write the release notes", "persist-credentials: true`n`n      - name: Write the release notes")
    @($release, "- 'v*'`n`npermissions: {}", "- 'v*'`n`npermissions: write-all")
    @($release, "timeout-minutes: 5`n    permissions: {}", "timeout-minutes: 5`n    permissions: write-all")
    @($release, 'run: dotnet test Hindsound.slnx', 'run: Write-Output Hindsound.slnx')
    @($release, 'run: ./tools/release/test-release-scripts.ps1', 'run: Write-Output')
    @($release, '"VERSION=$version" >> $env:GITHUB_ENV', '"VERSION=0.1.0" >> $env:GITHUB_ENV')

    # The same minor, for the next patch
    @($tag, '$sameMinor = $parts[0] -eq $highest[0] -and $parts[1] -eq $highest[1]', '$sameMinor = $parts[1] -eq $highest[1]')
    @($tag, '$sameMinor = $parts[0] -eq $highest[0] -and $parts[1] -eq $highest[1]', '$sameMinor = $parts[0] -eq $highest[0]')

    # The package of the Inno Setup compiler
    @($iscc, 'if ($actual -ne $packageSha256) {', 'if ($false) {')

    # The list of exports
    @($dll, 'if ($required.Count -eq 0) {', 'if ($false) {')

    # The version read as text in the changelog
    @('tools/release/extract-changelog.ps1', '[regex]::Escape($Version)', '$Version')

    # The delay-loaded imports
    @($dll, 'if ($summary.DelayImports) {', 'if ($false) {')

    # The latest release, which the app reads
    @($release, '--draft=false --latest', '--draft=false')

    # The job ends by itself
    @($release, '    timeout-minutes: 60', '    # timeout-minutes: 60')

    # A failure of gh release view that is not "release not found"
    @($release, 'if ($viewExit -ne 0 -and "$view" -notmatch ''release not found'') {', 'if ($false) {')
    @($release, '$viewExit = $LASTEXITCODE', '$viewExit = 0')

    # The clip must be a real MP3 of about one second
    @($setup, 'if ($bytes.Length -lt 2 -or $bytes[0] -ne 0xFF -or ($bytes[1] -band 0xF0) -ne 0xF0) {', 'if ($false) {')
    @($setup, '$bytes[0] -ne 0xFF -or ($bytes[1] -band 0xF0) -ne 0xF0', '$bytes[0] -ne 0xFF')
    @($setup, 'if ($bytes.Length -lt 20000 -or $bytes.Length -gt 30000) {', 'if ($false) {')
    @($setup, '$bytes.Length -lt 20000 -or ', '')
    @($setup, ' -or $bytes.Length -gt 30000', '')

    # The clip must be in the folder itself, not in the rescue folder
    @($setup, 'if ($mp3[0].DirectoryName -ne $root) {', 'if ($false) {')

    # The .deb: in the release, checked as a Debian package, and installed before the release gathers its files
    @($assets, '$zipName, "hindsound-$Version-linux-x64.AppImage", "hindsound-$Version-linux-x64.tar.gz", $debName,', '$zipName, "hindsound-$Version-linux-x64.AppImage", "hindsound-$Version-linux-x64.tar.gz",')
    @($assets, 'if (-not $debHead.StartsWith("!<arch>`ndebian-binary") -or $debHead.Substring(68, 4) -cne "2.0`n") {', 'if ($false) {')
    @('tools/release/assemble-release.ps1', ', "hindsound_${Version}_amd64.deb")', ')')
    @($release, ' "$env:RUNNER_TEMP/out/hindsound_${env:VERSION}_amd64.deb"', '')
    @('.github/workflows/release-files.yml', 'run: bash packaging/linux/try-deb.sh linux-files', 'run: echo packaging/linux/try-deb.sh linux-files')
    @('.github/workflows/release-files.yml', "image: ['ubuntu:22.04', 'ubuntu:24.04', 'debian:12', 'debian:13']", "image: ['ubuntu:24.04']")
    @('.github/workflows/release-files.yml', "'debian:12', 'debian:13']", "'debian:12']")
    # The Mac app
    @('tools/release/assemble-release.ps1', 'foreach ($name in $macFiles) {', 'foreach ($name in @()) {')
    @('tools/release/assemble-release.ps1', "'hindsound-update-linux-x64.json', 'hindsound-update-macos-arm64.json') {", "'hindsound-update-linux-x64.json', 'hindsound-update-macos-x64.json') {")
    @($assets, 'if ($entry -cmatch ''(^|/)\._|__MACOSX'') {', 'if ($false) {')
    @($release, ' "$env:RUNNER_TEMP/out/Hindsound-$env:VERSION-macos-arm64.zip"', '')
    @('.github/workflows/release-files.yml', 'run: bash packaging/mac/check-zip.sh', 'run: echo packaging/mac/check-zip.sh')
    # The certificate of the Mac app, read by sign-mac of release.yml only, and the zip it signs, the one sent
    @($release, ('name: release-files-signed' + "`n" + '          path: ${{ runner.temp }}/out'), ('name: release-files' + "`n" + '          path: ${{ runner.temp }}/out'))
    @($release, ('name: hindsound-macos-arm64-signed' + "`n" + '          path: ${{ runner.temp }}/macos'), ('name: hindsound-macos-arm64' + "`n" + '          path: ${{ runner.temp }}/macos'))
    @($release, 'bash packaging/mac/resign-zip.sh "$RUNNER_TEMP/adhoc/$zip"', 'echo packaging/mac/resign-zip.sh "$RUNNER_TEMP/adhoc/$zip"')
    @($release, ('      version: ${{ needs.check.outputs.version }}' + "`n`n" + '  # The Mac app'), ('      version: ${{ needs.check.outputs.version }}' + "`n" + '    secrets: inherit' + "`n`n" + '  # The Mac app'))
    @($release, 'needs: [check, files, sign-mac, assemble-signed, release]', 'needs: [check, files, release]')
    @('.github/workflows/release-files.yml', '        run: bash packaging/mac/build-app.sh', ('        env:' + "`n" + '          P12: ${{ secrets.MACOS_SIGNING_P12 }}' + "`n" + '        run: bash packaging/mac/build-app.sh'))
    @('.github/workflows/release-files.yml', 'bash packaging/mac/resign-zip.sh "$RUNNER_TEMP/out/$zip" "$RUNNER_TEMP/rehearsal/', 'echo packaging/mac/resign-zip.sh "$RUNNER_TEMP/out/$zip" "$RUNNER_TEMP/rehearsal/')
    # The checkouts of sign-mac and assemble-signed keep the token out of git too
    @($release, "persist-credentials: false`n`n      - name: Take the Mac app signed ad hoc", "persist-credentials: true`n`n      - name: Take the Mac app signed ad hoc")
    @($release, "persist-credentials: false`n`n      - name: Take the Windows files", "persist-credentials: true`n`n      - name: Take the Windows files")
    # The certificate lives in the environment mac-signing; the SHA-1 committed decides, and a missing secret stops the release
    @($release, ('timeout-minutes: 30' + "`n" + '    environment: mac-signing' + "`n"), ('timeout-minutes: 30' + "`n"))
    @($release, ('if [ -s packaging/mac/signing-certificate.sha1 ]; then' + "`n"), ('if false; then' + "`n"))
    @($release, '[ -n "$MACOS_SIGNING_P12" ] && [ -n "$MACOS_SIGNING_P12_PASSWORD" ] ||', 'true ||')
    # The app starts only in the step without the secret, once the .p12 is gone
    @($release, 'bash packaging/mac/check-zip.sh "$RUNNER_TEMP/signed/$zip"', 'echo packaging/mac/check-zip.sh "$RUNNER_TEMP/signed/$zip"')
    @($release, ('- name: Check the signed Mac app' + "`n" + '        timeout-minutes: 15' + "`n"), ('- name: Check the signed Mac app' + "`n" + '        timeout-minutes: 15' + "`n" + '        env:' + "`n" + '          P12: ${{ secrets[''MACOS_SIGNING_P12''] }}' + "`n"))
    @($release, ('            rm -rf "$signing"' + "`n" + '          else'), ('            bash packaging/mac/check-zip.sh "$RUNNER_TEMP/signed/$zip" "$VERSION" "$RUNNER_TEMP/early-check"' + "`n" + '            rm -rf "$signing"' + "`n" + '          else'))
    @($release, ('            trap ''rm -rf "$signing"'' EXIT' + "`n"), '')
    @('packaging/mac/resign-zip.sh', 'echo "resign-zip: $(basename "$out")', ('bash "$here/check-zip.sh" "$out" 1.0.0 "$work/check" "$expected"' + "`n" + 'echo "resign-zip: $(basename "$out")'))
    @('packaging/mac/resign-zip.sh', '[ "$certificate" = "$expected" ] || fail', 'true || fail')
    @('.github/workflows/release-files.yml', ('"$RUNNER_TEMP/rehearsal-work" "$sha1"' + "`n" + '          rm -rf "$certificate"' + "`n"), ('"$RUNNER_TEMP/rehearsal-work" "$sha1"' + "`n"))
    @('.github/workflows/release-files.yml', 'grep -F ''which is not the certificate', 'echo ''which is not the certificate')
    # Every way to read a secret, in release.yml and in the CI of the pull requests
    @($release, ('    environment: mac-signing' + "`n" + '    permissions:' + "`n" + '      contents: read' + "`n" + '    env:' + "`n"), ('    environment: mac-signing' + "`n" + '    permissions:' + "`n" + '      contents: read' + "`n" + '    env:' + "`n" + '      ALL: ${{ toJSON(secrets) }}' + "`n"))
    @('.github/workflows/ci.yml', ('    uses: ./.github/workflows/release-files.yml' + "`n"), ('    uses: ./.github/workflows/release-files.yml' + "`n" + '    secrets: inherit' + "`n"))
    # The keychain and the key in clear go whatever happens
    @('packaging/mac/sign-app.sh', ('  trap cleanup EXIT' + "`n"), '')
    @('packaging/mac/sign-app.sh', ('    security delete-keychain "$keychain" 2> /dev/null || true' + "`n"), '')
    @('packaging/mac/make-signing-certificate.sh', ('trap ''rm -f "$out/key.pem" "$out/certificate.cnf"'' EXIT' + "`n"), '')
)

$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).ProviderPath
$shell = (Get-Process -Id $PID).Path
# PowerShell flattens the list above into one array: three values per entry.
$entries = [int]($mutations.Count / 3)
$last = [Math]::Min($To, $entries)
$totalFailures = 0
$missed = @()
$stale = @()

for ($index = $From; $index -le $last; $index++) {
    $file, $find, $replace = $mutations[(3 * ($index - 1))..(3 * $index - 1)]

    $copy = Join-Path $WorkFolder ('hindsound-mutation-' + [Guid]::NewGuid().ToString('N'))
    try {
        # What test-release-scripts.ps1 reads from the repository: the scripts, the workflows, and the files of the packages it compares.
        foreach ($folder in 'tools/release', 'tools/mac', '.github/workflows', 'licenses', 'packaging/windows', 'packaging/mac', 'installer') {
            $target = Join-Path $copy $folder
            New-Item -ItemType Directory -Force -Path $target | Out-Null
            Copy-Item -Path (Join-Path $repository "$folder/*") -Destination $target
        }
        foreach ($shared in 'LICENSE', 'THIRD-PARTY-NOTICES.md') {
            Copy-Item -LiteralPath (Join-Path $repository $shared) -Destination (Join-Path $copy $shared)
        }

        # The test of the portable zip takes the date of the commit of the repository it runs in: the copy is one, with an empty commit.
        & git -C $copy init -q
        & git -C $copy -c user.name=mutation -c user.email=mutation@example.invalid commit -q --allow-empty -m copy
        if ($LASTEXITCODE -ne 0) { throw 'git could not make the commit of the copy.' }

        $path = Join-Path $copy $file
        # A Windows checkout has CRLF; the texts of the entries are written with LF.
        $text = [IO.File]::ReadAllText($path).Replace("`r`n", "`n")
        $count = ($text.Length - $text.Replace($find, '').Length) / $find.Length
        # The steps of the release moved from release.yml to release-files.yml: an entry whose text is no longer in the first
        # is looked for in the second.
        if ($count -ne 1 -and $file -eq $release) {
            $moved = Join-Path $copy '.github/workflows/release-files.yml'
            $movedText = [IO.File]::ReadAllText($moved).Replace("`r`n", "`n")
            if (($movedText.Length - $movedText.Replace($find, '').Length) / $find.Length -eq 1) {
                $file = '.github/workflows/release-files.yml'
                $path = $moved
                $text = $movedText
                $count = 1
            }
        }
        if ($count -ne 1) {
            Write-Output ('{0,3} STALE  {1}: its text is found {2} times: {3}' -f $index, (Split-Path -Leaf $file), $count, $find.Replace("`n", ' '))
            $stale += $index
            continue
        }
        [IO.File]::WriteAllText($path, $text.Replace($find, $replace))

        # Continue: in Windows PowerShell, with Stop, the error stream of a program ends this script. A failed run writes there.
        $ErrorActionPreference = 'Continue'
        $output = & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $copy 'tools/release/test-release-scripts.ps1') 2> $null
        $exit = $LASTEXITCODE
        $ErrorActionPreference = 'Stop'
        $failures = @($output | Where-Object { $_ -like 'FAIL *' })
    }
    finally {
        if (Test-Path -LiteralPath $copy) { Remove-Item -LiteralPath $copy -Recurse -Force }
    }

    $totalFailures += $failures.Count
    $state = if ($exit -ne 0 -and $failures.Count -gt 0) { 'caught' } else { 'MISSED' }
    if ($state -eq 'MISSED') { $missed += $index }
    # Several entries look for the same text: the replacement tells which variant this one is.
    $label = if ($replace) { $replace } else { '(removed)' }
    if ($label.Length -gt 70) { $label = $label.Substring(0, 70) + '...' }
    Write-Output ('{0,3} {1} {2,3} fail(s)  {3}: {4}  =>  {5}' -f $index, $state, $failures.Count, (Split-Path -Leaf $file), $find, $label)
    foreach ($failure in $failures | Select-Object -First 2) {
        $short = "$failure"
        if ($short.Length -gt 110) { $short = $short.Substring(0, 110) }
        Write-Output "      $short"
    }
}

Write-Output "Entries $From to $last of ${entries}: $totalFailures failure(s) in all, missed: $($missed.Count), out of date: $($stale.Count)."
if ($stale.Count -gt 0) {
    throw "Entries out of date, their text is no longer in the file: $($stale -join ', ')."
}
if ($missed.Count -gt 0) {
    throw "Guards undone without a failing test: entries $($missed -join ', ')."
}
