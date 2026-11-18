# Incident Response

## Purpose

This document defines the incident response process for SQLitePass.

Its objectives are to:

- Protect users
- Contain security incidents
- Preserve evidence
- Reduce impact
- Restore normal operation
- Improve future resilience

SQLitePass is a local-first password manager. Consequently, most incidents affect:

- Confidentiality
- Integrity
- Availability

of local vault data rather than centralized cloud infrastructure.

---

# Scope

This process applies to:

- Security vulnerabilities
- Suspected cryptographic weaknesses
- Vault integrity failures
- Backup failures
- Unauthorized access attempts
- Malicious modifications
- Supply chain concerns
- Update security problems
- Data corruption events

This process does not replace:

- Operating system incident response
- Enterprise SOC procedures
- Digital forensics investigations
- Regulatory breach notification requirements

---

# Security Principles

When handling incidents:

1. Preserve evidence
2. Prevent further damage
3. Avoid unnecessary modifications
4. Work on copies whenever possible
5. Validate recovery procedures
6. Document every action

---

# Incident Severity Classification

## Severity 1 (Critical)

Immediate compromise of confidentiality.

Examples:

- Vault decryption without credentials
- Authentication bypass
- Key disclosure
- Remote code execution
- Supply-chain compromise
- Malicious release distribution

Expected response:

```text
Immediate
```

Recommended action:

- Stop distribution
- Preserve evidence
- Notify users
- Publish advisory

---

## Severity 2 (High)

Potential compromise or serious integrity failure.

Examples:

- Cryptographic implementation flaw
- Backup validation bypass
- Recovery verification bypass
- Privilege escalation
- Vault tampering bypass

Expected response:

```text
Urgent
```

Recommended action:

- Investigate immediately
- Assess exploitation risk
- Prepare fix
- Publish advisory when appropriate

---

## Severity 3 (Medium)

Security weaknesses requiring user action.

Examples:

- Information disclosure
- Unsafe defaults
- Insufficient validation
- Auto-Type security weaknesses
- Clipboard handling defects

Expected response:

```text
High priority
```

Recommended action:

- Correct in next release
- Update documentation
- Communicate mitigations

---

## Severity 4 (Low)

Minor risks without significant practical impact.

Examples:

- Cosmetic security bugs
- Non-sensitive information leakage
- Logging defects without credential exposure

Expected response:

```text
Normal development cycle
```

---

# Incident Categories

## Cryptographic Incident

Examples:

- AES implementation flaw
- Argon2 weakness
- Authentication bypass
- Key derivation error

Potential impact:

- Vault compromise
- Offline cracking exposure
- Integrity bypass

Priority:

```text
Critical
```

---

## Vault Integrity Incident

Examples:

- Manifest validation failure
- Unexpected modification detection
- Tampering concerns

Potential impact:

- Data corruption
- Data integrity uncertainty

Priority:

```text
High
```

---

## Backup Incident

Examples:

- Backup validation failure
- Missing backups
- Corrupted backups
- Recovery failure

Potential impact:

- Availability loss
- Recovery failure

Priority:

```text
High
```

---

## Authentication Incident

Examples:

- Incorrect unlock behavior
- Key-file bypass
- Session validation failure

Potential impact:

- Unauthorized access

Priority:

```text
Critical
```

---

## Auto-Type Incident

Examples:

- Wrong target detection
- Focus validation failure
- Credential disclosure during typing

Potential impact:

- Credential exposure

Priority:

```text
High
```

---

## Clipboard Incident

Examples:

- Clipboard not cleared
- Clipboard timeout failure
- Cloud Clipboard leakage

Potential impact:

- Temporary credential exposure

Priority:

```text
Medium
```

---

## Supply Chain Incident

Examples:

- Malicious dependency
- Compromised build script
- Compromised CI/CD workflow
- Malicious release artifact

Potential impact:

- Full compromise

Priority:

```text
Critical
```

---

# Detection Sources

Incidents may be detected through:

## User Reports

Examples:

- GitHub Issues
- Private vulnerability reports
- Security researchers

---

## Internal Testing

Examples:

- Automated tests
- Regression tests
- CI verification
- Release validation

---

## Security Review

Examples:

- Source code review
- Dependency review
- External audit
- Penetration testing

---

## Operational Observation

Examples:

- Vault integrity failures
- Recovery failures
- Unexpected crashes
- Validation errors

---

# Initial Response Procedure

## Step 1 - Record the Event

Capture:

- Date
- Time
- Version
- Environment
- Reporter
- Reproduction details

Assign a temporary incident identifier.

Example:

```text
INC-2026-001
```

---

## Step 2 - Preserve Evidence

Preserve:

- Vault copies
- Recovery copies
- Logs
- Screenshots
- Error messages

Do not modify original artifacts.

---

## Step 3 - Determine Severity

Classify:

- Critical
- High
- Medium
- Low

Severity determines response urgency.

---

## Step 4 - Assess Scope

Determine:

- A
