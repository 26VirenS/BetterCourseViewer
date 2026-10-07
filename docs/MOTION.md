# Motion

How Simpl moves, since 2.98.2: on springs, from CSS first, with every scripted exit under a watchdog.
The corners are a separate step (see the end). The rules in §4 are the ones every path keeps to; the
reading of what went wrong in 2.96 is in `docs/SAFARI.md` §4.

## 1. The springs (`content/app/motion.js`, `BCV.motion`)

A damped spring — stiffness, damping, mass, an initial velocity — solved exactly (the under-,
critically- and over-damped cases), so a motion is physics and not an eased guess. The presets:

| preset | for | settle | overshoot |
|---|---|---|---|
| snappy | pins, ticks, toggles, menus, toasts, presses | ~290 ms | ~3.5% (small and playful) |
| gentle | sheets, popups, screens, the preview panel | ~460 ms | none visible |
| settle | rows and blocks arriving, staggered | ~350 ms | none |
| scrim | opacity only | ~400 ms | none |
| phone | push and pop, the bottom sheet | ~390 ms | none |
| island | the look switch growing under the pointer into its list | ~490 ms | none |
| pin | the widgets: the timer's island and every pin's panel swelling under the pointer (2.98.102) | ~370 ms | none |

(2.98.4 slowed each preset by about a fifth and softened the turn: smoother over speed. The
curves are sampled every ~5 ms for CSS — up to 192 stops — and every 4 ms for script keyframes,
finer than a frame at 120 Hz, so what the display draws is the spring itself and never a straight
line between two of its points.)

The engine has no view of the page beyond two things it is asked for:

- **Tokens.** At load, `installTokens` samples each preset into a CSS `linear()` easing over its own
  settle time and writes `--bcv-spring-<preset>` and `--bcv-t-<preset>` on the root, adding
  `html.bcv-springs`. It does so only where `CSS.supports('animation-timing-function', 'linear(0, 1)')`
  (Safari 17.2+, Chromium 113+); elsewhere the vars block's `cubic-bezier` fallbacks and durations
  stand, and nothing else changes. Every `animation` and `transition` in `app.css` reads the tokens.
- **Runs.** `run(el, spec, preset, { from, to, v0, fill })` samples a spring into keyframes for
  `element.animate`; `exit(el, kind)` reads what the element shows this instant and how far its
  entrance has got, cancels it and runs the exit spring from there. Both are used only by the two
  paths below, and both are wrapped so a throw falls back to the stylesheet.

## 2. Where the script moves things

