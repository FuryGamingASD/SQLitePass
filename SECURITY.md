# Security policy

The current development line is SQLitePass 1.1, targeting .NET 10 and vault format 2. Earlier 1.0 builds have known integrity and recovery defects described in `docs/AUDIT_2026-11-16.md`. Keep an independent copy before upgrading an older vault.

## Reporting a vulnerability

Use the repository's private vulnerability reporting feature when it is available: https://github.com/FuryGamingASD/SQLitePass/security/advisories/new.
If private reporting is unavailable, open an issue requesting a confidential contact channel without attaching a real vault, master password, key file, recovery code, memory dump or detailed exploit against an unpatched public release. The maintainers have not declared a guaranteed response time.

Use synthetic data for reproduction. State the commit/version, vault format, Windows version, prerequisites and whether the failure affects confidentiality, integrity or availability.

## Security boundaries

- Password/key-file based key derivation uses Argon2id. Entries and groups use AES-256-GCM with authenticated record context. The authenticated manifest also covers the encrypted rows and metadata.
- Vault name, identifiers, parent relationships, revisions, sizes, KDF parameters and timing metadata are not all concealed. Backups and old exports retain the properties of the format that produced them.
- The application cannot protect secrets from a compromised operating system or another process with sufficient access to its memory. Managed strings cannot be guaranteed to be erased. Screen-capture exclusion and clipboard history flags are operating-system features, not protection from arbitrary malware.
- Auto-Type uses simulated keyboard input. The window title is a heuristic, not verification of a website's origin. Confirm the target and stop when the focus or title changes; a residual timing race is inherent in foreground input APIs.
- An authenticated local file alone cannot prove that the complete file is the newest version. Keep independent, versioned backups. Do not use live WAL databases as a synchronization format for cloud folders or network shares.
- Backup verification does not make a disk immune to failure. A saved record and a completed backup have separate outcomes. Check the status and periodically test recovery on a copy.
- Migration cannot reconstruct secrets that an earlier version has already overwritten. Recovery requires a good earlier copy.

## Release requirements

Run the Windows build and regression suite, restore from the locked dependency graph, review the dependency scan, test install/update/portable behavior on clean Windows environments, and sign installer/application binaries using an authorized code-signing certificate. The build script refuses to package a public-style release without a signing identity unless `-AllowUnsigned` is explicitly selected for local QA. This repository configuration does not itself attest to a signed or externally audited release.
