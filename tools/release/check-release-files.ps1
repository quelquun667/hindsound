<#
.SYNOPSIS
    Checks the files of a release: as assemble-release.ps1 gathered them, and as GitHub serves them from the draft, downloaded
    back into a folder, before it goes public.
.DESCRIPTION
    An immutable release takes no change once published, so a file damaged on its way up, or one left by an earlier run,
    would reach the users. The folder must hold exactly the files of the release:
      Hindsound-<v>-Setup.exe, Hindsound-<v>-win-x64-portable.zip, hindsound-<v>-linux-x64.AppImage,
      hindsound-<v>-linux-x64.tar.gz, hindsound-update-win-x64.json, hindsound-update-win-x64-portable.json,
      hindsound-update-linux-x64.json, lame-3.100.tar.gz, fuse-3.15.0.tar.xz, type2-runtime-20251108-source.tar.gz,
      type2-runtime-20251108-libraries-source.tar.gz and SHA256SUMS.
    The Setup manifest must give this version, the address of this Setup on the release of the tag, and the size and
    SHA-256 of the Setup in the folder. The two announce manifests must give this version and exactly the page of the
    release, and nothing else. The portable zip must hold portable.txt next to Hindsound.exe. The LAME source must have the
    SHA-256 given in THIRD-PARTY-NOTICES.md. fuse-3.15.0.tar.xz must have the SHA-256 that fetch-runtime-sources.sh pins,
    read from that script, and the source of the runtime must be a gzip archive: that script checked what it holds. The
    source of the libraries linked into the runtime must be a gzip archive of a tar that holds README.txt and exactly the
    files of LIBRARY_FILES in that script, under its prefix, in that order, each a plain file with its SHA-256 there.
    SHA256SUMS must list every other file once, with its SHA-256.
    With -Expected, every file must also have the size and SHA-256 of the file of that name in the folder that was sent.
.PARAMETER Folder
    The files of the release.
.PARAMETER Version
    The version, X.Y.Z.
.PARAMETER Repository
    owner/name on GitHub.
.PARAMETER Expected
    The folder the files were sent from, when Folder holds them as gh release download wrote them.
.PARAMETER LameSha256
    For test-release-scripts.ps1 only, with a fake archive. A workflow never passes it.
.PARAMETER FuseSha256
    For test-release-scripts.ps1 only, with a fake archive. A workflow never passes it: the SHA-256 is then read from
    fetch-runtime-sources.sh, the one place it is pinned.
.PARAMETER LibraryFiles
    For test-release-scripts.ps1 only, with a fake archive: lines '<sha256>  <path>', in place of LIBRARY_FILES. A workflow
    never passes it: the lines are then read from fetch-runtime-sources.sh, the one place they are pinned.
.EXAMPLE
    ./tools/release/check-release-files.ps1 -Folder verify -Version 0.1.0 -Repository quelquun667/hindsound -Expected out
