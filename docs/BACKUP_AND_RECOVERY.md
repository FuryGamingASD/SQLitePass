# Backup and Recovery

## Overview

SQLitePass is designed around the assumption that failures will occur.

Hardware fails.

Disks fail.

Power is lost.

Applications crash.

Users make mistakes.

For this reason, backup and recovery are treated as first-class security features rather than optional convenience features.

The objective of the backup architecture is not merely to preserve data, but to ensure that recovery is:

- Verifiable
- Authenticated
- Predictable
- Reversible

SQLitePass intentionally refuses many recovery operations that cannot be validated safely.

---

# Design Goals

The backup and recovery subsystem is designed to:

✅ Prevent accidental vault loss

✅ Minimize corruption risk

✅ Preserve recoverable versions

✅ Validate backups before publication

✅ Validate recovery candidates before saving a recovered copy

✅ Preserve original files during recovery

✅ Detect corruption before data is overwritten

The design intentionally favors safety over convenience.

---

# Recovery Philosophy

SQLitePass follows the principle:

> Never overwrite a known-good vault with a file that has not been verified.

Backups are only published after successful validation.

Recoveries are only performed after successful validation.

Integrity failures stop operations before destructive actions occur.

---

# Backup Architecture

## Primary Backup

Each vault automatically maintains a dedicated backup file:

```text
vault.kpd.backup
```

The backup is generated from a live SQLite snapshot rather than through direct filesystem copying.

This helps ensure consistency while the application is active.

---

## Snapshot Method

SQLitePass uses SQLite's native online backup API.

Advantages include:

- Consistent database state
- Transaction-safe snapshots
- Support for active databases
- Reduced corruption risk

The backup process copies a validated SQLite state rather than an arbitrary file image.

SQLitePass removes the temporary SQLite database and its journals after the backup check finishes. The verified backup and its retained generations are kept.

---

## Atomic Publication

Backups are published using atomic replacement.

The operation completes in one step:

```text
Old Backup
      ↓
New Backup
```

Under normal filesystem operation, either:

- the previous backup remains valid, or
- the new backup becomes active.

Partial backup publication is intentionally avoided by using the platform's file-replacement operation. This is not a guarantee against every storage-device, filesystem, controller or power-loss failure; the previous verified generation should remain protected as an independent copy.

---

# Backup Rotation

SQLitePass automatically keeps multiple recovery generations.

Retention structure:

```text
vault.kpd.backup
vault.kpd.backup.1
vault.kpd.backup.2
vault.kpd.backup.3
```

---

## Purpose of Rotation

Backup rotation provides protection against:

- Accidental deletion
- User mistakes
- Late-discovered corruption
- Failed imports
- Failed modifications
- Rekeying mistakes

Older versions remain available even when newer backups exist.

---

# Backup Validation

## Validation Requirement

A backup is never considered valid merely because it was written successfully.

Before publication SQLitePass verifies:

1. SQLite integrity
2. Metadata integrity
3. Cryptographic integrity

---

## SQLite Integrity Verification

The backup candidate is opened and validated.

Database checks include:

```text
PRAGMA quick_check
PRAGMA foreign_key_check
```

If SQLite reports corruption:

```text
Backup publication fails
```

---

## Metadata Validation

Backup metadata must remain internally consistent.

Validation includes:

- Format compatibility
- Header consistency
- Required metadata presence

Malformed backups are rejected.

---

## Cryptographic Verification

The backup candidate must pass vault authentication checks.

Verification confirms:

- Manifest validity
- Record authentication
- Vault authenticity

Backups failing authentication are discarded.

---

# Backup Status Reporting

SQLitePass tracks backup outcomes independently from save outcomes.

The application therefore distinguishes:

```text
Save Successful
```

from:

```text
Backup Successful
```

A record may be saved while a backup later fails.

Users should monitor backup status separately.

---

# Secondary Backup Locations

## Optional Replication

A secondary backup destination may be configured.

Example:

```text
External USB Drive
NAS Share
Secondary SSD
Dedicated Backup Disk
```

---

## Operation Flow

The process is:

```text
Vault
  ↓
Verified Primary Backup
  ↓
Secondary Backup Copy
```

Only validated backups are mirrored.

---

## Collision Avoidance

Secondary backups use vault-based identifiers to reduce naming collisions.

This helps prevent accidental overwriting of unrelated vault backups.

---

# Recovery Architecture

## Recovery Requirements

Recovery requires:

- A recovery candidate
- The correct vault credential
- The correct key file (if configured)

A recovery operation is not permitted merely because a backup file exists.

Close the vault. On the unlock screen, enter the password and key file valid for the backup and select **Restore from .backup**. SQLitePass finds a backup next to the chosen vault if it exists. You can select a backup directly when there is no matching file. Choose an unused destination on a local filesystem and keep the originals available. During 1.1.3 testing, a VirtualBox shared folder returned an I/O error, and the same files restored on the VM's local disk. [SQLite WAL documentation](https://sqlite.org/wal.html)

