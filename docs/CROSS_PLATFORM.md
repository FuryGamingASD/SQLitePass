# Windows and Linux desktop migration

The Avalonia application in `SQLitePass.Desktop` uses `SQLitePass.Core` for the existing vault format, authentication, verification, paging, and writes. Both builds read the version from `Directory.Build.props`. The Windows 1.1.3 release was built from the WPF application.

## Verification builds

The Verify workflow runs the same `SQLitePass.Tests` suite on Windows x64 and Ubuntu x64. Its Avalonia tests use synthetic vaults to cover unlock, search, folder changes, the recycle bin, credential rotation, recovery, import and export, and entry revisions with custom fields, attachments, and expiration dates. They use the headless backend, which exercises the application's controls without testing the operating system's window, clipboard, or lock behavior.

The native jobs run `scripts/Build-AvaloniaCandidate.ps1` with `win-x64` and `linux-x64`. Each produces a verification package and its own Velopack manifest. Package notices come from the dependencies in each published runtime. Windows verifies the published executable on its desktop. Linux verifies the AppImage on Xvfb with Openbox and an isolated D-Bus session. The probes cover clipboard round-tripping with a separate program, privacy formats, timed clearing, clearing on vault lock, preservation of another program's clipboard contents, and minimizing to and restoring from the tray. The Linux tray probe also exercises the fallback when the StatusNotifier host disappears. Auto-Type verification sends synthetic credentials to a separate process and checks that losing focus stops the sequence. The Linux probe uses the Italian keyboard layout and checks that an unmappable character prevents the entire sequence from starting. The assembly job requires these results, checks the commit, version and asset hashes, and stores both distributions in one Actions artifact. The verification packages use the `SQLitePassAvaloniaPreview` application identifier and are intended for synthetic vaults in isolated test environments.

## Before switching the release

- Verify Windows session lock with Win+L on a native desktop, and Linux lock and suspend with the supported desktop environments and their installed logind. CI checks Windows capture-affinity flags on the actual app window. The Linux AppImage test sends logind-compatible lock and suspend events over a private D-Bus and tests inactivity fallback when that service disappears.
- Verify tray and Auto-Type under XWayland on the supported desktop sessions. The selected-entry Auto-Type flow uses two user-directed focus changes and works without a global shortcut. The Linux release workflow tests clipboard exchange between the XWayland app and a separate Wayland client under Sway.
- Check a tagged publication and verify that both public update feeds are reachable. The rehearsal verifies upgrade from the public Windows 1.1.3 installer and in-place replacement and restart of a mounted synthetic AppImage using the Linux release feed staged in CI.

## Shared preferences

Open **Settings** from the unlock screen or vault menu. The shared app uses the existing profile preferences. The theme, window behavior, clipboard timeout, lock rules and backup settings apply to the running app. A shorter clipboard timeout also applies to the current app-owned copy. Changing the interface language takes effect after restart. Capture exclusion is available on Windows, and startup registration is available in the official Windows installation. The entry editor continues to mask passwords by default.

The browser supports text, exact tag and expiry filters, with paged sorting. The entry editor can save and restore custom fields and attachments, edit expiration and Auto-Type settings, and reveal values temporarily. The password generator is available from the unlock screen, the vault menu and the entry editor. **Use password** puts the result in the entry draft. **Copy** uses the same temporary clipboard as vault entries.

## Releasing both platforms

Set the shared version in `Directory.Build.props` and add one matching `# SQLitePass X.Y.Z` section to `RELEASE_NOTES.md`. For a version newer than 1.1.3, push its `vX.Y.Z` tag on a commit in `main`. Verify schedules the Windows and Linux builds in parallel. It assembles their verified assets, validates their hashes, and publishes the Windows installer, AppImage and both Velopack feeds in one GitHub release. The Windows package retains the `SQLitePass` package ID, `SQLitePass.exe` apphost and `win` channel. The Linux package uses the `linux` channel. After publication, the workflow compares both public feed files with the verified copies.

The shared app offers **Check for Updates** on the unlock screen and in the vault menu. Official packages check their platform's channel. A confirmed update downloads while the app is open and locks the vault before restarting. A cancelled download does not trigger automatic installation on the next startup. For Linux release builds, the AppImage upgrade probe uses a mounted synthetic 1.1.3 AppImage and the candidate's local feed. It checks that the downloaded release replaces the installed file, restarts at the new version, and can reopen the synthetic vault.

To rehearse this from a branch, run Verify with the `release_rehearsal` input. This builds with the public package identity and retains the combined bundle as a CI artifact. Publication requires a pushed release tag. The Windows job verifies fresh installation, opening an existing tray instance, a clean shutdown before updating, upgrade from the public 1.1.3 WPF release, and uninstall with synthetic data outside the installation. The rehearsal Windows installer uses the public installation identity. Use it in an isolated test profile.

## Recovery in the shared app

On the locked screen, **Restore from .backup** authenticates a backup using the entered password or key file. Choose an unused destination for the recovered vault. The backup, source vault and its journals stay available while the copy is verified. To recover a backup created before a credential change, use the credential that was valid when the backup was created.

## Desktop clipboard

Copying a user name or password uses the configured clipboard timeout. Foreground apps can paste the value until it expires, is replaced, or the vault locks. Windows requests exclusion from clipboard history and cloud synchronization. The Linux adapter uses the X11 clipboard and advertises a password-manager hint. Clipboard history tools can retain copies if they ignore that hint. Copy commands require a supported clipboard adapter.

## Auto-Type in the shared app

Select an active entry and choose **Auto-Type Selected Entry**. Switch to the destination field within 12 seconds. Confirm the displayed entry, window title and process, then return to that field within 12 seconds. Auto-Type aborts if the confirmed entry changes, if the destination loses focus during typing, or if the vault window is minimized or locked. Verify the actual destination before approving it. Window titles and process names alone cannot identify a website.

On Linux, Auto-Type requires an X11 target on a local X server with XTEST and X-Resource 1.2, plus the runtime libraries `libX11-xcb`, `libXres`, `libXtst` and `libxkbcommon`. It checks that the entire sequence is available in the active layout before sending any text. Wayland-native target windows are currently outside that adapter's scope. X11 clients share access to the local display, so use Auto-Type only in a trusted desktop session.

## Package license sources

The package script copies published dependencies' license and notice files from the locked NuGet cache. These three licenses are preserved in `SQLitePass.Desktop/ThirdPartyLicenses` because the packages declare the license by name.

| File | Source at the pinned package commit |
| --- | --- |
| `Avalonia-LICENSE.txt` | [Avalonia 12.1.6](https://github.com/AvaloniaUI/Avalonia/blob/cf8e564674700348947e93ab19105f27afe859a9/licence.md) |
| `MicroCom-LICENSE.txt` | [MicroCom.Runtime 0.11.6](https://github.com/kekekeks/MicroCom/blob/76785efcafd91b5902fd19dd11145f6dd655b7b4/LICENSE) |
| `Tmds.DBus-LICENSE.txt` | [Tmds.DBus.Protocol 0.94.1](https://github.com/tmds/Tmds.DBus/blob/b4a7fed0b878f74cb54f7cca84d2889af4e596ba/COPYING) |
