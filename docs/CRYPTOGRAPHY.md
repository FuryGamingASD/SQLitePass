# Cryptography

## Overview

SQLitePass uses modern authenticated cryptographic primitives designed to provide:

- Confidentiality
- Integrity
- Resistance to offline attacks
- Protection against credential theft from stolen vault files

Cryptographic operations are performed entirely on the client device.

No encryption keys, passwords or derived secrets are transmitted to external services.

---

# Cryptographic Goals

SQLitePass cryptography is designed to achieve:

| Goal | Status |
|--------|----------|
| Vault Confidentiality | Supported |
| Record Integrity | Supported |
| Tamper Detection | Supported |
| Offline Password Resistance | Supported |
| Key File Integration | Supported |
| Authenticated Recovery Validation | Supported |

SQLitePass does not attempt to provide:

| Goal | Status |
|--------|----------|
| Protection from a compromised operating system | Out of Scope |
| Protection from kernel malware | Out of Scope |
| Protection from privileged memory acquisition | Out of Scope |

---

# Cryptographic Components

## Key Derivation

SQLitePass derives encryption keys using:

```text
Argon2id
```

Argon2id is a memory-hard password hashing algorithm designed to resist:

- GPU attacks
- ASIC attacks
- FPGA attacks
- Large-scale offline cracking

---

## Default Parameters

SQLitePass uses the following defaults:

| Parameter | Value |
|------------|---------|
| Memory | 64 MB |
| Iterations | 3 |
| Parallelism | 2 |
| Output Length | 32 bytes |

The resulting output becomes the vault encryption key.

---

## Parameter Validation

SQLitePass validates vault parameters before derivation.

Supported ranges:

| Parameter | Accepted Range |
|------------|------------------|
| Salt Length | 16-64 bytes |
| Iterations | 1-20 |
| Memory | 8 MB-512 MB |
| Parallelism | 1-16 |

Unsupported values are rejected before key derivation begins.

This prevents malformed vault files from forcing unsafe operating parameters.

---

# Master Credential Architecture

## Password Only

A vault may be protected solely by:

```text
Master Password
```

The password is transformed into a composite secret and supplied to Argon2id.

---

## Key File Only

A vault may alternatively be protected by:

```text
Key File
```

The contents of the key file are incorporated into the derivation process.

---

## Password + Key File

The strongest supported configuration combines:

```text
Master Password
+
Key File
```

Both components contribute to the final composite secret before Argon2id derivation.

Access requires possession of both factors.

---

# Salt Generation

SQLitePass generates a cryptographically secure random salt when a vault is created.

Properties:

| Attribute | Value |
|------------|---------|
| Size | 16 bytes |
| Random Source | Operating System CSPRNG |

The salt is stored in vault metadata and is not secret.

---

# Encryption

## Algorithm

SQLitePass uses:

```text
AES-256-GCM
```

AES-GCM is an authenticated encryption mode providing:

- Confidentiality
- Integrity
- Authenticity

within a single operation.

---

## Parameters

| Property | Value |
|------------|---------|
| AES Key Size | 256 bit |
| Nonce Length | 96 bit |
| Authentication Tag | 128 bit |

These values follow current industry recommendations for AES-GCM deployments.

---

# Nonce Generation

Each encryption operation generates a new nonce using:

```text
Operating System Cryptographic RNG
```

Nonce properties:

| Property | Value |
|------------|---------|
| Length | 12 bytes |
| Generation | Random |
| Reuse Strategy | Never intentionally reused |

Random nonces significantly reduce the risk of accidental nonce reuse.

---

# Authenticated Encryption

SQLitePass never relies on encryption alone.

Every encrypted payload includes:

- Ciphertext
- Authentication Tag
- Context Binding

Modification of encrypted data causes decryption failure.

Corrupted or tampered records therefore cannot silently decrypt into altered data.

---

# Context-Bound Encryption

## Additional Authenticated Data (AAD)

SQLitePass uses Additional Authenticated Data (AAD).

Each encrypted record is cryptographically bound to contextual information.

Examples include:

- Vault identifier
- Record type
- Record identifier
- Parent identifier
- Revision number

---

## Security Benefits

AAD prevents attacks involving:

### Record Substitution

An attacker cannot move a valid encrypted record from one entry to another.

### Parent Manipulation

An attacker cannot re-parent encrypted records without invalidating authentication.

### Revision Manipulation

Unauthorized modification of revision values is detected.

### Cross-Vault Copying

Encrypted data copied from one vault to another fails validation.

---

# Protected Data

The following information is encrypted:

- Passwords
- Usernames
- URLs
- Notes
- Attachments
- Custom fields
- Entry history
- Group contents

