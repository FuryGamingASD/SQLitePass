param([Parameter(Mandatory)][string]$CandidateDirectory)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsLinux -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'The XWayland clipboard probe requires a disposable GitHub-hosted Linux desktop.'
}
$candidate = (Resolve-Path -LiteralPath $CandidateDirectory).ProviderPath
$manifestPath = Join-Path $candidate 'candidate-verification.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.PackageId -cne 'SQLitePass' -or $manifest.Runtime -cne 'linux-x64' -or
    $manifest.Distribution -cnotmatch '^SQLitePass[-a-zA-Z0-9.]*\.AppImage$') {
    throw 'The XWayland clipboard probe requires a verified release AppImage.'
}

$probe = [Diagnostics.ProcessStartInfo]::new('dbus-run-session')
$probe.UseShellExecute = $false
$probe.Environment['APPIMAGE_EXTRACT_AND_RUN'] = '1'
foreach ($argument in @('--', 'bash', (Join-Path $PSScriptRoot 'Start-XWaylandVerificationDesktop.sh'),
    (Join-Path $candidate $manifest.Distribution), '--verify-startup-and-clipboard')) {
    $probe.ArgumentList.Add($argument)
}
$process = [Diagnostics.Process]::Start($probe)
try {
    if (-not $process.WaitForExit(55000)) {
        $process.Kill($true)
        throw 'The XWayland clipboard probe timed out.'
    }
    if ($process.ExitCode -ne 0) { throw "The XWayland clipboard probe exited with $($process.ExitCode)." }
} finally { $process.Dispose() }

$manifest | Add-Member -NotePropertyName NativeXWaylandClipboardVerified -NotePropertyValue $true
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8
