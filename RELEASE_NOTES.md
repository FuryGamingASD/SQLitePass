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
- Native Windows Auto-Type validation
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

### Memory Handling

Introduced:

- Key-file buffer sanitization
- Additional memory cleanup on lock

### Data Integrity

Introduced:

- Database integrity verification
- Faster corruption detection
- Backup-assisted recovery

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
- TOTP seeds

### Browser CSV Import

Support for:

- Chrome
- Edge
- Firefox
- Brave
- Opera

## Performance

- High-performance bulk imports.
- Large import acceleration.
- Transaction-based import processing.

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

- Fixed minimization race conditions.
- Added additional safety validation.

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

### Resilience

Introduced:

- WAL durability
- Automatic backup generation
- Recovery-focused architecture

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

Versions 1.1.0–1.1.2

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

Vault Format 2 (introduced in 1.1.0) represents the current architectural baseline and is the recommended format for all active deployments.
