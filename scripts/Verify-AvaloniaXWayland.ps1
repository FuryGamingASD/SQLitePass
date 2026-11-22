param([Parameter(Mandatory)][string]$CandidateDirectory)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsLinux -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'The XWayland desktop probe requires a disposable GitHub-hosted Linux desktop.'
}
$candidate = (Resolve-Path -LiteralPath $CandidateDirectory).ProviderPath
$manifestPath = Join-Path $candidate 'candidate-verification.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.PackageId -cnotin @('SQLitePass', 'SQLitePassAvaloniaPreview') -or $manifest.Runtime -cne 'linux-x64' -or
    $manifest.Distribution -cnotmatch ('^' + [regex]::Escape($manifest.PackageId) + '[-a-zA-Z0-9.]*\.AppImage$')) {
    throw 'The XWayland desktop probe requires a verified Linux AppImage.'
}

$probe = [Diagnostics.ProcessStartInfo]::new('dbus-run-session')
$probe.UseShellExecute = $false
$probe.Environment['APPIMAGE_EXTRACT_AND_RUN'] = '1'
$probe.Environment['SQLITEPASS_TRAY_PROBE_WATCHER'] = Join-Path $PSScriptRoot 'Verify-LinuxTrayWatcher.py'
$probe.Environment['SQLITEPASS_LOGIN_SESSION_PROBE'] = Join-Path $PSScriptRoot 'Verify-LinuxLoginSession.py'
foreach ($argument in @('--', 'bash', (Join-Path $PSScriptRoot 'Start-XWaylandVerificationDesktop.sh'),
    (Join-Path $candidate $manifest.Distribution), '--verify-desktop')) {
    $probe.ArgumentList.Add($argument)
}
$process = [Diagnostics.Process]::Start($probe)
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(150)
    while (-not $process.WaitForExit(1000)) {
        if ([DateTime]::UtcNow -ge $deadline) {
            $process.Kill($true)
            throw 'The XWayland desktop probe timed out.'
        }
    }
    if ($process.ExitCode -ne 0) { throw "The XWayland desktop probe exited with $($process.ExitCode)." }
} finally { $process.Dispose() }

$manifest | Add-Member -NotePropertyName NativeXWaylandClipboardVerified -NotePropertyValue $true
$manifest | Add-Member -NotePropertyName NativeXWaylandTrayVerified -NotePropertyValue $true
$manifest | Add-Member -NotePropertyName NativeXWaylandAutoTypeVerified -NotePropertyValue $true
$manifest | Add-Member -NotePropertyName NativeXWaylandLogindTransportVerified -NotePropertyValue $true
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8
