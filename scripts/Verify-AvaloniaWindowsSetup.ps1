param(
    [Parameter(Mandatory)][string]$CandidateDirectory,
    [ValidatePattern('^[0-9a-fA-F]{64}$')][string]$ExistingInstallerSha256
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'The Windows installation probe requires a disposable GitHub-hosted Windows profile.'
}
$testProfileDirectory = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'SQLitePass'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SQLitePass'
$startupRegistryPath = 'Software\Microsoft\Windows\CurrentVersion\Run'
$startupKey = "HKCU:\$startupRegistryPath"
function Get-StartupEntry {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($startupRegistryPath)
    if ($null -eq $key) { return }
    try { $key.GetValue('SQLitePass') }
    finally { $key.Dispose() }
}
$shortcuts = @(
    (Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'SQLitePass.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'SQLitePass.lnk')
)
if (Test-Path -LiteralPath (@($testProfileDirectory, $uninstallKey) + $shortcuts) -ErrorAction SilentlyContinue | Where-Object { $_ }) {
    throw 'The Windows installation probe requires an unused SQLitePass profile and installation.'
}
if ($null -ne (Get-StartupEntry)) { throw 'An existing startup entry cannot be changed.' }
if (Get-Process SQLitePass -ErrorAction SilentlyContinue) { throw 'A SQLitePass process is already running.' }

$candidate = (Resolve-Path -LiteralPath $CandidateDirectory).ProviderPath
$manifestPath = Join-Path $candidate 'candidate-verification.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.PackageId -cne 'SQLitePass' -or $manifest.Runtime -cne 'win-x64' -or
    $manifest.Distribution -cne 'SQLitePass-win-Setup.exe' -or $manifest.Version -notmatch '^\d+\.\d+\.\d+$' -or
    $manifest.UpdatePackage -cnotmatch '^SQLitePass-[0-9.]+-full\.nupkg$') {
    throw 'The installation probe requires the Windows release package identity.'
}
$setup = Join-Path $candidate $manifest.Distribution
$updatePackage = Join-Path $candidate $manifest.UpdatePackage
$previousHashes = [ordered]@{
    '1.1.3' = 'F9782717230A6AA1123F8E4BFB13DB02A9DB3202C3E6B2214E20926AA5D9C2B5'
    '1.2.0' = '524DC476347B508FC1A9E45F48E29F5688FE0FCB81C8E1338B842B1D6FD9E847'
    '1.2.1' = '63AD97E6BCA08D47295A451D9589358FE9E1CC7458C3D12E7A693B6AF77AD07B'
}
if ([version]$manifest.Version -le [version]'1.2.1') { throw 'The candidate must be newer than the public Avalonia baseline.' }
if ([version]$manifest.Version -gt [version]'1.2.2') {
    $previousHashes['1.2.2'] = 'DC25A95A6092044CC33271F3F524B5BD2AA36A5E2E744FD4B62C3CF285A18A36'
}
if ($ExistingInstallerSha256) { $previousHashes[$manifest.Version] = $ExistingInstallerSha256.ToUpperInvariant() }
$isolated = [IO.Directory]::CreateTempSubdirectory('SQLitePass-Avalonia-package-').FullName
$logs = (New-Item -ItemType Directory -Path 'artifacts/verification/avalonia-windows-setup' -Force).FullName
$previousSetups = @{}
foreach ($version in $previousHashes.Keys) {
    $previousSetup = Join-Path $isolated "SQLitePass-$version-Setup.exe"
    Invoke-WebRequest -Uri "https://github.com/FuryGamingASD/SQLitePass/releases/download/v$version/SQLitePass-win-Setup.exe" -OutFile $previousSetup
    if ((Get-FileHash -LiteralPath $previousSetup).Hash -cne $previousHashes[$version]) { throw "The public $version installer checksum differs." }
    $previousSetups[$version] = $previousSetup
}

function Start-PackageProcess([string]$File, [string[]]$Arguments) {
    $start = [Diagnostics.ProcessStartInfo]::new($File)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    [Diagnostics.Process]::Start($start)
}

function Invoke-PackageCommand([string]$File, [string[]]$Arguments) {
    $process = Start-PackageProcess $File $Arguments
    try {
        if (-not $process.WaitForExit(90000)) {
            $process.Kill($true)
            throw 'The Windows package command timed out.'
        }
        if ($process.ExitCode -ne 0) { throw "The Windows package command exited with $($process.ExitCode)." }
    } finally { $process.Dispose() }
}

function Assert-WindowWakesAndCloses([string]$Path) {
    $mainExe = Join-Path $Path 'current/SQLitePass.exe'
    $primary = Start-PackageProcess $mainExe @('--minimized')
    try {
        Start-Sleep -Seconds 2
        $primary.Refresh()
        if ($primary.HasExited -or $primary.MainWindowHandle -ne [IntPtr]::Zero) { throw 'Start minimized did not leave a tray instance.' }
        Invoke-PackageCommand $mainExe @()
        $deadline = [Diagnostics.Stopwatch]::StartNew()
        while ($true) {
            $primary.Refresh()
            if ($primary.HasExited -or $deadline.Elapsed.TotalSeconds -gt 15) { throw 'Second-instance activation did not show the first window.' }
            if ($primary.MainWindowHandle -ne [IntPtr]::Zero -and $primary.MainWindowTitle -eq 'SQLitePass') { break }
            Start-Sleep -Milliseconds 100
        }
        Invoke-PackageCommand $mainExe @('--veloapp-obsolete', $manifest.Version)
        if (-not $primary.WaitForExit(10000) -or $primary.ExitCode -ne 0) { throw 'The pre-update hook did not shut down the running Avalonia window.' }
        Write-Output 'Verified Windows tray startup, second-instance activation and a clean pre-update shutdown.'
    } finally {
        if (-not $primary.HasExited) {
            $primary.Kill($true)
            $primary.WaitForExit(5000) | Out-Null
        }
        $primary.Dispose()
    }
}

