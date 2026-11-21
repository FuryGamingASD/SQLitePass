# Release Notes

This document contains the complete public release history of SQLitePass.

Historical release notes describe functionality and expectations at the time they were published.

Where differences exist, the following documents take precedence:

- README.md
- SECURITY.md
- SECURITY_ARCHITECTURE.md
- CRYPTOGRAPHY.md
- DATA_PROTECTION.md
- THREAT_MODEL.md
- BACKUP_AND_RECOVERY.md

Security guarantees should always be interpreted according to the current documentation rather than historical marketing language.

---

# SQLitePass 1.1.3

Released on 21 November 2026.

- Auto-Type cancels when the vault window is minimized, even with automatic locking on minimize disabled.
- Unexpected dispatcher errors lock the vault or shut down the application. A vault lock can also complete safely while the entry editor is closing.
- Activation and shutdown requests received during startup are preserved. Uninstall removes the Windows startup entry when it belongs to that installation.
- Backup cleanup removes temporary SQLite journal files. The vault format remains unchanged.

The Windows package is unsigned. Auto-Type retains the documented P1 risk associated with global keyboard input. The release outcome records the verified assets, the VM results and the checks deferred at publication. [Release notes](RELEASE_NOTES_v1.1.3.md) · [Release outcome](docs/releases/RELEASE_OUTCOME_1.1.3.md)

---

# SQLitePass 1.1.2

Released: 2026-11-18

## Overview

Maintenance and security-hardening release.

No vault format changes were introduced.

No migration is required.

Existing vaults remain fully compatible.

## Security Improvements

- Updated Microsoft.Data.Sqlite to 11.0.0.
- Refreshed locked dependency graph and license notices.
- Improved concurrent vault conflict detection.
- Added dedicated conflict reporting for stale revision scenarios.
- Strengthened Auto-Type target verification.
- Auto-Type now validates:
  - Target window
  - Process identity
  - Window title
  after every injected event.
- Improved cryptographic error reporting.
- Improved settings file concurrency handling.

## Reliability Improvements

- Enhanced drag-and-drop error handling.
- Improved settings update behavior under file-lock contention.
- Improved user feedback for concurrent changes.

## Compatibility

- No vault format change.
- No data migration required.
- Existing preferences remain compatible.

## Known Limitations

The following remain outside completed validation scope:

- Native Windows clipboard validation
- Native Windows Auto-Type validation; `SendInput` uses the global keyboard stream, so a focus change can race with a checked target
- Native Win+L, screen-capture, DPI, Narrator and high-contrast validation
- Hardware-level power-loss and storage-failure validation
- Independent security review
- Independent penetration testing

---

# SQLitePass 1.1.1

Released: 2026-11-17

## Overview

Hotfix release focused on application lifecycle management and Windows integration.

No vault format changes were introduced.

## Fixes

### Launch Behavior

- Launching SQLitePass while already running now restores the existing window.
- Hidden tray-only sessions reopen correctly.
- Existing unlock dialogs are brought to foreground.

### Update Reliability

- Updates now request clean process shutdown.
- Uninstall operations correctly wait for application exit.
- Tray icon persistence issues were corrected.

## Compatibility

- Vault format unchanged.
- Existing vaults unchanged.
- Existing preferences unchanged.

---

# SQLitePass 1.1.0

Released: 2026-11-17

## Major Release

Version 1.1.0 introduced Vault Format 2.

This release represents the largest architectural evolution in the project history.

## Security Architecture

### Vault Format 2

Introduced:

- Authenticated encrypted storage
- Authenticated manifests
- Record revision tracking
- Key epochs
- Improved integrity verification

### Migration Protection

Introduced:

- Authenticated migration workflows
- Pre-migration snapshots
- Recovery validation
- Rollback preservation

### Session Security

Introduced:

- Enhanced lock handling
- Sensitive data cleanup
- Improved Auto-Type validation
- Clipboard coordination

### Data Integrity

Introduced:

- Manifest validation
- Integrity verification
- Recovery validation
- Safe recovery procedures

## User Experience Improvements

### Navigation

- Folder path visibility
- Global filters
- Expiry filters
- Tag filtering

### Editing

- Draft-based editing
- Validation workflows
- Change detection
- Safe cancellation

### Secrets Handling

- Timed reveal protection
- Secret hiding on lock
- Attachment visibility controls

### Scalability

- Paged loading
- Memory budgeting
- On-demand entry detail loading

## Import & Export

Introduced:

- Atomic imports
- Cancellation support
- Progress reporting
- Background CSV export

## Accessibility

Introduced:

- Vector icon system
- Screen reader names
- Improved contrast handling
- Responsive layouts

## Testing

- 152 automated tests passed.
- Release verification completed.
- Recovery workflows validated.
- Build reproducibility validated.

## Upgrade Guidance

Vault 1.0 users should:

1. Create an independent backup.
2. Upgrade using SQLitePass 1.1.
3. Complete migration.
4. Verify contents.
5. Verify recovery procedures.

