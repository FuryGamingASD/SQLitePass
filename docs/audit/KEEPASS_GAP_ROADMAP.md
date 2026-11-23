# SQLitePass hardening and KeePass migration

## Progress log

| Date | Step | Status and evidence |
| --- | --- | --- |
| 2026-11-23 | Baseline | Started from `9053d42` (1.2.2). The Avalonia grid computes the password quality estimate from every displayed entry at page load. Existing source-level and native checks are listed in the [audit status](../AUDIT_STATUS.md). |
| 2026-11-23 | Implementation | Removed eager estimates from the Avalonia grid. The shield in the selected entry preview estimates quality on request after loading the authenticated entry revision. The score clears when the selection or page changes. The existing browsing path still materializes scalar secrets during page loading. |
| 2026-11-23 | Focused verification | The Avalonia workspace test passed on Windows. It verifies that the score starts hidden, appears only on request without revealing or copying the password, and clears when the selection is removed. Existing entry gestures remain covered by the same test. |
| 2026-11-23 | Regression suite | The Release suite passed 169 tests on Windows. The five locale files parsed and contain the new label. Local targets for links in the three updated Markdown files were checked. |
| 2026-11-23 | Scoring helper | Removed the lowercased plaintext copy and the distinct-character collection from the shared scoring helper. The case comparison agreed with the prior behavior for the characters in the weak-pattern list across the Basic Multilingual Plane on the Windows development machine. The Release suite passed all 169 tests after adding a mixed-case regression assertion. |
| 2026-11-23 | Paging implementation | Page cursors store the boundary entry ID and the snapshot and query stamps. The Core resolves its sort key by ID inside the authenticated read transaction. This keeps passwords and other sort values out of the cursors retained between page loads. |
| 2026-11-23 | Paging verification | The Windows Release suite passed 171 tests. The new cases exercise both password sort directions, case-insensitive ties, empty passwords, forward and backward navigation, focused reload, and rejection of a missing boundary. |
| 2026-11-23 | Auto-Type memory | Candidate lookup projects identity, title, URL and eligibility for matching. The confirmation retains only ID, revision and title. The confirmed read projects fields usable by Auto-Type and skips attachments and history. Both paths still decrypt and clear a temporary authenticated record buffer. |
| 2026-11-23 | Auto-Type verification | The Windows Release suite passed 171 tests, including title and URL matching, ambiguity and revision rejection, and use of custom fields with attachments and history absent from the confirmed model. The legacy WPF Release project built with zero warnings or errors. Native desktop retesting is pending. |
| 2026-11-23 | Linux verification | On FuryUbuntu x64, an isolated checkout of `e221930` passed the locked restore and all 171 Release tests with SDK 10.0.404. The source startup probe passed under Xvfb. The package and native input probes remain pending. |

## Order of work

- [x] Move password quality estimation from page loading to an explicit field action. Preserve password copying, preview and integrity checks. Verify the change on synthetic vaults.
- [ ] Reduce plaintext lifetime in the remaining browse, edit, clipboard and Auto-Type paths. Record which strings or copies the runtime or OS can retain.
- [ ] Complete the native checks for lock during dialogs and operations, Windows clipboard history and sync, and interrupted recovery. Record the platform, package and outcome of each check.
- [ ] Retain desktop Auto-Type and its confirmation. Evaluate a browser integration that verifies the destination origin before filling. Track the Windows input race and Linux display limits in the [threat model](../THREAT_MODEL.md).
- [ ] Add read-only KDBX 4 import into a new vault, with encrypted fixtures and a report of preserved and unsupported data. Cover cancellation and failed imports without leaving a partial destination.
- [ ] Prepare a scoped independent review of the format, credential rotation, backup, import and update packages. Record the reviewed commit and follow-up findings.
- [ ] Evaluate machine-level configuration for locking, export and Auto-Type when deployment requirements are available.

The browsing scan still decrypts each scalar overview. Password sorting temporarily uses the plaintext value. The next memory pass should reduce fields materialized by the normal grid while preserving sorting, search, selection and the explicit reveal path.
