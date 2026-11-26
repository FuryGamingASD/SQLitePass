# Changelog

## [Unreleased]


## [1.3.1] - 2026-11-26

- Fix KeePass CSV format handling and improve KDBX import validation, credential prompts, and missing-file feedback.
- Add resizable entry-grid columns and save their widths per vault; keep the vault toolbar and filter controls on one row.
- Refresh the Windows taskbar icon after the window opens, and localize clipboard and missing-file guidance across all five supported languages.
- On Windows build 26300, allow credential copying only when Windows reports both clipboard history and roaming disabled; continue requesting per-item exclusion from history and synchronization.
- Treat SQLite corruption and non-database files as integrity failures while allowing other SQLite/I/O errors to surface as storage errors.
- Use the label “Username” in the English interface.

## [1.3.0] - 2026-11-24

- Import KeePass KDBX 4.0 and 4.1 into a new encrypted vault with a migration report.
- Use optional machine policy to enforce vault locking and restrict exports or Auto-Type.
- Reduce plaintext retention in entry grids, the editor and selected credential operations.
- Block credential copying when Windows clipboard roaming, Windows build 26300 or incompatible Linux clipboard managers make retention controls unreliable.
- Authenticate updates using an independently held publisher key and a signed freshness checkpoint. Install 1.3.0 manually to establish trust in the publisher key.

## [1.2.2] - 2026-11-23

- Use SQLitePass as the workspace heading.
- Fit the unlock window to its contents and integrate minimize, maximize, restore and close into the workspace header beside the menus.
- Highlight the destination folder while dragging an entry or group over it.
- Keep the entry editor tabs on one row. Fit Credentials at the default size and place any scrollbar beside the fields in smaller windows.

## [1.2.1] - 2026-11-23

- Restore the branded unlock card, light and dark palettes, nested group sidebar, icon toolbar, settings shortcuts and entry preview in the Avalonia desktop app.
- Restore entry double-click actions, column sorting, live search, recent vaults, folder drag-and-drop, and edit discard confirmation.
- Restore timed password reveals, saved window sizes and the Windows Ctrl+Alt+A Auto-Type shortcut with target confirmation.
- Document feature parity and remaining verification for each release from 1.0.0 through the published 1.2.0.
- Verify native Windows folder drag-and-drop and global Auto-Type with synthetic credentials. Test updates from the public 1.2.0 packages on Windows and Linux.

## [1.2.0] - 2026-11-22

### Added

- Ship the shared Avalonia app as a Windows x64 installer and portable build and as a Linux x64 AppImage, with separate update feeds.
- Add Linux clipboard expiry, tray integration, Auto-Type to X11 targets and automatic locking on logind session lock, suspend and Xfce Screensaver activation.
- Verify the Windows 1.1.3 upgrade, Linux AppImage replacement and restart, and desktop integration under X11 and XWayland.

### Changed

- Share the vault, entry editor, settings, password generator and search across Windows and Linux. Existing Vault Format 2 files and credentials remain compatible.
- Restore verified backups to a new destination and preserve the source files.
- Confirm the selected Auto-Type entry, target window and process before sending input.

### Fixed

- Lock the active vault after an unhandled dispatcher error or exit if the lock fails. Route unobserved task errors to the same recovery workflow.

## [1.1.3] - 2026-11-21

### Fixed
- Cancel active Auto-Type when the vault window is minimized, including when lock-on-minimize is disabled.
- Add a synthetic WPF regression for minimize cancellation without displaying a window or sending desktop input.
- Fail closed on unhandled dispatcher errors: lock the active vault before marking the exception handled, and shut down if a vault cannot be locked or no vault window is active. Unobserved task failures now schedule the same lock-or-shutdown response before being marked observed.
- Prevent reentrant editor closing when a vault lock arrives while an entry discard prompt is already closing the window.
- Preserve activation and updater shutdown requests that arrive before the app finishes starting.
- Remove the uninstalled copy's Windows startup entry when it belongs to that installation.
- Remove temporary SQLite journal files after vault backups.
- Correct release claims and repair incomplete recovery and incident-response documentation.

## [1.1.2] - 2026-11-18

