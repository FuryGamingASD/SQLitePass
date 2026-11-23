param([Parameter(Mandatory)][string]$CandidateDirectory)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsLinux -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'The AppImage upgrade probe requires a disposable GitHub-hosted Linux desktop.'
}
if (-not (Test-Path -LiteralPath '/dev/fuse')) { throw 'The AppImage upgrade probe requires FUSE.' }

$candidate = (Resolve-Path -LiteralPath $CandidateDirectory).ProviderPath
$manifestPath = Join-Path $candidate 'candidate-verification.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$baseline = '1.2.2'
if ($manifest.PackageId -cne 'SQLitePass' -or $manifest.Runtime -cne 'linux-x64' -or
    $manifest.Version -notmatch '^\d+\.\d+\.\d+$' -or [version]$manifest.Version -le [version]$baseline -or
    $manifest.Distribution -cnotmatch '^SQLitePass[-a-zA-Z0-9.]*\.AppImage$') {
    throw 'The AppImage upgrade probe requires a release candidate newer than the public Avalonia baseline.'
}

$isolated = [IO.Directory]::CreateTempSubdirectory('SQLitePass-AppImage-upgrade-').FullName
$evidencePath = Join-Path $isolated 'evidence'
New-Item -ItemType Directory -Path $evidencePath | Out-Null
$evidence = (New-Item -ItemType Directory -Path 'artifacts/verification/avalonia-linux-update' -Force).FullName
$signedEvidence = (New-Item -ItemType Directory -Path (Join-Path $evidence 'signed-upgrade')).FullName

function Invoke-AppImageUpgrade([string]$Image, [string]$Packages, [string]$Results, [string]$Scenario) {
    $probe = [Diagnostics.ProcessStartInfo]::new('dbus-run-session')
    $probe.UseShellExecute = $false
    foreach ($argument in @('--', 'xvfb-run', '-a', 'bash', (Join-Path $PSScriptRoot 'Start-LinuxVerificationDesktop.sh'),
        'bash', (Join-Path $PSScriptRoot 'Wait-LinuxAppImageUpgrade.sh'), $Image, $Packages, $Results, $Scenario)) {
        $probe.ArgumentList.Add($argument)
    }
    $probe.Environment['SQLITEPASS_LOGIN_SESSION_PROBE'] = Join-Path $PSScriptRoot 'Verify-LinuxLoginSession.py'
    $process = [Diagnostics.Process]::Start($probe)
    try {
        if (-not $process.WaitForExit(125000)) {
            $process.Kill($true)
            throw 'The AppImage upgrade probe timed out.'
        }
        if ($process.ExitCode -ne 0) { throw "The AppImage upgrade probe exited with $($process.ExitCode)." }
    } finally { $process.Dispose() }
}

