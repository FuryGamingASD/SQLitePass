param(
    [Parameter(Mandatory)][string]$Windows,
    [Parameter(Mandatory)][string]$Linux,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateSet('preview','release')][string]$PackageIdentity = 'preview',
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$Commit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$packageId = if ($PackageIdentity -eq 'release') { 'SQLitePass' } else { 'SQLitePassAvaloniaPreview' }
if ($PackageIdentity -eq 'release' -and -not $Version) { throw 'Release assembly requires the planned source version.' }
$platforms = @(
    @{ Directory = $Windows; Runtime = 'win-x64'; Channel = 'win'; Extension = '.exe' },
    @{ Directory = $Linux; Runtime = 'linux-x64'; Channel = 'linux'; Extension = '.AppImage' }
)
$files = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
$checksums = [Collections.Generic.List[object]]::new()
$sourceTree = & git rev-parse "$Commit^{tree}"
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve the candidate commit tree.' }
$versions = @()
$provenance = @()
foreach ($platform in $platforms) {
    $directory = (Resolve-Path -LiteralPath $platform.Directory).ProviderPath
    $manifestName = 'candidate-verification.json'
    $manifest = Get-Content -LiteralPath (Join-Path $directory $manifestName) -Raw | ConvertFrom-Json
    if ($manifest.SourceCommit -ne $Commit -or $manifest.SourceTree -ne $sourceTree -or $manifest.SourceDirty -ne $false -or
        $manifest.NativeStartupVerified -ne $true -or $manifest.NativeClipboardVerified -ne $true -or
        $manifest.NativeTrayVerified -ne $true -or $manifest.NativeAutoTypeVerified -ne $true -or
        $manifest.Runtime -cne $platform.Runtime -or $manifest.PackageId -cne $packageId -or
        $manifest.Channel -cne $platform.Channel -or $manifest.Version -notmatch '^\d+\.\d+\.\d+$' -or
        ($Version -and $manifest.Version -cne $Version) -or
        $manifest.Feed -cne "releases.$($platform.Channel).json" -or
        [IO.Path]::GetExtension($manifest.Distribution) -cne $platform.Extension) {
        throw "Unexpected package provenance for $($platform.Runtime)."
    }
    $sessionVerified = if ($platform.Runtime -eq 'win-x64') { $manifest.NativeWindowsCaptureAffinityVerified } else { $manifest.NativeLinuxLogindTransportVerified }
    if ($sessionVerified -ne $true) { throw "Native desktop session evidence is missing for $($platform.Runtime)." }
    if ($platform.Runtime -eq 'win-x64' -and $manifest.NativeWindowsWorkspaceVerified -ne $true) {
        throw 'The Windows candidate requires native workspace and global Auto-Type verification.'
    }
    if ($platform.Runtime -eq 'linux-x64' -and
        ($manifest.NativeXWaylandClipboardVerified -ne $true -or $manifest.NativeXWaylandTrayVerified -ne $true -or
            $manifest.NativeXWaylandAutoTypeVerified -ne $true -or $manifest.NativeXWaylandLogindTransportVerified -ne $true)) {
        throw 'The Linux candidate requires XWayland clipboard, tray, Auto-Type and session transport verification.'
    }
    if ($PackageIdentity -eq 'release' -and $platform.Runtime -eq 'win-x64') {
        if ($manifest.InstallerVerified -ne $true -or '1.1.3' -cnotin $manifest.InstallerUpgradeFrom -or
            '1.2.0' -cnotin $manifest.InstallerUpgradeFrom -or '1.2.1' -cnotin $manifest.InstallerUpgradeFrom -or
            '1.2.2' -cnotin $manifest.InstallerUpgradeFrom) {
            throw 'The Windows setup and upgrades from 1.1.3, 1.2.0, 1.2.1 and 1.2.2 require native verification.'
        }
    }
    if ($PackageIdentity -eq 'release' -and $platform.Runtime -eq 'linux-x64' -and
        ($manifest.AppImageUpgradeVerified -ne $true -or $manifest.AppImageUpgradeFrom -cne '1.2.2')) {
        throw 'The Linux release requires AppImage upgrade verification.'
    }
    if ($PackageIdentity -eq 'release' -and $manifest.SignedUpgradeVerified -ne $true) {
        throw "The release requires a signed native update rehearsal for $($platform.Runtime)."
    }
    $versions += $manifest.Version
    $provenance += $manifest
    $manifestFiles = @($manifest.Assets | ForEach-Object Name)
    $actualFiles = @(Get-ChildItem -LiteralPath $directory -Force | Where-Object Name -ne $manifestName | ForEach-Object Name)
    if (@(Compare-Object $manifestFiles $actualFiles -CaseSensitive).Count -ne 0) { throw 'Candidate asset set differs from its manifest.' }
    foreach ($required in @($manifest.Feed, $manifest.Distribution, $manifest.UpdatePackage)) {
        if ($required -cnotin $manifestFiles) { throw "Missing candidate asset: $required" }
    }
    foreach ($asset in $manifest.Assets) {
        if ($asset.Name -cnotmatch '^[a-zA-Z0-9][a-zA-Z0-9._-]+$' -or
            $asset.Name -in @($manifestName, 'bundle-verification.json', 'checksums.json', 'release-provenance.json')) {
            throw 'Invalid candidate asset filename.'
        }
        $path = Join-Path $directory $asset.Name
        if ($asset.SHA256 -ne (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash) { throw "Candidate asset checksum differs: $($asset.Name)" }
        if (-not $files.TryAdd($asset.Name, $path)) { throw "Candidate asset is shared by both packages: $($asset.Name)" }
        $checksums.Add([ordered]@{ File = $asset.Name; Hash = $asset.SHA256 })
    }
    $feed = Get-Content -LiteralPath (Join-Path $directory $manifest.Feed) -Raw | ConvertFrom-Json
    $package = Get-Item -LiteralPath (Join-Path $directory $manifest.UpdatePackage)
    $packageHash = ($manifest.Assets | Where-Object Name -ceq $manifest.UpdatePackage).SHA256
    if (@($feed.Assets).Count -ne 1 -or $feed.Assets[0].PackageId -cne $packageId -or $feed.Assets[0].Version -cne $manifest.Version -or
        $feed.Assets[0].Type -cne 'Full' -or $feed.Assets[0].FileName -cne $manifest.UpdatePackage -or
        $feed.Assets[0].Size -ne $package.Length -or $feed.Assets[0].SHA256 -ne $packageHash) {
        throw "The $($platform.Channel) feed does not identify the verified update package."
    }
}

if ($versions[0] -cne $versions[1]) { throw 'The candidate platforms must have the same version.' }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a fresh candidate output directory.' }
$output = (New-Item -ItemType Directory -Path $OutputDirectory).FullName
foreach ($file in $files.Values) { Copy-Item -LiteralPath $file -Destination $output }
$bundle = [ordered]@{ SourceCommit = $Commit; SourceTree = $sourceTree; SourceDirty = $false;
    Version = $versions[0]; PackageId = $packageId; Platforms = $provenance }
$evidenceName = if ($PackageIdentity -eq 'release') { 'release-provenance.json' } else { 'bundle-verification.json' }
$evidencePath = Join-Path $output $evidenceName
$bundle | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $evidencePath -Encoding utf8
if ($PackageIdentity -eq 'release') {
    $checksums.Add([ordered]@{ File = $evidenceName; Hash = (Get-FileHash -LiteralPath $evidencePath).Hash })
    ConvertTo-Json -InputObject $checksums.ToArray() -Depth 4 |
        Set-Content -LiteralPath (Join-Path $output 'checksums.json') -Encoding utf8
}
Write-Output "Staged version $($versions[0]) with Windows and Linux distributions and update feeds."
