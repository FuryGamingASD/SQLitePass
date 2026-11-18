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

Security testing is performed continuously throughout development and before public releases.

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

