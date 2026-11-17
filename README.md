# SQLitePass

A local Windows password manager built with WPF, .NET 10, SQLite, Argon2id and AES-256-GCM.

**SQLitePass 1.1 uses vault format 2** and addresses integrity, recovery and session-management defects identified in the [November 2026 audit](docs/AUDIT_2026-11-16.md). Changes and outstanding validation are tracked in [the remediation report](docs/REMEDIATION_2026-11-16.md) and the [17 November release verification](docs/RELEASE_READINESS_2026-11-17.md). This is not a claim of independent security certification or immunity to data loss.

## Vault protection

- Passwords and optional key files form the master key. The default Argon2id parameters are 64 MB memory, 3 iterations and 2 lanes; files with unsupported or excessive parameters are rejected before derivation.
- Format 2 encrypts entry and group data, including titles, usernames, URLs, notes, custom fields, attachments and history. Each record is authenticated with its vault, record type, identity, parent and revision as context. An authenticated manifest covers the current rows and header.
- The SQLite container remains inspectable. Vault name, identifiers, relationships, revisions, encrypted sizes, header timestamps and KDF information remain visible. Complete-file rollback cannot be detected without trusted external state.
- Integrity failures stop normal reads/writes instead of turning errors into password values. Existing encrypted payloads and known-good backups are not overwritten by the failed operation.
- Concurrent writers use record revisions and a key epoch. A session with an old key must reopen the vault after another session changes it.

SQLite WAL with `synchronous=FULL` improves durability, but hardware, operating-system or filesystem failures can still damage data. The master password and key file cannot be recovered if lost.

## Backups and recovery

Automatic backups use SQLite's online backup API and atomic replacement. The current `.backup` and three preceding generations (`.backup.1`, `.backup.2`, `.backup.3`) are retained. The status bar reports the last verified backup or a failure separately from a successful save. An optional secondary directory uses a vault-ID based filename to avoid collisions.

Recovery verifies the candidate with the supplied password/key file before replacement, refuses an active application session, and preserves the original file together with its old WAL/SHM files under a `.recovery-...` name. Do not mix a database with another copy's journal files or manually delete a WAL to try to repair it.

Before rekeying or migrating, an additional `.pre-rekey-...backup` or `.pre-v2-...backup` snapshot is kept. These snapshots are not automatically deleted. **Older backups retain the previous key and the privacy properties of the previous format.** Plan their retention explicitly and keep an independent backup on another device. A mirror on the same disk does not protect against losing the disk.

To upgrade a 1.0 vault:

1. Make an independent copy of the vault and any associated files while all applications using it are closed.
2. Open it in SQLitePass 1.1 and confirm the format upgrade.
3. Supply the original password and key file, if used. Conversion begins only after successful authentication and validation; a legacy snapshot is retained.
4. Verify the entries and test recovery on a separate copy before replacing your independent backups.

Format 2 requires SQLitePass 1.1 or later. Migration cannot reconstruct credentials already overwritten or lost by an older application. Do not reopen an upgraded vault with a 1.0 build.

## Daily use

- Groups, global search, entry editing, full-copy duplication and a recycle bin. The recycle bin has an explicit restore command with a destination folder and revision checks. Removing a folder moves its descendant entries to the bin; the old folder hierarchy is removed after confirmation. Folder expansion is remembered per user and vault without rewriting the vault.
- The list shows the selected folder path and has exact tag filters (comma/semicolon separators), expired entries and entries expiring within 30 days. Expiry compares calendar dates; today is included in the upcoming window. The reference date stays fixed while paging and refreshes with a new query or F5. Search scope is explicit and operational feedback is separate from the clipboard countdown.
- Editing uses a separate draft; cancellation does not change the active entry. Inline validation covers title, enabled expiry date and Auto-Type. The editor shows whether it has been edited and confirms discarding changes on window close; explicit Cancel discards directly. Final comparison and Auto-Type parsing run on a worker. Reverted edits do not write an unchanged existing record or add history. Revisions include complete snapshots for new changes. Up to 50 revisions are retained per entry; the combined record has a 64 MB size limit. You can remove history in the editor before saving.
- The list holds one page of up to 200 lightweight entries, with an estimated 8 MiB budget (one oversized entry is allowed). Navigation uses snapshot-bound cursors without keeping previous pages. Search and sorting cover the whole vault; export, Auto-Type matching and emptying the bin also operate beyond the visible page. Attachment, custom-field and history collections are loaded when needed for editing, copying or confirmed Auto-Type. The entire encrypted vault is still authenticated on each query. Loading shows progress and can be cancelled with `Esc`; `F5` retries. See the [synthetic memory benchmark](docs/PERFORMANCE_2026-11-16.md) for measured benefits and remaining limits.
- Attachments are limited to 20 MB per file. Reading, exporting and restoring a revision run off the UI thread. Exported attachments are plaintext files; failed replacement preserves an existing destination, and closing the editor requests cancellation.
- Custom fields marked protected are masked in the list and edited in a dedicated dialog. Entry passwords, preview/list passwords and custom-field values can be revealed for 15 seconds; changing windows or locking hides them earlier. The deadline uses a monotonic clock and is checked by the UI dispatcher, so a stalled UI can delay the visible update. Hidden entry password text controls are cleared, with undo disabled. Attachment rows show actual loaded byte length.
- The password generator uses a cryptographic random source. Its quality indicator is a **heuristic score, not measured entropy or a guarantee against guessing**.
- Sensitive copy actions share a timed clipboard service with Windows history/cloud exclusion formats. The OS and other processes can still access clipboard content; the flags are not protection from arbitrary malware.
- Locking clears active controls and drafts, disposes the key, cancels Auto-Type and blocks vault commands. .NET strings and operating-system buffers cannot be guaranteed to be erased.
- Auto-Type requires an explicit target and confirmation. The global shortcut is `Ctrl+Alt+A`; registration conflicts are reported. The window title is only a matching heuristic. Input stops on detected focus/title changes or lock. Use care: foreground keyboard APIs have unavoidable timing races and do not authenticate a website's origin.
- `Ctrl+L` locks; `Ctrl+I` adds an entry; `Ctrl+G` adds a group; `Ctrl+M` changes the master credential. `Enter`/`Delete` operate on selected entries and `F2` renames a group.
- Startup with `--minimized` opens a locked tray entry without prompting for the vault immediately.
- Localized in English, Italian, French, German, and Spanish with runtime language switching and catalog validation. Scalable vector `AppIcon` controls replace emoji glyphs, suppressing accessibility peers for decorative elements while exposing `AutomationProperties.Name` across interactive controls for screen readers.