### Changed
- Update Microsoft.Data.Sqlite to 11.0.0 and refresh locked transitive dependencies and their license notices.
- Refresh the SHA-pinned GitHub Actions used by the Windows verification and release workflow.
- Give stale revision/session conflicts a specific `VaultConflictException` so callers can distinguish concurrent changes from other invalid operations.
- Recheck the Auto-Type target window and owning process after each injected event, and stop when the target identity changes.
- Show the receiving process name in the Auto-Type confirmation and recheck the foreground target after async lookup.
- Let unexpected drag-and-drop errors reach the safe-lock handler and log expected tray-icon fallback failures.
- Treat only malformed or unauthentic canary data as an invalid credential; propagate unexpected engine errors.
- Retry concurrent application settings updates with bounded backoff under file-lock contention.

Vault format and user data are unchanged. The package is unsigned. The 1.1.2 readiness report documents the open native workflow, accessibility, durability and independent-review checks.

## [1.1.1] - 2026-11-17

### Fixed
- Restore and activate the existing unlock or vault window when SQLitePass is launched again while already running, including instances minimized to the notification area.
- Keep a hidden tray instance responsive to a new launch request by opening its unlock window.
- Ask a running instance to close and wait for it before Velopack updates or uninstalls, so the process and tray icon do not linger.

Vault format and user data are unchanged. Authenticode signing was outside the scope of this release.

## [1.1.0] - 2026-11-17

- Add a pinned Windows CI pipeline with coverage, WPF evidence, package/license/runtime/hash verification and silent installer/update/uninstall checks. Publish verified assets through a draft release, record source provenance and verify the anonymous public update feed with Velopack.

- Add folder breadcrumbs, exact tag and expiry filters across paged queries, explicit global-search scope, and separate operation/clipboard feedback.
- Add inline editor validation, edited-state feedback and discard confirmation. Compare the final draft on a worker, update bound history on the caller context, and skip unchanged existing records.
- Hide revealed entry, preview, list and custom-field values after a monotonic 15-second deadline, on deactivation and on lock. Clear hidden plaintext password controls and show actual attachment byte length.
- Exercise actual asynchronous provider loading, UI filters, dirty-editor lock, deadline and validation workflows in the isolated STA verifier without showing windows or sending desktop input.

- Bound the visible list to 200 rows and an estimated 8 MiB, with stable forward/backward cursors and global search/sort. Keep CSV export, Auto-Type matching and recycle-bin operations independent of the visible page.
- Load confirmed Auto-Type entries with custom fields and revision checks. Stream legacy migration in two validated passes, preserve child ownership and the original username/URL policy, and retain the key-file requirement.
- Replace tree/list emoji with vector icons, bound text previews, skip expensive quality scoring for oversized inputs, and correct selected-row colors in simulated high contrast.

- Move the application to .NET 10 and locked dependencies.
- Introduce authenticated vault format 2 with encrypted group/entry contents, record revisions and key epochs.
- Add guarded legacy migration, validated recovery with preservation of old journals, and versioned backup status.
- Fix session lock, key-file credential changes, draft cancellation, child data persistence, protected fields and Auto-Type target handling.
- Replace desktop-input tests with isolated adapters and recovery regression fixtures.
- Add deterministic build configuration, release checksums, optional certificate signing and explicit unsigned QA packaging.
- Make folder and entry imports atomic, cancellable and observable, with file parsing off the UI thread and strict CSV column validation.
- Merge independent preference edits across instances, reject conflicts, preserve readable recovery settings and distinguish startup-registration failures.
- Persist per-vault folder expansion without rewriting the vault; report global hotkey conflicts and implement Ctrl+H/F5.
- Allow toolbar wrapping and resizable, scrollable dialogs; translate the new import and shortcut-status flows into all five supported languages.
- Load heavy entry details on demand, reject partial-model writes and preserve full content when moving an entry from the list.
- Stream manifest hashing and process rekey, folder trash and recovery validation one entry at a time; reduce redundant AES-GCM and serialization buffers while retaining format-2 compatibility.
- Prepare edit drafts, restore revisions and handle attachment files off the UI thread; add guarded cancellation, atomic attachment export, refresh progress and local date display.
- Add format compatibility, overview integrity and attachment regression tests plus a bounded synthetic memory benchmark.
- Centralize all user interface strings in `TranslationCatalog` with embedded JSON catalogs for English, Italian, French, German, and Spanish; validate key parity and composite format arguments via automated tests (105 tests).
- Modernize accessibility across windows and dialogs: replace text emoji icons with the scalable vector `AppIcon` control (suppressing accessibility peers for decorative elements), and bind `AutomationProperties.Name` across input controls and action buttons.
- Align security terminology: explicitly clarify best-effort screen capture exclusion and differentiate verified backup presence from operational filesystem state.
- Add explicit recycle-bin restoration with a destination picker and revision checks; protect permanent deletion batches against stale confirmations.
- Stream CSV exports in the background with progress, cancellation and atomic destination replacement; preserve existing files on failure.
- Refresh the SDK to 10.0.404 and bundled runtime to 10.0.15; extend synthetic volume, WPF rendering and theme-contrast verification and include dependency license texts.
- The 1.0 release notes below are historical; absolute security and corruption-prevention claims are superseded by SECURITY.md and the current README.


