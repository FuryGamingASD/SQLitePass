# Security Testing

## Purpose and limits

This document describes the security testing strategy and the automation currently present in SQLitePass. Testing is evidence about the exercised code paths and environments; it does not prove the absence of vulnerabilities or replace independent review.

The current CI workflow inventory, trigger conditions, artifact retention, and release-attestation behavior are documented in [CI and release verification](CI_AND_RELEASE_VERIFICATION.md). The workflow YAML and scripts are authoritative if this summary becomes outdated.

## Automated checks

### Shared build, tests, and dependency audit

The [Verify workflow](../.github/workflows/verify.yml) runs on pushes to branches, `v*` tags, pull requests, and manual dispatch. Its Windows and Linux jobs invoke `scripts/Invoke-Ci.ps1`, which performs a locked restore, a Release build with warnings treated as errors, the shared test suite, and a NuGet vulnerability query over direct and transitive packages. The script confirms that the audit covers every project in `SQLitePass.slnx`.

The workflow also builds native Avalonia candidates and runs platform-specific launch, desktop-integration, installation, update, and XWayland checks as applicable to the event and package identity. On public release tags, the workflow generates platform SBOMs and creates provenance/SBOM attestations. Signing and publication remain separate operator-controlled steps.

### Static analysis

Two separate workflows run on pushes and pull requests to `main`, weekly, and on manual dispatch:

- [CodeQL](../.github/workflows/codeql.yml) analyzes C# with `build-mode: none` and publishes code-scanning results.
- [Semgrep](../.github/workflows/semgrep.yml) runs `semgrep scan --config auto`, validates the SARIF report, and publishes findings for pull requests and `main` pushes.

These are static-analysis checks. Their results require triage and do not establish exploitability or security certification.

### Running shared checks locally

With the SDK selected by [`global.json`](../global.json), run:

```powershell
./scripts/Invoke-Ci.ps1
```

This does not reproduce every native CI check: packaging and desktop integrations require the corresponding Windows or Linux environment.

## Security testing principles

1. Test security-critical behavior directly, including authentication, encryption, integrity validation, recovery, session locking, clipboard operations, Auto-Type, import, and update authentication.
2. Add regression tests for fixed defects where practical.
3. Exercise user workflows, failure paths, and recovery—not only isolated methods.
4. Describe what was actually tested, distinguishing automated results, manual observations, and unverified assumptions.

## Test areas

The shared test suite and platform probes cover security-relevant behavior including:

- credential derivation and authenticated encryption;
- vault integrity, tamper detection, and SQLite validation;
- backup validation, recovery, migration, and rekey behavior;
- session locking, cleanup, and cancellation of in-flight operations;
- clipboard expiry, ownership, platform safeguards, and replacement behavior;
- Auto-Type target validation and interruption on target changes;
- import validation and authenticated update-package handling;
- native application launch, desktop integrations, and selected installation/update scenarios.

The detailed scope and recorded evidence vary by platform and test run. See the [release verification reports](README.md#releases), [desktop verification guide](CROSS_PLATFORM.md), and [audit status](AUDIT_STATUS.md). Do not interpret a test category in this list as proof that every possible failure mode has been exercised.

## Release security validation

Before relying on a release candidate, inspect the successful tagged Verify run and its artifacts. The workflow's release path generates Windows and Linux SBOMs and attestations for distribution and updater packages. It does not publish the release itself: the signing and publication procedure is documented in [Update package authentication](UPDATE_SIGNING.md).

Keep synthetic test data and credentials in verification. Never use production vaults, passwords, or key files in test evidence.

## Future work

Potential improvements that are not currently defined as dedicated repository workflows include:

- fuzz testing;
- coverage reporting;
- dynamic application security testing;
- external penetration testing;
- independent cryptographic and application-security reviews.

CodeQL, Semgrep, and the NuGet vulnerability audit are already part of the current automation and are not listed as future additions.

## Limitations

Automated security testing cannot prove:

- absence of vulnerabilities or future defects;
- resistance to unknown attack techniques;
- hardware-level durability or behavior outside tested environments;
- safety against a compromised operating system or privileged attacker.

SQLitePass has not undergone an independent cryptographic audit or external penetration test. See [Audit status](AUDIT_STATUS.md) for the current assurance limits.
