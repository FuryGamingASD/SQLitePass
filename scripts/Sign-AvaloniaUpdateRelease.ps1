[CmdletBinding(DefaultParameterSetName = 'OfflineKey')]
param(
    [Parameter(Mandatory)][string]$ArtifactDirectory,
    [Parameter(Mandatory, ParameterSetName = 'OfflineKey')][string]$PrivateKeyPath,
    [Parameter(Mandatory, ParameterSetName = 'ProvidedSigner')][System.Security.Cryptography.RSA]$Signer,
    [string]$RenewFreshnessTo
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$directory = (Resolve-Path -LiteralPath $ArtifactDirectory).ProviderPath
$publicBase64 = $env:UPDATE_SIGNING_PUBLIC_KEY

$manifestPath = Join-Path $directory 'sqlitepass-update-auth.json'
$signaturePath = Join-Path $directory 'sqlitepass-update-auth.sig'
$freshnessPath = Join-Path $directory 'sqlitepass-update-freshness.json'
$checksumsPath = Join-Path $directory 'checksums.json'
$temporarySuffix = '.signing-' + [Guid]::NewGuid().ToString('N') + '.tmp'
$manifestTemporaryPath = $manifestPath + $temporarySuffix
$signatureTemporaryPath = $signaturePath + $temporarySuffix
$freshnessTemporaryPath = $freshnessPath + $temporarySuffix
$checksumsTemporaryPath = $checksumsPath + $temporarySuffix
$ownsManifest = $false
$ownsSignature = $false
$ownsFreshness = $false
$signingComplete = $false
$rsa = $Signer
$ownsSigner = $false

try {
if ([string]::IsNullOrWhiteSpace($publicBase64)) {
    throw 'Set UPDATE_SIGNING_PUBLIC_KEY to the public key embedded in the release clients.'
}
if (-not $RenewFreshnessTo -and ((Test-Path -LiteralPath $manifestPath) -or (Test-Path -LiteralPath $signaturePath) -or
    (Test-Path -LiteralPath $freshnessPath))) {
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

    if ($PSCmdlet.ParameterSetName -eq 'OfflineKey') {
        if ($env:GITHUB_ACTIONS -eq 'true') { throw 'The production private key must be loaded outside GitHub Actions.' }
        $pem = Get-Content -LiteralPath $PrivateKeyPath -Raw
        $password = Read-Host 'Signing key passphrase' -AsSecureString
        try {
            $rsa = [System.Security.Cryptography.RSA]::Create()
            $ownsSigner = $true
            $rsa.ImportFromEncryptedPem($pem, [PSCredential]::new('signing-key', $password).GetNetworkCredential().Password)
        } finally {
            $pem = $null
            $password.Dispose()
        }
    }
    if ($rsa.KeySize -lt 3072 -or $rsa.KeySize -gt 8192) { throw 'The update signing key must be RSA 3072-bit or larger.' }
    $derivedPublic = [Convert]::ToBase64String($rsa.ExportSubjectPublicKeyInfo())
    if ($derivedPublic -cne $publicBase64) { throw 'The private signing key does not match UPDATE_SIGNING_PUBLIC_KEY.' }

    $manifest = & (Join-Path $PSScriptRoot 'Get-AvaloniaUpdateManifest.ps1') -ArtifactDirectory $directory
    $manifestBytes = [Text.UTF8Encoding]::new($false).GetBytes($manifest)
    if ($RenewFreshnessTo) {
        $previousBytes = [IO.File]::ReadAllBytes($manifestPath)
        $previousSignature = [IO.File]::ReadAllBytes($signaturePath)
        if ([Text.Encoding]::UTF8.GetString($previousBytes) -cne $manifest -or
            -not $rsa.VerifyData($previousBytes, $previousSignature, [System.Security.Cryptography.HashAlgorithmName]::SHA256,
                [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)) {
            throw 'Freshness renewal requires a verified existing update manifest.'
        }
    }
    $now = [DateTimeOffset]::UtcNow
    $checkpoint = [ordered]@{
        SchemaVersion = 1; PackageId = 'SQLitePass'; Version = ($manifest | ConvertFrom-Json).Version
        ManifestSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($manifestBytes))
        IssuedAt = $now.ToString('O'); ExpiresAt = $now.AddDays(30).ToString('O')
    } | ConvertTo-Json -Compress
    $checkpointBytes = [Text.UTF8Encoding]::new($false).GetBytes($checkpoint)
    $checkpointSignature = $rsa.SignData($checkpointBytes, [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $envelope = [ordered]@{
        Checkpoint = [Convert]::ToBase64String($checkpointBytes)
        Signature = [Convert]::ToBase64String($checkpointSignature)
    } | ConvertTo-Json -Compress
    if ($RenewFreshnessTo) {
        $outputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($RenewFreshnessTo)
        if ([IO.Path]::GetDirectoryName($outputPath) -eq $directory -or (Test-Path -LiteralPath $outputPath)) {
            throw 'Choose a new checkpoint output file outside the approved bundle.'
        }
        [IO.File]::WriteAllText($outputPath, $envelope, [Text.UTF8Encoding]::new($false))
        Write-Output 'Created a renewed freshness checkpoint for the same signed release.'
        return
    }
    $signature = $rsa.SignData($manifestBytes, [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    [IO.File]::WriteAllBytes($manifestTemporaryPath, $manifestBytes)
    [IO.File]::WriteAllBytes($signatureTemporaryPath, $signature)
    [IO.File]::WriteAllText($freshnessTemporaryPath, $envelope, [Text.UTF8Encoding]::new($false))

    $checksums += [ordered]@{ File = 'sqlitepass-update-auth.json'; Hash = (Get-FileHash -LiteralPath $manifestTemporaryPath -Algorithm SHA256).Hash }
    $checksums += [ordered]@{ File = 'sqlitepass-update-auth.sig'; Hash = (Get-FileHash -LiteralPath $signatureTemporaryPath -Algorithm SHA256).Hash }
    $checksums += [ordered]@{ File = 'sqlitepass-update-freshness.json'; Hash = (Get-FileHash -LiteralPath $freshnessTemporaryPath -Algorithm SHA256).Hash }
    [IO.File]::WriteAllText($checksumsTemporaryPath, ($checksums | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))

    [IO.File]::Move($manifestTemporaryPath, $manifestPath)
    $ownsManifest = $true
    [IO.File]::Move($signatureTemporaryPath, $signaturePath)
    $ownsSignature = $true
    [IO.File]::Move($freshnessTemporaryPath, $freshnessPath)
    $ownsFreshness = $true
    [IO.File]::Move($checksumsTemporaryPath, $checksumsPath, $true)
    $signingComplete = $true
}
finally {
    if ($ownsSigner) { $rsa.Dispose() }
    foreach ($temporaryPath in @($manifestTemporaryPath, $signatureTemporaryPath, $freshnessTemporaryPath, $checksumsTemporaryPath)) {
        if (Test-Path -LiteralPath $temporaryPath) { Remove-Item -LiteralPath $temporaryPath -Force }
    }
    if (-not $signingComplete) {
        if ($ownsManifest -and (Test-Path -LiteralPath $manifestPath)) { Remove-Item -LiteralPath $manifestPath -Force }
        if ($ownsSignature -and (Test-Path -LiteralPath $signaturePath)) { Remove-Item -LiteralPath $signaturePath -Force }
        if ($ownsFreshness -and (Test-Path -LiteralPath $freshnessPath)) { Remove-Item -LiteralPath $freshnessPath -Force }
    }
    $publicBase64 = $null
}

Write-Output 'The signed update manifest matches both verified full packages.'
