# License sources

The checked-in `dependency-metadata.json` records the historical Windows WPF package graph. For 1.2.0, `scripts/Copy-AvaloniaNotices.ps1` generates a separate metadata file from the packages and runtime packs used by each published Avalonia distribution. The file ships under `THIRD_PARTY_LICENSES/` with the applicable notices. Additional license texts used by the Avalonia build are listed in the [desktop guide](../CROSS_PLATFORM.md#package-license-sources).

- Avalonia-LICENSE.txt: upstream commit 5d9e6d1b0e87337f74f233bead580db186412357, as declared by Avalonia 12.2.0: https://github.com/AvaloniaUI/Avalonia/blob/5d9e6d1b0e87337f74f233bead580db186412357/licence.md
- MicroCom-LICENSE.txt: upstream commit cd2fa3c89525fbe7b46bd13f9ec8c77bd829e815, as declared by MicroCom.Runtime 0.11.7: https://github.com/kekekeks/MicroCom/blob/cd2fa3c89525fbe7b46bd13f9ec8c77bd829e815/LICENSE
- Konscious-LICENSE.txt: upstream commit 4ed95a5377e411506ca6868409b5c7d7ecaa859b, as declared by Argon2 1.3.1: https://github.com/kmaragon/Konscious.Security.Cryptography/blob/4ed95a5377e411506ca6868409b5c7d7ecaa859b/LICENSE
- Velopack-LICENSE.txt: upstream commit 92d6a1c91716729d449034df5c50307dcce39493, as declared by Velopack 1.2.161: https://github.com/velopack/velopack/blob/92d6a1c91716729d449034df5c50307dcce39493/LICENSE
- BouncyCastle.Cryptography 2.7.0: upstream commit `4007498b13582d90ee1eda5d9920c324428b98b3`, as declared by the package; license expression: [MIT](https://licenses.nuget.org/MIT).
- Hardcodet.NotifyIcon.Wpf-LICENSE.txt: the license file distributed inside the restored 2.0.1 NuGet package.
- Microsoft-EFCore-LICENSE.txt: the upstream EF Core MIT license, https://github.com/dotnet/efcore/blob/main/LICENSE.txt. The precise restored package identities and copyright declarations are retained in the JSON; this license retrieval is not a source-code provenance attestation for those packages.
- Apache-2.0.txt: the complete standard Apache 2.0 terms from https://github.com/spdx/license-list-data/blob/main/text/Apache-2.0.txt. SQLitePCLRaw 3.0.5 package manifests declare Apache-2.0; repository commits supplied by the manifests are retained per package in dependency-metadata.json. The bundle package has no repository commit in its manifest, and this inventory does not imply a separate source audit of bundled binaries.
- SQLite-PUBLIC-DOMAIN.txt: the license notice distributed in the restored SQLite 3.53.4 NuGet package. The SQLite project confirms that the engine code is public domain at https://sqlite.org/copyright.html. This applies to the SQLite engine; SQLitePCLRaw remains Apache-2.0.
- Microsoft.Data.Sqlite 11.0.0 package manifests declare MIT and identify dotnet/dotnet commit 297acf930fb0f5c07fd4e51d78e521b4ad2e5d1f; package identities and copyright declarations are retained in dependency-metadata.json.
- The self-contained .NET runtime's LICENSE.txt and THIRD-PARTY-NOTICES.TXT remain in the publish output alongside Microsoft's runtime files.

Package declarations and repository pins were rechecked against the locked manifests on 26 November 2026. License notices are preserved for review; this inventory does not certify legal compliance or the provenance of every native binary.
