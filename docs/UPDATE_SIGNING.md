# Update package authentication

The Avalonia updater verifies a detached RSA/SHA-256 signature over a release manifest before accepting a Velopack update. The signed manifest binds the stable version, source commit, application ID, channel, package filename, byte length, and SHA-256 digest. Metadata and package downloads use fixed HTTPS GitHub release URLs, reject redirects outside GitHub-owned HTTPS hosts, and stop package writes at the signed byte length. The same trusted manifest is checked against downloaded package bytes and the local Velopack cache before the updater can extract or apply `Update.exe`.

GitHub Actions builds and verifies the release candidate with the repository variable `UPDATE_SIGNING_PUBLIC_KEY`. The value is base64-encoded RSA SubjectPublicKeyInfo DER of at least 3072 bits. The workflow embeds it in both clients and retains the verified bundle as an artifact. The signing operator reviews the candidate on a separate machine and signs it there. The publishing operator needs only the signed bundle and the public key.

## Offline key custody

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

Velopack's feed SHA-256 remains an additional corruption check. The detached signature is the publisher-authentication check and does not depend on the GitHub release token. The client accepts only the signed latest full package for its platform; delta packages are not used. Velopack's default version policy rejects versions at or below the installed version, so replay cannot downgrade an already updated client. A release server that withholds a newer release could still replay an older signed version that is newer than the installed version; preventing that freeze requires a trusted persistent release-version floor or a separate freshness service.

Existing installed versions that predate this verifier cannot authenticate the first update that installs it. After that transition, the new client enforces the pinned key. Changing the signing key requires a separately designed trust-key rotation and migration procedure; replacing the repository variable alone will make existing clients reject future manifests.

The client checks a cached package after acquiring Velopack's package lock, before Velopack can take its cached-package early return or extract `Update.exe`. This serializes the check against other Velopack update operations. A process running as the same user can write to the cache and the installed application and is outside the remote-update threat boundary.

## Transition for existing installations

1. Choose a new version higher than the public 1.2.2 and embed the independently approved public key in both release clients. Agree on who controls the matching private key and which build provenance they approve before signing.
2. Verify a clean release candidate and record the SHA-256 hashes of the Windows installer and Linux AppImage alongside the pinned public key fingerprint. Communicate those hashes through a channel independent of the release hosting account. The signed update manifest covers the full Velopack packages. The installer and standalone AppImage used for the first manual installation need their own verified hashes.
3. Rehearse installing the verified installer or AppImage over 1.2.2 in isolated Windows and Linux environments. Keep an independent vault backup. Check that the same vault opens after the transition and that the installed clients trust the selected key.
4. Rehearse an update between two builds that contain the pin. Preserve evidence that both platforms accept the correctly signed package and reject a mismatched signature or package. Publish only after the production verification and both rehearsals pass.

The publication script verifies the signature and checks that the signed manifest matches both packages, their feeds and the candidate provenance before accessing the release. The regression harness uses an ephemeral RSA key and a local CLI double to exercise publication and rejection paths.

## Build, sign and publish

1. Push the approved version tag from `main` and download `Avalonia-release-win-x64-linux-x64` from the successful tagged Verify run. Inspect the source commit, both native rehearsal results and the checksums before transferring the bundle to the signing machine.
2. On the signing machine, set `UPDATE_SIGNING_PUBLIC_KEY` to the approved public value and run `./scripts/Sign-AvaloniaUpdateRelease.ps1 -ArtifactDirectory <bundle> -PrivateKeyPath <encrypted-pem>`. PowerShell prompts for the passphrase. Transfer the resulting signed bundle to the publishing machine.
3. On the publishing machine, set `GITHUB_REPOSITORY` and the same `UPDATE_SIGNING_PUBLIC_KEY`. Run `./scripts/Publish-GitHubRelease.ps1` with `-ArtifactDirectory <bundle> -SourceCommit <commit> -ExistingTag <tag> -VerifyOnly`. Remove `-VerifyOnly` to publish after the rehearsal gates pass. Run `./scripts/Verify-PublicAvaloniaFeeds.ps1` with the same bundle and tag to capture the public feed evidence.

## Freshness policy

Official clients fetch `sqlitepass-update-freshness.json` from the latest release. The offline publisher signature binds the version, signed update manifest digest and a validity period of at most 30 days. The client rejects mismatched or expired checkpoints and uses the local UTC clock. An attacker controlling distribution can replay an earlier valid checkpoint during its remaining lifetime. After it expires, the updater reports a verification failure. Vault access remains local. Independent publication of the latest version and bootstrap digests remains important for the first manual installation.

Renew the checkpoint before expiry when the same release is still current. Confirm the latest version independently, then use `Sign-AvaloniaUpdateRelease.ps1 -RenewFreshnessTo <new-file>` with the verified bundle and offline signing key. Verify the envelope against the original manifest before replacing the public checkpoint. Only the checkpoint and its entry in the release checksums should change during renewal.
