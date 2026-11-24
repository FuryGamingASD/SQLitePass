param([string]$ResultsDirectory = 'artifacts/verification')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).ProviderPath
Push-Location -LiteralPath $root
try {
    dotnet test SQLitePass.Tests/SQLitePass.Tests.csproj -c Release --no-build --no-restore `
        --logger 'trx;LogFileName=ci-tests.trx' --collect 'XPlat Code Coverage' --results-directory $ResultsDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Core regression tests failed.' }

    $resultsPath = [IO.Path]::GetFullPath($ResultsDirectory, $root)
    $coverageReports = @(Get-ChildItem -LiteralPath $resultsPath -Recurse -File -Filter 'coverage.cobertura.xml')
    if ($coverageReports.Count -eq 0) { throw 'Core regression tests did not produce a Cobertura coverage report.' }

    [long]$linesCovered = 0
    [long]$linesValid = 0
    [long]$branchesCovered = 0
    [long]$branchesValid = 0
    foreach ($coverageReport in $coverageReports) {
        [xml]$report = Get-Content -LiteralPath $coverageReport.FullName -Raw
        $coverage = $report.SelectSingleNode('/coverage')
        if ($null -eq $coverage) { throw "Invalid Cobertura coverage report: $($coverageReport.FullName)" }
        $linesCovered += [long]$coverage.GetAttribute('lines-covered')
        $linesValid += [long]$coverage.GetAttribute('lines-valid')
        $branchesCovered += [long]$coverage.GetAttribute('branches-covered')
        $branchesValid += [long]$coverage.GetAttribute('branches-valid')
    }

    $lineRate = if ($linesValid -gt 0) { '{0:P1}' -f ($linesCovered / $linesValid) } else { 'n/a' }
    $branchRate = if ($branchesValid -gt 0) { '{0:P1}' -f ($branchesCovered / $branchesValid) } else { 'n/a' }
    $coverageSummary = @(
        '## Regression test coverage'
        ''
        '| Measure | Covered | Total | Rate |'
        '| --- | ---: | ---: | ---: |'
        "| Lines | $linesCovered | $linesValid | $lineRate |"
        "| Branches | $branchesCovered | $branchesValid | $branchRate |"
        ''
        "Cobertura reports: $($coverageReports.Count). No percentage gate is applied until a reviewed baseline is established."
    ) -join "`n"
    if ($env:GITHUB_STEP_SUMMARY) {
        [IO.File]::AppendAllText($env:GITHUB_STEP_SUMMARY, "$coverageSummary`n", [Text.UTF8Encoding]::new($false))
    }
    Write-Output $coverageSummary
} finally { Pop-Location }
