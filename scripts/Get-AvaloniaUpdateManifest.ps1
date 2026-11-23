param([Parameter(Mandatory)][string]$ArtifactDirectory)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$directory = (Resolve-Path -LiteralPath $ArtifactDirectory).ProviderPath
$provenance = Get-Content -LiteralPath (Join-Path $directory 'release-provenance.json') -Raw | ConvertFrom-Json
if ($provenance.PackageId -cne 'SQLitePass' -or $provenance.Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$' -or
    $provenance.SourceCommit -cnotmatch '^[0-9a-f]{40}$' -or $provenance.SourceDirty -ne $false) {
    throw 'Only a clean verified SQLitePass release candidate can produce an update manifest.'
}

$assets = foreach ($channel in @('win', 'linux')) {
    $feedPath = Join-Path $directory "releases.$channel.json"
    if (-not (Test-Path -LiteralPath $feedPath -PathType Leaf)) { throw "Missing $channel update feed." }
    $feed = Get-Content -LiteralPath $feedPath -Raw | ConvertFrom-Json
    if (@($feed.Assets).Count -ne 1) { throw "The $channel feed must contain one full package." }
    $asset = $feed.Assets[0]
    if ($asset.PackageId -cne 'SQLitePass' -or $asset.Version -cne $provenance.Version -or $asset.Type -cne 'Full' -or
        [string]::IsNullOrEmpty($asset.FileName) -or $asset.FileName -cne [IO.Path]::GetFileName($asset.FileName)) {
        throw "The $channel feed contains an unexpected update package."
    }
    $packagePath = Join-Path $directory $asset.FileName
    $length = (Get-Item -LiteralPath $packagePath).Length
    $hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($length -lt 1 -or $length -ne [long]$asset.Size -or $hash -cne ([string]$asset.SHA256).ToLowerInvariant()) {
        throw "The $channel package does not match its Velopack feed."
    }
    [ordered]@{
        Channel = $channel; PackageId = 'SQLitePass'; Version = $provenance.Version
        Type = 'Full'; FileName = $asset.FileName; Size = $length; Sha256 = $hash
    }
}

[ordered]@{
    SchemaVersion = 1; PackageId = 'SQLitePass'; Version = $provenance.Version
    SourceCommit = $provenance.SourceCommit; Assets = @($assets)
} | ConvertTo-Json -Depth 5 -Compress
