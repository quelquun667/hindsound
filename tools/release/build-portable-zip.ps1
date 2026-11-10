<#
.SYNOPSIS
    Makes the portable zip of Hindsound for Windows from the folder the Setup is built from.
.DESCRIPTION
    The zip holds one folder, named like the zip, with the files the Setup installs (Hindsound.exe, libmp3lame.64.dll,
    LICENSE, THIRD-PARTY-NOTICES.md, licenses\*.txt), plus portable.txt and README.txt from packaging\windows.
    portable.txt next to Hindsound.exe is what makes a copy portable: the Setup never installs it.
    Every entry gets the date of the commit, not that of the build. Once written, the
    zip is read back, and each entry must have the bytes of its source.
.PARAMETER PublishDir
    The published app, with the compiled libmp3lame.64.dll already in place: the folder ISCC takes.
.PARAMETER Version
    The version, X.Y.Z.
.PARAMETER OutFile
    The zip to write. Its name must be Hindsound-<version>-win-x64-portable.zip.
.PARAMETER RepoRoot
    The repository, for LICENSE, THIRD-PARTY-NOTICES.md, licenses and packaging\windows.
.PARAMETER Timestamp
    The date of every entry. By default the date of the commit of RepoRoot.
.EXAMPLE
    ./tools/release/build-portable-zip.ps1 -PublishDir publish -Version 0.1.0 -OutFile out/Hindsound-0.1.0-win-x64-portable.zip
#>
param(
    [Parameter(Mandatory = $true)][string]$PublishDir,
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$OutFile,
    [string]$RepoRoot = (Join-Path $PSScriptRoot '..\..'),
    [Nullable[DateTimeOffset]]$Timestamp = $null
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression

function Get-Sha256OfStream([IO.Stream]$Stream) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        ([BitConverter]::ToString($sha.ComputeHash($Stream)) -replace '-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function Get-Sha256([string]$Path) {
    (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
}

function Get-FullPath([string]$Path) {
    $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

if ($Version -cnotmatch '\A[0-9]+\.[0-9]+\.[0-9]+\z') {
    throw "Not a version: $Version."
}
$top = "Hindsound-$Version-win-x64-portable"
$out = Get-FullPath $OutFile
if ([IO.Path]::GetFileName($out) -cne "$top.zip") {
    throw "The zip of $Version must be named $top.zip, not $([IO.Path]::GetFileName($out))."
}

$publish = Get-FullPath $PublishDir
$repo = Get-FullPath $RepoRoot
$packaging = Join-Path $repo 'packaging\windows'

# Entry name (under the top folder) -> source file. All but portable.txt and README.txt are what installer\Hindsound.iss installs.
$sources = [ordered]@{
    'Hindsound.exe'          = Join-Path $publish 'Hindsound.exe'
    'libmp3lame.64.dll'      = Join-Path $publish 'libmp3lame.64.dll'
    'LICENSE'                = Join-Path $repo 'LICENSE'
    'THIRD-PARTY-NOTICES.md' = Join-Path $repo 'THIRD-PARTY-NOTICES.md'
    'portable.txt'           = Join-Path $packaging 'portable.txt'
    'README.txt'             = Join-Path $packaging 'README.txt'
}
$licences = @(Get-ChildItem -LiteralPath (Join-Path $repo 'licenses') -Filter '*.txt' -File -ErrorAction SilentlyContinue)
if ($licences.Count -eq 0) {
    throw "The repository has no licenses\*.txt, under ${repo}: a zip never ships without its licences."
}
foreach ($licence in $licences) {
    $sources["licenses/$($licence.Name)"] = $licence.FullName
}
foreach ($entry in $sources.Keys) {
    if (-not (Test-Path -LiteralPath $sources[$entry] -PathType Leaf)) {
        throw "Missing for the zip: $($sources[$entry])."
    }
}

if ($null -eq $Timestamp) {
    $commitDate = & git -C $repo log -1 --format=%cI
    if ($LASTEXITCODE -ne 0 -or -not $commitDate) {
        throw "git log failed in ${repo}: give -Timestamp."
    }
    $Timestamp = [DateTimeOffset]::Parse("$commitDate", [Globalization.CultureInfo]::InvariantCulture)
}

[string[]]$names = @($sources.Keys | ForEach-Object { "$top/$_" })
[Array]::Sort($names, [StringComparer]::Ordinal)
$outFolder = Split-Path -Parent $out
New-Item -ItemType Directory -Path $outFolder -Force | Out-Null
# Written beside, then moved: a failure never leaves a zip with that name.
$partial = "$out.partial"
Remove-Item -LiteralPath $partial -Force -ErrorAction SilentlyContinue

try {
    $stream = [IO.File]::Open($partial, [IO.FileMode]::CreateNew)
    try {
        $zip = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($name in $names) {
                $entry = $zip.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = [DateTimeOffset]$Timestamp
                $target = $entry.Open()
                try {
                    $source = [IO.File]::OpenRead($sources[$name.Substring($top.Length + 1)])
                    try { $source.CopyTo($target) } finally { $source.Dispose() }
                }
                finally {
                    $target.Dispose()
                }
            }
        }
        finally {
            $zip.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }

    # Read back: exactly these entries, each with the bytes of its source.
    $stream = [IO.File]::OpenRead($partial)
    try {
        $zip = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Read)
        try {
            $written = @($zip.Entries | ForEach-Object FullName)
            if (($written -join '|') -cne ($names -join '|')) {
                throw "The zip holds $($written -join ', '), expected $($names -join ', ')."
            }
            foreach ($entry in $zip.Entries) {
                $read = $entry.Open()
                try { $actual = Get-Sha256OfStream $read } finally { $read.Dispose() }
                $expected = Get-Sha256 $sources[$entry.FullName.Substring($top.Length + 1)]
                if ($actual -cne $expected) {
                    throw "$($entry.FullName) in the zip has the SHA-256 $actual, its source $expected."
                }
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
catch {
    Remove-Item -LiteralPath $partial -Force -ErrorAction SilentlyContinue
    throw
}

Move-Item -LiteralPath $partial -Destination $out -Force
Write-Output "build-portable-zip: $out, $($names.Count) files under $top/."
