# SQLitePass 1.2.0 release outcome

SQLitePass 1.2.0 is the first public release of the shared C# and Avalonia desktop app. It uses the existing Vault Format 2 on Windows x64 and Linux x64. The Windows package keeps the installation identity used by 1.1.3. The Linux AppImage has its own Velopack update channel. [Public release and downloads](https://github.com/FuryGamingASD/SQLitePass/releases/tag/v1.2.0) · [Release notes](../../RELEASE_NOTES.md#sqlitepass-120)

## Downloads and provenance

| Platform | Distribution | Update feed |
| --- | --- | --- |
| Windows x64 | [Installer](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.0/SQLitePass-win-Setup.exe) · [Portable ZIP](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.0/SQLitePass-win-Portable.zip) | [Win feed](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.0/releases.win.json) |
| Linux x64 | [AppImage](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.0/SQLitePass.AppImage) | [Linux feed](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.0/releases.linux.json) |

The publication workflow builds both platforms from the tagged commit and compares the public assets and feed hashes with the verified bundle. The Windows installer is unsigned. [Release provenance](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.0/release-provenance.json) · [SHA-256 manifest](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.0/checksums.json)

## Verification

- The release rehearsal passed 168 shared tests on Windows and 168 on Linux, including synthetic vault authentication, recovery and edit workflows. The workflow also checks transitive NuGet dependencies and package provenance. [Rehearsal run for the Xfce correction](https://github.com/FuryGamingASD/SQLitePass/actions/runs/45064931632)
- Windows packaging exercises fresh installation, startup and activation, upgrade from the public WPF 1.1.3 installer, uninstall and retention of synthetic user files. The Windows VM check confirmed Win+L locks an open vault.
- Linux packaging exercises AppImage replacement in place, restart and vault preservation using a synthetic 1.1.3 predecessor. The release rehearsal AppImage also started via FUSE on Ubuntu 26.04.1 LTS.
- Desktop probes cover clipboard expiry and ownership, tray restore, Auto-Type and focus-loss cancellation on Windows and X11/XWayland. The release adds an injected dispatcher-error probe for vault lock and password-field cleanup.
- Separate Linux checks covered the installed system logind, real ACPI S3 suspend and resume in a disposable Ubuntu VM, and Xfce screen lock and password unlock in an actual X11 session. The Xfce fix closes the vault while the desktop is locked and leaves it closed afterward. [Desktop verification details](../CROSS_PLATFORM.md#native-session-results-for-120)

## Platform limits

Screen capture exclusion and startup registration are Windows features. On Linux the clipboard hint depends on the clipboard manager, and Auto-Type targets X11 or XWayland windows on a trusted local display. On Windows the keyboard input race, interactive Clipboard History and cloud behavior, elevated Auto-Type targets, and Win+L during sensitive dialogs or operations remain outside the recorded tests. Broader DPI and assistive-technology coverage, hardware failure behavior and independent security review remain open. [Security policy](../../SECURITY.md) · [Audit status](../AUDIT_STATUS.md)

Keep an independent backup before upgrading. Use the original vault credential and key file, and restore to a new destination when recovering a backup. [Backup and recovery guide](../BACKUP_AND_RECOVERY.md)