---

# SQLitePass 1.0.2

Released: 2026-11-12

## Overview

Security and reliability release.

## Security

### Screen Capture Protection

Introduced:

- SetWindowDisplayAffinity integration
- WDA_EXCLUDEFROMCAPTURE support
- WDA_MONITOR fallback

This is best-effort exclusion for supported capture paths. It does not prevent all screenshots, screen recording, malware, remote-control software or privileged capture. The native Windows behavior has not been validated for every configuration.

### Memory Handling

Introduced:

- Key-file buffer sanitization
- Additional memory cleanup on lock

These actions clear selected buffers and release references where possible. They do not guarantee that all secret copies or immutable .NET strings are removed from memory or unavailable to a memory-dump tool.

### Data Integrity

Introduced:

- Database integrity verification
- Backup-candidate validation

SQLite structural checks do not establish cryptographic authenticity or detect every logical problem. A valid recovery copy must exist and authenticate; recovery from physical storage failure is not guaranteed.

## Recovery

### Recycle Bin

Introduced:

- Soft deletion
- Empty recycle bin
- Restore workflows

## Import Capabilities

Introduced:

### Bitwarden JSON Import

Support for:

- Credentials
- URLs
- Notes
- Custom fields
- recognized custom fields

### Browser CSV Import

Support for:

- Chrome
- Edge
- Firefox
- Brave
- Opera

Imports cover recognized source formats rather than every field or future export layout. Inspect the imported records and keep the original export until the result is verified. Unsupported Bitwarden item types are not automatically native SQLitePass features.

## Performance

- Transaction-based bulk imports.

No fixed speedup is promised; performance varies with vault size, storage and backup settings.

---

# SQLitePass 1.0.1

Released: 2026-11-11

## Overview

User interface modernization release.

## User Interface

### Unlock Window

Introduced:

- Single-card login experience
- Improved layout consistency
- Simplified navigation

### Themes

Introduced:

- Refined Light Theme
- Deep Dark Theme
- Improved contrast handling

### Navigation

Introduced:

- Improved search
- Toolbar simplification
- Better folder management

## Stability Fixes

### Password Generator

- Fixed startup crash conditions.

### Auto-Type

- Fixed the selected-entry crash triggered by a minimize-state change.
- Added target validation. Version 1.1.3 also cancels Auto-Type on minimize. The race between checking the foreground window and sending global keyboard input remains a P1 limitation.

### Auto-Lock

- Improved background behavior.
- Reduced focus-stealing lock prompts.

## Security

Continued use of:

- AES-256-GCM
- Argon2id
- SQLite WAL
- Automatic backups

---

# SQLitePass 1.0.0

Released: 2026-11-10

## Initial Release

First public release of SQLitePass.

## Core Features

### Secure Storage

Introduced:

- SQLite-based vault storage
- AES-256-GCM encryption
- Argon2id key derivation

Encrypted payloads do not imply that every structural field or metadata value is hidden; consult the current Data Protection documentation.

### Resilience

Introduced:

- SQLite WAL recovery for supported transaction failures
- Automatic backup generation
- Recovery-focused architecture

WAL and backup behavior do not guarantee recovery after every hardware, filesystem or power failure.

### Password Management

Introduced:

- Entry management
- Custom fields
- Attachments
- History tracking

### Auto-Type

Introduced:

- Global Auto-Type
- Keyboard automation
- Window targeting

### Clipboard

Introduced:

- Timed clearing
- Secure copying

### Password Generator

Introduced:

- Cryptographic random generation
- Password quality indicators

### Localization

Introduced:

- English
- Italian
- French
- German
- Spanish

### Distribution

Introduced:

- Installer package
- Portable package
- Velopack update support

---

# Support Policy

## Supported Releases

```text
1.1.x
```

## Historical Releases

```text
1.0.x
```

Historically documented but no longer recommended for new deployments.

Users should migrate to the latest supported version whenever practical.

---

# Project Evolution Summary

SQLitePass evolved through three major phases:

## Phase 1 – Initial Password Manager

Version 1.0.0

Focus:

- Encryption
- SQLite resilience
- Local-first storage

## Phase 2 – UX and Security Hardening

Versions 1.0.1–1.0.2

Focus:

- Screen capture protection
- Memory handling
- Import capabilities
- User interface improvements

## Phase 3 – Authenticated Vault Architecture

Versions 1.1.0–1.1.3

Focus:

- Vault Format 2
- Manifest validation
- Recovery validation
- Backup verification
- Session security
- Architectural hardening

---

# Conclusion

SQLitePass has evolved from a resilient local password manager into a security-focused platform centered on:

- Authenticated encryption
- Integrity verification
- Recovery validation
- Defensive session handling
- Verified backup workflows

Vault Format 2 (introduced in 1.1.0) is the current supported architectural baseline. Whether SQLitePass is suitable for a deployment depends on its threat model, recovery practice and the documented limitations; this history does not certify it for every use.