try {
    $installedPath = Join-Path $isolated 'SQLitePass.AppImage'
    Invoke-WebRequest -Uri "https://github.com/FuryGamingASD/SQLitePass/releases/download/v$baseline/SQLitePass.AppImage" -OutFile $installedPath
    $beforeHash = (Get-FileHash -LiteralPath $installedPath).Hash
    if ($beforeHash -cne 'A5D38FF401AA1A7FF972D20E5006D31908E636A7A8FEDE1C1EF2295C218F79ED') {
        throw 'The public baseline AppImage checksum differs.'
    }
    & chmod +x -- $installedPath
    if ($LASTEXITCODE -ne 0) { throw 'Could not make the installed AppImage executable.' }
    $expectedHash = (Get-FileHash -LiteralPath (Join-Path $candidate $manifest.Distribution)).Hash
    if ($beforeHash -ceq $expectedHash) { throw 'The update fixture and release AppImage have identical bytes.' }

    Invoke-AppImageUpgrade $installedPath $candidate $evidencePath '--verify-appimage-update'

    foreach ($stage in @('before', 'scheduled', 'after')) {
        $receipt = Get-Content -LiteralPath (Join-Path $evidencePath "$stage.json") -Raw | ConvertFrom-Json
        $expected = if ($stage -ceq 'after') { $manifest.Version } else { $baseline }
        if ($receipt.Version -cne $expected -or $receipt.AssemblyVersion -cne "$expected.0" -or $receipt.AppImagePath -cne $installedPath) {
            throw "The $stage receipt does not match the installed AppImage and version."
        }
    }
    $afterHash = (Get-FileHash -LiteralPath $installedPath).Hash
    if ($afterHash -cne $expectedHash) { throw 'The installed file differs from the verified release AppImage after upgrading.' }

    [ordered]@{ InstalledAppImageSHA256 = $afterHash; BaselineVersion = $baseline; InstalledVersion = $manifest.Version } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'in-place-upgrade.json') -Encoding utf8

    $manualDirectory = (New-Item -ItemType Directory -Path (Join-Path $isolated 'manual')).FullName
    $installedPath = Join-Path $manualDirectory 'SQLitePass.AppImage'
    Copy-Item -LiteralPath (Join-Path $candidate $manifest.Distribution) -Destination $installedPath
    & chmod +x -- $installedPath
    if ($LASTEXITCODE -ne 0 -or (Get-FileHash -LiteralPath $installedPath).Hash -cne $expectedHash) {
        throw 'The manual AppImage installation differs from the candidate.'
    }
    $probePackages = Join-Path (Split-Path -Parent $candidate) 'signed-upgrade'
    Invoke-AppImageUpgrade $installedPath $probePackages $signedEvidence '--verify-signed-update'
    $probeFeed = Get-Content -LiteralPath (Join-Path $probePackages 'releases.linux.json') -Raw | ConvertFrom-Json
    $target = @($probeFeed.Assets)[0]
    foreach ($stage in @('before','scheduled','after')) {
        $receipt = Get-Content -LiteralPath (Join-Path $signedEvidence "$stage.json") -Raw | ConvertFrom-Json
        $expected = if ($stage -eq 'after') { $target.Version } else { $manifest.Version }
        if ($receipt.Version -cne $expected -or $receipt.AppImagePath -cne $installedPath -or
            $receipt.AssemblyVersion -cne "$($manifest.Version).0") { throw 'Unexpected signed AppImage upgrade receipt.' }
    }
    $probeImage = @(Get-ChildItem -LiteralPath $probePackages -File -Filter '*.AppImage')
    if ($probeImage.Count -ne 1 -or (Get-FileHash -LiteralPath $installedPath).Hash -cne
        (Get-FileHash -LiteralPath $probeImage[0].FullName).Hash) { throw 'The signed AppImage upgrade bytes differ.' }
    Write-Output 'Verified a signed Linux package upgrade, locked restart and reopening the synthetic vault.'
    $manifest | Add-Member -NotePropertyName AppImageUpgradeVerified -NotePropertyValue $true
    $manifest | Add-Member -NotePropertyName AppImageUpgradeFrom -NotePropertyValue $baseline
    $manifest | Add-Member -NotePropertyName SignedUpgradeVerified -NotePropertyValue $true
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    Write-Output "Verified in-place AppImage upgrade from public $baseline to $($manifest.Version), restart and synthetic vault preservation."
} finally {
    foreach ($stage in @('before', 'scheduled', 'after')) {
        $receiptPath = Join-Path $evidencePath "$stage.json"
        if (Test-Path -LiteralPath $receiptPath -PathType Leaf) { Copy-Item -LiteralPath $receiptPath -Destination $evidence }
    }
    $velopackLog = Join-Path ([IO.Path]::GetTempPath()) 'velopack_SQLitePass.log'
    if (Test-Path -LiteralPath $velopackLog -PathType Leaf) { Copy-Item -LiteralPath $velopackLog -Destination $evidence }
}
