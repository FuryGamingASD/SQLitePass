param(
    [Parameter(Mandatory)][ValidatePattern('^v\d+\.\d+\.\d+$')][string]$Tag,
    [Parameter(Mandatory)][string]$ArtifactDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_REPOSITORY -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'No valid GitHub repository was supplied.' }
$output = (New-Item -ItemType Directory -Path $OutputDirectory -Force).FullName
foreach ($channel in @('win', 'linux')) {
    $name = "releases.$channel.json"
    $expected = (Get-FileHash -LiteralPath (Join-Path $ArtifactDirectory $name)).Hash
    $path = Join-Path $output $name
    $url = "https://github.com/$env:GITHUB_REPOSITORY/releases/download/$Tag/$name"
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            Invoke-WebRequest -Uri $url -OutFile $path -TimeoutSec 30
            if ((Get-FileHash -LiteralPath $path).Hash -ne $expected) { throw "The public $channel feed checksum differs." }
            Write-Output "Verified public $name for $Tag."
            break
        } catch {
            if ($attempt -eq 3) { throw }
            Start-Sleep -Seconds 10
        }
    }
}
