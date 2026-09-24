# Widgets of your own

A widget is one HTML file. Simpl Courses lists it on the Tools page as a card beside the built-in tools, opens it in the same popup with the same head, lets you pin it beside the look switch, and the search box's `/tool` and `/pin` know it. The widget runs in a sandbox — it cannot see the Canvas page, your session, or the network — and talks to the interface through a small `window.simpl`.

Add one from **Tools → Add a widget**: paste the file, choose it, give its address, or start from one of the starters (a countdown, a notes pad, a unit converter, a word counter). The importer checks the file, says exactly what is wrong if anything is, shows the card and the widget running, and *Add to Tools* keeps it. From the widget's popup, the download button saves its file back (to change it and add it again) and the bin removes it.

## The file

```html
<!doctype html>
<meta name="simpl-widget" content='{"name":"Countdown","note":"Days to a date you pick.","icon":"clock","size":"S","color":"#ff375f","version":"1"}'>
<style>
  .big { font: 700 44px/1 var(--bcv-display); color: #ff375f; }
</style>
<input id="date" type="date">
<div class="big" id="big">—</div>
<script>
  var big = document.getElementById('big'), date = document.getElementById('date');
  function paint() { var d = new Date(date.value + 'T00:00:00'); big.textContent = isNaN(d) ? '—' : Math.ceil((d - Date.now()) / 864e5) + ' days'; }
  date.addEventListener('input', function () { simpl.storage.set('date', date.value); paint(); });
  simpl.ready(function () { simpl.storage.get('date', '').then(function (v) { date.value = v; paint(); }); });
</script>
```

**The header** is one `<meta name="simpl-widget">` whose `content` is a JSON object (single quotes round the attribute, double quotes inside):

| field | what it is |
| --- | --- |
| `name` | required; up to 40 characters. A widget added with the same name as one you have replaces it (its pin and its store stay). |
| `note` | the line under the name on its card; up to 80 characters |
| `icon` | one of the interface's icon names (`tool` when left out): `grid`, `dash`, `book`, `people`, `check`, `cal`, `mail`, `doc`, `bolt`, `disc`, `chart`, `folder`, `sheet`, `page`, `clock`, `bell`, `stream`, `sun`, `moon`, `shield`, `search`, `chevron`, `back`, `close`, `dots`, `compose`, `download`, `reader`, `sparkle`, `send`, `warn`, `star`, `link`, `modules`, `external`, `plus`, `reply`, `copy`, `stop`, `settings`, `image`, `video`, `audio`, `zip`, `calendarPlus`, `lock`, `filter`, `pencil`, `text`, `flag`, `task`, `tool`, `timer`, `graph`, `calc`, `table`, `convert`, `cards`, `quote`, `globe`, `journal`, `slides`, `upload`, `play`, `pause`, `pin`, `percent`, `merge`, `marker`, `scan` |
| `size` | `S` (400 × 300), `M` (560 × 420, the default) or `L` (760 × 560): the popup's width and the frame's starting height |
| `color` | a hex colour for the card's tile and the pin, like `#5856d6` (the default) |
| `version` | anything you like, shown to the widget in `simpl.theme.version` |

The rest of the file is ordinary HTML: any number of `<style>` and `<script>` blocks (inline — a `<script src>` or a linked stylesheet is refused, since the widget has no network) and the markup. The importer puts the styles first, then the markup, then the scripts, whatever order the file had them in, so a script can count on the markup being there. Up to 200 KB per widget, 2 MB for all of them together, 24 widgets at most.

## What a widget can do

The frame is `sandbox="allow-scripts"`: no same-origin, so the widget is nobody — no cookies, no storage of the page's, no way to the Canvas page or the window above it — and its own policy (`connect-src 'none'`, no frames, no forms sent anywhere) keeps it off the network. Pictures and fonts as `data:` URLs work. `alert()` does not (use `simpl.toast`).

`window.simpl` is there before the widget's scripts run:

| call | what it does |
| --- | --- |
| `simpl.ready(fn)` | `fn(theme)` once the interface has said hello (the theme is in; see below). Register anything that needs the theme or the store here. |
| `simpl.onTheme(fn)` | `fn(theme)` again whenever the look changes under the widget |
| `simpl.theme` | the last theme sent: `{ vars, dark, name, size, color, version }` |
| `simpl.storage.get(key, fallback)` | a promise of the value kept under `key` (this widget's own store; 64 KB in all) |
| `simpl.storage.set(key, value)` | keeps plain data (anything `JSON.stringify` takes); rejects when the store is full |
| `simpl.storage.remove(key)` | forgets it |
| `simpl.toast(text)` | the interface's toast |
| `simpl.resize(px)` | asks for the frame to be that tall (160 to 640) |
| `simpl.close()` | closes the popup |
| `simpl.copy(text)` | puts text on the clipboard |
| `simpl.open(url)` | opens an http(s) address in a new tab |

**The theme.** The interface's own variables are set on the widget's `:root` and updated when the look changes: `--bcv-bg`, `--bcv-card`, `--bcv-fill`, `--bcv-fill2`, `--bcv-hover`, `--bcv-sep`, `--bcv-edge`, `--bcv-ink`, `--bcv-ink2`, `--bcv-ink3`, `--bcv-blue`, `--bcv-font`, `--bcv-display`; `:root` carries `data-theme="light"` or `"dark"`. The body starts with the interface's font, ink and a 16px padding on a transparent background, so a widget that sets nothing already looks at home; buttons and fields inherit the font.

## Where it lives

Widgets are kept in the extension's storage (`widgets:custom`), each widget's store under `widgets:data:<id>`. Nothing is uploaded; a widget travels between devices only as its file. On the Mac and the iPhone the same files ship, so a widget added in Safari is there in the app after the next sync of the extension's storage — and a widget's file, saved from its popup, can be added anywhere.

The starters are the interface's own examples (`extension/content/app/tools/widget-starters.js`), written against this page; the importer's *Starters* pane runs any of them before it is added.
