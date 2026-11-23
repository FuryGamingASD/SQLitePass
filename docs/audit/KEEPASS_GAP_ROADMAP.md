# SQLitePass hardening and KeePass migration

## Progress log

| Date | Step | Status and evidence |
| --- | --- | --- |
| 2026-11-23 | Baseline | Started from `9053d42` (1.2.2). The Avalonia grid computes the password quality estimate from every displayed entry at page load. Existing source-level and native checks are listed in the [audit status](../AUDIT_STATUS.md). |
| 2026-11-23 | Implementation | Removed eager estimates from the Avalonia grid. The shield in the selected entry preview estimates quality on request after loading the authenticated entry revision. The score clears when the selection or page changes. The existing browsing path still materializes scalar secrets during page loading. |
| 2026-11-23 | Focused verification | The Avalonia workspace test passed on Windows. It verifies that the score starts hidden, appears only on request without revealing or copying the password, and clears when the selection is removed. Existing entry gestures remain covered by the same test. |
| 2026-11-23 | Regression suite | The Release suite passed 169 tests on Windows. The five locale files parsed and contain the new label. Local targets for links in the three updated Markdown files were checked. |
| 2026-11-23 | Scoring helper | Removed the lowercased plaintext copy and the distinct-character collection from the shared scoring helper. The case comparison agreed with the prior behavior for the characters in the weak-pattern list across the Basic Multilingual Plane on the Windows development machine. The Release suite passed all 169 tests after adding a mixed-case regression assertion. |

## Order of work

- [x] Move password quality estimation from page loading to an explicit field action. Preserve password copying, preview and integrity checks. Verify the change on synthetic vaults.
- [ ] Reduce plaintext lifetime in the remaining browse, edit, clipboard and Auto-Type paths. Record which strings or copies the runtime or OS can retain.
- [ ] Complete the native checks for lock during dialogs and operations, Windows clipboard history and sync, and interrupted recovery. Record the platform, package and outcome of each check.
- [ ] Retain desktop Auto-Type and its confirmation. Evaluate a browser integration that verifies the destination origin before filling. Track the Windows input race and Linux display limits in the [threat model](../THREAT_MODEL.md).
- [ ] Add read-only KDBX 4 import into a new vault, with encrypted fixtures and a report of preserved and unsupported data. Cover cancellation and failed imports without leaving a partial destination.
- [ ] Prepare a scoped independent review of the format, credential rotation, backup, import and update packages. Record the reviewed commit and follow-up findings.
- [ ] Evaluate machine-level configuration for locking, export and Auto-Type when deployment requirements are available.

The next memory pass should review the Core browsing path. Password sorting is an existing desktop option, and its in-memory page cursors can hold plaintext sort values. Preserve ordering and cursor invalidation while reducing how long those values are retained.
