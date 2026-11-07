<#
.SYNOPSIS
    Builds the 64-bit libmp3lame DLL from the upstream LAME 3.100 archive, and checks the result.
.DESCRIPTION
    Runs on a machine with the MSVC tools, such as a windows-latest runner. It fails if any step fails:
    1. Downloads lame-3.100.tar.gz into WorkDir, or takes ArchivePath, and verifies its SHA-256 before anything else.
    2. Finds MSVC with vswhere and VsDevCmd.
    3. Extracts the archive and builds the DLL with Makefile.MSVC, the build file of the archive, and no file changed.
       vc_solution/ holds only Visual Studio 2008 projects, which MSBuild no longer reads.
    4. Fails if the build changed a file of the archive: the notices promise an unchanged LAME.
    5. Runs check-lame-dll.ps1: x64, no redistributable needed, the exports NAudio.Lame calls, version 3.100.

    Makefile.MSVC is written for an old Windows SDK, so three of its macros are set on the nmake command line, which wins
    over the file:
    - MSVCVER=Win64           its own 64-bit settings: no NASM, /MT (static CRT).
    - MACHINE=/machine:X64    the file sets /machine:I386 for every build, and the linker refuses x64 objects with it.
    - ADDL_OBJ=kernel32.lib   the file links bufferoverflowU.lib for Win64, which current Visual Studio does not have.
                              kernel32.lib is linked anyway, and nmake needs a value here to replace the file's.
.PARAMETER WorkDir
    An empty or new folder. Receives lame-3.100.tar.gz, the sources in src, and the log in nmake.log.
.PARAMETER OutFile
    Where to copy the DLL once checked, for example libmp3lame.64.dll.
.PARAMETER ArchivePath
    An archive already there, instead of downloading. Its SHA-256 is verified just the same.
.PARAMETER ReferenceDll
    The DLL of the NAudio.Lame package, for check-lame-dll.ps1 to compare the exports with.
.PARAMETER DownloadOnly
    Stops once the archive is verified. No workflow uses it: every release ships the compiled DLL.
.PARAMETER ExpectedSha256
    For test-release-scripts.ps1 only, with a fake archive. A workflow never passes it.
.PARAMETER VsDevCmd
    For test-release-scripts.ps1 only, with a fake compiler. A workflow never passes it.
.PARAMETER Urls
    For test-release-scripts.ps1 only, with file addresses. A workflow never passes it.
.EXAMPLE
    ./tools/release/build-lame.ps1 -WorkDir lame -OutFile lame/libmp3lame.64.dll -ReferenceDll publish/libmp3lame.64.dll
#>
param(
    [Parameter(Mandatory = $true)][string]$WorkDir,
    [Parameter(Mandatory = $true)][string]$OutFile,
    [string]$ArchivePath,
    [string]$ReferenceDll,
    [switch]$DownloadOnly,
    [string]$ExpectedSha256 = 'ddfe36cab873794038ae2c1210557ad34857a4b6bdc515785d1da9e175b1da1e',
    [string]$VsDevCmd,
    [string[]]$Urls = @(
        'https://sourceforge.net/projects/lame/files/lame/3.100/lame-3.100.tar.gz/download'
        'https://downloads.sourceforge.net/project/lame/lame/3.100/lame-3.100.tar.gz'
    )
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-FullPath([string]$Path) {
    $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

function Get-TreeHashes([string]$Root) {
    $hashes = @{}
    foreach ($file in Get-ChildItem -LiteralPath $Root -Recurse -File -Force) {
        $hashes[$file.FullName] = (Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName).Hash
    }
    $hashes
}

$WorkDir = Get-FullPath $WorkDir
$OutFile = Get-FullPath $OutFile
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null

# --- 1. The archive, verified before anything reads it ---
if ($ArchivePath) {
    $archive = Get-FullPath $ArchivePath
}
else {
    $archive = Join-Path $WorkDir 'lame-3.100.tar.gz'
    # curl: for a browser-like user agent, as Invoke-WebRequest sends, SourceForge answers with a web page, not the file.
    # The second address is the same file on the download host: it is tried when the first one fails or gives another file.
    foreach ($url in $Urls) {
        Write-Output "Downloading $url"
        & curl.exe --fail --silent --show-error --location --retry 3 --connect-timeout 30 --max-time 300 --output $archive $url
        if ($LASTEXITCODE -ne 0) {
            Write-Output "curl failed with exit code $LASTEXITCODE."
        }
        elseif ((Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash.ToLowerInvariant() -ceq $ExpectedSha256) {
            break
        }
        else {
            Write-Output 'What came is not the archive: its SHA-256 differs.'
        }
        if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive }
    }
    if (-not (Test-Path -LiteralPath $archive)) {
        throw 'The LAME archive could not be downloaded from any address.'
    }
}

$actualSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash.ToLowerInvariant()
if ($actualSha256 -cne $ExpectedSha256) {
    throw "$archive has the SHA-256 $actualSha256, expected $ExpectedSha256. Nothing was extracted."
}
Write-Output "Archive verified: $archive, SHA-256 $actualSha256"
if ($DownloadOnly) { return }

# --- 2. MSVC ---
if (-not $VsDevCmd) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) {
        throw "vswhere is not at $vswhere."
    }
    $installation = @(& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)
    if ($installation.Count -eq 0 -or -not $installation[0]) {
        throw 'vswhere finds no Visual Studio with the component VC.Tools.x86.x64.'
    }
    Write-Output "Visual Studio: $($installation[0])"
    $VsDevCmd = Join-Path $installation[0] 'Common7\Tools\VsDevCmd.bat'
}
if (-not (Test-Path -LiteralPath $VsDevCmd)) {
    throw "VsDevCmd is not at $VsDevCmd."
}

