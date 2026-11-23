# SQLitePass 1.2.1 release verification

SQLitePass 1.2.1 restores the desktop interface and workflows documented in the comparison with the original 1.2.0 release. It uses the same Windows installation identity, Linux update channel, vault format and credentials. [Feature-parity audit](../audit/FEATURE_PARITY_1.2.0.md) · [Release notes](../../RELEASE_NOTES.md#sqlitepass-121)

## Downloads and verification

| Platform | Distribution | Update feed |
| --- | --- | --- |
| Windows x64 | [Installer](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.1/SQLitePass-win-Setup.exe) · [Portable ZIP](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.1/SQLitePass-win-Portable.zip) | [Windows feed](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.1/releases.win.json) |
| Linux x64 | [AppImage](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.1/SQLitePass.AppImage) | [Linux feed](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.1/releases.linux.json) |

The tag workflow runs the shared suite and native probes on both platforms. Its Windows checks include folder drag-and-drop, global Auto-Type and upgrade from the public 1.1.3 and 1.2.0 packages. The Linux checks run the public 1.2.0 AppImage against the new feed and verify restart and synthetic vault preservation. [Tagged workflow](https://github.com/FuryGamingASD/SQLitePass/actions/workflows/verify.yml?query=branch%3Av1.2.1) · [Release provenance](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.1/release-provenance.json) · [SHA-256 checksums](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.1/checksums.json)

## Scope and limits

The Windows installer is unsigned. The native automation covers the tested desktop configuration. A dedicated Narrator, DPI and high-contrast sweep remains open. Linux Auto-Type targets X11 and XWayland. [Desktop guide](../CROSS_PLATFORM.md) · [Audit status](../AUDIT_STATUS.md)