Screen-capture exclusion is best-effort Windows functionality. It does not defend against a compromised operating system, memory access, cameras or all recording tools. See [SECURITY.md](SECURITY.md).

## Import and export

Supported input paths include KeePass-style CSV (`Group,Title,Username,Password,URL,Notes`, in that exact order), browser CSV with recognized, unambiguous headers, and unencrypted Bitwarden JSON. Imports validate structure and show a confirmation before writing. Folder paths are reconstructed under the selected destination; ambiguous existing folder names and paths into the recycle bin are rejected. Folders and entries commit in one transaction: failure or cancellation before commit leaves neither partial entries nor empty import folders. Reading/parsing and database work run off the UI thread, with progress and cancellation. Bitwarden data without a dedicated field is retained in the protected `BitwardenOriginalJson` custom field; this does not make cards, passkeys or other types native application features. Data absent from an export cannot be reconstructed.

CSV export streams in the background, supports progress and cancellation, and replaces the destination atomically. It contains passwords and notes **in plaintext**, excludes the recycle bin, and omits attachments, custom fields and history. It is a migration format, not a complete backup. Do not open untrusted CSV content in spreadsheet software; formula-like cells may be interpreted. Use a verified encrypted vault backup for a complete copy.

Input is limited to 32 MB and CSV imports to 100,000 records. Importing a malformed file fails rather than guessing unknown columns. Keep any plaintext source export only in an appropriately protected location.

## Build and test

Prerequisites: Windows and the .NET SDK selected by `global.json` (10.0.404, with patch roll-forward). NuGet dependencies are pinned in `packages.lock.json` files.

```powershell
dotnet restore SQLitePass.slnx --locked-mode
dotnet build SQLitePass.slnx -c Release --no-restore
dotnet test SQLitePass.slnx -c Release --no-build --no-restore
dotnet run --project SQLitePass.UI
```

Tests use synthetic vaults and a fake Auto-Type input adapter. The small `SQLitePass.TestHost` helper leaves a real SQLite WAL in an isolated temporary fixture for recovery testing; it does not send keyboard input or access a real vault. It is not included in the application package.

The bounded benchmark creates and removes its own synthetic temporary vault, with no vault path or credential arguments:

```powershell
dotnet run --project SQLitePass.TestHost -c Release -- --benchmark
```

Settings and diagnostics are stored under `%APPDATA%\SQLitePass`. Preferences include recent file paths and opaque vault/group identifiers for folder expansion; they are not encrypted. Writes use an OS lease and atomic replacement. Independent concurrent preference edits are merged, while incompatible edits to the same value are rejected with a request to reload. A Windows startup-registration failure is reported separately from saved preferences. The portable executable does not make these settings portable. Logs omit credential values and raw exception messages, but diagnostic retention and file access still matter.

## CI and release

The Windows [Verify workflow](https://github.com/FuryGamingASD/SQLitePass/actions/workflows/verify.yml) runs the complete verification and packaging sequence, preserves evidence and verifies silent installation/update/uninstall. Run the same sequence locally with `./scripts/Invoke-Ci.ps1`. A main-branch commit marked `[release]` publishes only after those checks pass, with asset digest verification and commit provenance. The published update feed is then downloaded and verified through the application’s Velopack library. See [release procedure and remaining validation](docs/RELEASE_1.1.0.md).

## Packaging

```powershell
# Local QA only: explicitly unsigned, without installing or publishing anything
.\build-installer.ps1 -Version 1.1.1 -AllowUnsigned

# Signed release, using an authorized certificate from the Windows store
.\build-installer.ps1 -Version 1.1.1 -CertificateThumbprint YOUR_CERTIFICATE_SHA1_THUMBPRINT
```

The signing thumbprint can also be supplied through `SQLITEPASS_SIGN_CERT`. Packaging runs tests first, uses the pinned local Velopack tool, preserves existing releases, writes versioned output under `artifacts/releases/`, and produces SHA-256 checksums. Authenticode remains waived as agreed. Release 1.1.0 is public; source changes for the 1.1.1 launch hotfix are being prepared and require CI verification before publication. Native interactive UI checks and independent security review remain open. Building packages does not publish them.

## License and security reporting

SQLitePass is licensed under [GPL-3.0](LICENSE). Dependencies retain their own licenses; see [third-party notices](docs/THIRD_PARTY_NOTICES.md). Follow [the security policy](SECURITY.md) when reporting a vulnerability; never attach real secrets or a real vault to a public issue.