#>
param(
    [Parameter(Mandatory = $true)][string]$Folder,
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$Repository,
    [string]$Expected = '',
    [string]$LameSha256 = 'ddfe36cab873794038ae2c1210557ad34857a4b6bdc515785d1da9e175b1da1e',
    [string]$LibraryFiles = '',
    [string]$FuseSha256 = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression

function Get-Sha256([string]$Path) {
    (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
}

function Get-SortedNames([string]$Path) {
    [string[]]$names = @(Get-ChildItem -LiteralPath $Path -Force | ForEach-Object Name)
    [Array]::Sort($names, [StringComparer]::Ordinal)
    , $names
}

function Read-Manifest([string]$Name) {
    $text = [IO.File]::ReadAllText((Join-Path $Folder $Name))
    $manifest = $text | ConvertFrom-Json
    if ("$($manifest.product)" -cne 'Hindsound') {
        throw "$Name on the release gives the product $($manifest.product), expected Hindsound."
    }
    if ("$($manifest.version)" -cne $Version) {
        throw "$Name on the release gives the version $($manifest.version), expected $Version."
    }
    if ($text -cnotmatch '"published":\s*"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z"') {
        throw "$Name on the release has no UTC date of publication."
    }
    $manifest
}

$setupName = "Hindsound-$Version-Setup.exe"
$zipName = "Hindsound-$Version-win-x64-portable.zip"
$manifestName = 'hindsound-update-win-x64.json'
$announceNames = @('hindsound-update-win-x64-portable.json', 'hindsound-update-linux-x64.json')
$lameName = 'lame-3.100.tar.gz'
$fuseName = 'fuse-3.15.0.tar.xz'
$runtimeSourceName = 'type2-runtime-20251108-source.tar.gz'
$librariesSourceName = 'type2-runtime-20251108-libraries-source.tar.gz'
$sumsName = 'SHA256SUMS'

[string[]]$expectedNames = @(
    $setupName, $zipName, "hindsound-$Version-linux-x64.AppImage", "hindsound-$Version-linux-x64.tar.gz",
    $manifestName, $announceNames[0], $announceNames[1], $lameName, $fuseName, $runtimeSourceName, $librariesSourceName,
    $sumsName
)
[Array]::Sort($expectedNames, [StringComparer]::Ordinal)
$names = Get-SortedNames $Folder
if (($names -join ', ') -cne ($expectedNames -join ', ')) {
    throw "The release holds $($names -join ', '), expected $($expectedNames -join ', ')."
}

# The Setup manifest: the one the installed app downloads and runs a Setup from.
$setup = Get-Item -LiteralPath (Join-Path $Folder $setupName)
$setupSha256 = Get-Sha256 $setup.FullName
$manifest = Read-Manifest $manifestName
$url = "https://github.com/$Repository/releases/download/v$Version/$setupName"
if ("$($manifest.url)" -cne $url) {
    throw "The manifest on the release gives the address $($manifest.url), expected $url."
}
if ("$($manifest.size)" -cne "$($setup.Length)") {
    throw "The manifest on the release gives the size $($manifest.size), and the Setup there has $($setup.Length) bytes."
}
if ("$($manifest.sha256)" -cne $setupSha256) {
    throw "The manifest on the release gives the SHA-256 $($manifest.sha256), and the Setup there has $setupSha256."
}

# The announce manifests: the page of the release, which the app opens, and nothing to download.
$page = "https://github.com/$Repository/releases/tag/v$Version"
foreach ($name in $announceNames) {
    $announce = Read-Manifest $name
    $fields = ($announce.PSObject.Properties | ForEach-Object Name) -join ' '
    if ($fields -cne 'product version url published') {
        throw "$name on the release has the fields $fields, expected product version url published."
    }
    if ("$($announce.url)" -cne $page) {
        throw "$name on the release gives the address $($announce.url), expected $page."
    }
}

# The portable zip: portable.txt next to Hindsound.exe is what makes the copy portable.
$top = "Hindsound-$Version-win-x64-portable"
$stream = [IO.File]::OpenRead((Join-Path $Folder $zipName))
try {
    $zip = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Read)
    try {
        $entries = @($zip.Entries | ForEach-Object FullName)
    }
    finally {
        $zip.Dispose()
    }
}
finally {
    $stream.Dispose()
}
foreach ($entry in "$top/Hindsound.exe", "$top/libmp3lame.64.dll", "$top/portable.txt") {
    if ($entries -cnotcontains $entry) {
        throw "$zipName on the release has no $entry."
    }
}

$lameActual = Get-Sha256 (Join-Path $Folder $lameName)
if ($lameActual -cne $LameSha256) {
    throw "$lameName on the release has the SHA-256 $lameActual, expected $LameSha256."
}

# The source of libfuse and of the AppImage runtime, which the LGPL-2.1 of libfuse asks to offer next to the AppImage.
if ($FuseSha256 -eq '') {
    $fetchScript = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'fetch-runtime-sources.sh'))
    if ($fetchScript -cnotmatch '(?m)^readonly FUSE_SHA256=''([0-9a-f]{64})''\r?$') {
        throw "fetch-runtime-sources.sh does not pin the SHA-256 of $fuseName."
    }
    $FuseSha256 = $Matches[1]
}
$fuseActual = Get-Sha256 (Join-Path $Folder $fuseName)
if ($fuseActual -cne $FuseSha256) {
    throw "$fuseName on the release has the SHA-256 $fuseActual, expected $FuseSha256."
}
$runtimeSourceBytes = [IO.File]::ReadAllBytes((Join-Path $Folder $runtimeSourceName))
if ($runtimeSourceBytes.Length -lt 2 -or $runtimeSourceBytes[0] -ne 0x1F -or $runtimeSourceBytes[1] -ne 0x8B) {
    throw "$runtimeSourceName on the release is not a gzip archive."
}

