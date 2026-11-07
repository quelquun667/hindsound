<#
.SYNOPSIS
    Installs a Setup in a temporary folder of a GitHub runner, checks what it put there, starts the exe, and uninstalls.
.DESCRIPTION
    A Setup touches the Run key, the Uninstall key and the log of the machine that runs it: this refuses to run anywhere but
    on a GitHub-hosted runner. With -CheckFolder, it only verifies a folder, and installs nothing. That part runs anywhere.
.PARAMETER Version
    The version of the tag, X.Y.Z. The exe must have the file version X.Y.Z.0.
.PARAMETER DllPath
    The compiled DLL: the installed one must have its hash.
.PARAMETER SetupPath
    The Setup to install.
.PARAMETER WorkDir
    A new folder for the installation, the logs and the files that the exe writes.
.PARAMETER RepoRoot
    The repository, for LICENSE, THIRD-PARTY-NOTICES.md and licenses\*.txt.
.PARAMETER CheckFolder
    An installed folder to verify. Nothing is installed.
.EXAMPLE
    ./tools/release/check-setup.ps1 -SetupPath out/Hindsound-0.1.0-Setup.exe -Version 0.1.0 -DllPath lame/libmp3lame.64.dll -WorkDir check
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$DllPath,
    [string]$SetupPath,
    [string]$WorkDir,
    [string]$RepoRoot,
    [string]$CheckFolder
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $RepoRoot) {
    $RepoRoot = Join-Path $PSScriptRoot '..\..'
}
$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).ProviderPath

function Get-Sha256([string]$Path) {
    (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash
}

function Assert-SameFile([string]$Installed, [string]$Expected) {
    if (-not (Test-Path -LiteralPath $Installed -PathType Leaf)) {
        throw "The installed folder has no $Installed."
    }
    if ((Get-Sha256 $Installed) -cne (Get-Sha256 $Expected)) {
        throw "$Installed differs from $Expected."
    }
}

function Assert-InstalledFolder([string]$Folder) {
    $exe = Join-Path $Folder 'Hindsound.exe'
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
        throw "The installed folder has no $exe."
    }
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
    $fileVersion = '{0}.{1}.{2}.{3}' -f $info.FileMajorPart, $info.FileMinorPart, $info.FileBuildPart, $info.FilePrivatePart
    if ($fileVersion -cne "$Version.0") {
        throw "$exe has the file version $fileVersion, expected $Version.0."
    }
    Write-Output "Exe: file version $fileVersion"

    Assert-SameFile (Join-Path $Folder 'libmp3lame.64.dll') $DllPath
    Write-Output "DLL: the compiled one, SHA-256 $(Get-Sha256 $DllPath)"

    foreach ($name in 'LICENSE', 'THIRD-PARTY-NOTICES.md') {
        Assert-SameFile (Join-Path $Folder $name) (Join-Path $RepoRoot $name)
    }

    $expectedLicences = @(Get-ChildItem -LiteralPath (Join-Path $RepoRoot 'licenses') -Filter '*.txt' -File)
    if ($expectedLicences.Count -eq 0) {
        throw "The repository has no licenses\*.txt, under $RepoRoot."
    }
    foreach ($licence in $expectedLicences) {
        Assert-SameFile (Join-Path $Folder "licenses\$($licence.Name)") $licence.FullName
    }
    Write-Output "Licences: LICENSE, THIRD-PARTY-NOTICES.md and $($expectedLicences.Count) in licenses, as in the repository."
}

if ($CheckFolder) {
    Assert-InstalledFolder $CheckFolder
    Write-Output "$CheckFolder passed."
    return
}

# Before anything else: this part must never run on a working PC.
if ($env:GITHUB_ACTIONS -cne 'true' -or $env:RUNNER_ENVIRONMENT -cne 'github-hosted') {
    throw 'check-setup.ps1 installs and uninstalls: it runs on a GitHub-hosted runner only. Use -CheckFolder to verify a folder.'
}
if (-not $SetupPath -or -not $WorkDir) {
    throw 'Give -SetupPath and -WorkDir.'
}

function Invoke-Program([string]$Name, [string]$Path, [string]$Arguments, [int]$TimeoutSeconds) {
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $Path
    $start.Arguments = $Arguments
    $start.UseShellExecute = $false
    Write-Output "$Name`: $Path $Arguments"
    $process = [Diagnostics.Process]::Start($start)
    try {
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill()
            throw "$Name did not finish in $TimeoutSeconds s."
        }
        if ($process.ExitCode -ne 0) {
            throw "$Name ended with exit code $($process.ExitCode)."
        }
    }
    finally {
        $process.Dispose()
    }
}

function Show-File([string]$Path) {
    if (Test-Path -LiteralPath $Path) {
        Write-Output "--- $Path ---"
        Get-Content -LiteralPath $Path | ForEach-Object { Write-Output $_ }
    }
}

# GetFullPath as well: the workflow joins with a forward slash, and /DIR= of Setup wants a Windows path.
$WorkDir = [IO.Path]::GetFullPath($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($WorkDir))
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
$install = Join-Path $WorkDir 'install'
$setupLog = Join-Path $WorkDir 'setup.log'
$uninstallLog = Join-Path $WorkDir 'uninstall.log'
$settings = Join-Path $WorkDir 'settings.json'
$dump = Join-Path $WorkDir 'dump'
$clips = Join-Path $WorkDir 'clips'

# The same safe settings as on a working PC: the mic stays closed, nothing reaches the clipboard, the Run key stays alone.
$safeSettings = @{
    audio   = @{ micDeviceId = 'disabled' }
    export  = @{ outputFolder = $clips; copyToClipboard = $false }
    general = @{ startWithWindows = $false; confirmSound = 'never' }
}
[IO.File]::WriteAllText($settings, ($safeSettings | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))

try {
    Invoke-Program 'Setup' (Resolve-Path -LiteralPath $SetupPath).ProviderPath "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR=`"$install`" /LOG=`"$setupLog`"" 300

    Get-ChildItem -LiteralPath $install -Recurse -File | ForEach-Object { Write-Output ("{0,12} {1}" -f $_.Length, $_.FullName) }
    Assert-InstalledFolder $install

    Invoke-Program 'The installed exe' (Join-Path $install 'Hindsound.exe') "--no-mic --no-clipboard --no-hotkeys --settings `"$settings`" --dump-notifications `"$dump`"" 120

    # The dump is a set of BMP files (App/NotificationDump.cs).
    $drawn = @(Get-ChildItem -LiteralPath $dump -Filter '*.bmp' -File -ErrorAction SilentlyContinue | Where-Object { $_.Length -gt 0 })
    if ($drawn.Count -eq 0) {
        throw "The installed exe wrote no BMP file in $dump."
    }
    Write-Output "The installed exe drew $($drawn.Count) BMP files."

    $uninstaller = Join-Path $install 'unins000.exe'
    if (-not (Test-Path -LiteralPath $uninstaller -PathType Leaf)) {
        throw "The installed folder has no $uninstaller."
    }
    Invoke-Program 'The uninstaller' $uninstaller "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=`"$uninstallLog`"" 300

    # It may finish from a copy of itself in the temporary folder.
    $exe = Join-Path $install 'Hindsound.exe'
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    while ((Test-Path -LiteralPath $exe) -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Seconds 1
    }
    if (Test-Path -LiteralPath $exe) {
        throw "The uninstaller left $exe."
    }
    Write-Output 'The Setup passed: installed, checked, started and uninstalled.'
}
catch {
    Show-File $setupLog
    Show-File $uninstallLog
    Show-File (Join-Path $WorkDir 'settings.log')
    throw
}
