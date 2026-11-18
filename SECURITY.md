# Security Policy

## Supported Versions

The current supported release line is:

| Version | Status |
|----------|----------|
| 1.1.x | Supported |
| 1.0.x | Unsupported |
| Earlier versions | Unsupported |

SQLitePass 1.1 introduces Vault Format 2, authenticated vault integrity verification, validated recovery workflows, record revisions and authenticated encrypted storage.

Earlier 1.0 releases contain known architectural limitations and recovery defects documented in the project audit and remediation reports.

Always maintain an independent backup before upgrading an existing vault.

---

# Reporting a Vulnerability

## Preferred Method

Use GitHub Private Vulnerability Reporting whenever available:

https://github.com/FuryGamingASD/SQLitePass/security/advisories/new

If private reporting is unavailable:

- Open a GitHub issue requesting a confidential contact channel.
- Do not publish exploit details for unfixed vulnerabilities.
- Do not attach real vaults.
- Do not attach master passwords.
- Do not attach key files.
- Do not attach memory dumps.
- Do not attach production credentials.

Use only synthetic datasets for reproduction.

---

## Recommended Report Content

Please provide:

- SQLitePass version
- Vault format version
- Windows version
- Installation type (Portable or Installed)
- Steps to reproduce
- Expected behavior
- Actual behavior
- Confidentiality impact
- Integrity impact
- Availability impact
- Screenshots using non-sensitive data only

---

# Security Architecture Overview

SQLitePass is designed around four security goals:

1. Confidentiality
2. Integrity
3. Recoverability
4. Operational Safety

The application prioritizes preserving both vault confidentiality and recoverability during unexpected failures.

---

# Crypt
