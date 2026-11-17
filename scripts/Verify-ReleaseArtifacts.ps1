param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+-[a-f0-9]{32}$')]
    [string]$BuildId,
    [string]$TestResults = 'editing-tests.trx',
    [string]$UiResultsDirectory = 'ui-editing',
    [string]$VulnerabilityResults = 'editing-nuget-vulnerabilities.json',
    # Keep aligned with the regression suite; the exact count also detects test discovery drift.
    [ValidateRange(1, 100000)][int]$ExpectedTests = 159,
    [ValidateRange(1, 100000)][int]$ExpectedUiCases = 1080,
    [ValidateRange(1, 100000)][int]$ExpectedContrastPairs = 105
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).ProviderPath
$output = Join-Path $root 'artifacts\verification'
$release = Join-Path $root "artifacts\releases\$BuildId"
$publish = Join-Path $root "artifacts\publish\$BuildId"
$checksums = @(Get-Content (Join-Path $release 'checksums.json') -Raw | ConvertFrom-Json)
foreach ($checksum in $checksums) {
    if ((Get-FileHash -LiteralPath (Join-Path $release $checksum.File) -Algorithm SHA256).Hash -ne $checksum.Hash) { throw "Checksum mismatch: $($checksum.File)" }
}
$archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $release 'SQLitePass-win-Portable.zip'))
function Read-PackageText([string]$Name) {
    $entries = @($archive.Entries | Where-Object Name -eq $Name)
    if ($entries.Count -ne 1) { throw "Expected exactly one package file: $Name" }
    $reader = [IO.StreamReader]::new($entries[0].Open())
    try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
}
try {
    foreach ($file in @('SQLitePass.exe','SQLitePass.dll','SQLitePass.Core.dll','LICENSE','SECURITY.md','THIRD_PARTY_NOTICES.md','e_sqlite3.dll','coreclr.dll','dependency-metadata.json','Konscious-LICENSE.txt','Velopack-LICENSE.txt','Hardcodet.NotifyIcon.Wpf-LICENSE.txt','Apache-2.0.txt','Microsoft-EFCore-LICENSE.txt','LICENSE-SOURCES.md')) {
        if (-not @($archive.Entries | Where-Object Name -eq $file).Count) { throw "Missing shipped file: $file" }
    }
    if ($archive.Entries.FullName -match 'SQLitePass\.(Tests|TestHost|UI\.Verification)|xunit|testhost\.dll|coverlet') { throw 'Test components must not be shipped.' }
    $runtime = Read-PackageText 'SQLitePass.runtimeconfig.json' | ConvertFrom-Json
    foreach ($framework in $runtime.runtimeOptions.includedFrameworks) { if ($framework.version -ne '10.0.15') { throw 'Unexpected bundled runtime.' } }
    $dependencies = Read-PackageText 'SQLitePass.deps.json' | ConvertFrom-Json -AsHashtable
    $licenses = @(Read-PackageText 'dependency-metadata.json' | ConvertFrom-Json)
    $packageNames = @($dependencies.libraries.Keys | Where-Object { $dependencies.libraries[$_].type -eq 'package' -and $_ -notmatch '^Microsoft\.(NETCore|WindowsDesktop)\.App\.Runtime\.' })
    $licenseNames = @($licenses | ForEach-Object { $_.Package + '/' + $_.Version })
    if (@(Compare-Object ($packageNames | Sort-Object) ($licenseNames | Sort-Object)).Count -ne 0) { throw 'License inventory differs from shipped application packages.' }
    if (@($licenses | Where-Object { [string]::IsNullOrWhiteSpace($_.License) -or [string]::IsNullOrWhiteSpace($_.Copyright) }).Count -ne 0) { throw 'A package license/copyright declaration is missing.' }
    $entryCount = $archive.Entries.Count
} finally { $archive.Dispose() }
$version = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $publish 'SQLitePass.dll')).Version.ToString()
$releaseVersion = ($BuildId -split '-', 2)[0]
$expectedAssemblyVersion = "$releaseVersion.0"
if ($version -ne $expectedAssemblyVersion) { throw 'Unexpected application assembly version.' }
$scan = Get-Content (Join-Path $output $VulnerabilityResults) -Raw | ConvertFrom-Json -AsHashtable
if ($scan.ContainsKey('problems') -and $scan.problems.Count -gt 0) { throw 'Vulnerability scan failed.' }
$vulnerabilities = 0
foreach ($project in $scan.projects) {
    if (-not $project.ContainsKey('frameworks')) { continue }
    foreach ($framework in $project.frameworks) {
        foreach ($kind in @('topLevelPackages','transitivePackages')) {
            if (-not $framework.ContainsKey($kind)) { continue }
            foreach ($package in $framework[$kind]) { if ($package.ContainsKey('vulnerabilities')) { $vulnerabilities += $package.vulnerabilities.Count } }
        }
    }
}
if ($vulnerabilities -ne 0 -or $scan.projects.Count -ne 5) { throw 'Unexpected vulnerability scan result.' }
[xml]$trx = Get-Content (Join-Path $output $TestResults) -Raw
$counters = $trx.TestRun.ResultSummary.Counters
if ([int]$counters.total -ne $ExpectedTests -or [int]$counters.passed -ne $ExpectedTests -or [int]$counters.failed -ne 0) { throw 'Unexpected regression suite result.' }
$uiOutput = Join-Path $output $UiResultsDirectory
$ui = Get-Content (Join-Path $uiOutput 'ui-render-results.json') -Raw | ConvertFrom-Json
$session = Get-Content (Join-Path $uiOutput 'session-lock-results.json') -Raw | ConvertFrom-Json
$editor = Get-Content (Join-Path $uiOutput 'editor-results.json') -Raw | ConvertFrom-Json
if ($ui.Cases -ne $ExpectedUiCases -or $ui.WindowsShown -or -not $ui.PagedMainWindowIncluded -or -not $session.EditorCleared -or -not $session.KeyDisposed -or -not $session.OwnedDialogsClosed) { throw 'Unexpected WPF verification result.' }
if (-not $session.ProviderLoadingTested -or -not $session.UiFiltersTested -or -not $session.RevealsAndFiltersCleared -or
    -not $editor.MonotonicRevealDeadline -or -not $editor.InlineTitleExpiryAndAutoTypeValidation -or -not $editor.BoundHistoryPreparedOnDispatcher) { throw 'Editor/filter/reveal verification failed.' }
