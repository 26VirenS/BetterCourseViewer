# Motion and shape

How Simpl moves and what shape its panes are. Part 1 is the audit of every motion in the code as
it stands (2.95.0); part 2 is the plan that replaces it: springs for motion, continuous corners
(squircles) for shape. The rules at the end are the ones every path has to keep to.

## 1. The motion there is (2.95.0)

Tokens (`app.css` vars block): `--bcv-ease: cubic-bezier(.32,.72,0,1)`, `--bcv-t-fast .18s`,
`--bcv-t .28s`, `--bcv-t-slow .34s`. Mechanisms (`ui.js`): `enter` (staggered `.bcv-enter`
class), `still` (entrances off for a redraw), `dismiss` + `afterMotion` (a closing class, then the
element leaves when its animations end, capped at 450 ms), `morphFrom` (a sheet's transform origin
set to the control that opened it), `roll` (a number arrives in 16 steps), `anchor` /
`keepOnScreen` (placement, no motion of their own).

| What | Trigger · context | Now (file:line) | Spring preset |
|---|---|---|---|
| Screen root | a screen lands · arrive | `bcv-fade-up .34s` on `.bcv-main > .bcv-screen` (css:73) | gentle, direction-aware (forward rises, back settles down) |
| Blocks and rows | a screen's parts follow it · arrive | `.bcv-enter` fade-up 240 ms, delay i×55 capped 420 (css:78, ui.enter) | settle, staggered; unchanged under `still` |
| Sheet scrim | a sheet opens · arrive | `bcv-scrim .28s` opacity + blur (css:80) | scrim (opacity only) |
| Sheet | opens from a control · arrive | `bcv-morph .34s` scale .82→1 from the anchor (css:81, ui.morphFrom) | gentle, from the anchor, velocity kept on close |
| Sheet closing | Escape, X, scrim press · leave | `bcv-leave` + `bcv-leave-shrink .18s` (css:90–91, ui.dismiss) | gentle reversed, from the current position |
| Phone bottom sheet | opens/closes · arrive/leave | `bcv-sheet .26s` rise, `bcv-leave-down .22s` (css:82, 92) | phone (rise, settle), drag velocity on close |
| Menus, toasts, GPA pop | open · arrive | `bcv-pop .2s` scale .96→1 (css:83) | snappy from the anchor edge |
| Menus, quick nav closing | leave | `bcv-leave-pop .14s` (css:93) | snappy reversed |
| Reader overlay | arrive/leave | `bcv-fade-in .2s`, `bcv-leave .18s` (css:84, 94) | scrim |
| Tool popup (and the hub popover) | open from a card/pin · arrive; stacked over another | `bcv-morph`; closing `bcv-tool-ovout`/`bcv-tool-out .18s` (css:2955–2956, 3615) | gentle from the pin/card; the one under comes back on its own spring |
| Tray pins and islands | hover swells, leave folds · move/resize | width/height/opacity transitions on `--bcv-ease`, `bcv-pin-pop .5s` overshoot on a new pin (css:2447, 2957) | snappy (overshoot allowed: small and playful) |
| Quick nav | opens · arrive | `bcv-qn-in` (css:334) | snappy |
| Preview panel | slides in beside a list · arrive | `bcv-pv-in .34s` (css:1693) | gentle slide |
| Notifications rows | arrive | `bcvNfUp .36s` (css:2278) | settle, staggered |
| Rings and bars (grades, workload) | data arrives · arrive | `bcv-ring-fill .9s`, `bcv-grow .8s`, `bcv-line-draw 1.05s` (css:101–111) | kept as draws (data arriving), timing on the gentle curve |
| Loading bar, skeleton shimmer | waiting · loop | `bcv-load`, `bcv-shimmer` (css:125, 131) | unchanged (not a motion of the interface) |
| Press feedback | pointer down · press | transform transitions `.32,.72,0,1` and the `.34,1.3,.42,1` overshoot pair (css:96+) | snappy `linear()` easing from the same spring |
| Hover colour, opacity | hover · toggle | `background/opacity/color T ease` (137 transitions) | unchanged where colour-only; transform ones on the spring easing |
| Numbers | a count lands · arrive | `roll` 16 × 52 ms (ui.roll) | unchanged |
| Welcome stages, setup | guided demos | their own keyframes (css:269–273, 3013–3114) | unchanged (choreography), timing tokens shared |
| Phone push/pop | tab and screen changes | screen root fade-up only | phone: push slides in over a parallaxed under-screen, pop returns; interactive back hands its velocity on |

## 2. How it moves now (2.96.0)

### 2.1 Springs (`content/app/motion.js`, `BCV.motion`)

A damped spring — stiffness, damping, mass, an initial velocity — solved exactly (the under-,
critically- and over-damped cases), so every motion is physics and none is a bezier guess. The
same springs drive the stylesheet and the script:

