# Disaster Recovery

## Purpose

This document defines the disaster recovery strategy for SQLitePass and provides guidance for restoring vault availability after severe operational failures.

The objective of disaster recovery is to ensure that credential data remains recoverable following:

- Hardware failures
- Storage corruption
- Operating system failures
- Application defects
- User mistakes
- Unsuccessful migrations
- Failed credential changes
- Accidental deletions

Disaster recovery assumes that the master credential remains available and that at least one valid vault copy still exists.

---

# Recovery Objectives

## Recovery Time Objective (RTO)

Target recovery times depend on the deployment scenario.

| Environment | Target RTO |
|------------|------------|
| Personal Use | < 30 minutes |
| Small Business | < 4 hours |
| Managed IT Environment | < 8 hours |

Recovery time depends on:

- Backup availability
- Credential availability
- Storage performance
- Vault size
- Attachment volume

---

## Recovery Point Objective (RPO)

SQLitePass backups are generated automatically following successful write operations.

Typical RPO:

| Scenario | Typical Data Loss |
|------------|------------------|
| Current backup available | Minutes |
| Secondary backup available | Minutes |
| Offline backup only | Depends on backup schedule |

No recovery process can restore changes that were never successfully written.

---

# Disaster Scenarios

## Scenario 1 - Accidental Entry Deletion

### Symptoms

Examples:

- Entry deleted unintentionally
- Entry moved incorrectly
- Group removed by mistake

### Recovery Options

Preferred:

1. Use the Recycle Bin.
2. Restore the entry.
3. Verify credentials.
4. Save the vault.

Alternative:

1. Restore from a backup copy.

### Expected Data Loss

```text
Minimal or none
```

---

## Scenario 2 - Corrupted Active Vault

### Symptoms

Examples:

- Integrity validation failure
- Unexpected authentication errors
- Metadata corruption
- SQLite corruption

### Recovery Steps

1. Stop SQLitePass.
2. Make a copy of the damaged vault.
3. Preserve associated WAL and SHM files.
4. Attempt vault opening only on copies.
5. Use built-in recovery.
6. Authenticate recovery candidate.
7. Verify restored contents.

### Expected Result

Validated backup replaces damaged vault.

### Expected Data Loss

```text
Up to the age of the most recent valid backup
```

---

## Scenario 3 - Power Failure During Write

### Symptoms

Examples:

- Unexpected shutdown
- Power outage
- System restart during save

### Built-In Protection

SQLitePass relies on:

- SQLite WAL
- Atomic transactions
- Synchronous writes
- Backup preservation

### Recovery Procedure

1. Reopen the vault.
2. Allow SQLite recovery.
3. Validate integrity.
4. Verify backup status.

If validation fails:

5. Restore a validated backup.

### Expected Result

Most events recover without user intervention.

---

## Scenario 4 - Failed Rekey Operation

### Symptoms

Examples:

- Password changed incorrectly
- Key file replaced accidentally
- Interruption during rekey

### Built-In Protection

SQLitePass creates:

```text
.pre-rekey-*.backup
```

snapshots before re-e**ryption.

### Recovery Procedure

*. Locate the pre-rekey snapshot.
2* Create a working copy.
3.*Authenticate using the previous cr*dentials.
4. Verify*contents.
5. Decide whether to ret*y*rekeying.

### Expected*Data Loss

```text
None
```

*f the pre-rekey snapshot remains a*ailable.

---

*# Scenario 5 - Failed Format Migra*ion

### Symptoms

*xamples:

- Interrupted vault upgr*de
- Errors*during migration
- Validation fail*re while upgrading

### Built-In P*otection

SQLitePass creates:

```*ext
.pre-v2-*.*ackup
```

before migration.

### *ecovery Procedure

1. Locate migra*ion snapshot.
2.*Verify snapshot integrity.
3. Open*snapshot using original credential*.
4. Retry migration on a copy.

#*# Expected Data*Loss

```text
None
```

if*migration snapshot remains availab*e.

---

## Scenario 6 - Dam*ged Primary Storage*Device

### Symptoms

Examples:

-*SSD failure*- HDD failure
- Files*stem corruption
- Unreadable secto*s

### Recovery*Procedure*
Preferred order:

1. Secondary*backup copy.
2. Offline*backup.
3. Removable storage copy.*4* Archived vault*version.

### Requirements*
Recovery requires:

- Password*- Key file (if configured)

*## Expected Data Loss

Depends on *ackup freshness.

*--

## Scenario 7 - Lost Work*tation

### Symptoms

Examples:

-*Laptop theft*- Device loss
- Device destruction*
###*Recovery Procedure

1. Obtain*latest backup*
2. Install SQLitePass.
*. Restore vault.
4. Authenticate*
5. Verify*entries.

### Security Considerati*n*
A stolen device*does not automatically imply*vault compromise if:

