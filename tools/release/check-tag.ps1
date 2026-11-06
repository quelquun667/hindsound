<#
.SYNOPSIS
    Refuses a release tag that is not exactly vX.Y.Z, and prints X.Y.Z.
.PARAMETER Tag
    The tag name, for example v0.1.0.
.PARAMETER MainRef
    When given, the tag must also point at a commit reachable from this ref. Run from the repository.
.EXAMPLE
    ./tools/release/check-tag.ps1 -Tag v0.1.0 -MainRef origin/main
#>
param(
    [Parameter(Mandatory = $true)][string]$Tag,
    [string]$MainRef
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# -cmatch: a capital V is not the tag format. [0-9]: \d also accepts the digits of other scripts.
# \z: $ also accepts a final line break. No leading zero, as in SemVer.
if ($Tag -cnotmatch '\Av((0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*))\z') {
    throw "Tag '$Tag' is not in the form vX.Y.Z."
}
$version = $Matches[1]

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

$version
