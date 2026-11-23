# Threat Model

## Purpose

This document describes the threat model used by SQLitePass.

The goal of a threat model is to clearly define:

- What SQLitePass attempts to protect.
- Which threats are mitigated.
- Which threats are partially mitigated.
- Which threats are explicitly out of scope.

A threat model is not a guarantee of security.

It defines the security assumptions under which the application is expected to operate.

---

# Security Objectives

SQLitePass is designed to protect:

1. Vault Confidentiality
2. Credential Integrity
3. Vault Availability
4. Recovery Capability

The architecture prioritizes preventing silent credential loss while maintaining strong confidentiality protections.

---

# Protected Assets

The following assets are considered security-sensitive.

## Credentials

Including:

- Usernames
- Passwords
- API Keys
- Recovery Codes
- SSH Credentials
- VPN Credentials
- Service Accounts

---

## Protected Fields

Including:

- Notes
- Custom Fields
- Auto-Type Data
- Historical Revisions

---

## Attachments

Including:

- Configuration Files
- Certificates
- License Files
- Documents
- Secure Notes

---

## Cryptographic Material

Including:

- Master Password
- Key File
- Derived Encryption Key
- Authentication Data

---

## Backup Data

Including:

- Active Vault
- Backup Vaults
- Recovery Snapshots
- Migration Snapshots

---

# Architectural Assumptions

SQLitePass assumes:

✅ The user controls the endpoint.

✅ The user controls vault storage.

✅ The user controls available backups.

✅ The user can maintain password secrecy.

✅ The operating system functions correctly.

SQLitePass does not assume:

❌ Network connectivity.

❌ Cloud services.

❌ Server-side trust.

❌ Vendor-hosted infrastructure.

---

# Attacker Models

## A1 – Opportunistic File Thief

### Capabilities

The attacker can obtain:

- Vault file
- Backup files
- Archived vault copies

The attacker cannot obtain:

- Master Password
- Key File

### Examples

- Lost USB stick
- Stolen laptop drive
- Backup theft
- Cloud storage compromise

---

### Security Goal

Prevent vault decryption.

---

### Mitigations

- Argon2id key derivation
- AES-256-GCM encryption
- Authentication canary validation

---

### Residual Risk

Depends primarily on:

- Password strength
- Key-file usage

---

# A2 – Database Tamperer

### Capabilities

The attacker can modify:

- SQLite files
- Metadata
- Records
- Revisions

The attacker cannot derive encryption keys.

---

### Examples

- Local file corruption
- Malicious database editing
- Recovery manipulation attempts

---

### Security Goal

Detect unauthorized modification.

---

### Mitigations

- AES-GCM authentication
- Additional Authenticated Data (AAD)
- Manifest validation
- Record verification
- Metadata verification

---

### Expected Outcome

Vault opening fails.

---

# A3 – Accidental Corruption

### Capabilities

No malicious intent.

Examples include:

- Power loss
- Disk failure
- Operating-system crashes
- Filesystem corruption
- Application termination

---

### Security Goal

Preserve recoverability.

---

### Mitigations

- SQLite WAL
- Synchronous commits
- Automatic backups
- Backup validation
- Recovery validation
- Backup rotation

---

### Expected Outcome

Recovery remains possible.

---

# A4 – Casual Workstation Observer

### Capabilities

The attacker can view the screen while the user is present.

Examples:

- Shoulder surfing
- Shared office environment
- Remote screen sharing

---

### Security Goal

Reduce accidental exposure.

---

### Mitigations

- Password masking
- Timed reveal controls
- Auto lock
- Screen capture protection

---

### Residual Risk

Screen observation remains possible.

---

# A5 – Clipboard Observer

### Capabilities

The attacker can access clipboard contents.

Examples:

- Shared workstation
- User error
- Clipboard history
- Cloud clipboard synchronization

---

### Security Goal

Reduce clipboard persistence.

---

### Mitigations

- Timed clipboard clearing
- Clipboard ownership tracking
- Clipboard history exclusion
- Cloud clipboard exclusion
- On Linux X11, reject copies when a clipboard-manager selection owner or the detected Xfce Clipman D-Bus service is active

---

### Residual Risk

Clipboard data remains exposed while present. Windows privacy flags are not reliable on every tested Windows build, so SQLitePass blocks copies on the affected build and whenever sync is enabled. On Linux X11, third-party manager detection covers the standard manager selection and Xfce Clipman; a configured session bus that cannot be queried also blocks copying. An unknown manager or one that starts after the check may still retain a copy.

