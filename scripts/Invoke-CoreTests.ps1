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
    $coverageReports = if (Test-Path -LiteralPath $resultsPath -PathType Container) {
        @(Get-ChildItem -LiteralPath $resultsPath -Recurse -File -Filter 'coverage.cobertura.xml')
    } else { @() }
    $coverageWarnings = [Collections.Generic.List[string]]::new()

    [long]$linesCovered = 0
    [long]$linesValid = 0
    [long]$branchesCovered = 0
    [long]$branchesValid = 0
    foreach ($coverageReport in $coverageReports) {
        try {
            [xml]$report = Get-Content -LiteralPath $coverageReport.FullName -Raw
            $coverage = $report.SelectSingleNode('/coverage')
            if ($null -eq $coverage) { throw 'Missing Cobertura root element.' }
            $linesCovered += [long]$coverage.GetAttribute('lines-covered')
            $linesValid += [long]$coverage.GetAttribute('lines-valid')
            $branchesCovered += [long]$coverage.GetAttribute('branches-covered')
            $branchesValid += [long]$coverage.GetAttribute('branches-valid')
        } catch {
            $coverageWarnings.Add("Could not read Cobertura report: $($coverageReport.Name)")
        }
    }

    if ($linesValid -gt 0) {
        $lineRate = '{0:P1}' -f ($linesCovered / $linesValid)
        $branchRate = if ($branchesValid -gt 0) { '{0:P1}' -f ($branchesCovered / $branchesValid) } else { 'n/a' }
        $coverageDetails = @(
            '| Measure | Covered | Total | Rate |'
            '| --- | ---: | ---: | ---: |'
            "| Lines | $linesCovered | $linesValid | $lineRate |"
            "| Branches | $branchesCovered | $branchesValid | $branchRate |"
        )
    } else {
        $coverageWarnings.Add('The test runner completed, but no readable Cobertura report was produced.')
        $coverageDetails = @('Coverage percentages are unavailable for this run.')
    }
    $coverageSummaryLines = @('## Regression test coverage', '') + $coverageDetails + @('',
        "Cobertura reports found: $($coverageReports.Count). No percentage gate is applied until a reviewed baseline is established.")
    if ($coverageWarnings.Count -gt 0) {
        $coverageSummaryLines += @('', 'Coverage notes:')
        $coverageSummaryLines += @($coverageWarnings | ForEach-Object { "- $_" })
        foreach ($coverageWarning in $coverageWarnings) { Write-Warning $coverageWarning }
    }
    $coverageSummary = $coverageSummaryLines -join "`n"
    if ($env:GITHUB_STEP_SUMMARY) {
        [IO.File]::AppendAllText($env:GITHUB_STEP_SUMMARY, "$coverageSummary`n", [Text.UTF8Encoding]::new($false))
    }
    Write-Output $coverageSummary
} finally { Pop-Location }
