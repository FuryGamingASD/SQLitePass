param([Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$releaseHistory = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'RELEASE_NOTES.md') -Raw
$escapedVersion = [regex]::Escape($Version)
$pattern = '(?ms)^# SQLitePass ' + $escapedVersion + '[ \t]*(?:\r?\n|\z)(?<body>.*?)(?=^# |\z)'
$sections = [regex]::Matches($releaseHistory, $pattern)
if ($sections.Count -ne 1) { throw "Expected one '# SQLitePass $Version' heading in RELEASE_NOTES.md." }
$body = [regex]::Replace($sections[0].Groups['body'].Value.Trim(), '(?:\A|\r?\n)---[ \t]*\z', '').Trim()
if (-not $body) { throw "Release notes section for $Version is empty." }
"# SQLitePass $Version`n`n$body"
