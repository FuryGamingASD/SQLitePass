# Third-party components

SQLitePass is distributed under GPL-3.0. Runtime dependencies retain their own license and copyright notices. The project lock files pin the package versions.

| Component | Version | Declared license |
| --- | --- | --- |
| Avalonia desktop UI and Fluent theme | 12.1.6 | MIT |
| MicroCom.Runtime | 0.11.6 | MIT |
| Tmds.DBus.Protocol | 0.94.1 | MIT |
| Microsoft.Win32.SystemEvents | 10.0.15 | MIT |
| Konscious.Security.Cryptography.Argon2 | 1.3.1 | MIT |
| Konscious.Security.Cryptography.Blake2 | 1.1.1 | MIT |
| Microsoft.Data.Sqlite and Microsoft.Data.Sqlite.Core | 11.0.0 | MIT |
| SQLite engine | 3.53.4 | Public domain |
| SQLitePCLRaw bundle, configuration, core and provider | 3.0.5 | Apache-2.0 |
| Velopack | 1.2.161 | MIT |

This table lists principal dependencies. Each Windows or Linux package includes its complete release-specific inventory in `THIRD_PARTY_LICENSES/dependency-metadata.json`, with the corresponding notices. The inventory includes the relevant .NET runtime pack and native libraries such as SkiaSharp and HarfBuzz. Build and test tool versions are recorded in their own lock files and tool manifest. [Avalonia license sources](CROSS_PLATFORM.md#package-license-sources) · [Repository license](../LICENSE) · [SQLite public domain notice](https://sqlite.org/copyright.html)

The historical WPF notices and package metadata are retained for the Windows 1.1.x line. [Historical license sources](licenses/LICENSE-SOURCES.md)
