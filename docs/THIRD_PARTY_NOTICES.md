# Third-party components

SQLitePass is distributed under GPL-3.0; see `LICENSE`. Runtime component versions below are pinned by the project lock files. Each component retains its own license and copyright notices. The NuGet package links provide its upstream source and licensing information.

| Component | Version | Declared license | Package |
| --- | --- | --- | --- |
| Konscious.Security.Cryptography.Argon2 | 1.3.1 | MIT | [upstream](https://www.nuget.org/packages/Konscious.Security.Cryptography.Argon2/1.3.1) |
| Konscious.Security.Cryptography.Blake2 | 1.1.1 | MIT | [upstream](https://www.nuget.org/packages/Konscious.Security.Cryptography.Blake2/1.1.1) |
| Microsoft.Data.Sqlite / Microsoft.Data.Sqlite.Core | 11.0.0 | MIT | [upstream](https://www.nuget.org/packages/Microsoft.Data.Sqlite/11.0.0) |
| SQLite | 3.53.4 | Public Domain | [upstream](https://www.nuget.org/packages/SQLite/3.53.4) |
| SQLitePCLRaw.bundle_e_sqlite3 / config.e_sqlite3 / core / provider.e_sqlite3 | 3.0.5 | Apache-2.0 | [upstream](https://www.nuget.org/packages/SQLitePCLRaw.bundle_e_sqlite3/3.0.5) |
| Hardcodet.NotifyIcon.Wpf | 2.0.1 | MIT | [upstream](https://www.nuget.org/packages/Hardcodet.NotifyIcon.Wpf/2.0.1) |
| Velopack | 1.2.161 | MIT | [upstream](https://www.nuget.org/packages/Velopack/1.2.161) |

The self-contained package also includes Microsoft's .NET runtime and Windows Desktop runtime. Their runtime license and third-party notices must remain with the published files. SQLite 3.53.4 is the native engine package version; SQLite's code is public domain ([upstream notice](https://sqlite.org/copyright.html)). SQLitePCLRaw wrapper packages remain Apache-2.0 licensed.

Build and test tools (vpk, xUnit, VSTest and coverlet) are development dependencies and are not application features. See the corresponding lock files and tool manifest for their versions. This inventory is not a legal certification of a release; verify the notices accompanying the actual distribution when preparing public artifacts.

The distribution also contains THIRD_PARTY_LICENSES/ with preserved license text, copyright metadata for all eleven application packages, and source/retrieval details. Read LICENSE-SOURCES.md for package provenance details. Runtime notices are retained separately in the self-contained output.
