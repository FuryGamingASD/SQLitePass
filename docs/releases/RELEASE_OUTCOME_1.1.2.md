# SQLitePass 1.1.2 release outcome

Verified: 18 November 2026.

## Publication and provenance

- [Public release v1.1.2](https://github.com/FuryGamingASD/SQLitePass/releases/tag/v1.1.2), published at 2026-11-18 00:47:12 UTC.
- Tag `v1.1.2` points to merge commit `f8c378386a43ba4886b17d6b5341bd28881c367d` (PR #7).
- [Workflow 44209854259](https://github.com/FuryGamingASD/SQLitePass/actions/runs/44209854259) completed successfully. Both `windows` and `release` jobs passed; the release job published assets and verified the anonymous Velopack update feed and package.
- Application and installer are `NotSigned`. This release uses the existing vault format.

## Verification

- The Windows job built the release, ran the full test suite twice (**161 passed** each time), reported **0 known NuGet vulnerabilities**, verified the package, and passed silent installation, local update from 1.0.2 and uninstall checks.
- Installer checks did not launch SQLitePass or open a vault. No native desktop workflow is attested by this workflow.
- The release job verified the public update feed and package. The public GitHub release is not a substitute for independent cryptographic review or penetration testing.

## Published asset digests

SHA-256 values reported by GitHub for the eight release assets:

| Asset | SHA-256 |
| --- | --- |
| `SQLitePass-win-Setup.exe` | `9679209912914f611ff5aeb5b544b7c60b20b6643055f98d04f4691d99764723` |
| `SQLitePass-win-Portable.zip` | `fb25b08f4df89062d25c01fbe2209f73486f63cf3bac6140c9f7829f843c817b` |
| `SQLitePass-1.1.2-full.nupkg` | `c0f1dcfcb1f9ebe16b3cee26bf24b28e9f607d3d1c91557a7a9c597051bbaa77` |
| `RELEASES` | `08ce82ce1bbd9dbd6c434699be97c7e40d42ec12c7c1affcf277935bee30f261` |
| `releases.win.json` | `e4bdaf5fca5c5060350ca95e84791407519a441e19a45ae6b212c0d2678b5088` |
| `assets.win.json` | `f3d26f5acafeff815146c155c2b9ea22c2a6c2d8ea5cc41a20690bb9d3dda616` |
| `release-provenance.json` | `35e7949e36f9c7dd20443252ee67f8458a89817fcadf33f6936d9fdca5933729` |
| `checksums.json` | `4decface0be2123f2d8e095a6ebbe03cd04d81e84e5bafb7ef81dbd0d6d443c0` |

## Remaining assurance limits

- Auto-Type remains mitigated rather than fully closed: window/process/title checks cannot make global `SendInput` atomic with target validation.
- Native Windows checks for Auto-Type, lock transitions, clipboard history/cloud behavior, screen capture, DPI and assistive technology remain separate from CI and must be recorded as manual test results.
- Software fault injection does not prove survival of physical media, controller, filesystem or power failures.
- Authenticode signing was outside the scope of this release. Independent security review and penetration testing remain open.

The pre-publication snapshot is retained in [RELEASE_READINESS_1.1.2.md](RELEASE_READINESS_1.1.2.md). Its statements that publication had not happened describe the state at that time; this outcome records the final publication.
