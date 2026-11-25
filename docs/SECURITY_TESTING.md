# Security Testing

## Purpose

This document describes the security testing strategy used by SQLitePass.

Its objectives are to:

- Identify security defects
- Prevent regressions
- Validate architectural assumptions
- Verify data integrity protections
- Validate recovery workflows
- Reduce the likelihood of introducing vulnerabilities

Automated security checks run in development and release workflows. The 1.3.0 suite adds KDBX import, machine-policy and signed-update regressions, including invalid signatures, mismatched packages and expired checkpoints. Native release gates exercise installation, upgrade and reopening a synthetic vault on both platforms. The verification roadmap and audit status record the scope and unresolved checks.

Testing improves confidence but does not guarantee the absence of vulnerabilities.

---

# Security Testing Philosophy

SQLitePass follows four testing principles:

## 1. Verify Security-Critical Behavior

Security-sensitive functionality must be tested directly.

Examples include:

- Authentication
- Encryption
- Integrity validation
- Recovery
- Session locking
- Auto-Type

---

## 2. Prevent Regression

Every vulnerability, bug or design flaw that is fixed should result in a corresponding automated test whenever practical.

---

## 3. Validate Real Workflows

Security testing should validate:

- User workflows
- Recovery procedures
- Failure conditions

and not only individual methods.

---

## 4. Prefer Evidence Over Assumptions

Features are not considered secure because they were designed correctly.

They are considered more trustworthy only after:

- Review
- Testing
- Repeated validation

---

# Automated CI and Static Analysis

The repository's automation is described in [CI and release verification](CI_AND_RELEASE_VERIFICATION.md). The workflow YAML and scripts are authoritative if this summary becomes outdated.

## Verify workflow

The `Verify` workflow runs for pushes to branches, `v*` tags, pull requests and manual dispatch. Its Windows and Linux jobs invoke `scripts/Invoke-Ci.ps1`, which performs:

- A locked restore of `SQLitePass.slnx`.
- A Release build with warnings treated as errors.
- The shared `SQLitePass.Tests` suite through `scripts/Invoke-CoreTests.ps1`.
- A NuGet vulnerability audit over direct and transitive dependencies.
- Confirmation that every project in the shared solution was covered by the audit.

The platform jobs build Avalonia candidates for `win-x64` and `linux-x64`, preserve verification evidence, and run native launch and desktop integration checks. Public-release rehearsals also cover installation, upgrades and AppImage replacement as applicable.

For public release tags, the candidate assembly creates Windows and Linux CycloneDX SBOMs. A tagged release job creates SLSA provenance attestations for release packages and SBOM attestations for each platform package set. Signing and publication remain separate operator-controlled steps.

## CodeQL

The [CodeQL workflow](../.github/workflows/codeql.yml) runs on pushes and pull requests to `main`, weekly, and by manual dispatch. It initializes CodeQL for C# using `build-mode: none` and publishes code-scanning results.

## Semgrep

The [Semgrep workflow](../.github/workflows/semgrep.yml) runs on pushes and pull requests to `main`, weekly, and by manual dispatch. It executes `semgrep scan --config auto --sarif`, validates that the report was produced, and uploads findings to GitHub code scanning for pull requests and `main` pushes.

Static-analysis findings require triage. A finding is not itself evidence that an issue is exploitable.

## Running shared checks locally

With the SDK pinned in `global.json`, run:

```powershell
./scripts/Invoke-Ci.ps1
```

This executes shared build, test and dependency checks. Native packaging and platform desktop checks require the Windows or Linux CI environment.

---

# Security Test Categories

## Unit Tests

Unit tests validate individual security-sensitive components.

Examples:

### Cryptography

- Key derivation
- Encryption
- Decryption
- Authentication failures
- Invalid key behavior

### Data Validation

- Metadata validation
- Vault integrity checks
- Context validation

### Input Validation

- CSV parsing
- JSON import validation
- Settings validation

---

## Integration Tests

Integration tests verify that independent security features operate correctly together.

Examples:

- Vault creation and unlock
- Backup creation and validation
- Restore workflows
- Migration workflows
- Rekey operations

---

## Recovery Tests

Recovery testing validates the most critical reliability requirement of SQLitePass:

```text
Recoverability
```

Scenarios include:

- Corrupted vault files
- Interrupted writes
- Failed migrations
- Failed rekey operations
- Backup restoration

Recovery tests should verify both:

- Technical success
- Data correctness

---

## User Interface Security Tests

UI-focused tests validate:

