# Third-party components

SQLitePass is distributed under GPL-3.0; see `LICENSE`. Runtime component versions below are pinned by the project lock files. Each component retains its own license and copyright notices. The NuGet package links provide its upstream source and licensing information.

| Component | Version | Declared license | Package |
| --- | --- | --- | --- |
| Konscious.Security.Cryptography.Argon2 | 1.3.1 | MIT | [upstream](https://www.nuget.org/packages/Konscious.Security.Cryptography.Argon2/1.3.1) |
| Konscious.Security.Cryptography.Blake2 | 1.1.1 | MIT | [upstream](https://www.nuget.org/packages/Konscious.Security.Cryptography.Blake2/1.1.1) |
| Microsoft.Data.Sqlite / Microsoft.Data.Sqlite.Core | 10.0.14 | MIT | [upstream](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.14) |
| SQLitePCLRaw.bundle_e_sqlite3 / core / provider.e_sqlite3 / lib.e_sqlite3 | 2.1.13 | Apache-2.0 | [upstream](https://www.nuget.org/packages/SQLitePCLRaw.bundle_e_sqlite3/2.1.13) |
| Hardcodet.NotifyIcon.Wpf | 2.0.1 | MIT | [upstream](https://www.nuget.org/packages/Hardcodet.NotifyIcon.Wpf/2.0.1) |
| Velopack | 1.2.161 | MIT | [upstream](https://www.nuget.org/packages/Velopack/1.2.161) |

The self-contained package also includes Microsoft's .NET runtime and Windows Desktop runtime. Their runtime license and third-party notices must remain with the published files. SQLite's native engine reports version 3.53.3 in the audited development build; the engine's upstream licensing information is available at [upstream](https://sqlite.org/copyright.html).

Build and test tools (vpk, xUnit, VSTest and coverlet) are development dependencies and are not application features. See the corresponding lock files and tool manifest for their versions. This inventory is not a legal certification of a release; verify the notices accompanying the actual distribution when preparing public artifacts.

The distribution also contains THIRD_PARTY_LICENSES/ with preserved license text, copyright metadata for all ten application packages, and source/retrieval details. Read LICENSE-SOURCES.md for provenance limitations, including the unavailable SQLitePCLRaw source tag. Runtime notices are retained separately in the self-contained output.
