<p align="center">
  <img src="assets/logo.png" width="128" height="128" alt="SQLitePass Logo" />
</p>

# SQLitePass 🛡️

A modern, lightweight, and ultra-resilient password manager for Windows (.NET 8 C# + WPF) inspired by the core capabilities of **KeePass 2**, but re-engineered from the ground up to **permanently eliminate database corruption and catastrophic data loss**.

---

## 💡 Why SQLitePass instead of KeePass?

In traditional KeePass `.kdbx` files, the entire database is stored as a single monolithic encrypted binary blob. If a Windows update, system crash, bluescreen, or power outage interrupts a write operation, even a single altered byte in the header will render **100% of your credentials completely unrecoverable** (*"Header corrupt"* or *"HMAC mismatch"* errors).

### The SQLitePass Architectural Solution:

1. **Unencrypted Database at the File Level (Field-Level Encryption):**
   * The `.kpd` file is a standard SQLite database that can be inspected, queried, and repaired at any time using standard tools (such as *DB Browser for SQLite*).
   * **Zero-Knowledge Field-Level Encryption:** Sensitive fields (Passwords, Notes, Attachments, Custom Fields) are encrypted individually using **AES-256-GCM** with keys derived via **Argon2id** (memory-hard, GPU/ASIC resistant).
   * *The benefit:* Even in the worst-case scenario of physical bad sectors on disk, only the affected row or byte is impacted—**all other passwords and groups remain completely intact and decryptable**.

2. **WAL (Write-Ahead Logging) Mode:**
   * Native ACID transactions with `PRAGMA journal_mode = WAL;` and `PRAGMA synchronous = NORMAL;`. Changes are written sequentially to a dedicated WAL append-only log before commit. In the event of a sudden power outage, SQLite automatically replays or rolls back incomplete operations to the last consistent state.

3. **Isolated Atomic Dual-Backup (`.kpd.backup`):**
   * SQLitePass only keeps the primary database file open in memory.
   * On every change (add, update, delete), an atomic snapshot is created via SQLite's online backup API to a temporary file and swapped into `[vault].kpd.backup` using atomic OS file replacement.
   * The backup file is **never kept open**, completely eliminating concurrent file corruption risks.
   * A **"Restore from .backup"** button is built directly into the initial unlock screen.

4. **Zero-Configuration Embedded Engine:**
   * Powered by embedded SQLite—no database servers, background services, daemon processes, or open ports required.

---

## 🚀 KeePass 2 Parity & Productivity Features

* **Context-Aware Column Double-Clicking:**
  * **Password:** Instantly copies the password to the clipboard (without unmasking it) and triggers an automatic countdown to clear the clipboard (default 12s).
  * **User Name:** Copies the username to clipboard.
  * **URL:** Opens the URL directly in your default browser.
  * **Title / Row:** Opens the full entry editor dialog.

* **Global Auto-Type (`Ctrl+Alt+A`):**
  * System-wide hotkey using Windows `SendInput`: detects the focused window or browser tab title and automatically types credentials and customizable sequences (e.g. `{USERNAME}{TAB}{PASSWORD}{ENTER}`, `{DELAY 500}`).

* **System Tray & Desktop Integration:**
  * Notification area (System Tray) icon with quick context menu.
  * *Minimize to Tray* option.
  * *Close button (X) minimizes to Tray instead of quitting* option.
  * *Start with Windows* integration.
  * *Always on top* mode.

* **Intelligent Auto-Lock:**
  * Inactivity timeout lock (1, 3, 5, 10, 15, 30 minutes).
  * Auto-lock on minimize to tray.
  * Auto-lock on Windows session lock (`Win+L`).

* **Password Generator with Entropy Scoring:**
  * Real-time **bit entropy calculation** with visual quality bar.
  * Configurable length (6–64 characters), Uppercase, Lowercase, Digits, Symbols, and exclusion of visually ambiguous characters (`0`, `O`, `1`, `l`, `I`).

* **Multi-Language Support (5 Languages Built-In):**
  * 🇬🇧 **English**
  * 🇮🇹 **Italiano**
  * 🇫🇷 **Français**
  * 🇩🇪 **Deutsch**
  * 🇪🇸 **Español**
  * Automatic system culture detection with real-time runtime language switching in Settings.

* **Adaptive Dual Theme Design:**
  * **Light Theme:** Luminous pure white (`#FFFFFF`) canvas with alternating sky-tinted rows (`#F4F8FD`), vibrant category badges, and modern typography.
  * **Dark Obsidian Theme:** Sleek deep slate palette (`#0F172A`) for low eye-strain in dim environments.
  * Quick theme toggle button (🌓) available both on the main toolbar and unlock window.

* **History & Versioning:**
  * Retains previous versions of modified entries for auditing and rollback.

* **Encrypted Attachments & Custom Fields:**
  * Attach binary files and add unlimited key-value custom attributes with optional masking.

* **KeePass CSV Import & Export:**
  * Seamlessly migrate existing vaults to and from KeePass 2.

* **Redundant Secondary Backup Directory:**
  * Optional setting to mirror backups to a second physical location (e.g., USB drive or secondary hard drive).

---

## 🛠️ Building & Running from Source

### Prerequisites
* Windows 10, Windows 11, or Windows Server
* **.NET 8 SDK**

```powershell
# Run the test suite (Cryptography, Argon2id, SQLite, Atomic Backup, CSV Import/Export)
dotnet test SQLitePass.slnx

# Run the UI application
dotnet run --project SQLitePass.UI
```

---

## 📦 Packaging & Velopack Installer

To build the self-contained single-file installer and portable zip:

```powershell
.\build-installer.ps1 -Version "1.0.0"
```

Artifacts generated in `./releases/`:
* **`SQLitePass-win-Setup.exe`**: Standard Windows installer (creates Start Menu & Desktop shortcuts, with automatic background updates via GitHub Releases).
* **`SQLitePass-win-Portable.zip`**: Zero-install standalone portable edition (ideal for USB flash drives).
* **`SQLitePass-x.x.x-full.nupkg` & `releases.win.json`**: Update delta packages for Velopack auto-updater.

---

## ⚖️ License

Distributed under the **GNU General Public License v3.0 (GPLv3)**.
Free, open-source, and protected by strong copyleft.
