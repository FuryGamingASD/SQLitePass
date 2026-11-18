# SQLitePass 1.1.3 readiness

Status **candidate verification in progress**. Updated 19 November 2026.

The source baseline is the published [v1.1.2 release](RELEASE_OUTCOME_1.1.2.md), tag commit `f8c378386a43ba4886b17d6b5341bd28881c367d`. The current remediation branch adds a minimize-during-Auto-Type cancellation fix and documentation corrections. The vault format is intended to remain unchanged. Do not create the release tag until the verification and manual checklist below have been recorded against the final candidate.

## Implemented in this work

- Minimizing the vault cancels active Auto-Type even when lock-on-minimize is disabled; an isolated WPF verification exercises this behavior without displaying a window or sending desktop input.
- Unhandled dispatcher errors now lock the authenticated vault before being marked handled; if the vault cannot be locked or no vault window is active, the application shuts down. The isolated verifier exercises the lock entry point and awaits its cleanup. An unobserved task failure schedules the same lock-or-shutdown response.
- Editor lock cleanup is now idempotent during an in-progress WPF close: a lock arriving during the discard prompt completes the existing close instead of calling `Close()` reentrantly.
- The startup coordinator now records early activation and shutdown requests under the same lock it uses to register handlers. CI exercises the installed executable, second-instance activation from tray startup, and an update from the published 1.1.2 while it is running.
- The uninstall callback removes the Windows startup entry when it points to the copy being removed. Velopack 1.2.161 stops processes before calling this callback, so the uninstall smoke verifies process removal and registry ownership. The update smoke separately checks for a clean exit.
- Added a 1.1.2 publication outcome with tag, workflow, asset digests and explicit verification limits.
- Prepared the 1.1.3 application, assembly and package version metadata, release notes and CI defaults; publication remains disabled until the final gates pass.
- Retained Auto-Type at the user's direction. KeePass documents that window titles are not unique and that users must verify the target window. That is a comparable target-trust limit; KeePass's documentation does not establish that it has SQLitePass's specific check-to-`SendInput` race, and it does not eliminate SQLitePass's race ([Auto-Type](https://keepass.info/help/base/autotype.html), [security analysis](https://keepass.info/help/kb/sec_issues.html)).
- Repaired corrupted recovery guidance, removed unresolved citation tokens and added repository-local synthetic benchmark evidence.
- The documentation links, release history and security claims have been reconciled with the published 1.1.2 outcome and the current audit status.

## Verification performed

The [verification summary](../evidence/release-readiness-1.1.3.json) combines local checks with hosted CI. On the pinned **.NET SDK 10.0.404**, locked restore and Release compilation passed with **0 warnings and 0 errors**; **161/161 automated tests** passed and the five-project transitive NuGet scan found **0 known advisories**. The synthetic session verifier passed after the editor-close fix, including minimize cancellation, no lock when lock-on-minimize is disabled, and fail-closed handling of unexpected UI errors. The exact hidden WPF verifier associated with the earlier crash report was rerun locally on 18 November: **1,080/1,080 cases passed**, exit code 0, with no windows shown and no desktop input sent.

I also ran an isolated native startup smoke on 18 November using SDK 10.0.404. A temporary WPF host invoked the actual `SQLitePass.UI.Program.Main()` entry point from the current source project, with temporary settings and log directories. It observed the real `LoginWindow` visible at 480×540 with a native HWND and WPF presentation source, then shut the app down after five seconds. No user settings, vault or desktop input were used. This confirms that the current source bootstrap can show its unlock window; it does not exercise the installed package or reproduce/explain the user's earlier crash. The retained JSON is in the ignored `artifacts/verification/1.1.3-startup-smoke-20261118/` folder.

Hosted GitHub Actions [Verify run #112](https://github.com/FuryGamingASD/SQLitePass/actions/runs/44401931309) on Windows Server 2025 passed for current PR #8 head `e3630a6bc20a68bf5b285cc22f308c79edd0b0f3`. It reports the full build/test, hidden WPF verification (**1,080 layout cases**), **105 contrast pairs**, dependency scan and package/install smoke checks. Fresh install, upgrade from 1.0.2, and uninstall passed in a temporary test directory. The product application was not started and no real vault was opened. The run tested synthetic PR merge commit `a3715e8a3de344fd61deb6cfef19e631171303ee`. The workflow artifacts and their digests are recorded in the evidence JSON, expire on 2 December 2026 at 21:33 UTC, and are not a public release. Detailed BuildId/package-inspection metadata is also retained from Verify run #110 and explicitly labeled with that run in the evidence JSON. The eventual main-branch candidate still requires its own CI and release workflow.

The earlier completed local WPF run, before the idempotent editor-close fix, failed during cleanup of a synthetic dirty `EntryEditDialog`. A later local attempt left 233 screenshots ending at `CustomFieldDialog-fr-light-100`, but no final manifest or exception trace; the retained files do not establish why it stopped. I reran the exact full hidden WPF command at 23:08 on the pinned SDK 10.0.404: it completed all 1,080 cases, wrote 540 screenshots, exited 0, showed no windows and sent no desktop input. No matching application crash, .NET Runtime or graphics-system event was recorded during that reproduction. This does **not** launch the product application or use a real vault, so it neither reproduces nor explains the user's separate application crash. The supplied WER `LiveKernelEvent 193` refers to a `WATCHDOG` dump dated 14 November and is not evidence of a crash during this 18 November rerun. Four release-publisher safety scenarios pass using a fake CLI without network calls.

Hosted Verify runs [#119](https://github.com/FuryGamingASD/SQLitePass/actions/runs/44411772629) and [#120](https://github.com/FuryGamingASD/SQLitePass/actions/runs/44411783294) both passed on PR head `2528e4becbc0477b098f58f53af7539d64b59760`; #120 is the pull-request run. The earlier [run #112](https://github.com/FuryGamingASD/SQLitePass/actions/runs/44401931309) records detailed build, test, dependency, package and installer checks. The [draft PR #8](https://github.com/FuryGamingASD/SQLitePass/pull/8) remains unmerged and the release is unpublished. These results do not complete the installed-package launch, tray behavior, Win+L, Clipboard History, DPI, Narrator, high contrast or controlled-target Auto-Type checks, close the retained A07 Auto-Type race, or substitute for a final-main release build. The original crash is still unexplained: neither the hidden WPF verifier nor the isolated source-entrypoint smoke reproduced it. Detailed local TRX/render output is ignored by Git.

## Security and reliability release gates

| Area | Current status | Evidence required to close |
| --- | --- | --- |
| Auto-Type A07 | Partially mitigated; P1 residual remains and is accepted for retention by the user. Process/title/focus validation cannot bind global `SendInput` atomically to an HWND. Minimize cancellation is added in this branch. KeePass documents its title-matching limit: titles are not unique, so the user must verify the receiving window. This is a similar target-trust concern; the cited KeePass documentation does not establish an identical check-to-input race in KeePass and does not resolve SQLitePass's implementation-specific race ([Auto-Type](https://keepass.info/help/base/autotype.html), [security analysis](https://keepass.info/help/kb/sec_issues.html)). | Keep the P1 disclosure and perform the native synthetic-target checks below. Never state that target changes are impossible or the race is eliminated. |
| Session lock A08 | Automated lock cleanup and generation invalidation exist; native modal and Windows lock behavior is not verified here. | Complete the Win+L, dialog, long-operation and minimize checks below. |
| Clipboard A09/B06 | Shared ownership/timeout handling exists; OS history and cloud behavior are unverified. | Complete copy, timeout, ownership, history and cloud checks below on the target Windows build. |
| Backup A11 | Software fault-injection checks exist, including an OS replacement failure. No physical storage/power-loss test is represented. | Re-run release CI and recovery checks. Record hardware testing only if performed on isolated copies; keep physical durability as an explicit limit otherwise. |
| Performance C13 | Paging lowers retained memory; authentication/scanning remains O(N), records and backups remain full-sized, and scalar secrets remain in page memory. | Keep the known limits. Do not add an unreviewed format or manifest change to this patch release. |
| Error handling C14 | Typed conflicts exist. Unhandled dispatcher errors lock the active vault before being marked handled and shut down if no vault is active or immediate locking fails. Unobserved task failures schedule the same lock-or-shutdown response before being marked observed. The idempotent editor close fix passes the session-only verifier; hosted CI and the latest local hidden WPF rerun each passed all 1,080 cases. The isolated source-entrypoint smoke displayed a native unlock window, but did not reproduce the reported crash. The earlier partial local attempt has no final manifest or exception trace. Other generic catches and task ownership remain. | Obtain an app-specific crash event/dump or the exact user trigger before claiming the reported crash is fixed. Review the release diff for swallowed security/integrity failures. Defer broad architecture replacement unless a concrete defect requires it. |
| Accessibility D05–D07 | In-memory layout, contrast and localization checks exist; native DPI and assistive technology are unverified. | Complete the native accessibility checks below and record OS/monitor settings. |
| Independent review | No independent cryptography audit or penetration test is recorded. | Preserve as an explicit limitation; do not claim certification or absence of vulnerabilities. |
| Code signing E03 | Intentionally waived by the user; package remains unsigned. | Keep the release unsigned and document the expected Windows reputation prompt. |

## Native Windows checklist

Use a synthetic vault and an isolated Windows profile for the desktop checks. Record the Windows build, display scaling, candidate build, date and result. The hosted Windows runner can verify the installed process, native window and clipboard with synthetic values. The checks that depend on desktop interaction, accessibility settings or Windows account policy require a separate result.

### Launch, tray and package lifecycle

- [ ] Install the candidate from a clean profile and confirm the unlock window is visible and responsive.
- [ ] Launch a second instance; confirm the existing unlock/vault window restores from tray and minimization.
- [ ] Lock/unlock, close-to-tray and minimize-to-tray behavior match settings.
- [ ] Upgrade from 1.1.2 with the app running; confirm clean shutdown, update completion and next launch.
- [ ] Uninstall and verify application-owned shortcuts/processes are removed without deleting the user's vault or backups.

### Synthetic vault, credentials and recovery

- [ ] Create a new synthetic vault; confirm a pre-existing file is not overwritten.
- [ ] Unlock with correct and incorrect passwords; verify incorrect credentials do not mutate the vault.
- [ ] Test password plus key file and key-file-only vaults, including missing/wrong key files.
- [ ] On a copy of a legacy-format fixture, migrate, reopen, verify representative fields/history/attachments, and confirm a pre-migration snapshot exists.
- [ ] Change password and key file; verify old factors fail and new factors succeed. Repeat on copies only.
- [ ] Restore a verified backup on a copy and verify content before accepting it.

### Locking and sensitive UI

- [ ] Trigger `Win+L` with an editor, settings dialog and another sensitive dialog open; after unlock, confirm the vault is locked and old plaintext is absent.
- [ ] Trigger `Win+L` during a long import/export or vault operation; confirm cancellation/lock completes safely and no partial data is reported as committed.
- [ ] Minimize while synthetic Auto-Type is active; confirm typing stops. Repeat with lock-on-minimize enabled and disabled.
- [ ] Reveal a password, switch applications and wait through the configured timeout; confirm it is masked again.

### Clipboard and screen capture

- [ ] Copy username, password, custom field and generated password; verify configured timeout clears only SQLitePass-owned content.
- [ ] Replace clipboard content from another application before expiry; confirm SQLitePass does not erase the replacement.
- [ ] Inspect `Win+V`, Clipboard History and Cloud Clipboard on the test Windows profile; record the Windows configuration and observed result.
- [ ] Test screen capture with protection enabled and disabled using synthetic values. Record API fallback/unsupported behavior; treat the control as best-effort.

### Auto-Type and accessibility

- [ ] Use a controlled local test target and synthetic credentials. Confirm the target process/title shown in the confirmation and test ambiguous matches.
- [ ] Change focus, title, process or window handle during typing; confirm typing stops and no later characters are sent.
- [ ] Test with Shift, Ctrl or Alt held and with an elevated target; confirm safe refusal or documented behavior.
- [ ] Navigate every primary workflow using keyboard only and Narrator; verify accessible names, focus order, visible focus and error announcements.
- [ ] Test native high-contrast mode, text scaling and display scales 100%, 125%, 150% and 200%, including a small display and long German strings.

## Automated release evidence required

- [x] Locked restore, Release build with zero warnings/errors, full test suite and 1,080-case WPF verification pass on the current PR merge commit in hosted CI run #112. Repeat on the final candidate commit after merge.
- [x] NuGet vulnerability scan reports no known advisories for the PR build's shipped dependency graph in run #112. Repeat on the final candidate commit after merge.
- [x] Self-contained package verification and temporary-directory fresh install/upgrade/uninstall smoke checks pass on the PR merge commit in run #112. Final-main checksums, provenance and release assets are still pending.
- [ ] The tag workflow publishes the expected assets and verifies the public update feed/package.
- [ ] The native checklist has a dated result record, with failures fixed and retested or remaining risks explicitly accepted.
- [ ] `RELEASE_OUTCOME_1.1.3.md` records the final tag, workflow, asset digests, native checklist result and remaining limitations.

The native checklist must remain open until a person has run the installed candidate on Windows. The isolated source-entrypoint smoke is limited evidence and does not exercise the installed package, open a vault or cover the remaining native behaviors. The prior signature waiver continues to apply and is not a native test result. Do not publish 1.1.3 until the original crash is either diagnosed or explicitly recorded as unreproduced, the required native checklist is recorded, final-main CI passes, and the release workflow produces verified assets.
