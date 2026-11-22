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
$baseline = '1.1.3'
if ($manifest.PackageId -cne 'SQLitePass' -or $manifest.Runtime -cne 'linux-x64' -or
    $manifest.Version -notmatch '^\d+\.\d+\.\d+$' -or [version]$manifest.Version -le [version]$baseline -or
    $manifest.Distribution -cnotmatch '^SQLitePass[-a-zA-Z0-9.]*\.AppImage$') {
    throw 'The AppImage upgrade probe requires a release candidate newer than its synthetic baseline.'
}

$isolated = [IO.Directory]::CreateTempSubdirectory('SQLitePass-AppImage-upgrade-').FullName
$baselinePublish = Join-Path $isolated 'publish'
$baselinePackages = Join-Path $isolated 'baseline'
$evidencePath = Join-Path $isolated 'evidence'
New-Item -ItemType Directory -Path $evidencePath | Out-Null
$root = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $root
$evidence = (New-Item -ItemType Directory -Path 'artifacts/verification/avalonia-linux-update' -Force).FullName
try {
    dotnet publish SQLitePass.Desktop/SQLitePass.Desktop.csproj -c Release -r linux-x64 --self-contained -o $baselinePublish `
        "-p:Version=$baseline" -p:RestoreLockedMode=true -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'The synthetic AppImage baseline build failed.' }
    dotnet tool run vpk -- pack --skip-updates -u SQLitePass -v $baseline -p $baselinePublish -e SQLitePass.Desktop `
        -o $baselinePackages -r linux-x64 -c linux -i (Join-Path $root 'SQLitePass.UI/Resources/app_icon.png')
    if ($LASTEXITCODE -ne 0) { throw 'The synthetic AppImage baseline packaging failed.' }

    $images = @(Get-ChildItem -LiteralPath $baselinePackages -Filter '*.AppImage' -File)
    if ($images.Count -ne 1) { throw 'The baseline did not produce exactly one AppImage.' }
    $installedPath = Join-Path $isolated 'SQLitePass.AppImage'
    Copy-Item -LiteralPath $images[0].FullName -Destination $installedPath
    & chmod +x -- $installedPath
    if ($LASTEXITCODE -ne 0) { throw 'Could not make the installed AppImage executable.' }
    $beforeHash = (Get-FileHash -LiteralPath $installedPath).Hash
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
    Write-Output "Verified in-place AppImage upgrade from synthetic $baseline to $($manifest.Version), restart and vault preservation."
} finally {
    foreach ($stage in @('before', 'scheduled', 'after')) {
        $receiptPath = Join-Path $evidencePath "$stage.json"
        if (Test-Path -LiteralPath $receiptPath -PathType Leaf) { Copy-Item -LiteralPath $receiptPath -Destination $evidence }
    }
    $velopackLog = Join-Path ([IO.Path]::GetTempPath()) 'velopack_SQLitePass.log'
    if (Test-Path -LiteralPath $velopackLog -PathType Leaf) { Copy-Item -LiteralPath $velopackLog -Destination $evidence }
    Pop-Location
}
