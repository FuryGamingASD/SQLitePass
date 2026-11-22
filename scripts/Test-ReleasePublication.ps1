Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$root=Join-Path ([IO.Path]::GetTempPath()) ('SQLitePass-release-test-'+[guid]::NewGuid().ToString('N'))
$savedSha=$env:GITHUB_SHA; $savedRepo=$env:GITHUB_REPOSITORY
try {
    New-Item -ItemType Directory -Path $root | Out-Null
    $env:GITHUB_SHA='0123456789012345678901234567890123456789'; $env:GITHUB_REPOSITORY='example/release-test'
    [ordered]@{Version='1.1.3';SourceCommit=$env:GITHUB_SHA;SourceDirty=$false} | ConvertTo-Json | Set-Content (Join-Path $root 'release-provenance.json') -Encoding utf8
    Set-Content (Join-Path $root 'payload.txt') 'synthetic release bytes' -Encoding utf8
    @(Get-ChildItem $root -File | ForEach-Object {[ordered]@{File=$_.Name;Hash=(Get-FileHash $_.FullName -Algorithm SHA256).Hash}}) | ConvertTo-Json | Set-Content (Join-Path $root 'checksums.json') -Encoding utf8
    function gh {
        $global:LASTEXITCODE=0
        $state=$global:SQLitePassReleaseTestState
        $command=@($args)
        if($command[0] -eq 'release' -and $command[1] -eq 'view'){
            if(-not $state.Exists){$global:LASTEXITCODE=1;return}
            @{apiUrl='https://api.github.com/repos/example/release-test/releases/1';isDraft=$state.Draft;targetCommitish=$state.Commit} | ConvertTo-Json -Compress
        } elseif($command[0] -eq 'release' -and $command[1] -eq 'create'){
            if($state.RequireExistingTag -and ('--verify-tag' -notin $command -or '--target' -in $command)){
                throw 'Tag publication must verify the pushed tag without creating or retargeting it.'
            }
            $notesArgument=[Array]::IndexOf($command,'--notes-file')
            if($notesArgument -lt 0 -or $notesArgument+1 -ge $command.Count -or -not (Test-Path -LiteralPath $command[$notesArgument+1] -PathType Leaf)){throw 'Release notes file was not passed or does not exist.'}
            $notesText=Get-Content -LiteralPath $command[$notesArgument+1] -Raw
            $headings=[regex]::Matches($notesText,'(?m)^# .+$')
            if($headings.Count -ne 1 -or $headings[0].Value.TrimEnd() -cne '# SQLitePass 1.1.3'){
                throw 'Draft notes must contain only the matching release section from RELEASE_NOTES.md.'
            }
            $state.Writes.Add('create');$state.Exists=$true;$state.Draft=$true
        } elseif($command[0] -eq 'release' -and $command[1] -eq 'upload'){
            $state.Writes.Add('upload');$file=Get-Item -LiteralPath $command[3]
            $state.Assets.Add([pscustomobject]@{name=$file.Name;size=$file.Length;digest=('sha256:'+(Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant())})
        } elseif($command[0] -eq 'release' -and $command[1] -eq 'edit'){
            $state.Writes.Add('publish');$state.Draft=$false
        } elseif($command[0] -eq 'api' -and $command[1] -like '*git/ref/tags/*'){
            @{object=@{sha=$state.Commit}} | ConvertTo-Json -Compress
        } elseif($command[0] -eq 'api' -and $command[1] -ceq 'repos/example/release-test/commits/refs%2Ftags%2Fv1.1.3'){
            $state.TagCommit
        } elseif($command[0] -eq 'api'){
            @{assets=@($state.Assets.ToArray())} | ConvertTo-Json -Depth 6 -Compress
        } else {throw 'Unexpected fake CLI command.'}
    }
    function New-State([bool]$Exists,[bool]$Draft,[bool]$WithAssets){
        $state=@{Exists=$Exists;Draft=$Draft;Commit=$env:GITHUB_SHA;TagCommit=$env:GITHUB_SHA;RequireExistingTag=$false;
            Assets=[Collections.Generic.List[object]]::new();Writes=[Collections.Generic.List[string]]::new()}
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
    $global:SQLitePassReleaseTestState=New-State $false $true $false
    $provenancePath=Join-Path $root 'release-provenance.json'
    $provenance=Get-Content -LiteralPath $provenancePath -Raw | ConvertFrom-Json
    $provenance.Version='9999.9999.9999'
    $provenance | ConvertTo-Json | Set-Content -LiteralPath $provenancePath -Encoding utf8
    $rejected=$false
    try {& $publisher -ArtifactDirectory $root} catch {
        if($_.Exception.Message -ne "Expected one '# SQLitePass 9999.9999.9999' heading in RELEASE_NOTES.md."){throw}
        $rejected=$true
    }
    if(-not $rejected -or $global:SQLitePassReleaseTestState.Writes.Count -ne 0){throw 'Missing release notes did not stop publication.'}

    $provenance.Version='1.1.3'
    $provenance | Add-Member -NotePropertyName PackageId -NotePropertyValue 'SQLitePass'
    $provenance | ConvertTo-Json | Set-Content -LiteralPath $provenancePath -Encoding utf8
    foreach($asset in @('releases.win.json', 'releases.linux.json', 'SQLitePass-win-Setup.exe', 'SQLitePass-linux.AppImage')){
        Set-Content -LiteralPath (Join-Path $root $asset) -Value "Synthetic $asset" -Encoding utf8
    }
    @(Get-ChildItem -LiteralPath $root -File | Where-Object Name -ne 'checksums.json' | ForEach-Object {
        [ordered]@{File=$_.Name;Hash=(Get-FileHash -LiteralPath $_.FullName).Hash}
    }) | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'checksums.json') -Encoding utf8

    $global:SQLitePassReleaseTestState=New-State $false $true $false
    $global:SQLitePassReleaseTestState.RequireExistingTag=$true
    & $publisher -ArtifactDirectory $root -ExistingTag 'v1.1.3'
    if($global:SQLitePassReleaseTestState.Draft -or -not $global:SQLitePassReleaseTestState.Writes.Contains('publish')){
        throw 'The two-platform release was not published in the simulation.'
    }
    $global:SQLitePassReleaseTestState=New-State $false $true $false
    $global:SQLitePassReleaseTestState.TagCommit='a different commit'
    $rejected=$false
    try { & $publisher -ArtifactDirectory $root -ExistingTag 'v1.1.3' } catch { $rejected=$true }
    if(-not $rejected -or $global:SQLitePassReleaseTestState.Writes.Count -ne 0){ throw 'A moved tag was not rejected before writing.' }

    $global:SQLitePassReleaseTestState=New-State $false $true $false
    Remove-Item -LiteralPath (Join-Path $root 'releases.linux.json')
    $rejected=$false
    try { & $publisher -ArtifactDirectory $root -ExistingTag 'v1.1.3' } catch { $rejected=$true }
    if(-not $rejected -or $global:SQLitePassReleaseTestState.Writes.Count -ne 0){ throw 'A missing channel was not rejected before writing.' }
    Write-Output 'Eight publication safety scenarios passed using an in-memory CLI.'
} finally {
    $env:GITHUB_SHA=$savedSha;$env:GITHUB_REPOSITORY=$savedRepo
    Remove-Variable SQLitePassReleaseTestState -Scope Global -ErrorAction SilentlyContinue
    $resolved=[IO.Path]::GetFullPath($root)
    if(-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or -not [IO.Path]::GetFileName($resolved).StartsWith('SQLitePass-release-test-')){throw 'Invalid test cleanup path.'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
