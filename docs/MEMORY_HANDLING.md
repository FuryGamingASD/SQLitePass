# Memory Handling

## Overview

SQLitePass is designed to minimize the lifetime of sensitive information in memory wherever practical.

Because SQLitePass is implemented using .NET and WPF, memory protection must balance:

- Security
- Functionality
- Reliability
- Performance

The application therefore adopts a defense-in-depth strategy that combines:

- Key disposal
- Buffer wiping
- Session locking
- UI cleanup
- Clipboard expiration
- Context invalidation

while acknowledging the limitations of managed runtimes.

---

# Design Goals

SQLitePass memory handling is intended to:

✅ Reduce accident retention of secrets

✅ Minimize lifetime of decrypted credentials

✅ Reduce exposure during normal operation

✅ Remove sensitive data when sessions end

✅ Limit credential visibility after lock events

SQLitePass does not attempt to:

❌ Guarantee complete elimination of secrets from RAM

❌ Defeat administrative memory acquisition

❌ Defeat kernel-level malware

❌ Defeat operating-system compromise

---

# Memory Threat Model

Memory protections primarily target:

- Accidental disclosure
- Stale UI content
- Clipboard persistence
- Crash recovery artifacts
- Long-lived decrypted objects
- User workstation lock scenarios

The architecture assumes the operating system itself remains trusted.

---

# Encryption Key Lifecycle

## Key Creation

Vault encryption keys are derived from:

- Master Password
- Optional Key File

using Argon2id.

The derived key is never stored on disk.

---

## Key Usage

The derived key exists only for the duration of the authenticated vault session.

During use, the key is stored in managed memory as a byte array.

The key remains available only while required for:

- Vault read operations
- Vault write operations
- Backup validation
- Recovery validation
- Rekey operations

---

## Key Disposal

When a vault session ends or the cryptographic engine is disposed:

```text
CryptographicOperations.ZeroMemory()
```

is applied to the internal encryption key buffer before release.

This attempts to overwrite key material before the associated object becomes eligible for garbage collection.

---

# Master Credential Handling

## Password Entry

Master passwords are entered through protected password controls.

The application attempts to keep passwords out of standard text input controls whenever possible.

---

## Password Reveal

When the user explicitly requests password visibility:

- the password becomes visible inside a standard UI text control,
- user convenience is prioritized,
- memory exposure temporarily increases.

Password reveal is therefore a convenience feature and should be used only when necessary.

---

## Key Files

When key files are used:

- the file is read only when required,
- its contents participate in key derivation,
- temporary buffers are cleared after processing where possible.

---

# Entry Data Lifecycle

## Decryption

Vault entries are decrypted only after:

1. Authentication succeeds
2. Vault integrity validation succeeds
3. Session validation succeeds

Decryption occurs in memory immediately before use.

---

## Sensitive Fields

Sensitive entry fields include:

- Title
- Username
- Password
- URL
- Notes
- Auto-Type sequences
- Custom field values
- History snapshots

These values are decrypted for use and are cleared from active models when those models are released or the session is locked. Managed strings and copies held by the runtime may persist until garbage collection; this does not guarantee that plaintext exists only while actively required.

---

## Entry Cleanup

SQLitePass actively clears entry data through dedicated cleanup routines.

Cleanup attempts to remove:

- Passwords
- Usernames
- URLs
- Notes
- Tags
- Auto-Type sequences
- Custom-field values
- Historical snapshots

before the entry leaves active use.

---

# Attachment Memory Handling

## Attachment Loading

Encrypted attachments are decrypted only when loaded.

Attachment contents exist in memory as byte arrays.

---

## Buffer Sanitization

Before attachment data is released:

```text
CryptographicOperations.ZeroMemory()
```

is applied directly to the attachment buffer.

This is one of the strongest memory protections available within managed .NET applications because byte arrays can be overwritten in place.

---

## Attachment Limits

To reduce excessive memory pressure:

- individual attachments are limited to 20 MB,
- oversized attachments are rejected.

This improves both performance and memory safety.

---

# History Handling

SQLitePass supports entry history.

Historical revisions may contain:

- Usernames
- Passwords
- URLs
- Notes

When historical records are released, stored plaintext values are overwritten with empty content before disposal.

History retention is intentionally limited to reduce long-term memory growth.

---

# User Interface Memory Protection

## Sensitive Dialog Cleanup

When sensitive windows close or the vault is locked:

- PasswordBox controls are cleared
- TextBox controls are cleared
- TextBlock controls are cleared
- Dialogs are closed

The cleanup mechanism traverses the user-interface tree and removes visible credential data.

---

## Undo History Removal

When text controls are cleared:

```text
Undo support is disabled before cleanup
```

This prevents previously visible secrets from remaining available through user-interface undo operations.

---

## Draft Removal

Vault locks also remove:

- Active drafts
- Entry editing content
- Pending authentication data

