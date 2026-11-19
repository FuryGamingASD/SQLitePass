# Incident Response

Updated: 18 November 2026.

This runbook covers suspected vulnerabilities, credential exposure, vault integrity failures, backup failures, malicious releases and supply-chain concerns. It supplements the device owner's and organization's incident process; it does not determine legal notification duties.

## Report a vulnerability

Use [GitHub Private Vulnerability Reporting](https://github.com/FuryGamingASD/SQLitePass/security/advisories/new) for suspected vulnerabilities. If that channel is unavailable, ask for a confidential contact channel without posting exploit details publicly. Never attach a real vault, password, key file, production credential or memory dump. Reproduce with synthetic data only.

## Severity and response target

| Severity | Examples | Initial action |
| --- | --- | --- |
| Critical | Vault authentication bypass, key disclosure, remote code execution, malicious release or compromised signing/build pipeline | Immediately contain distribution or affected service; preserve evidence; assess user impact and prepare an advisory. |
| High | Authentication or integrity bypass, unsafe recovery, destructive data loss, exploitable Auto-Type disclosure | Investigate urgently, reproduce safely, determine affected versions and prepare a fix or mitigation. |
| Medium | Clipboard leakage, information disclosure, unsafe defaults or security control failure requiring user action | Triage promptly, determine exposure and plan a fix and user guidance. |
| Low | Limited non-sensitive disclosure or operational defect with no material confidentiality/integrity impact | Track through normal maintenance and correct the documentation where needed. |

Severity is an initial assessment, not a promise about response time. Reclassify when evidence changes.

## Response procedure

### 1. Record and acknowledge

Record the report date, affected release, Windows version, installation type, vault format, reproduction steps and observed confidentiality, integrity and availability impact. Assign an incident identifier. Do not record secret values.

### 2. Preserve evidence

Preserve relevant logs, release metadata, workflow runs, installer hashes and synthetic reproduction files. For a damaged vault, preserve the vault and its `-wal` and `-shm` sidecars together. Keep originals unchanged and restrict access to evidence. Do not request or copy user passwords, key files, production vaults or memory dumps.

### 3. Contain

Choose containment based on the incident. Examples include pausing a release, disabling an affected feature, advising users to stop using an affected workflow, or isolating an affected endpoint. Do not delete backups or evidence as a containment step. If the endpoint may be compromised, do not unlock the vault there.

### 4. Assess scope and impact

Determine affected versions, formats, operating conditions, prerequisites, exploitability and whether confidentiality, integrity or availability was affected. Separate reproduced behavior from code-based analysis and unverified scenarios. Identify whether credentials may have been exposed and which external services need credential rotation.

### 5. Correct and validate

Develop the narrowest safe fix or mitigation. Add regression coverage for the reproduced failure where practical. Validate build, automated tests, dependency findings, package provenance and recovery behavior. Native Windows behavior, hardware failure resistance and independent audit results must be reported only when they have actually been verified.

### 6. Communicate and release

Prepare an advisory that states affected versions, impact, prerequisites, mitigation, fixed version and remaining limits. Avoid publishing exploit details before users have a practical mitigation. Verify the release assets and update feed before directing users to them.

### 7. Recover and learn

If a user's endpoint or vault was affected, recovery should use a trusted device and a validated copy; follow the Disaster Recovery runbook. After closure, record root cause, timeline, affected versions, evidence, corrective actions and follow-up owners. Update the threat model, security policy, release notes and tests when their assumptions changed.

## Incident-specific considerations

### Cryptography or authentication

Treat suspected key disclosure, authentication bypass, canary bypass or record-authentication failure as Critical or High until scope is established. Preserve the exact affected version and synthetic test case. Do not make claims about compromise or safety without evidence. Coordinate credential rotation from a trusted device if exposure is plausible.

### Vault integrity or recovery

Stop writes to the affected copy. Preserve the vault and sidecars, validate recovery candidates on copies and retain the original until recovered data has been checked. A successful SQLite integrity check does not alone prove cryptographic authenticity or content completeness.

### Auto-Type or clipboard

Record the target process, window state, user action and timing using synthetic credentials. Do not reproduce against a real login or production account. Clipboard history, cloud synchronization and screen capture behavior depend on Windows configuration and must not be described as verified unless tested on the relevant Windows build.

### Release or supply chain

Pause distribution when a release artifact, dependency, workflow or provenance record may be compromised. Preserve workflow logs and asset digests. Compare the published feed and assets with the verified build output, then publish corrected packages and a user advisory if necessary.

## Closure checklist

- [ ] Scope, severity, affected versions and evidence are recorded.
- [ ] Containment and user guidance are documented.
- [ ] Fixes have regression coverage and verification evidence.
- [ ] Release assets and update feed are checked, if a release was required.
- [ ] Recovery and credential rotation guidance is provided when relevant.
- [ ] Advisory, release notes and security documentation are updated.
- [ ] Remaining risks, owner and follow-up date are recorded.

This runbook is an internal process guide, not a guarantee of response time, incident recovery or regulatory compliance.
