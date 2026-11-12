# SQLitePass v1.0.1 🛡️

A major visual and architectural UI/UX overhaul. This release transforms SQLitePass into a modern, minimal, and elegant password manager inspired by Bitwarden and modern KeePassXC standards, featuring complete dual-theme support (Soft Light Mode & Deep Dark Mode) and an ultra-clean unlock dialog.

---

### 🎨 Visual & UI/UX Highlights

* **Single-Card Centered Unlock Modal**:
  * Eliminated nested boxes (*box-in-box*) in favor of a single elevated modal card with 16px smooth corners.
  * Standardized width, height (36px), and styling across Database selection, Master Password, and Key File inputs.
  * Replaced the bulky backup banner with a discreet, minimal status dot (`● Isolated .backup synchronized`).
  * Prominent full-width **Unlock Vault** primary button paired with elegant, discreet secondary links for *Create New Vault* and *Exit*.

* **Refined Neutral Light Theme (Tema Chiaro)**:
  * Replaced harsh pure whites with a calming, neutral **Slate-50** (`#F8FAFC`) dashboard canvas.
  * Crisp white cards (`#FFFFFF`) with delicate, well-defined **Slate-200/300** borders.
  * High-readability typography with **Slate-900** (`#0F172A`) headings and **Slate-500** (`#64748B`) secondary labels (no blinding white or pure black).
  * Soft **Blue-100** rounded selection pills for category and folder navigation.

* **Polished Deep Dark Theme (Tema Scuro)**:
  * Harmonized palette with a deep **Zinc-950** (`#09090B`) canvas, elevated **Zinc-900** surfaces, and subtle **Zinc-800** separation lines.
  * Crisp **Zinc-100** text with soft muted labels for fatigue-free night sessions.

* **Streamlined Icon-Only Toolbar**:
  * Removed oversized buttons; all toolbar items now share uniform 32×32px geometry, consistent stroke weight, and subtle tooltips on hover.
  * Integrated compact search bar with real-time filtering and dynamic placeholder.

* **Sidebar & DataGrid Ergonomics**:
  * Clean vertical padding with rounded selection pills for groups and folders.
  * 36px table row height with delicate horizontal dividers and high-contrast row selections.
  * Redesigned inspector panel with rounded quick-copy buttons and real-time password entropy visualization.

---

### 🐛 Bug Fixes & Stability Hardening

* **Password Generator Dialog Crash**: Fixed `NullReferenceException` caused by premature event triggers (`ValueChanged` on slider and `Checked` on checkboxes) during XAML component initialization.
* **Auto-Type Crash & Execution Hardening**: Fixed crash in `BtnAutoType_Click` caused by window state minimization race condition clearing selected entry reference. Added null-checks and exception guards across `AutoTypeService`.
* **Smart Context-Aware Action Buttons**: Top toolbar buttons (Copy User, Copy Password, Open URL, Auto-Type) and context menus are now dynamically enabled only when the selected entry actually contains that specific data, preventing misleading clickable states on empty fields.
* **Auto-Lock on Minimize Conflict**: Fixed issue where Auto-Type would cause the vault to lock immediately if `AutoLockOnMinimize` was enabled. Added `_isPerformingAutoType` guard flag to suppress minimization auto-lock during active credential dispatch.
* **Silent Background Idle Lock**: Fixed intrusive behavior where idle timer or workstation lock triggered immediate focus-stealing modal dialogs. SQLitePass now safely purges keys in memory and prompts for the Master Password only when the user explicitly restores the window.

---

### 🛡️ Core Engine & Security
* **Zero-Knowledge Field-Level Encryption**: Sensitive fields individually encrypted via **AES-256-GCM** with **Argon2id** key derivation.
* **ACID SQLite WAL Mode**: Write-Ahead Logging permanently eliminates database corruption from sudden reboots or OS updates.
* **Isolated Atomic Dual-Backup**: Automatic out-of-process `.kpd.backup` synchronized on every change.
* **Global Auto-Type (`Ctrl+Alt+A`)**: Background hotkey for system-wide credential dispatch.
* **Multi-Language Support**: English, Italiano, Français, Deutsch, and Español.

---

### 📦 Downloads & Checksums

| Package | Description |
| :--- | :--- |
| **`SQLitePass-win-Setup.exe`** | Windows Installer (Desktop/Start Menu shortcuts + Auto-Update) |
| **`SQLitePass-win-Portable.zip`** | Standalone zero-install portable zip (ideal for USB drives) |
| **`SQLitePass-1.0.1-full.nupkg`** | Velopack Delta Update Package |
