# Security Architecture

## Overview

SQLitePass is a local-first Windows password manager designed to prioritize:

1. Confidentiality
2. Integrity
3. Recoverability
4. Operational safety

The architecture includes controls intended to reduce credential-loss risk from software errors, interrupted transactions and unsafe recovery. It cannot ensure survival of every storage-device, filesystem, controller or power failure; see the recovery documentation for these limits.

SQLitePass does not depend on remote infrastructure, cloud services, browser extensions or external synchronization services.

The application operates entirely on locally controlled vault files.

---

# Design Principles

SQLitePass follows several architectural principles:

## Defense in Depth

No single security mechanism is trusted in isolation.

Protection is achieved through multiple independent controls:

- Argon2id key derivation
- AES-256-GCM authenticated encryption
- Authenticated manifest verification
- Database integrity checks
- Verified backup publication
- Verified recovery procedures
- Session hardening
- Clipboard hardening
- Screen capture protection

---

## Fail Safe by Default

When an unexpected condition is detected:

- Access is denied.
- Writes are aborted.
- Integrity failures stop normal operation.
- Existing data is preserved.
- Recovery paths remain available.

SQLitePass intentionally prefers disruption over silent data corruption.

---

## Recovery Before Convenience

The architecture assumes that:

- disks fail,
- users make mistakes,
- power loss occurs,
- applications crash.

For this reason, the vault design prioritizes recoverability and validated backups over aggressive optimization.

---

# Vault Architecture

## Vault Format 2

Vault Format 2 stores encrypted content inside SQLite while maintaining authenticated integrity verification.

The vault contains:

- Metadata
- Groups
- Entries
- History
- Attachments
- Custom fields

Entry and group contents are stored as authenticated encrypted payloads.

---

## Storage Layout

The vault uses SQLite as a storage engine.

Core tables include:

```text
sp_metadata
sp_groups
sp_entries
```

Group and entry content are stored as encrypted binary payloads.

The database therefore functions primarily as a structured storage container rather than a location for plaintext credential storage.

---

## Metadata Exposure

Some metadata intentionally remains visible without unlocking the vault.

Examples include:

- Vault identifier
- Entry identifier
- Group identifier
- Parent relationships
- Revision numbers
- Timestamps
- KDF parameters
- Encrypted payload sizes

This metadata visibility is required to support validation, migration and recovery operations.

No credential content is stored in plaintext.

---

# Cryptographic Architecture

## Master Credential Model

A vault may be protected by:

- Master Password
- Key File
- Master Password + Key File

When both are present they are combined before key derivation.

The resulting composite secret becomes the input to the key derivation process.

---

## Key Derivation

SQLitePass uses:

```text
Argon2id
```

Default parameters:

| Parameter | Default Value |
|------------|---------------|
| Memory | 64 MB |
| Iterations | 3 |
| Parallelism | 2 |
| Key Length | 256 bit |

Vault-specific KDF parameters are stored in metadata and validated before use.

Unsupported values are rejected.

---

## Encryption

SQLitePass uses:

```text
AES-256-GCM
```

Properties:

| Component | Value |
|------------|---------|
| Key Size | 256 bit |
| Nonce | 96 bit |
| Authentication Tag | 128 bit |
| Random Source | OS Cryptographic RNG |

A unique random nonce is generated for every encryption operation.

---

## Authenticated Additional Data

Encrypted records are bound to contextual information through Additional Authenticated Data (AAD).

Context includes:

- Vault ID
- Record type
- Record identifier
- Parent identifier
- Revision number

This prevents encrypted records from being relocated or substituted without detection.

---

# Integrity Architecture

## Vault Manifest

SQLitePass implements authenticated vault integrity verification.

The integrity mechanism protects:

- Vault metadata
- Groups
- Entries
- Record revisions
- Storage structure

An authenticated manifest is generated from the vault contents and must validate successfully before normal access is granted.

---

## Tamper Detection

Unauthorized modification of:

- encrypted payloads
- identifiers
- parent relationships
- revisions
- metadata

causes validation failure.

Vault operations are blocked until a valid copy or backup is restored.

---

## Database Integrity Validation

Before critical operations SQLitePass performs:

```text
PRAGMA quick_check
PRAGMA foreign_key_check
```

SQLite integrity validation is treated as separate from cryptographic integrity validation.

Both must succeed.

---

# Durability Architecture

## SQLite WAL

SQLitePass uses:

```text
PRAGMA journal_mode=WAL
```

and

```text
PRAGMA synchronous=FULL
```

These SQLite settings request:

- atomic commits
- crash recovery
- reduced corruption risk
- durable transaction persistence

Actual durability still depends on the filesystem and storage stack honoring flush requests. This is not evidence of hardware-level power-loss protection.

---

## Atomic Operations

Database modifications use SQLite transactions and are committed atomically within the database transaction boundary.

For failures handled by SQLite's transaction mechanism, vault updates either:

- complete successfully, or
- leave the previous valid state intact.

Partial commits are intentionally avoided.

---

## Revision Control

Each protected record contains:

- Revision number
- Identity
- Ownership information

Updates require matching revision information.

Concurrent write conflicts therefore result in rejection rather than silent overwrite.

---

# Backup Architecture

## Online Snapshots

Backups are created using SQLite's native online backup mechanism.

This allows:

- consistent snapshots
- active database backup
- validated copy generation

without depending on direct file duplication.

---

## Backup Verification

Backup candidates are verified before publication.

Verification includes:

1. SQLite integrity validation
2. Metadata validation
3. Cryptographic verification

Invalid backups are discarded.

