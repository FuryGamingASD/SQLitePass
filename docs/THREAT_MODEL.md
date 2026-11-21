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

---

### Residual Risk

Clipboard data remains exposed while present.

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

Auto-Type sends events through the global Windows `SendInput` stream. The target can change between a check and an event. Process identity and window title are indicators, so users still need to verify the destination. Version 1.1.3 cancels typing on detected focus changes, session lock and vault window minimization. The race remains a P1 risk and explicit target confirmation is required. KeePass also documents the limits of matching by window title. [KeePass Auto-Type](https://keepass.info/help/base/autotype.html) · [KeePass security analysis](https://keepass.info/help/kb/sec_issues.html)

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
