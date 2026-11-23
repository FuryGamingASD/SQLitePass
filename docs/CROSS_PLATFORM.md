# Windows and Linux desktop guide

The Avalonia application in `SQLitePass.Desktop` uses `SQLitePass.Core` for the existing vault format, authentication, verification, paging, and writes. Both builds read the version from `Directory.Build.props`. SQLitePass 1.2.0 ships the shared app on Windows x64 and Linux x64. The Windows 1.1.3 release used WPF. [1.2.0 release outcome](releases/RELEASE_OUTCOME_1.2.0.md)

## Verification builds

`SQLitePass.slnx` contains the shared Core, Avalonia application, test suite and test host, and builds on either operating system. Both native CI jobs call `scripts/Invoke-Ci.ps1` for the same locked restore, build, tests, publication checks and dependency audit. Packaging then runs once in each native job. Ordinary branch builds no longer generate an additional WPF installer. Historical Windows-only source and diagnostics remain in the opt-in `SQLitePass.LegacyWpf.slnx`; `scripts/Invoke-LegacyWpfCi.ps1 -Version 1.1.3` retains their explicit verification path. The old `build-installer.ps1` refuses versions newer than 1.1.3.

The Verify workflow runs the same `SQLitePass.Tests` suite on Windows x64 and Ubuntu x64. Its Avalonia tests use synthetic vaults to cover unlock, search, folder changes, the recycle bin, credential rotation, recovery, import and export, and entry revisions with custom fields, attachments, and expiration dates. They use the headless backend, which exercises the application's controls without testing the operating system's window, clipboard, or lock behavior.

Every Linux candidate also runs `scripts/Verify-AvaloniaXWayland.ps1` under an isolated Sway compositor. This extends the clipboard exchange with a Wayland-native peer to cover close-to-tray, StatusNotifier activation, restoration when the tray host disappears, Auto-Type into a separate XWayland process with focus-loss cancellation, and the private logind transport fixture. The Sway tray probe uses the application's close-to-tray preference; the Openbox probe covers minimize-to-tray. Assembly requires all four XWayland results for both preview and release candidates.

The native jobs run `scripts/Build-AvaloniaCandidate.ps1` with `win-x64` and `linux-x64`. Each produces a verification package and its own Velopack manifest. Package notices come from the dependencies in each published runtime. Windows verifies the published executable on its desktop. Linux verifies the AppImage on Xvfb with Openbox and an isolated D-Bus session. The probes cover clipboard round-tripping with a separate program, privacy formats, timed clearing, clearing on vault lock, preservation of another program's clipboard contents, and minimizing to and restoring from the tray. The Linux tray probe also exercises the fallback when the StatusNotifier host disappears. Auto-Type verification sends synthetic credentials to a separate process and checks that losing focus stops the sequence. The Linux probe uses the Italian keyboard layout and checks that an unmappable character prevents the entire sequence from starting. The assembly job requires these results, checks the commit, version and asset hashes, and stores both distributions in one Actions artifact. Ordinary verification builds use the `SQLitePassAvaloniaPreview` application identifier. Release rehearsals and tagged builds use the public `SQLitePass` package identity. The probes use synthetic vaults in isolated environments.

