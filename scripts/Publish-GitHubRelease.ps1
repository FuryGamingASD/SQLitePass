param(
    [Parameter(Mandatory)][string]$ArtifactDirectory,
    [ValidatePattern('^[0-9a-f]{40}$')][string]$SourceCommit = $env:GITHUB_SHA,
    [ValidatePattern('^v\d+\.\d+\.\d+$')][string]$ExistingTag
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$provenance=Get-Content (Join-Path $ArtifactDirectory 'release-provenance.json') -Raw | ConvertFrom-Json
if(-not $SourceCommit -or $provenance.SourceCommit -ne $SourceCommit -or $provenance.SourceDirty){throw 'Only the clean verified commit may be published.'}
if($provenance.Version -notmatch '^\d+\.\d+\.\d+$'){throw 'Unexpected version.'}
$tag='v'+$provenance.Version
if($ExistingTag -and $ExistingTag -cne $tag){throw 'The release tag must match the verified version.'}
$notesBody=& (Join-Path $PSScriptRoot 'Get-ReleaseNotes.ps1') -Version $provenance.Version
function Assert-ExistingTag {
    $encodedRef=[Uri]::EscapeDataString("refs/tags/$tag")
    $remoteCommit=& gh api "repos/$env:GITHUB_REPOSITORY/commits/$encodedRef" --jq '.sha'
    if($LASTEXITCODE -ne 0 -or $remoteCommit -cne $SourceCommit){throw 'The remote tag does not resolve to the verified source commit.'}
}
$checksums=@(Get-Content (Join-Path $ArtifactDirectory 'checksums.json') -Raw | ConvertFrom-Json)
foreach($required in @('sqlitepass-update-auth.json','sqlitepass-update-auth.sig')){
    if($required -notin @($checksums | ForEach-Object File)){throw 'The release is missing a signed update manifest; run the protected signing workflow step first.'}
    if(-not (Test-Path -LiteralPath (Join-Path $ArtifactDirectory $required) -PathType Leaf)){throw "Missing signed update asset: $required"}
}
if($ExistingTag){
    $files=@(Get-ChildItem -LiteralPath $ArtifactDirectory -Force | Where-Object Name -cne 'checksums.json' | ForEach-Object Name)
    if($provenance.PackageId -cne 'SQLitePass' -or 'releases.win.json' -cnotin $files -or 'releases.linux.json' -cnotin $files -or
        @(Compare-Object @($checksums | ForEach-Object File) $files -CaseSensitive).Count -ne 0){
        throw 'Publication requires exactly the verified assets for both release channels.'
    }
}
foreach($entry in $checksums){
    if($entry.File -ne [IO.Path]::GetFileName($entry.File)){throw 'Invalid checksum filename.'}
    if((Get-FileHash (Join-Path $ArtifactDirectory $entry.File) -Algorithm SHA256).Hash -ne $entry.Hash){throw 'Downloaded CI artifact checksum mismatch.'}
}
if($ExistingTag){Assert-ExistingTag}
$existingText=& gh release view $tag --repo $env:GITHUB_REPOSITORY --json apiUrl,isDraft,targetCommitish 2>$null
$existing=$null
$alreadyPublished=$false
if($LASTEXITCODE -eq 0){$existing=$existingText | ConvertFrom-Json}
if($existing){
    if(-not $ExistingTag -and $existing.targetCommitish -ne $SourceCommit){throw 'An existing release from a different commit must not be overwritten.'}
    $alreadyPublished=-not $existing.isDraft
} else {
    $notesFile=New-TemporaryFile
    try {
        Set-Content -LiteralPath $notesFile.FullName -Value $notesBody -Encoding utf8
        [string[]]$targetArguments=if($ExistingTag){@('--verify-tag')}else{@('--target',$SourceCommit)}
        & gh release create $tag --repo $env:GITHUB_REPOSITORY @targetArguments --title "SQLitePass $($provenance.Version)" --notes-file $notesFile.FullName --draft
        if($LASTEXITCODE -ne 0){throw 'Draft release creation failed.'}
    } finally { Remove-Item -LiteralPath $notesFile.FullName }
}
$releaseMetadata=& gh release view $tag --repo $env:GITHUB_REPOSITORY --json apiUrl
if($LASTEXITCODE -ne 0){throw 'Cannot find the draft release.'}
$releaseApi=($releaseMetadata | ConvertFrom-Json).apiUrl
$releaseText=& gh api $releaseApi
if($LASTEXITCODE -ne 0){throw 'Cannot read the draft release.'}
$release=$releaseText | ConvertFrom-Json
foreach($file in (Get-ChildItem -LiteralPath $ArtifactDirectory -File)){
    $uploaded=@($release.assets | Where-Object name -eq $file.Name)
    if($uploaded.Count -eq 0){
        if($alreadyPublished){throw 'An existing published release has missing assets; it will not be modified.'}
        & gh release upload $tag $file.FullName --repo $env:GITHUB_REPOSITORY
        if($LASTEXITCODE -ne 0){throw "Asset upload failed: $($file.Name)"}
    }
}
$releaseText=& gh api $releaseApi
if($LASTEXITCODE -ne 0){throw 'Cannot inspect uploaded assets.'}
$release=$releaseText | ConvertFrom-Json
if(@($release.assets).Count -ne @(Get-ChildItem -LiteralPath $ArtifactDirectory -File).Count){throw 'Unexpected remote asset set.'}
foreach($file in (Get-ChildItem -LiteralPath $ArtifactDirectory -File)){
    $uploaded=@($release.assets | Where-Object name -eq $file.Name)
    $expected='sha256:'+((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant())
    if($uploaded.Count -ne 1 -or $uploaded[0].size -ne $file.Length -or $uploaded[0].digest -ne $expected){throw "Remote asset digest mismatch: $($file.Name)"}
}
if(-not $alreadyPublished){
    if($ExistingTag){Assert-ExistingTag}
    & gh release edit $tag --repo $env:GITHUB_REPOSITORY --draft=false --latest
    if($LASTEXITCODE -ne 0){throw 'Release publication failed.'}
}
if($ExistingTag){Assert-ExistingTag}else{
    $refText=& gh api "repos/$env:GITHUB_REPOSITORY/git/ref/tags/$tag"
    if($LASTEXITCODE -ne 0 -or ($refText | ConvertFrom-Json).object.sha -ne $SourceCommit){throw 'Published tag does not match the verified commit.'}
}
Write-Output "Published https://github.com/$env:GITHUB_REPOSITORY/releases/tag/$tag"
