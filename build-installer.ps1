param(
    [ValidatePattern('^\d+\.\d+\.\d+(-[A-Za-z0-9.-]+)?$')]
    [string]$Version = "1.1.2",
    [string]$CertificateThumbprint = $env:SQLITEPASS_SIGN_CERT,
    [switch]$AllowUnsigned
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $AllowUnsigned -and [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    throw 'Per una release firmata specificare -CertificateThumbprint. Usare -AllowUnsigned solo per pacchetti locali di verifica.'
}
if ($CertificateThumbprint -and $CertificateThumbprint -notmatch '^[A-Fa-f0-9]{40}$') {
    throw 'Impronta del certificato non valida.'
}

Push-Location -LiteralPath $PSScriptRoot
try {
    # Each build has its own directories. Existing releases are never deleted.
    $buildId = $Version + '-' + [guid]::NewGuid().ToString('N')
    $artifactRoot = Join-Path $PSScriptRoot 'artifacts'
    $publishDir = Join-Path (Join-Path $artifactRoot 'publish') $buildId
    $releaseDir = Join-Path (Join-Path $artifactRoot 'releases') $buildId
    New-Item -ItemType Directory -Path $publishDir, $releaseDir -Force | Out-Null

    dotnet restore SQLitePass.slnx --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore fallito.' }
    dotnet test SQLitePass.slnx -c Release --no-restore --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Test falliti: packaging interrotto.' }
    dotnet publish SQLitePass.UI -c Release -r win-x64 --self-contained true -o $publishDir /p:Version=$Version /p:RestoreLockedMode=true
    if ($LASTEXITCODE -ne 0) { throw 'Publish fallito.' }
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'Ripristino del tool Velopack fallito.' }

    $packArguments = @('tool', 'run', 'vpk', '--', 'pack', '-u', 'SQLitePass', '-v', $Version,
        '-p', $publishDir, '-e', 'SQLitePass.exe', '-o', $releaseDir, '-r', 'win-x64',
        '-i', (Join-Path $PSScriptRoot 'SQLitePass.UI\Resources\app.ico'), '--packTitle', 'SQLitePass')
    if ($CertificateThumbprint) {
        $packArguments += @('--signParams', "/sha1 $CertificateThumbprint /fd sha256 /tr http://timestamp.digicert.com /td sha256")
    }
    & dotnet @packArguments
    if ($LASTEXITCODE -ne 0) { throw 'Packaging fallito.' }

    if ($CertificateThumbprint) {
        foreach ($file in @((Join-Path $publishDir 'SQLitePass.exe'), (Join-Path $releaseDir 'SQLitePass-win-Setup.exe'))) {
            if ((Get-AuthenticodeSignature -LiteralPath $file).Status -ne 'Valid') { throw "Firma non valida: $file" }
        }
    }
    $sourceCommit = & git -c 'safe.directory=*' rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Impossibile identificare il commit sorgente.' }
    $sourceTree = & git -c 'safe.directory=*' rev-parse 'HEAD^{tree}'
    if ($LASTEXITCODE -ne 0) { throw 'Impossibile identificare il tree sorgente.' }
    $sourceDirty = @(& git -c 'safe.directory=*' status --porcelain).Count -ne 0
    $provenance = [ordered]@{
        Version=$Version; BuildId=$buildId; SourceCommit=$sourceCommit; SourceTree=$sourceTree; SourceDirty=$sourceDirty;
        Sdk=(& dotnet --version); SigningWaived=[bool]$AllowUnsigned;
        NativeInteractiveQaVerified=$false; IndependentSecurityReviewVerified=$false;
        RunUrl= $(if ($env:GITHUB_RUN_ID) { "$($env:GITHUB_SERVER_URL)/$($env:GITHUB_REPOSITORY)/actions/runs/$($env:GITHUB_RUN_ID)" } else { $null })
    }
    $provenance | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $releaseDir 'release-provenance.json') -Encoding utf8
    Get-ChildItem -LiteralPath $releaseDir -File | Get-FileHash -Algorithm SHA256 |
        Select-Object @{Name='File';Expression={Split-Path $_.Path -Leaf}}, Hash |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $releaseDir 'checksums.json') -Encoding utf8
    $provenance | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $artifactRoot 'last-build.json') -Encoding utf8
    Write-Host "Pacchetti generati in: $releaseDir"
    if ($AllowUnsigned) { Write-Host 'Firma esclusa con -AllowUnsigned; limiti di verifica riportati nella provenienza e nelle release notes.' }
}
finally { Pop-Location }
