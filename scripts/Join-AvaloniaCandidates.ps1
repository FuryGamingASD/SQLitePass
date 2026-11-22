param(
    [Parameter(Mandatory)][string]$Windows,
    [Parameter(Mandatory)][string]$Linux,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$Commit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$platforms = @(
    @{ Directory = $Windows; Runtime = 'win-x64'; Channel = 'win'; Extension = '.exe' },
    @{ Directory = $Linux; Runtime = 'linux-x64'; Channel = 'linux'; Extension = '.AppImage' }
)
$files = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
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
        $manifest.Runtime -cne $platform.Runtime -or
        $manifest.Channel -cne $platform.Channel -or $manifest.Version -notmatch '^\d+\.\d+\.\d+$' -or
        $manifest.Feed -cne "releases.$($platform.Channel).json" -or
        [IO.Path]::GetExtension($manifest.Distribution) -cne $platform.Extension) {
        throw "Unexpected package provenance for $($platform.Runtime)."
    }
    $versions += $manifest.Version
    $provenance += $manifest
    $manifestFiles = @($manifest.Assets | ForEach-Object Name)
    $actualFiles = @(Get-ChildItem -LiteralPath $directory -File | Where-Object Name -ne $manifestName | ForEach-Object Name)
    if (@(Compare-Object $manifestFiles $actualFiles -CaseSensitive).Count -ne 0) { throw 'Candidate asset set differs from its manifest.' }
    foreach ($required in @($manifest.Feed, $manifest.Distribution, $manifest.UpdatePackage)) {
        if ($required -cnotin $manifestFiles) { throw "Missing candidate asset: $required" }
    }
    foreach ($asset in $manifest.Assets) {
        if ($asset.Name -notmatch '^[a-zA-Z0-9._-]+$' -or $asset.Name -in @('.', '..', 'bundle-verification.json')) {
            throw 'Invalid candidate asset filename.'
        }
        $path = Join-Path $directory $asset.Name
        if ($asset.SHA256 -ne (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash) { throw "Candidate asset checksum differs: $($asset.Name)" }
        if (-not $files.TryAdd($asset.Name, $path)) { throw "Candidate asset is shared by both packages: $($asset.Name)" }
    }
}

if ($versions[0] -cne $versions[1]) { throw 'The candidate platforms must have the same version.' }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a fresh candidate output directory.' }
$output = (New-Item -ItemType Directory -Path $OutputDirectory).FullName
foreach ($file in $files.Values) { Copy-Item -LiteralPath $file -Destination $output }
[ordered]@{ SourceCommit = $Commit; SourceTree = $sourceTree; Version = $versions[0]; Platforms = $provenance } | ConvertTo-Json -Depth 6 |
    Set-Content -LiteralPath (Join-Path $output 'bundle-verification.json') -Encoding utf8
Write-Output "Staged version $($versions[0]) with Windows and Linux distributions and update feeds."
