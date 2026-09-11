# Simpl Courses for iOS

The iOS app is the extension in a different wrapper: a full-screen web view of the student's own Canvas site, with the same scripts and stylesheet injected into every Canvas page. Signing in happens on Canvas's real login page inside the app, so the app never sees a password and needs no API key or developer key from the school.

## How it works

```
ios/
  project.yml                     XcodeGen spec → SimplCourses.xcodeproj (generated, not committed)
  SimplCourses/
    App/                          SwiftUI: school picker, the browser screen, the settings sheet
    Web/WebController.swift       one WKWebView with the extension injected + navigation/dialog delegates
    Web/ScriptBundle.swift        reads the bundled extension/manifest.json and builds the WKUserScripts
    Web/bridge.js                 stands in for the browser-extension APIs inside the page
    Web/Bridge.swift              the native half: storage, the fetch proxy, "open settings"
    Web/BridgeStore.swift         storage.local as one JSON file in Application Support
    Web/FetchProxy.swift          streams the smart-panel providers' replies through URLSession
    Resources/Assets.xcassets     the app icon (rendered by scripts/make-icons.mjs)
```

- **The extension ships unchanged.** The `extension/` folder is a folder reference in the Xcode target, so the app bundle carries the same files the Safari and Chrome builds do. `ScriptBundle` reads `manifest.json` and injects each `content_scripts` entry at its `run_at`, into an isolated content world (like an extension's content scripts), main frame only, and only on the configured Canvas host. Single sign-on pages on other hosts are left untouched.
- **`browser.*` is a bridge.** `bridge.js` is injected first and provides `storage.local` (+ `onChanged`), `runtime.sendMessage`/`onMessage`, `runtime.connect`/`onConnect`, `getManifest`, `openOptionsPage`, and inert `action`/`permissions`/`tabs`. `background.js` runs inside the same page, so the smart panel's streaming port and the settings page's key test work exactly as in the extension. Storage lives in a native JSON file shared by every web view the app opens; a change in the settings sheet reaches the Canvas view through `storage.onChanged`.
- **Provider requests go native.** Inside the page a `fetch` to `api.anthropic.com` or `api.openai.com` would hit CORS, so the bridge hands those to `FetchProxy`, which performs them with URLSession and streams the body back chunk by chunk into a `ReadableStream`. Canvas API calls stay the page's own fetch, with the Canvas session cookie.
- **Native chrome is minimal.** A navigation bar with the school's host and a menu (Settings, Reload, Open in Safari, Sign out). The settings sheet shows the school, sign-out, the version, and the extension's own `options.html` loaded from the bundle (its "Canvas sites" section is hidden: the school is chosen natively). JavaScript alerts and confirms (leaving a quiz, submitting) become native alerts; `window.open` to another host opens an in-app Safari sheet; downloads are handed to Safari.

## Build and run

1. Xcode 15 or later, and Homebrew (for XcodeGen; the script installs it).
2. Generate and open the project:
   ```bash
   ./scripts/build-ios-app.sh --open
   ```
3. In Xcode select the **SimplCourses** target → Signing & Capabilities → choose your Team (a personal Apple ID team is enough to run on your own iPhone).
4. Pick your iPhone or a simulator and press ⌘R.
5. In the app enter your school's Canvas address (for example `school.instructure.com`), sign in on Canvas's page, and the interface takes over.

`scripts/dev/hybrid-test.mjs` checks the injection model without an iPhone: it injects the bundle into the mock Canvas in headless Chromium the way `ScriptBundle` does and exercises the bridge (storage and `onChanged`, the message bus, the smart panel's port, the host guard).

## Known limits

- **Single sign-on inside a web view.** Shibboleth, SAML and Microsoft logins work. Google sign-in refuses embedded web views by policy, so a school on Google Workspace cannot sign in this way.
- **Layout.** The interface is the desktop one until the phone layout lands; it renders, but it is built for a wide window.
- **Downloads** open in Safari rather than saving in the app; **the app icon badge** is not set (the extension's toolbar badge has no equivalent yet).
- **App Store review** will ask why the app displays a third-party service: it re-renders the student's own Canvas from Canvas's public API and needs a demo login (a Canvas Free-for-Teacher account works).