The [release rehearsal including the Xfce correction](https://github.com/FuryGamingASD/SQLitePass/actions/runs/45064931632) passed 168 shared tests per platform, desktop integrations, the Windows 1.1.3 upgrade and in-place AppImage upgrade and restart. The public build adds a native test that injects an unexpected dispatcher error and checks that the vault locks. [Release outcome](releases/RELEASE_OUTCOME_1.2.0.md)

## Session verification

CI checks Windows capture-affinity flags on the native window. On Linux, the private D-Bus fixture exercises logind lock and suspend, Xfce Screensaver activation and deactivation, and inactivity fallback after logind disappears. X11/Openbox and Sway/XWayland probes use separate synthetic target applications for clipboard and Auto-Type. The selected-entry Auto-Type flow uses two user-directed focus changes.

### Native session results for 1.2.0

- Windows VM. On 2026-11-22 the maintainer confirmed that pressing Win+L locked an open vault in the test VM. This result is a manual observation.
- Linux system logind. A dedicated SSH session on Ubuntu 26.04.1 LTS verified that the vault locks when the installed logind reports a session lock and stays locked when the hint clears.
- Linux suspend and resume. A disposable Ubuntu 24.04.5 LTS VM entered ACPI S3. SQLitePass had closed the synthetic vault by the time QEMU reported suspension, and the vault stayed closed on resume.
- Linux Xfce lock screen. In an Ubuntu 24.04.5 LTS VM with Xfce 4.18.3 and LightDM, `xflock4` activated the actual screen locker while logind's `LockedHint` stayed false. The original build left the vault open. The corrected build closed the vault while the screen was locked and kept it closed after desktop authentication.

For the interactive Windows lock check, run `./scripts/Verify-AvaloniaWindowsSessionLock.ps1 -Executable <path-to-candidate-exe>` in PowerShell 7 on a test VM. The wrapper checks for the Avalonia assembly beside the executable and rejects the historical WPF app before launch. The diagnostic opens a synthetic vault in a temporary profile, disables its inactivity timeout and backups, and waits up to four minutes for real Windows session events. Press Win+L when prompted in the app, then sign in again yourself. The vault must remain locked after Windows unlock. The script saves a JSON report under `artifacts/verification/windows-session-lock/<run-id>/session-lock.json` and fails if either event or the locked state is missing. An adjacent `execution.json` identifies the executable and application assembly by SHA-256, and the wrapper rejects a build that changes during the check. Close the verification window after reading the result. The executable also accepts `--verify-win-session-lock` without a report, or `--verify-win-session-lock <new-report-path>` with an existing parent directory; an existing report is never overwritten.

On a Linux host that registers SSH sessions with logind, run `bash scripts/Verify-AvaloniaLinuxSessionLock.sh <app-executable> <new-report-directory>` from a dedicated SSH session. The executable must contain the `--verify-linux-session-lock` diagnostic. The script starts an isolated X11 display and sets the lock hint on its own SSH session through the host's system D-Bus. The resulting JSON records both transitions and whether the synthetic vault remained locked. The interactive desktop locker and suspend/resume have separate checks.

## Shared preferences

Open **Settings** from the unlock screen or vault menu. The shared app uses the existing profile preferences. The theme, window behavior, clipboard timeout, lock rules and backup settings apply to the running app. A shorter clipboard timeout also applies to the current app-owned copy. Changing the interface language takes effect after restart. Capture exclusion is available on Windows, and startup registration is available in the official Windows installation. With automatic desktop locking enabled, the Linux app treats Xfce Screensaver activation as a lock. The screensaver can activate before it asks for the desktop password. The entry editor continues to mask passwords by default.

The browser supports text, exact tag and expiry filters, with paged sorting. The entry editor can save and restore custom fields and attachments, edit expiration and Auto-Type settings, and reveal values temporarily. The password generator is available from the unlock screen, the vault menu and the entry editor. **Use password** puts the result in the entry draft. **Copy** uses the same temporary clipboard as vault entries.

The unlocked window places minimize, maximize, restore and close next to the menus. Drag its SQLitePass header to move it or resize it from an edge. The close button follows the configured tray preference. The entry editor keeps the tabs on one row and reserves separate space for a scrollbar when resized below the height needed for all Credentials fields.

## Releasing both platforms

Set the shared version in `Directory.Build.props` and add one matching `# SQLitePass X.Y.Z` section to `RELEASE_NOTES.md`. For a version newer than 1.1.3, push its `vX.Y.Z` tag on a commit in `main`. Verify schedules the Windows and Linux builds in parallel. It assembles their verified assets, validates their hashes, and publishes the Windows installer, AppImage and both Velopack feeds in one GitHub release. The Windows package retains the `SQLitePass` package ID, `SQLitePass.exe` apphost and `win` channel. The Linux package uses the `linux` channel. After publication, the workflow compares both public feed files with the verified copies.

The shared app offers **Check for Updates** on the unlock screen and in the vault menu. Official packages check their platform's channel. A confirmed update downloads while the app is open and locks the vault before restarting. A cancelled download does not trigger automatic installation on the next startup. The Linux upgrade probe runs the checksum-verified public 1.2.1 AppImage against the candidate's local feed. It checks the replacement, restart, and reopening of a synthetic vault.

To rehearse this from a branch, run Verify with the `release_rehearsal` input. This builds with the public package identity and retains the combined bundle as a CI artifact. Publication requires a pushed release tag. The Windows job verifies fresh installation, tray activation, clean shutdown before updating, upgrades from the public 1.1.3, 1.2.0 and 1.2.1 installers, and uninstall with synthetic data outside the installation. It also checks the unlock-window frame, folder drop feedback and global Auto-Type on the isolated Windows desktop.

For a same-version replacement, pass the original public Windows installer checksum in `existing_windows_installer_sha256` during rehearsal. The Windows job installs that build, runs the replacement Setup over it, compares the installed assembly with the candidate package and checks that synthetic user data survives. An existing 1.2.2 installation needs a manual download when the published package is replaced at the same version.

## Recovery in the shared app

On the locked screen, **Restore from .backup** authenticates a backup using the entered password or key file. Choose an unused destination for the recovered vault. The backup, source vault and its journals stay available while the copy is verified. To recover a backup created before a credential change, use the credential that was valid when the backup was created.

## Desktop clipboard

Copying a user name or password uses the configured clipboard timeout. Foreground apps can paste the value until it expires, is replaced, or the vault locks. Windows requests exclusion from clipboard history and cloud synchronization; credential copying is blocked on the tested Windows 11 build where those privacy requests did not prevent remote history retention. The Linux X11 adapter advertises a password-manager hint and blocks copying when an X11 clipboard-manager selection owner or the Xfce Clipman D-Bus service is detected, and when a configured D-Bus session cannot be queried. Clipman was verified to retain a synthetic fixture after expiry and shutdown. Other managers may ignore the hint; an unrecognized manager or one that starts after the check may still retain a copy. Copy commands require a supported clipboard adapter.

## Auto-Type in the shared app

Select an active entry and choose **Auto-Type Selected Entry**. Switch to the destination field within 12 seconds. Confirm the displayed entry, window title and process, then return to that field within 12 seconds. Auto-Type aborts if the confirmed entry changes, if the destination loses focus during typing, or if the vault window is minimized or locked. Verify the actual destination before approving it. Window titles and process names alone cannot identify a website.

On Windows, press **Ctrl+Alt+A** with the destination focused and the vault unlocked. SQLitePass asks for the same confirmation when exactly one active entry matches the window title.

On Linux, Auto-Type requires an X11 target on a local X server with XTEST and X-Resource 1.2, plus the runtime libraries `libX11-xcb`, `libXres`, `libXtst` and `libxkbcommon`. It checks that the entire sequence is available in the active layout before sending any text. Wayland-native target windows are currently outside that adapter's scope. X11 clients share access to the local display, so use Auto-Type only in a trusted desktop session.

## Package license sources

The package script copies published dependencies' license and notice files from the locked NuGet cache. These three licenses are preserved in `SQLitePass.Desktop/ThirdPartyLicenses` because the packages declare the license by name.

| File | Source at the pinned package commit |
| --- | --- |
| `Avalonia-LICENSE.txt` | [Avalonia 12.1.6](https://github.com/AvaloniaUI/Avalonia/blob/cf8e564674700348947e93ab19105f27afe859a9/licence.md) |
| `MicroCom-LICENSE.txt` | [MicroCom.Runtime 0.11.6](https://github.com/kekekeks/MicroCom/blob/76785efcafd91b5902fd19dd11145f6dd655b7b4/LICENSE) |
| `Tmds.DBus-LICENSE.txt` | [Tmds.DBus.Protocol 0.94.1](https://github.com/tmds/Tmds.DBus/blob/b4a7fed0b878f74cb54f7cca84d2889af4e596ba/COPYING) |
