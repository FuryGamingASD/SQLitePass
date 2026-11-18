# SQLitePass 1.1.3

Maintenance and reliability release.

## Changes

- Cancel active Auto-Type when the vault window is minimized, even when automatic locking on minimize is disabled.
- Lock the active vault before handling an unexpected dispatcher error; shut down if no authenticated vault is active or locking cannot complete.
- Safely complete an entry editor close when a vault lock arrives during its discard prompt.
- Preserve second-instance activation and updater shutdown requests that arrive while the app is starting.
- Remove the uninstalled copy's Windows startup entry when it belongs to that installation.
- Correct security and recovery documentation to describe verified behavior and known limits.

## Security and compatibility

- Auto-Type remains available. It uses Windows' global simulated keyboard input. Foreground, process and title checks plus confirmation reduce risk, but they cannot bind each input atomically to a specific window. The target can change between validation and input. Treat this as a remaining P1 limitation and verify the target before each use.
- No vault-format change or migration is required.
- The release package is unsigned under the user's existing signing waiver. Windows may show a publisher or reputation warning.
