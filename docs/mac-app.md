# The Mac app

Simpl Courses on a Mac is an app that carries the Safari extension inside it, downloaded from
simplcourses.com rather than the App Store. The app is more than a wrapper now:

- **Its window is the settings.** The extension's own settings page (`extension/options/`) runs in
  the app's window over a small bridge (`macos/Simpl Courses/Simpl Courses/Resources/Bridge.js`),
  with a *This Mac* section first: Safari's word on the extension and a button to Safari's
  Extensions settings, updates, *Install updates by themselves* and *Open at login*. The two
  sections that need the browser's Canvas session (courses and targets, grades) are not there;
  those are set on Canvas's own pages (the Grades page, All Courses).
- **The extension follows the app.** Settings live in one JSON file in the app group container
  (`SharedStore.swift`, built into both targets). The app writes it; the extension's background
  asks its native handler (`SafariWebExtensionHandler.swift`) for it on every Canvas page load
  and every few seconds while Safari is up, takes whatever is newer (a revision number says), and
  writes its own few switches (the look switch on a page, the popup) back up. Reset everything in
  the app leaves the extension a *wipe* command, run and acknowledged on the next sync. Settings
  in the popup and in the account panel open the app (`simplcourses://settings`).
- **It keeps itself up to date.** `Updater.swift` reads `https://simplcourses.com/app/latest.json`
  at launch and every hour (the app stays running after its window closes, as a mark in the menu
  bar; it is a login item unless that is turned off). A newer version is downloaded, its SHA-256
  checked against the feed, unpacked with `ditto`, checked to be Simpl Courses and signed, moved
  into this copy's place (this copy goes to the Trash) and opened again — or into the Applications
  folder when this copy cannot be replaced where it is (a temporary copy macOS made, a folder that
  cannot be written). *Install updates by themselves* is on by default; off, the window shows
  *Update now*.
- **It opens on a first screen, and lives in the Applications folder.** The first launch shows a
  black screen — *Let’s make Canvas simpler*, *Open Safari* — and the button opens Safari's
  Extensions settings on the extension. A downloaded app opened straight from Downloads is run by
  macOS from a hidden temporary copy (app translocation), and Safari cannot see an extension inside
  such a copy: on a Mac with the app open, that is the one reason the window says Safari does not
  have the extension. So the button, a prompt on any later launch from the wrong place, and *Move
  to Applications* on the *This Mac* card all move the app there (`Placement` in
  `AppDelegate.swift`: a copy in Applications, the download's quarantine mark removed so macOS runs
  it in place, the copy the user opened to the Trash, the new one opened in its place).

Because the app replaces itself, it is not sandboxed (the extension still is). Hardened runtime
stays on, which is what notarization needs.

## The feed and the download address

Both live on the `site` branch, which Cloudflare deploys on every push:

| Address | What |
| --- | --- |
| `/app/latest.json` | `{ version, url, download, sha256, size, minimumOS, notes, published }` — the app compares `version` with its own, downloads `url` (which redirects to `download`) and checks `sha256`. |
| `/download/mac` | A 302 to the zip on the GitHub Release (`public/_redirects`). The site's download button points here, so the address never changes. |

`scripts/release-mac-app.sh` writes both after every release. The file committed on the branch
by hand only names the version that shipped with it; the script overwrites it.

## Releasing

The `Package` workflow builds the zips on every push and, on a version tag (or *Run workflow*),
publishes the Release. Its `mac-app` job then builds the app on a Mac runner, signs it with a
Developer ID, notarizes and staples it, zips it, uploads the zip to the same Release and pushes the
feed and the redirect to the `site` branch. It needs five secrets under **Settings → Secrets and
variables → Actions**; without them it says so and skips, so tagging is safe before they are set.

| Secret | Value |
| --- | --- |
| `MAC_CERT_P12` | The **Developer ID Application** certificate with its private key, exported from Keychain Access as a `.p12`, then `base64 -i cert.p12 \| pbcopy`. Made at developer.apple.com → Certificates → + → *Developer ID Application*. |
| `MAC_CERT_PASSWORD` | The password given when exporting the `.p12`. |
| `APPLE_ID` | The Apple ID of the team's account holder. |
| `APPLE_APP_PASSWORD` | An app-specific password for that Apple ID (appleid.apple.com → Sign-In and Security → App-Specific Passwords), which `notarytool` uses. |
| `APPLE_TEAM_ID` | The ten-character team identifier (developer.apple.com → Membership). |

The first notarization can take a while (Apple's queue); after that a release is usually done in
ten to fifteen minutes. A user's app finds it on its next hourly check.

## Building by hand

```bash
./scripts/build-mac-app.sh --build     # ad-hoc signed, for this Mac
./scripts/build-mac-app.sh --open      # the Xcode project
```

The Xcode project under `macos/` is kept by hand now — it carries the app's own files
(`AppDelegate.swift`, `ViewController.swift`, `Updater.swift`, `Shared/SharedStore.swift`,
`Resources/Bridge.js`, the two `.entitlements`) — so do not delete `macos/` to regenerate it with
Apple's converter: the regenerated project would not have them. An ad-hoc build has no team, so
its app group falls back to Application Support and its extension is unsigned: Safari needs
*Allow unsigned extensions* on to list it, and the store the app and the extension share is only
shared when both can reach the same folder (a signed build).

## What runs where

| | Safari (the app) | Chrome / Edge | Firefox | iOS app |
| --- | --- | --- | --- | --- |
| Settings page | the app's window | the extension's own page | the extension's own page | in the app |
| Source of truth | the app's shared store | `storage.local` | `storage.local` | the app's storage file |
| Finding a school's own Canvas | on its own (`content/sniff.js`) once Safari lets it see every website — one press on the page after install; or a site at a time, as Safari asks | on its own (`content/sniff.js`) | *Enable on this site* | the address typed in |
| Updates | the app, hourly | the Web Store | manual | the App Store |

`node scripts/dev/mac-window-test.mjs` runs the window's page in Chromium over a fake app;
`node scripts/dev/app-sync-test.mjs` runs the extension's side of the sync with a fake handler.
