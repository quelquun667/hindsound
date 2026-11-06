<#
.SYNOPSIS
    Writes the update manifest of a release from its Setup file.
.DESCRIPTION
    The app reads it from releases/latest/download/hindsound-update-win-x64.json. The size and the SHA-256 only
    detect a damaged download: the manifest is not signed.
.PARAMETER Version
    The version, X.Y.Z. The file is expected on the release of the tag vX.Y.Z.
.PARAMETER SetupPath
    The Setup file. Its name becomes the end of the download URL.
.PARAMETER Repository
    owner/name on GitHub.
.PARAMETER OutFile
    The manifest to write, in UTF-8 without a BOM.
.EXAMPLE
    ./tools/release/write-manifest.ps1 -Version 0.1.0 -SetupPath out/Hindsound-0.1.0-Setup.exe -Repository quelquun667/hindsound -OutFile out/hindsound-update-win-x64.json
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$SetupPath,
    [Parameter(Mandatory = $true)][string]$Repository,
    [Parameter(Mandatory = $true)][string]$OutFile
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$setup = Get-Item -LiteralPath $SetupPath

$manifest = [ordered]@{
    product   = 'Hindsound'
    version   = $Version
    url       = "https://github.com/$Repository/releases/download/v$Version/$($setup.Name)"
    sha256    = (Get-FileHash -Algorithm SHA256 -LiteralPath $setup.FullName).Hash.ToLowerInvariant()
    size      = $setup.Length
    published = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ', [Globalization.CultureInfo]::InvariantCulture)
}

$json = ($manifest | ConvertTo-Json) + "`n"
[IO.File]::WriteAllText($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutFile), $json, (New-Object Text.UTF8Encoding $false))
