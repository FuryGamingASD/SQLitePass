# Security Policy

## Supported Versions

The current supported release line is:

| Version | Status |
|----------|----------|
| 1.1.x | Supported |
| 1.0.x | Unsupported |
| Earlier versions | Unsupported |

SQLitePass 1.1 introduces Vault Format 2, authenticated vault integrity verification, validated recovery workflows, record revisions and authenticated encrypted storage.

Earlier 1.0 releases contain known architectural limitations and recovery defects documented in the project audit and remediation reports.

Always maintain an independent backup before upgrading an existing vault.

---

# Reporting a Vulnerability

## Preferred Method

Use GitHub Private Vulnerability Reporting whenever available:

https://github.com/FuryGamingASD/SQLitePass/security/advisories/new

If private reporting is unavailable:

- Open a GitHub issue requesting a confidential contact channel.
- Do not publish exploit details for unfixed vulnerabilities.
- Do not attach real vaults.
- Do not attach master passwords.
- Do not attach key files.
- Do not attach memory dumps.
- Do not attach production credentials.

Use only synthetic datasets for reproduction.

---

## Recommended Report Content

Please provide:

- SQLitePass version
- Vault format version
- Windows version
- Installation type (Portable or Installed)
- Steps to reproduce
- Expected behavior
- Actual behavior
- Confidentiality impact
- Integrity impact
- Availability impact
- Screenshots using non-sensitive data only

---

# Security Architecture Overview

SQLitePass is designed around four security goals:

1. Confidentiality
2. Integrity
3. Recoverability
4. Operational Safety

The application prioritizes preserving both vault confidentiality and recoverability during unexpected failures.

---

# Cryptography

## Key Derivation

SQLitePass derives encryption keys using:

- Argon2id
- Password-based key derivation
- Optional key-file integration

Default parameters:

| Parameter | Default |
|------------|------------|
| Memory | 64 MB |
| Iterations | 3 |
| Parallelism | 2 |
| Key Length | 256-bit |

Unsupported or excessive KDF parameters are rejected before derivation.

---

## Encryption

SQLitePass uses:

- AES-256-GCM
- 96-bit random nonces
- 128-bit authentication tags

Encryption provides both:

- Confidentiality
- Integrity protection

Sensitive data is encrypted before storage.

Protected data includes:

- Passwords
- Usernames
- URLs
- Notes
- Custom fields
- Attachments
- Entry history

---

## Authenticated Encryption Context

Vault records are not encrypted as anonymous blobs.

Each encrypted record is bound to contextual metadata including:

- Vault identity
- Record type
- Record identifier
- Parent relationship
- Revision number

This prevents unauthorized record relocation or substitution attacks inside the database.

---

# Vault Integrity Verification

SQLitePass validates vault integrity in addition to decryption.

The vault contains:

- Authenticated metadata
- Authenticated encrypted records
- Authenticated manifest verification

Unauthorized modification of:

- Groups
- Entries
- Relationships
- Metadata
- Encrypted payloads

is detected during vault validation.

If integrity verification fails:

- Write operations are blocked.
- Read operations are restricted.
- Existing encrypted data is preserved.
- Existing backups remain available for recovery.

Integrity controls are designed to prevent silent corruption from becoming silent credential loss.

---

# Secure Storage Design

Vault Format 2 uses encrypted serialized records stored inside SQLite.

SQLitePass intentionally prioritizes:

- Integrity
- Durability
- Recoverability

over obscurity.

The following information remains visible without the master credential:

- Vault identifiers
- Record identifiers
- Parent relationships
- Revision counters
- KDF settings
- Timestamps
- Encrypted payload sizes

This metadata visibility is a documented privacy trade-off. Its exposure may still matter for some users' threat models and should not be dismissed as irrelevant.

---

# Backup and Recovery Security

## Automatic Backup

SQLitePass automatically maintains:

- Current verified backup
- Backup.1
- Backup.2
- Backup.3

using SQLite online snapshots and atomic file replacement.

---

## Backup Verification

A backup is not published until:

1. SQLite integrity checks succeed.
2. Metadata validation succeeds.
3. Cryptographic verification succeeds.

Backups failing verification are rejected.

---

## Recovery Validation

Recovery operations require:

- Valid vault backup
- Correct password
- Correct key file (if configured)

The candidate recovery file is validated before replacing the active vault.

SQLitePass refuses recovery while an active vault session is open.

---

## Additional Safety Snapshots

SQLitePass creates dedicated snapshots before:

- Rekey operations
- Vault-format migrations

These snapshots are preserved intentionally and may contain older encryption formats or credentials.

Administrators should define a retention and destruction policy for historical backups.

---

# Clipboard Security

SQLitePass treats clipboard operations as sensitive actions.

Clipboard copies:

- Use an automatic timeout
- Attempt exclusion from Clipboard History
- Attempt exclusion from Cloud Clipboard synchronization

The clipboard is only cleared if SQLitePass still owns the copied content.

This prevents accidental deletion of unrelated user clipboard data.

---

## Clipboard Limitations

Clipboard protection cannot prevent:

- Malware running on the system
- Clipboard monitoring
- Other operating-system or application clipboard consumers
- Clipboard History or Cloud Clipboard behavior that is not excluded by the current Windows configuration
- A recipient application retaining data after a paste

The application requests exclusion flags where supported, but those flags are best-effort. Native Clipboard History and Cloud Clipboard behavior has not been validated on every supported Windows configuration.

SQLitePass is not designed to protect against:

- Administrator-level or kernel-level compromise
- Malware with access to the unlocked process
- Memory dumping by a privileged attacker
- Hardware keyloggers or physical observation

These threats require endpoint and operational controls beyond the vault application.

---

# Current Known Risks

- Auto-Type validates the foreground window, process and title before and after injected events, but Windows `SendInput` uses the global keyboard stream. The target can change between validation and an input event; this remains a P1 risk.
- The 1.1.3 candidate cancels Auto-Type when the vault window is minimized, including when lock-on-minimize is disabled. This narrows one scenario but does not remove the global-input race. Auto-Type is retained at the user's direction. KeePass documents the same general transfer limitation: its window-title matching cannot uniquely identify a target, so the user must verify the receiving window. That design comparison does not make `SendInput` atomic or remove SQLitePass's check-to-input race ([KeePass Auto-Type](https://keepass.info/help/base/autotype.html), [KeePass security notes](https://keepass.info/help/kb/sec_issues.html)). Native Windows validation remains pending.
- The application and installer are unsigned, as previously agreed. Windows may show reputation or publisher warnings.
- There has been no independent cryptographic audit or penetration test. Hardware-level power-loss and storage-failure behavior is not certified.

For current release evidence and the complete manual test plan, see [the audit status](docs/AUDIT_STATUS.md) and [1.1.3 readiness report](docs/releases/RELEASE_READINESS_1.1.3.md).
