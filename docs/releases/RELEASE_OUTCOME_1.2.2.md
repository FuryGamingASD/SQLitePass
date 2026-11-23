# SQLitePass 1.2.2 release verification

The updated 1.2.2 packages integrate the window controls into the vault header, keep the entry editor tabs on one row and reserve space for scrolling beside the Credentials fields when needed. They also contain the compact unlock window and the folder drop highlight. [Release notes](../../RELEASE_NOTES.md#sqlitepass-122)

| Platform | Distribution | Update feed |
| --- | --- | --- |
| Windows x64 | [Installer](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.2/SQLitePass-win-Setup.exe) · [Portable ZIP](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.2/SQLitePass-win-Portable.zip) | [Windows feed](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.2/releases.win.json) |
| Linux x64 | [AppImage](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.2/SQLitePass.AppImage) | [Linux feed](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.2/releases.linux.json) |

The original 1.2.2 packages came from commit `d4365e8`. The replacement keeps the same version and release page. Existing 1.2.2 installations require a new download. On Windows, close the app and run the new Setup or replace the portable application files. On Linux, close the app and replace the AppImage. Use the same vault path and credentials when reopening the app.

Verification covers the shared test suite, native window controls, folder drop feedback, upgrades from 1.2.1 and installing the revised Windows package over the original 1.2.2. The Windows reinstall probe compares the installed assembly to the candidate package and checks that synthetic user data survives. [Replacement verification runs](https://github.com/FuryGamingASD/SQLitePass/actions/workflows/verify.yml?query=branch%3Acodex%2Fworkspace-controls-compact-editor) · [Release provenance](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.2/release-provenance.json) · [SHA-256 checksums](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.2/checksums.json)
