/* Simpl Courses background script.
 * Runs as a service worker (Safari 16.4+, Chrome) or a non-persistent
 * background page (older Safari, Firefox). Responsibilities:
 *  - keep the toolbar badge in sync with due-soon counts
 *  - register content scripts for user-added Canvas domains
 */
if (typeof importScripts === 'function' && !self.BCV?.settings) {
  importScripts('lib/settings.js');
}
if (typeof importScripts === 'function' && !self.BCV?.devcode) {
  importScripts('lib/devcode.js');
}
if (typeof importScripts === 'function' && !self.BCV_LAZY_MODULES) {
  importScripts('content/app/lazy-modules.js'); // the on-demand modules' files: what a page may ask to have loaded
}

(function () {
  const BCV = self.BCV;
  const api = BCV.api;
  const S = BCV.settings;

  // ---- a tool in a tab of its own -----------------------------------------
  // An external tool is not framed over Canvas any more: it opens in a tab, and the extension puts
  // its own bar across the top of the tool's own page (content/toolbar.js) so it still reads as
  // part of the interface. That means the tool is a real tab to the browser — its own cookies, its
  // own windows, its own sign-in — which is the whole point: nothing has to be caught, handed back
  // or nursed through a frame any more.
  //
  // What is kept here is the thread back: which Canvas tab a tool was opened from, so the X in its
  // bar lands there. A tab the tool opens for itself is adopted into the same session, so a sign-in
  // that goes through three sites keeps the bar and still comes home at the end.
  const DEV = BCV.devcode;
  let cap = { ...DEV.SHIPPED };
  api.storage?.local?.get?.(DEV.KEY).then((r) => { if (r?.[DEV.KEY]) cap = { ...DEV.SHIPPED, ...r[DEV.KEY] }; }).catch(() => {});
  const tools = new Map(); // tabId -> { from, title, note, state }
  const seen = []; // the log, newest last, for the Developer section to read back
  function note(entry) {
    if (!cap.log) return;
    seen.push({ at: Date.now(), ...entry });
    if (seen.length > 120) seen.splice(0, seen.length - 120);
  }
  // A service worker is stopped and started again whenever the browser feels like it, and a tool's
  // tab outlives that easily. The sessions are mirrored where they survive it, so the bar is still
  // there — and still knows its way back — after the worker has been round the houses.
  const KEEP = 'tool:tabs';
  const store = api.storage?.session || api.storage?.local;
  const save = () => { try { store?.set?.({ [KEEP]: [...tools.entries()] }); } catch { /* it lives in memory then */ } };
  const loaded = (async () => {
    try { const r = await store?.get?.(KEEP); for (const [id, t] of r?.[KEEP] || []) tools.set(Number(id), t); } catch { /* nothing kept */ }
  })();
  function setTool(id, t) { tools.set(id, t); save(); }

  /** Wait until the tab has finished doing whatever it opened to do.
   *
   *  A tool's first address is rarely where it ends up: a launch redirects, a sign-in is several
   *  pages — the tool, the school's identity provider, a code, a redirect back. While that is going
   *  on the bar says so and asks for quiet, and it stops saying so once the tab has settled: loaded,
   *  and at the same address for a quiet stretch. Cut short by a cap, in case it never settles.
   */
  async function settled(tabId) {
    const quiet = DEV.SETTLE_MS[cap.settle] ?? 2000;
    const CAP = 120000;
    const until = Date.now() + CAP;
    let last = '';
    let since = 0;
    while (Date.now() < until) {
      await new Promise((r) => setTimeout(r, 300));
      let t = null;
      try { t = await api.tabs.get(tabId); } catch { return { gone: true, url: '' }; }
      const url = t.pendingUrl || t.url || '';
      if (url !== last || t.status !== 'complete') { last = url; since = t.status === 'complete' ? Date.now() : 0; continue; }
      if (!since) { since = Date.now(); continue; }
      if (!quiet || Date.now() - since >= quiet) return { gone: false, url };
    }
    let t = null;
    try { t = await api.tabs.get(tabId); } catch { return { gone: true, url: '' }; }
    return { gone: false, url: t.pendingUrl || t.url || '', capped: true };
  }
  /** Watch a tool's tab until it stops moving, then take the notice down. */
  async function watch(tabId) {
    const end = await settled(tabId);
    const t = tools.get(tabId);
    if (!t || end.gone) return;
    t.state = 'ready';
    save();
    note({ kind: 'settled', tabId, url: end.url, action: end.capped ? 'never settled, said so anyway' : 'settled' });
    try { await api.tabs.sendMessage(tabId, { type: 'toolState', state: 'ready' }); } catch { /* no bar on it */ }
    ensureBar(tabId, t, end.url);
  }

  /* The bar has to survive the tool's own moves.
   *
   * A tool's tab rarely stays on one page: Canvas's launch page posts to the tool's site, the tool
   * signs in through another, and any of them may load again. Every one of those is a new document,
   * and the bar has to be put over each. Where the browser lets the extension onto the site, the
   * manifest's own script does that before the page paints; where it does not yet — Safari asks for
   * every site one at a time, the quiet Chrome build has Canvas's domain alone — nothing of ours can
   * run there, and the bar and the pinned tools are simply gone. So every navigation of a tool's tab
   * is followed here: the bar is asked whether it is there, put in when it is not, and when it cannot
   * be, the tab is marked (the toolbar icon's badge and title say the bar is off on this site) and the
   * popup offers to keep it there — the one press the browser needs to allow the site. */
  const actionApi = () => api.action || api.browserAction || null;
  async function injectBar(tabId) {
    if (!api.scripting?.executeScript) return false;
    try { await api.scripting.executeScript({ target: { tabId }, files: ['content/toolbar.js'] }); return true; } catch { return false; }
  }
  /** Is the bar on this tab? 'on', 'waking' (the script is there and building it), or 'none'. */
  async function barState(tabId) {
    try { const r = await api.tabs.sendMessage(tabId, { type: 'toolPing' }); return r?.ok ? (r.bar ? 'on' : 'waking') : 'none'; } catch { return 'none'; }
  }
  /** The bar over a tool's tab, after a move: there already, put in now, or marked off for this site. */
  async function ensureBar(tabId, t, url = '') {
    let s = await barState(tabId);
    if (s === 'none' && (await injectBar(tabId))) s = await barState(tabId);
    if (!tools.has(tabId)) return;
    await markBar(tabId, t, s !== 'none', url);
  }
  /** Remember whether the bar is off on the tab's site, and say so on the toolbar icon for that tab. */
  async function markBar(tabId, t, on, url = '') {
    let origin = '';
    try { const u = new URL(url); if (/^https?:$/.test(u.protocol)) origin = u.origin; } catch { /* no address to speak of */ }
    const was = t.barOff || '';
    // ('?': the browser would not say where the tab is — without the tabs permission, a site this
    // build may not reach has no address to read — so the badge says "this site", and the popup,
    // which reads the tab it is over from the press itself, names the site and offers it)
    t.barOff = on ? '' : (origin || '?');
    if (t.barOff !== was) { save(); note({ kind: 'bar', tabId, url, action: t.barOff ? `off on ${origin || 'a site it cannot see'}` : 'on' }); }
    const action = actionApi();
    if (!action?.setBadgeText) return;
    try {
      if (t.barOff) {
        await action.setBadgeText({ tabId, text: '!' });
        await action.setBadgeBackgroundColor?.({ tabId, color: '#ff9f0a' });
        await action.setTitle?.({ tabId, title: `Simpl Courses — its bar is off on ${origin ? new URL(origin).hostname : 'this site'}. Press to keep it here.` });
      } else if (was) {
        // the tab's own badge cleared (null), so the global count shows again; a browser that will not take null gets a blank
        try { await action.setBadgeText({ tabId, text: null }); } catch { await action.setBadgeText({ tabId, text: '' }); }
        try { await action.setTitle?.({ tabId, title: null }); } catch { await action.setTitle?.({ tabId, title: 'Simpl Courses' }); }
      }
    } catch { /* no badge to set here */ }
  }
  api.tabs?.onUpdated?.addListener(async (tabId, info) => {
    if (!info?.status) return; // (a title or a favicon changing is not a move)
    await loaded;
    const t = tools.get(tabId);
    if (!t) return;
    if (info.status === 'loading') { injectBar(tabId).catch(() => {}); return; } // early, so the bar is up before the page paints where it can be
    let url = '';
    try { url = (await api.tabs.get(tabId))?.url || ''; } catch { return; }
    ensureBar(tabId, t, url);
  });
  /** The bar-only script registered for a site — an origin, a whole domain's pattern (every aleks.com
   *  host), or the pattern for every site — so the next load there has it before the page paints. */
  async function registerBarScript(site) {
    if (!api.scripting?.registerContentScripts) return false;
    const match = /\*/.test(site) ? site : `${site}/*`;
    const id = `bcv-toolbar-${site.replace(/[^a-z0-9]/gi, '-')}`;
    try {
      const all = await api.scripting.getRegisteredContentScripts().catch(() => []);
      if ((all || []).some((sc) => sc.id === id)) return true;
      await api.scripting.registerContentScripts([{ id, matches: [match], js: ['content/toolbar.js'], runAt: 'document_start', persistAcrossSessions: true }]);
      return true;
    } catch { return false; }
  }
  /** The popup, on a tool's tab whose site the browser has just allowed (from a press there): the
   *  bar-only script registered for the site — or for every site, when that is what was allowed —
   *  and the bar put over the page now. */
  async function toolSite(msg) {
    await loaded;
    const tabId = Number(msg?.tabId);
    let origin = '';
    try { origin = new URL(msg?.origin).origin; } catch { return { ok: false, message: 'No site given.' }; }
    await registerBarScript(msg?.all ? '*://*/*' : origin);
    const t = tools.get(tabId);
    if (!t) return { ok: false, message: 'That tab is not a tool’s.' };
    await ensureBar(tabId, t, `${origin}/`);
    return { ok: !t.barOff, barOff: t.barOff || '' };
  }
  /** What the popup wants to know about the tab it is over: a tool's, and is its bar there? */
  async function toolTabOf(tabId) {
    await loaded;
    const t = tools.get(Number(tabId));
    return { ok: true, tool: t ? { title: t.title, note: t.note, state: t.state, barOff: t.barOff || '' } : null };
  }
  /** The quiet Chrome build has the run of Canvas's own domain and nothing else, so the bar cannot
   *  be drawn over the tool's own site until that site has been said yes to. The launch page names
   *  the site (the form Canvas posts to the tool), so it is read and asked for on the press that
   *  opens the tool, once per site — a build with the run of every site never asks. A no is kept,
   *  and never asked again. */
  const TOOL_SITES = 'tool:sites'; // origin -> 'yes' | 'no'
  async function allSites() {
    try { return await api.permissions.contains({ origins: ['*://*/*'] }); } catch { return true; } // (a browser that cannot say has the bar everywhere it is allowed)
  }
  async function toolOriginOf(url) {
    try {
      const r = await fetch(url, { credentials: 'include', redirect: 'follow' });
      const html = await r.text();
      const m = html.match(/<form[^>]+action\s*=\s*["']([^"']+)["']/i);
      const origin = m ? new URL(m[1], url).origin : null;
      return origin && origin !== new URL(url).origin && /^https?:/.test(origin) ? origin : null;
    } catch { return null; }
  }
  /** What a tool's origin is asked for as: the origin itself and, for a real domain, every host under
   *  its registrable domain (secure.aleks.com asks for *://*.aleks.com/*) — a tool moves between its
   *  own hosts after a launch (a sign-in, a redirect to the app's own server), and each would
   *  otherwise need a press of its own. A two-letter country code after co, com, ac, org, net, gov
   *  or edu keeps three labels (aleks.co.uk); an IP address or a bare host (localhost) is asked for
   *  as it is. The last pattern is the widest. */
  function toolSitePatterns(origin) {
    let u = null;
    try { u = new URL(origin); } catch { return []; }
    if (!/^https?:$/.test(u.protocol)) return [];
    const out = [`${u.origin}/*`];
    const host = u.hostname;
    const labels = host.split('.');
    const ip = /^\d+$/.test(labels[labels.length - 1]) || host.startsWith('[');
    if (labels.length >= 2 && !ip) {
      const n = labels.length >= 3 && labels[labels.length - 1].length === 2 && /^(co|com|ac|org|net|gov|edu)$/.test(labels[labels.length - 2]) ? 3 : 2;
      out.push(`*://*.${labels.slice(-n).join('.')}/*`);
    }
    return out;
  }
  async function toolSiteReady(url) {
    if (await allSites()) return;
    const origin = await toolOriginOf(url);
    if (!origin) return;
    const patterns = toolSitePatterns(origin);
    if (!patterns.length) return;
    const site = patterns[patterns.length - 1]; // (the whole domain where there is one, the origin alone otherwise)
    const kept = (await api.storage.local.get(TOOL_SITES).catch(() => ({})))?.[TOOL_SITES] || {};
    if (kept[site] === 'no' || kept[origin] === 'no') return;
    let ok = false;
    try { ok = await api.permissions.contains({ origins: [site] }); } catch { return; }
    if (!ok) {
      try { ok = await api.permissions.request({ origins: [site] }); } catch { return; } // (no gesture reached here: nothing to ask with)
      kept[site] = ok ? 'yes' : 'no';
      api.storage.local.set({ [TOOL_SITES]: kept }).catch(() => {});
    }
    if (ok) await registerBarScript(site);
  }
  /** A tool link pressed in the interface: a tab for it, remembering the tab it was opened from. */
  async function openTool(sender, msg) {
    const from = sender?.tab?.id;
    if (from == null || !msg?.url) return { ok: false };
    await toolSiteReady(msg.url);
    // openerTabId is what makes the browser put the tool's tab beside the one it came from; a
    // browser that will not take it still gets the tab, and the way home is remembered here anyway.
    let tab = null;
    try { tab = await api.tabs.create({ url: msg.url, active: true, openerTabId: from }); } catch { tab = await api.tabs.create({ url: msg.url, active: true }); }
    setTool(tab.id, { from, title: msg.title || 'External tool', note: msg.note || '', state: 'auth' });
    note({ kind: 'open', tabId: tab.id, from, url: msg.url });
    watch(tab.id);
    return { ok: true, tabId: tab.id };
  }
  /** Take a tab into the session its opener belongs to. Idempotent: the new-tab notice and the bar's
   *  own question race each other, and either one may be the first to get here. */
  function adopt(id, parent) {
    if (tools.has(id)) return tools.get(id);
    const t = { from: parent.from, title: parent.title, note: parent.note, state: 'auth' };
    setTool(id, t);
    watch(id);
    return t;
  }
  /** What the bar on a page needs to know, if this tab is a tool's at all. */
  async function toolTab(sender) {
    await loaded;
    const id = sender?.tab?.id;
    if (id == null) return { ok: true, tool: null };
    let t = tools.get(id);
    if (!t) {
      const parent = sender.tab.openerTabId == null ? null : tools.get(sender.tab.openerTabId);
      if (parent) t = adopt(id, parent);
    }
    return { ok: true, tool: t ? { title: t.title, note: t.note, state: t.state } : null };
  }
  /* The pinned tools, into a tool's own tab.
   *
   * The tray is meant to follow you about, and somebody else's page is exactly where a calculator or
   * the timer is wanted. Its scripts are not content scripts of the site's — they would then load on
   * every page in the world — so they are put in when the bar asks, on the tab the bar is on and
   * nowhere else. The list is the interface's own, less everything that is about Canvas: no store,
   * no API, no screens, no shell. */
  const TRAY_JS = [ // (tools.js before the tool modules, as the manifest has it: each takes `BCV.tools` as it loads)
    'lib/settings.js', 'lib/utils.js',
    'content/app/icons.js', 'content/app/motion.js', 'content/app/ui.js',
    'content/app/tools/tools.js', 'content/app/tools/widgets.js',
    'content/app/tools/ptable-data.js', 'content/app/tools/ptable.js',
    'content/app/tools/cite.js', 'content/app/tools/cards.js', 'content/app/tools/convert.js',
    'content/app/tools/need.js', 'content/app/tools/pdfs.js', 'content/app/tools/mark.js',
    'content/app/tools/ocr.js',
  ];
  async function toolWidgets(sender) {
    const id = sender?.tab?.id;
    if (id == null || !api.scripting?.executeScript) return { ok: false };
    if (!tools.has(id)) return { ok: false }; // only a tool's own tab, and only one the bar is already on
    try {
      await api.scripting.insertCSS({ target: { tabId: id }, files: ['content/styles/app.css'] });
      await api.scripting.executeScript({ target: { tabId: id }, files: TRAY_JS });
      return { ok: true };
    } catch (e) {
      note({ kind: 'widgets', tabId: id, action: `could not: ${e?.message || e}` });
      return { ok: false, message: e?.message || String(e) };
    }
  }
  /** The X in the bar: the tab goes, and the Canvas tab it came from comes back. */
  async function closeTool(sender) {
    await loaded;
    const id = sender?.tab?.id;
    if (id == null) return { ok: false };
    const t = tools.get(id);
    let back = false;
    if (t?.from != null) { try { await api.tabs.update(t.from, { active: true }); back = true; } catch { /* that tab was closed */ } }
    if (!back) {
      // whatever else is there, newest first, skipping the tool tabs (which lead nowhere better)
      try {
        const rest = (await api.tabs.query({ windowId: sender.tab.windowId })).filter((x) => x.id !== id);
        const pick = rest.reverse().find((x) => !tools.has(x.id)) || rest[0];
        if (pick) await api.tabs.update(pick.id, { active: true });
      } catch { /* nothing left to go back to */ }
    }
    tools.delete(id);
    save();
    note({ kind: 'close', tabId: id, action: back ? 'back to the Canvas tab' : 'back to whatever was there' });
    try { await api.tabs.remove(id); } catch { /* already gone */ }
    return { ok: true };
  }
  // What this browser will even say. A log that shows nothing is otherwise two different stories —
  // nothing opened, or nothing was reported — and those want opposite fixes.
  const CAN = { tabs: !!api.tabs, onCreated: !!api.tabs?.onCreated, session: !!api.storage?.session };
  let heardATab = false;
  api.tabs?.onRemoved?.addListener(async (id) => { await loaded; if (tools.delete(id)) save(); }); // (after the restore: a close that wakes the worker would otherwise be undone by it)
  // A window the tool opens for itself is the tool still: same session, same way home, same bar.
  api.tabs?.onCreated?.addListener(async (tab) => {
    heardATab = true;
    await loaded;
    const parent = tab.openerTabId == null ? null : tools.get(tab.openerTabId);
    if (!parent || tools.has(tab.id)) return;
    adopt(tab.id, parent);
    note({ kind: 'adopt', tabId: tab.id, from: parent.from, url: tab.pendingUrl || tab.url || '' });
  });
  const normalNow = () => DEV.encode(cap) === DEV.encode(DEV.SHIPPED);
  function devGet() { return { ok: true, settings: { ...cap }, code: DEV.encode(cap), normal: normalNow() }; }
  async function devSet(settings) {
    cap = { ...DEV.SHIPPED, ...settings };
    // Back to normal forgets it rather than writing it down. A setting kept from a diagnosis would
    // otherwise sit on top of every default that ships afterwards.
    if (normalNow()) await api.storage.local.remove(DEV.KEY);
    else await api.storage.local.set({ [DEV.KEY]: cap });
    return { ok: true, settings: { ...cap }, code: DEV.encode(cap), normal: normalNow() };
  }
  function devLog(clear) {
    const rows = seen.slice();
    if (clear) seen.length = 0;
    return { ok: true, rows, code: DEV.encode(cap), can: { ...CAN, heardATab }, open: [...tools.keys()] };
  }

  // ---- one-shot messages --------------------------------------------------
  /** Wikipedia's opensearch, for the Search everything box (content/app/search.js): the titles, a
   *  line each, the links. From here rather than the page, so the page's own rules never block it.
   *  (dev:wikiBase: the suites point it at the mock.) */
  async function wiki(q) {
    const base = (await api.storage.local.get('dev:wikiBase'))['dev:wikiBase'] || 'https://en.wikipedia.org';
    const res = await fetch(`${base}/w/api.php?action=opensearch&format=json&origin=*&namespace=0&limit=6&search=${encodeURIComponent(String(q || ''))}`);
    if (!res.ok) throw new Error(`Wikipedia ${res.status}`);
    const data = JSON.parse((await res.text()).replace(/^while\(1\);/, ''));
    const [, titles = [], descs = [], urls = []] = Array.isArray(data) ? data : [];
    return { ok: true, hits: titles.map((t, i) => ({ title: t, text: descs[i] || '', url: urls[i] || `https://en.wikipedia.org/wiki/${encodeURIComponent(t)}` })) };
  }
  /** A text file from an http(s) address, small enough for a widget (content/app/tools/widgets.js). */
  const FETCH_MAX = 300 * 1024;
  async function fetchText(url) {
    let u;
    try { u = new URL(String(url || '')); } catch { throw new Error('That is not an address.'); }
    if (!/^https?:$/.test(u.protocol)) throw new Error('Only an http or https address.');
    const res = await fetch(u.href, { redirect: 'follow', credentials: 'omit' });
    if (!res.ok) throw new Error(`The site answered ${res.status}.`);
    const tooBig = `That file is too big for a widget (${Math.round(FETCH_MAX / 1024)} KB at most).`;
    if (Number(res.headers.get('content-length') || 0) > FETCH_MAX) throw new Error(tooBig);
    const text = await res.text();
    if (text.length > FETCH_MAX) throw new Error(tooBig);
    return { ok: true, text, type: res.headers.get('content-type') || '' };
  }
  api.runtime.onMessage.addListener((msg, sender, sendResponse) => {
    if (!msg || typeof msg !== 'object') return false;
    const reply = (p) => Promise.resolve(p).then(sendResponse, (e) => sendResponse({ ok: false, message: e?.message || String(e) }));
    switch (msg.type) {
      case 'openOptions':
        reply(openOptions());
        return true;
      case 'registerDomain':
        reply(registerDomain(msg.origin));
        return true;
      case 'unregisterDomain':
        reply(unregisterDomain(msg.origin));
        return true;
      case 'wipeSiteNotes':
        reply(wipeSiteNotes());
        return true;
      case 'pushSettings': // the popup and the settings page ask for this straight after a save
        reply(S.get().then(pushSettings).then(() => ({ ok: true })));
        return true;
      case 'inject': // a bundled library, into the tab that asks for it (the Tools tab's file converter)
        reply(injectVendor(sender, msg.files));
        return true;
      case 'load': // one of the interface's own on-demand modules (content/app/lazy.js), into the page that asks
        reply(injectLazy(sender, msg.files));
        return true;
      case 'canvasSeen': // the Chrome build's sniffer found Canvas on a site of the school's own
        reply(canvasSeen(sender, msg));
        return true;
      case 'syncApp': // a Canvas page loading on a Mac: the settings the app holds, taken now
        reply(syncApp().then(() => ({ ok: true })));
        return true;
      case 'openTool': // a tool link in the interface: a tab of its own, with the bar over it
        reply(openTool(sender, msg));
        return true;
      case 'toolTab': // the bar asking whether this tab is a tool's
        reply(toolTab(sender));
        return true;
      case 'toolTabOf': // the popup asking about the tab it is over
        reply(toolTabOf(msg.tabId));
        return true;
      case 'toolSite': // the popup: the tool's site allowed from a press there — the bar registered for it and put in now
        reply(toolSite(msg));
        return true;
      case 'closeTool': // the X in that bar
        reply(closeTool(sender));
        return true;
      case 'toolWidgets': // the pinned tools, into the tool's own tab
        reply(toolWidgets(sender));
        return true;
      case 'wiki': // Search everything: a Wikipedia lookup
        reply(wiki(msg.q));
        return true;
      case 'fetchText': // the widget importer: a widget's file from an address (the page's own rules never block it)
        reply(fetchText(msg.url));
        return true;
      case 'devGet': // the Developer section: what the catch is set to
        reply(devGet());
        return true;
      case 'devSet':
        reply(devSet(msg.settings));
        return true;
      case 'devLog':
        reply(devLog(msg.clear));
        return true;
      case 'closeSetupTab': // the page after install, once the setup is under way on a Canvas tab
        reply(sender?.tab?.id != null ? api.tabs.remove(sender.tab.id).then(() => ({ ok: true })) : { ok: false });
        return true;
      default:
        return false;
    }
  });

  // The converter's libraries (lib/vendor/, see LICENSES.txt there) are too big to run on every
  // Canvas page as content scripts, so a page asks for them when a tool needs them and they land
  // in that page's isolated world beside our own scripts. Nothing outside lib/vendor/ can be asked
  // for, and nothing is fetched: the files travel with the build.
  const VENDOR = new Set(['lib/vendor/mammoth.browser.min.js', 'lib/vendor/jspdf.umd.min.js', 'lib/vendor/pdf.min.js', 'lib/vendor/pdf.worker.min.js', 'lib/vendor/pdf-lib.min.js', 'content/app/tools/office.js', 'lib/vendor/katex/katex.min.js', 'lib/vendor/katex/katex-css.js']);
  async function injectVendor(sender, files) {
    const list = Array.isArray(files) ? files.filter((f) => VENDOR.has(f)) : [];
    if (!list.length) throw new Error('Nothing to load');
    if (!sender?.tab?.id || !api.scripting?.executeScript) throw new Error('Not available here');
    await api.scripting.executeScript({ target: { tabId: sender.tab.id, frameIds: [sender.frameId || 0] }, files: list });
    return { ok: true, files: list };
  }
  // The interface's own on-demand modules (content/app/lazy.js): a page may ask for the files that
  // content/app/lazy-modules.js names, and nothing outside that list. The source manifest lists the
  // same files in a content-script group whose match never fires — Safari parses none of them until
  // asked, the iPhone app injects the group whole — and the Chrome builds leave that group out, since
  // Chrome would list its host among the sites the extension reads.
  const LAZY_MATCH = 'https://lazy.simplcourses.invalid/*'; // (that group, where the manifest has it: not the interface's script for a site)
  let lazyOk = null;
  const lazyFiles = () => (lazyOk ||= new Set(Object.values(self.BCV_LAZY_MODULES || {}).flatMap((m) => m.files || [])));
  async function injectLazy(sender, files) {
    const ok = lazyFiles();
    const list = Array.isArray(files) ? files.filter((f) => ok.has(f)) : [];
    if (!list.length || list.length !== files.length) throw new Error('Nothing to load');
    if (!sender?.tab?.id || !api.scripting?.executeScript) throw new Error('Not available here');
    await api.scripting.executeScript({ target: { tabId: sender.tab.id, frameIds: [sender.frameId || 0] }, files: list });
    return { ok: true, files: list };
  }

  // A settings change has to reach the open Canvas tabs, and storage.onChanged is not a reliable way
  // to get it there: in Safari a content script often never hears a change written by the popup or
  // the settings page, so the look switch wrote the setting and the page it was pressed for sat
  // exactly as it was. Messaging a tab does work (it is how Reset everything reaches them), so every
  // change is pushed to every tab as well. A tab that already knows does nothing with it; a tab with
  // none of ours in it never answers.
  S.onChange((settings) => { pushSettings(settings); writeUp(settings); });
  async function pushSettings(settings) {
    const tabs = await canvasTabs(); // the Canvas tabs alone: every other site's tab has nothing of ours to tell
    await Promise.all(tabs.map((t) => api.tabs.sendMessage(t.id, { type: 'settingsPush', settings }).catch(() => {})));
  }

  /** Reset everything (the settings page): every open Canvas tab is told to drop the one-line note it
   *  keeps in its own site storage (the look and the appearance, applied before first paint), so
   *  nothing of the extension's stays on the site. Tabs without the content script just do not answer. */
  async function wipeSiteNotes() {
    let tabs = [];
    try { tabs = await api.tabs.query({}); } catch { return { ok: false, cleared: 0 }; }
    let cleared = 0;
    await Promise.all(tabs.map(async (t) => {
      try {
        const res = await api.tabs.sendMessage(t.id, { type: 'wipeSiteNote' });
        if (res?.ok) cleared += 1;
      } catch { /* not a Canvas tab */ }
    }));
    return { ok: true, cleared };
  }

  async function openOptions() {
    if (app.on) { // on a Mac the settings live in the app: its window comes up
      try { await app.native({ type: 'openApp' }); return { ok: true }; } catch { /* the page below, then */ }
    }
    try {
      await api.runtime.openOptionsPage();
      return { ok: true };
    } catch {
      const url = api.runtime.getURL('options/options.html');
      await api.tabs.create({ url });
      return { ok: true };
    }
  }

  // ---- custom Canvas domains ---------------------------------------------
  function normalizeOrigin(input) {
    try {
      const u = new URL(/^https?:\/\//i.test(input) ? input : `https://${input}`);
      return u.origin;
    } catch {
      return null;
    }
  }

  function scriptIdFor(origin) {
    return 'bcv-' + origin.replace(/[^a-z0-9]/gi, '-');
  }

  /** Build registerContentScripts entries by mirroring the manifest: the interface's own scripts and
   *  the bar over a tool's tab (a tool launches from a page of the Canvas site being added, and the
   *  bar says Authenticating on it). The sniffer already runs on every site it may look at. */
  const NOT_THE_INTERFACE = ['content/sniff.js'];
  function scriptsFor(origin) {
    const manifest = api.runtime.getManifest();
    const match = `${origin}/*`;
    // (the on-demand modules' group is not registered for a site: a page asks for those as it needs them)
    return (manifest.content_scripts || []).filter((cs) => !NOT_THE_INTERFACE.some((f) => (cs.js || []).includes(f)) && !(cs.matches || []).includes(LAZY_MATCH)).map((cs, i) => ({
      id: `${scriptIdFor(origin)}-${i}`,
      matches: [match],
      js: cs.js || [],
      css: cs.css || [],
      runAt: cs.run_at || 'document_idle',
      persistAcrossSessions: true,
    }));
  }

  async function registerDomain(rawOrigin) {
    const origin = normalizeOrigin(rawOrigin);
    if (!origin) return { ok: false, message: 'That does not look like a valid URL.' };
    if (!api.scripting?.registerContentScripts) {
      return { ok: false, message: 'This browser cannot add extra sites (scripting API unavailable).' };
    }
    try {
      const granted = await api.permissions.contains({ origins: [`${origin}/*`] });
      if (!granted) return { ok: false, message: 'Permission for that site was not granted.' };
    } catch {
      /* Safari may not support permissions.contains; try registering anyway */
    }
    const scripts = scriptsFor(origin);
    try {
      // Registrations persist across sessions with the file list they were
      // made with, so always drop the old ones (any id we ever used for this
      // origin) and register fresh from the current manifest.
      const all = await api.scripting.getRegisteredContentScripts().catch(() => []);
      const stale = (all || []).map((s) => s.id).filter((id) => id.startsWith(`${scriptIdFor(origin)}-`));
      if (stale.length) await api.scripting.unregisterContentScripts({ ids: stale }).catch(() => {});
      await api.scripting.registerContentScripts(scripts);
    } catch (e) {
      return { ok: false, message: `Could not enable on ${origin}: ${e?.message || e}` };
    }
    const settings = await S.get();
    if (!settings.domains.includes(origin)) {
      await S.update({ domains: [...settings.domains, origin] });
    }
    return { ok: true, origin };
  }

  async function unregisterDomain(rawOrigin) {
    const origin = normalizeOrigin(rawOrigin);
    if (!origin) return { ok: false };
    const ids = scriptsFor(origin).map((s) => s.id);
    try {
      await api.scripting.unregisterContentScripts({ ids });
    } catch {
      /* may not be registered */
    }
    try {
      await api.permissions.remove({ origins: [`${origin}/*`] });
    } catch {
      /* ignore */
    }
    const settings = await S.get();
    await S.update({ domains: settings.domains.filter((d) => d !== origin) });
    return { ok: true };
  }

  /** The sniffer found Canvas on a site of the school's own (content/sniff.js, which runs on every page
   *  the extension may look at and reports a Canvas page once): the site is enabled — its scripts
   *  registered, as Enable on this site does from the toolbar — and, signed in, the tab is loaded again
   *  so the interface (and the setup, until it is done) comes up on it now. Canvas's sign-in page is
   *  enabled but left as it is: the page after signing in runs the interface. Canvas's own domain is
   *  built in and never asks. */
  const seenTabs = new Map(); // tab id → when it was last loaded again for this, so a page that keeps asking is not loaded round and round
  async function canvasSeen(sender, msg) {
    const tabId = sender?.tab?.id;
    let origin = null;
    try { origin = new URL(sender?.tab?.url || sender?.url || msg?.origin).origin; } catch { return { ok: false }; }
    if (!/^https?:$/.test(new URL(origin).protocol)) return { ok: false };
    if (/\.instructure\.com$/i.test(new URL(origin).hostname)) return { ok: true, builtIn: true };
    const r = await registerDomain(origin);
    if (r && r.ok === false) return r;
    if (!msg?.signedIn || tabId == null) return { ok: true, origin, reloaded: false };
    const last = seenTabs.get(tabId) || 0;
    if (Date.now() - last < 30 * 1000) return { ok: true, origin, reloaded: false };
    seenTabs.set(tabId, Date.now());
    try { await api.tabs.reload(tabId); } catch { /* the tab went */ }
    return { ok: true, origin, reloaded: true };
  }

  /** Identifies the content-script set of this build; changes whenever the
   *  manifest's script list or the version changes. */
  function manifestStamp() {
    const m = api.runtime.getManifest();
    return `${m.version}:${JSON.stringify((m.content_scripts || []).map((cs) => [cs.js, cs.css, cs.run_at]))}`;
  }
  const STAMP_KEY = 'scriptsStamp';

  /** Re-register scripts for saved domains whenever this build's script set
   *  differs from the one they were registered with (or when forced). */
  async function ensureDomains({ force = false } = {}) {
    try {
      const settings = await S.get();
      if (!settings.domains?.length) return;
      const stamp = manifestStamp();
      const stored = (await api.storage.local.get(STAMP_KEY))[STAMP_KEY];
      let registered = [];
      try {
        registered = (await api.scripting.getRegisteredContentScripts()) || [];
      } catch {
        registered = [];
      }
      for (const origin of settings.domains) {
        const has = registered.some((s) => s.id.startsWith(`${scriptIdFor(origin)}-`));
        if (!force && stored === stamp && has) continue;
        const ok = await api.permissions.contains({ origins: [`${origin}/*`] }).catch(() => true);
        if (ok) await registerDomain(origin);
      }
      await api.storage.local.set({ [STAMP_KEY]: stamp });
    } catch {
      /* ignore */
    }
  }

  /** Which setup flow this build carries. A build that changes the flow bumps it, and the flags
   *  from the older flow ("offered", "done") are cleared once, so the new flow is seen once. */
  const SETUP_FLOW = 3;
  /** Older builds kept Canvas answers in storage; nothing is kept between pages now. */
  async function dropStoredCache() {
    try {
      const all = await api.storage.local.get(null);
      const keys = Object.keys(all).filter((k) => k.startsWith('cache:'));
      if (keys.length) await api.storage.local.remove(keys);
    } catch {
      /* ignore */
    }
  }
  async function migrateSetup() {
    await dropStoredCache();
    try {
      const cur = await api.storage.local.get('setup:flow');
      if (cur && cur['setup:flow'] === SETUP_FLOW) return;
      await api.storage.local.remove(['setup:offered', 'setup:done', 'setup:plan']);
      await api.storage.local.set({ 'setup:flow': SETUP_FLOW });
    } catch {
      /* ignore */
    }
  }
  /** The page that says how to start. It opens when the extension is switched on and setup has not
   *  been done — once per browser session, so Safari turning the extension on (which is not an
   *  install, and often follows a rebuild) still lands on it, and finishing setup ends it for good.
   *  Without session storage there is nothing to tell one run from the next, so it opens once ever. */
  // One offer at a time. An install runs this twice at once — from onInstalled and from the
  // background starting — and two runs that both read the flags before either has set them both
  // open the page; the second now waits for the first and finds the page already offered.
  let offering = null;
  function offerSetup() {
    if (!offering) offering = offerSetupNow().finally(() => { offering = null; });
    return offering;
  }
  async function offerSetupNow() {
    try {
      await migrateSetup();
      const state = await api.storage.local.get(['setup:offered', 'setup:done']);
      if (state['setup:done']) return; // set up already: never again
      let thisSession = null;
      if (api.storage.session) {
        try {
          const s = await api.storage.session.get('setup:shown');
          thisSession = !!(s && s['setup:shown']);
        } catch { /* no session storage after all */ }
      }
      if (thisSession === true) return; // shown once since this browser started
      if (thisSession === null && state['setup:offered']) return;
      await api.storage.local.set({ 'setup:offered': true });
      if (api.storage.session) await api.storage.session.set({ 'setup:shown': true }).catch(() => {});
      await openSetupPage();
    } catch {
      /* ignore */
    }
  }
  /** Safari starts the background page as it enables the extension, a moment when a new tab can be
   *  refused; a couple of retries cover that rather than losing the page. */
  async function openSetupPage(attempt = 0) {
    try {
      await api.tabs.create({ url: api.runtime.getURL('setup/setup.html') });
    } catch (e) {
      if (attempt < 3) setTimeout(() => openSetupPage(attempt + 1), 600 * (attempt + 1));
    }
  }

  /** Chrome closes the toolbar popup the moment its permission dialog opens, so the popup cannot
   *  finish what Set up (or Enable on this site) started. The popup leaves a note first; when the
   *  permission lands, the rest happens here: the site registered, then the setup over that tab
   *  (or the tab reloaded). The note expires, and the popup clears it itself when it survives. */
  async function continuePending(added) {
    try {
      // the popup on a tool's tab asked for the tool's site (or for every site): the bar registered for it and put over the page
      const tp = (await api.storage.local.get('tool:pending'))?.['tool:pending'];
      if (tp?.origin && Date.now() - (tp.at || 0) < 3 * 60 * 1000 && (!(added?.origins || []).length || added.origins.some((o) => o.startsWith(tp.origin) || o === '*://*/*' || o === '<all_urls>'))) {
        await api.storage.local.remove('tool:pending');
        await toolSite(tp);
      }
    } catch { /* the popup may still be alive and finish it itself */ }
    try {
      const all = await api.storage.local.get('setup:pending');
      const p = all && all['setup:pending'];
      if (!p || !p.origin || Date.now() - (p.at || 0) > 3 * 60 * 1000) return;
      const origins = (added && added.origins) || [];
      if (origins.length && !origins.some((o) => o.startsWith(p.origin))) return;
      await api.storage.local.remove('setup:pending');
      const r = await registerDomain(p.origin);
      if (r && r.ok === false) return;
      if (p.next === 'setup') {
        await S.update({ appearance: { skin: true } });
        await api.tabs.update(p.tabId, { url: `${p.origin}/?bcv=setup` });
      } else if (p.tabId != null) await api.tabs.reload(p.tabId);
    } catch {
      /* the popup may still be alive and finish it itself */
    }
  }
  if (api.permissions && api.permissions.onAdded) api.permissions.onAdded.addListener(continuePending);

  // ---- the Mac app: the settings' home on macOS ---------------------------------------------------
  // On a Mac the extension lives inside the Simpl Courses app, and the app holds the settings: its
  // window is where they are set, and this background takes them from the app's shared store through
  // the native message handler (SafariWebExtensionHandler.swift) — on every Canvas page load, every
  // few seconds while Safari is up, and after any switch pressed here (the popup, the look switch on
  // a page), which is written up to the app so its window shows it. A revision number says whether
  // there is anything new; a command the app left (a wipe after Reset everything) is run and reported
  // done. Nowhere else (Chrome, Firefox, the iOS app) is there an app to ask.
  const app = {
    on: (() => { try { return typeof api.runtime.sendNativeMessage === 'function' && /Mac/.test(navigator.platform || '') && /apple/i.test(navigator.vendor || ''); } catch { return false; } })(),
    revision: -1, // the store's revision as last taken (or written)
    lastJSON: null, // the settings as last taken or written, so a change that is only ours coming back is not written up again
    lastPrefsJSON: null, // the site's preferences as last sent up, so they go up only when they changed
    syncing: null,
    native: (msg) => api.runtime.sendNativeMessage('application.id', msg),
  };
  async function syncApp() {
    if (!app.on) return null;
    if (app.syncing) return app.syncing;
    app.syncing = syncOnce().finally(() => { app.syncing = null; });
    const r = await app.syncing;
    if (r && r.again) return syncApp(); // a command changed things here: ask again, with the store as it is now
    return r;
  }
  async function syncOnce() {
    try {
      const extra = await api.storage.local.get(['site:last', 'setup:done']).catch(() => ({}));
      // the site's own preferences go up with the ask when they changed since last time, so the app's
      // window can show the Grades section (a change made there comes back as a setPrefs command)
      const host = extra['site:last']?.host || '';
      let prefs = null;
      if (host) {
        const p = (await api.storage.local.get(`prefs:${host}`).catch(() => ({})))?.[`prefs:${host}`] || {};
        const json = JSON.stringify(p);
        if (json !== app.lastPrefsJSON) { prefs = p; app.lastPrefsJSON = json; }
      }
      const r = await app.native({ type: 'getSettings', revision: app.revision, site: extra['site:last'] || null, setupDone: !!extra['setup:done'], ...(prefs ? { prefs, prefsHost: host } : {}) });
      if (!r || typeof r.revision !== 'number') return null;
      // what the app asked for comes first (a wipe changes what there is to exchange below)
      const commands = Array.isArray(r.commands) ? r.commands.filter((c) => c && typeof c === 'object') : [];
      if (commands.length) {
        for (const c of commands) {
          await runAppCommand(c).catch(() => {});
          if (c.id) await app.native({ type: 'done', id: c.id }).catch(() => {});
        }
        return { again: true };
      }
      if (r.revision !== app.revision) {
        if (r.settings && typeof r.settings === 'object') {
          const merged = await S.replace(r.settings);
          app.lastJSON = JSON.stringify(merged);
          app.revision = r.revision;
          ensureDomains(); // a site added in the app gets its scripts
        } else if (!r.hasSettings) {
          // the app has none yet (a first launch, a wipe): it takes ours — unless they went up a moment ago
          const mine = await S.get();
          const json = JSON.stringify(mine);
          if (json !== app.lastJSON) {
            app.lastJSON = json;
            const w = await app.native({ type: 'setSettings', settings: mine });
            app.revision = w && typeof w.revision === 'number' ? w.revision : r.revision;
          } else {
            app.revision = r.revision;
          }
        } else {
          app.revision = r.revision;
        }
      }
      return r;
    } catch {
      return null; // no app to ask (a build without one, a handler that failed): the settings here stand
    }
  }
  /** A switch pressed here reaches the app's store (and its window). */
  async function writeUp(settings) {
    if (!app.on) return;
    const json = JSON.stringify(settings);
    if (json === app.lastJSON) return;
    app.lastJSON = json;
    try {
      const w = await app.native({ type: 'setSettings', settings });
      if (w && typeof w.revision === 'number') app.revision = w.revision;
    } catch { /* the app answers next time */ }
  }
  /** What the app asks for: a wipe (Reset everything in its window), or the site notes cleared. */
  async function runAppCommand(c) {
    if (!c || typeof c !== 'object') return;
    if (c.type === 'wipe') {
      const settings = await S.get();
      for (const origin of settings.domains || []) await unregisterDomain(origin).catch(() => {});
      await api.storage.local.clear().catch(() => {});
      try { await api.storage.session?.clear(); } catch { /* none */ }
      await wipeSiteNotes().catch(() => {});
      app.lastJSON = null;
    } else if (c.type === 'wipeSiteNotes') {
      await wipeSiteNotes().catch(() => {});
    } else if (c.type === 'setPrefs' && c.host && c.patch && typeof c.patch === 'object') {
      // the app's window changed the site's preferences (the Grades section): the keys it names land
      // on the copy kept here, a null taking a key away; the rest — a snapshot recorded meanwhile — stays
      const key = `prefs:${c.host}`;
      const prefs = (await api.storage.local.get(key).catch(() => ({})))?.[key] || {};
      for (const [k, v] of Object.entries(c.patch)) { if (v === null || v === undefined) delete prefs[k]; else prefs[k] = v; }
      await api.storage.local.set({ [key]: prefs }).catch(() => {});
      app.lastPrefsJSON = null; // sent up afresh on the next ask, so the app's copy is the whole
    }
  }
  if (app.on) {
    syncApp();
    setInterval(syncApp, 5000);
  }

  // ---- an update: every Canvas tab loads again, so the new version is on it at once -------------
  // The tabs are the ones the interface runs on — Canvas's own domain from the manifest, the sites
  // added (their registered scripts) — found by address; every other tab is left alone. Driven from
  // onInstalled's update, and from the version noted in storage on every start (Safari rebuilds and
  // reloads the extension without an onInstalled at times), once per version either way.
  const VERSION_KEY = 'version:running';
  let reloadedFor = null; // the version the tabs were loaded again for, this run
  /** A manifest match pattern as a test of a tab's address. */
  function matchRe(p) {
    const m = /^(\*|https?):\/\/(\*|\*\.[^/]+|[^/*]+)(\/.*)$/.exec(p);
    if (!m) return null;
    const scheme = m[1] === '*' ? 'https?' : m[1];
    const host = m[2] === '*' ? '[^/]+' : m[2].startsWith('*.') ? `(?:[^/]+\\.)?${m[2].slice(2).replace(/\./g, '\\.')}` : m[2].replace(/\./g, '\\.');
    const path = m[3].replace(/[.+?^${}()|[\]\\]/g, '\\$&').replace(/\*/g, '.*');
    return new RegExp(`^${scheme}://${host}${path}$`, 'i');
  }
  async function canvasTabs() {
    let tabs = [];
    try { tabs = await api.tabs.query({}); } catch { return []; }
    // the interface's own scripts alone: the sniffer and the tool bar run on every site, and their
    // matches would make every open tab in the browser a "Canvas tab" — reloaded, work and all
    const NOT_THE_INTERFACE = ['content/sniff.js', 'content/toolbar.js'];
    const manifestScripts = (api.runtime.getManifest().content_scripts || []).filter((cs) => !NOT_THE_INTERFACE.some((f) => (cs.js || []).includes(f)) && !(cs.matches || []).includes(LAZY_MATCH));
    const registered = (await api.scripting?.getRegisteredContentScripts?.().catch(() => []) || []).filter((s) => !String(s.id || '').startsWith('bcv-toolbar-'));
    const res = [...manifestScripts, ...registered].flatMap((cs) => cs.matches || []).map(matchRe).filter(Boolean);
    return tabs.filter((t) => typeof t.url === 'string' && /^https?:/.test(t.url) && res.some((re) => re.test(t.url)));
  }
  async function afterUpdate(previous) {
    const version = api.runtime.getManifest().version;
    if (reloadedFor === version) return { ok: true, reloaded: 0, previous, already: true };
    reloadedFor = version;
    const tabs = await canvasTabs();
    await Promise.all(tabs.map((t) => api.tabs.reload(t.id).catch(() => {})));
    return { ok: true, reloaded: tabs.length, previous };
  }
  async function noteVersion() {
    const version = api.runtime.getManifest().version;
    try {
      const was = (await api.storage.local.get(VERSION_KEY))[VERSION_KEY];
      await api.storage.local.set({ [VERSION_KEY]: version });
      if (was && was !== version) await afterUpdate(was);
    } catch { /* nothing to note it in */ }
  }

  // ---- the page after this update: "Simpl has updated. Please accept the permissions." ---------
  // Chrome keeps an updated extension running but withholds site access it did not have before,
  // until the puzzle piece's prompt is accepted (an API permission with a new warning switches it
  // off instead, and then nothing here can run at all). So the Chrome build's first run after an
  // update from before 2.98.15 opens one tab that says so, points at the puzzle piece, and offers
  // the press that asks (setup/updated.html). Once: the version it was shown for is kept. Other
  // builds (Safari, Firefox, the quiet build) have no run of every site to have withheld.
  const UPDATED_FOR = '2.98.15';
  const olderThan = (a, b) => { const x = String(a).split('.').map(Number), y = String(b).split('.').map(Number); for (let i = 0; i < 3; i++) { if ((x[i] || 0) !== (y[i] || 0)) return (x[i] || 0) < (y[i] || 0); } return false; };
  function updatedPageDue(previous, hostPerms = api.runtime.getManifest().host_permissions || []) {
    return !!previous && olderThan(previous, UPDATED_FOR) && (hostPerms || []).includes('*://*/*');
  }
  async function openUpdated(previous) {
    if (!updatedPageDue(previous)) return false;
    try {
      const r = await api.storage.local.get('updated:shown');
      if (r['updated:shown'] === UPDATED_FOR) return false;
      await api.storage.local.set({ 'updated:shown': UPDATED_FOR });
    } catch { /* shown all the same */ }
    try { await api.tabs.create({ url: api.runtime.getURL('setup/updated.html'), active: true }); return true; } catch { return false; }
  }

  // ---- lifecycle ----------------------------------------------------------
  api.runtime.onInstalled.addListener(async (details) => {
    await ensureDomains({ force: true });
    if (details.reason === 'install') await offerSetup();
    if (details.reason === 'update') { await afterUpdate(details.previousVersion || null); await openUpdated(details.previousVersion || null); }
    // An update: the first Canvas page after it shows what changed (content/app/whatsnew.js), from
    // the version left behind — the oldest one still unread, when several updates go by unseen.
    if (details.reason === 'update' && details.previousVersion) {
      try {
        const p = await api.storage.local.get('whatsnew:from');
        if (!p['whatsnew:from']) await api.storage.local.set({ 'whatsnew:from': details.previousVersion });
      } catch { /* the page then says the version alone */ }
    }
  });
  if (api.runtime.onStartup) api.runtime.onStartup.addListener(() => ensureDomains());
  // Every time the background wakes: cheap check, repairs stale registrations
  // even when onInstalled/onStartup never fired (Safari rebuilds, reloads).
  ensureDomains();
  offerSetup();
  noteVersion();
  BCV.background = { offerSetup, ensureDomains, app, syncApp, openOptions, afterUpdate, canvasTabs, toolSitePatterns, updatedPageDue, openUpdated }; // the harness drives these directly
})();
