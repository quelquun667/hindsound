<#
.SYNOPSIS
    Checks the files of a draft release as GitHub serves them, downloaded back into a folder, before it goes public.
.DESCRIPTION
    An immutable release takes no change once published, so a file damaged on its way up, or one left by an earlier run,
    would reach the players. The folder must hold exactly the Setup, the manifest and the LAME source. The manifest must give
    this version, the address of this Setup on the release of the tag, and the size and SHA-256 of the Setup in the folder.
    The LAME source must have the SHA-256 given in THIRD-PARTY-NOTICES.md.
.PARAMETER Folder
    The files of the release, as gh release download wrote them.
.PARAMETER Version
    The version, X.Y.Z.
.PARAMETER Repository
    owner/name on GitHub.
.PARAMETER LameSha256
    For test-release-scripts.ps1 only, with a fake archive. A workflow never passes it.
.EXAMPLE
    ./tools/release/check-release-files.ps1 -Folder verify -Version 0.1.0 -Repository quelquun667/hindsound
#>
param(
    [Parameter(Mandatory = $true)][string]$Folder,
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$Repository,
    [string]$LameSha256 = 'ddfe36cab873794038ae2c1210557ad34857a4b6bdc515785d1da9e175b1da1e'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-Sha256([string]$Path) {
    (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
}

$setupName = "Hindsound-$Version-Setup.exe"
$manifestName = 'hindsound-update-win-x64.json'
$lameName = 'lame-3.100.tar.gz'

$expected = @($setupName, $manifestName, $lameName | Sort-Object)
$names = @(Get-ChildItem -LiteralPath $Folder -File | ForEach-Object Name | Sort-Object)
if (($names -join ', ') -cne ($expected -join ', ')) {
    throw "The release holds $($names -join ', '), expected $($expected -join ', ')."
}

$setup = Get-Item -LiteralPath (Join-Path $Folder $setupName)
$setupSha256 = Get-Sha256 $setup.FullName
$manifest = [IO.File]::ReadAllText((Join-Path $Folder $manifestName)) | ConvertFrom-Json
$url = "https://github.com/$Repository/releases/download/v$Version/$setupName"
if ("$($manifest.version)" -cne $Version) {
    throw "The manifest on the release gives the version $($manifest.version), expected $Version."
}
if ("$($manifest.url)" -cne $url) {
    throw "The manifest on the release gives the address $($manifest.url), expected $url."
}
if ("$($manifest.size)" -cne "$($setup.Length)") {
    throw "The manifest on the release gives the size $($manifest.size), and the Setup there has $($setup.Length) bytes."
}
if ("$($manifest.sha256)" -cne $setupSha256) {
    throw "The manifest on the release gives the SHA-256 $($manifest.sha256), and the Setup there has $setupSha256."
}
$lameActual = Get-Sha256 (Join-Path $Folder $lameName)
if ($lameActual -cne $LameSha256) {
    throw "$lameName on the release has the SHA-256 $lameActual, expected $LameSha256."
}
Write-Output "The release holds $($expected -join ', '), and they match: Setup of $($setup.Length) bytes, SHA-256 $setupSha256."