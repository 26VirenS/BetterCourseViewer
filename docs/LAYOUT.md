# How the layout holds together

The rules every screen, sheet, menu and popup follows, so that a zoomed browser, a scaled display, a
slow machine or a late font never finds a special case. Read this before adding a floating element,
a breakpoint or a wait.

## Widths: two tiers, two numbers

A CSS pixel is whatever the browser's zoom and the OS's scaling make of it. A 1366px laptop at 200%
zoom is a 683px viewport; at 150% it is 911px; a 1080p screen at Windows' 175% scaling is 1097px.
So the tiers are met by zoom as much as by a small window, and the CSS has only two width numbers:

| tier | width | what changes |
|---|---|---|
| phone | ≤ 700px | `html.bcv-phone`: the iPhone layout (`content/app/phone.js`), a page loaded at this width — or reloaded into it |
| compact | ≤ 1100px | the sidebar narrows to 200px and the course rail to icons, split sheets and side panels take the whole width, two-column detail stacks, a Canvas-drawn page stacks its To Do column, the Dashboard's counters go two across |
| regular | > 1100px | the column grows with the window up to 2000px (`--w` on `.bcv-screen`) |

The same two numbers live in `content/early.js` as `BCV.layout` (`PHONE_MAX`, `COMPACT_MAX`, `tier()`).
The phone tier is decided once the document is parsed and kept for the page's life: the two shells
are different code, and a screen is never re-flowed into the other under the student. When a zoom
crosses the phone line while the interface is up, the page is loaded afresh into the other layout
once the zoom has settled — unless something on the page would be lost (a hand-in being written, a
quiz attempt: `app.holds()`). Everything inside a tier answers a zoom live, through the CSS.

Grids that must fit rather than break use `repeat(auto-fit, minmax(max(<min>, <share>), 1fr))` — the
Dashboard's counters are three across where three fit and two where they do not — rather than a
breakpoint.

## Floating things never take a size the window has not got

A sheet is `width: 520px; max-width: 100%` of an overlay with padding. A tool popup asks for its
own width through `--bcv-tool-w` (set by `tools.popup`) and the CSS keeps it to
`min(var(--bcv-tool-w), 100%)`; its tall variants are `min(their height, 100vh − margin)`. The toast
is `min(480px, 100vw − 32px)`. A menu, a picker list, a date popover, the quick nav panel and the
annotator's bubble are placed by `ui.anchor`, which puts them beside their element, flips them to the
other side when there is no room, clamps them to the viewport and gives a list a `maxHeight` so it
scrolls rather than runs off the edge.

## The mechanisms (content/app/ui.js)

Nothing waits a guessed number of milliseconds, measures once and hopes, or carries an observer of
its own. Use these:

- `afterMotion(el)` — resolves when the element's (and its children's) running animations and
  transitions end, read from the page; capped; at once under reduced motion. `dismiss(el)` is
  "add `is-closing`, then remove after `afterMotion`". Tool popups, pins, the setup's step change and
  What's New's exit use it.
- `onGone(el, fn)` — fn once, when the element has left the document, however it left. One observer
  serves every registration. Popups use it to drop their key and paste listeners; the hub's hand-in
  box to reset its state; a widget frame to detach its bridge.
- `watchLayout(el, fn)` — fn(rect) when the element is first measured and whenever its box may have
  moved: its own or the page's size, something around it (not inside it) drawn or restyled, an
  animation or transition ending, the fonts arriving, a resize or a zoom — then each frame until it
  has held still for three. The punch-through hole (Canvas's page laid into our screen), the course
  header's height, the focus island's strip and the welcome's look stage use it.
- `anchor(el, at, { side, gap, margin, align, minWidth })`, `keepOnScreen(el)`, `boundsOf(el)` — the
  placement above.

## The stacking ladder

`--bcv-z-*` in the vars block, named once: `chrome` (the phone's bars) 22, `menu` 30, `bar` 39,
`sheet` 40, `panel` 42, `picker` 45, `toast` 60, then above Canvas's own top layer `tray`, `float`
(the switch, the pins' drop, the bubble), `free` (a pin pulled out), `welcome`, `ghost`, and the two
top rungs a tool's own tab keeps for the bar and what floats under it (`under-top`, `top`).

## Testing it

`node scripts/dev/zoom-test.mjs` (also in the runner, `test-all.mjs`): four windows — a laptop at
200%, 150% and 67%, a 1080p screen at 175% — a browser context each, the main screens and the
overlays measured for anything sideways, past the edge, off screen, cut, overlapping or squeezed,
the tiers, the popups' clamping, and the live switch across the phone line.
