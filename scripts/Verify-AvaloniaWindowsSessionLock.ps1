param(
    [Parameter(Mandatory)][string]$Executable,
    [string]$ResultsDirectory = 'artifacts/verification/windows-session-lock'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Run this interactive check on Windows.' }
$application = (Resolve-Path -LiteralPath $Executable).ProviderPath
$assemblyPath = Join-Path (Split-Path -Parent $application) 'SQLitePass.Desktop.dll'
if ([IO.Path]::GetFileName($application) -notin @('SQLitePass.exe', 'SQLitePass.Desktop.exe') -or
    -not (Test-Path -LiteralPath $assemblyPath -PathType Leaf) -or
    [Reflection.AssemblyName]::GetAssemblyName($assemblyPath).Name -cne 'SQLitePass.Desktop') {
    throw 'Choose the Avalonia candidate executable with SQLitePass.Desktop.dll beside it. The legacy WPF app cannot run this diagnostic.'
}
$directory = New-Item -ItemType Directory -Path (Join-Path $ResultsDirectory ([guid]::NewGuid().ToString('N')))
$reportPath = Join-Path $directory.FullName 'session-lock.json'
$receipt = [ordered]@{
    Executable = $application
    ExecutableSHA256 = (Get-FileHash -LiteralPath $application -Algorithm SHA256).Hash
    ApplicationAssemblySHA256 = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash
    ApplicationVersion = [Reflection.AssemblyName]::GetAssemblyName($assemblyPath).Version.ToString()
    StartedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    ResultFile = 'session-lock.json'
}
$receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory.FullName 'execution.json') -Encoding utf8
$start = [Diagnostics.ProcessStartInfo]::new($application)
$start.UseShellExecute = $false
$start.ArgumentList.Add('--verify-win-session-lock')
$start.ArgumentList.Add($reportPath)
Write-Host 'The app will open a synthetic vault and Settings dialog in a temporary profile.'
Write-Host 'While Settings is open, press Win+L and sign in to Windows yourself.'
Write-Host 'The app will then start a cancellable operation. Press Win+L again and sign in again.'
$process = [Diagnostics.Process]::Start($start)
try {
    $deadline = [DateTime]::UtcNow.AddMinutes(8)
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
    if ($null -eq $result -or -not $result.Passed -or -not $result.SessionLockObserved -or -not $result.SessionUnlockObserved -or
        -not $result.DialogLockObserved -or -not $result.DialogClosedAfterLock -or -not $result.DialogUnlockObserved -or
        -not $result.OperationLockObserved -or -not $result.OperationCancelled -or -not $result.OperationUnlockObserved) {
        throw "Windows session lock verification did not pass. Inspect $reportPath"
    }
    if ((Get-FileHash -LiteralPath $application -Algorithm SHA256).Hash -ne $receipt.ExecutableSHA256 -or
        (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash -ne $receipt.ApplicationAssemblySHA256) {
        throw 'The candidate changed during verification; repeat with an unchanged build.'
    }
    Write-Host "Verified Win+L closed the open dialog, cancelled an active operation and kept the synthetic vault locked after both Windows unlocks. Evidence: $reportPath"
    Write-Host 'Close the verification window when finished.'
} finally { $process.Dispose() }
