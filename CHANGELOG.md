# Changelog

All notable changes to **SQLitePass** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
