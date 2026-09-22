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

(function () {
  const BCV = self.BCV;
  const api = BCV.api;
  const S = BCV.settings;

  // ---- a window a framed tool opens for itself ----------------------------
  // A tool in a popup sometimes asks for a browser window of its own — a sign-in, a viewer, a
  // "this needs to open in a new window" button. The page cannot catch that: the frame is another
  // origin, so its window.open is out of reach from there. The browser tells the extension instead,
  // as a new tab; the address goes back to the page, which puts it in a popup over the one already
  // up, and the tab is taken away.
  //
  // What counts as the tool's window is not fixed, because browsers do not agree on what they say
  // about a new tab: whether they name the tab that opened it, whether the address is there yet.
  // The settings behind it are in lib/devcode.js and read as a code (SC1-1000100 is what ships).
  const DEV = BCV.devcode;
  let cap = { ...DEV.SHIPPED };
  api.storage?.local?.get?.(DEV.KEY).then((r) => { if (r?.[DEV.KEY]) cap = { ...DEV.SHIPPED, ...r[DEV.KEY] }; }).catch(() => {});
  const framed = new Map(); // tabId -> { open, allowUntil, windowId }
  const ALLOW_MS = 4000;
  const seen = []; // the log, newest last, for the Developer section to read back
  function note(entry) {
    if (!cap.log) return;
    seen.push({ at: Date.now(), ...entry });
    if (seen.length > 120) seen.splice(0, seen.length - 120);
  }
  function notePopup(sender, msg) {
    const id = sender?.tab?.id;
    if (id == null) return { ok: false };
    const was = framed.get(id) || {};
    framed.set(id, { ...was, open: !!msg.open, windowId: sender.tab.windowId });
    note({ kind: 'popup', tabId: id, open: !!msg.open });
    return { ok: true };
  }
  function allowTab(sender) {
    const id = sender?.tab?.id;
    if (id == null) return { ok: false };
    framed.set(id, { ...(framed.get(id) || {}), allowUntil: Date.now() + ALLOW_MS });
    note({ kind: 'allow', tabId: id });
    return { ok: true };
  }
  /** The tab a framed popup is open on, if this new tab could have come from one. */
  function opener(tab) {
    if (!cap.mode) return null;
    const live = [...framed.entries()].filter(([, st]) => st.open);
    if (!live.length) return null;
    if (cap.mode === 1) { const hit = live.find(([id]) => id === tab.openerTabId); return hit ? hit[0] : null; }
    if (cap.mode === 2) { const hit = live.find(([, st]) => st.windowId === tab.windowId) || live.find(([id]) => id === tab.openerTabId); return hit ? hit[0] : null; }
    return live[live.length - 1][0]; // mode 3: any new tab at all, to the popup opened last
  }
  /** The address, waited for when the browser has not filled it in yet. */
  async function addressOf(tabId, first) {
    const url = first.pendingUrl || first.url || '';
    if (/^https?:\/\//i.test(url)) return url;
    const ms = DEV.WAIT_MS[cap.wait] || 0;
    if (!ms || (!cap.blank && url && url !== 'about:blank')) return '';
    const until = Date.now() + ms;
    while (Date.now() < until) {
      await new Promise((r) => setTimeout(r, 120));
      let t = null;
      try { t = await api.tabs.get(tabId); } catch { return ''; } // the tab went
      const u = t.pendingUrl || t.url || '';
      if (/^https?:\/\//i.test(u)) return u;
    }
    return '';
  }
  async function caught(tab) {
    const entry = { kind: 'tab', tabId: tab.id, openerTabId: tab.openerTabId ?? null, windowId: tab.windowId, url: tab.url || '', pendingUrl: tab.pendingUrl || '' };
    try {
      const from = opener(tab);
      if (from == null) { note({ ...entry, action: 'no match' }); return; }
      if (Date.now() < (framed.get(from)?.allowUntil || 0)) { note({ ...entry, action: 'ours, left alone' }); return; }
      const url = await addressOf(tab.id, tab);
      if (!url) { note({ ...entry, action: 'no address' }); return; }
      await api.tabs.sendMessage(from, { type: 'framedPopup', url, toast: !!cap.toast }); // first: a page that is not listening keeps its tab
      if (cap.close) await api.tabs.remove(tab.id);
      note({ ...entry, url, action: cap.close ? 'caught, tab closed' : 'caught, tab left open' });
    } catch (e) {
      note({ ...entry, action: `could not: ${e?.message || e}` });
    }
  }
  // What this browser will even say. A log that shows nothing is otherwise two different stories —
  // nothing opened, or nothing was reported — and those want opposite fixes.
  const CAN = {
    tabs: !!api.tabs,
    onCreated: !!api.tabs?.onCreated,
    windows: !!api.windows?.onCreated,
    webNavigation: !!api.webNavigation?.onCreatedNavigationTarget,
  };
  let heardATab = false;
  api.tabs?.onRemoved?.addListener((id) => framed.delete(id));
  api.tabs?.onCreated?.addListener((tab) => { heardATab = true; caught(tab); });
  api.windows?.onCreated?.addListener(async (win) => {
    if (!cap.windows) return;
    try {
      const tabs = await api.tabs.query({ windowId: win.id });
      for (const t of tabs) await caught(t);
    } catch { /* the window went */ }
  });
  const normalNow = () => DEV.encode(cap) === DEV.encode(DEV.SHIPPED);
  function devGet() { return { ok: true, settings: { ...cap }, code: DEV.encode(cap), normal: normalNow() }; }
  async function devSet(settings) {
    cap = { ...DEV.SHIPPED, ...settings };
    // Back to normal forgets it rather than writing it down. A setting kept from a diagnosis would
    // otherwise sit on top of every default that ships afterwards, which is how close-the-tab came
    // to being off for good on a machine that had once been asked to catch everything.
    if (normalNow()) await api.storage.local.remove(DEV.KEY);
    else await api.storage.local.set({ [DEV.KEY]: cap });
    return { ok: true, settings: { ...cap }, code: DEV.encode(cap), normal: normalNow() };
  }
  function devLog(clear) {
    const rows = seen.slice();
    if (clear) seen.length = 0;
    return { ok: true, rows, code: DEV.encode(cap), can: { ...CAN, heardATab }, open: [...framed.entries()].filter(([, st]) => st.open).map(([id]) => id) };
  }

  // ---- one-shot messages --------------------------------------------------
  api.runtime.onMessage.addListener((msg, sender, sendResponse) => {
    if (!msg || typeof msg !== 'object') return false;
    const reply = (p) => Promise.resolve(p).then(sendResponse, (e) => sendResponse({ ok: false, message: e?.message || String(e) }));
    switch (msg.type) {
      case 'openOptions':
        reply(openOptions());
        return true;
      case 'setBadge':
        reply(setBadge(msg.count));
        return true;
      case 'registerDomain':
        reply(registerDomain(msg.origin));
        return true;
      case 'unregisterDomain':
        reply(unregisterDomain(msg.origin));
        return true;
      case 'listDomains':
        reply(listRegistered());
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
      case 'canvasSeen': // the Chrome build's sniffer found Canvas on a site of the school's own
        reply(canvasSeen(sender, msg));
        return true;
      case 'syncApp': // a Canvas page loading on a Mac: the settings the app holds, taken now
        reply(syncApp().then(() => ({ ok: true })));
        return true;
      case 'openTab': // an external tool that would not open in its popup: it gets a tab of its own
        reply(openTab(msg.url));
        return true;
      case 'framedPopup': // a framed tool is up on this tab, or has gone: see catchOpenedTab below
        reply(notePopup(sender, msg));
        return true;
      case 'framedAllowTab': // our own Open in new tab, about to make one on purpose
        reply(allowTab(sender));
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
  const VENDOR = new Set(['lib/vendor/mammoth.browser.min.js', 'lib/vendor/jspdf.umd.min.js', 'lib/vendor/pdf.min.js', 'lib/vendor/pdf.worker.min.js', 'lib/vendor/pdf-lib.min.js', 'content/app/tools/office.js']);
  async function injectVendor(sender, files) {
    const list = Array.isArray(files) ? files.filter((f) => VENDOR.has(f)) : [];
    if (!list.length) throw new Error('Nothing to load');
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
    let tabs = [];
    try { tabs = await api.tabs.query({}); } catch { return; }
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

  /** A tab for a web address, opened from here: a content script's own window.open would be the
   *  browser's idea of a popup once the press that started it is seconds old. */
  async function openTab(url) {
    if (!/^https?:\/\//i.test(String(url || ''))) return { ok: false };
    try {
      await api.tabs.create({ url });
      return { ok: true };
    } catch (e) {
      return { ok: false, message: e?.message || String(e) };
    }
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

  async function setBadge(count) {
    try {
      const n = Number(count) || 0;
      await api.action.setBadgeText({ text: n > 0 ? String(n) : '' });
      if (n > 0 && api.action.setBadgeBackgroundColor) {
        await api.action.setBadgeBackgroundColor({ color: '#dc2626' });
      }
      return { ok: true };
    } catch (e) {
      return { ok: false, message: e?.message };
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
   *  nothing else. The sniffer already runs on every site it may look at, and content/popout.js
   *  belongs inside a tool's own frame, which is never the Canvas site being added here. */
  const NOT_THE_INTERFACE = ['content/sniff.js', 'content/popout.js'];
  function scriptsFor(origin) {
    const manifest = api.runtime.getManifest();
    const match = `${origin}/*`;
    return (manifest.content_scripts || []).filter((cs) => !NOT_THE_INTERFACE.some((f) => (cs.js || []).includes(f))).map((cs, i) => ({
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

  async function listRegistered() {
    try {
      const scripts = await api.scripting.getRegisteredContentScripts();
      return { ok: true, scripts: scripts.map((s) => ({ id: s.id, matches: s.matches })) };
    } catch (e) {
      return { ok: false, message: e?.message };
    }
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
      const r = await app.native({ type: 'getSettings', revision: app.revision, site: extra['site:last'] || null, setupDone: !!extra['setup:done'] });
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
    const manifestScripts = (api.runtime.getManifest().content_scripts || []).filter((cs) => !(cs.js || []).includes('content/sniff.js'));
    const registered = await api.scripting?.getRegisteredContentScripts?.().catch(() => []) || [];
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

  // ---- lifecycle ----------------------------------------------------------
  api.runtime.onInstalled.addListener(async (details) => {
    await ensureDomains({ force: true });
    if (details.reason === 'install') await offerSetup();
    if (details.reason === 'update') await afterUpdate(details.previousVersion || null);
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
  BCV.background = { offerSetup, ensureDomains, app, syncApp, openOptions, afterUpdate, canvasTabs }; // the harness drives these directly
})();