---

## Backup Retention

SQLitePass maintains:

```text
vault.kpd.backup
vault.kpd.backup.1
vault.kpd.backup.2
vault.kpd.backup.3
```

Older generations provide rollback options when corruption, user error or migration issues are discovered after the fact.

---

## Secondary Backup Targets

An optional secondary backup destination may be configured.

The secondary backup:

- receives the already validated backup
- is published atomically
- uses vault-ID derived naming

to avoid accidental collisions.

---

# Recovery Architecture

## Verified Recovery

Recovery requires:

- a backup file
- the correct password
- the correct key file (if enabled)

The backup is validated before replacement.

Invalid recovery candidates are rejected.

---

## Recovery Preservation

Before installation of a recovered vault:

- the previous vault is preserved
- existing WAL files are preserved
- existing SHM files are preserved

This approach avoids destructive recovery behavior.

---

## Migration Recovery

Additional safety snapshots are generated before:

- format migrations
- key changes

allowing rollback if required.

---

# Session Security

## Locking System

Vaults may be locked:

- manually
- through inactivity timeout
- through Windows session lock

When locking occurs:

- keys are disposed
- dialogs are closed
- drafts are cleared
- Auto-Type is cancelled

Version 1.1.3 also cancels active Auto-Type when the vault window is minimized, including when lock-on-minimize is disabled.

An unhandled dispatcher exception locks the active vault before the exception is marked handled. The application shuts down if there is no active vault window or it cannot lock the vault immediately. An unobserved task failure schedules the same response on the dispatcher before being marked observed.

---

## Session Invalidation

Credential-related operations use generation tracking.

A session lock invalidates in-flight operations that may still hold credential context.

This prevents stale operations from continuing after a lock event.

---

# Clipboard Architecture

## Managed Clipboard Operations

Sensitive clipboard operations are centralized through a dedicated coordinator.

Features include:

- automatic timeout
- ownership tracking
- safe clearing
- retry logic
- centralized copy handling

---

## Clipboard History Controls

SQLitePass attempts to prevent copied secrets from entering:

- Windows Clipboard History
- Cloud Clipboard synchronization

where supported by the operating system.

---

## Limitations

Clipboard content remains visible to:

- the operating system
- privileged software
- malware with sufficient access

Clipboard protection is intended to reduce accidental exposure, not defeat system compromise.

---

# Auto-Type Architecture

## Target Validation

Auto-Type requires:

- explicit user invocation
- explicit destination selection
- confirmation by the user

---

## Continuous Verification

Before and after injected input events SQLitePass checks:

- foreground window
- process identity
- window title

These checks reduce the chance of accidental misdirection. Windows `SendInput` writes to the global keyboard stream and cannot atomically target the verified window handle, so a focus change can race with an input event. This is a best-effort control, not origin authentication or a guarantee that credentials cannot reach another window.

---

## Security Assumptions

Auto-Type assumes the endpoint is trusted.

The mechanism cannot provide protection against:

- keyloggers
- accessibility hooks
- malicious kernel drivers
- compromised operating systems

---

# User Interface Hardening

## Screen Capture Protection

Protected windows may use:

```text
WDA_EXCLUDEFROMCAPTURE
```

with fallback to:

```text
WDA_MONITOR
```

on supported Windows versions.

This reduces accidental exposure through many screen recording and screen sharing mechanisms.

---

## Sensitive Window Cleanup

When sensitive windows close or the vault locks:

- PasswordBox content is cleared
- TextBox content is cleared
- TextBlock content is cleared
- Undo history is disabled before clearing

This reduces secret retention inside the active user interface.

---

# Memory Handling

## Secret Cleanup

SQLitePass actively attempts to remove secrets when they are no longer required.

Cleanup includes:

- Entry passwords
- Usernames
- Notes
- URLs
- Custom-field values
- Entry history data
- Attachment buffers
- Encryption keys

Attachment buffers are explicitly overwritten before disposal.

---

## Managed Runtime Limitation

SQLitePass runs on .NET.

Because immutable strings are controlled by the runtime:

- string contents cannot be forcibly erased,
- garbage collection timing cannot be controlled.

Users operating in environments where privileged memory acquisition is expected should consider this limitation part of the threat model.

---

# Threat Boundaries

SQLitePass is designed to protect against:

- Vault theft
- Offline database analysis
- Accidental corruption
- Interrupted writes
- Power failures
- Application crashes
- User recovery mistakes
- Basic tampering of vault contents

SQLitePass is not designed to protect against:

- Administrative compromise
- Kernel compromise
- Malware running with full local privileges
- Memory dumping attacks
- Hardware keyloggers
- Physical observation attacks
- Compromised operating systems

These threats require additional endpoint security controls beyond the scope of a password manager.

---

# Security Review Status

Current status:

| Item | Status |
|--------|----------|
| Public Source Code | ✅ |
| Source Review Performed | ✅ |
| Independent Cryptographic Audit | ❌ |
| External Security Audit | ❌ |
| Independent Penetration Test | ❌ |
| Formal Certification | ❌ |
| Bug Bounty Program | ❌ |

Future third-party review is strongly encouraged.

---

# Conclusion

SQLitePass combines authenticated encryption, authenticated integrity verification, validated recovery workflows, defensive clipboard handling, secure session management and resilient SQLite storage to create a recovery-focused local password manager architecture.

The architecture deliberately favors data preservation and integrity verification over silent continuation after faults.

No software can eliminate all risk. SQLitePass aims to make supported integrity and recovery failures visible and to preserve validated copies where possible; it does not ensure that every failure is recoverable.
