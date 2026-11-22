param([string]$ResultsDirectory = 'artifacts/verification')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).ProviderPath
Push-Location -LiteralPath $root
try {
    dotnet test SQLitePass.Tests/SQLitePass.Tests.csproj -c Release --no-build --no-restore `
        --logger 'trx;LogFileName=ci-tests.trx' --collect 'XPlat Code Coverage' --results-directory $ResultsDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Core regression tests failed.' }
} finally { Pop-Location }
