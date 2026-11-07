<#
.SYNOPSIS
    Undoes the guards of the release scripts and workflows one at a time, on a copy, and fails if a test run does not notice.
.DESCRIPTION
    Each entry replaces one piece of text, found exactly once, in a new copy of tools/release and .github/workflows, then runs
    test-release-scripts.ps1 from that copy. Nothing in the repository changes. Each entry takes about 20 to 60 s, depending
    on the load of the PC.
.PARAMETER From
    The first entry to run, from 1.
.PARAMETER To
    The last entry to run. By default, the last one.
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\release\mutate-release-guards.ps1
#>
param(
    [int]$From = 1,
    [int]$To = [int]::MaxValue
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
    @($assets, 'if (($names -join '', '') -cne ($expected -join '', '')) {', 'if ($false) {')
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
    @($release, 'persist-credentials: false', 'persist-credentials: true')
    @($release, 'permissions: {}', 'permissions: write-all')
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
)

$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).ProviderPath
$shell = (Get-Process -Id $PID).Path
# PowerShell flattens the list above into one array: three values per entry.
$entries = [int]($mutations.Count / 3)
$last = [Math]::Min($To, $entries)
$totalFailures = 0
$missed = @()

for ($index = $From; $index -le $last; $index++) {
    $file, $find, $replace = $mutations[(3 * ($index - 1))..(3 * $index - 1)]

    $copy = Join-Path ([IO.Path]::GetTempPath()) ('hindsound-mutation-' + [Guid]::NewGuid().ToString('N'))
    try {
        foreach ($folder in 'tools/release', '.github/workflows') {
            $target = Join-Path $copy $folder
            New-Item -ItemType Directory -Force -Path $target | Out-Null
            Copy-Item -Path (Join-Path $repository "$folder/*") -Destination $target
        }

        $path = Join-Path $copy $file
        $text = [IO.File]::ReadAllText($path)
        $count = ($text.Length - $text.Replace($find, '').Length) / $find.Length
        if ($count -ne 1) {
            throw "Entry $index is out of date: its text is found $count times in ${file}: $find"
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

Write-Output "Entries $From to $last of ${entries}: $totalFailures failure(s) in all, missed: $($missed.Count)."
if ($missed.Count -gt 0) {
    throw "Guards undone without a failing test: entries $($missed -join ', ')."
}
