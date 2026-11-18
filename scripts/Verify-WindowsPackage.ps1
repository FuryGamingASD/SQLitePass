param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+-[a-f0-9]{32}$')]
    [string]$BuildId,
    [switch]$CheckRunningApp
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).ProviderPath
$output = Join-Path $projectRoot 'artifacts\verification'
$release = Join-Path $projectRoot "artifacts\releases\$BuildId"
$applicationVersion = $BuildId.Substring(0, $BuildId.LastIndexOf('-'))
$assemblyVersion = "$applicationVersion.0"
$setup = Join-Path $release 'SQLitePass-win-Setup.exe'
$package = Join-Path $release "SQLitePass-$applicationVersion-full.nupkg"
$legacySetup = Join-Path $projectRoot 'releases\SQLitePass-win-Setup.exe'
$previousSetup = Join-Path $projectRoot 'releases\SQLitePass-1.1.2-Setup.exe'
$settingsDirectory = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'SQLitePass'
$startupKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$inputPaths = @($setup, $package, $legacySetup)
if ($CheckRunningApp) {
    if ($env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or (Test-Path -LiteralPath $settingsDirectory) -or
        (Get-ItemProperty -LiteralPath $startupKey -Name SQLitePass -ErrorAction SilentlyContinue)) {
        throw 'The live application smoke requires a clean GitHub-hosted Windows profile.'
    }
    $inputPaths += $previousSetup
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class PackageSmokeWindow
{
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, UIntPtr wParam,
        IntPtr lParam, uint flags, uint timeout, out UIntPtr result);

    public static bool IsResponsiveAndOnScreen(IntPtr window)
    {
        return window != IntPtr.Zero && IsWindowVisible(window) && !IsIconic(window)
            && MonitorFromWindow(window, 0) != IntPtr.Zero
            && SendMessageTimeout(window, 0, UIntPtr.Zero, IntPtr.Zero, 2, 1000, out _) != IntPtr.Zero;
    }
}
'@
}
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SQLitePass'
$shortcuts = @(
    (Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'SQLitePass.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'SQLitePass.lnk')
)
if (Test-Path -LiteralPath $uninstallKey) { throw 'An existing SQLitePass installation must not be changed by this test.' }
foreach ($shortcut in $shortcuts) { if (Test-Path -LiteralPath $shortcut) { throw 'An existing SQLitePass shortcut must not be overwritten.' } }
if (Get-Process -Name SQLitePass -ErrorAction SilentlyContinue) { throw 'Close existing application sessions before this isolated test.' }
foreach ($path in $inputPaths) { if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required test artifact missing: $path" } }
$checksums = Get-Content -LiteralPath (Join-Path $release 'checksums.json') -Raw | ConvertFrom-Json
foreach ($checksum in $checksums) {
    if ((Get-FileHash -LiteralPath (Join-Path $release $checksum.File) -Algorithm SHA256).Hash -ne $checksum.Hash) { throw 'Release artifact checksum mismatch.' }
}
$settings = Join-Path $settingsDirectory 'appsettings.json'
$settingsHash = if (Test-Path -LiteralPath $settings) { (Get-FileHash -LiteralPath $settings -Algorithm SHA256).Hash } else { $null }
$results = [Collections.Generic.List[object]]::new()
$runId = [guid]::NewGuid().ToString('N')
function Invoke-Native([string]$Executable, [string[]]$Arguments) {
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $Executable; $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($info)
    try {
        if (-not $process.WaitForExit(90000)) { $process.Kill($true); throw 'Installer/updater test timed out.' }
        if ($process.ExitCode -ne 0) { throw "Installer/updater exited with code $($process.ExitCode)." }
    } finally { $process.Dispose() }
}
function Assert-Version([string]$Root, [string]$Expected) {
    $assembly = Join-Path $Root 'current\SQLitePass.dll'
    if ([Reflection.AssemblyName]::GetAssemblyName($assembly).Version.ToString() -ne $Expected) { throw 'Unexpected installed application version.' }
}
function Uninstall-TestCopy([string]$Root, [string]$Label) {
    $updater = Join-Path $Root 'Update.exe'
    if (Test-Path -LiteralPath $updater) {
        Invoke-Native $updater @('--silent', '--log', (Join-Path $output ($Label + '-uninstall.log')), 'uninstall')
    }
    if (Test-Path -LiteralPath (Join-Path $Root 'current\SQLitePass.dll')) { throw 'Uninstall left the application installed.' }
    if (Test-Path -LiteralPath $uninstallKey) { throw 'Uninstall left the application registration present.' }
    foreach ($shortcut in $shortcuts) { if (Test-Path -LiteralPath $shortcut) { throw 'Uninstall left an application shortcut present.' } }
}
function New-TestInstallPath([string]$Scenario) {
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $resolved = [IO.Path]::GetFullPath((Join-Path $temp "SQLitePass-install-test-$runId-$Scenario"))
    if ([IO.Path]::GetDirectoryName($resolved) -ne [IO.Path]::TrimEndingDirectorySeparator($temp) -or
        (Test-Path -LiteralPath $resolved)) { throw 'Unsafe or occupied test installation path.' }
    return $resolved
}
function Start-TestApp([string]$Root, [string[]]$Arguments = @()) {
    $info = [Diagnostics.ProcessStartInfo]::new((Join-Path $Root 'current\SQLitePass.exe'))
    $info.UseShellExecute = $false
    $info.WorkingDirectory = Join-Path $Root 'current'
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    return [Diagnostics.Process]::Start($info)
}
function Wait-ForLoginWindow([Diagnostics.Process]$Process) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.Elapsed.TotalSeconds -lt 20) {
        $Process.Refresh()
        if ($Process.HasExited) { throw "SQLitePass exited before showing its window (exit $($Process.ExitCode))." }
        if ($Process.MainWindowTitle.StartsWith('SQLitePass', [StringComparison]::Ordinal) -and
            [PackageSmokeWindow]::IsResponsiveAndOnScreen($Process.MainWindowHandle)) { return }
        Start-Sleep -Milliseconds 200
    }
    throw 'The installed SQLitePass unlock window did not become visible and responsive.'
}
function Assert-CleanAppExit([Diagnostics.Process]$Process, [string]$Operation) {
    if (-not $Process.WaitForExit(10000)) { throw "SQLitePass did not exit during $Operation." }
    if ($Process.ExitCode -ne 0) { throw "SQLitePass exited with code $($Process.ExitCode) during $Operation." }
}
function Invoke-RunningAppScenarios {
    New-Item -ItemType Directory -Path $settingsDirectory -Force | Out-Null
    $preservedFile = Join-Path $settingsDirectory "package-smoke-$runId.txt"
    Set-Content -LiteralPath $preservedFile -Value 'Synthetic user data marker' -Encoding utf8
    $preservedHash = (Get-FileHash -LiteralPath $preservedFile -Algorithm SHA256).Hash
    foreach ($scenario in @('running-fresh', 'running-upgrade')) {
        Write-Output "Testing the installed application in scenario $scenario."
        $resolved = New-TestInstallPath $scenario
        $started = [Collections.Generic.List[Diagnostics.Process]]::new()
        $isUpgrade = $scenario -eq 'running-upgrade'
        try {
            $inputSetup = if ($isUpgrade) { $previousSetup } else { $setup }
            Invoke-Native $inputSetup @('--silent', '--installto', $resolved, '--log', (Join-Path $output "$scenario-install.log"))
            Assert-Version $resolved $(if ($isUpgrade) { '1.1.2.0' } else { $assemblyVersion })
            $primary = Start-TestApp $resolved
            $started.Add($primary)
            Wait-ForLoginWindow $primary
            if ($isUpgrade) {
                Invoke-Native (Join-Path $resolved 'Update.exe') @('--silent', '--log', (Join-Path $output "$scenario-apply.log"), 'apply', '--package', $package, '--norestart')
                Assert-CleanAppExit $primary 'update'
                Assert-Version $resolved $assemblyVersion
                $primary = Start-TestApp $resolved
                $started.Add($primary)
                Wait-ForLoginWindow $primary
            } else {
                if (-not $primary.CloseMainWindow()) { throw 'Could not close the first unlock window.' }
                Assert-CleanAppExit $primary 'closing the unlock window'
                $primary = Start-TestApp $resolved @('--minimized')
                $started.Add($primary)
                Start-Sleep -Seconds 2
                $primary.Refresh()
                if ($primary.HasExited -or $primary.MainWindowHandle -ne [IntPtr]::Zero) {
                    throw 'Tray startup did not keep the first process running without a main window.'
                }
                $secondary = Start-TestApp $resolved
                $started.Add($secondary)
                Assert-CleanAppExit $secondary 'second-instance activation'
                Wait-ForLoginWindow $primary
            }
            # Verify ownership in both directions using the disposable runner's registry.
            $startupExe = if ($isUpgrade) { Join-Path ([IO.Path]::GetTempPath()) 'OtherInstallation\SQLitePass.exe' } else { Join-Path $resolved 'current\SQLitePass.exe' }
            $startupValue = '"' + $startupExe + '" --minimized'
            if (-not (Test-Path -LiteralPath $startupKey)) { New-Item -Path $startupKey | Out-Null }
            Set-ItemProperty -LiteralPath $startupKey -Name SQLitePass -Value $startupValue
            Uninstall-TestCopy $resolved $scenario
            # Velopack stops running processes before invoking the uninstall hook.
            if (-not $primary.WaitForExit(10000)) { throw 'Uninstall left SQLitePass running.' }
            $observedStartup = Get-ItemPropertyValue -LiteralPath $startupKey -Name SQLitePass -ErrorAction SilentlyContinue
            $expectedStartup = if ($isUpgrade) { $startupValue } else { $null }
            if ($observedStartup -ne $expectedStartup) { throw 'Uninstall did not preserve startup registration ownership.' }
            if ($isUpgrade) { Remove-ItemProperty -LiteralPath $startupKey -Name SQLitePass }
            if ((Get-FileHash -LiteralPath $preservedFile -Algorithm SHA256).Hash -ne $preservedHash) {
                throw 'The app lifecycle changed the synthetic user data marker.'
            }
            $results.Add([ordered]@{
                Scenario = $scenario
                Passed = $true
                InstalledVersion = $assemblyVersion
                ResponsiveOnScreenUnlockWindow = $true
                WakeFromTrayViaSecondInstance = -not $isUpgrade
                UpgradeFrom = $(if ($isUpgrade) { '1.1.2' } else { $null })
                CleanExitWhileUpdating = $isUpgrade
                AppStoppedByUninstall = $true
                StartupRegistryOwnershipVerified = $true
                SyntheticUserDataPreserved = $true
                AppStarted = $true
                RealVaultOpened = $false
            })
        } finally {
            foreach ($process in $started) {
                try {
                    if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit(5000) | Out-Null }
                } finally { $process.Dispose() }
            }
            if (Test-Path -LiteralPath (Join-Path $resolved 'current\SQLitePass.dll')) {
                Uninstall-TestCopy $resolved "$scenario-cleanup"
            }
        }
    }
}
try {
    foreach ($scenario in @('fresh', 'upgrade')) {
        $resolved = New-TestInstallPath $scenario
        Write-Output "Testing $scenario installation in an isolated temporary directory."
        try {
            $inputSetup = if ($scenario -eq 'fresh') { $setup } else { $legacySetup }
            Invoke-Native $inputSetup @('--silent', '--installto', $resolved, '--log', (Join-Path $output ($scenario + '-install.log')))
            Assert-Version $resolved $(if ($scenario -eq 'fresh') { $assemblyVersion } else { '1.0.2.0' })
            if ($scenario -eq 'upgrade') {
                Invoke-Native (Join-Path $resolved 'Update.exe') @('--silent', '--log', (Join-Path $output 'upgrade-apply.log'), 'apply', '--package', $package, '--norestart')
                Assert-Version $resolved $assemblyVersion
            }
            foreach ($required in @('LICENSE', 'SECURITY.md', 'THIRD_PARTY_NOTICES.md', 'THIRD_PARTY_LICENSES\dependency-metadata.json')) {
                if (-not (Test-Path -LiteralPath (Join-Path (Join-Path $resolved 'current') $required))) { throw 'An installed license/policy file is missing.' }
            }
            $runtime = Get-Content -LiteralPath (Join-Path $resolved 'current\SQLitePass.runtimeconfig.json') -Raw | ConvertFrom-Json
            foreach ($framework in $runtime.runtimeOptions.includedFrameworks) { if ($framework.version -ne '10.0.15') { throw 'Unexpected installed runtime.' } }
            $results.Add([ordered]@{ Scenario=$scenario; Passed=$true; InstalledVersion=$assemblyVersion; Runtime='10.0.15'; AppStarted=$false; RealVaultOpened=$false; LocalPackageApplied=($scenario -eq 'upgrade') })
        } finally { Uninstall-TestCopy $resolved $scenario }
    }
    $afterHash = if (Test-Path -LiteralPath $settings) { (Get-FileHash -LiteralPath $settings -Algorithm SHA256).Hash } else { $null }
    if ($afterHash -ne $settingsHash) { throw 'Application preferences changed during installer testing.' }
    if ($CheckRunningApp) { Invoke-RunningAppScenarios }
    [ordered]@{ BuildId=$BuildId; Passed=$true; SilentInstall=$true; AutomaticAppLaunch=$false; ExplicitAppLaunch=[bool]$CheckRunningApp; RealVaultOpened=$false; SettingsUnchangedDuringSilentInstallerSmoke=$true; Uninstalled=$true; Scenarios=@($results.ToArray()); LegacySetupSha256=(Get-FileHash -LiteralPath $legacySetup -Algorithm SHA256).Hash } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'windows-package-smoke.json') -Encoding utf8
    Write-Output 'Fresh install, local update from 1.0.2, and uninstall passed.'
} catch {
    [ordered]@{ BuildId=$BuildId; Passed=$false; ErrorType=$_.Exception.GetType().FullName; CompletedScenarios=@($results.ToArray()) } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'windows-package-smoke.json') -Encoding utf8
    throw
}