---

# A6 – Auto-Type Interference

### Capabilities

The attacker attempts to capture credentials during Auto-Type by manipulating window focus or destination windows.

Examples:

- Focus hijacking
- Fake login windows
- Application switching

---

### Security Goal

Prevent accidental credential delivery.

---

### Mitigations

- Explicit target selection
- Confirmation dialog
- Process verification
- Window-title verification
- Foreground, process and title checks before and after injected events

---

### Residual Risk

Windows `SendInput` uses a global keyboard stream and the target can change between a check and an event. This remains a P1 risk. Linux Auto-Type uses the shared X11 display and requires a trusted local session. SQLitePass cancels on detected focus changes, session lock and vault minimization. The user must confirm the destination. Process identity and window title provide context for this decision. [Platform behavior](CROSS_PLATFORM.md#auto-type-in-the-shared-app) · [KeePass Auto-Type](https://keepass.info/help/base/autotype.html)

### Browser form filling (not implemented)

The desktop flow can type into a browser, but its process and window title checks do not authenticate the active page's web origin. A browser integration should be a separate, explicit extension flow: user-invoked temporary `activeTab` access, exact canonical-origin matching, confirmation in SQLitePass, and rechecking the same tab and origin immediately before filling. Use native messaging with an exact extension allowlist; do not add a local HTTP/WebSocket listener or auto-submit a form. Even with those checks, the approved page receives the credentials and can read them. Browser support therefore adds a new trust boundary and needs separate packaging and threat review. See the official [Chrome `activeTab`](https://developer.chrome.com/docs/extensions/develop/concepts/activeTab) and [native messaging](https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging) documentation and [Edge native messaging](https://learn.microsoft.com/en-us/microsoft-edge/extensions/developer-guide/native-messaging).

---

# A7 – Malicious Local User

### Capabilities

The attacker has local account access but does not possess vault credentials.

Examples:

- Shared workstation
- Unauthorized user profile access

---

### Security Goal

Prevent vault access.

---

### Mitigations

- Encryption
- Authentication validation
- Key-file support

---

### Residual Risk

Depends on password quality and endpoint configuration.

---

# A8 – Backup Theft

### Capabilities

The attacker obtains:

- Current backups
- Historical backups
- Archived recovery copies

---

### Security Goal

Prevent credential exposure.

---

### Mitigations

- Encrypted backup contents
- Authentication validation
- Same cryptographic protections as active vaults

---

### Residual Risk

Older backups may contain older formats or policies.

---

# A9 – Insider Administrator

### Capabilities

The attacker possesses:

- Administrative permissions
- Process access
- Debugging capability
- Memory inspection tools

Examples:

- Local Administrator
- Domain Administrator
- Incident responder
- Malware running as administrator

---

### Security Goal

Reduce exposure where practical.

---

### Mitigations

- Session locking
- Key disposal
- Secret cleanup
- Clipboard cleanup

---

### Residual Risk

HIGH

Administrative compromise is considered outside the application's primary protection boundary.

---

# A10 – Malware

### Capabilities

The attacker can execute code locally.

Examples:

- Trojan
- Infostealer
- RAT
- Banking malware

---

### Security Goal

Reduce accidental exposure.

---

### Mitigations

Limited.

SQLitePass assumes endpoint trust.

---

### Residual Risk

HIGH

Malware can potentially observe:

- Keystrokes
- Clipboard contents
- Screen contents
- Memory contents

depending on privileges.

---

# A11 – Kernel-Level Adversary

### Capabilities

The attacker controls:

- Kernel drivers
- Hypervisor access
- Advanced monitoring

---

### Examples

- Rootkits
- Malicious signed drivers
- Forensic acquisition

---

### Security Goal

None.

---

### Status

Out of Scope.

SQLitePass cannot reasonably defend against a compromised operating-system kernel.

---

# A12 – Physical Observation

### Capabilities

The attacker can physically observe the user.

Examples:

- Cameras
- Shoulder surfing
- Screen photography

---

### Security Goal

Reduce visible exposure.

---

### Mitigations

- Password masking
- Timed reveals
- Locking controls

---

### Residual Risk

Physical observation remains possible.

---

# Availability Threats

SQLitePass treats availability as a security concern.

---

## Threat: Accidental Deletion

Mitigations:

- Recycle Bin
- Backup Rotation
- Recovery Snapshots

---

## Threat: Failed Migration

Mitigations:

- Pre-v2 snapshots
- Validation before migration

---

## Threat: Failed Rekey

Mitigations:

- Pre-rekey snapshots
- Recovery preservation

---

## Threat: Database Corruption

Mitigations:

- WAL
- Integrity verification
- Validated backups

---

# Trust Boundaries

## Inside Trust Boundary

Components trusted by SQLitePass:

- Local application process
- Authenticated vault session
- Operating-system cryptographic providers
- SQLite storage engine

---

## Outside Trust Boundary

Components not trusted:

- Other processes
- Browsers
- Extensions
- Cloud services
- Removable storage
- Network services

---

# A13 – Update Feed Tampering

### Capabilities

The attacker can replace an update feed and its package on the configured release host, for example after compromise of the repository or release-upload credentials. This does not assume ordinary HTTPS transport interception.

### Security Goal

Ensure that an update package was authorized by the SQLitePass publisher independently of the host that serves it.

### Current Checks

The Avalonia updater verifies a detached RSA/SHA-256 signature against a public key pinned in the client. The signed manifest binds the package identity, channel, version, source commit, filename, byte length and SHA-256 digest. The client verifies downloaded packages and cached packages before Velopack can extract or apply them, uses fixed HTTPS GitHub release URLs, rejects redirects outside approved hosts, disables delta packages and requires user confirmation. A release with a missing or mismatched signing key fails closed. Synthetic tests cover package tampering, invalid signatures, cache substitution and replay/rollback behavior.

### Residual Risk

The signature prevents an attacker with only release-feed or asset-upload access from replacing both the feed and package with a matching unsigned pair. The signing key is currently designed to come from a GitHub Actions secret in the same repository security domain. A repository administrator or actor able to change the signing workflow, secret or repository settings could still sign or publish a malicious package. No production key pair is provisioned, so release signing and publication fail closed until that setup is completed. Existing clients that predate signature verification cannot authenticate the first transition update. A stale but still newer signed release can be replayed; preventing this freeze requires a persistent trusted version floor or a separate freshness service. The independent review finding is mitigated for release-host-only compromise, but remains open for production key custody, transition planning and freshness. No malicious update was executed. Velopack maintainers describe feed hashes as download-integrity checks rather than publisher authentication in [issue #105](https://github.com/velopack/velopack/issues/105).

---

# Security Features by Threat

| Threat | Primary Mitigation |
|----------|----------|
| Stolen Vault | AES-256-GCM + Argon2id |
| Backup Theft | Vault Encryption |
| Tampering | Authenticated Manifest |
| Corruption | WAL + Recovery |
| Clipboard Exposure | Timed Clipboard Cleanup |
| Window Capture | Screen Capture Protection |
| Auto-Type Hijack | Per-event Validation; Residual Global-Input Race |
| Update Feed Tampering | Pinned publisher signature and package hash verification; production key custody and freshness remain open |
| User Error | Recovery Snapshots |
| Failed Rekey | Pre-Rekey Backup |
| Failed Migration | Migration Backup |

---

# Explicit Non-Goals

SQLitePass does not attempt to:

- Prevent kernel compromise
- Prevent administrative memory acquisition
- Prevent hardware keylogging
- Prevent physical observation
- Replace endpoint protection software
- Replace operating-system security controls
- Guarantee memory erasure of immutable .NET strings

Those threats require controls outside the password manager itself.

---

# Recommended Complementary Controls

For high-value deployments:

## Endpoint Security

- Microsoft Defender for Endpoint
- Microsoft Defender Antivirus
- Microsoft Defender Credential Guard
- Microsoft Defender SmartScreen

---

## Disk Protection

- BitLocker
- TPM-backed encryption
- Secure Boot

---

## Backup Protection

- Offline backups
- Immutable backups
- Versioned backups

---

## Access Protection

- Strong master password
- Key-file usage
- Least privilege

---

# Threat Model Limitations

Threat models evolve.

New attack techniques emerge continuously.

This document represents the security assumptions and architectural protections currently implemented in SQLitePass and should be reviewed whenever significant security-sensitive changes are introduced.

---

# Conclusion

SQLitePass is primarily designed to protect against:

- Vault theft
- Offline analysis
- Database tampering
- Corruption
- Accidental disclosure
- Recovery failures

The architecture deliberately prioritizes:

- Authenticated encryption
- Integrity verification
- Recoverability

while recognizing that no local password manager can fully protect against a compromised operating system or a sufficiently privileged attacker.

The security of a deployment ultimately depends on both the protections implemented in SQLitePass and the security of the environment in which it operates.
