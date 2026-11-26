# Audit Status

Updated on 26 November 2026 after PR #21 was merged into `main`. This document summarizes the project's internal evidence; the published 1.3.0 verification remains a record of that release only. [Release verification](releases/RELEASE_OUTCOME_1.3.0.md)

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

The 1.3.0 review covers KDBX 4 import, machine policy, clipboard guards and update signatures. PR #21 was merged into `main` as `a7b148b`; it adds responsive workspace sizing, per-vault entry-column widths, an in-flow KDBX destination credential dialog, improved missing-file feedback and a more selective Windows 26300 clipboard guard. The post-merge [Verify run](https://github.com/FuryGamingASD/SQLitePass/actions/runs/45726681637), [CodeQL run](https://github.com/FuryGamingASD/SQLitePass/actions/runs/45726681589) and [Semgrep run](https://github.com/FuryGamingASD/SQLitePass/actions/runs/45726681610) all succeeded. Verify passed its Windows and Linux jobs, including the Linux XWayland probes; release-only installation and upgrade gates were skipped on this push-triggered run. The permitted Windows 26300 clipboard path still needs an interactive native copy test. An independent cryptographic audit and penetration test remain open. [KeePass migration and hardening roadmap](audit/KEEPASS_GAP_ROADMAP.md)

| Area | Status |
|---------|---------|
| Source Code Review | Internal review of the audit baseline and subsequent fixes. Independent review remains open. |
| Architecture Review | Internal review completed; rework recommendations remain |
| Cryptography Review | Internal Review Completed |
| Backup & Recovery Review | Internal review and software fault tests; hardware durability unverified |
| Session Security Review | Manual Windows Win+L, Linux logind, Xfce lock-screen and VM suspend passed. Synthetic tests cover dispatcher errors and lock during dialogs and a cancellable operation. |
| Clipboard Security Review | Native checks cover expiry and clipboard replacement. Interactive Windows 26300 and Xfce Clipman tests reproduced retention. The current Windows guard checks roaming and, on build 26300, history independently. Its permitted 26300 path (both disabled) still needs an interactive retest. |
| Auto-Type Review | Native two-process checks passed on Windows, X11 and XWayland. The Windows P1 race and X11 display trust assumptions remain. |
| Dependency Review | Completed |
| Release checklist | Staged 1.3.0 build, offline signing and verification gates cover both platforms. The 1.1.3 WPF-specific V1–V8 evidence remains in its historical checklist. |
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

- Avalonia `VaultWindow`, `DesktopSession` and `LinuxDesktopSession`
- Historical WPF `LoginWindow`, `MainWindow.Security` and `InactivityLockCoordinator`

Verified:

- Session invalidation
- Automatic lock
- Windows Win+L and supported Linux logind, suspend and Xfce Screensaver lock
- Draft cleanup

Status:

✅ Reviewed

---

## Clipboard Security

Reviewed:

- Shared `ClipboardCoordinator` and Avalonia native clipboard adapter
- Historical WPF `SensitiveClipboardBehavior`

Verified:

- Automatic clearing
- Ownership validation
- Windows Clipboard History and cloud exclusion formats
- Linux X11 password-manager hint

Status:

✅ Reviewed

---

## Auto-Type

Reviewed:

- Shared `AutoTypeService` and the Avalonia selected-entry workflow
- Windows and X11 input adapters
- Historical WPF confirmation dialog

Verified:

- Explicit confirmation
- Process validation
- Window validation
- Foreground/process/title checks before and after injected events
- Focus change interruption

Limitations:

- `SendInput` uses the global keyboard stream; the target can change between validation and an input event.
- Process name and window title do not authenticate the destination or website.
- Windows, X11 and XWayland target and focus checks passed. Elevated Windows targets and broader desktop interaction remain unverified.

Status:

⚠️ Mitigated; residual P1 risk remains

---

## User Interface Hardening

Reviewed:

- Avalonia Windows capture-affinity adapter and sensitive dialogs
- Historical WPF window security and sensitive-window services

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

## Desktop

- Avalonia
- Velopack
- Tmds.DBus.Protocol

Release verification scans the shared solution's transitive NuGet dependencies. Packages include an inventory that covers the native dependencies. [Third-party notices](THIRD_PARTY_NOTICES.md) · [Update signing and transition](UPDATE_SIGNING.md)

Dependency monitoring should continue through routine maintenance.

---

# Release Assessment

Version 1.3.0 adds a read-only KeePass import, reduces plaintext retention and introduces signed updates with an expiring freshness checkpoint. The native gates exercise the 1.2.2 transition and an update between packages that contain the verification key. [Verification roadmap](audit/KEEPASS_GAP_ROADMAP.md) · [Update signing and transition](UPDATE_SIGNING.md)

The evidence covers internal review and project verification. An independent audit and penetration test remain open. A07 remains a P1 Windows Auto-Type race. The project does not recommend SQLitePass as the sole store for high-value or privileged credentials.

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
| Session Management | Manual Windows Win+L, Linux logind, Xfce and suspend, plus lock during a dialog and a cancellable operation on both platforms. |
| Clipboard | Native expiry and replacement tests on both platforms, plus interactive retention tests on Windows 26300 and Xfce Clipman. The latest conditional Windows 26300 allow path is pending native retest. |
| Auto-Type | Automated regressions and native focus-loss checks. The P1 Windows check-to-input race remains. |
| UI Hardening | Shared Avalonia workflow tests and Windows capture-affinity flags. Broader native DPI and assistive-technology checks remain open. |
| Memory Handling | Managed-buffer cleanup reviewed; complete string/runtime zeroization is not guaranteed |
| Third-Party Components | NuGet advisory scan and license inventory; no independent native-binary provenance audit |
| Deployment Environment | Outside source-review scope |

---

# Conclusion

The source review and automated suite provide evidence for specific controls, especially authenticated storage, recovery checks, session invalidation and backup handling. They do not establish that SQLitePass is free of vulnerabilities or suitable for a particular organization's risk profile.

The principal outstanding assurance limits are:

- A07 remains a P1 mitigated Auto-Type risk.
- Windows checks for elevated Auto-Type targets, actual capture, broader DPI and assistive technology remain open. Clipboard and session-lock evidence applies to the recorded Windows and Linux configurations.
- Hardware failure scenarios, independent cryptographic review and penetration testing are not covered.

The earlier internal source review identified no critical cryptographic implementation flaw within its reviewed scope. [Verification roadmap](audit/KEEPASS_GAP_ROADMAP.md) · [Desktop verification guide](CROSS_PLATFORM.md)