function Assert-AvaloniaStartup([string]$Path) {
    $payload = Join-Path $Path 'current/SQLitePass.Desktop.dll'
    if ([Reflection.AssemblyName]::GetAssemblyName($payload).Version.ToString() -cne "$($manifest.Version).0") {
        throw 'The installed Avalonia assembly version does not match the verified package.'
    }
    Invoke-PackageCommand (Join-Path $Path 'current/SQLitePass.exe') @('--verify-startup')
}

New-Item -ItemType Directory -Path $testProfileDirectory | Out-Null
$marker = Join-Path $testProfileDirectory 'isolated-verification.txt'
Set-Content -LiteralPath $marker -Value 'Synthetic user data outside the installation' -Encoding utf8
$markerHash = (Get-FileHash -LiteralPath $marker).Hash
foreach ($previousVersion in @('') + @($previousHashes.Keys)) {
    $sameVersion = $previousVersion -ceq $manifest.Version
    $scenario = if ($sameVersion) { 'manual-reinstall' } elseif ($previousVersion) { "upgrade-$previousVersion" } else { 'fresh' }
    $installation = Join-Path $isolated $scenario
    $updater = Join-Path $installation 'Update.exe'
    $inputSetup = if ($previousVersion) { $previousSetups[$previousVersion] } else { $setup }
    $startupApp = if ($previousVersion) { Join-Path $isolated 'OtherInstallation/SQLitePass.exe' } else { Join-Path $installation 'current/SQLitePass.exe' }
    $startupEntry = '"' + $startupApp + '" --minimized'
    try {
        Invoke-PackageCommand $inputSetup @('--silent', '--installto', $installation, '--log', (Join-Path $logs "$scenario-install.log"))
        if ($previousVersion) {
            $oldPayloadName = if ($previousVersion -eq '1.1.3') { 'SQLitePass.dll' } else { 'SQLitePass.Desktop.dll' }
            $oldPayload = Join-Path $installation "current/$oldPayloadName"
            if ([Reflection.AssemblyName]::GetAssemblyName($oldPayload).Version.ToString() -cne "$previousVersion.0") {
                throw 'The public installation did not report the expected version.'
            }
            if ($sameVersion) {
                $before = (Get-FileHash -LiteralPath $oldPayload).Hash
                $archive = [IO.Compression.ZipFile]::OpenRead($updatePackage)
                try {
                    $payload = $archive.GetEntry('lib/app/SQLitePass.Desktop.dll')
                    if ($null -eq $payload) { throw 'The candidate lacks the Avalonia payload.' }
                    $stream = $payload.Open()
                    try { $expected = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
                    finally { $stream.Dispose() }
                } finally { $archive.Dispose() }
                if ($before -ceq $expected) { throw 'The published and candidate applications have identical bytes.' }
                Invoke-PackageCommand $setup @('--silent', '--installto', $installation, '--log', (Join-Path $logs 'manual-reinstall-apply.log'))
                if ((Get-FileHash -LiteralPath $oldPayload).Hash -cne $expected) {
                    throw 'Running Setup again did not replace the same-version application files.'
                }
            } else {
                Invoke-PackageCommand $updater @('--silent', '--log', (Join-Path $logs "$scenario-apply.log"), 'apply', '--package', $updatePackage, '--norestart')
            }
            if ($previousVersion -eq '1.1.3' -and (Test-Path -LiteralPath $oldPayload)) {
                throw 'The update left the WPF assembly in the current application directory.'
            }
        }
        Assert-AvaloniaStartup $installation
        if ($scenario -eq 'fresh') { Assert-WindowWakesAndCloses $installation }
        if (-not (Test-Path -LiteralPath $startupKey)) { New-Item -Path $startupKey | Out-Null }
        Set-ItemProperty -LiteralPath $startupKey -Name SQLitePass -Value $startupEntry
        Write-Output "Verified Windows $scenario with the installed SQLitePass.exe entry point."
    } finally {
        if (Test-Path -LiteralPath $updater) {
            Invoke-PackageCommand $updater @('--silent', '--log', (Join-Path $logs "$scenario-uninstall.log"), 'uninstall')
        }
    }
    if (Test-Path -LiteralPath $uninstallKey) { throw 'Uninstall left the SQLitePass registration.' }
    foreach ($shortcut in $shortcuts) { if (Test-Path -LiteralPath $shortcut) { throw 'Uninstall left a SQLitePass shortcut.' } }
    if (Test-Path -LiteralPath (Join-Path $installation 'current/SQLitePass.exe')) { throw 'Uninstall left the app installed.' }
    if ((Get-FileHash -LiteralPath $marker).Hash -cne $markerHash) { throw 'The package lifecycle changed synthetic data outside the installation.' }
    $observed = Get-StartupEntry
    if ($previousVersion) {
        if ($observed -cne $startupEntry) { throw 'Uninstall changed a startup entry belonging to a different installation.' }
        Remove-ItemProperty -LiteralPath $startupKey -Name SQLitePass
    } elseif ($null -ne $observed) { throw 'Uninstall left its own Windows startup entry.' }
}

$manifest | Add-Member -NotePropertyName InstallerVerified -NotePropertyValue $true
$manifest | Add-Member -NotePropertyName InstallerUpgradeFrom -NotePropertyValue @($previousHashes.Keys | Where-Object { $_ -cne $manifest.Version })
if ($ExistingInstallerSha256) { $manifest | Add-Member -NotePropertyName InstallerSameVersionReinstallVerified -NotePropertyValue $true }
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8