---

# Recovery Validation

Before recovery installation:

1. SQLite integrity is verified.
2. Metadata is verified.
3. Authentication succeeds.
4. Vault contents validate successfully.

Only after all checks pass may recovery proceed.

---

## Why Validation Matters

A backup file may exist and still be:

- Corrupted
- Incomplete
- Tampered with
- Copied incorrectly

Validation rejects an unusable backup before it can be published as the recovered vault.

---

# Recovery Execution

The app stages the recovered database at the destination, validates it and publishes it to the selected unused path. Open the new vault with the credential used by the backup and verify representative data before adopting it as your primary copy.

Keep the original vault, its `-wal` and `-shm` files, and the source backup together. The Avalonia process preserves those files. Older WPF recovery used an in-place replacement and retained separately named `.recovery-...` artifacts.

---

# Legacy Vault Migration

## Format Upgrade Snapshots

Before a legacy format conversion:

SQLitePass creates:

```text
.pre-v2-*.backup
```

snapshots.

These snapshots preserve the original vault state before migration.

---

## Upgrade Safety

Migration occurs only after:

- Successful authentication
- Metadata verification
- Structural validation
- Group validation
- Entry validation

Invalid legacy databases are rejected before conversion.

---

# Rekey Recovery

## Purpose

The following rekey operations are considered high-risk:

- Changing the master password
- Replacing or removing the key file
- Changing the KDF parameters

## Pre-Rekey Snapshot

Before re-encryption SQLitePass creates:

```text
.pre-rekey-*.backup
```

snapshots.

These backups preserve access to the vault if rekeying fails or user credentials are entered incorrectly.

---

# Recovery Testing

## Recommended Practice

Backups should be tested periodically.

Do not wait until a real failure occurs.

Recommended process:

1. Copy the current vault.
2. Copy the backup.
3. Open the backup on a test system.
4. Verify authentication.
5. Verify entries.
6. Verify attachments.
7. Verify recovery procedures.

---

## What to Verify

Recovery tests should confirm:

- Password access
- Key file access
- Group hierarchy
- Attachments
- Custom fields
- History entries
- Auto-Type data

A backup that has never been tested should not be assumed to be recoverable.

---

# Recovery Limitations

Recovery cannot reconstruct:

- Deleted backups
- Lost passwords
- Lost key files
- Data overwritten before backup creation
- Corruption already present inside a backup

Recovery quality depends on the quality of the backup being restored.

---

# Disaster Recovery Recommendations

## Minimum Recommendation

Maintain:

- Current vault
- SQLitePass backup
- One offline copy

---

## Recommended

Maintain:

```text
Vault
+
Primary Backup
+
External Backup
```

on separate storage devices.

---

## Strong Recommendation

Follow the 3-2-1 rule:

### Three Copies

- Active vault
- Local backup
- Offsite backup

### Two Media Types

Example:

- SSD
- External USB Drive

### One Offsite Copy

Example:

- Bank safe
- Offline encrypted drive
- Secure backup repository

---

# What Backup Does Not Protect Against

Backup cannot solve:

- Weak passwords
- Malware running on the system
- Keyloggers
- Administrative compromise
- Lost encryption credentials
- Destruction of all backup copies

Backup protects availability, not endpoint security.

---

# Operational Recommendations

## For Individual Users

Recommended:

- Enable automatic backup
- Test recovery quarterly
- Use a key file
- Store an offline copy

---

## For Small Organizations

Recommended:

- Secondary backup location
- Periodic recovery drills
- Versioned backup retention
- Access-controlled backup storage

---

## For High-Value Environments

Recommended:

- Dedicated backup repository
- Offline backup storage
- Documented recovery procedures
- Periodic validation exercises
- Separate storage administrators

---

# Failure Scenarios

## Power Failure During Save

Expected software behavior, subject to the storage device and operating system honoring flush requests:

✅ SQLite WAL recovery

✅ Previous backup remains valid

---

## Power Failure During Backup

Expected software behavior, subject to the storage device and operating system honoring flush requests:

✅ Existing backup remains available

✅ New backup not published

---

## Corrupted Backup

Expected outcome:

✅ Backup validation failure

✅ Backup rejected

✅ Existing backup preserved

---

## Corrupted Active Vault

Expected outcome:

✅ Integrity detection

✅ Recovery available from validated backup

---

## Incorrect Recovery Password

Expected outcome:

✅ Recovery denied

✅ Existing vault preserved

---

# Conclusion

SQLitePass treats backup and recovery as security features rather than operational afterthoughts.

The design combines:

- SQLite durability
- Atomic updates
- Backup rotation
- Cryptographic validation
- Recovery verification
- Rollback preservation

to maximize the probability that a recoverable vault remains available even when common failure conditions occur.

The system cannot eliminate all data-loss scenarios, but it is specifically designed to avoid making recovery failures worse through unsafe automation or unverified replacement operations.
