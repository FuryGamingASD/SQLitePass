# Audit Status

Updated: 18 November 2026, after publication of SQLitePass 1.1.2 and during local 1.1.3 remediation. The minimize-cancels-Auto-Type change is not part of a published release. This is an internal evidence summary, not an independent audit.

## Purpose

This document summarizes the current security review status of SQLitePass.

It is intended to provide transparency regarding:

- Verified controls
- Tested functionality
- Known limitations
- Outstanding review activities
- External validation status

This document does not constitute an independent certification.

It reflects the current state of publicly available evidence.

---

# Current Assessment

| Area | Status |
|---------|---------|
| Source Code Review | Internal review completed against audit baseline; current branch changes reviewed, not independently audited |
| Architecture Review | Internal review completed; rework recommendations remain |
| Cryptography Review | Internal Review Completed |
| Backup & Recovery Review | Internal review and software fault tests; hardware durability unverified |
| Session Security Review | Automated/internal review; native Windows lock checklist pending |
| Clipboard Security Review | Code review and synthetic tests; native history/cloud behavior pending |
| Auto-Type Review | Mitigated; P1 race remains and native validation is pending |
| Dependency Review | Completed |
| Native Windows release checklist | Not completed |
| Third-Party Audit | Not Performed |
| Penetration Test | Not Performed |
| Bug Bounty Program | Not Available |

---

# Source Components Reviewed

The following security-sensitive components have been reviewed:

## Cryptography

Reviewed:

- CryptoEngine
- MasterKey

Verified:

- Argon2id
- AES-256-GCM
- Nonce generation
- Authentication tags
- Key disposal

Status:

✅ Reviewed

---

## Storage

Reviewed:

- SqliteStorageProvider
- LegacyVaultReader

Verified:

- Encrypted payload storage
- Authenticated manifests
- Context-bound encryption
- Record revision handling

Status:

✅ Reviewed

---

## Recovery

Reviewed:

- VaultBackup

Verified:

- Backup validation
- Recovery validation
- Atomic replacement
- Snapshot creation

Status:

✅ Reviewed

---

## Session Security

Reviewed:

- LoginWindow
- MainWindow.Security
- InactivityLockCoordinator

Verified:

- Session invalidation
- Automatic lock
- Lock on Windows lock
- Draft cleanup

Status:

✅ Reviewed

---

## Clipboard Security

Reviewed:

- ClipboardCoordinator
- SensitiveClipboardBehavior

Verified:

- Automatic clearing
- Ownership validation
- Clipboard History exclusion
- Cloud Clipboard exclusion

Status:

✅ Reviewed

---

## Auto-Type

Reviewed:

- AutoTypeService
- AutoTypeConfirmDialog

Verified:

- Explicit confirmation
- Process validation
- Window validation
- Foreground/process/title checks before and after injected events
- Focus change interruption

Limitations:

- `SendInput` uses the global keyboard stream; the target can change between validation and an input event.
- Process name and window title do not authenticate the destination or website.
- Native Windows target/focus/modifier checks have not been completed.

Status:

⚠️ Mitigated; residual P1 risk remains

---

## User Interface Hardening

Reviewed:

- WindowSecurityService
- SensitiveWindowService

Verified:

- Screen capture protection
- Sensitive control cleanup
- Undo history removal

Status:

✅ Reviewed

---

## Password Generation

Reviewed:

- PasswordGenerator

Verified:

- Cryptographic randomness
- Fisher-Yates shuffling
- Character-class enforcement

Status:

✅ Reviewed

---

# Cryptographic Assessment

Current assessment:

| Component | Status |
|------------|---------|
| Argon2id | Acceptable |
| AES-256-GCM | Acceptable |
| Random Number Generation | Acceptable |
| Context Binding (AAD) | Acceptable |
| Manifest Authentication | Acceptable |

No critical cryptographic implementation flaws were identified during source review.

---

# Security Strengths

The following controls were verified:

✅ Authenticated encryption

✅ Manifest-based integrity validation

✅ Per-record authenticated context

✅ Session invalidation

✅ Verified backups

✅ Verified recovery

✅ Atomic updates

✅ Clipboard hardening

