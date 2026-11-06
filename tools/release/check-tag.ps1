<#
.SYNOPSIS
    Refuses a release tag that is not exactly vX.Y.Z, or an unexpected version jump, and prints X.Y.Z.
.DESCRIPTION
    The app remembers the highest version it has read (HighestSeen). A 9.0.0 released by mistake would block everything
    lower, for good, for whoever read it. So, with -CheckVersionJump, against the vX.Y.Z tags already there, this tag aside
    so that a failed run can be run again:
    - no earlier tag: a 0.x version passes, anything else needs the confirmation;
    - a version lower than or equal to the highest: always refused, even confirmed;
    - the next patch, or the next minor with patch 0: passes;
    - any other jump, a new major included: needs the confirmation.
.PARAMETER Tag
    The tag name, for example v0.1.0.
.PARAMETER MainRef
    When given, the tag must also point at a commit reachable from this ref. Run from the repository.
.PARAMETER CheckVersionJump
    Compares the tag with the others, as above. Needs all the tags: a checkout with fetch-depth 0. Run from the repository.
.PARAMETER ConfirmVersion
    The confirmation: exactly this version, X.Y.Z. The workflow passes the repository variable RELEASE_CONFIRM_VERSION.
.EXAMPLE
    ./tools/release/check-tag.ps1 -Tag v0.1.0 -MainRef origin/main -CheckVersionJump -ConfirmVersion $env:RELEASE_CONFIRM_VERSION
#>
param(
    [Parameter(Mandatory = $true)][string]$Tag,
    [string]$MainRef,
    [switch]$CheckVersionJump,
    [string]$ConfirmVersion
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# -cmatch: a capital V is not the tag format. [0-9]: \d also accepts the digits of other scripts.
# \z: $ also accepts a final line break. No leading zero, as in SemVer.
$tagFormat = '\Av((0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*))\z'

function Get-VersionParts([string]$Name) {
    if ($Name -cnotmatch $tagFormat) { return $null }
    # A bigint, so that a tag too long for an int is still compared, not an error on a later release.
    , @([bigint]::Parse($Matches[2]), [bigint]::Parse($Matches[3]), [bigint]::Parse($Matches[4]))
}

function Compare-VersionParts($Left, $Right) {
    for ($i = 0; $i -lt 3; $i++) {
        if ($Left[$i] -lt $Right[$i]) { return -1 }
        if ($Left[$i] -gt $Right[$i]) { return 1 }
    }
    0
}

$parts = Get-VersionParts $Tag
if ($null -eq $parts) {
    throw "Tag '$Tag' is not in the form vX.Y.Z."
}
$version = $Tag.Substring(1)

if ($MainRef) {
    $commit = git rev-parse --verify --quiet "refs/tags/$Tag^{commit}"
    if ($LASTEXITCODE -ne 0) {
        throw "Tag '$Tag' does not exist in this repository."
    }
    git merge-base --is-ancestor $commit $MainRef
    if ($LASTEXITCODE -ne 0) {
        throw "Tag '$Tag' points at $commit, which is not a commit of $MainRef."
    }
}

if ($CheckVersionJump) {
    $names = @(git tag --list)
    if ($LASTEXITCODE -ne 0) {
        throw 'git tag failed: the earlier versions are unknown.'
    }

    $highest = $null
    $highestName = ''
    foreach ($name in $names) {
        if ($name -ceq $Tag) { continue }
        $other = Get-VersionParts $name
        if ($null -eq $other) { continue }
        if ($null -eq $highest -or (Compare-VersionParts $other $highest) -gt 0) {
            $highest = $other
            $highestName = $name
        }
    }

    $confirmationNeeded = $null
    if ($null -eq $highest) {
        if ($parts[0] -ne 0) {
            $confirmationNeeded = "No earlier version tag, and $version is not a 0.x version."
        }
    }
    else {
        if ((Compare-VersionParts $parts $highest) -le 0) {
            throw "Version $version is not above the highest tag, $highestName. The app would never offer it to anyone who read that one."
        }
        $sameMinor = $parts[0] -eq $highest[0] -and $parts[1] -eq $highest[1]
        $isNextPatch = $sameMinor -and $parts[2] -eq ($highest[2] + 1)
        $isNextMinor = $parts[0] -eq $highest[0] -and $parts[1] -eq ($highest[1] + 1) -and $parts[2] -eq 0
        if (-not ($isNextPatch -or $isNextMinor)) {
            $confirmationNeeded = "Version $version is a jump from the highest tag, $highestName."
        }
    }

    # -cne and no trimming: the confirmation is this exact version, not a near one left from an earlier release.
    if ($null -ne $confirmationNeeded -and $ConfirmVersion -cne $version) {
        throw "$confirmationNeeded A release cannot be taken back from those who read it (HighestSeen). If it is wanted, set the repository variable RELEASE_CONFIRM_VERSION to exactly $version. See tools/release/README.md."
    }
}

$version
