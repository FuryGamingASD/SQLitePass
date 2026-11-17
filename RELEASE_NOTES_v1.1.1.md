# SQLitePass 1.1.1

Hotfix for Windows launch and window activation.

- Launching SQLitePass again now restores the existing unlock or vault window from a minimized state and brings it to the foreground.
- If the app is running in the notification area without a window, launching it opens the unlock window in the existing process.
- Updates and uninstalls now request a clean application shutdown and wait for the running process to exit.
- Vault format, vault files, and saved preferences are unchanged.
- Authenticode signing remains waived as previously agreed. The installer and application are unsigned.

This hotfix does not complete the remaining native UI and independent security review items recorded in the audit.