- **`ui.dismiss(el)`** is the one way out for sheets, tool popups, the hand-in popover, menus, the
  quick nav, the reader and toasts. With the springs it marks the element `is-closing bcv-sprung`
  (the stylesheet's exit stands down) and runs the exit springs — the scrim fades, the sheet inside
  it shrinks (or slides down on the phone) from wherever its entrance had got to. Without them, or
  under reduced motion, `is-closing` starts the stylesheet's exit and `afterMotion` sees it end.
  **Either way the element is removed by 700 ms**: the wait is raced against a timer. A spring that
  throws hands over to the stylesheet's exit.
- **The phone's bottom sheet** follows the finger; let go past the line or thrown, it goes on down at
  the speed it was moving and the scrim fades, the removal raced against the same watchdog; let go
  short, it springs back with the finger's speed handed on. Every branch has a plain fallback.

## 3. Context, in CSS

- Screens arrive the way you went: `app.render` marks the root `bcv-screen--fwd` or `--back` from
  the navigation kind — forward rises from below, back settles down from above; on the phone a
  pushed screen slides in from the right, a popped one from the left, a tab change only fades.
- Menus and pickers scale from the edge they hang from: `ui.anchor` sets `transform-origin` to the
  side it chose; the account and appearance menus grow up out of their row.
- Toasts rise from the bottom on the snappy spring and sit centred (`translate: -50% 0` keeps the
  centring under the entrance's transform).
- Hover lifts and presses transition on the snappy spring. The timer's island and every pin's
  panel swell on the pin spring — nine tenths of the way there by 200 ms, the same for all of them —
  with their contents fading up from the start of the swell. What is in them is laid out once at
  the open size and only clipped as the box grows, never laid out again on each frame. Their big shadow is a layer of its own
  under the pin, hidden until the box stands still (a shadow redrawn on every frame of a resize is
  what dropped frames), then faded in.
- Two entrances 2.96 had written with two easings in one shorthand (the quick nav, the search panel)
  — invalid, so they never played — now play.
- Hover lifts are for a mouse only (`@media (hover: hover) and (pointer: fine)`): a tap on a phone
  leaves nothing lifted. Presses scale to 0.97 (0.98 on a large card) on `:active`.

## 3a. The curves and what 2.99.4 moved off layout

Three curves, as tokens in the vars block, and nothing typed out by hand anywhere else:

| token | curve | for |
|---|---|---|
| `--bcv-ease` | `cubic-bezier(.32,.72,0,1)` | the house curve (iOS-like drawer) |
| `--bcv-ease-out` | `cubic-bezier(.23,1,.32,1)` | arrivals, exits, presses, the ring's sweep back, the Dashboard's bars |
| `--bcv-ease-in-out` | `cubic-bezier(.77,0,.175,1)` | a thing moving across the screen (the segmented thumb) |

A loading fill is `linear`. Nothing starts from `scale(0)`: pop-ins start at 0.9, menus at 0.96.

- **The counter box and the mark box** (`ui.cardBox`) open by `clip-path` from the card's own
  rectangle, laid out once at their open size. The dim is one gradient drawn once and moved by
  `translate`/`scale`; the big shadow is a layer of its own (a clip would cut it). A press while one
  folds reverses the fold from where it is.
- **Pins and the timer island** swell by `clip-path` too; their neighbours slide by `translate`.
- **The search float** moves its pill by `transform` (the words scale with it) and the results panel
  grows by `clip-path`, its shade a layer of its own scaled on Y.
- **The To Do list** (`ui.flipList`): a row ticked off or dismissed leaves, and the rows below slide
  up into the gap over 200 ms, by `translate` alone.
- **A hand-in or a finished quiz** (`ui.drawCheck`): the tick draws itself in over 400 ms, once per
  attempt — a rare moment, allowed it.
- The Dashboard's numbers roll and its bars grow on the first visit of a session only (≤ 300 ms);
  a toast already up takes the new words instead of being replaced; phone swipes decide by speed
  (over ~0.11 px/ms) and pull with rising resistance past their ends.

## 3b. What 2.99.7 added, and what it took away

From a sweep for places that should move and do not — and the ones that moved too much:

- **Less.** A redraw no longer replays an entrance: Notifications (desktop and phone) draws its
  entrance once, and every draw after a dismiss, Mark read or a chip is the same list changed in
  place, the rows that stay sliding into the gap (`ui.flipList`). Flashcards' parts rise when a view
  is arrived at, never on Next, Previous, a star or a key. Search opened from the keyboard (`/`, ⌘K)
  is simply there in the middle, and Escape puts it back at once (`keyed` in search.js: the glide
  stays for a press on the box). (The highlight still glides between rows as the arrows move: 2.98.98's
  one highlight is a deliberate choice, kept.) The settings page's courses and sites do not fade up
  again after a save.
- **More, small.** The in-app switch's knob slides (`translate` by `--bcv-sw-travel`, it was moved by
  `justify-content`, which cannot animate); the small controls give under the finger by the `scale`
  property (.96, .99 for wide ones), so no transform of theirs is touched; the module and idle
  chevrons turn with what they open; a To Do tick pops in where it is painted in place, never on a
  redraw; the dropdown list grows from its button (`bcv-pop` from `ui.anchor`'s origin) and the
  course colour menu from its dot; a toast has a stylesheet exit (it had none without the springs
  and cut out under reduced motion); modules re-sorted slide to their places; a conversation read
  fades in; the preview panel put away by a press slides back out, and another row pressed changes
  only its words. The settings page's switches slide as they are pressed, its Saved pill fades out
  and nods at a second save; the setup's stepper nods rather than popping from nothing.

## 4. The rules

1. **CSS first, script adds.** Nothing on the page depends on `BCV.motion` being present.
2. **Every scripted removal has a watchdog.** A promise that waits on motion is raced against a
   timer; the element leaves either way.
3. Only `transform` and `opacity` move; a size change is measured first and played as a transform.
4. Duration follows distance and size: a menu ~290 ms, a sheet or a screen ~460 ms, a row ~350 ms.
5. Overshoot only on small, playful things; never on a pane that carries text.
6. Scrims fade; they never move. A parent and its child never animate the same property at once.
7. A redraw is not an arrival (`ui.still`). Focus moves after the motion. Reduced motion is honoured
   on every path, with nothing left invisible: movement goes, a 150 ms fade stays.
8. No document-wide observers for decoration.
9. Curves come from the tokens (§3a). A script animation, which cannot read `var()`, reads the token's
   value from the root.

## 5. The corners

Continuous corners (`corner-shape: superellipse()`) are a later step, and only where the browser
draws them natively; the Safari-only path layer of 2.96 does not come back (`docs/SAFARI.md` §4).
The radius scale — `--bcv-r-tile` 9 · `tile-lg` 12 · `menu` 14 · `panel` 16 · `row` 18 · `card` 20 ·
`card-lg` 22 · `sheet` 26 — is in the vars block already, read by every pane, so the shapes are one
family whatever draws them.
