# Safari: what is checked, and what is not

Simpl Courses is a Safari extension first (the Mac app, the iPhone app) and a Chrome extension
second. Every suite in `scripts/dev` runs in Chromium: Playwright loads extensions in Chromium alone,
and there is no WebKit on the machine the suites run on. So Safari is covered two ways, and both are
written down here so nobody mistakes a green run for a Safari check.

## 1. The floor

The Mac app supports macOS 13 (`MACOSX_DEPLOYMENT_TARGET` in the Xcode project, `minimumOS` in the
site's `latest.json`). Ventura's last Safari is 18; an unupdated one is 17. So **Safari 17.0 is the
floor**: a script API or a CSS feature Safari got later
than 17.0 needs a fallback, and one Safari has never had needs a guard.

## 2. The lint (`scripts/dev/safari-lint.mjs`)

Run over `extension/` by the `api` lane of the runner (and by hand: `node scripts/dev/safari-lint.mjs
[dir] [--floor 17.0]`). A table of script APIs and CSS features with the Safari version that first had
each (`null`: never). It fails on:

- a script API below the floor on a line with no guard (`typeof x`, `window.x ? … :`, `self.x ||`,
  `'x' in`, `?.`, a `catch`, a `supports(`);
- `backdrop-filter` without `-webkit-backdrop-filter` in the same block (Safari 9–17 read only the
  prefix);
- `linear()` easing with no bezier fallback on the line;
- a CSS feature a rule *needs* to work and Safari lacks (scroll-driven animation, anchor positioning,
  `@scope`, CSS nesting below 17.2, `position-area`).

It lists, without failing, the features Safari ignores harmlessly (`corner-shape`, `text-wrap: pretty`,
`content-visibility`, `scrollbar-width`…) so the reader knows what Safari will not show.

What the lint cannot see: behaviour. A property Safari parses can still render differently, and a
promise Safari never resolves is a page that never recovers. Those need the engine.

## 3. The engine (not here)

Playwright's WebKit build would let the content UI run in Safari's engine against the mock (the
extension API shimmed, as the iOS hybrid test shims the app's bridge). Its download host,
`playwright.download.prss.microsoft.com`, is not allowed by this environment's network policy; with
it allowed, `npx playwright install webkit` puts the browser under `/opt/pw-browsers` and a WebKit
lane can be added to the runner. Until then, every Safari-facing change is reviewed by hand against
§4 before it ships.

## 4. What broke in 2.96 and 2.97, and the rules that follow

2.96.0 (motion on springs, continuous corners) and 2.97.0 (the Away Refresh hold) passed every suite
and did not hold up in use. Both were reverted whole in 2.98.0. The lint finds nothing in either that
Safari cannot parse; the fault was behavioural, and two mechanisms stand out.

**The Safari-only corner path (`shape.js`, `html.bcv-sq-path`).** Where `corner-shape` is unknown —
every Safari — the stylesheet took the background, the border and the glass blur *off* every card,
sheet, menu and toast and drew them on a `::before` clipped to a computed path, with `position:
relative; isolation: isolate` on the pane, and a MutationObserver over the whole document found the
panes as they were drawn. Chromium never ran that way except in one forced check. In WebKit a
`backdrop-filter` inside a `clip-path` has long been unreliable (the blur unclipped, or not drawn at
all — a see-through sheet over the page), a new stacking context on every card traps anything
z-indexed inside it, a new containing block moves anything absolutely positioned within, and a
document-wide observer scans on every DOM change. Any one of these is a page that looks broken.

**The sprung exits (`ui.dismiss`).** An overlay's removal waited on `Promise.all` of Web Animations
`finished` promises with no time cap. Any animation that never finishes — cancelled by a style
change, an element detached early, an engine that treats a keyframe list differently — leaves the
scrim in place and the page under it unusable.

The rules for bringing the features back:

1. **No whole-interface repaint structure that only Safari runs.** A visual enhancement is
   progressive: `corner-shape` where the browser draws it, plain corners elsewhere. A fallback that
   changes how every pane is painted ships only once it has run in the engine it is for.
2. **Every scripted removal has a watchdog.** A promise that waits on motion is raced against a
   timer; the element is removed either way. CSS carries the exit; script only refines it.
3. **CSS first, script adds.** Spring easings are `linear()` tokens with bezier fallbacks in the vars
   block; nothing in the page depends on `BCV.motion` being present.
4. **No document-wide observers for decoration.** A pane that wants a computed shape asks for it
   when it is drawn; nothing watches the whole tree.
5. **The lint is green, the suites are green, and the change is read against §4 before it ships.**

## 5. After an update: Safari keeps the files it has

The Mac app's updater swaps the app on disk and relaunches itself; Safari is not told, and keeps
the extension's files it has until it is opened afresh — and can mix them: one version's script
with another version's stylesheet (2.98.10 was shipped for a calculator drawn by 2.98.7's script
under a stylesheet without that script's rules, its KaTeX never injected). Nothing in the page's
own logic can mend that, so the page tells:

- `lib/settings.js` stamps the scripts' version (`BCV_VERSION`); `content/styles/app.css` stamps the
  stylesheet's (`--bcv-version` on the theme root); both are bumped with the manifest on every
  release, and the smoke suite fails when one is left behind.
- `content/app/app.js` compares the two with the manifest's version a moment after the page loads.
  When they disagree it says, on every load until it is done: *Simpl Courses X is installed, but
  Safari is still running an older copy of it. Quit Safari (⌘Q) and open it again.*
- The app's settings window says the same under the update title on its first look after an
  update.

Rule: never assume a page runs one version. A file added in a release is not there for a Safari
that has not been reopened, and a style a new script relies on may be missing; the stamps make the
state visible instead of letting the interface half-work.
