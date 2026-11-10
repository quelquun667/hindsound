<#
.SYNOPSIS
    Gathers the files of a release from what the Windows and Linux jobs built, writes the two announce manifests and one
    SHA256SUMS for all of them.
.DESCRIPTION
    The Windows folder must hold exactly the Setup, the portable zip, hindsound-update-win-x64.json and lame-3.100.tar.gz.
    The Linux folder must hold exactly the AppImage, the tar.gz, their SHA256SUMS (which must match them) and
    lame-3.100.tar.gz, the same bytes as the Windows one. The output folder then holds the files of the release, and only
    them: those six, hindsound-update-win-x64-portable.json, hindsound-update-linux-x64.json and SHA256SUMS.
    check-release-files.ps1 checks the result.
.PARAMETER Version
    The version, X.Y.Z.
.PARAMETER Repository
    owner/name on GitHub.
.PARAMETER WindowsFolder
    The files of the Windows job.
.PARAMETER LinuxFolder
    The files of the Linux job.
.PARAMETER OutFolder
    Where the files of the release go. It must not exist, or be empty.
.EXAMPLE
    ./tools/release/assemble-release.ps1 -Version 0.1.0 -Repository quelquun667/hindsound -WindowsFolder windows -LinuxFolder linux -OutFolder release
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$Repository,
    [Parameter(Mandatory = $true)][string]$WindowsFolder,
    [Parameter(Mandatory = $true)][string]$LinuxFolder,
    [Parameter(Mandatory = $true)][string]$OutFolder
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-Sha256([string]$Path) {
    (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
}

function Get-FullPath([string]$Path) {
    $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

function Assert-Holds([string]$Folder, [string]$Label, [string[]]$Expected) {
    [string[]]$names = @(Get-ChildItem -LiteralPath $Folder -Force | ForEach-Object Name)
    [Array]::Sort($names, [StringComparer]::Ordinal)
    [Array]::Sort($Expected, [StringComparer]::Ordinal)
    if (($names -join ', ') -cne ($Expected -join ', ')) {
        throw "The $Label files are $($names -join ', '), expected $($Expected -join ', ')."
    }
}

if ($Version -cnotmatch '\A[0-9]+\.[0-9]+\.[0-9]+\z') {
    throw "Not a version: $Version."
}

$windows = Get-FullPath $WindowsFolder
$linux = Get-FullPath $LinuxFolder
$out = Get-FullPath $OutFolder

$lameName = 'lame-3.100.tar.gz'
$windowsFiles = @("Hindsound-$Version-Setup.exe", "Hindsound-$Version-win-x64-portable.zip", 'hindsound-update-win-x64.json', $lameName)
$packages = @("hindsound-$Version-linux-x64.AppImage", "hindsound-$Version-linux-x64.tar.gz")
Assert-Holds $windows 'Windows' $windowsFiles
Assert-Holds $linux 'Linux' ($packages + @('SHA256SUMS', $lameName))

# The checksums of build-packages.sh: what the Linux job checked is what arrived here.
$listed = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::Ordinal)
foreach ($line in [IO.File]::ReadAllText((Join-Path $linux 'SHA256SUMS')).Split("`n")) {
    if ($line -eq '') { continue }
    if ($line -cnotmatch '\A([0-9a-f]{64})  (\S+)\z') {
        throw "A line of the Linux SHA256SUMS is not '<sha256>  <name>': $line"
    }
    if ($listed.ContainsKey($Matches[2])) {
        throw "The Linux SHA256SUMS lists $($Matches[2]) twice."
    }
    $listed[$Matches[2]] = $Matches[1]
}
foreach ($package in $packages) {
    if (-not $listed.ContainsKey($package)) {
        throw "The Linux SHA256SUMS does not list $package."
    }
    $actual = Get-Sha256 (Join-Path $linux $package)
    if ($actual -cne $listed[$package]) {
        throw "$package has the SHA-256 $actual, and the Linux SHA256SUMS gives $($listed[$package])."
    }
}
if ($listed.Count -ne $packages.Count) {
    throw "The Linux SHA256SUMS lists $($listed.Count) files, expected $($packages.Count)."
}

# Both jobs downloaded and verified the LAME source: the release carries one copy, which must be both.
$windowsLame = Get-Sha256 (Join-Path $windows $lameName)
$linuxLame = Get-Sha256 (Join-Path $linux $lameName)
if ($windowsLame -cne $linuxLame) {
    throw "$lameName differs between the jobs: $windowsLame on Windows, $linuxLame on Linux."
}

if (Test-Path -LiteralPath $out) {
    if (@(Get-ChildItem -LiteralPath $out -Force).Count -ne 0) {
        throw "$out is not empty: the files of a release are gathered in an empty folder."
    }
}
else {
    New-Item -ItemType Directory -Path $out | Out-Null
}

foreach ($name in $windowsFiles) {
    Copy-Item -LiteralPath (Join-Path $windows $name) -Destination (Join-Path $out $name)
}
foreach ($name in $packages) {
    Copy-Item -LiteralPath (Join-Path $linux $name) -Destination (Join-Path $out $name)
}

$writeAnnounce = Join-Path $PSScriptRoot 'write-announce-manifest.ps1'
foreach ($manifest in 'hindsound-update-win-x64-portable.json', 'hindsound-update-linux-x64.json') {
    & $writeAnnounce -Version $Version -Repository $Repository -OutFile (Join-Path $out $manifest)
}

# One line per file, "<sha256>  <name>", as sha256sum writes it and sha256sum --check reads it, in ordinal order.
[string[]]$names = @(Get-ChildItem -LiteralPath $out -File | ForEach-Object Name)
[Array]::Sort($names, [StringComparer]::Ordinal)
$sums = ($names | ForEach-Object { "$(Get-Sha256 (Join-Path $out $_))  $_`n" }) -join ''
[IO.File]::WriteAllText((Join-Path $out 'SHA256SUMS'), $sums, (New-Object Text.UTF8Encoding $false))

Write-Output "assemble-release: $($names.Count + 1) files in $out."
