/* Simpl Courses background script.
 * Runs as a service worker (Safari 16.4+, Chrome) or a non-persistent
 * background page (older Safari, Firefox). Responsibilities:
 *  - keep the toolbar badge in sync with due-soon counts
 *  - register content scripts for user-added Canvas domains
 */
if (typeof importScripts === 'function' && !self.BCV?.settings) {
  importScripts('lib/settings.js');
}

(function () {
  const BCV = self.BCV;
  const api = BCV.api;
  const S = BCV.settings;

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

  /** Build registerContentScripts entries by mirroring the manifest (the interface's scripts: the
   *  sniffer, content/sniff.js, already runs on every site it may look at and is not one of them). */
  function scriptsFor(origin) {
    const manifest = api.runtime.getManifest();
    const match = `${origin}/*`;
    return (manifest.content_scripts || []).filter((cs) => !(cs.js || []).includes('content/sniff.js')).map((cs, i) => ({
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

  // ---- lifecycle ----------------------------------------------------------
  api.runtime.onInstalled.addListener(async (details) => {
    await ensureDomains({ force: true });
    if (details.reason === 'install') await offerSetup();
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
  BCV.background = { offerSetup, ensureDomains, app, syncApp, openOptions }; // the harness drives these directly
})();
