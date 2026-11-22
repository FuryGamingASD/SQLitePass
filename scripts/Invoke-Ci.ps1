param([ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).ProviderPath
Push-Location -LiteralPath $root
try {
    $sharedVersion = & dotnet msbuild SQLitePass.Desktop/SQLitePass.Desktop.csproj -nologo -getProperty:Version
    if ($LASTEXITCODE -ne 0 -or $sharedVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Cannot resolve the shared application version.' }
    if ($Version -and $Version -cne $sharedVersion) { throw 'The requested version differs from Directory.Build.props.' }

    & (Join-Path $PSScriptRoot 'Test-ReleasePublication.ps1')
    New-Item -ItemType Directory -Path 'artifacts/verification' -Force | Out-Null
    dotnet restore SQLitePass.slnx --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }
    dotnet build SQLitePass.slnx -c Release --no-restore --warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Shared application build failed.' }
    & (Join-Path $PSScriptRoot 'Invoke-CoreTests.ps1')

    $auditPath = Join-Path $root 'artifacts/verification/ci-nuget-vulnerabilities.json'
    dotnet list SQLitePass.slnx package --vulnerable --include-transitive --format json --no-restore > $auditPath
    if ($LASTEXITCODE -ne 0) { throw 'NuGet vulnerability query failed.' }
    $audit = Get-Content -LiteralPath $auditPath -Raw | ConvertFrom-Json -AsHashtable
    if ($audit.ContainsKey('problems') -and $audit.problems.Count -gt 0) { throw 'NuGet audit reported problems.' }
    foreach ($project in $audit.projects) {
        if (-not $project.ContainsKey('frameworks')) { continue }
        foreach ($framework in $project.frameworks) {
            foreach ($kind in @('topLevelPackages', 'transitivePackages')) {
                if (-not $framework.ContainsKey($kind)) { continue }
                foreach ($package in $framework[$kind]) {
                    if ($package.ContainsKey('vulnerabilities') -and $package.vulnerabilities.Count -gt 0) {
                        throw "Known vulnerabilities reported for $($package.id)."
                    }
                }
            }
        }
    }
    $solution = [xml](Get-Content -LiteralPath (Join-Path $root 'SQLitePass.slnx') -Raw)
    $expected = @($solution.SelectNodes('//Project[@Path]') | ForEach-Object { [IO.Path]::GetFullPath($_.Path, $root) })
    $scanned = @($audit.projects | ForEach-Object { [IO.Path]::GetFullPath($_.path, $root) })
    if (@(Compare-Object $expected $scanned).Count -ne 0) { throw 'NuGet audit did not cover every shared solution project.' }
    Write-Output "Verified the shared Avalonia application $sharedVersion. Native packaging runs in each platform job."
} finally { Pop-Location }
