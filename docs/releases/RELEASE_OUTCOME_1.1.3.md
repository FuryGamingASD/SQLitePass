# SQLitePass 1.1.3 release outcome

Published on 21 November 2026 at 00:07:15 CET. [GitHub release](https://github.com/FuryGamingASD/SQLitePass/releases/tag/v1.1.3)

## Publication and verification

- Tag `v1.1.3` points to `72c43c5e3262d081d5dd217013889e0812134a25` on `main`. The published files came from BuildId `1.1.3-bcb674b45f3d486593be2ee42de60f0a` in [Verify run #140](https://github.com/FuryGamingASD/SQLitePass/actions/runs/44487220560).
- CI passed 162 automated tests, 1,080 WPF layout cases and four Windows package scenarios. The package checks covered install, startup, tray activation, update from an open 1.1.2 unlock window and uninstall. The transitive NuGet scan reported zero known advisories. Both the application and installer were unsigned.
- Windows CI verified that synthetic clipboard content expired, newer content was preserved and the Windows exclusion formats were present. Interactive history and sync checks remain open.
- The repository publisher uploaded the verified CI artifact and checked the size and SHA-256 of every GitHub asset before publication. After publication, the Velopack library used by the app read the public feed anonymously and downloaded the full package. Its SHA-256 matched the feed and the CI artifact.

## Verification on the Windows VM

- The tester confirmed that the correct password opened the vault, the wrong password was rejected and creation could not overwrite an existing vault in V1. The file integrity check after the rejected attempts remains open.
- V2 passed with password and key file and with a key file alone. V3 passed on local storage for credential changes, legacy migration and backup recovery. A restore on a VirtualBox share returned an I/O error. The same fixture files restored on local storage. The tester confirmed that the reported difficulty typing symbols came from the guest keyboard layout.
- V4 passed for locking, tray behavior, reopening a second instance and opening the local vault after uninstall and reinstall. An earlier copy on the Desktop failed to open and the cause remains unknown. The problem did not recur on `Documents/SQLite`.
- The maintainer approved publication with V5–V8 deferred. Those checks cover Windows session locking and sensitive UI, clipboard history and sync, screen capture, native Auto-Type scenarios, and accessibility with Narrator, high contrast and display scaling. The original crash remains unreproduced. The documented Auto-Type P1 limitation and the lack of independent security review remain open.

## Published asset digests

| Asset | SHA-256 |
| --- | --- |
| `SQLitePass-win-Setup.exe` | `f9782717230a6aa1123f8e4bfb13db02a9db3202c3e6b2214e20926aa5d9c2b5` |
| `SQLitePass-win-Portable.zip` | `52b1ea9585f79a08ee0dcc7fc3050977bcfea25af1d263ef18f1a13607c1d336` |
| `SQLitePass-1.1.3-full.nupkg` | `62ff4f455ac502a3bb478b60ce4fbf7eaa47284d25754159f656d5c1912e9bd7` |
| `RELEASES` | `281c4e2db8a8e88ecb42ecca0f27fb3fed5bb4d754057c213a7ad2dd2631bc84` |
| `releases.win.json` | `f2c837e40329fe61e60fa11aae8c1872b6fa850db74066765d47d6f9da8b9aea` |
| `assets.win.json` | `ed8a440d7e2cda5265a89596d3f6d5ec8d0ce44e686f16f0c849c31c0e8d3ce2` |
| `release-provenance.json` | `d516f6ae7ef9c02a4cac555206157f30371f94d5e2a793bc923fd494d6448cfb` |
| `checksums.json` | `f21a66fee2839018db1fdb15de8b6a24a96117bdc1d171f83ea8eafbed9621e2` |

The original test plan and earlier runs are recorded in the [readiness report](RELEASE_READINESS_1.1.3.md).
