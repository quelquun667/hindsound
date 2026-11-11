<#
.SYNOPSIS
    Writes the notes of a release: its section of the changelog, then the files of the release and whom each one is for.
.PARAMETER Version
    The version, X.Y.Z.
.PARAMETER ChangelogFile
    The section of the changelog, as extract-changelog.ps1 wrote it.
.PARAMETER OutFile
    The notes to write, in UTF-8 without a BOM.
.EXAMPLE
    ./tools/release/write-release-notes.ps1 -Version 0.1.0 -ChangelogFile changelog.md -OutFile release-notes.md
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$ChangelogFile,
    [Parameter(Mandatory = $true)][string]$OutFile
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($Version -cnotmatch '\A[0-9]+\.[0-9]+\.[0-9]+\z') {
    throw "Not a version: $Version."
}

$changelog = [IO.File]::ReadAllText($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ChangelogFile)).Replace("`r`n", "`n").Trim()
if ($changelog -eq '') {
    throw "$ChangelogFile is empty: a release never goes out without its notes."
}

$downloads = @"
## Downloads

| File | For |
| --- | --- |
| ``Hindsound-$Version-Setup.exe`` | Windows, 64-bit: installs Hindsound for your user and keeps it up to date. Start here on Windows. |
| ``Hindsound-$Version-win-x64-portable.zip`` | Windows, 64-bit, without installing anything: unzip it and start ``Hindsound.exe``. It does not update itself: it tells you when a new version is out, with a link to its release. |
| ``hindsound-$Version-linux-x64.AppImage`` | Linux, 64-bit: one file to make executable and run. Start here on Linux. |
| ``hindsound-$Version-linux-x64.tar.gz`` | Linux, 64-bit, where the AppImage does not run: a folder to unpack, with a ``README.txt`` that says how to install it. |
| ``hindsound-update-win-x64.json``, ``hindsound-update-win-x64-portable.json``, ``hindsound-update-linux-x64.json`` | Read by Hindsound to learn that this version is out. Nothing to download. |
| ``lame-3.100.tar.gz`` | The source of LAME, the MP3 encoder that every download above carries, under the LGPL. |
| ``fuse-3.15.0.tar.xz`` | The source of libfuse, which the AppImage carries in its runtime, the program at its head that mounts it: offered next to the AppImage under the LGPL-2.1, unchanged. |
| ``type2-runtime-20251108-source.tar.gz`` | The source of that AppImage runtime, with the change it makes to libfuse (``patches/libfuse/mount.c.diff``) and its build scripts: offered for the LGPL-2.1 of libfuse. |
| ``SHA256SUMS`` | The SHA-256 of every file above. On Linux: ``sha256sum --check --ignore-missing SHA256SUMS``. |

Hindsound is not signed: Windows will probably show **Windows protected your PC** for the Setup and for the program of the
zip. Click **More info**, then **Run anyway**.
"@

$notes = $changelog + "`n`n" + $downloads.Replace("`r`n", "`n").Trim() + "`n"
[IO.File]::WriteAllText($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutFile), $notes, (New-Object Text.UTF8Encoding $false))
