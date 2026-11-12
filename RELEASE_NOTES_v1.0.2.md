## What's New in SQLitePass v1.0.2

SQLitePass v1.0.2 introduces critical security protections, blazing-fast bulk imports, comprehensive soft-delete trash recovery, and full drag-and-drop organization for folders and accounts.

---

### 🛡️ Security & Privacy Hardening
* **Anti-Screenshot & Screen Capture Protection**: Implemented native Windows `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE / WDA_MONITOR)`. Application windows (Unlock Vault, Dashboard, Entry Editor, Settings) now appear completely blacked out / invisible to screen capture tools (Windows Snipping Tool, OBS, Discord screen share, remote view tools, and spyware). Configurable in *Settings*.
* **In-Memory Key File Sanitization**: Added guaranteed `CryptographicOperations.ZeroMemory` wipes for `_keyFileBytes` in `finally` blocks across vault unlock, vault creation, and window closing to prevent residual key material in RAM dumps.
* **In-Memory String Purge on Vault Lock**: Forcefully triggers garbage collection (`GC.Collect()` and finalizer synchronization) when locking the vault to purge discarded in-memory strings.

---

### 💾 Database, Performance & Resilience
* **50x Faster Bulk Imports (`SaveEntriesBatchAsync`)**: Bulk import operations are now executed in a single atomic SQLite transaction with a single isolated `.backup` snapshot. Importing thousands of credentials takes less than 500 ms (down from ~35 seconds).
* **Proactive Database Integrity Check (`PRAGMA quick_check;`)**: Verifies physical database page consistency upon vault opening. If disk sector corruption is detected, SQLitePass alerts the user and offers instant one-click recovery from the synchronized `.kpd.backup` file.
* **Recycle Bin & Soft-Delete**: First-time entry deletions safely move items to the "Cestino" (Recycle Bin) folder instead of permanently erasing them. Permanent erasure requires confirmation only when deleting directly from the Recycle Bin or using the new **"Empty Recycle Bin"** feature.

---

### 🚀 Multi-Platform Vault Imports
* **Bitwarden JSON Importer**: Import your Bitwarden unencrypted `.json` vault export files including usernames, passwords, URLs, notes, custom fields, and TOTP seeds (*File -> Import Bitwarden JSON*).
* **Web Browser CSV Importer**: Automatically recognizes and imports passwords exported from **Google Chrome, Microsoft Edge, Mozilla Firefox, Brave, and Opera** (*File -> Import Browser CSV*).

---

### ✨ User Experience & Ergonomics
* **Visual Drag & Drop Feedback (Adorner Lines & Highlights)**:
  - **Insertion Line Indicator**: When reordering folders, a vibrant blue horizontal guideline with an anchor dot appears above or below the target folder, showing exactly where it will be placed.
  - **Parent Folder Highlight**: Hovering over the middle of a folder highlights it with a soft blue rounded container, providing immediate visual confirmation that the folder will become the new parent.
  - **Account Entry Drag Highlight**: Dragging accounts/credentials from the DataGrid onto any sidebar folder highlights the target destination before releasing.
  - **Recycle Bin (Cestino) Protection**: The Recycle Bin is strictly solitary and cannot have child folders. Dropping folders into the Recycle Bin or creating subfolders inside it is prevented with clear visual feedback.
* **Full Folder / Group Drag & Drop**:
  - Drag and drop folders inside other folders to nest them.
  - Drag and drop folders above or below other folders to reorder them at the same level.
  - Drag folders out to empty space to promote them back to root level.
  - Built-in cycle detection prevents invalid loops (cannot drop a folder into itself or its descendants).
  - Context menu and keyboard shortcuts: **Move Up** (`Ctrl+Up`), **Move Down** (`Ctrl+Down`), and **Move to Parent Level**.
* **Entry Drag & Drop**: Drag credentials directly from the DataGrid onto sidebar folders to organize accounts seamlessly.
* **DataGrid Column Sorting**: Click any column header (*Title*, *User Name*, *Password*, *URL*, *Notes*, *Last Modified*) to sort entries in ascending or descending order.
* **Local Diagnostic File Logging**: Thread-safe file logger at `%APPDATA%\SQLitePass\logs\sqlitepass.log` with automatic 5 MB rotation.

---

### 📦 Installation & Verification
* **Setup Installer**: `SQLitePass-win-Setup.exe` (Automatic install with desktop shortcut & background auto-updater support)
* **Portable Package**: `SQLitePass-win-Portable.zip` (Zero-install portable version, extract and run `SQLitePass.exe`)
* **Auto-Update Feed**: `SQLitePass-1.0.2-full.nupkg`, `RELEASES`, `releases.win.json`
