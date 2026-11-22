param(
    [Parameter(Mandatory)][ValidateSet('win-x64','linux-x64')][string]$Runtime,
    [switch]$VerifyNativeLaunch,
    [switch]$VerifyDesktopIntegrations
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).ProviderPath
Push-Location -LiteralPath $root
try {
    if ($VerifyDesktopIntegrations -and (-not $VerifyNativeLaunch -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted')) {
        throw 'The desktop integration probes require an isolated GitHub-hosted desktop.'
    }
    if (($Runtime -eq 'win-x64' -and -not $IsWindows) -or ($Runtime -eq 'linux-x64' -and -not $IsLinux)) {
        throw "Package $Runtime on its native operating system."
    }
    $project = 'SQLitePass.Desktop/SQLitePass.Desktop.csproj'
    $version = & dotnet msbuild $project -nologo -getProperty:Version
    if ($LASTEXITCODE -ne 0 -or $version -notmatch '^\d+\.\d+\.\d+$') { throw 'Cannot resolve the shared application version.' }
    $commit = & git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve the source commit.' }
    $tree = & git rev-parse 'HEAD^{tree}'
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve the source tree.' }
    $sourceChanges = @(& git status --porcelain)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the source tree.' }

    $candidateId = "$version-$([Guid]::NewGuid().ToString('N'))"
    $publish = Join-Path $root "artifacts/avalonia/$candidateId/publish"
    $release = Join-Path $root "artifacts/avalonia/$candidateId/packages"
    dotnet publish $project -c Release -r $Runtime --self-contained -o $publish -p:RestoreLockedMode=true -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Avalonia publish failed.' }
    $nugetRoot = & dotnet msbuild $project -nologo -getProperty:NuGetPackageRoot
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $nugetRoot -PathType Container)) { throw 'Cannot resolve the NuGet package cache.' }
    & (Join-Path $PSScriptRoot 'Copy-AvaloniaNotices.ps1') -PublishDirectory $publish -NuGetRoot $nugetRoot

    $channel = if ($IsWindows) { 'win' } else { 'linux' }
    $executable = if ($IsWindows) { 'SQLitePass.Desktop.exe' } else { 'SQLitePass.Desktop' }
    $icon = if ($IsWindows) { 'app.ico' } else { 'app_icon.png' }
    $distributionPattern = if ($IsWindows) { '*-Setup.exe' } else { '*.AppImage' }
    $packageId = 'SQLitePassAvaloniaPreview'

    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'Velopack tool restore failed.' }
    dotnet tool run vpk -- pack --skip-updates -u $packageId -v $version -p $publish -e $executable -o $release `
        -r $Runtime -c $channel -i (Join-Path $root "SQLitePass.UI/Resources/$icon") --packTitle 'SQLitePass Avalonia (verification)'
    if ($LASTEXITCODE -ne 0) { throw 'Velopack packaging failed.' }

    $feedName = "releases.$channel.json"
    $feed = Get-Content -LiteralPath (Join-Path $release $feedName) -Raw | ConvertFrom-Json
    $assets = @($feed.Assets)
    if ($assets.Count -ne 1 -or $assets[0].Version -ne $version -or $assets[0].PackageId -ne $packageId -or $assets[0].Type -ne 'Full') {
        throw "Unexpected update feed for $Runtime."
    }
    $asset = $assets[0]
    if ([IO.Path]::GetFileName($asset.FileName) -ne $asset.FileName) { throw 'Invalid update asset name.' }
    $fullPackage = Get-Item -LiteralPath (Join-Path $release $asset.FileName)
    if ($asset.Size -ne $fullPackage.Length -or $asset.SHA256 -ne (Get-FileHash -LiteralPath $fullPackage.FullName -Algorithm SHA256).Hash) {
        throw 'The update feed does not match the full package.'
    }
    $distributions = @(Get-ChildItem -LiteralPath $release -File -Filter $distributionPattern)
    if ($distributions.Count -ne 1) { throw 'Expected exactly one native distribution.' }
    $distribution = $distributions[0]
    $header = [byte[]]::new(12)
    $stream = $distribution.OpenRead()
    try { $stream.ReadExactly($header) } finally { $stream.Dispose() }
    if ($IsWindows -and [Convert]::ToHexString($header, 0, 2) -ne '4D5A') { throw 'Invalid Windows installer header.' }
    if ($IsLinux -and ([Convert]::ToHexString($header, 0, 4) -ne '7F454C46' -or
        [Convert]::ToHexString($header, 8, 3) -ne '414902')) { throw 'Invalid AppImage header.' }

    $archive = [IO.Compression.ZipFile]::OpenRead($fullPackage.FullName)
    try {
        if ($IsWindows) {
            foreach ($required in @($executable, 'LICENSE', 'SECURITY.md', 'THIRD_PARTY_LICENSES/dependency-metadata.json')) {
                if ($null -eq $archive.GetEntry("lib/app/$required")) { throw "Windows update package is missing $required." }
            }
        } else {
            $images = @($archive.Entries | Where-Object FullName -Match '^lib/app/[^/]+\.AppImage$')
            if ($images.Count -ne 1) { throw 'The Linux update must contain one AppImage.' }
            $image = $images[0].Open()
            try { $embeddedHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($image)) }
            finally { $image.Dispose() }
            if ($embeddedHash -ne (Get-FileHash -LiteralPath $distribution.FullName -Algorithm SHA256).Hash) {
                throw 'The Linux update and downloadable AppImage differ.'
            }
        }
    } finally { $archive.Dispose() }

    if ($VerifyNativeLaunch) {
        $probe = [Diagnostics.ProcessStartInfo]::new()
        $probe.UseShellExecute = $false
        $probe.CreateNoWindow = $true
        if ($IsLinux) {
            & chmod +x -- $distribution.FullName
            if ($LASTEXITCODE -ne 0) { throw 'Cannot mark the AppImage executable.' }
            if ($VerifyDesktopIntegrations) {
                $probe.FileName = 'dbus-run-session'
                $probe.ArgumentList.Add('--')
                $probe.ArgumentList.Add('xvfb-run')
                $probe.Environment['SQLITEPASS_TRAY_PROBE_WATCHER'] = Join-Path $PSScriptRoot 'Verify-LinuxTrayWatcher.py'
            } else { $probe.FileName = 'xvfb-run' }
            $probe.ArgumentList.Add('-a')
            if ($VerifyDesktopIntegrations) {
                $probe.ArgumentList.Add('bash')
                $probe.ArgumentList.Add((Join-Path $PSScriptRoot 'Start-LinuxVerificationDesktop.sh'))
            }
            $probe.ArgumentList.Add($distribution.FullName)
            $probe.Environment['APPIMAGE_EXTRACT_AND_RUN'] = '1'
        } else {
            $probe.FileName = Join-Path $publish $executable
        }
        $probe.ArgumentList.Add($(if ($VerifyDesktopIntegrations) { '--verify-desktop' } else { '--verify-startup' }))
        $probe.RedirectStandardError = $true
        $process = [Diagnostics.Process]::Start($probe)
        try {
            $errors = $process.StandardError.ReadToEndAsync()
            if (-not $process.WaitForExit(75000)) {
                $process.Kill($true)
                throw 'The native Avalonia startup probe timed out.'
            }
            if ($process.ExitCode -ne 0) {
                throw "The native Avalonia probe exited with $($process.ExitCode). $($errors.GetAwaiter().GetResult())"
            }
        } finally { $process.Dispose() }
    }
    $evidence = [ordered]@{
        SourceCommit = $commit; SourceTree = $tree; SourceDirty = ($sourceChanges.Count -gt 0)
        Version = $version; Runtime = $Runtime; Channel = $channel
        Distribution = $distribution.Name; Feed = $feedName; UpdatePackage = $asset.FileName
        NativeStartupVerified = [bool]$VerifyNativeLaunch
        NativeClipboardVerified = [bool]$VerifyDesktopIntegrations
        NativeTrayVerified = [bool]$VerifyDesktopIntegrations
        Assets = @(Get-ChildItem -LiteralPath $release -File | ForEach-Object {
            [ordered]@{ Name = $_.Name; SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
        })
    }
    $evidence | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $release 'candidate-verification.json') -Encoding utf8
    if ($env:GITHUB_OUTPUT) { "candidate_path=artifacts/avalonia/$candidateId/packages" | Add-Content -LiteralPath $env:GITHUB_OUTPUT }
    Write-Output "Verified $Runtime packaging and feed at $release"
}
finally { Pop-Location }
