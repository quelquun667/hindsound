<#
.SYNOPSIS
    Downloads the Inno Setup compiler from the Tools.InnoSetup NuGet package, and prints the path of ISCC.exe.
.DESCRIPTION
    The version and the SHA-256 are pinned here. Dependabot does not see them: update both by hand.
.PARAMETER Destination
    A folder for the package. Created if needed.
.EXAMPLE
    $iscc = ./tools/release/get-iscc.ps1 -Destination inno
#>
param(
    [Parameter(Mandatory = $true)][string]$Destination
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$packageVersion = '7.1.0'
$packageSha256 = 'aad15c662593c6f5656457a5c001f4591babd72820cacf63f9706d4af5d93b75'

New-Item -ItemType Directory -Force -Path $Destination | Out-Null
# Expand-Archive of Windows PowerShell only opens a .zip name.
$package = Join-Path $Destination "tools.innosetup.$packageVersion.zip"
$url = "https://api.nuget.org/v3-flatcontainer/tools.innosetup/$packageVersion/tools.innosetup.$packageVersion.nupkg"

# Three tries: on 2026-11-07 this address cut one connection short, with "unexpected EOF", and the next one worked.
for ($try = 1; $try -le 3; $try++) {
    try {
        Invoke-WebRequest -UseBasicParsing -TimeoutSec 180 -Uri $url -OutFile $package
        break
    }
    catch {
        if ($try -eq 3) { throw }
        Write-Warning "Download of Tools.InnoSetup failed, try $try of 3: $($_.Exception.Message)"
        Start-Sleep -Seconds 10
    }
}

$actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $package).Hash.ToLowerInvariant()
if ($actual -ne $packageSha256) {
    throw "Tools.InnoSetup $packageVersion has the SHA-256 $actual, expected $packageSha256."
}

$content = Join-Path $Destination 'package'
Expand-Archive -LiteralPath $package -DestinationPath $content -Force

(Get-Item -LiteralPath (Join-Path $content 'tools\ISCC.exe')).FullName
