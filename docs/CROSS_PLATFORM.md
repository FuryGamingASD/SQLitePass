# Windows and Linux desktop migration

The Avalonia application in `SQLitePass.Desktop` uses `SQLitePass.Core` for the existing vault format, authentication, verification, paging, and writes. Both builds read the version from `Directory.Build.props`. The Windows 1.1.3 release was built from the WPF application.

## Verification builds

The Verify workflow runs the same `SQLitePass.Tests` suite on Windows x64 and Ubuntu x64. Its Avalonia test opens a synthetic vault, rejects a wrong credential, searches entries, switches between active entries and the recycle bin, saves an edit with history, and checks that a vault lock closes the editor and empties the visible list. It uses the headless backend, which exercises the application's controls without testing the operating system's window, clipboard, or lock behavior.

The native jobs run `scripts/Build-AvaloniaCandidate.ps1` with `win-x64` and `linux-x64`. Each produces a verification package and its own Velopack manifest. Package notices come from the dependencies in each published runtime. Windows checks startup of the published executable. Linux checks startup through the AppImage under Xvfb. Both checks use a temporary profile and exit on opening the window. The assembly job checks the artifacts came from the same clean source tree and version, verifies their hashes, and stores both distributions in one Actions artifact. The verification packages use the `SQLitePassAvaloniaPreview` application identifier and are intended for synthetic vaults in isolated test environments.

## Before switching the release

- Finish migrating the vault browser and dialogs, including group management, credential changes, recovery, import and export, and entry custom fields, attachments, and history.
- Verify Windows session lock and capture protection on a native desktop. The Linux adapter listens for logind lock hints and suspend signals. Verify these signals on the supported Linux desktops and verify the inactivity fallback when logind is unavailable on the system bus.
- Port and test the clipboard, tray, and Auto-Type adapters. Clipboard clearing must preserve data subsequently copied by another application. Auto-Type must preserve target confirmation and abort when the target changes. Validate Linux capabilities per supported desktop session.
- Verify launch after Windows installation and from the AppImage on supported desktops, upgrade from the installed Windows line, update discovery and application on both platforms, and package notices for all shipped dependencies.
- Switch the publish job to a single version tag and the two verified package sets. Keep the existing Windows `win` feed compatible and publish the Linux `linux` feed in the same GitHub release with notes from `RELEASE_NOTES.md`.

## Package license sources

The package script copies published dependencies' license and notice files from the locked NuGet cache. These three licenses are preserved in `SQLitePass.Desktop/ThirdPartyLicenses` because the packages declare the license by name.

| File | Source at the pinned package commit |
| --- | --- |
| `Avalonia-LICENSE.txt` | [Avalonia 12.1.6](https://github.com/AvaloniaUI/Avalonia/blob/cf8e564674700348947e93ab19105f27afe859a9/licence.md) |
| `MicroCom-LICENSE.txt` | [MicroCom.Runtime 0.11.6](https://github.com/kekekeks/MicroCom/blob/76785efcafd91b5902fd19dd11145f6dd655b7b4/LICENSE) |
| `Tmds.DBus-LICENSE.txt` | [Tmds.DBus.Protocol 0.94.1](https://github.com/tmds/Tmds.DBus/blob/b4a7fed0b878f74cb54f7cca84d2889af4e596ba/COPYING) |