All notable changes to **SQLitePass** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.0.2] - 2026-11-12

### 🛡️ Security & Privacy Hardening
- **Best-effort Screen Capture Exclusion**: Added optional Win32 `SetWindowDisplayAffinity` support (`WDA_EXCLUDEFROMCAPTURE`, with a `WDA_MONITOR` fallback). Supported capture paths may omit or blank protected windows; this does not block every capture method, spyware or privileged process.
- **Key-file Buffer Cleanup**: The selected `_keyFileBytes` buffer is cleared after derivation. This does not clear every copy held by the runtime or prevent memory-dump access to other copies.
- **Lock-time Memory Cleanup**: Locking releases references and requests garbage collection for eligible objects. It cannot guarantee that immutable .NET strings have been overwritten or immediately removed from process memory.

### 💾 Database, Performance & Resilience
- **Batch Import**: Added bulk entry writes in a SQLite transaction. Import time depends on vault size, storage, backup configuration and the imported data; no general speedup factor is claimed.
- **Database Integrity Check**: Added SQLite `quick_check` validation and a recovery-candidate flow. This checks database structure; it cannot repair every damaged file or guarantee recovery from physical media failure.
- **Recycle Bin & Soft-Delete**: First-time entry deletions move items to the Recycle Bin. Permanent deletion removes them from the active vault after confirmation, but prior backups or retained history may still contain copies; this is not a secure-erasure guarantee.

### 🚀 Multi-Platform Import Support
- **Bitwarden JSON Importer**: Added import of supported fields from unencrypted Bitwarden `.json` exports. This is not a full-fidelity round trip; unsupported item types are not necessarily converted to native SQLitePass features.
- **Browser CSV Importer**: Added import support for recognized CSV formats from Chrome, Edge, Firefox, Brave and Opera. Export layouts vary; review the imported data and retain the source export until it is verified.

### ✨ User Experience & Ergonomics
- **Interactive Folder / Group Drag & Drop**:
  - Drag and drop folders inside other folders to create nested subfolders.
  - Drag and drop folders above or below other folders to reorder them at the same level.
  - Drag folders out to empty space to promote them back to root level.
  - Built-in cycle detection prevents invalid loops (cannot drop a folder into itself or its descendants).
  - Added right-click context menu and keyboard shortcuts: **Move Up** (`Ctrl+Up`), **Move Down** (`Ctrl+Down`), and **Move to Parent Level**.
- **Interactive Entry Drag & Drop**: Drag credentials directly from the DataGrid onto sidebar folders to organize accounts seamlessly.
- **DataGrid Column Sorting**: Click any column header (*Title*, *User Name*, *Password*, *URL*, *Notes*, *Last Modified*) to sort entries in ascending or descending order.
- **Local Diagnostic File Logging**: Added thread-safe logger writing to `%APPDATA%\SQLitePass\logs\sqlitepass.log` with automatic 5 MB rotation.

---

## [1.0.1] - 2026-11-11

