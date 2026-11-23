param([Parameter(Mandatory)][string]$ArtifactDirectory)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$directory = (Resolve-Path -LiteralPath $ArtifactDirectory).ProviderPath
$privatePem = $env:UPDATE_SIGNING_PRIVATE_KEY
$publicBase64 = $env:UPDATE_SIGNING_PUBLIC_KEY
$env:UPDATE_SIGNING_PRIVATE_KEY = $null

$manifestPath = Join-Path $directory 'sqlitepass-update-auth.json'
$signaturePath = Join-Path $directory 'sqlitepass-update-auth.sig'
$checksumsPath = Join-Path $directory 'checksums.json'
$temporarySuffix = '.signing-' + [Guid]::NewGuid().ToString('N') + '.tmp'
$manifestTemporaryPath = $manifestPath + $temporarySuffix
$signatureTemporaryPath = $signaturePath + $temporarySuffix
$checksumsTemporaryPath = $checksumsPath + $temporarySuffix
$ownsManifest = $false
$ownsSignature = $false
$signingComplete = $false
$rsa = $null

try {
if ([string]::IsNullOrWhiteSpace($privatePem)) {
    throw 'Release signing requires the UPDATE_SIGNING_PRIVATE_KEY GitHub Actions secret.'
}
if ([string]::IsNullOrWhiteSpace($publicBase64)) {
    throw 'Release signing requires the UPDATE_SIGNING_PUBLIC_KEY repository variable embedded in the client.'
}
if ((Test-Path -LiteralPath $manifestPath) -or (Test-Path -LiteralPath $signaturePath)) {
    throw 'The candidate already contains update-authentication assets.'
}

$checksums = @(Get-Content -LiteralPath $checksumsPath -Raw | ConvertFrom-Json)
$files = @(Get-ChildItem -LiteralPath $directory -File | Where-Object Name -cne 'checksums.json' | ForEach-Object Name)
if ($checksums.Count -ne $files.Count -or @($checksums | ForEach-Object File | Select-Object -Unique).Count -ne $checksums.Count -or
    @(Compare-Object @($checksums | ForEach-Object File) $files -CaseSensitive).Count -ne 0) {
    throw 'The release artifact file set differs from its verified checksums.'
}
foreach ($entry in $checksums) {
    if ($entry.File -ne [IO.Path]::GetFileName($entry.File) -or $entry.Hash -notmatch '^[0-9a-fA-F]{64}$' -or
        (Get-FileHash -LiteralPath (Join-Path $directory $entry.File) -Algorithm SHA256).Hash -cne $entry.Hash) {
        throw "The release artifact checksum differs: $($entry.File)"
    }
}

$rsa = [System.Security.Cryptography.RSA]::Create()
    $rsa.ImportFromPem($privatePem)
    if ($rsa.KeySize -lt 3072 -or $rsa.KeySize -gt 8192) { throw 'The update signing key must be RSA 3072-bit or larger.' }
    $derivedPublic = [Convert]::ToBase64String($rsa.ExportSubjectPublicKeyInfo())
    if ($derivedPublic -cne $publicBase64) { throw 'The private signing key does not match UPDATE_SIGNING_PUBLIC_KEY.' }

    $manifest = & (Join-Path $PSScriptRoot 'Get-AvaloniaUpdateManifest.ps1') -ArtifactDirectory $directory
    $manifestBytes = [Text.UTF8Encoding]::new($false).GetBytes($manifest)
    $signature = $rsa.SignData($manifestBytes, [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    [IO.File]::WriteAllBytes($manifestTemporaryPath, $manifestBytes)
    [IO.File]::WriteAllBytes($signatureTemporaryPath, $signature)

    $checksums += [ordered]@{ File = 'sqlitepass-update-auth.json'; Hash = (Get-FileHash -LiteralPath $manifestTemporaryPath -Algorithm SHA256).Hash }
    $checksums += [ordered]@{ File = 'sqlitepass-update-auth.sig'; Hash = (Get-FileHash -LiteralPath $signatureTemporaryPath -Algorithm SHA256).Hash }
    [IO.File]::WriteAllText($checksumsTemporaryPath, ($checksums | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))

    [IO.File]::Move($manifestTemporaryPath, $manifestPath)
    $ownsManifest = $true
    [IO.File]::Move($signatureTemporaryPath, $signaturePath)
    $ownsSignature = $true
    [IO.File]::Move($checksumsTemporaryPath, $checksumsPath, $true)
    $signingComplete = $true
}
finally {
    if ($rsa) { $rsa.Dispose() }
    foreach ($temporaryPath in @($manifestTemporaryPath, $signatureTemporaryPath, $checksumsTemporaryPath)) {
        if (Test-Path -LiteralPath $temporaryPath) { Remove-Item -LiteralPath $temporaryPath -Force }
    }
    if (-not $signingComplete) {
        if ($ownsManifest -and (Test-Path -LiteralPath $manifestPath)) { Remove-Item -LiteralPath $manifestPath -Force }
        if ($ownsSignature -and (Test-Path -LiteralPath $signaturePath)) { Remove-Item -LiteralPath $signaturePath -Force }
    }
    $privatePem = $null
    $publicBase64 = $null
}

Write-Output 'The signed update manifest matches both verified full packages.'
