# Changelog

## [1.1.2] - 2026-11-17

### Changed
- Update Microsoft.Data.Sqlite to 11.0.0 and refresh locked transitive dependencies and their license notices.
- Refresh the SHA-pinned GitHub Actions used by the Windows verification and release workflow.

Vault format and user data are unchanged. The package remains unsigned as previously agreed. The audit's native workflow, accessibility, durability and independent-review limitations remain documented in the 1.1.2 readiness report.

## [1.1.1] - 2026-11-17

### Fixed
- Restore and activate the existing unlock or vault window when SQLitePass is launched again while already running, including instances minimized to the notification area.
- Keep a hidden tray instance responsive to a new launch request by opening its unlock window.
- Ask a running instance to close and wait for it before Velopack updates or uninstalls, so the process and tray icon do not linger.

Vault format and user data are unchanged. Authenticode remains waived as previously agreed.

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
- **Anti-Screenshot & Anti-Spyware Protection**: Integrated native Win32 `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE / WDA_MONITOR)`. Application windows (Unlock Vault, Dashboard, Entry Editor, Settings) now appear completely blacked out / invisible to screen capture tools (Snipping Tool, OBS, Discord, TeamViewer, screenshot malware). Can be toggled in *Settings*.
- **In-Memory Key File Sanitization**: Guaranteed zeroing of `_keyFileBytes` with `CryptographicOperations.ZeroMemory` within `finally` blocks immediately after Argon2id key derivation, preventing key material leakage in memory dumps.
- **Memory Purge on Vault Lock**: Forced garbage collection (`GC.Collect()` and finalizer synchronization) when locking the vault to purge discarded in-memory strings.

### 💾 Database, Performance & Resilience
- **High-Performance Batch Import (50x Faster)**: Implemented `SaveEntriesBatchAsync` executing bulk entry insertions inside a single atomic SQLite transaction with a single online backup snapshot, reducing import times for thousands of passwords from ~35s to under 500ms.
- **Proactive Database Integrity Check (`PRAGMA quick_check;`)**: Verifies physical database page consistency upon vault opening; in the event of disk sector damage or corruption, automatically offers instant recovery from the isolated `.kpd.backup` snapshot.
- **Recycle Bin & Soft-Delete**: First-time entry deletions now safely relocate items to the "Cestino" (Recycle Bin) folder. Permanent erasure requires confirmation only when deleting directly from the Recycle Bin or through the new **"Empty Recycle Bin"** action.

### 🚀 Multi-Platform Import Support
- **Bitwarden JSON Importer**: Full support for Bitwarden unencrypted `.json` vault exports (credentials, URLs, notes, custom fields, and TOTP seeds).
- **Web Browser CSV Importer**: Auto-detects and imports passwords from **Google Chrome, Microsoft Edge, Mozilla Firefox, Brave, and Opera**.

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
- **Auto-Type Crash & Execution Hardening**: Fixed crash in `BtnAutoType_Click` caused by window state minimization race condition clearing selected entry reference. Added null-checks and exception guards across `AutoTypeService`.
- **Smart Context-Aware Action Buttons**: Top toolbar buttons (Copy User, Copy Password, Open URL, Auto-Type) and context menus are now dynamically enabled only when the selected entry actually contains that specific data, preventing misleading clickable states on empty fields.
- **Silent Background Auto-Lock**: Prevented the unlock window from aggressively popping up and stealing focus over other applications when idle timeout or Windows session lock occurs while the app is in the background or minimized to the System Tray. Cancelling unlock no longer abruptly shuts down the application.

### 🛡️ Core & Packaging
- Built official release binaries (Setup installer and Portable zip) for v1.0.1 using Velopack.

---

## [1.0.0] - 2026-11-10

### 🚀 Initial Official Release
- **Anti-Corruption Architecture**: Field-level AES-256-GCM encryption with Argon2id key derivation, stored in unencrypted SQLite database.
- **SQLite WAL Engine**: ACID transactions with `PRAGMA journal_mode = WAL` preventing data loss from unexpected power cuts or OS updates.
- **Isolated Atomic Dual-Backup**: Automatic out-of-process `.kpd.backup` synchronized on every change.
- **Global Auto-Type (`Ctrl+Alt+A`)**: Background hotkey for system-wide credential entry.
- **KeePass Parity**: Custom fields, encrypted file attachments, history/versioning, CSV import/export, and configurable clipboard clearing.
- **Multi-Language Support**: English, Italiano, Français, Deutsch, and Español.
- **Velopack Installer & Auto-Updater**: Native background updates and portable edition.