- **In CSS.** At load, each preset is sampled into a `linear()` easing over its own settle time and
  written to the root as `--bcv-spring-<preset>` and `--bcv-t-<preset>`; every `animation` and
  `transition` in `app.css` reads those tokens, so a rule plays the spring with no script per
  element (the vars block holds bezier fallbacks for a browser without `linear()`).
- **In script.** `run(el, spec, preset, { from, to, v0 })` samples the spring into keyframes and
  plays them with `element.animate`; the handle's `now()` reads position and velocity mid-flight.
  `exit(el, kind)` reads how far an element's entrance has got (`progressOf`), cancels it and runs
  the exit spring from that point, so nothing snaps or restarts.
- Presets: **snappy** (pins, ticks, toggles, menus, toasts, presses: ~240 ms, a 5% overshoot),
  **gentle** (sheets, popups, screens, the preview panel: ~390 ms, no overshoot), **settle**
  (rows and blocks arriving, staggered: ~300 ms), **scrim** (opacity only: ~340 ms), **phone**
  (push and pop, bottom sheets: ~320 ms).
- Reduced motion: every path becomes a short opacity change, nothing left invisible.

### 2.2 Context

- Screens arrive the way you went (`app.render` marks the root `bcv-screen--fwd` or `--back`
  from the navigation kind): forward rises from below, back settles down from above; on the phone
  a pushed screen slides in from the right, a popped one from the left, a tab change only fades.
- Sheets and popups grow from the control that opened them (`morphFrom` sets the origin) and
  shrink back toward it; menus, pickers and the date popover scale from the edge they hang from
  (`anchor` sets `transform-origin` to the side it chose); toasts rise from the bottom — and now
  sit centred (the entrance used to override the centring transform); the tray's pins swell and
  fold on the snappy spring; the phone's bottom sheet follows the finger and, let go, springs back
  or away at the speed it was released (`openSheet` hands the drag velocity to the spring).

### 2.3 Interruptible

`ui.dismiss` is the one way out: it marks the element `is-closing bcv-sprung` (the stylesheet's
exit stands down) and runs `motion.exit` on the scrim and the sheet from wherever their entrance
had got to. A sheet closed at a third of the way in shrinks back from a third of the way in.
Tool popups, the hand-in popover, menus, the quick nav, the reader and toasts all leave this way;
a stacked popup's under-layer comes back only once the top one has truly left.

### 2.4 Squircles (`content/app/shape.js`, `BCV.shape`)

The continuous corner iOS draws (the superellipse's curve blended into the straight edge, so the
curvature builds and eases rather than jumping) on every pane and tile — cards, sheets, popups,
menus, toasts, the omni box and its panel, stat and course cards, tiles, the tray pins' faces, the
skeletons, the composers, the preview panel, the phone's cards and tab bar; capsules (buttons,
pills, badges) and discs stay what they are.

- Where the browser draws it (Chromium 139+): `corner-shape: superellipse(var(--bcv-sq-k))`
  beside each pane's `border-radius`; shadows and borders follow the curve natively.
- Elsewhere (Safari): `shape.js` watches the document, and for each pane writes the exact path
  for its size and its own per-corner radii into `--bcv-sq` (one ResizeObserver for all). A pane
  draws its shape on a `::before` under its content (`clip-path: var(--bcv-sq)`, the pane's own
  background and glass on that layer) and keeps its shadow on itself — under a blur, a squircle's
  edge and an arc's are the same shadow; a tile or a card's picture clips to the path directly.
  A capsule or a disc gets no smoothing (the curve needs room the shape has not got). Tests force
  this way on with `BCV.shape.force(true)`.
- One scale of corners in the vars block, by size — `--bcv-r-tile` 9 · `--bcv-r-tile-lg` 12 ·
  `--bcv-r-menu` 14 · `--bcv-r-panel` 16 · `--bcv-r-row` 18 · `--bcv-r-card` 20 · `--bcv-r-card-lg`
  22 · `--bcv-r-sheet` 26 — read by every pane and tile rule, so the shapes are one family.

### 2.5 The rules (nothing jarring)

1. Duration follows distance and size: a menu 160–220 ms, a sheet 300–420 ms, a screen 260–360 ms.
2. Overshoot only on small, playful things (pins, ticks, toggles, the star); never on a pane that
   carries text.
3. Scrims fade; they never move.
4. A parent and its child never animate the same property at once: the sheet morphs, then its
   rows settle.
5. Only `transform` and `opacity` move; a size change is measured first and then played as a
   transform.
6. A redraw is not an arrival (`still`): the same rows never vanish and fade back in.
7. Focus moves after the motion, not during it.
8. Reduced motion is honoured on every path, with nothing left invisible.
