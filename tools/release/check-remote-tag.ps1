<#
.SYNOPSIS
    Fails unless the tag on the remote still points at the commit that was built.
.DESCRIPTION
    A run builds the commit of its own event, even when it is rerun after the tag moved to a fix: actions/checkout fetches that
    commit and makes the local tag follow it, so check-tag.ps1 never sees the move. The remote is asked instead, just before
    publishing, so that a Setup built before the fix never goes out under the moved tag. An annotated tag counts by its commit.
.PARAMETER Tag
    The tag, vX.Y.Z.
.PARAMETER Commit
    The full hash of the commit that was built.
.PARAMETER Remote
    The remote to ask. By default origin.
.EXAMPLE
    ./tools/release/check-remote-tag.ps1 -Tag v0.1.0 -Commit (git rev-parse HEAD)
#>
param(
    [Parameter(Mandatory = $true)][string]$Tag,
    [Parameter(Mandatory = $true)][string]$Commit,
    [string]$Remote = 'origin'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# The second name is the commit of an annotated tag: ls-remote lists it as <tag>^{}, right after the tag object.
$lines = @(& git ls-remote $Remote "refs/tags/$Tag" "refs/tags/$Tag^{}")
if ($LASTEXITCODE -ne 0) {
    throw "git ls-remote failed with exit code ${LASTEXITCODE}: $Tag cannot be read on $Remote."
}

$found = @{}
foreach ($line in $lines) {
    $hash, $name = "$line" -split '\s+', 2
    $found[$name] = $hash
}

$remoteCommit = $null
if ($found.ContainsKey("refs/tags/$Tag^{}")) {
    $remoteCommit = $found["refs/tags/$Tag^{}"]
}
elseif ($found.ContainsKey("refs/tags/$Tag")) {
    $remoteCommit = $found["refs/tags/$Tag"]
}

if ($null -eq $remoteCommit) {
    throw "$Tag is not on $Remote any more. Nothing is published."
}
if ($remoteCommit -cne $Commit) {
    throw "$Tag now points at $remoteCommit on $Remote, and this run built $Commit. Nothing is published: the run of the tag as it is now publishes it."
}
Write-Output "$Tag still points at $Commit on $Remote."
