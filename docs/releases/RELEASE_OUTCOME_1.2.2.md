# SQLitePass 1.2.2 release verification

The patch adjusts the workspace heading, fits the undecorated unlock window to its contents and highlights the folder selected by a drag. [Release notes](../../RELEASE_NOTES.md#sqlitepass-122)

| Platform | Distribution | Update feed |
| --- | --- | --- |
| Windows x64 | [Installer](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.2/SQLitePass-win-Setup.exe) · [Portable ZIP](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.2/SQLitePass-win-Portable.zip) | [Windows feed](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.2/releases.win.json) |
| Linux x64 | [AppImage](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.2/SQLitePass.AppImage) | [Linux feed](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.2/releases.linux.json) |

The desktop preview passed 169 shared tests per platform and checked the actual Windows window frame and drop outline with a synthetic vault. The tagged workflow repeats those tests and verifies upgrades from public 1.2.1 packages and both public update feeds. [Preview verification](https://github.com/FuryGamingASD/SQLitePass/actions/runs/45135297648) · [Tagged workflow](https://github.com/FuryGamingASD/SQLitePass/actions/workflows/verify.yml?query=branch%3Av1.2.2) · [Release provenance](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.2/release-provenance.json) · [SHA-256 checksums](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.2/checksums.json)
