# Security Architecture

## Overview

SQLitePass is a local-first Windows password manager designed to prioritize:

1. Confidentiality
2. Integrity
3. Recoverability
4. Operational safety

The architecture is intentionally designed to resist common causes of credential loss, including accidental corruption, interrupted writes, storage failures, application crashes and unsafe recovery procedures.

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

This provides:

- atomic commits
- crash recovery
- reduced corruption risk
- durable transaction persistence
