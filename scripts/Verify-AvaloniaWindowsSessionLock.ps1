param(
    [Parameter(Mandatory)][string]$Executable,
    [string]$ResultsDirectory = 'artifacts/verification/windows-session-lock'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Run this interactive check on Windows.' }
$application = (Resolve-Path -LiteralPath $Executable).ProviderPath
$directory = New-Item -ItemType Directory -Path (Join-Path $ResultsDirectory ([guid]::NewGuid().ToString('N')))
$reportPath = Join-Path $directory.FullName 'session-lock.json'
$start = [Diagnostics.ProcessStartInfo]::new($application)
$start.UseShellExecute = $false
$start.ArgumentList.Add('--verify-win-session-lock')
$start.ArgumentList.Add($reportPath)
Write-Host 'The app will open a synthetic vault in a temporary profile.'
Write-Host 'When prompted in its status bar, press Win+L, then sign in to Windows yourself.'
$process = [Diagnostics.Process]::Start($start)
try {
    $deadline = [DateTime]::UtcNow.AddMinutes(5)
    while (-not (Test-Path -LiteralPath $reportPath)) {
        if ($process.HasExited) { throw 'The verification app exited without a result.' }
        if ([DateTime]::UtcNow -ge $deadline) { throw 'No session-lock result was received within five minutes.' }
        Start-Sleep -Milliseconds 250
    }
    # The report appears while its writer still holds an exclusive file handle.
    $result = $null
    while ($null -eq $result -and [DateTime]::UtcNow -lt $deadline) {
        try { $result = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json }
        catch [IO.IOException] { Start-Sleep -Milliseconds 100 }
    }
    if ($null -eq $result -or -not $result.Passed -or -not $result.SessionLockObserved -or -not $result.SessionUnlockObserved) {
        throw "Windows session lock verification did not pass. Inspect $reportPath"
    }
    Write-Host "Verified Win+L and a vault that stays locked after Windows unlock. Evidence: $reportPath"
    Write-Host 'Close the verification window when finished.'
} finally { $process.Dispose() }
