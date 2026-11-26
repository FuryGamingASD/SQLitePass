# Update package authentication

The Avalonia updater verifies a detached RSA/SHA-256 signature over a release manifest before accepting a Velopack update. The signed manifest binds the stable version, source commit, application ID, channel, package filename, byte length, and SHA-256 digest. Metadata and package downloads use fixed HTTPS GitHub release URLs, reject redirects outside GitHub-owned HTTPS hosts, and stop package writes at the signed byte length. The same trusted manifest is checked against downloaded package bytes and the local Velopack cache before the updater can extract or apply `Update.exe`.

GitHub Actions builds and verifies the release candidate with the repository variable `UPDATE_SIGNING_PUBLIC_KEY`. The value is base64-encoded RSA SubjectPublicKeyInfo DER of at least 3072 bits. The workflow embeds it in both clients and retains the verified bundle as an artifact. The signing operator reviews the candidate on a separate machine and signs it there. The publishing operator needs only the signed bundle and the public key.

## Offline key custody

The production pin for 1.3.0 is RSA 3072. The SHA-256 of its public SubjectPublicKeyInfo DER is `214BDFCF48E07D3631B4504F84959162CF5DF67D504A14CADFAE342D52AF2FCB`. Keep the encrypted private key under separate operator custody and use the provisioned key for signing and renewal.

On the operator-controlled Linux machine, run the following interactively. OpenSSL prompts for a passphrase while creating the encrypted key. Keep the passphrase and an encrypted backup under the signing operator's control.

```bash
(
set -euo pipefail
umask 077
key_dir="$HOME/.local/share/sqlitepass-release-signing"
mkdir -p -- "$key_dir"
key="$key_dir/update-signing.pem"
if [ -e "$key" ] || [ -L "$key" ]; then
    printf '%s\n' 'A signing key already exists. Use the established key or follow a key rotation plan.' >&2
else
    openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -aes-256-cbc -out "$key"
fi
openssl pkey -in "$key" -pubout -outform DER | openssl base64 -A
printf '\n'
)
```

Register only the resulting public value as `UPDATE_SIGNING_PUBLIC_KEY`. The private PEM stays on the signing machine. Keep the public key fingerprint with the independently reviewed source commit and candidate digests. Provisioning and using a separate HSM-backed RSA signer can use the same approved-bundle flow.

Velopack's feed SHA-256 remains an additional corruption check. The detached signature provides publisher authentication independently of the GitHub release token. The client accepts only the signed latest full package for its platform. The installed version is the downgrade floor, and the signed checkpoint limits how long a release server can replay an older release that is still newer than the installed version. [Freshness policy](#freshness-policy)

Existing installed versions that predate this verifier cannot authenticate the first update that installs it. After that transition, the new client enforces the pinned key. Changing the signing key requires a separately designed trust-key rotation and migration procedure; replacing the repository variable alone will make existing clients reject future manifests.

The client checks a cached package after acquiring Velopack's package lock, before Velopack can take its cached-package early return or extract `Update.exe`. This serializes the check against other Velopack update operations. A process running as the same user can write to the cache and the installed application and is outside the remote-update threat boundary.

## Transition for existing installations

1. Keep an independent vault backup and close SQLitePass. If the installed client predates 1.3.0, download a published Windows installer, portable ZIP or Linux AppImage for 1.3.0 or later; these versions contain the pinned publisher key. [Release downloads](https://github.com/FuryGamingASD/SQLitePass/releases/latest)
2. Compare the download's SHA-256 with a digest obtained through an independently trusted channel. On Windows use `Get-FileHash <download> -Algorithm SHA256`. On Linux use `sha256sum <download>`. GitHub also publishes a checksum manifest for file integrity. The signed update manifest covers the full Velopack packages, while these downloads establish the initial trust in the publisher key.
3. Run the Windows installer, replace the portable app files, or replace the Linux AppImage. Keep the AppImage executable and in a writable directory. Open the existing vault with its current credentials and check that it opens successfully.
4. Future installed versions can use **Check for Updates**. The pinned key authenticates the publisher, and a current signed checkpoint is required.

The publication script verifies the signature and checks that the signed manifest matches both packages, their feeds and the candidate provenance before accessing the release. The regression harness uses an ephemeral RSA key and a local CLI double to exercise publication and rejection paths.

## Build, sign and publish

1. Push the approved version tag from `main` and download `Avalonia-release-win-x64-linux-x64` from the successful tagged Verify run. Inspect the source commit, both native rehearsal results, the platform CycloneDX SBOMs and the checksums before transferring the bundle to the signing machine. The tagged workflow also creates GitHub SLSA provenance and SBOM attestations for the release packages. Verify a downloaded package with `gh attestation verify <artifact> --repo FuryGamingASD/SQLitePass`; this checks the GitHub build identity and SBOM association, while the offline RSA signature continues to authenticate updater packages. Preserve the bootstrap digests for independently communicating the first-installation hashes.
2. On the signing machine, set `UPDATE_SIGNING_PUBLIC_KEY` to the approved public value and run `./scripts/Sign-AvaloniaUpdateRelease.ps1 -ArtifactDirectory <bundle> -PrivateKeyPath <encrypted-pem>`. PowerShell prompts for the passphrase. Transfer the resulting signed bundle to the publishing machine.
3. On the publishing machine, set `GITHUB_REPOSITORY` and the same `UPDATE_SIGNING_PUBLIC_KEY`. Run `./scripts/Publish-GitHubRelease.ps1` with `-ArtifactDirectory <bundle> -SourceCommit <commit> -ExistingTag <tag> -VerifyOnly`. Remove `-VerifyOnly` to publish after the rehearsal gates pass. Run `./scripts/Verify-PublicAvaloniaFeeds.ps1` with the same bundle and tag to capture the public feed evidence.

## Freshness policy

Official clients fetch `sqlitepass-update-freshness.json` from the latest release. The offline publisher signature binds the version, signed update manifest digest and a validity period of at most 30 days. The client rejects mismatched or expired checkpoints and uses the local UTC clock. An attacker controlling distribution can replay an earlier valid checkpoint during its remaining lifetime. After it expires, the updater reports a verification failure. Vault access remains local. Independent publication of the latest version and bootstrap digests remains important for the first manual installation.

Renew the checkpoint before expiry when the same release is still current. Confirm the latest version independently, then use `Sign-AvaloniaUpdateRelease.ps1 -RenewFreshnessTo <new-file>` with the verified bundle and offline signing key. Replace the staged checkpoint and update its SHA-256 in `checksums.json`. Run the publisher with `-VerifyOnly` to check the renewed envelope and its binding to the original manifest. Replace only these two public assets and verify the download from the `latest` URL. Renew within 25 days of signing to allow time for verification.
