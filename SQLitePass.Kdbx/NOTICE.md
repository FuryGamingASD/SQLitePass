# Third-party source notice

This directory contains a vendored snapshot of **DgNet.Keepass** by pidamg, from upstream commit `9a4928aad73150d9f5a1a0ce735c86b77be6f094` (NuGet package version 0.2.0 source tree).

- Upstream: <https://github.com/pidamg/DgNet.Keepass>
- License: MIT, reproduced in [LICENSE](LICENSE)
- Upstream API and source tests were reviewed before vendoring. SQLitePass uses the KDBX 4 read path only and applies additional bounds checks around hostile file sizes, decompression, and KDF parameters.

SQLitePass modifications to this snapshot are not part of the upstream project.
