# Disaster Recovery

Updated on 22 November 2026 for SQLitePass 1.2.0.

This guide describes how to preserve and recover a SQLitePass vault after data loss, corruption, a failed migration, a credential change or device loss. Recovery depends on having a valid vault or backup and the credential factors used to protect it.

## Recovery expectations

SQLitePass creates verified backup generations after successful vault updates when backup is enabled and the destination is available. A displayed backup status is evidence about the latest application operation; it does not prove that every past backup can be restored or that the storage device will survive a physical failure.

There is no fixed recovery time or recovery point guarantee. Recovery time depends on the user, vault size, storage, available credentials and whether a usable copy exists. The newest verified backup may not contain changes after its creation. Check the recorded backup time and status before relying on it.

## General rules

1. Stop editing the affected vault.
2. Close SQLitePass before making forensic copies.
3. Preserve the vault and any adjacent `-wal` and `-shm` sidecar files together. Do not delete a WAL file or overwrite the damaged database by hand.
4. Make copies to separate storage and perform investigation on copies.
5. Keep the master password and key file separate from the evidence and backups. Never put credentials in logs or incident reports.
6. On the unlock screen, use **Restore from .backup** to authenticate the backup and save it to a new path. Keep the original and recovery snapshots until the recovered vault has been checked.
7. Record the version, date, file locations, backup timestamp and outcome without recording secret values.

## Common scenarios

### An entry was deleted or changed accidentally

1. Open the vault and check the Recycle Bin or entry history.
2. Restore the item to the intended group or restore the needed revision.
3. Verify the restored fields and attachments before editing again.
4. If the item is absent, use the built-in recovery flow with a verified backup. A backup restores the entire vault state from its creation time, so first preserve the current vault.

### SQLitePass reports integrity or authentication failure

1. Do not save, import, rekey or migrate the affected vault.
2. Close the application and preserve the vault with its sidecars.
3. Check whether another process or another SQLitePass instance still has the file open.
4. Use the built-in recovery flow to validate a candidate backup with the expected master password and key file.
5. After recovery, verify groups, entry counts, important credentials, history and attachments. Keep the original and generated recovery copy until verification is complete.

An integrity check or successful database open alone does not prove that credentials authenticate or that all content is correct.

### A save, backup, migration or rekey was interrupted

1. Preserve the active vault and its `-wal` and `-shm` sidecars before attempting another open or repair.
2. Keep the pre-migration or pre-rekey snapshot produced by SQLitePass. Those snapshots may require the older credentials and may contain older data.
3. Work from copies. Validate the candidate with the credential factors that were in use when it was created.
4. Do not repeat a migration or rekey on the only copy. First confirm that the source copy and a separate backup are readable.

SQLite transactions and file replacement protect specific software failure paths. They do not guarantee survival of every power, filesystem, controller or media failure.

### The computer or primary storage was lost

1. Restore the latest independently stored vault or verified backup to a trusted device.
2. Install SQLitePass from the official release source and verify the published checksum when available.
3. Open a copy first. Supply the master password and the key file, if the vault requires one.
4. Check integrity and verify representative entries, history and attachments before making it the primary copy.
5. Create and verify a new backup before resuming normal use.

A backup on the same device or volume may be lost with the device. Maintain an offline or otherwise independent copy if availability matters.

### Ransomware or suspected tampering

1. Disconnect the affected device from networks and shared storage using the organization's incident process.
2. Do not connect backup media to the affected system or overwrite evidence.
3. Preserve copies and relevant security logs; involve a qualified incident responder where appropriate.
4. Restore only from a known-good copy on a trusted, patched device.
5. Rotate credentials that may have been exposed, from a clean device, according to the affected service's procedures.

Encrypted vault storage does not protect a vault that is unlocked on a compromised endpoint.

## Recovery validation checklist

- [ ] The candidate opens with the expected master password and key file.
- [ ] SQLite and vault integrity checks pass.
- [ ] Expected groups and entries are present.
- [ ] Important usernames, passwords, URLs and notes are correct.
- [ ] Required entry history is present.
- [ ] Required attachments open and have plausible sizes.
- [ ] Auto-Type settings are present and reviewed before use.
- [ ] A new verified backup is created after recovery.
- [ ] The original and recovery copies remain preserved until acceptance.

## Backup practice

Keep more than one copy on separate storage. For important credentials, keep at least one offline or otherwise isolated copy and periodically test restoration on a copy. Set the backup frequency and retention period to match the maximum data loss the owner can accept; SQLitePass does not enforce an organizational RPO or RTO.

Do not store a key file only beside the vault if the goal is to require separate possession. Protect backups with the same care as the active vault. Historical backups can retain credentials that were later changed or deleted.

## Recovery limitations

SQLitePass cannot recover a forgotten master password, a lost required key file, destroyed copies or data that was never saved to a recoverable copy. No vendor-operated password reset or cloud recovery service exists. A successful recovery does not establish that a compromised endpoint is clean.

Organizations should maintain their own recovery contacts, storage inventory, retention policy and test records. These records must not contain master passwords, key-file contents or recovery secrets.
