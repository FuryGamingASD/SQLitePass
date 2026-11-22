param([ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version='1.1.3')
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).ProviderPath
Push-Location -LiteralPath $root
try {
    & (Join-Path $PSScriptRoot 'Test-ReleasePublication.ps1')
    New-Item -ItemType Directory -Path 'artifacts/verification' -Force | Out-Null
    dotnet restore SQLitePass.slnx --locked-mode
    if($LASTEXITCODE -ne 0){throw 'Locked restore failed.'}
    dotnet build SQLitePass.slnx -c Release --no-restore --warnaserror
    if($LASTEXITCODE -ne 0){throw 'Release build failed.'}
    & (Join-Path $PSScriptRoot 'Invoke-CoreTests.ps1')
    dotnet run --project SQLitePass.UI.Verification -c Release --no-build --no-restore -- artifacts/verification/ui-ci
    if($LASTEXITCODE -ne 0){throw 'Isolated WPF verification failed.'}
    dotnet list SQLitePass.slnx package --vulnerable --include-transitive --format json --no-restore > artifacts/verification/ci-nuget-vulnerabilities.json
    if($LASTEXITCODE -ne 0){throw 'NuGet vulnerability query failed.'}
    & (Join-Path $root 'build-installer.ps1') -Version $Version -AllowUnsigned
    $build=Get-Content artifacts/last-build.json -Raw | ConvertFrom-Json
    & (Join-Path $PSScriptRoot 'Verify-ReleaseArtifacts.ps1') -BuildId $build.BuildId -TestResults 'ci-tests.trx' -UiResultsDirectory 'ui-ci' -VulnerabilityResults 'ci-nuget-vulnerabilities.json'
    $legacy=Join-Path $root 'releases/SQLitePass-win-Setup.exe'
    if(-not (Test-Path -LiteralPath $legacy)){
        New-Item -ItemType Directory -Path (Split-Path $legacy -Parent) -Force | Out-Null
        Invoke-WebRequest -Uri 'https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.0.2/SQLitePass-win-Setup.exe' -OutFile $legacy
    }
    if((Get-FileHash -LiteralPath $legacy -Algorithm SHA256).Hash -ne '11F557F257C731BB9F6BE547CB4020ED189F1A5B86A09010B688A1A016C4D52B'){throw 'Legacy setup checksum differs.'}
    if ($env:RUNNER_ENVIRONMENT -eq 'github-hosted') {
        $previousSetup = Join-Path $root 'releases/SQLitePass-1.1.2-Setup.exe'
        Invoke-WebRequest -Uri 'https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.1.2/SQLitePass-win-Setup.exe' -OutFile $previousSetup
        if ((Get-FileHash -LiteralPath $previousSetup -Algorithm SHA256).Hash -ne '9679209912914F611FF5AEB5B544B7C60B20B6643055F98D04F4691D99764723') {
            throw 'Previous release setup checksum differs.'
        }
        & (Join-Path $PSScriptRoot 'Verify-WindowsPackage.ps1') -BuildId $build.BuildId -CheckRunningApp
    } else {
        & (Join-Path $PSScriptRoot 'Verify-WindowsPackage.ps1') -BuildId $build.BuildId
    }
    if($env:GITHUB_OUTPUT){
        "build_id=$($build.BuildId)" | Add-Content -LiteralPath $env:GITHUB_OUTPUT
        "release_path=artifacts/releases/$($build.BuildId)" | Add-Content -LiteralPath $env:GITHUB_OUTPUT
    }
} finally {Pop-Location}