- Locking behavior
- Dialog cleanup
- Password reveal behavior
- Secure workflows
- Input validation

Examples:

- Lock while editing
- Lock during Auto-Type
- Lock during vault operations
- Dialog disposal

---

# Cryptography Test Requirements

## Key Derivation

Required verification:

- Password-only vaults
- Key-file-only vaults
- Password + key file vaults
- Salt validation
- Invalid KDF parameters

Expected outcomes:

- Correct derivation
- Deterministic key generation
- Controlled failures

---

## AES-GCM Validation

Required verification:

- Successful encryption
- Successful decryption
- Invalid key handling
- Invalid nonce handling
- Modified ciphertext detection
- Modified authentication tag detection

Expected outcome:

```text
Any tampered ciphertext must fail authentication.
```

---

## Authentication Canary

Required verification:

- Correct credential acceptance
- Wrong credential rejection
- Corrupted canary rejection

Expected outcome:

```text
Only valid credentials unlock the vault.
```

---

## AAD Validation

Required verification:

- Record identity changes
- Parent changes
- Revision changes
- Vault identifier changes

Expected outcome:

```text
AAD mismatch causes authentication failure.
```

---

# Vault Integrity Testing

## Manifest Verification

Tests should validate:

- Authentic manifest generation
- Manifest verification
- Modified record detection

Expected outcome:

```text
Any unauthorized modification invalidates the vault.
```

---

## Tamper Detection

Sample attacks:

### Entry Modification

Modify encrypted records directly.

Expected:

```text
Vault validation failure
```

---

### Metadata Modification

Change metadata values.

Expected:

```text
Vault validation failure
```

---

### Revision Modification

Alter revision information.

Expected:

```text
Vault validation failure
```

---

# Database Testing

## SQLite Integrity Checks

Required verification:

- Healthy database
- Corrupted database
- Missing database pages
- Broken foreign keys

Expected outcome:

```text
Corruption is detected before normal operation.
```

---

## WAL Recovery

Required testing:

- Power-loss simulation
- Interrupted commits
- WAL checkpoint scenarios

Expected outcome:

```text
Database remains consistent.
```

---

# Backup Testing

## Snapshot Testing

Verify:

- Snapshot creation
- Snapshot integrity
- Snapshot completeness

Expected outcome:

- Consistent backup generation
- No data loss

---

## Backup Validation Testing

Verify:

- Corrupted backup rejection
- Healthy backup acceptance
- Metadata verification

Expected outcome:

```text
Unverified backups never become active backups.
```

---

## Backup Rotation Testing

Verify:

- Backup retention
- Backup replacement
- Rotation ordering

Expected outcome:

```text
Older generations remain recoverable.
```

---

# Recovery Testing

## Recovery Validation

Verify:

- Backup authenticity
- Recovery candidate validation
- Authentication checks

Expected outcome:

```text
Invalid backups are rejected.
```

---

## Recovery Preservation

Verify:

- Existing vault preservation
- WAL preservation
- SHM preservation

Expected outcome:

```text
Recovery never destroys the previous vault unexpectedly.
```

---

# Migration Testing

## Legacy Upgrade Testing

Verify:

- Format conversion
- Metadata migration
- Group migration
- Entry migration
- Attachment migration
- History migration

Expected outcome:

```text
Vault content remains intact after migration.
```

---

## Migration Failure Testing

Test:

- Interrupted migration
- Invalid migration data
- Damaged legacy vault

Expected outcome:

```text
Migration aborts safely.
```

---

# Rekey Testing

## Password Change

Verify:

- Password changes
- Authentication canary regeneration
- New key validation

Expected outcome:

```text
Only the new credential unlocks the vault.
```

---

## Key File Changes

Verify:

- Key file replacement
- Missing key file
- Invalid key file

Expected behavior:

- Correct validation
- No silent fallback

---

# Clipboard Security Testing

## Auto Clear

Verify:

- Clipboard timeout
- Clipboard ownership validation
- Clipboard clearing

Expected outcome:

```text
Only SQLitePass clipboard content is removed.
```

---

## Clipboard History Controls

Verify on supported Windows versions:

- Clipboard History exclusion
- Cloud Clipboard exclusion

Check the requested exclusion formats separately from the actual behavior of the Windows history and cloud features. On Linux, test the clipboard manager's treatment of the X11 password-manager hint separately from expiry and ownership.

---

# Auto-Type Testing

## Target Validation

Verify:

- Window matching
- Process matching
- Confirmation requirements

Expected outcome:

```text
Auto-Type targets the intended window.
```

---

