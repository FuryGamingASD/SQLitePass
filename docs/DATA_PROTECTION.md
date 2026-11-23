# Data Protection

## Purpose

This document describes how SQLitePass protects, stores, processes and disposes of sensitive data.

The goal is to provide transparency regarding:

- Data collection
- Data processing
- Data storage
- Data protection mechanisms
- Data retention
- Privacy limitations

SQLitePass is a local-first password manager.

The application is specifically designed to minimize disclosure of credential data while maintaining usability and recoverability.

---

# Privacy Overview

SQLitePass follows several privacy principles:

## Data Minimization

SQLitePass collects only the information required to operate the vault.

The application does not require:

- User accounts
- Cloud identities
- Vendor-managed profiles
- Remote synchronization services

---

## Local Processing

All cryptographic operations occur locally.

Examples:

- Key derivation
- Encryption
- Decryption
- Password generation
- Backup validation
- Recovery validation

Credential material is never intentionally transmitted to external services.

---

## User-Controlled Storage

Users control:

- Vault location
- Backup location
- Secondary backup location
- Retention policies

SQLitePass does not automatically export vault contents to external services.

---

# Data Classification

## Highly Sensitive Data

Examples:

- Passwords
- Recovery codes
- API keys
- SSH keys
- VPN credentials
- Service account passwords

These items are encrypted before storage.

---

## Sensitive Data

Examples:

- Usernames
- URLs
- Secure notes
- Custom fields
- Entry history
- Attachments

These items are also encrypted within Vault Format 2.

---

## Operational Metadata

Examples:

- Vault identifier
- Entry identifier
- Group identifier
- Revision number
- KDF parameters
- Timestamps
- Parent relationships

This information may remain visible without unlocking the vault and is required for integrity verification, recovery and storage management.

---

# Protected Data

## Vault Entries

Protected entry data includes:

- Title
- Username
- Password
- URL
- Notes
- Auto-Type configuration
- History
- Custom fields
- Attachments

Vault Format 2 encrypts entry and group contents before storage.

---

## Custom Fields

Custom fields may contain arbitrary sensitive content.

Examples:

- PIN codes
- License keys
- MFA recovery codes
- Private references

Custom field values are encrypted within the vault and cleared when sensitive entry data is disposed.

---

## Attachments

Attachments are encrypted while stored in the vault.

Examples:

- Certificates
- Text files
- Configuration exports
- Documentation

Attachment data is decrypted only when needed and attachment buffers are explicitly overwritten when released.

---

# Cryptographic Protection

SQLitePass protects stored data using:

| Control | Purpose |
|----------|----------|
| Argon2id | Key Derivation |
| AES-256-GCM | Authenticated Encryption |
| AAD | Record Binding |
| Manifest Verification | Vault Integrity |

The implementation protects both confidentiality and integrity of stored content.

---

# Password Protection

## Master Password

The master password is never stored inside the vault.

SQLitePass stores:

- Salt
- Key derivation parameters
- Authentication artifacts

but not the plaintext password.

---

## Key Files

When configured, a key file becomes part of the credential material required to unlock the vault.

Access requires possession of both:

```text
Password
+
Key File
```

when the dual-factor mode is enabled.

---

# Clipboard Protection

Clipboard operations present a privacy risk because copied secrets temporarily leave vault protection.

SQLitePass implements several controls.

---

## Automatic Clipboard Clearing

Copied secrets automatically expire after a configurable time interval.

Default:

```text
12 seconds
```

Supported range:

```text
3 to 300 seconds
```



---

## Clipboard Ownership Tracking

Clipboard contents are cleared only if SQLitePass still owns them.

This prevents accidental deletion of unrelated user clipboard content.

---

## Clipboard History Exclusion

Where supported by Windows, SQLitePass requests exclusion from:

- Clipboard History
- Cloud Clipboard Synchronization

The Linux X11 adapter advertises a password-manager hint. A clipboard manager can retain the data if it ignores the hint.

---

## Clipboard Limitations

Clipboard contents remain visible to:

- The operating system
- Privileged applications
- Malware with appropriate permissions

while present in the clipboard buffer.

Clipboard protection reduces exposure but cannot guarantee complete confidentiality.

---

# Screen Capture Protection

SQLitePass supports optional screen-capture protection on Windows.

Protected windows attempt to use:

```text
WDA_EXCLUDEFROMCAPTURE
```

with fallback to:

```text
WDA_MONITOR
```

when necessary.

---

## Scope

The protection is intended to reduce disclosure through:

- Snipping Tool
- Windows Graphics Capture
- Many screen-recording utilities
- Various conferencing platforms

on supported Windows versions.

---

## Limitations

Screen-capture protection does not protect against:

- Cameras
- Smartphones
- Memory inspection
- Kernel malware
- Administrative compromise

Protection should be considered best-effort rather than absolute.

---

# Session Protection

## Automatic Lock

SQLitePass can lock automatically after inactivity.

Supported range:

```text
0-60 minutes
```

A value of:

```text
0
```

disables inactivity locking.

