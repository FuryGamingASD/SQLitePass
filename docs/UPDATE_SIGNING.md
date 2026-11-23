# Update package authentication

The Avalonia updater verifies a detached RSA/SHA-256 signature over a release manifest before accepting a Velopack update. The signed manifest binds the stable version, source commit, application ID, channel, package filename, byte length, and SHA-256 digest. Metadata and package downloads use fixed HTTPS GitHub release URLs, reject redirects outside GitHub-owned HTTPS hosts, and stop package writes at the signed byte length. The same trusted manifest is checked against downloaded package bytes and the local Velopack cache before the updater can extract or apply `Update.exe`.

The release workflow needs two repository settings before it can build or publish official packages:

- Repository variable `UPDATE_SIGNING_PUBLIC_KEY`: base64-encoded RSA SubjectPublicKeyInfo DER, at least 3072 bits. CI embeds this public key in both release clients.
- Actions secret `UPDATE_SIGNING_PRIVATE_KEY`: the matching PEM private key. Only the dedicated release signing step receives it. The workflow checks that its public half matches the client pin and fails if the secret or variable is missing.

Provision the pair outside the repository and store the private PEM only in the Actions secret. Do not commit the private key, put it in workflow output, or expose it to build/test jobs. No production signing key is stored in this source tree. Until the matching variable and secret are provisioned, official release builds and publishing fail closed; local preview builds have no signing key and cannot install public updates.

This Actions secret protects clients against replacement of GitHub release feeds, assets, or update packages by someone who has only the GitHub release token or release-hosting access: those changes cannot produce a valid signature. It does not protect against a repository administrator or anyone who can change the trusted workflow or signing script, change repository settings, or read the Actions secret. Such an actor can alter the signing job or use the key to sign malicious packages. Keep release changes behind normal branch/tag protections and review. For a stronger boundary, put the private key in an independently controlled protected environment or external signing service/HSM that verifies approved provenance and requires separate approval; the workflow should receive only a narrowly scoped signing operation, never the raw private key.

Velopack's feed SHA-256 remains an additional corruption check. The detached signature is the publisher-authentication check and does not depend on the GitHub release token. The client accepts only the signed latest full package for its platform; delta packages are not used. Velopack's default version policy rejects versions at or below the installed version, so replay cannot downgrade an already updated client. A release server that withholds a newer release could still replay an older signed version that is newer than the installed version; preventing that freeze requires a trusted persistent release-version floor or a separate freshness service.

Existing installed versions that predate this verifier cannot authenticate the first update that installs it. After that transition, the new client enforces the pinned key. Changing the signing key requires a separately designed trust-key rotation and migration procedure; replacing the repository variable alone will make existing clients reject future manifests.

The client checks a cached package after acquiring Velopack's package lock, before Velopack can take its cached-package early return or extract `Update.exe`. This serializes the check against other Velopack update operations. A process running as the same user can write to the cache and the installed application and is outside the remote-update threat boundary.

## Transition for existing installations

1. Choose a new version higher than the public 1.2.2 and embed the independently approved public key in both release clients. Agree on who controls the matching private key and which build provenance they approve before signing.
2. Verify a clean release candidate and record the SHA-256 hashes of the Windows installer and Linux AppImage alongside the pinned public key fingerprint. Communicate those hashes through a channel independent of the release hosting account. The signed update manifest covers the full Velopack packages. The installer and standalone AppImage used for the first manual installation need their own verified hashes.
3. Rehearse installing the verified installer or AppImage over 1.2.2 in isolated Windows and Linux environments. Keep an independent vault backup. Check that the same vault opens after the transition and that the installed clients trust the selected key.
4. Rehearse an update between two builds that contain the pin. Preserve evidence that both platforms accept the correctly signed package and reject a mismatched signature or package. Publish only after the production verification and both rehearsals pass.

The publication script verifies the signature and checks that the signed manifest matches both packages, their feeds and the candidate provenance before accessing the release. The regression harness uses an ephemeral RSA key and a local CLI double to exercise publication and rejection paths.

Choose the freshness source before claiming protection against a release host withholding the latest update. Remembering the highest authenticated version a client has seen can reject an older response after that observation. A client that has never seen the new release needs a separate trusted source to detect the omission. A time-limited manifest also needs a renewal policy for releases that remain current.