$contrasts = @(Get-Content (Join-Path $uiOutput 'theme-contrast-results.json') -Raw | ConvertFrom-Json)
if ($contrasts.Count -ne $ExpectedContrastPairs -or @($contrasts | Where-Object Ratio -lt 4.5).Count -ne 0) { throw 'Theme text contrast verification failed.' }
$sourceFiles = @(& git -c 'safe.directory=*' -C $root -c core.quotePath=false ls-files --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0) { throw 'Could not inventory source files.' }
$sourceHashes = @($sourceFiles | Sort-Object -Unique | ForEach-Object {
    $file = Join-Path $root $_
    if (Test-Path -LiteralPath $file -PathType Leaf) { [ordered]@{ File=$_; Sha256=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash } }
})
$sourceHashes | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $output 'editing-source-files-sha256.json') -Encoding utf8
$result = [ordered]@{
    BuildId=$BuildId; ApplicationVersion=$version; BundledFrameworks=@($runtime.runtimeOptions.includedFrameworks);
    PortableEntryCount=$entryCount; ArtifactHashesChecked=$checksums.Count; ApplicationDependenciesWithNotices=$licenses.Count;
    TestsPassed=[int]$counters.passed; NugetProjectsScanned=$scan.projects.Count; KnownNugetVulnerabilities=$vulnerabilities;
    InMemoryWpfCases=$ui.Cases; ThemeTextContrastPairs=$contrasts.Count; SessionEditorCleared=$session.EditorCleared; SessionKeyDisposed=$session.KeyDisposed;
    ProviderLoadingVerified=$session.ProviderLoadingTested; UiFiltersVerified=$session.UiFiltersTested; TimedRevealVerified=$editor.MonotonicRevealDeadline; InlineEditorValidationVerified=$editor.InlineTitleExpiryAndAutoTypeValidation;
    ApplicationSignature=(Get-AuthenticodeSignature (Join-Path $publish 'SQLitePass.exe')).Status.ToString();
    SetupSignature=(Get-AuthenticodeSignature (Join-Path $release 'SQLitePass-win-Setup.exe')).Status.ToString();
    SigningWaivedByUser=$true; RealVaultOpened=$false; Published=$false; SourceFilesRecorded=$sourceHashes.Count;
    NativeDpiAndNarratorVerified=$false
}
$result | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $output 'editing-artifact-verification.json') -Encoding utf8
$result | ConvertTo-Json -Depth 8
