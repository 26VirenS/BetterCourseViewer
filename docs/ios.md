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
    Resources/Assets.xcassets     the app icon (rendered by scripts/make-icons.mjs)
```

- **The extension ships unchanged.** The `extension/` folder is a folder reference in the Xcode target, so the app bundle carries the same files the Safari and Chrome builds do. `ScriptBundle` reads `manifest.json` and injects each `content_scripts` entry at its `run_at`, into an isolated content world (like an extension's content scripts), main frame only, and only on the configured Canvas host. Single sign-on pages on other hosts are left untouched.
- **`browser.*` is a bridge.** `bridge.js` is injected first and provides `storage.local` (+ `onChanged`), `runtime.sendMessage`/`onMessage`, `runtime.connect`/`onConnect`, `getManifest`, `openOptionsPage`, and inert `action`/`permissions`/`tabs`. `background.js` runs inside the same page, so the smart panel's streaming port and the settings page's key test work exactly as in the extension. Storage lives in a native JSON file shared by every web view the app opens; a change in the settings sheet reaches the Canvas view through `storage.onChanged`.
- **There is no native chrome.** The web view fills the screen, under the status bar and the home indicator, and the interface draws its own bars: the phone layout's tab bar and back bar (below). `ScriptBundle` sets `viewport-fit=cover` on every Canvas page so the stylesheet can read the safe-area insets, and the web view's own inset adjustment is off so nothing is padded twice. Pull down to reload. The settings sheet (opened from the account sheet's Settings row, or by the smart panel's "add a key") shows the school, the version, and the extension's own `options.html` loaded from the bundle (its "Canvas sites" section is hidden: the school is chosen natively). JavaScript alerts and confirms (leaving a quiz, submitting) become native alerts; `window.open` to another host opens an in-app Safari sheet; downloads are handed to Safari.
- **The phone layout.** `extension/content/app/phone.js` and the phone block of `app.css` rebuild the screens for a narrow touch screen when the viewport is 700px or less (decided before first paint, so the same files serve a phone-sized browser window too): a five-item tab bar (Today, Courses, To Do, Grades, Calendar) and a back bar on every pushed screen, both true glass (a low-opacity tint over `blur(30px) saturate(1.9)`, a hairline, an inset specular, a solid fallback where blur is unavailable) that the content scrolls under; an edge-swipe from the left pops the stack like the Back button (not during a quiz). Large titles, grouped inset lists, screens that push in at .3s with rows staggered 70ms (capped at 280ms). **Swipe a row left** for its actions — Done / Priority on a To Do task, Read / Clear on a notification, Nickname on a course — up to 152px, latching past half, one row open at a time; every swipe action is also a tap target. Anything modal is a **bottom sheet with a grab handle**: drag it down past 110px to dismiss, or tap the scrim; the drag lives on the handle so a list inside still scrolls. A To Do row opens a task sheet (priority, mark done, open the assignment, delete a task of your own); a **grade ring expands on a tap** (one course at a time) into the category rings, a per-group breakdown and the target picker, and a what-if switch tries scores without saving anything; an assignment is handed in on its own page, the submit block at the end of the same scroll; a quiz attempt takes the whole screen. The bell on Today opens Notifications; Inbox, Groups, the appearance switch, Settings and Sign out live under the avatar. **Only the courses chosen in setup appear**, on every surface and in every count — the list is filtered before anything is counted (new enrollments are visible until the selection is changed in Settings). There is **no Immersive Reader on the phone**: iOS has Reader, Speak Screen and Dynamic Type.
- **What only the app can do** is exposed as `BCVBridge.native` (null in a browser): `signOut()` clears the Canvas session's cookies and site data and reloads to the login page. The appearance the interface chooses (Settings → Appearance) reaches the app through the settings write, so the status bar follows it; "system" follows the device.
- **The first launch opens the guided setup** (`/?bcv=setup` on the Canvas site, after Canvas's own sign-in), as a card over Today: the courses to favourite, the GPA goal, tracking and a target per course, the smart panel key, then straight into a tour of the phone layout. It can be run again from the account sheet under the avatar on Today. The settings page's own "Guided setup" section is hidden in the app, like "Canvas sites".
- **The session survives a relaunch.** WebKit keeps cookies without an expiry date (Canvas's session cookie is one) in memory only, so an app that was closed came back at the login page. `CookieJar` mirrors the web view's cookies into the Keychain whenever they change and when the app leaves the foreground, and puts them back before the first load. On Canvas's own login form "Stay signed in" starts checked (it can be unticked). Sign out empties the jar.
- **Canvas's bundles load only where Canvas draws the page.** `ContentRules` compiles two content-rule lists: telemetry (New Relic, Pendo, analytics), blocked everywhere; and Canvas's script and stylesheet bundles, blocked only on pages the interface draws itself (`RenderedRoutes`, a port of `parseRoute` and the course and group tab switches). Those pages no longer download, parse and run Canvas's front end to build a page that stays hidden, which is most of the time a tap used to take. Pages handed back to Canvas (a quiz being taken, a file preview, an external tool, `?bcv=native`, everything when the interface is off) and frames (tools) are untouched; switching the interface on or off reloads the page so the right rules apply.

## Build and run

1. Xcode 15 or later, and Homebrew (for XcodeGen; the script installs it).
2. Generate and open the project:
   ```bash
   ./scripts/build-ios-app.sh --open
   ```
3. In Xcode select the **SimplCourses** target → Signing & Capabilities → choose your Team (a personal Apple ID team is enough to run on your own iPhone).
4. Pick your iPhone or a simulator and press ⌘R.
5. In the app enter your school's Canvas address (for example `school.instructure.com`), sign in on Canvas's page, and the interface takes over.

`scripts/dev/hybrid-test.mjs` checks the injection model without an iPhone: it injects the bundle into the mock Canvas in headless Chromium the way `ScriptBundle` does and exercises the bridge (storage and `onChanged`, the message bus, the smart panel's port, the host guard). `scripts/dev/phone-test.mjs` walks the phone layout at an iPhone viewport (the five tabs, the sheets, a course, an item, submit, a quiz, the smart panel) and saves screenshots to `scripts/dev/out/phone-*.png`.

## Known limits

- **Single sign-on inside a web view.** Shibboleth, SAML and Microsoft logins work. Google sign-in refuses embedded web views by policy, so a school on Google Workspace cannot sign in this way.
- **Canvas-drawn pages** (a page the interface hands back to Canvas, an external tool) are Canvas's own responsive layout under the interface's bars.
- **Downloads** open in Safari rather than saving in the app; **the app icon badge** is not set (the extension's toolbar badge has no equivalent yet).
- **App Store review** will ask why the app displays a third-party service: it re-renders the student's own Canvas from Canvas's public API and needs a demo login (a Canvas Free-for-Teacher account works).
