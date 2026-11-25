# Documentation index

The root [README](../README.md), [security policy](../SECURITY.md), [changelog](../CHANGELOG.md) and [release history](../RELEASE_NOTES.md) describe the current product and published versions. Historical audit and release reports retain the scope and conclusions applicable when they were written; consult current guidance for present behavior.

## Current security and operations guidance

- [Audit status](AUDIT_STATUS.md)
- [Threat model](THREAT_MODEL.md)
- [Security architecture](SECURITY_ARCHITECTURE.md)
- [Cryptography](CRYPTOGRAPHY.md)
- [Data protection](DATA_PROTECTION.md)
- [Memory handling](MEMORY_HANDLING.md)
- [Backup and recovery](BACKUP_AND_RECOVERY.md)
- [Disaster recovery](DISASTER_RECOVERY.md)
- [Incident response](INCIDENT_RESPONSE.md)
- [Security testing strategy](SECURITY_TESTING.md)
- [CI and release verification](CI_AND_RELEASE_VERIFICATION.md)
- [Performance measurements and limits](PERFORMANCE_2026-11-16.md)
- [Third-party notices](THIRD_PARTY_NOTICES.md)
- [Windows and Linux desktop guide](CROSS_PLATFORM.md)
- [Machine-managed policy](MACHINE_POLICY.md)
- [Update package authentication and transition](UPDATE_SIGNING.md)

## Audit evidence and findings

- [SQLitePass hardening and KeePass migration roadmap](audit/KEEPASS_GAP_ROADMAP.md).
- [Feature parity from 1.0.0 through 1.2.0](audit/FEATURE_PARITY_1.2.0.md).
- [Initial technical audit](audit/AUDIT_2026-11-16.md) — findings against the identified 1.0.2 commit.
- [Remediation status](audit/REMEDIATION_2026-11-16.md) — historical findings and fixes through 1.1.3, with a 1.2.0 follow-up.
- [Synthetic reproduction evidence](audit/AUDIT_EVIDENCE_2026-11-16.json).
- [Synthetic benchmark evidence](evidence/).

## Releases

- [SQLitePass 1.3.0 release verification and downloads](releases/RELEASE_OUTCOME_1.3.0.md).
- [SQLitePass 1.3.0 release notes](../RELEASE_NOTES.md#sqlitepass-130).
- [SQLitePass 1.3.0 first installation and authenticated upgrades](UPDATE_SIGNING.md#transition-for-existing-installations).
- [SQLitePass 1.2.2 release outcome and downloads](releases/RELEASE_OUTCOME_1.2.2.md).
- [SQLitePass 1.2.2 release notes](../RELEASE_NOTES.md#sqlitepass-122).
- [SQLitePass 1.2.1 release outcome](releases/RELEASE_OUTCOME_1.2.1.md).
- [SQLitePass 1.2.0 release outcome](releases/RELEASE_OUTCOME_1.2.0.md).

### Historical Windows releases

- [SQLitePass 1.1.3 release outcome](releases/RELEASE_OUTCOME_1.1.3.md).
- [SQLitePass 1.1.3 readiness and manual checklist](releases/RELEASE_READINESS_1.1.3.md).
- [SQLitePass 1.1.3 release notes](../RELEASE_NOTES.md#sqlitepass-113).
- [SQLitePass 1.1.2 release outcome](releases/RELEASE_OUTCOME_1.1.2.md).
- [SQLitePass 1.1.1 release outcome](releases/RELEASE_OUTCOME_2026-11-17_1.1.1.md).
- [SQLitePass 1.1.0 release outcome](releases/RELEASE_OUTCOME_2026-11-17.md).
- [1.1.2 pre-publication readiness snapshot](releases/RELEASE_READINESS_1.1.2.md).
- [1.1.0 readiness snapshot](releases/RELEASE_READINESS_2026-11-17.md).
- [Archived status snapshot](archive/STATUS_2026-11-17.md).

Release binaries and CI artifacts are not stored in this documentation tree. Release reports link to public GitHub releases and workflow runs; synthetic benchmark JSON is retained here so its measurement protocol can be reviewed.
