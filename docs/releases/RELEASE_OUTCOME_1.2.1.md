# SQLitePass 1.2.1 release verification

SQLitePass 1.2.1 restores the desktop interface and workflows documented in the comparison with the original 1.2.0 release. It uses the same Windows installation identity, Linux update channel, vault format and credentials. [Feature-parity audit](../audit/FEATURE_PARITY_1.2.0.md) · [Release notes](../../RELEASE_NOTES.md#sqlitepass-121)

## Downloads and verification

| Platform | Distribution | Update feed |
| --- | --- | --- |
| Windows x64 | [Installer](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.1/SQLitePass-win-Setup.exe) · [Portable ZIP](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.1/SQLitePass-win-Portable.zip) | [Windows feed](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.1/releases.win.json) |
| Linux x64 | [AppImage](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.1/SQLitePass.AppImage) | [Linux feed](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.1/releases.linux.json) |

The release rehearsal passed 169 shared tests on both platforms. Windows verified native folder drag-and-drop and persistence after reopening, global Auto-Type into a separate process after destination confirmation, and updates from the public 1.1.3 and 1.2.0 installers. Linux verified that the public 1.2.0 AppImage replaces itself, restarts at 1.2.1 and reopens its synthetic vault. [Release rehearsal](https://github.com/FuryGamingASD/SQLitePass/actions/runs/45123919637)

The tag workflow repeats the native probes and verifies both public update feeds. [Tagged workflow](https://github.com/FuryGamingASD/SQLitePass/actions/workflows/verify.yml?query=branch%3Av1.2.1) · [Release provenance](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.1/release-provenance.json) · [SHA-256 checksums](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.1/checksums.json)

## Scope and limits

The Windows installer is unsigned. The native automation covers the tested desktop configuration. A dedicated Narrator, DPI and high-contrast sweep remains open. Linux Auto-Type targets X11 and XWayland. [Desktop guide](../CROSS_PLATFORM.md) · [Audit status](../AUDIT_STATUS.md)
