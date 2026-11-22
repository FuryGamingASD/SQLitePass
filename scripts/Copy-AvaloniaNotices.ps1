param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$NuGetRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$repositoryNotices = Join-Path $root 'docs/licenses'
$desktopNotices = Join-Path $root 'SQLitePass.Desktop/ThirdPartyLicenses'
$output = Join-Path $PublishDirectory 'THIRD_PARTY_LICENSES'
$libraries = (Get-Content -LiteralPath (Join-Path $PublishDirectory 'SQLitePass.Desktop.deps.json') -Raw | ConvertFrom-Json -AsHashtable).libraries

$metadata = foreach ($libraryName in $libraries.Keys) {
    $runtimePack = $libraryName.StartsWith('runtimepack.', [StringComparison]::Ordinal)
    if (-not $runtimePack -and $libraries[$libraryName].type -ne 'package') { continue }
    $package, $version = $libraryName -replace '^runtimepack\.', '' -split '/', 2
    if ($package -notmatch '^[A-Za-z0-9._-]+$' -or $version -notmatch '^[A-Za-z0-9._+-]+$') { throw 'Invalid NuGet package identity.' }
    $packageDirectory = Join-Path $NuGetRoot "$($package.ToLowerInvariant())/$version"
    $nuspecPath = Join-Path $packageDirectory ($package.ToLowerInvariant() + '.nuspec')
    $nuspec = ([xml](Get-Content -LiteralPath $nuspecPath -Raw)).DocumentElement.SelectSingleNode("*[local-name()='metadata']")
    $license = $nuspec.SelectSingleNode("*[local-name()='license']")
    $copyright = $nuspec.SelectSingleNode("*[local-name()='copyright']")
    $repository = $nuspec.SelectSingleNode("*[local-name()='repository']")
    if ($null -eq $license -or [string]::IsNullOrWhiteSpace($license.InnerText) -or $null -eq $copyright -or
        [string]::IsNullOrWhiteSpace($copyright.InnerText)) { throw "Missing license or copyright for $package/$version." }

    $notices = @(Get-ChildItem -LiteralPath $packageDirectory -File | Where-Object Name -Match '^(LICENSE|COPYING|NOTICE|THIRD-PARTY-NOTICES)(\.|$)')
    if ($license.GetAttribute('type') -eq 'file') {
        if ([IO.Path]::GetFileName($license.InnerText) -cne $license.InnerText -or $license.InnerText -cnotin $notices.Name) {
            throw "The declared license file is missing for $package/$version."
        }
    } elseif (-not @($notices | Where-Object Name -Match '^(LICENSE|COPYING)(\.|$)').Count) {
        $fallback = switch -Regex ($package) {
            '^Avalonia(\.|$)' { Join-Path $desktopNotices 'Avalonia-LICENSE.txt'; break }
            '^Konscious\.' { 'Konscious-LICENSE.txt'; break }
            '^Microsoft\.Data\.Sqlite(\.|$)' { 'Microsoft-EFCore-LICENSE.txt'; break }
            '^Microsoft\.Win32\.SystemEvents$' { 'Microsoft-EFCore-LICENSE.txt'; break }
            '^MicroCom\.Runtime$' { Join-Path $desktopNotices 'MicroCom-LICENSE.txt'; break }
            '^SQLitePCLRaw\.' { 'Apache-2.0.txt'; break }
            '^Tmds\.DBus\.Protocol$' { Join-Path $desktopNotices 'Tmds.DBus-LICENSE.txt'; break }
            '^Velopack$' { 'Velopack-LICENSE.txt'; break }
            default { throw "Preserve the upstream license for $package/$version before packaging." }
        }
        $notices += Get-Item -LiteralPath ([IO.Path]::GetFullPath($fallback, $repositoryNotices))
    }

    $destination = Join-Path $output "$package/$version"
    New-Item -ItemType Directory -Path $destination | Out-Null
    foreach ($notice in $notices) { Copy-Item -LiteralPath $notice.FullName -Destination $destination }
    [ordered]@{
        Package = $package; Version = $version; License = $license.InnerText; LicenseType = $license.GetAttribute('type')
        Copyright = $copyright.InnerText
        Repository = $(if ($repository) { $repository.GetAttribute('url') })
        Commit = $(if ($repository) { $repository.GetAttribute('commit') })
        Notices = @($notices.Name)
    }
}
$metadata | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'dependency-metadata.json') -Encoding utf8
