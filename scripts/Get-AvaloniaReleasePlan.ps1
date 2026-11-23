param([switch]$RehearseRelease)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$versionFile = Join-Path (Split-Path -Parent $PSScriptRoot) 'Directory.Build.props'
$version = ([xml](Get-Content -LiteralPath $versionFile -Raw)).Project.PropertyGroup.Version
if ($version -isnot [string] -or $version -cnotmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
    throw 'Directory.Build.props must supply a single stable version.'
}
$commit = & git rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve the checked-out source commit.' }

$taggedRelease = $env:GITHUB_EVENT_NAME -eq 'push' -and $env:GITHUB_REF -clike 'refs/tags/*'
$rehearsal = $env:GITHUB_EVENT_NAME -eq 'workflow_dispatch' -and $RehearseRelease
$tag = "v$version"
if (($taggedRelease -or $rehearsal) -and [version]$version -le [version]'1.2.2') {
    throw 'The signed release candidate must be newer than the public 1.2.2 baseline.'
}
if ($taggedRelease) {
    if ($env:GITHUB_REF -cne "refs/tags/$tag") { throw 'The pushed tag must match Directory.Build.props.' }
    $tagCommit = & git rev-parse "refs/tags/$tag^{commit}"
    if ($LASTEXITCODE -ne 0 -or $tagCommit -cne $commit) { throw 'The tag does not identify the checked-out commit.' }
    & git merge-base --is-ancestor $commit refs/remotes/origin/main
    if ($LASTEXITCODE -ne 0) { throw 'Release tags must point to a commit on main.' }
    & (Join-Path $PSScriptRoot 'Get-ReleaseNotes.ps1') -Version $version | Out-Null
}

$plan = [ordered]@{
    commit = $commit
    version = $version
    tag = $tag
    package_identity = $(if ($taggedRelease -or $rehearsal) { 'release' } else { 'preview' })
}
if ($env:GITHUB_OUTPUT) {
    $plan.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" } | Add-Content -LiteralPath $env:GITHUB_OUTPUT
}
$plan | ConvertTo-Json
