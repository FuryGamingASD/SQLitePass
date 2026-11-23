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
$baseline = '1.2.1'
if ($manifest.PackageId -cne 'SQLitePass' -or $manifest.Runtime -cne 'linux-x64' -or
    $manifest.Version -notmatch '^\d+\.\d+\.\d+$' -or [version]$manifest.Version -le [version]$baseline -or
    $manifest.Distribution -cnotmatch '^SQLitePass[-a-zA-Z0-9.]*\.AppImage$') {
    throw 'The AppImage upgrade probe requires a release candidate newer than the public Avalonia baseline.'
}

$isolated = [IO.Directory]::CreateTempSubdirectory('SQLitePass-AppImage-upgrade-').FullName
$evidencePath = Join-Path $isolated 'evidence'
New-Item -ItemType Directory -Path $evidencePath | Out-Null
$evidence = (New-Item -ItemType Directory -Path 'artifacts/verification/avalonia-linux-update' -Force).FullName
try {
    $installedPath = Join-Path $isolated 'SQLitePass.AppImage'
    Invoke-WebRequest -Uri "https://github.com/FuryGamingASD/SQLitePass/releases/download/v$baseline/SQLitePass.AppImage" -OutFile $installedPath
    $beforeHash = (Get-FileHash -LiteralPath $installedPath).Hash
    if ($beforeHash -cne '8229B7EF485E31A71277BBADAF24A7A9C89A664D67B5056299918835278BDE7F') {
        throw 'The public baseline AppImage checksum differs.'
    }
    & chmod +x -- $installedPath
    if ($LASTEXITCODE -ne 0) { throw 'Could not make the installed AppImage executable.' }
    $expectedHash = (Get-FileHash -LiteralPath (Join-Path $candidate $manifest.Distribution)).Hash
    if ($beforeHash -ceq $expectedHash) { throw 'The update fixture and release AppImage have identical bytes.' }

    $probe = [Diagnostics.ProcessStartInfo]::new('dbus-run-session')
    $probe.UseShellExecute = $false
    foreach ($argument in @('--', 'xvfb-run', '-a', 'bash', (Join-Path $PSScriptRoot 'Start-LinuxVerificationDesktop.sh'),
        'bash', (Join-Path $PSScriptRoot 'Wait-LinuxAppImageUpgrade.sh'), $installedPath, $candidate, $evidencePath)) {
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
    $manifest | Add-Member -NotePropertyName AppImageUpgradeVerified -NotePropertyValue $true
    $manifest | Add-Member -NotePropertyName AppImageUpgradeFrom -NotePropertyValue $baseline
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
