Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$root=Join-Path ([IO.Path]::GetTempPath()) ('SQLitePass-release-test-'+[guid]::NewGuid().ToString('N'))
$savedSha=$env:GITHUB_SHA; $savedRepo=$env:GITHUB_REPOSITORY
$priorGh=Get-Item Function:\global:gh -ErrorAction SilentlyContinue
try {
    New-Item -ItemType Directory -Path $root | Out-Null
    $env:GITHUB_SHA='0123456789012345678901234567890123456789'; $env:GITHUB_REPOSITORY='example/release-test'
    [ordered]@{Version='1.1.1';SourceCommit=$env:GITHUB_SHA;SourceDirty=$false} | ConvertTo-Json | Set-Content (Join-Path $root 'release-provenance.json') -Encoding utf8
    Set-Content (Join-Path $root 'payload.txt') 'synthetic release bytes' -Encoding utf8
    @(Get-ChildItem $root -File | ForEach-Object {[ordered]@{File=$_.Name;Hash=(Get-FileHash $_.FullName -Algorithm SHA256).Hash}}) | ConvertTo-Json | Set-Content (Join-Path $root 'checksums.json') -Encoding utf8
    function global:gh {
        $global:LASTEXITCODE=0
        $state=$global:SQLitePassReleaseTestState
        $command=@($args)
        if($command[0] -eq 'release' -and $command[1] -eq 'view'){
            if(-not $state.Exists){$global:LASTEXITCODE=1;return}
            @{apiUrl='https://api.github.com/repos/example/release-test/releases/1';isDraft=$state.Draft;targetCommitish=$state.Commit} | ConvertTo-Json -Compress
        } elseif($command[0] -eq 'release' -and $command[1] -eq 'create'){
            $state.Writes.Add('create');$state.Exists=$true;$state.Draft=$true
        } elseif($command[0] -eq 'release' -and $command[1] -eq 'upload'){
            $state.Writes.Add('upload');$file=Get-Item -LiteralPath $command[3]
            $state.Assets.Add([pscustomobject]@{name=$file.Name;size=$file.Length;digest=('sha256:'+(Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant())})
        } elseif($command[0] -eq 'release' -and $command[1] -eq 'edit'){
            $state.Writes.Add('publish');$state.Draft=$false
        } elseif($command[0] -eq 'api' -and $command[1] -like '*git/ref/tags/*'){
            @{object=@{sha=$state.Commit}} | ConvertTo-Json -Compress
        } elseif($command[0] -eq 'api'){
            @{assets=@($state.Assets.ToArray())} | ConvertTo-Json -Depth 6 -Compress
        } else {throw 'Unexpected fake CLI command.'}
    }
    function New-State([bool]$Exists,[bool]$Draft,[bool]$WithAssets){
        $state=@{Exists=$Exists;Draft=$Draft;Commit=$env:GITHUB_SHA;Assets=[Collections.Generic.List[object]]::new();Writes=[Collections.Generic.List[string]]::new()}
        if($WithAssets){foreach($file in Get-ChildItem $root -File){$state.Assets.Add([pscustomobject]@{name=$file.Name;size=$file.Length;digest=('sha256:'+(Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant())})}}
        return $state
    }
    $publisher=Join-Path $PSScriptRoot 'Publish-GitHubRelease.ps1'
    $global:SQLitePassReleaseTestState=New-State $false $true $false
    & $publisher -ArtifactDirectory $root
    if($global:SQLitePassReleaseTestState.Draft -or -not $global:SQLitePassReleaseTestState.Writes.Contains('publish')){throw 'Fresh publication test failed.'}
    $global:SQLitePassReleaseTestState=New-State $true $false $true
    & $publisher -ArtifactDirectory $root
    if($global:SQLitePassReleaseTestState.Writes.Count -ne 0){throw 'A verified published release was modified on retry.'}
    $global:SQLitePassReleaseTestState=New-State $true $false $true
    $global:SQLitePassReleaseTestState.Commit='different-commit'
    $rejected=$false
    try {& $publisher -ArtifactDirectory $root} catch {$rejected=$true}
    if(-not $rejected -or $global:SQLitePassReleaseTestState.Writes.Count -ne 0){throw 'Different-commit release was not preserved.'}
    $global:SQLitePassReleaseTestState=New-State $true $true $true
    $global:SQLitePassReleaseTestState.Assets[0].digest='sha256:incorrect'
    $rejected=$false
    try {& $publisher -ArtifactDirectory $root} catch {$rejected=$true}
    if(-not $rejected -or $global:SQLitePassReleaseTestState.Writes.Contains('publish')){throw 'Digest mismatch did not stop publication.'}
    Write-Output 'Four publication safety scenarios passed with an in-memory CLI; no network calls.'
} finally {
    $env:GITHUB_SHA=$savedSha;$env:GITHUB_REPOSITORY=$savedRepo
    Remove-Item Function:\global:gh -ErrorAction SilentlyContinue
    if($priorGh){Set-Item Function:\global:gh $priorGh.ScriptBlock}
    Remove-Variable SQLitePassReleaseTestState -Scope Global -ErrorAction SilentlyContinue
    $resolved=[IO.Path]::GetFullPath($root)
    if(-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or -not [IO.Path]::GetFileName($resolved).StartsWith('SQLitePass-release-test-')){throw 'Invalid test cleanup path.'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
