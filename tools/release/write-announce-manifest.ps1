<#
.SYNOPSIS
    Writes the manifest of a copy that does not install its updates: the Linux app, or the portable zip for Windows.
.DESCRIPTION
    Such a copy reads hindsound-update-linux-x64.json or hindsound-update-win-x64-portable.json from releases/latest/download,
    tells the user that a version is out, and opens the page of its release on a click. Nothing is downloaded, so the
    manifest names that page, exactly https://github.com/<owner/name>/releases/tag/v<version>, and carries no hash nor size:
    the app refuses any other address for such a manifest.
.PARAMETER Version
    The version, X.Y.Z.
.PARAMETER Repository
    owner/name on GitHub.
.PARAMETER OutFile
    The manifest to write, in UTF-8 without a BOM.
.EXAMPLE
    ./tools/release/write-announce-manifest.ps1 -Version 0.1.0 -Repository quelquun667/hindsound -OutFile out/hindsound-update-linux-x64.json
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$Repository,
    [Parameter(Mandatory = $true)][string]$OutFile
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($Version -cnotmatch '\A[0-9]+\.[0-9]+\.[0-9]+\z') {
    throw "Not a version: $Version."
}
if ($Repository -cnotmatch '\A[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\z') {
    throw "Not owner/name: $Repository."
}

$manifest = [ordered]@{
    product   = 'Hindsound'
    version   = $Version
    url       = "https://github.com/$Repository/releases/tag/v$Version"
    published = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ', [Globalization.CultureInfo]::InvariantCulture)
}

$json = ($manifest | ConvertTo-Json) + "`n"
[IO.File]::WriteAllText($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutFile), $json, (New-Object Text.UTF8Encoding $false))