This reduces accidental exposure after unlock interruptions.

---

# Session Lock Handling

## Windows Session Lock

When Windows is locked:

```text
Win + L
```

SQLitePass responds immediately.

Actions include:

- Password field clearing
- Dialog termination
- Session invalidation
- Active operation cancellation

---

## Credential Generation Tracking

Authentication-sensitive operations maintain an internal generation counter.

Whenever a security-sensitive lock event occurs:

- existing generations become invalid,
- pending operations are rejected.

This prevents stale authenticated actions from continuing after a lock event.

---

# Clipboard Handling

## Centralized Clipboard Service

Sensitive clipboard operations are processed through a dedicated coordinator.

Features include:

- Ownership tracking
- Automatic expiration
- Retry logic
- Centralized management

---

## Clipboard Ownership Validation

Clipboard clearing occurs only if SQLitePass still owns the clipboard contents.

This avoids deleting unrelated user clipboard data copied after the original credential.

---

## Automatic Cleanup

Clipboard contents expire automatically after a configurable interval.

Supported values:

| Setting | Range |
|----------|---------|
| Clipboard Timeout | 3-300 seconds |

Default:

```text
12 seconds
```

---

## Clipboard History Controls

Where supported by Windows, SQLitePass requests exclusion from:

- Clipboard History
- Cloud Clipboard Synchronization

These features reduce accidental persistence but do not provide malware protection.

---

# Auto-Type Memory Considerations

Auto-Type requires temporary access to:

- Usernames
- Passwords
- Custom fields

These values exist in memory before simulated keyboard entry.

SQLitePass attempts to:

- minimize retention,
- validate the destination window before and after input events,
- cancel execution when the target changes; the 1.1.3 candidate also cancels it when the vault window is minimized.

Credentials are not permanently cached by the Auto-Type subsystem.
Because `SendInput` uses the global keyboard stream, a target change can race with an input event. Auto-Type is best-effort and does not authenticate a destination.

---

# Search and Paging

SQLitePass loads entry data on demand.

The user interface avoids retaining the entire vault in memory simultaneously.

Benefits include:

- reduced memory consumption
- shorter secret lifetime
- reduced attack surface
- better performance on large vaults

Large collections such as:

- Attachments
- History
- Custom fields

are loaded when required rather than continuously.

---

# Memory Cleanup During Errors

If cryptographic errors occur:

- temporary plaintext buffers are cleared,
- failed operations are aborted,
- invalid content is discarded.

Sensitive data is not intentionally retained after cryptographic failures.

---

# Managed Runtime Limitations

## Immutable Strings

SQLitePass runs on .NET.

Many sensitive values are represented using:

```text
System.String
```

String objects are immutable.

Consequently:

- their contents cannot be overwritten directly,
- zeroization is not possible,
- garbage collection timing cannot be controlled.

Replacing a string with an empty value removes the reference but cannot guarantee immediate erasure of previous memory contents.

---

## Garbage Collection

The .NET garbage collector ultimately controls:

- object lifetime,
- memory reuse,
- physical reclamation.

SQLitePass cannot guarantee when released objects are removed from memory.

---

## Operating-System Caches

Windows itself may maintain:

- input buffers
- display buffers
- clipboard buffers
- memory pages

outside of SQLitePass control.

These behaviors are governed by the operating system.

---

# Threats Mitigated

SQLitePass memory protections help reduce exposure from:

- Walk-up workstation access
- Forgotten unlocked sessions
- Clipboard persistence
- Dialog remnants
- Stale edit windows
- Long-lived attachment buffers
- User-interface history retention

---

# Threats Not Mitigated

SQLitePass memory protections do not defend against:

- Administrative access
- Kernel-level malware
- Memory dumping tools
- Debugger attachment
- Physical RAM acquisition
- Hypervisor inspection
- Compromised operating systems
- Hardware keyloggers

Additional endpoint security controls are required for those threats.

---

# Security Recommendations

For high-value environments:

## Recommended

- Enable Auto-Lock
- Enable Lock on Windows Lock
- Use a Key File
- Avoid unnecessary password reveal
- Use screen capture protection
- Keep clipboard timeout short
- Lock the vault when not actively used

## Strongly Recommended

- Full-disk encryption
- Microsoft Defender for Endpoint
- Credential Guard where available
- BitLocker
- Endpoint application control
- Regular backup verification

---

# Conclusion

SQLitePass implements a layered memory-protection strategy focused on reducing the amount of time secrets remain exposed in memory and user-interface components.

While no managed desktop application can completely eliminate memory exposure, SQLitePass actively disposes cryptographic keys, sanitizes attachment buffers, clears sensitive interface elements, invalidates stale sessions and limits clipboard persistence wherever practical.

The architecture should be viewed as a defense-in-depth approach that reduces risk while acknowledging the limitations inherent to the .NET runtime and the Windows operating system.