⚠️ Auto-Type validation with a P1 check-to-input race

✅ Screen capture protection

✅ Attachment buffer wiping

✅ Secret cleanup workflows

---

# Known Limitations

The following limitations are currently known.

## Managed Strings

SQLitePass uses .NET.

Immutable strings cannot be forcibly erased from memory.

Impact:

Low

Status:

Accepted limitation.

---

## Endpoint Trust Assumption

SQLitePass assumes the operating system itself remains trusted.

Impact:

Expected

Status:

Part of threat model.

---

## Auto-Type Constraints

Auto-Type relies on operating-system keyboard APIs.

Impact:

Expected

Status:

Documented.

---

## Clipboard Constraints

Clipboard content remains observable while present.

Impact:

Expected

Status:

Documented.

---

# Outstanding Independent Validation

The following activities remain incomplete.

## Independent Cryptography Audit

Status:

❌ Not performed

Priority:

High

---

## Independent Application Security Audit

Status:

❌ Not performed

Priority:

High

---

## External Penetration Test

Status:

❌ Not performed

Priority:

Medium

---

## Public Bug Bounty

Status:

❌ Not available

Priority:

Low

---

# Dependency Review Status

Current reviewed dependencies:

## Core

- Microsoft.Data.Sqlite
- Konscious.Security.Cryptography.Argon2

## UI

- Velopack
- Hardcodet.NotifyIcon.Wpf

On 18 November 2026, `dotnet list package --vulnerable --include-transitive` reported no known NuGet advisories across five solution projects. This scan does not cover non-NuGet components, every bundled native binary, the operating system or undisclosed vulnerabilities. See the [local 1.1.3 verification summary](evidence/release-readiness-1.1.3.json).

Dependency monitoring should continue through routine maintenance.

---

# Release Assessment

Current release line:

```text
1.1.x
```

Assessment:

The release line has internal source and automated-test evidence. It has not received an independent audit or penetration test. A07 remains a P1 Auto-Type race, and the native Windows checklist has not been completed. This document does not recommend SQLitePass as the sole store for high-value or privileged credentials.

---

# Enterprise Considerations

Organizations handling:

- Domain Administrator credentials
- Privileged access management
- Production infrastructure secrets
- Regulatory-controlled environments

should consider:

- Independent review
- Recovery testing
- Backup verification
- Endpoint security controls

before relying exclusively on SQLitePass.

---

# Review Coverage

Evidence scope by area (not a security score):

| Area | Coverage |
|---------|----------|
| Cryptography | Internal source review; no independent protocol/implementation review |
| Storage | Automated and synthetic integrity/recovery tests; no physical failure testing |
| Recovery | Software fault tests; hardware durability not established |
| Session Management | Automated WPF/session tests; native Windows lock checklist pending |
| Clipboard | Implementation tests; Windows history/cloud behavior pending |
| Auto-Type | Fake-input regressions; P1 check-to-input race remains, native checks pending |
| UI Hardening | In-memory layout/contrast checks; native DPI and assistive-technology checks pending |
| Memory Handling | Managed-buffer cleanup reviewed; complete string/runtime zeroization is not guaranteed |
| Third-Party Components | NuGet advisory scan and license inventory; no independent native-binary provenance audit |
| Deployment Environment | Outside source-review scope |

---

# Conclusion

The source review and automated suite provide evidence for specific controls, especially authenticated storage, recovery checks, session invalidation and backup handling. They do not establish that SQLitePass is free of vulnerabilities or suitable for a particular organization's risk profile.

The principal outstanding assurance limits are:

- A07 remains a P1 mitigated Auto-Type risk.
- Native Windows behavior for Auto-Type, clipboard, screen capture, locking, DPI and assistive technology is unverified.
- Hardware failure scenarios, independent cryptographic review and penetration testing are not covered.

No critical cryptographic implementation flaw was identified in the cited internal source review. That statement is limited to its reviewed commit and scope; it is not a conclusion that no critical or other vulnerabilities exist in the current release. See [the 1.1.2 outcome](releases/RELEASE_OUTCOME_1.1.2.md) and [the 1.1.3 readiness checklist](releases/RELEASE_READINESS_1.1.3.md) for current release evidence.