## Focus Loss

Verify:

- Alt-Tab events
- Window replacement
- Focus theft scenarios

Expected outcome: Auto-Type stops when a target change is detected. The Windows check-to-input race remains documented.

---

## Session Lock

Verify:

- Lock during Auto-Type

Expected outcome:

```text
Auto-Type is cancelled.
```

---

# Session Security Testing

## Unexpected UI Errors

The Windows and Linux desktop probes inject an exception through the Avalonia dispatcher with a synthetic vault open. They require that the vault view closes, the unlock view becomes available and the password field is empty. The application exits if there is no active vault or cleanup fails. Unobserved task errors schedule the same recovery.

## Manual Lock

Verify:

- Key disposal
- Dialog closure
- Secret cleanup

Expected outcome:

```text
Sensitive data becomes unavailable.
```

---

## Inactivity Lock

Verify:

- Automatic timeout
- User activity detection

Expected outcome:

```text
Vault locks after configured inactivity period.
```

---

## Desktop Lock and Suspend

Run the Windows Win+L diagnostic in an interactive test session. On Linux, check logind and the desktop locker in the target session and check suspend/resume in a test VM. Use a synthetic vault with the inactivity timeout disabled. The vault must remain closed when the desktop unlocks or wakes. The private D-Bus fixture covers logind, Xfce Screensaver and inactivity fallback in CI.

---

# Screen Capture Testing

## WDA_EXCLUDEFROMCAPTURE

Verify on supported Windows versions:

- Window capture blocking
- Screen sharing behavior

Expected outcome:

```text
Protected windows are excluded when supported.
```

---

## Fallback Validation

Verify:

```text
WDA_MONITOR
```

behavior on older systems.

Expected outcome:

```text
Best-effort protection remains available.
```

---

# Import Security Testing

## CSV Testing

Verify:

- Malformed CSV rejection
- Oversized file rejection
- Invalid column handling

Expected outcome:

```text
Invalid input fails safely.
```

---

## JSON Testing

Verify:

- Invalid JSON
- Unexpected structures
- Excessive record counts

Expected outcome:

```text
Imports fail predictably.
```

---

# Memory Handling Testing

## Secret Cleanup

Verify:

- Entry cleanup
- History cleanup
- Custom field cleanup
- Attachment cleanup

Expected outcome:

```text
Sensitive data is released when no longer required.
```

---

## Attachment Buffer Wiping

Verify:

- Buffer overwrite execution

Expected outcome:

```text
Attachment byte arrays are zeroed before disposal.
```

---

# Dependency Security Testing

## Package Review

Before release verify:

- Package updates
- Known vulnerabilities
- Breaking changes

Recommended tools:

- GitHub Dependabot
- GitHub Advisory Database
- NuGet Audit
- Microsoft Defender Vulnerability Management

The Verify workflow's `dotnet list ... --vulnerable --include-transitive` gate is the repository automation for the shared solution; it does not replace human review or external audit.

---

## Build Reproducibility

Verify:

- Locked dependencies
- Deterministic build behavior

Expected outcome:

```text
Builds are repeatable.
```

---

# Release Security Validation

Before release:

## Required

- Build succeeds
- Tests succeed
- Recovery tests succeed
- Integrity tests succeed
- Migration tests succeed
- Rekey tests succeed

## Recommended

- Manual security review
- Dependency review
- Vulnerability review
- Artifact verification
- SBOM and provenance verification for tagged public releases

---

# Manual Security Review Checklist

Before major releases verify:

- Cryptographic changes
- Storage changes
- Recovery changes
- Authentication changes
- Clipboard changes
- Auto-Type changes
- Dependency changes

High-risk modifications should receive additional review.

---

# Future Testing Improvements

Potential future additions:

- Fuzz testing
- Coverage reporting
- Dynamic Application Security Testing (DAST)
- External penetration testing
- Independent cryptographic review

CodeQL and Semgrep are already implemented repository workflows. Dependency scanning automation is also implemented in `Invoke-Ci.ps1` through NuGet audit and should not be classified as future work.

---

# Limitations

Security testing cannot prove:

- Absence of vulnerabilities
- Absence of future defects
- Resistance to unknown attack techniques

Testing increases confidence but does not replace security review.

---

# Conclusion

SQLitePass security testing is designed to validate:

- Cryptography
- Integrity
- Recovery
- Session management
- Clipboard controls
- Auto-Type protections
- Import/export workflows
- Memory handling

The objective is not merely functional correctness, but evidence that security-critical guarantees continue to hold as the project evolves.
