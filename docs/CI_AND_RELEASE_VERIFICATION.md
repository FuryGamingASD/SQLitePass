# CI and release verification

This page describes the automation currently defined in `.github/workflows` and the scripts it invokes. Workflow files and scripts are the source of truth if this summary becomes outdated.

## Workflows

| Workflow | Triggers | Checks and outputs |
| --- | --- | --- |
| [Verify](../.github/workflows/verify.yml) | Push to any branch; `v*` tags; pull requests; manual dispatch | Resolves the source commit and release plan, then builds and tests on Windows and Linux. Produces platform candidates and verification evidence. Tagged public releases also receive SBOMs and attestations. |
| [CodeQL](../.github/workflows/codeql.yml) | Push and pull request to `main`; weekly schedule; manual dispatch | Runs CodeQL analysis for C# and uploads code-scanning results. It uses `build-mode: none`. |
| [Semgrep](../.github/workflows/semgrep.yml) | Push and pull request to `main`; weekly schedule; manual dispatch | Runs Semgrep with `--config auto`, validates the SARIF output, and uploads findings to code scanning for pull requests and `main` pushes. |

The scheduled CodeQL and Semgrep scans run on Mondays at 06:17 UTC and 06:43 UTC, respectively. Their findings are reported through GitHub code scanning; a finding is not itself a claim that the application is exploitable.

## Verify workflow details

The `Verify` workflow calls `scripts/Get-AvaloniaReleasePlan.ps1` to resolve the commit, version, and package identity. Its Windows and Linux jobs both run `scripts/Invoke-Ci.ps1`, which performs:

- locked restore of `SQLitePass.slnx`;
- Release build with warnings treated as errors;
- the shared test suite through `scripts/Invoke-CoreTests.ps1`;
- a NuGet vulnerability query for direct and transitive packages, checking that every project in the shared solution was scanned.

The jobs retain verification evidence as workflow artifacts. They also build `win-x64` and `linux-x64` Avalonia candidates and exercise platform-specific behavior. Windows verifies native launch and desktop integrations, and—when using the public release identity—installation and upgrade scenarios. Linux verifies native launch, desktop integrations, AppImage update behavior for release identity, and clipboard, tray, Auto-Type, and session transport under XWayland.

The candidate assembly job joins both platform outputs. For a public release identity it also creates Windows and Linux CycloneDX SBOMs and adds them to the release bundle. On a pushed `v*` tag with that identity, a separate job creates SLSA provenance attestations for the distribution and updater packages and attestations associating each platform's SBOM with its packages.

The optional `workflow_dispatch` input `release_rehearsal` builds with the public package identity and stages a release bundle. `existing_windows_installer_sha256` can be supplied to verify replacement of a same-version public installer. Verify creates and tests candidates; release signing and publication are separate operator-controlled steps described in [Update package authentication](UPDATE_SIGNING.md). The workflow does not itself publish a GitHub release.

Verification artifacts are retained for the periods configured in the workflow (14 days for evidence and platform candidates, 7 days for the assembled candidate). The release report and evidence links in `docs/releases` are historical records, not live status indicators; consult the linked workflow run for current results.

## Running the shared checks locally

Use the SDK pinned by [`global.json`](../global.json), then run:

```powershell
./scripts/Invoke-Ci.ps1
```

This runs shared build, tests, and NuGet vulnerability checks. Native packaging and desktop integration checks are performed in the Windows and Linux workflow jobs and require their respective environments.

## Scope and limitations

These workflows provide automated checks, not certification or proof that the software is free of vulnerabilities. CodeQL and Semgrep are static-analysis workflows; the NuGet audit is the automated dependency vulnerability gate in `Invoke-Ci.ps1`. The repository does not currently define a separate DAST workflow, fuzzing workflow, coverage-reporting workflow, or external penetration-test workflow.
