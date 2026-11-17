param([Parameter(Mandatory)][string]$ArtifactDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$provenance=Get-Content (Join-Path $ArtifactDirectory 'release-provenance.json') -Raw | ConvertFrom-Json
if($provenance.SourceCommit -ne $env:GITHUB_SHA -or $provenance.SourceDirty){throw 'Only the clean verified commit may be published.'}
if($provenance.Version -notmatch '^\d+\.\d+\.\d+$'){throw 'Unexpected version.'}
$tag='v'+$provenance.Version
$checksums=@(Get-Content (Join-Path $ArtifactDirectory 'checksums.json') -Raw | ConvertFrom-Json)
foreach($entry in $checksums){
    if($entry.File -ne [IO.Path]::GetFileName($entry.File)){throw 'Invalid checksum filename.'}
    if((Get-FileHash (Join-Path $ArtifactDirectory $entry.File) -Algorithm SHA256).Hash -ne $entry.Hash){throw 'Downloaded CI artifact checksum mismatch.'}
}
$existingText=& gh release view $tag --repo $env:GITHUB_REPOSITORY --json apiUrl,isDraft,targetCommitish 2>$null
$existing=$null
if($LASTEXITCODE -eq 0){$existing=$existingText | ConvertFrom-Json}
if($existing){
    if(-not $existing.isDraft -or $existing.targetCommitish -ne $env:GITHUB_SHA){throw 'An existing release must not be overwritten.'}
} else {
    & gh release create $tag --repo $env:GITHUB_REPOSITORY --target $env:GITHUB_SHA --title "SQLitePass $($provenance.Version)" --notes-file "RELEASE_NOTES_v$($provenance.Version).md" --draft
    if($LASTEXITCODE -ne 0){throw 'Draft release creation failed.'}
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
        & gh release upload $tag $file.FullName --repo $env:GITHUB_REPOSITORY
        if($LASTEXITCODE -ne 0){throw "Asset upload failed: $($file.Name)"}
    }
}
$releaseText=& gh api $releaseApi
if($LASTEXITCODE -ne 0){throw 'Cannot inspect uploaded assets.'}
$release=$releaseText | ConvertFrom-Json
foreach($file in (Get-ChildItem -LiteralPath $ArtifactDirectory -File)){
    $uploaded=@($release.assets | Where-Object name -eq $file.Name)
    $expected='sha256:'+((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant())
    if($uploaded.Count -ne 1 -or $uploaded[0].size -ne $file.Length -or $uploaded[0].digest -ne $expected){throw "Remote asset digest mismatch: $($file.Name)"}
}
& gh release edit $tag --repo $env:GITHUB_REPOSITORY --draft=false --latest
if($LASTEXITCODE -ne 0){throw 'Release publication failed.'}
$refText=& gh api "repos/$env:GITHUB_REPOSITORY/git/ref/tags/$tag"
if($LASTEXITCODE -ne 0 -or ($refText | ConvertFrom-Json).object.sha -ne $env:GITHUB_SHA){throw 'Published tag does not match the verified commit.'}
Write-Output "Published https://github.com/$env:GITHUB_REPOSITORY/releases/tag/$tag"
