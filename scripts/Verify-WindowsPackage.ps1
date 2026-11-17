param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+-[a-f0-9]{32}$')]
    [string]$BuildId
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
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SQLitePass'
$shortcuts = @(
    (Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'SQLitePass.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'SQLitePass.lnk')
)
if (Test-Path -LiteralPath $uninstallKey) { throw 'An existing SQLitePass installation must not be changed by this test.' }
foreach ($shortcut in $shortcuts) { if (Test-Path -LiteralPath $shortcut) { throw 'An existing SQLitePass shortcut must not be overwritten.' } }
if (Get-Process -Name SQLitePass -ErrorAction SilentlyContinue) { throw 'Close existing application sessions before this isolated test.' }
foreach ($path in @($setup, $package, $legacySetup)) { if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required test artifact missing: $path" } }
$checksums = Get-Content -LiteralPath (Join-Path $release 'checksums.json') -Raw | ConvertFrom-Json
foreach ($checksum in $checksums) {
    if ((Get-FileHash -LiteralPath (Join-Path $release $checksum.File) -Algorithm SHA256).Hash -ne $checksum.Hash) { throw 'Release artifact checksum mismatch.' }
}
$settings = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'SQLitePass\appsettings.json'
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
try {
    foreach ($scenario in @('fresh', 'upgrade')) {
        $installRoot = Join-Path ([IO.Path]::GetTempPath()) ("SQLitePass-install-test-$runId-$scenario")
        $resolved = [IO.Path]::GetFullPath($installRoot)
        if (-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase) -or
            -not [IO.Path]::GetFileName($resolved).StartsWith("SQLitePass-install-test-$runId-") -or (Test-Path -LiteralPath $resolved)) { throw 'Unsafe or occupied test installation path.' }
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
    [ordered]@{ BuildId=$BuildId; Passed=$true; SilentInstall=$true; AutomaticAppLaunch=$false; RealVaultOpened=$false; UserSettingsUnchanged=$true; Uninstalled=$true; Scenarios=@($results.ToArray()); LegacySetupSha256=(Get-FileHash -LiteralPath $legacySetup -Algorithm SHA256).Hash } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'windows-package-smoke.json') -Encoding utf8
    Write-Output 'Fresh install, local update from 1.0.2, and uninstall passed without launching the application.'
} catch {
    [ordered]@{ BuildId=$BuildId; Passed=$false; ErrorType=$_.Exception.GetType().FullName; CompletedScenarios=@($results.ToArray()) } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'windows-package-smoke.json') -Encoding utf8
    throw
}
