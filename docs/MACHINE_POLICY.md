# Machine-managed policy

SQLitePass supports personal installations and administrator-managed computers. If no machine policy is present, the existing per-user settings remain in effect. A machine policy can add restrictions that the user cannot turn off.

The policy is read once when SQLitePass starts. Restart the application after changing it.

## Windows

Set DWORD values under `HKLM\SOFTWARE\Policies\SQLitePass` using Group Policy, Intune, or another administrator-controlled tool. Use the names and values below:

| Value | Meaning |
| --- | --- |
| `maxAutoLockMinutes` | Required maximum idle timeout, from 1 to 60 minutes. A user timeout of 0 or a larger value is clamped to this maximum; a shorter timeout remains allowed. |
| `forceLockOnMinimize` | Set to `1` to require locking when the window is minimized or sent to the tray. |
| `forceLockOnSessionLock` | Set to `1` to require locking when Windows or the Linux desktop session locks. |
| `disableExport` | Set to `1` to disable CSV vault export and attachment export. Encrypted recovery remains available. |
| `disableAutoType` | Set to `1` to disable desktop Auto-Type and its global hotkey. |

Boolean values must be `0` or `1`. Values omitted from the key do not add a restriction. The application reads the 64-bit machine registry view.

## Linux

Create `/etc/sqlitepass/policy.json` as a root-owned regular file. `/etc/sqlitepass` and the policy file must not be group- or world-writable. The reader rejects symbolic links and invalid file types. A typical installation uses a root-owned `0755` directory and a root-owned `0644` file.

Example:

```json
{
  "maxAutoLockMinutes": 10,
  "forceLockOnMinimize": true,
  "forceLockOnSessionLock": true,
  "disableExport": true,
  "disableAutoType": true
}
```

The document must be UTF-8 without a byte-order mark, no larger than 4 KiB, and contain only the supported fields above. Omitted fields add no restriction.

## Invalid policy

If a policy is present but unreadable, malformed, unsafe, or contains an unknown field, SQLitePass fails closed: it will not open or create a vault, export or Auto-Type is disabled, and the effective idle timeout is one minute. Correct the machine policy and restart SQLitePass. Personal installations without a policy are unaffected.