Encryption occurs before data reaches SQLite storage.

---

# Authentication Canary

SQLitePass uses a verification canary to confirm that:

```text
Password
and/or
Key File
```

produce the correct encryption key.

The canary is itself encrypted using the derived key.

Successful decryption indicates possession of the correct credential.

The canary is not used for encryption and does not weaken vault security.

---

# Vault Integrity Verification

## Manifest-Based Validation

SQLitePass validates the vault as a whole.

A cryptographic manifest is generated from:

- Metadata
- Groups
- Entries
- Schema structure

The manifest is authenticated using the vault key.

---

## Verification Workflow

When a vault is opened:

1. SQLite integrity is checked.
2. Metadata is loaded.
3. Keys are derived.
4. Authentication canary is validated.
5. Manifest authentication is verified.
6. Access is granted only if all steps succeed.

Failure at any stage stops the operation.

---

# Key Changes

## Rekey Operations

When a user changes:

- Master Password
- Key File
- Argon2 Parameters

SQLitePass performs a complete re-encryption process.

The system:

1. Validates the current vault.
2. Creates a safety snapshot.
3. Derives new keys.
4. Rewrites encrypted records.
5. Generates a new authentication canary.
6. Generates a new key epoch.

---

## Key Epochs

Each vault contains a key epoch identifier.

The epoch allows SQLitePass to detect:

- stale sessions
- outdated encryption contexts
- invalid concurrent access

Sessions using an old key epoch must reopen the vault.

---

# Secure Randomness

SQLitePass relies on the operating system cryptographic random number generator for:

- Salt generation
- Nonce generation
- Password generation
- Record identifiers where applicable

No security-sensitive randomness uses:

```text
System.Random
```

or equivalent pseudo-random generators.

---

# Password Generator

## Character Generation

The password generator uses:

```text
RandomNumberGenerator.GetInt32()
```

for all character selection operations.

---

## Class Requirements

SQLitePass can enforce the presence of:

- Uppercase characters
- Lowercase characters
- Digits
- Symbols
- Brackets

when those categories are enabled.

---

## Shuffle Method

Generated passwords are randomized using a cryptographic implementation of:

```text
Fisher-Yates Shuffle
```

This prevents predictable placement of required character classes.

---

## Password Quality Score

The displayed password quality value is:

```text
A heuristic score
```

It is not a direct measurement of entropy.

The score is intended to provide practical feedback rather than cryptographic proof of strength.

---

# Memory Handling

## Key Material

Derived encryption keys are:

- held only when necessary
- disposed when sessions end
- overwritten before disposal where possible

---

## Attachment Buffers

Decrypted attachment data is stored in memory as byte arrays.

Before disposal:

```text
CryptographicOperations.ZeroMemory()
```

is applied to attachment buffers.

---

## Secret Cleanup

SQLitePass actively clears:

- Passwords
- Usernames
- Notes
- URLs
- Custom Field Values
- History Records

when entries are released.

---

# Security Limitations

## Managed Strings

SQLitePass is implemented in .NET.

The runtime uses immutable strings.

Consequently:

- strings cannot be forcibly erased,
- garbage collection timing cannot be guaranteed.

Sensitive text may remain in memory until reclaimed by the runtime.

This limitation is common to most managed desktop applications.

---

## Local System Trust

SQLitePass assumes the endpoint itself is trusted.

Cryptography does not protect against:

- malware running with sufficient privileges,
- kernel drivers,
- debugging interfaces,
- administrative compromise,
- memory dump acquisition.

---

# Algorithm Selection Rationale

| Function | Algorithm |
|-----------|-------------|
| Key Derivation | Argon2id |
| Encryption | AES-256-GCM |
| Authentication | GCM Authentication Tags |
| Vault Integrity | Authenticated Manifest |
| Randomness | OS Cryptographic RNG |
| Password Generation | Cryptographic RNG + Fisher-Yates |

All selected algorithms are publicly reviewed, widely deployed and currently considered suitable for password-manager applications.

---

# Future Improvements

Potential future cryptographic enhancements include:

- Independent cryptography review
- External security audit
- Optional hardware-backed key protection
- Optional FIDO2-based vault unlocking
- Optional memory-hard parameter escalation guidance
- Formal cryptographic specification

Future enhancements should preserve compatibility with the authenticated Vault Format 2 design wherever practical.

---

# Conclusion

SQLitePass cryptography is built around modern authenticated encryption and verified recovery.

The design focuses not only on preventing unauthorized access but also on ensuring that corruption, tampering and recovery failures are detected rather than silently accepted.

The architecture intentionally favors verifiable correctness, authenticated integrity and recoverability over opaque or proprietary cryptographic designs.
