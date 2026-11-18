# SQLitePass

SQLitePass is a modern local password manager for Windows built with .NET 10, WPF and SQLite.

SQLitePass stores vault records in SQLite. Vault Format 2 combines AES-256-GCM authenticated encryption, Argon2id key derivation, authenticated integrity verification, validated backup recovery and session controls. These mechanisms reduce specific risks but do not establish immunity to data loss, endpoint compromise or hardware failure.

SQLitePass is designed around four primary goals:

- Confidentiality of stored credentials
- Integrity verification of vault contents
- Resistance to corruption and data loss
- Recoverability after failures

This project is open source and distributed under GPL-3.0.

> SQLitePass is not independently audited and does not claim formal certification. Security claims are limited to currently implemented and publicly reviewable source code.

---

# Security Highlights

## Cryptography

SQLitePass uses modern authenticated cryptography throughout the vault:

- AES-256-GCM authenticated encryption
- Argon2id memory-hard key derivation
- Random 96-bit encryption nonces
- 128-bit authentication tags
- Optional key file support
- Vault-specific encryption contexts (AAD)

Passwords, usernames, URLs, notes, attachments, custom fields and entry history are encrypted before being written to disk.

---

## Vault Integrity Verification

SQLitePass verifies more than confidentiality.

Every vault includes:

- authenticated metadata
- authenticated encrypted records
- authenticated manifest verification

Unauthorized modification of:

- entries
- groups
- hierarchy
- metadata
- database records

is detected during vault opening and validation.

Vault integrity failures place the vault into a protected state rather than silently continuing operation.

---

## Backup and Recovery

SQLitePass implements multiple recovery mechanisms:

- SQLite WAL durability
- Atomic online database snapshots
- Automatic verified backups
- Backup rotation
- Secondary backup destinations
- Pre-migration recovery snapshots
- Pre-rekey recovery snapshots

Backup files are validated before being published as recoverable backups.

Recovery validates vault integrity and authentication before replacing the active vault.

---

## Secure Session Management

SQLitePass implements several protection mechanisms while a vault is unlocked:

- Automatic inactivity locking
- Lock on Windows session lock
- Auto-Type interruption on focus changes
- Auto-Type cancellation during session locking
- Sensitive UI clearing on lock
- Credential generation invalidation during lock transitions

Sensitive dialogs, password controls and editable content are cleared when the session is locked.

---

## Clipboard Protection

Clipboard operations are treated as sensitive actions.

SQLitePass:

- automatically clears copied secrets
- prevents accidental retention where supported
- requests exclusion from Windows Clipboard History
- requests exclusion from Cloud Clipboard synchronization
- tracks clipboard ownership before clearing

Clipboard contents remain accessible to the operating system and other software while present.

---

## Auto-Type Safety Limits

SQLitePass provides KeePass-style Auto-Type while attempting to minimize accidental credential disclosure.

Auto-Type:

- requires explicit target selection
- requires user confirmation
- validates the target process
- validates the window title
- checks the target before and after each injected input

Windows `SendInput` writes to the global keyboard input stream rather than an authenticated window handle. The checks reduce accidental misdirection, but a focus change can race with an input event. Auto-Type is best-effort and must not be treated as authentication of a website or destination.

---

## Screen Capture Protection

When enabled, SQLitePass attempts to prevent screenshots and screen recording of sensitive windows through native Windows APIs:

- WDA_EXCLUDEFROMCAPTURE
- WDA_MONITOR fallback

This is best-effort protection and does not defend against:

- cameras
- memory inspection
- compromised operating systems
- privileged malware

---

## Secure Memory Handling

SQLitePass attempts to minimize retention of sensitive data by:

- clearing password controls
- clearing text controls
- clearing attachment buffers
- disposing encryption keys
- removing sensitive history data

Because SQLitePass runs on .NET, complete erasure of immutable managed strings cannot be guaranteed.

---

## Threat Model

SQLitePass is primarily designed to protect against:

- theft of vault files
- unauthorized database modification
- accidental corruption
- power-loss interruption
- software crashes
- accidental credential disclosure
- operational recovery failures

SQLitePass is not designed to protect against:

- administrator-level compromise
- kernel-level malware
- memory dumping by privileged attackers
- hardware keyloggers
- physical observation of credentials

---

SQLitePass 1.1 uses Vault Format 2.

Changes introduced in Format 2 include:

- full-vault authenticated encryption
- authenticated integrity manifests
- record revisions
- key epochs
- validated recovery workflows
- hardened import/export handling
- defensive clipboard behavior
- improved Auto-Type validation

For security limitations and vulnerability reporting procedures see SECURITY.md.
