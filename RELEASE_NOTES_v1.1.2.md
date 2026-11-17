# SQLitePass 1.1.2

Maintenance release.

- Update Microsoft.Data.Sqlite to 11.0.0 and refresh locked transitive dependencies and their license notices.
- Report stale vault revisions and concurrent changes through a specific conflict error.
- Recheck Auto-Type target identity after each input event and stop when its window, process, or title changes.
- Surface unexpected drag-and-drop failures through the safe-lock handler instead of silently ignoring them.
- Distinguish invalid canary data from unexpected cryptographic engine errors.
- No vault format change or migration is introduced; existing vaults and user preferences are unchanged.
- Authenticode signing remains waived as previously agreed. The application and installer are unsigned.

The audit still records native clipboard, Auto-Type, lock, accessibility/DPI and physical durability checks, plus independent security review, as outstanding. See [release readiness](docs/RELEASE_READINESS_1.1.2.md) for the current status and limitations.
