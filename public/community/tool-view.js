// Simpl Community tools on the website: a tool drawn and working, as the apps draw it — its tile (the symbol on its
// colour), its inputs (numbers, choices, switches), its results worked out by the reference itself
// (/community/tool-format.js, the same file the site's Worker checks tools with) as anything is changed, the first
// one shown largest, then its table and its note. The gallery's cards, the maker's preview and the review all use it.
//
//   drawTool(tool, { values, onChange, head, table })  a checked tool (validateTool's) drawn: an element
//   toolTile(tool)  the tile alone           symbolIcon(name)  a symbol as an SVG
//   readTool(raw)   a tool as fetched, checked here again (leniently, as an app does): the tool, or null
//
// Every word a tool carries is set as text, never as markup; its colour is #rrggbb once checked.
import { FUNCTIONS, CONSTANTS, SYMBOLS, runTool, validateTool } from './tool-format.js';

export const GROUP_NAMES = { study: 'Study', math: 'Math', science: 'Science', money: 'Money', everyday: 'Everyday' };

// ---- the symbols: one drawing each, on a 24 × 24 grid, in the current colour --------------------------------------
const P = (d, fill) => ['path', { d, ...(fill ? { fill: 'currentColor', stroke: 'none' } : {}) }];
const C = (cx, cy, r, fill) => ['circle', { cx, cy, r, ...(fill ? { fill: 'currentColor', stroke: 'none' } : {}) }];
const DRAWINGS = {
  bolt: [P('M13.2 2.5 4.6 13.6h6.6l-1 7.9 8.6-11.1h-6.6z', true)],
  percent: [P('M18.5 5.5 5.5 18.5'), C(7, 7, 2.5), C(17, 17, 2.5)],
  function: [P('M15.5 4.5h-1.2a2.8 2.8 0 0 0-2.8 2.8v9.4a2.8 2.8 0 0 1-2.8 2.8H7.5'), P('M8 10.5h7')],
  sum: [P('M17.5 5h-11l6 7-6 7h11')],
  divide: [P('M5 12h14'), C(12, 6.5, 1.6, true), C(12, 17.5, 1.6, true)],
  ruler: [P('M3.5 16 16 3.5 20.5 8 8 20.5z'), P('M7.2 12.3l1.8 1.8M9.9 9.6l2.6 2.6M12.6 6.9l1.8 1.8')],
  scale: [P('M12 4v15M8 20h8M5 7h14'), P('M5 7 2.5 13a2.6 2.6 0 0 0 5 0zM19 7l-2.5 6a2.6 2.6 0 0 0 5 0z')],
  thermometer: [P('M14 14.6V5a2 2 0 0 0-4 0v9.6a4 4 0 1 0 4 0z'), P('M12 15.5V9')],
  clock: [C(12, 12, 9), P('M12 7v5l3.2 2')],
  calendar: [['rect', { x: 3.5, y: 5, width: 17, height: 15.5, rx: 2.5 }], P('M3.5 10h17M8 3v4M16 3v4')],
  money: [C(12, 12, 9), P('M12 6.5v11M14.8 9.2c-.4-.9-1.5-1.5-2.8-1.5-1.6 0-2.8.9-2.8 2.1 0 2.9 5.6 1.5 5.6 4.3 0 1.2-1.2 2.1-2.8 2.1-1.3 0-2.4-.6-2.8-1.5')],
  chart: [P('M6 20v-6M12 20V5M18 20v-10M3.5 20h17')],
  atom: [['ellipse', { cx: 12, cy: 12, rx: 9.5, ry: 3.8 }], ['ellipse', { cx: 12, cy: 12, rx: 9.5, ry: 3.8, transform: 'rotate(60 12 12)' }], ['ellipse', { cx: 12, cy: 12, rx: 9.5, ry: 3.8, transform: 'rotate(120 12 12)' }], C(12, 12, 1.6, true)],
  flask: [P('M9 3h6M10 3v6.5L4.6 18.8A1.5 1.5 0 0 0 5.9 21h12.2a1.5 1.5 0 0 0 1.3-2.2L14 9.5V3'), P('M7.2 15h9.6')],
  leaf: [P('M11 20A7 7 0 0 1 9.8 6.1C15.5 5 17 4.5 19 2c1 2 2 4.2 2 8 0 5.5-4.8 10-10 10z'), P('M2.5 21.5c0-3 1.9-5.4 5.1-6.1')],
  heart: [P('M19.8 5.6a5 5 0 0 0-7.1 0l-.7.7-.7-.7a5 5 0 0 0-7.1 7.1l.7.7L12 20.5l7.1-7.1.7-.7a5 5 0 0 0 0-7.1z')],
  book: [P('M2.5 4.5h5.5A4 4 0 0 1 12 8.5v12a3 3 0 0 0-3-3H2.5zM21.5 4.5H16a4 4 0 0 0-4 4v12a3 3 0 0 1 3-3h6.5z')],
  pencil: [P('M16.5 3.5a2.1 2.1 0 0 1 3 3L7.5 18.5 3 20l1.5-4.5z'), P('M14.5 5.5l3 3')],
  globe: [C(12, 12, 9), P('M3 12h18M12 3a13.8 13.8 0 0 1 3.6 9A13.8 13.8 0 0 1 12 21a13.8 13.8 0 0 1-3.6-9A13.8 13.8 0 0 1 12 3z')],
  car: [P('M5 16.5H3.8a.8.8 0 0 1-.8-.8V12l2.2-5.2A1.5 1.5 0 0 1 6.6 6h10.8a1.5 1.5 0 0 1 1.4.8L21 12v3.7a.8.8 0 0 1-.8.8H19M9.5 16.5h5M3 12h18'), C(7.3, 16.8, 2), C(16.7, 16.8, 2)],
  drop: [P('M12 3s-6.5 7-6.5 11.5a6.5 6.5 0 0 0 13 0C18.5 10 12 3 12 3z')],
  flame: [P('M8.5 14.5A2.5 2.5 0 0 0 11 12c0-1.4-.5-2-1-3-1.1-2.1-.2-4 2-6 .5 2.5 2 4.9 4 6.5 2 1.6 3 3.5 3 5.5a7 7 0 1 1-14 0c0-1.2.4-2.3 1-3a2.5 2.5 0 0 0 2.5 2.5z')],
  gauge: [P('M3.3 18.5a10 10 0 1 1 17.4 0'), P('M12 14.5l4-4.5'), C(12, 14.5, 1.4, true)],
  star: [P('M12 2.8l2.8 5.8 6.4.9-4.6 4.5 1.1 6.3L12 17.3l-5.7 3 1.1-6.3-4.6-4.5 6.4-.9z')],
  lightbulb: [P('M15 14c.2-1 .7-1.7 1.5-2.5A5.9 5.9 0 0 0 18 8 6 6 0 0 0 6 8c0 1 .2 2.2 1.5 3.5.7.7 1.3 1.5 1.5 2.5M9 18h6M10 21.5h4')],
  music: [P('M9 18V5.5l11-2v12.5'), C(6.5, 18, 2.5), C(17.5, 16, 2.5)],
  dice: [['rect', { x: 3.5, y: 3.5, width: 17, height: 17, rx: 3.5 }], C(8.3, 8.3, 1.4, true), C(15.7, 8.3, 1.4, true), C(12, 12, 1.4, true), C(8.3, 15.7, 1.4, true), C(15.7, 15.7, 1.4, true)],
  triangle: [P('M12 3.8 21.2 19.5H2.8z'), P('M7.5 19.5v-2.5M12 19.5V16M16.5 19.5v-2.5')],
};
const SVG = 'http://www.w3.org/2000/svg';
/** A symbol as an SVG drawing in the current colour (an unknown name: an empty square's worth of space). */
export function symbolIcon(name) {
  const svg = document.createElementNS(SVG, 'svg');
  for (const [k, v] of Object.entries({ viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor', 'stroke-width': '1.9', 'stroke-linecap': 'round', 'stroke-linejoin': 'round', 'aria-hidden': 'true', focusable: 'false' })) svg.setAttribute(k, v);
  for (const [tag, attrs] of DRAWINGS[name] || []) {
    const e = document.createElementNS(SVG, tag);
    for (const [k, v] of Object.entries(attrs)) e.setAttribute(k, String(v));
    svg.append(e);
  }
  return svg;
}
export { SYMBOLS };

// ---- small helpers ------------------------------------------------------------------------------------------------
const el = (tag, cls, attrs = {}, kids = []) => {
  const e = document.createElement(tag);
  if (cls) e.className = cls;
  for (const [k, v] of Object.entries(attrs)) if (v != null && v !== false) e.setAttribute(k, v === true ? '' : String(v));
  for (const k of [].concat(kids)) if (k != null) e.append(k);
  return e;
};
/** A colour light enough that its tile's symbol is drawn dark. */
const isLight = (hex) => {
  const n = parseInt(String(hex).slice(1), 16);
  if (!Number.isFinite(n)) return false;
  const lin = (c) => { const x = c / 255; return x <= 0.04045 ? x / 12.92 : ((x + 0.055) / 1.055) ** 2.4; };
  return 0.2126 * lin((n >> 16) & 255) + 0.7152 * lin((n >> 8) & 255) + 0.0722 * lin(n & 255) > 0.55;
};

/** The tile: the tool's symbol on its colour, as the apps draw a tool's tile. */
export function toolTile(tool, cls = '') {
  const t = el('span', `tv-tile${cls ? ` ${cls}` : ''}`, { 'aria-hidden': 'true' }, [symbolIcon(tool.symbol)]);
  t.style.background = tool.color;
  if (isLight(tool.color)) t.classList.add('is-light');
  return t;
}

/** A tool as fetched, checked here again as an app would (leniently): the checked tool, or null. */
export function readTool(raw) {
  try { return validateTool(raw).tool || null; } catch { return null; }
}

/** The names a formula can never be given (pi, e, true, false and the functions): for the maker's suggestions. */
export const RESERVED = new Set([...Object.keys(CONSTANTS), ...Object.keys(FUNCTIONS)]);

// ---- the tool, drawn and working ---------------------------------------------------------------------------------
let seq = 0;
/** A number input's text as the reference takes it: nothing typed is "not given" (its default). */
const givenOf = (input, v) => {
  if (input.kind === 'number') return v == null || String(v).trim() === '' ? undefined : Number(v);
  if (input.kind === 'toggle') return v == null ? undefined : v === true;
  return v;
};
const showText = (n) => (Number.isInteger(n) || Math.abs(n) >= 1e-6 ? String(n) : n.toExponential());

/**
 * A checked tool, drawn and working: its head (the tile, the name, the summary), its inputs, its results worked out
 * by the reference as anything changes, its table and its note. `values` are the inputs' values to start from (by id:
 * a number's text, a choice's value, a toggle's true or false); `onChange(values, results)` hears each change;
 * `table`: 'open' or 'closed' (a <details>); `head`: false leaves the head out.
 */
export function drawTool(tool, { values = {}, onChange = null, head = true, table = 'closed' } = {}) {
  const uid = `tv${++seq}`;
  const state = { ...values };
  const root = el('div', 'tv', { 'data-tool-view': tool.id || '' });
  root.style.setProperty('--tv-c', tool.color);
  if (head) {
    const words = el('div', 'tv-head__words', {}, [el('b', 'tv-name', {}, tool.name), el('span', 'tv-summary', {}, tool.summary)]);
    root.append(el('div', 'tv-head', {}, [toolTile(tool), words]));
  }

  const inputs = el('div', 'tv-inputs');
  for (const input of tool.inputs) inputs.append(field(input));
  const results = el('div', 'tv-results', { 'aria-live': 'polite', 'data-results': '' });
  root.append(inputs, results);

  if (tool.table) {
    const t = tool.table;
    const grid = el('table', 'tv-grid', {}, [
      el('thead', null, {}, el('tr', null, {}, t.columns.map((c) => el('th', null, { scope: 'col' }, c)))),
      el('tbody', null, {}, t.rows.map((r) => el('tr', null, {}, r.map((c) => el('td', null, {}, c))))),
    ]);
    const box = el('details', 'tv-table', { open: table === 'open' }, [el('summary', null, {}, t.title || 'Table'), el('div', 'tv-table__scroll', {}, grid)]);
    root.append(box);
  }
  if (tool.note) root.append(el('p', 'tv-note', {}, tool.note));

  function field(input) {
    const id = `${uid}-${input.id}`;
    if (input.kind === 'toggle') {
      const box = el('input', null, { type: 'checkbox', role: 'switch', id, 'data-input': input.id });
      box.checked = state[input.id] == null ? input.default : state[input.id] === true;
      box.addEventListener('change', () => { state[input.id] = box.checked; recalc(); });
      return el('label', 'tv-field tv-toggle', { for: id }, [box, el('span', 'tv-switch', { 'aria-hidden': 'true' }), el('span', 'tv-label', {}, input.label)]);
    }
    if (input.kind === 'choice') {
      const now = input.options.some((o) => o.value === state[input.id]) ? state[input.id] : input.default;
      const short = input.options.length <= 3 && input.options.reduce((n, o) => n + o.label.length, 0) <= 24;
      if (short) {
        const seg = el('div', 'tv-seg', { role: 'radiogroup', 'aria-labelledby': `${id}-l`, 'data-input': input.id });
        const pick = (v) => { for (const b of seg.children) b.setAttribute('aria-checked', String(Number(b.dataset.v) === v)); };
        input.options.forEach((o, i) => {
          const b = el('button', null, { type: 'button', role: 'radio', 'aria-checked': String(o.value === now), 'data-v': String(o.value), 'data-i': String(i) }, o.label);
          b.addEventListener('click', () => { state[input.id] = o.value; pick(o.value); recalc(); });
          seg.append(b);
        });
        // (arrows move the choice, as a radio group's do)
        seg.addEventListener('keydown', (e) => {
          const step = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 }[e.key];
          if (!step) return;
          e.preventDefault();
          const at = input.options.findIndex((o) => o.value === (state[input.id] ?? input.default));
          const next = input.options[(at + step + input.options.length) % input.options.length];
          state[input.id] = next.value; pick(next.value); recalc();
          seg.querySelector(`[data-v="${next.value}"]`)?.focus();
        });
        return el('div', 'tv-field tv-field--seg', {}, [el('span', 'tv-label', { id: `${id}-l` }, input.label), seg]);
      }
      const sel = el('select', 'tv-select', { id, 'data-input': input.id }, input.options.map((o, i) => el('option', null, { value: String(i) }, o.label)));
      sel.selectedIndex = Math.max(0, input.options.findIndex((o) => o.value === now));
      sel.addEventListener('change', () => { state[input.id] = input.options[sel.selectedIndex].value; recalc(); });
      return el('label', 'tv-field', { for: id }, [el('span', 'tv-label', {}, input.label), el('span', 'tv-box tv-box--select', {}, sel)]);
    }
    const box = el('input', null, { type: 'number', inputmode: 'decimal', id, step: String(input.step), min: input.min ?? null, max: input.max ?? null, 'data-input': input.id, autocomplete: 'off' });
    box.value = state[input.id] != null ? String(state[input.id]) : showText(input.default);
    box.addEventListener('input', () => { state[input.id] = box.value; recalc(); });
    const kids = [box];
    if (input.unit) kids.push(el('i', 'tv-unit', {}, input.unit));
    return el('label', 'tv-field', { for: id }, [el('span', 'tv-label', {}, input.label), el('span', 'tv-box', {}, kids)]);
  }

  function recalc() {
    const given = {};
    for (const input of tool.inputs) given[input.id] = givenOf(input, state[input.id]);
    let out = null;
    try { out = runTool(tool, given); } catch { /* a tool not checked: nothing to show */ }
    const shown = (out || []).filter((r) => r.shown);
    const value = (r, cls) => el('b', cls, {}, [r.text, r.unit && r.text !== '—' ? el('i', 'tv-unit', {}, ` ${r.unit}`) : null]);
    results.replaceChildren(...(shown.length
      ? shown.map((r, i) => (i === 0
        ? el('div', 'tv-hero', { 'data-result': r.label }, [el('small', null, {}, r.label), value(r, 'tv-value')])
        : el('div', 'tv-row', { 'data-result': r.label }, [el('span', null, {}, r.label), value(r, 'tv-value')])))
      : [el('p', 'tv-none', {}, 'Nothing to show for these values.')]));
    if (onChange) onChange({ ...state }, out);
  }
  recalc();
  return root;
}

// ---- how it looks: the page's own colours where it has them (--bg, --tile, --text…), light and dark --------------
const css = `
.tv { --tv-c: #0a84ff; --tv-font: var(--font, -apple-system, BlinkMacSystemFont, "SF Pro Text", "Segoe UI", system-ui, sans-serif); --tv-font-display: var(--font-display, var(--tv-font)); display: grid; gap: 14px; min-width: 0; color: var(--text, #1d1d1f); text-align: left; }
.tv-head { display: flex; align-items: center; gap: 12px; min-width: 0; }
.tv-head__words { min-width: 0; }
.tv-tile { flex: none; display: inline-grid; place-items: center; width: 44px; height: 44px; border-radius: 11px; color: #fff; box-shadow: inset 0 0 0 .5px rgba(0, 0, 0, .1); }
.tv-tile.is-light { color: #1d1d1f; }
.tv-tile svg { width: 60%; height: 60%; }
.tv-name { display: block; font: 600 17px/1.25 var(--tv-font-display); letter-spacing: -.015em; overflow-wrap: anywhere; }
.tv-summary { display: block; margin-top: 1px; font-size: 14px; line-height: 1.35; color: var(--text-2, #6e6e73); overflow-wrap: anywhere; }
.tv-inputs { display: grid; grid-template-columns: repeat(auto-fill, minmax(min(100%, 128px), 1fr)); gap: 10px 12px; }
.tv-field { display: grid; align-content: start; gap: 5px; min-width: 0; }
.tv-label { font-size: 12.5px; font-weight: 500; line-height: 1.3; color: var(--text-2, #6e6e73); overflow-wrap: anywhere; }
.tv-box { display: flex; align-items: center; gap: 6px; height: 38px; padding: 0 11px; border-radius: 10px; background: var(--bg, #fff); box-shadow: inset 0 0 0 1px var(--hair, rgba(0, 0, 0, .1)); transition: box-shadow 140ms ease; }
.tv-box:focus-within { box-shadow: inset 0 0 0 2px var(--tv-c); }
.tv-box:has(input:out-of-range) { box-shadow: inset 0 0 0 2px #ff9f0a; }
.tv-box input, .tv-select { flex: 1; min-width: 0; width: 100%; height: 100%; padding: 0; border: 0; background: none; color: inherit; font: 500 15px/1 var(--tv-font); font-variant-numeric: tabular-nums; outline: none; }
.tv-box input::-webkit-inner-spin-button { opacity: .5; }
.tv-box--select { padding: 0 6px 0 9px; }
.tv-select { cursor: pointer; }
.tv-unit { flex: none; font-style: normal; font-weight: 500; font-size: 13px; color: var(--text-3, #86868b); }
.tv-field--seg { grid-column: 1 / -1; }
.tv-seg { display: grid; grid-auto-flow: column; grid-auto-columns: 1fr; gap: 2px; height: 38px; padding: 3px; border-radius: 10px; background: var(--soft, #e8e8ed); }
.tv-seg button { min-width: 0; padding: 0 6px; border: 0; border-radius: 7px; background: none; color: var(--text, #1d1d1f); font: 500 13px/1 var(--tv-font); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; cursor: pointer; -webkit-tap-highlight-color: transparent; }
.tv-seg button[aria-checked="true"] { background: var(--bg, #fff); box-shadow: 0 1px 3px rgba(0, 0, 0, .14); font-weight: 600; }
.tv-toggle { grid-column: 1 / -1; display: flex; align-items: center; gap: 10px; cursor: pointer; }
.tv-toggle input { position: absolute; opacity: 0; width: 1px; height: 1px; }
.tv-switch { position: relative; flex: none; width: 42px; height: 26px; border-radius: 999px; background: var(--soft, #e8e8ed); box-shadow: inset 0 0 0 1px var(--hair, rgba(0, 0, 0, .08)); transition: background-color 160ms ease; }
.tv-switch::after { content: ""; position: absolute; top: 3px; left: 3px; width: 20px; height: 20px; border-radius: 50%; background: #fff; box-shadow: 0 1px 3px rgba(0, 0, 0, .25); transition: transform 180ms cubic-bezier(.23, 1, .32, 1); }
.tv-toggle input:checked + .tv-switch { background: var(--tv-c); }
.tv-toggle input:checked + .tv-switch::after { transform: translateX(16px); }
.tv-toggle input:focus-visible + .tv-switch { outline: 3px solid color-mix(in srgb, var(--tv-c) 55%, transparent); outline-offset: 2px; }
.tv-toggle .tv-label { font-size: 14px; color: var(--text, #1d1d1f); }
.tv-results { display: grid; padding: 12px 14px 8px; border-radius: 14px; background: color-mix(in srgb, var(--tv-c) 11%, var(--bg, #fff)); }
.tv-hero { display: grid; gap: 1px; padding-bottom: 6px; }
.tv-hero small { font-size: 12.5px; font-weight: 600; color: var(--text-2, #6e6e73); }
.tv-hero .tv-value { font: 700 30px/1.15 var(--tv-font-display); letter-spacing: -.025em; font-variant-numeric: tabular-nums; overflow-wrap: anywhere; }
.tv-hero .tv-unit { font-size: 17px; letter-spacing: -.01em; }
.tv-row { display: flex; align-items: baseline; justify-content: space-between; gap: 14px; padding: 7px 0; border-top: 1px solid var(--hair, rgba(0, 0, 0, .08)); font-size: 14.5px; }
.tv-row > span { min-width: 0; color: var(--text-2, #6e6e73); overflow-wrap: anywhere; }
.tv-row .tv-value { font-weight: 600; text-align: right; font-variant-numeric: tabular-nums; overflow-wrap: anywhere; }
.tv-none { margin: 0 0 6px; font-size: 14px; color: var(--text-3, #86868b); }
.tv-table summary { cursor: pointer; font-size: 14px; font-weight: 600; color: var(--text-2, #6e6e73); }
.tv-table__scroll { margin-top: 8px; overflow-x: auto; border-radius: 10px; box-shadow: inset 0 0 0 1px var(--hair, rgba(0, 0, 0, .08)); }
.tv-grid { width: 100%; border-collapse: collapse; font-size: 13.5px; font-variant-numeric: tabular-nums; }
.tv-grid th, .tv-grid td { padding: 6px 10px; text-align: left; white-space: nowrap; border-top: 1px solid var(--hair, rgba(0, 0, 0, .08)); }
.tv-grid th { border-top: 0; font-weight: 600; color: var(--text-2, #6e6e73); background: color-mix(in srgb, var(--tv-c) 7%, transparent); }
.tv-note { margin: 0; font-size: 13.5px; line-height: 1.45; color: var(--text-2, #6e6e73); overflow-wrap: anywhere; }
@media (prefers-reduced-motion: reduce) { .tv-box, .tv-switch, .tv-switch::after { transition: none; } }
`;
if (typeof document !== 'undefined' && !document.getElementById('tv-style')) {
  const style = document.createElement('style');
  style.id = 'tv-style';
  style.textContent = css;
  document.head.append(style);
}
