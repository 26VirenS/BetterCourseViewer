# Signed updates

Simpl for Mac, Simpl Courses (the Safari app) and Simpl for Windows install an update only when it is signed with
Simpl's own update key. A copy built by anyone else, or a file swapped on the way, is refused.

## How it works

- **The key.** An ECDSA P-256 key pair. The private half lives only in the repository secret `UPDATE_SIGNING_KEY`
  (and wherever its owner keeps a backup). The public half is built into each app:
  - `mac/Simpl/Support/UpdateSignature.swift`
  - `macos/Simpl Courses/Simpl Courses/Updater.swift` (`UpdateSignature`)
  - `windows/SimplFluent/Support/UpdateSignature.cs` (on the Windows branch)
- **Signing.** `scripts/sign-releases.py` puts `<file>.sig` beside each update file in its GitHub release:
  `Simpl-Mac-<version>.zip` (`mac-v*`), `Simpl-Courses-Mac-<version>.zip` (`v*`) and `Simpl-Setup.exe`
  (`windows-fluent-v*`, `windows-fluent-beta-v*`). The `.sig` is JSON: the tag, the file, its SHA-256, and a signature
  over `simpl-update-v1\n<tag>\n<file>\n<sha256>\n`. The script runs:
  - from the release scripts (`release-mac-native.sh`, `release-mac-app.sh`), for the release just made;
  - from `publish-site.sh` (the Site workflow: after every Mac or Windows release workflow, and every hour), for every
    release. It signs anything new, and re-signs a file replaced after it was signed.
- **Checking.** Before installing, an app downloads the file, then `<file>.sig` beside it. It rebuilds the signed text
  from the version it expects and the SHA-256 of what it downloaded, and checks the signature against its built-in
  key. No signature yet (a release made a minute ago): it tries again later. A wrong signature: nothing is installed.

## Setting up the key (once)

On your Mac, in Terminal:

1. Make the key (it stays on your Mac):

   ```
   openssl ecparam -name prime256v1 -genkey -noout | openssl pkcs8 -topk8 -nocrypt -out ~/simpl-update-key.pem
   ```

2. Show the public half, to build into the apps (it is safe to share):

   ```
   openssl pkey -in ~/simpl-update-key.pem -pubout
   ```

3. Copy the private half: `pbcopy < ~/simpl-update-key.pem`. In GitHub, go to the repository's **Settings → Secrets
   and variables → Actions → New repository secret**. Name it `UPDATE_SIGNING_KEY`, paste, and save.
4. Keep `~/simpl-update-key.pem` somewhere safe and private (a password manager), then delete it from the Mac.
   Never commit it.

Then run the Site workflow once (Actions → Site → Run workflow): it signs every existing release.

## Replacing the key

Add the new public key beside the old one in all three apps (`trustedKeys` / `TrustedKeys` holds a list), and ship
those versions. Then change the secret to the new private key, and run the Site workflow. Old releases keep their old
signatures, which the apps still accept until the old key is taken out of the list.