---

## Desktop Session Lock

With automatic desktop locking enabled, SQLitePass locks on Windows session lock, including Win+L. On Linux the app responds to supported logind lock and suspend events and to Xfce Screensaver activation. Unlocking the desktop leaves the vault locked.

---

## Vault Lock Effects

When a vault locks, SQLitePass:

- Disposes cryptographic keys
- Closes sensitive dialogs
- Clears UI content
- Cancels Auto-Type operations
- Invalidates in-flight credential operations



---

# Memory Protection

SQLitePass attempts to reduce retention of sensitive content in memory.

Examples include:

- Encryption key disposal
- Attachment buffer wiping
- History cleanup
- Custom-field cleanup
- UI control cleanup



---

# Memory Limitations

SQLitePass is built on .NET.

Because .NET strings are immutable:

- String contents cannot be overwritten directly.
- Garbage collection timing cannot be controlled.
- Complete memory erasure cannot be guaranteed.

This limitation applies to managed desktop applications in general.

---

# Diagnostic Data

## Application Settings

SQLitePass stores local settings under:

```text
%APPDATA%\SQLitePass
```

Examples:

- Recent vault paths
- Window preferences
- Language preferences
- Backup settings

Settings are not encrypted.

---

## Logging

Diagnostic logging can be enabled for troubleshooting.

Designed behavior:

- Credentials are not logged.
- Secrets are not intentionally written to log files.
- Raw credential values are excluded.

Logs remain operational data and should still be protected appropriately.

---

# Import and Export Privacy

## Supported Imports

- KeePass KDBX 4.0 and 4.1 into a new vault
- KeePass CSV
- Browser CSV
- Bitwarden JSON

CSV and JSON import files can contain plaintext credentials. KeePass KDBX files are encrypted. From the unlock screen, enter the intended new vault credentials and choose **Import KeePass KDBX into a new vault**. Provide the source credentials when asked, review the preserved and unsupported data, and choose a new `.kpd` path. The import reads the source without changing it. Keep the source until you have verified the new vault and made a backup. [Import memory handling](MEMORY_HANDLING.md) · [KeePass migration evidence](audit/KEEPASS_GAP_ROADMAP.md)

---

## CSV Export

CSV exports contain plaintext information.

Exported data includes:

- Titles
- Usernames
- Passwords
- URLs
- Notes



---

## Export Limitations

CSV exports do not preserve:

- Attachments
- History
- Custom fields
- Full vault integrity protections

CSV exports should be used for migration purposes only.

---

# Backup Privacy

Backups contain encrypted vault data and remain protected by the same cryptographic controls as the active vault.

However:

- Historical backups remain accessible if corresponding credentials remain known.
- Older backups may preserve older data.
- Retention policies remain the responsibility of the user or organization.

---

# End User Responsibilities

Users should:

✅ Choose strong master passwords

✅ Use key files when appropriate

✅ Enable automatic locking

✅ Enable screen-capture protection

✅ Store backups securely

✅ Verify recovery procedures

✅ Protect export files

---

# Organizational Responsibilities

Organizations should additionally:

✅ Use BitLocker

✅ Protect backup repositories

✅ Control administrator access

✅ Maintain endpoint protection

✅ Periodically test recovery

✅ Define backup retention policies

✅ Protect key files separately from vault files

---

# Regulatory Considerations

SQLitePass is a local software application and does not itself determine regulatory compliance.

Organizations remain responsible for evaluating requirements under:

- GDPR
- NIS2
- ISO/IEC 27001
- SOC-related controls
- Internal security policies

Privacy and compliance outcomes depend on deployment practices, endpoint security and operational controls.

---

# What SQLitePass Protects

SQLitePass is designed to protect against:

- Vault theft
- Backup theft
- Offline analysis
- Database tampering
- Credential disclosure from storage
- Clipboard persistence
- Accidental screen capture
- Common recovery failures

---

# What SQLitePass Does Not Protect Against

SQLitePass is not designed to protect against:

- Administrative compromise
- Privileged malware
- Memory-dump acquisition
- Kernel-level compromise
- Hardware keyloggers
- Physical observation
- Compromised operating systems

Additional endpoint and organizational controls are required for those threats.

---

# Implementation References

The design and limitations described here are summarized in the project's [cryptography](CRYPTOGRAPHY.md), [security architecture](SECURITY_ARCHITECTURE.md), [memory handling](MEMORY_HANDLING.md), [backup and recovery](BACKUP_AND_RECOVERY.md), and [threat model](THREAT_MODEL.md) documents. Those summaries are not independent attestations; consult the current source and the release-specific verification report for implementation status.

---

# Conclusion

SQLitePass implements a defense-in-depth data protection strategy based on:

- Modern authenticated cryptography
- Local-first processing
- Controlled clipboard handling
- Screen-capture protection
- Session hardening
- Verified backups
- Recovery validation

The application minimizes unnecessary exposure of sensitive information while remaining transparent about the limitations imposed by local operating systems, managed runtimes and endpoint security conditions.