# The source of the other libraries linked into the runtime: fetch-runtime-sources.sh wrote it from the files it pins, and
# each file is checked again here against those pins, without the network.
if ($LibraryFiles -eq '') {
    $fetchScript = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'fetch-runtime-sources.sh'))
    if ($fetchScript -cnotmatch '(?ms)^readonly LIBRARY_FILES=''([^'']*)''\r?$') {
        throw "fetch-runtime-sources.sh does not pin the files of $librariesSourceName."
    }
    $LibraryFiles = $Matches[1]
}
$librariesPrefix = 'type2-runtime-20251108-libraries-source/'
$pinned = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::Ordinal)
foreach ($line in $LibraryFiles.Replace("`r", '').Split("`n")) {
    if ($line -eq '') { continue }
    if ($line -cnotmatch '\A([0-9a-f]{64})  (\S+)\z') {
        throw "A line of the files of $librariesSourceName is not '<sha256>  <path>': $line"
    }
    $pinned[$librariesPrefix + $Matches[2]] = $Matches[1]
}
[string[]]$librariesExpected = @($pinned.Keys) + @($librariesPrefix + 'README.txt')
[Array]::Sort($librariesExpected, [StringComparer]::Ordinal)
$librariesBytes = [IO.File]::ReadAllBytes((Join-Path $Folder $librariesSourceName))
if ($librariesBytes.Length -lt 2 -or $librariesBytes[0] -ne 0x1F -or $librariesBytes[1] -ne 0x8B) {
    throw "$librariesSourceName on the release is not a gzip archive."
}
$tar = New-Object IO.MemoryStream
$gzip = New-Object IO.Compression.GZipStream((New-Object IO.MemoryStream(, $librariesBytes)), [IO.Compression.CompressionMode]::Decompress)
try {
    $gzip.CopyTo($tar)
}
catch {
    throw "$librariesSourceName on the release is not a whole gzip archive: $($_.Exception.Message)"
}
finally {
    $gzip.Dispose()
}
$tarBytes = $tar.ToArray()
$held = New-Object 'System.Collections.Generic.List[string]'
$sha256 = [Security.Cryptography.SHA256]::Create()
try {
    $offset = 0
    while ($true) {
        if ($offset + 512 -gt $tarBytes.Length) {
            throw "$librariesSourceName on the release ends inside a tar header."
        }
        if (@($tarBytes[$offset..($offset + 511)] | Where-Object { $_ -ne 0 }).Count -eq 0) {
            break
        }
        $name = [Text.Encoding]::ASCII.GetString($tarBytes, $offset, 100).TrimEnd([char]0)
        $namePrefix = [Text.Encoding]::ASCII.GetString($tarBytes, $offset + 345, 155).TrimEnd([char]0)
        if ($namePrefix -ne '') {
            $name = "$namePrefix/$name"
        }
        $sizeText = [Text.Encoding]::ASCII.GetString($tarBytes, $offset + 124, 12).Trim([char]0, ' ')
        $size = [Convert]::ToInt64($sizeText, 8)
        $type = [char]$tarBytes[$offset + 156]
        if ($type -ne '0' -and $type -ne [char]0) {
            throw "$librariesSourceName on the release holds $name, which is not a plain file."
        }
        if ($offset + 512 + $size -gt $tarBytes.Length) {
            throw "$librariesSourceName on the release ends inside $name."
        }
        if ($pinned.ContainsKey($name)) {
            $actual = [BitConverter]::ToString($sha256.ComputeHash($tarBytes, $offset + 512, [int]$size)).Replace('-', '').ToLowerInvariant()
            if ($actual -cne $pinned[$name]) {
                throw "$librariesSourceName on the release holds $name with the SHA-256 $actual, expected $($pinned[$name])."
            }
        }
        $held.Add($name)
        $offset += 512 + [int]([Math]::Ceiling($size / 512.0) * 512)
    }
}
finally {
    $sha256.Dispose()
}
if (($held -join ', ') -cne ($librariesExpected -join ', ')) {
    throw "$librariesSourceName on the release holds $($held -join ', '), expected $($librariesExpected -join ', ')."
}