### 🎨 Visual & UI/UX Overhaul
- **Modern Minimalist Design**: Redesigned the entire user interface inspired by Bitwarden and modern KeePassXC principles.
- **Single-Card Unlock Dialog**:
  - Eliminated the double nested box-in-box layout.
  - Re-architected login into an elegant, centered modal dialog with smooth rounded corners (16px).
  - Standardized widths, heights (36px), and styling across Database file selector, Master Password, and Key File fields.
  - Sized primary "Unlock Vault" action button to full width, paired with subtle, clean secondary links for "Create New Vault..." and "Exit".
  - Replaced bulky backup status badge with a quiet, synchronized green indicator dot (`● Isolated .backup synchronized`).
- **Refined Light Mode (Tema Chiaro)**:
  - Replaced glaring white with a soothing, neutral **Slate-50** (`#F8FAFC`) dashboard background.
  - Clean crisp white cards (`#FFFFFF`) with subtle, well-defined **Slate-200/300** (`#E2E8F0` / `#CBD5E1`) borders.
  - High-readability typography with **Slate-900** (`#0F172A`) headings and **Slate-500** (`#64748B`) secondary labels, avoiding harsh pure black.
  - Gentle **Blue-100** (`#DBEAFE`) rounded selection pills for sidebar folder navigation.
- **Polished Dark Mode (Tema Scuro)**:
  - Harmonized palette using deep **Zinc-950** (`#09090B`) canvas, elevated **Zinc-900** (`#121215`) cards, and subtle **Zinc-800** (`#27272A`) border lines.
  - Soft **Zinc-100** (`#F4F4F5`) primary text with muted secondary labels.
- **Streamlined Toolbar**:
  - Replaced oversized `+` block with a cohesive, uniform 32×32px icon button bar.
  - Clean tooltips on hover and smooth opacity transitions for disabled actions.
  - Compact modern search input box with real-time filtering and dynamic placeholder.
- **Sidebar & DataGrid Ergonomics**:
  - Roomier vertical item padding with custom-templated rounded pill indicators on category/folder selection.
  - Enhanced DataGrid row height (36px) with clean horizontal-only dividers and high-contrast row selections.
  - Polished detail inspector card with subtle password entropy meter and rounded quick-copy buttons.

### 🐛 Bug Fixes
- **Password Generator Dialog Crash**: Fixed `NullReferenceException` caused by premature event triggers (`ValueChanged` on slider and `Checked` on checkboxes) during XAML component initialization.
- **Auto-Type Selection Crash**: Fixed a crash when minimizing cleared the selected-entry reference. Version 1.1.3 also cancels active Auto-Type on minimize. The separate race with global `SendInput` remains open.
- **Smart Context-Aware Action Buttons**: Top toolbar buttons (Copy User, Copy Password, Open URL, Auto-Type) and context menus are now dynamically enabled only when the selected entry actually contains that specific data, preventing misleading clickable states on empty fields.
- **Silent Background Auto-Lock**: Prevented the unlock window from aggressively popping up and stealing focus over other applications when idle timeout or Windows session lock occurs while the app is in the background or minimized to the System Tray. Cancelling unlock no longer abruptly shuts down the application.

### 🛡️ Core & Packaging
- Built official release binaries (Setup installer and Portable zip) for v1.0.1 using Velopack.

---

## [1.0.0] - 2026-11-10

### 🚀 Initial Official Release
- **Encrypted SQLite Storage**: Used AES-256-GCM payload encryption and Argon2id key derivation. SQLite structure and some metadata remained visible in this format.
- **SQLite WAL Transactions**: Used SQLite transactions and WAL recovery. These controls reduce software-level partial-write risk but do not guarantee survival of every power, filesystem, controller or media failure.
- **Automatic Backup**: Added a local `.kpd.backup` workflow. Backup creation and publication depend on successful writes, configuration and available storage; a same-device copy does not protect against device loss.
- **Global Auto-Type (`Ctrl+Alt+A`)**: Background hotkey for system-wide credential entry.
- **Password Management Features**: Custom fields, encrypted file attachments, history, CSV import/export and configurable clipboard clearing. This feature list does not imply complete format compatibility with KeePass or other password managers.
- **Multi-Language Support**: English, Italiano, Français, Deutsch, and Español.
- **Velopack Installer & Auto-Updater**: Native background updates and portable edition.
