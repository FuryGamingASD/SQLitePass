# SQLitePass 1.3.0 release verification

Version 1.3.0 adds read-only KeePass KDBX 4.0 and 4.1 import, optional machine policy, shorter plaintext lifetimes, clipboard safeguards and authenticated updates. Install this version manually to establish trust in its pinned publisher key. Existing SQLitePass vaults retain their format and credentials. [Release notes](../../RELEASE_NOTES.md#sqlitepass-130) · [First installation](../UPDATE_SIGNING.md#transition-for-existing-installations)

| Platform | Distribution |
| --- | --- |
| Windows x64 | [Installer](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.3.0/SQLitePass-win-Setup.exe) · [Portable ZIP](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.3.0/SQLitePass-win-Portable.zip) |
| Linux x64 | [AppImage](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.3.0/SQLitePass.AppImage) |

## Verification

The release rehearsal passed locked restore, the Release build, 227 shared tests per platform and the NuGet advisory gate. Windows verified fresh installation, tray activation, upgrades from 1.1.3, 1.2.0, 1.2.1 and 1.2.2, and clean uninstall. Linux verified that the public 1.2.2 AppImage upgraded to the exact candidate, restarted and reopened a synthetic vault. Linux also passed the XWayland desktop probes. [Branch rehearsal](https://github.com/FuryGamingASD/SQLitePass/actions/runs/45283170672)

The signed update rehearsals installed a temporary later Velopack package containing the same candidate binaries. An ephemeral RSA key signed the package manifest and its freshness checkpoint. Both app instances locked before restart and reopened the synthetic vault after the update. The rehearsal package and test key are confined to verification. The tagged Verify run tests the release again and stages the production bundle for separate signing. [Tagged verification runs](https://github.com/FuryGamingASD/SQLitePass/actions/workflows/verify.yml?query=branch%3Av1.3.0)

## Update trust and limits

The encrypted RSA production key is held outside GitHub Actions. Its public fingerprint and the checkpoint policy are in the signing runbook. The Windows installer has no Authenticode signature. Verify the initial download independently. Later updates require both the package signature and a current signed checkpoint. [Release provenance](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.3.0/release-provenance.json) · [SHA-256 checksums](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.3.0/checksums.json) · [Update signing](../UPDATE_SIGNING.md)

An independent cryptographic and application security audit remains open. The internal evidence and residual Auto-Type, clipboard and memory constraints are documented in the audit and threat model. [Audit status](../AUDIT_STATUS.md) · [Threat model](../THREAT_MODEL.md)