# SHA256SUMS: every other file, once, with its hash, in the form sha256sum --check reads.
$sumsBytes = [IO.File]::ReadAllBytes((Join-Path $Folder $sumsName))
if ($sumsBytes -contains 13) {
    throw "$sumsName on the release has a carriage return."
}
$listed = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::Ordinal)
foreach ($line in [Text.Encoding]::UTF8.GetString($sumsBytes).TrimEnd("`n").Split("`n")) {
    if ($line -cnotmatch '\A([0-9a-f]{64})  (\S+)\z') {
        throw "A line of $sumsName on the release is not '<sha256>  <name>': $line"
    }
    if ($listed.ContainsKey($Matches[2])) {
        throw "$sumsName on the release lists $($Matches[2]) twice."
    }
    $listed[$Matches[2]] = $Matches[1]
}
[string[]]$listedNames = @($listed.Keys)
[Array]::Sort($listedNames, [StringComparer]::Ordinal)
[string[]]$summed = @($expectedNames | Where-Object { $_ -cne $sumsName })
if (($listedNames -join ', ') -cne ($summed -join ', ')) {
    throw "$sumsName on the release lists $($listedNames -join ', '), expected $($summed -join ', ')."
}
foreach ($name in $summed) {
    $actual = Get-Sha256 (Join-Path $Folder $name)
    if ($actual -cne $listed[$name]) {
        throw "$name on the release has the SHA-256 $actual, and $sumsName gives $($listed[$name])."
    }
}

# What GitHub serves is what was sent, byte for byte.
if ($Expected -ne '') {
    $sentNames = Get-SortedNames $Expected
    if (($sentNames -join ', ') -cne ($expectedNames -join ', ')) {
        throw "The files sent were $($sentNames -join ', '), expected $($expectedNames -join ', ')."
    }
    foreach ($name in $expectedNames) {
        $served = Get-Item -LiteralPath (Join-Path $Folder $name)
        $sent = Get-Item -LiteralPath (Join-Path $Expected $name)
        if ($served.Length -ne $sent.Length) {
            throw "$name on the release has $($served.Length) bytes, and the file sent $($sent.Length)."
        }
        $servedSha256 = Get-Sha256 $served.FullName
        $sentSha256 = Get-Sha256 $sent.FullName
        if ($servedSha256 -cne $sentSha256) {
            throw "$name on the release has the SHA-256 $servedSha256, and the file sent $sentSha256."
        }
    }
}

Write-Output "The release holds $($expectedNames -join ', '), and they match: Setup of $($setup.Length) bytes, SHA-256 $setupSha256."