- Strong*master password exists
- Key file *emains*separate
- Full disk encryption*was enabled

---

## Scenario 8 - **nsomware

### Symptoms

Examples:
*- Encrypted files
- Deleted data
-*File modification*
### Recovery Procedure

1. Discon*ect affected systems.
2. Preserve*forensic evidence if*required.
3. Do not overwrite*vaults.
4. Recover from known*good backups.
5. Verify integrity *efore use.

### Recommendation

Ma*ntain offline backups that are ina*cessible to ransomware.

---

# Re*overy Sources

## Priority*Order

Preferred recovery sources*

###*Level 1

```*ext
vault.kpd.backup
*``

### Level 2

```text
*ault.kpd.backup.1
``*

### Level 3

```text
vault*kpd.backup.2
```

###*Level 4

```text
vault.kpd*backup.3
```

### Level *

Secondary backup destination

*## Level 6

Offline archival backu*

---

* Recovery Validation Checklist

Ev*ry recovery should verify*

## Vault Access

- [*] Opens successfully
- [ * Authenticates correctly
- [ ] Key*file accepted (if required)

---

*# Data Integrity*
- [ ] No integrity warnings*- [ ] Groups visible
- [ * Entries accessible
- [ ] Searches*work correctly

---

## Attachment**
- [ ] Files open correctly
- [*] Expected sizes match*- [ ] No corruption detected

---
*##*Entry Data*
- [ ] Usernames visible
- [*] Passwords decrypt correctly
- [ * URLs intact
- [ ] Notes*intact

---

## History

- [ ] His*ory*accessible
- [ ] Previous*revisions visible

---

## Auto-Ty*e

- [ ] Entries*remain*selectable
- [ ] Configuration*preserved

---

# Backup*Storage Recommendations

## Person*l Users*
Minimum:

```*ext
Primary Vault
+
Automatic*Backup
```

Recommended:

```text
*rimary*Vault
+
Automatic Backup
+
Externa* USB Backup
```

---

## Small Bus*ness

Recommended:

*``text
Primary Vault
+
Secondary*Backup Destination
+
Offline Month*y Backup
```

---

## Enterprise*Environments

Recommended:

```*ext
Primary Vault
+
Secondary Back*p Target
*
Versioned Backup Repository
+
Off*ine Recovery*Archive
```

Examples:

- V*eam Backup Repository
- Azure Back*p Vault*- Synology Hyper*Backup
- Immutable backup*storage

---

# Backup*Testing Schedule*
## Personal Use

Every*6 months

---

## Small Business

*uarter*y

---

## Enterprise

Monthly

--*

# Recovery Testing Procedure

1* Make copies*of production backups.
2. Use*a non*production environment.
3. Open th* recovered*vault.
4. Authenticate successfull*.
5. Verify entries.
6. Verify att*chments.
7. Verify history.
8. Ver*fy searches.
9. Document results.
*Never test recovery directly again*t the*production vault.

---

# Credenti*l Recovery Limitations

SQLitePass*cannot recover:

- Forgotten maste* passwords
- Lost*key files
- Deleted backups
- Dest*oyed backup media
- Data never suc*essfully written

There is no mast*r password reset mechanism.

There*is no vendor-operated recovery ser*ice.

This property is intentional*and consistent with local-first pa*sword manager designs.

---

# Rec*very Documentation

Organizations *hould maintain:

- Vault owner
- B*ckup locations
- Recovery contacts*- Recovery procedures
- Recovery t*sting records

Documentation shoul* never contain:

- Master password*
- Key file contents
- Recovery se*rets

---

# Recommended Operation*l Controls

## Strongly Recommende*

- Enable BitLocker
- Maintain of*line backups
- Store key files*separately from vaults
- Verify*backups regularly
- Test recovery *rocedures periodically

---

## Hi*h-Security Deployments

Consider:
*- Dedicated backup workstations
- *ffline encrypted archives
- Hardwa*e-protected backup*storage
- Multi-person recovery*procedures
- Documented recovery r*nbooks

---

# Disaster Recovery C*ecklist

In any disaster*scenario:

1. Stop making changes.*2. Preserve the damaged vault*
3. Preserve WAL and SHM files*
4. Work only*on copies.
5. Identify*the newest valid backup.
6.*Validate authenticity.
7. Validate*integrity.
8. Restore.
*. Verify credentials.
10. Document*the incident.

*--

# Conclusion

SQLite*ass is designed around verified re*overy rather than blind restoratio*.

The application combines:

- SQ*ite durability
- Atomic updates
- *ackup verification
- Recovery vali*ation
- Snapshot preservation
- Mu*ti*generation retention

to maximize *he probability that credential dat* remains recoverable after operati*nal failures.

Recovery success ul*imately depends on maintaining ind*pendent backup copies, protecting *redentials and regularly validatin* recovery procedures.
