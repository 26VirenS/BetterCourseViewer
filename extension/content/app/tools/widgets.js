/* Widgets of your own. A widget is one HTML file with a small header — a name, a line, an icon, a
 * size — and its own markup, style and script (docs/WIDGETS.md). Tools lists it as a card beside
 * the built-in tools, opens it in the same popup with the same head, pins it beside the switch, and
 * the search box's /tool and /pin know it. The widget runs in a sandboxed frame: no access to the
 * Canvas page, its cookies or the network (the frame's own policy says so), only a small
 * window.simpl — the theme's colours, a store of its own, a toast, resize, close, copy, open a
 * link — over messages the frame and this page exchange. The importer (the last card on Tools)
 * takes the file pasted, chosen, fetched from an address or one of the starters, checks it, shows
 * it running, and keeps it in the extension's storage. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  if (BCV.widgets) return; // (put in twice — the app injects every group at once — the first copy stands)
  const { h, escapeHtml } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const T = () => BCV.tools;
  const api = BCV.api;
  const KEY = 'widgets:custom';
  const DATA = (id) => `widgets:data:${id}`;
  const MAX_HTML = 200 * 1024; // one widget's file
  const MAX_TOTAL = 2 * 1024 * 1024; // every widget's file together
  const MAX_COUNT = 24;
  const MAX_DATA = 64 * 1024; // a widget's own store
  const SIZES = { S: { w: 400, h: 300 }, M: { w: 560, h: 420 }, L: { w: 760, h: 560 } };
  const MIN_H = 160;
  const MAX_H = 640;
  const DEFAULT_COLOR = '#5856d6';
  const THEME_VARS = ['--bcv-bg', '--bcv-card', '--bcv-fill', '--bcv-fill2', '--bcv-hover', '--bcv-sep', '--bcv-edge', '--bcv-ink', '--bcv-ink2', '--bcv-ink3', '--bcv-blue', '--bcv-font', '--bcv-display'];
  const TRASH = 'M4 7h16M10 11v6M14 11v6M6 7l1 12h10l1-12M9 7V4h6v3';
  const kb = (b) => (b >= 1048576 ? `${(b / 1048576).toFixed(1)} MB` : `${Math.max(1, Math.round(b / 1024))} KB`);
  const keyOf = (id) => `w:${id}`;
  const load = async (key, fallback) => { try { const r = await api.storage.local.get(key); return r[key] === undefined ? fallback : r[key]; } catch { return fallback; } };
  const save = async (key, value) => { try { await api.storage.local.set({ [key]: value }); } catch { /* the page keeps its own */ } };
  let list = null; // the widgets kept, once read: [{ id, name, note, icon, color, size, version, html, addedAt }]

  // ---- the file, read and checked -------------------------------------------------------------------
  /** The header and the checks: { ok, widget } or { ok: false, errors } — every error in plain words, the fix in it. */
  function parse(text, { source = '' } = {}) {
    const errors = [];
    const src = String(text || '');
    if (!src.trim()) return { ok: false, errors: ['Nothing to read yet: paste a widget’s HTML, choose its file, or give its address.'] };
    if (src.length > MAX_HTML) errors.push(`The file is ${kb(src.length)}; a widget may be ${kb(MAX_HTML)} at most.`);
    if (!/<[a-z!][\s\S]*>/i.test(src)) errors.push('That is not HTML: a widget is one HTML file with a header, markup, a <style> and a <script>.');
    let doc = null;
    try { doc = new DOMParser().parseFromString(src, 'text/html'); } catch { doc = null; }
    if (!doc) return { ok: false, errors: [...errors, 'That could not be read as HTML.'] };
    const metaEl = doc.querySelector('meta[name="simpl-widget"]');
    let meta = {};
    if (!metaEl) errors.push('No widget header. Put this line in the file, with its own name and line: <meta name="simpl-widget" content=\'{"name":"Countdown","note":"Days to a date.","icon":"clock","size":"S"}\'>');
    else {
      try { meta = JSON.parse(metaEl.getAttribute('content') || '{}'); } catch { errors.push('The widget header is not valid JSON: the content attribute holds {"name": …} — single quotes round the attribute, double quotes inside.'); }
      if (!meta || typeof meta !== 'object' || Array.isArray(meta)) { meta = {}; errors.push('The widget header must be a JSON object: {"name": …}.'); }
    }
    const name = String(meta.name || '').trim().slice(0, 40);
    if (metaEl && !name) errors.push('The header needs a "name" (up to 40 characters).');
    if (meta.icon && !(String(meta.icon) in IC)) errors.push(`No icon called "${String(meta.icon).slice(0, 30)}". One of: ${Object.keys(IC).join(', ')}.`);
    if (meta.size && !['S', 'M', 'L'].includes(String(meta.size).toUpperCase())) errors.push('"size" is S, M or L.');
    if (meta.color && !/^#[0-9a-f]{6}$/i.test(String(meta.color))) errors.push('"color" is a hex colour like "#5856d6".');
    if ([...doc.querySelectorAll('script')].some((s) => s.hasAttribute('src'))) errors.push('A <script src=…> is not allowed: put the code in the file itself (a widget has no network).');
    if (doc.querySelector('link[rel="stylesheet"], link[rel="import"]')) errors.push('A linked stylesheet is not allowed: put the CSS in a <style> in the file.');
    if (doc.querySelector('iframe, object, embed')) errors.push('A frame, object or embed is not allowed inside a widget.');
    if (errors.length) return { ok: false, errors };
    return { ok: true, widget: {
      name, note: String(meta.note || '').trim().slice(0, 80), icon: meta.icon ? String(meta.icon) : 'tool', color: meta.color ? String(meta.color).toLowerCase() : DEFAULT_COLOR,
      size: meta.size ? String(meta.size).toUpperCase() : 'M', version: String(meta.version ?? '1').slice(0, 20), html: src, source,
    } };
  }

  // ---- the sandbox: the widget's document, the runtime it talks through, the policy it lives under ----
  // The frame has scripts and nothing else (no same-origin: it is nobody, with no cookies and no
  // storage of the page's); its own policy allows the widget's inline code and styles, data: pictures
  // and fonts, and no network, no frames, no forms sent anywhere.
  const CSP = "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data: blob:; font-src data:; media-src data: blob:; connect-src 'none'; frame-src 'none'; child-src 'none'; form-action 'none'; base-uri 'none'";
  const BASE_CSS = ':root { color-scheme: light; } :root[data-theme="dark"] { color-scheme: dark; } html, body { margin: 0; padding: 0; background: transparent; } * { box-sizing: border-box; } body { padding: 16px; font: 14px/1.45 var(--bcv-font, -apple-system, system-ui, sans-serif); color: var(--bcv-ink, #1c1c1e); -webkit-font-smoothing: antialiased; } button, input, select, textarea { font: inherit; color: inherit; } button { cursor: pointer; } a { color: var(--bcv-blue, #0a84ff); }';
  const RUNTIME = `(function () {
  var seq = 0, waits = {}, readyFns = [], themeFns = [], ctx = null, said = false;
  function send(type, data) { parent.postMessage({ simpl: 1, type: type, data: data }, '*'); }
  function ask(type, data) { return new Promise(function (res, rej) { var id = ++seq; waits[id] = { res: res, rej: rej }; parent.postMessage({ simpl: 1, id: id, type: type, data: data }, '*'); }); }
  function apply(t) { var r = document.documentElement; var v = t && t.vars || {}; Object.keys(v).forEach(function (k) { r.style.setProperty(k, v[k]); }); r.setAttribute('data-theme', t && t.dark ? 'dark' : 'light'); }
  function call(fns) { fns.forEach(function (f) { try { f(ctx); } catch (e) { console.error(e); } }); }
  window.addEventListener('message', function (e) {
    var m = e.data; if (!m || m.simpl !== 1 || e.source !== parent) return;
    if (m.reply) { var w = waits[m.reply]; delete waits[m.reply]; if (!w) return; if (m.error) w.rej(new Error(m.error)); else w.res(m.value); return; }
    if (m.type === 'hello') { ctx = m.data; apply(ctx); if (!said) { said = true; call(readyFns); } else call(themeFns); }
    else if (m.type === 'theme') { ctx = m.data; apply(ctx); call(themeFns); }
  });
  window.simpl = {
    ready: function (fn) { if (typeof fn !== 'function') return; readyFns.push(fn); if (said) { try { fn(ctx); } catch (e) { console.error(e); } } },
    onTheme: function (fn) { if (typeof fn === 'function') themeFns.push(fn); },
    storage: {
      get: function (k, d) { return ask('get', { key: String(k) }).then(function (v) { return v === undefined ? d : v; }); },
      set: function (k, v) { return ask('set', { key: String(k), value: v }); },
      remove: function (k) { return ask('remove', { key: String(k) }); }
    },
    toast: function (text) { send('toast', { text: String(text) }); },
    close: function () { send('close'); },
    resize: function (px) { send('resize', { h: Number(px) }); },
    open: function (url) { send('open', { url: String(url) }); },
    copy: function (text) { send('copy', { text: String(text) }); },
    get theme() { return ctx; }
  };
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', function () { send('loaded'); }); else send('loaded');
})();`;
  const closeTag = (s, tag) => String(s).replace(new RegExp(`</${tag}`, 'gi'), `<\\/${tag}`);
  /** The widget's file as the document the frame shows: the policy and the runtime first, then its styles, its markup, its scripts (in that order, whatever order the file had them). */
  function build(w) {
    const doc = new DOMParser().parseFromString(w.html, 'text/html');
    const styles = [...doc.querySelectorAll('style')].map((s) => s.textContent);
    const scripts = [...doc.querySelectorAll('script')].filter((s) => !s.hasAttribute('src')).map((s) => s.textContent);
    for (const el of doc.querySelectorAll('script, style, link, meta, title, base, iframe, object, embed')) el.remove();
    const body = doc.body ? doc.body.innerHTML : '';
    return `<!doctype html><html><head><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="${CSP}"><title>${escapeHtml(w.name)}</title><style>${BASE_CSS}</style><script>${RUNTIME}</script>${styles.map((s) => `<style>${closeTag(s, 'style')}</style>`).join('')}</head><body>${body}${scripts.map((s) => `<script>${closeTag(s, 'script')}</script>`).join('')}</body></html>`;
  }
  const isDark = () => (BCV.app?.isDark ? BCV.app.isDark() : document.documentElement.getAttribute('data-bcv-theme') === 'dark');
  /** What the widget draws with: the theme's variables as they stand, the look, its own header. */
  function themeData(w) {
    const cs = getComputedStyle(document.documentElement);
    const vars = {};
    for (const k of THEME_VARS) { const v = cs.getPropertyValue(k).trim(); if (v) vars[k] = v; }
    return { vars, dark: isDark(), name: w.name, size: w.size, color: w.color, version: w.version };
  }
  const safeUrl = (s) => { try { const u = new URL(String(s || ''), location.origin); return /^https?:$/.test(u.protocol) ? u.href : null; } catch { return null; } };
  const dataOf = async (id) => { const d = await load(DATA(id), {}); return d && typeof d === 'object' && !Array.isArray(d) ? d : {}; };
  const live = new Map(); // frame → widget, for a theme change (the app repaints in place; a browser loads the page afresh)
  /** The bridge for one frame: what the widget may ask for, answered here and nowhere else. `preview`: a store that keeps nothing. */
  function attach(frame, w, { onClose = null, onResize = null, preview = false } = {}) {
    const onMsg = async (e) => {
      if (!frame.contentWindow || e.source !== frame.contentWindow) return;
      const m = e.data;
      if (!m || m.simpl !== 1) return;
      const reply = (value, error) => { try { frame.contentWindow.postMessage({ simpl: 1, reply: m.id, value, error }, '*'); } catch { /* the frame went */ } };
      try {
        switch (m.type) {
          case 'loaded': frame.contentWindow.postMessage({ simpl: 1, type: 'hello', data: themeData(w) }, '*'); frame.classList.add('is-in'); break;
          case 'get': reply(preview ? undefined : (await dataOf(w.id))[String(m.data?.key)]); break;
          case 'set': {
            if (preview) { reply(true); break; }
            const d = await dataOf(w.id);
            d[String(m.data?.key)] = m.data?.value;
            let json = '';
            try { json = JSON.stringify(d); } catch { reply(undefined, 'That value cannot be kept (it is not plain data).'); break; }
            if (json.length > MAX_DATA) { reply(undefined, `This widget’s store is full (${kb(MAX_DATA)} at most).`); break; }
            await save(DATA(w.id), JSON.parse(json));
            reply(true);
            break;
          }
          case 'remove': { if (!preview) { const d = await dataOf(w.id); delete d[String(m.data?.key)]; await save(DATA(w.id), d); } reply(true); break; }
          case 'toast': U.toast(String(m.data?.text || '').slice(0, 200)); break;
          case 'close': onClose?.(); break;
          case 'resize': { const px = Math.round(Number(m.data?.h)); if (Number.isFinite(px)) onResize?.(Math.max(MIN_H, Math.min(MAX_H, px))); break; }
          case 'open': { const u = safeUrl(m.data?.url); if (u) window.open(u, '_blank', 'noopener'); else U.toast('That link is not an http address.', { error: true }); break; }
          case 'copy': T()?.copyText?.(String(m.data?.text || '')); U.toast('Copied.'); break;
          default: break;
        }
      } catch (e) { if (m.id) reply(undefined, e?.message || String(e)); }
    };
    window.addEventListener('message', onMsg);
    live.set(frame, w);
    let mo = null;
    const detach = () => { window.removeEventListener('message', onMsg); live.delete(frame); mo?.disconnect(); };
    mo = new MutationObserver(() => { if (!frame.isConnected) detach(); });
    mo.observe(BCV.utils.overlayRoot(), { childList: true });
    return detach;
  }
  // a look change repaints the app's page in place: every live frame hears of it
  new MutationObserver(() => { for (const [frame, w] of live) { try { frame.contentWindow?.postMessage({ simpl: 1, type: 'theme', data: themeData(w) }, '*'); } catch { /* it went */ } } })
    .observe(document.documentElement, { attributes: true, attributeFilter: ['data-bcv-theme'] });
  /** The frame, sandboxed, with its bridge on; `preview` for the importer. */
  function frameFor(w, { preview = false, onClose = null } = {}) {
    const size = SIZES[w.size] || SIZES.M;
    const frame = h('iframe', { class: 'bcv-widget__frame', sandbox: 'allow-scripts', title: w.name, referrerpolicy: 'no-referrer', style: { height: `${size.h}px` } });
    const detach = attach(frame, w, { preview, onClose, onResize: (px) => { frame.style.height = `${px}px`; } });
    frame.srcdoc = build(w);
    return { frame, detach };
  }

  // ---- the widgets kept, and the tools they are listed as -------------------------------------
  const entryOf = (w) => ({ key: keyOf(w.id), name: w.name, note: w.note || 'A widget of your own', icon: IC[w.icon] || IC.tool, color: w.color || DEFAULT_COLOR, custom: true, widget: w, open: (app, o) => open(w, o) });
  /** The widgets given (or the ones kept) as tools in the registry, each once; a changed one replaced in place. */
  function register(widgets) {
    const TOOLS = T()?.TOOLS;
    if (!TOOLS) return;
    for (const w of Array.isArray(widgets) ? widgets : []) {
      if (!w || !w.id || !w.name) continue;
      const i = TOOLS.findIndex((t) => t.key === keyOf(w.id));
      if (i >= 0) TOOLS[i] = entryOf(w); else TOOLS.push(entryOf(w));
    }
  }
  const unregister = (id) => { const TOOLS = T()?.TOOLS; if (!TOOLS) return; const i = TOOLS.findIndex((t) => t.key === keyOf(id)); if (i >= 0) TOOLS.splice(i, 1); };
  /** The widgets kept, read once and registered. */
  async function all() {
    if (list) return list;
    const raw = await load(KEY, []);
    list = Array.isArray(raw) ? raw.filter((w) => w && w.id && w.name && typeof w.html === 'string') : [];
    register(list);
    return list;
  }
  async function keep(next) {
    list = next;
    await save(KEY, list);
  }
  /** A widget added (a widget of the same name replaces the old one, keeping its pin and its store). Returns the entry kept. */
  async function add(w) {
    const cur = await all();
    const same = cur.find((x) => x.name.toLowerCase() === w.name.toLowerCase());
    const total = cur.filter((x) => x !== same).reduce((n, x) => n + x.html.length, 0) + w.html.length;
    if (!same && cur.length >= MAX_COUNT) throw new Error(`That is ${MAX_COUNT} widgets already: remove one first.`);
    if (total > MAX_TOTAL) throw new Error(`Widgets may take ${kb(MAX_TOTAL)} together; this one would go over.`);
    const entry = { ...w, id: same ? same.id : T().uid('w'), addedAt: same ? same.addedAt : Date.now(), updatedAt: Date.now() };
    await keep(same ? cur.map((x) => (x === same ? entry : x)) : [...cur, entry]);
    register([entry]);
    T().paintPins?.();
    return { entry, replaced: !!same };
  }
  async function remove(id) {
    const cur = await all();
    await keep(cur.filter((x) => x.id !== id));
    try { await api.storage.local.remove(DATA(id)); } catch { /* the page keeps its own */ }
    if (T().pinned?.(keyOf(id))) await T().unpin(keyOf(id));
    unregister(id);
    T().paintPins?.();
  }
  const slug = (s) => String(s).toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '') || 'widget';
  const exportWidget = (w) => T().saveFile(`${slug(w.name)}.html`, new Blob([w.html], { type: 'text/html' }));
  async function removeAsk(w, from, after) {
    const ok = await U.askSheet({ label: 'Remove widget', title: `Remove ${w.name}?`, note: 'Its card, its pin and what it saved go with it. Its file can be saved first, from the popup.', okLabel: 'Remove', danger: true, from });
    if (!ok) return false;
    await remove(w.id);
    U.toast(`${w.name} removed.`);
    after?.();
    return true;
  }

  // ---- open: the popup every tool has, the frame in it -----------------------------------------
  function open(w, { from = null } = {}) {
    const tool = T().toolOf(keyOf(w.id)) || entryOf(w);
    const size = SIZES[w.size] || SIZES.M;
    let p = null;
    const { frame, detach } = frameFor(w, { onClose: () => p?.close() });
    const head = U.el('bcv-widget__acts', [
      h('button', { type: 'button', class: 'bcv-sheet__close bcv-widget__save', title: 'Save this widget’s file', 'aria-label': 'Save this widget’s file', onclick: () => exportWidget(w) }, U.svg(IC.download, { size: 13, stroke: 'var(--bcv-ink2)', width: 2.2 })),
      h('button', { type: 'button', class: 'bcv-sheet__close bcv-widget__remove', title: 'Remove this widget', 'aria-label': 'Remove this widget', onclick: (e) => removeAsk(w, e.currentTarget, () => { if (BCV.app?.state?.route?.screen === 'tools') BCV.app.render(); }) }, U.svg(TRASH, { size: 13, stroke: 'var(--bcv-ink2)', width: 2 })),
    ]);
    p = T().popup({ tool, title: w.name, sub: w.note || 'A widget of your own', width: size.w, body: U.el('bcv-widget', frame), from, cls: 'bcv-tool--widget', head, onClose: () => { detach(); } });
    return p;
  }

  // ---- the importer: paste, a file, an address, or a starter; checked, shown running, kept -------
  const IMPORTER = { key: 'widgets', name: 'Add a widget', icon: IC.plus, color: '#5856d6' };
  /** The last card on Tools: a dashed one that opens the importer. */
  const addCard = (app, { dark = false } = {}) => h('button', { type: 'button', class: 'bcv-tool-add', dataset: { tool: 'widgets' }, onclick: (e) => openImporter(app, { from: e.currentTarget }) }, [
    h('span', { class: 'bcv-tool-add__tile' }, U.svg(IC.plus, { size: 21, stroke: 'currentColor', width: 2 })),
    U.el('bcv-tool-add__text', [U.text('bcv-tool-add__name', 'Add a widget'), U.text('bcv-tool-add__note bcv-pretty', 'One HTML file of your own, or a starter to begin from.')]),
  ]);
  function openImporter(app, { from = null } = {}) {
    const dark = isDark();
    const body = U.el('bcv-wimp');
    const p = T().popup({ tool: IMPORTER, title: 'Add a widget', sub: 'One HTML file with a small header — see the starters for the shape.', width: 680, body, from, cls: 'bcv-tool--wimp' });
    let mode = 'paste';
    let detachPreview = null;
    const paneEl = U.el('bcv-wimp__pane');
    const resultEl = U.el('bcv-wimp__result');
    const clearResult = () => { detachPreview?.(); detachPreview = null; resultEl.replaceChildren(); };
    const showErrors = (errors) => { clearResult(); resultEl.append(U.text('bcv-tool__note bcv-tool__note--warn', 'Not yet — fix these and check again:'), h('ul', { class: 'bcv-wimp__errs' }, errors.map((e) => h('li', { text: e })))); };
    const showPreview = (w) => {
      clearResult();
      const entry = entryOf({ ...w, id: 'preview' });
      const { frame, detach } = frameFor(w, { preview: true });
      detachPreview = detach;
      const addBtn = U.btn('Add to Tools', { kind: 'primary', icon: IC.plus, iconColor: '#fff', onClick: async () => {
        addBtn.disabled = true;
        try {
          const { entry: kept, replaced } = await add(w);
          U.toast(replaced ? `${kept.name} updated.` : `${kept.name} is in Tools.`);
          p.close();
          if (app?.state?.route?.screen === 'tools') app.render();
        } catch (e) { U.toast(e?.message || String(e), { error: true }); addBtn.disabled = false; }
      } });
      resultEl.append(
        U.text('bcv-tool__hint bcv-pretty', `${w.name} checks out. This is how it will look on Tools, and here it is running (its store keeps nothing until it is added).`),
        U.el('bcv-wimp__preview', [T().cardEl(entry, { dark, demo: true }), U.el('bcv-wimp__stage', frame)]),
        U.el('bcv-wimp__foot', [U.btn('Try another', { onClick: () => { clearResult(); } }), addBtn]),
      );
    };
    const check = (text, source) => { const r = parse(text, { source }); if (r.ok) showPreview(r.widget); else showErrors(r.errors); };
    const panes = {
      paste: () => {
        const ta = h('textarea', { class: 'bcv-wimp__ta', placeholder: '<meta name="simpl-widget" content=\'{"name":"…","note":"…","icon":"tool","size":"M"}\'>\n<style>…</style>\n<div>…</div>\n<script>simpl.ready(function (t) { … });</script>', spellcheck: 'false', 'aria-label': 'The widget’s HTML' });
        return [ta, U.el('bcv-wimp__row', [U.btn('Check it', { kind: 'primary', onClick: () => check(ta.value, 'paste') })])];
      },
      file: () => {
        const input = h('input', { type: 'file', accept: '.html,.htm,text/html', hidden: true });
        const drop = h('label', { class: 'bcv-wimp__drop' }, [U.svg(IC.upload, { size: 22, stroke: 'var(--bcv-ink3)', width: 1.8 }), h('span', { text: 'Drop the widget’s HTML file here, or choose it' }), input]);
        const take = async (files) => { const f = files && files[0]; if (!f) return; try { check(await f.text(), f.name); } catch { showErrors(['The file could not be read.']); } };
        input.addEventListener('change', () => { take(input.files); input.value = ''; });
        drop.addEventListener('dragover', (e) => { e.preventDefault(); drop.classList.add('is-drag'); });
        drop.addEventListener('dragleave', () => drop.classList.remove('is-drag'));
        drop.addEventListener('drop', (e) => { e.preventDefault(); drop.classList.remove('is-drag'); take(e.dataTransfer?.files); });
        return [drop];
      },
      url: () => {
        const input = h('input', { type: 'url', class: 'bcv-wimp__url', placeholder: 'https://… the widget’s .html file', 'aria-label': 'The widget’s address', spellcheck: 'false' });
        const go = async () => {
          const url = input.value.trim();
          if (!url) { showErrors(['Type the address of the widget’s HTML file.']); return; }
          btn.disabled = true;
          try {
            const r = await Promise.resolve(api.runtime.sendMessage({ type: 'fetchText', url }));
            if (!r?.ok) throw new Error(r?.message || 'That address could not be fetched.');
            check(r.text, url);
          } catch (e) { showErrors([`${e?.message || e} — download the file and use Choose a file instead.`]); } finally { btn.disabled = false; }
        };
        const btn = U.btn('Fetch it', { kind: 'primary', onClick: go });
        input.addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); go(); } });
        return [U.el('bcv-wimp__row', [input, btn])];
      },
      starters: () => {
        const starters = self.BCV_WIDGET_STARTERS || [];
        if (!starters.length) return [U.text('bcv-tool__hint', 'The starters are not here on this page.')];
        return [U.el('bcv-wimp__starters', starters.map((s) => {
          const r = parse(s.html, { source: 'starter' });
          const w = r.ok ? r.widget : { name: s.name || 'Starter', note: '', icon: 'tool', color: DEFAULT_COLOR };
          return h('button', { type: 'button', class: 'bcv-wimp__starter', dataset: { starter: slug(w.name) }, onclick: () => check(s.html, 'starter') }, [
            h('span', { class: 'bcv-tool-card__tile', style: { background: T().tintOf(w.color, dark), width: '34px', height: '34px', borderRadius: '11px' } }, U.svg(IC[w.icon] || IC.tool, { size: 17, stroke: w.color, width: 1.8 })),
            U.el('bcv-omni__body', [U.text('bcv-omni__t', w.name, 'span'), U.text('bcv-omni__s', w.note, 'span')]),
            h('span', { class: 'bcv-tool-card__open' }, [h('span', { text: 'Try it' })]),
          ]);
        }))];
      },
    };
    const segBox = U.el('bcv-wimp__seg');
    const paintSeg = () => segBox.replaceChildren(T().seg([['paste', 'Paste'], ['file', 'A file'], ['url', 'An address'], ['starters', 'Starters']], mode, (v) => { if (mode === v) return; mode = v; paintSeg(); paint(); })); // (the control marks the pane chosen: rebuilt on each change, as every tool's is)
    const paint = () => { clearResult(); paneEl.replaceChildren(...panes[mode]()); };
    body.append(segBox, paneEl, resultEl, U.text('bcv-tool__hint bcv-pretty', 'A widget runs in a sandbox: it cannot see this page, your Canvas or the network. It gets the theme’s colours, a store of its own, a toast, and resize, close, copy and open a link — window.simpl in its script. The header names it and picks its icon and size.'));
    paintSeg();
    paint();
    const ready = self.BCV_WIDGET_STARTERS ? Promise.resolve() : (BCV.lazy?.load?.('starters') || Promise.resolve()).catch(() => {});
    ready.then(() => { if (mode === 'starters' && p.alive()) paint(); });
    return p;
  }

  BCV.widgets = { parse, build, all, add, remove, register, open, openImporter, addCard, exportWidget, keyOf, SIZES, MAX_HTML, MAX_DATA };
})();
