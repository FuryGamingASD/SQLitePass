# SQLitePass v1.0.0 — First Official Release 🚀

**SQLitePass** is a modern, lightweight, and ultra-resilient Windows password manager designed to provide a secure and anti-corruption alternative to KeePass's monolithic `.kdbx` file format.

---

### 🛡️ Why SQLitePass?

* **Two-Tier Anti-Corruption Architecture**:
  * The primary SQLite database (`.kpd`) runs in **WAL (Write-Ahead Logging)** mode, guaranteeing ACID transaction integrity even during sudden Windows crashes, bluescreens, or power cuts.
  * **Isolated Dual-Backup**: on every modification, a synchronized `[vault].kpd.backup` copy is created atomically and immediately closed, eliminating concurrent corruption risks.
* **Zero-Knowledge Field-Level Encryption**:
  * The database structure remains inspectable and repairable with standard SQL tools (such as *DB Browser for SQLite*).
  * Passwords, notes, attachments, and custom attributes are encrypted individually with **AES-256-GCM** using keys derived via **Argon2id**.
* **Full KeePass 2 Parity**:
  * **Global Auto-Type** (`Ctrl+Alt+A`) with active window detection.
  * **Column Double-Click Actions**: double-click password to copy with a 12-second auto-clear timer; double-click username to copy; double-click URL to launch in browser; double-click title to edit.
  * **Secure Password Generator** with real-time bit entropy calculation and visual quality gauge.
  * **Intelligent Auto-Lock**: lock on inactivity, minimize to tray, or Windows workstation lock (`Win+L`).
  * **System Tray Integration**: minimize and close to the notification area with tray context menu.
  * **KeePass CSV Import & Export**.
* **Multi-Language (5 Languages Built-In)**:
  * 🇬🇧 English, 🇮🇹 Italiano, 🇫🇷 Français, 🇩🇪 Deutsch, 🇪🇸 Español with real-time switching in Settings.
* **Dual Theme Design**:
  * **Light Theme**: Clean, luminous pure white (`#FFFFFF`) canvas with alternating sky-tinted rows and vibrant category badges.
  * **Dark Obsidian Theme**: Deep slate palette (`#0F172A`) for low eye fatigue.

---

### 📦 Downloads

| File | Description |
|---|---|
| **`SQLitePass-win-Setup.exe`** | **Official Windows Installer (Recommended)**: installs SQLitePass on your machine, creates Start Menu & Desktop shortcuts, and supports seamless one-click background updates. |
| **`SQLitePass-win-Portable.zip`** | **Portable Standalone Edition**: zero installation required. Extract anywhere (e.g. on a USB flash drive) and run `SQLitePass.exe`. |
| `SQLitePass-1.0.0-full.nupkg` | Delta package for the Velopack auto-updater engine. |
| `RELEASES` & `releases.win.json` | Manifest files used by the application to check for and install updates from GitHub. |

---

### ⚖️ License
Distributed under the **GNU General Public License v3.0 (GPLv3)**.
Free, open-source, and protected by strong copyleft.