# --- 3. Extraction ---
$sourceRoot = Join-Path $WorkDir 'src'
if (Test-Path -LiteralPath $sourceRoot) {
    throw "$sourceRoot already exists: give an empty WorkDir."
}
New-Item -ItemType Directory -Path $sourceRoot | Out-Null
# The tar of Windows by its full path: the tar of Git, earlier on a runner's PATH, reads C:\ as a remote host.
& (Join-Path $env:SystemRoot 'System32\tar.exe') -xzf $archive -C $sourceRoot
if ($LASTEXITCODE -ne 0) {
    throw "tar failed with exit code $LASTEXITCODE on $archive."
}
$source = Join-Path $sourceRoot 'lame-3.100'
foreach ($needed in 'Makefile.MSVC', 'configMS.h', 'include\lame.def') {
    if (-not (Test-Path -LiteralPath (Join-Path $source $needed))) {
        Get-ChildItem -LiteralPath $sourceRoot -Force | ForEach-Object { Write-Output $_.FullName }
        throw "The archive has no lame-3.100\$needed."
    }
}
$hashesBefore = Get-TreeHashes $source

# --- 4. The build, through a command file: VsDevCmd only changes the environment of its own cmd ---
# VsDevCmd can return an error and still work, so this asks for the tools themselves. "call": nmake may be a command file in a test.
$nmakeArguments = '/NOLOGO -f Makefile.MSVC MSVCVER=Win64 MACHINE=/machine:X64 ADDL_OBJ=kernel32.lib libmp3lame.dll'
$inner = Join-Path $WorkDir 'build-inner.cmd'
[IO.File]::WriteAllLines($inner, [string[]]@(
        '@echo off'
        "call `"$VsDevCmd`" -arch=x64 -host_arch=x64 -no_logo"
        'where cl || exit /b 101'
        'where link || exit /b 101'
        'where nmake || exit /b 101'
        "cd /d `"$source`" || exit /b 102"
        "echo nmake $nmakeArguments"
        "call nmake $nmakeArguments"
        'exit /b %errorlevel%'
    ))
$log = Join-Path $WorkDir 'nmake.log'
$outer = Join-Path $WorkDir 'build.cmd'
[IO.File]::WriteAllLines($outer, [string[]]@(
        '@echo off'
        "call `"$inner`" > `"$log`" 2>&1"
        'exit /b %errorlevel%'
    ))

& $outer
$buildExit = $LASTEXITCODE

Write-Output '--- nmake.log ---'
if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log | ForEach-Object { Write-Output $_ } }
Write-Output '--- end of nmake.log ---'

$outputFolder = Join-Path $source 'output'
Write-Output "--- $outputFolder ---"
if (Test-Path -LiteralPath $outputFolder) {
    Get-ChildItem -LiteralPath $outputFolder -Force | ForEach-Object { Write-Output ("{0,12} {1}" -f $_.Length, $_.Name) }
}
else {
    Write-Output 'This folder does not exist.'
}

if ($buildExit -eq 101) {
    throw 'cl, link or nmake is not on the PATH after VsDevCmd.'
}
if ($buildExit -ne 0) {
    throw "nmake failed with exit code $buildExit. Its output is above."
}

# --- 5. The result ---
$hashesAfter = Get-TreeHashes $source
$changed = @($hashesBefore.Keys | Where-Object { $hashesAfter[$_] -cne $hashesBefore[$_] })
if ($changed.Count -gt 0) {
    throw "The build changed a source file, which the notices rule out: $($changed -join ', ')."
}

$built = Join-Path $outputFolder 'libmp3lame.dll'
if (-not (Test-Path -LiteralPath $built)) {
    throw "nmake succeeded, but there is no output\libmp3lame.dll."
}

$checkArguments = @{ DllPath = $built }
if ($ReferenceDll) { $checkArguments.ReferenceDll = $ReferenceDll }
& (Join-Path $PSScriptRoot 'check-lame-dll.ps1') @checkArguments

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutFile) | Out-Null
Copy-Item -LiteralPath $built -Destination $OutFile -Force
Write-Output "Built: $OutFile"
