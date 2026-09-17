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
      case 'scanTabs': // the page after install, as it opens: a Canvas that is already open
        reply(scanTabs());
        return true;
      case 'checkFound': // the page after install, as it opens: is what was found still there?
        reply(checkFound());
        return true;
      case 'startSetup': // the page after install, from a press: the setup on that site's tab
        reply(startSetupOn(msg.origin, msg.tabId, { force: true }).then((ok) => ({ ok })));
        return true;
      default:
        return false;
    }
  });

  // A settings change has to reach the open Canvas tabs, and storage.onChanged is not a reliable way
  // to get it there: in Safari a content script often never hears a change written by the popup or
  // the settings page, so the look switch wrote the setting and the page it was pressed for sat
  // exactly as it was. Messaging a tab does work (it is how Reset everything reaches them), so every
  // change is pushed to every tab as well. A tab that already knows does nothing with it; a tab with
  // none of ours in it never answers.
  S.onChange((settings) => { pushSettings(settings); });
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

  /** Build registerContentScripts entries by mirroring the manifest. */
  function scriptsFor(origin) {
    const manifest = api.runtime.getManifest();
    const match = `${origin}/*`;
    return (manifest.content_scripts || []).map((cs, i) => ({
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

  // ---- noticing a Canvas tab ------------------------------------------------------------------
  /** Does this address look like Canvas? Canvas's own hosts, a host with canvas in its name, or the
   *  paths only Canvas has. Without a permission for the site its address is all there is to go on,
   *  and a school's own name for it (lms.school.edu) gives nothing away — the page after install
   *  takes an address typed in for those. */
  function looksLikeCanvas(url, tab = null) {
    if (!/^https?:$/.test(url.protocol)) return false;
    if (/(^|\.)instructure\.com$|(^|\.)canvaslms\.com$|canvas/i.test(url.hostname)) return true;
    if (/^\/(courses|dashboard|calendar|conversations|login\/canvas)(\/|$)/.test(url.pathname) || url.searchParams.has('login_success')) return true;
    // Canvas's own favicon, from its own build (/dist/images/favicon-…) on a school's own address
    // as much as on Instructure's CDN: the one thing a dashboard sitting at / still gives away
    const icon = String(tab?.favIconUrl || '');
    return /\/dist\/images\/favicon[-.]/i.test(icon) || /instructure|canvas/i.test(icon);
  }
  const ourPage = (url) => url.startsWith(api.runtime.getURL(''));
  async function siteGranted(origin) {
    if (/\.instructure\.com$/i.test(new URL(origin).hostname)) return true;
    try { return await api.permissions.contains({ origins: [`${origin}/*`] }); } catch { return false; }
  }
  /** Whether the browser granted every site at install (Chrome does, from the manifest). Where it
   *  did, a site being allowed says nothing about it — the page has to be Canvas by its own signs.
   *  Safari asks per site whatever the manifest says, so there an allowed site is one the user chose. */
  async function broadGrant() {
    try { return await api.permissions.contains({ origins: ['<all_urls>'] }); } catch { return false; }
  }
  /** Canvas, by the page itself — run inside the tab, DOM only: Canvas's own favicon from its own
   *  build, its application shell, or the banner for its iOS app. A login page carries them too. */
  const sniffFn = () => {
    const d = document;
    const icon = [...d.querySelectorAll('link[rel~="icon"], link[rel="shortcut icon"]')].some((l) => /\/dist\/images\/favicon[-.]/i.test(l.href || ''));
    const shell = !!d.querySelector('#application.ic-app, body.ic-Layout, .ic-Layout-wrapper, #wrapper.ic-Layout-wrapper');
    const banner = !!d.querySelector('meta[name="apple-itunes-app"][content*="480883488"]');
    return { canvas: icon || shell || banner, origin: location.origin };
  };
  async function sniff(tabId) {
    try {
      const res = await api.scripting.executeScript({ target: { tabId }, func: sniffFn });
      return !!(res && res[0] && res[0].result && res[0].result.canvas);
    } catch {
      return false; // a page that cannot be reached: the browser's own pages, a site Safari has not allowed
    }
  }
  /** Signed in there? The setup reads the API with the user's session; on a login page it would have
   *  nothing to read, and the tab arrives again once they are in. */
  async function signedIn(origin) {
    try {
      const r = await fetch(`${origin}/api/v1/users/self`, { credentials: 'include', headers: { accept: 'application/json' } });
      return r.status !== 401 && r.status !== 403;
    } catch {
      return true; // not knowable from here: let the setup find out
    }
  }
  /** A tab that has arrived on a Canvas page while the setup is still to do. Where the site is
   *  already allowed, the setup opens there now — once per site per run, so the page it loads does
   *  not open it again under the card. Where it is not, the page after install is told which site
   *  was found and offers to allow it: that press is the gesture a permission request needs, and
   *  the one thing the browser will not let happen on its own. Only the address is read. */
  const noticed = new Set();
  const noteFound = (f) => api.storage.local.set({ 'setup:found': { ...f, at: Date.now() } });
  /** The setup, opened on a tab of an allowed site: once per site per run (a press on the page after
   *  install may force it), so the page it loads does not open it again under the card. The site's
   *  scripts are registered first where they are not built in — a site allowed from Safari's own
   *  settings has none until then. The tab is brought forward, window and all; what goes wrong on
   *  the way is written down for the page after install to say, rather than swallowed. */
  async function startSetupOn(origin, tabId, { force = false } = {}) {
    if (!force && noticed.has(origin)) return false;
    if (!(await signedIn(origin))) { // the login page: the tab arrives again once they are in
      await noteFound({ origin, tabId, granted: true, error: 'sign in to Canvas there first' });
      return false;
    }
    noticed.add(origin);
    if (!/\.instructure\.com$/i.test(new URL(origin).hostname)) {
      const r = await registerDomain(origin);
      if (r && r.ok === false) { await noteFound({ origin, tabId, granted: true, error: r.message || 'its scripts could not be registered' }); return false; }
    }
    await S.update({ appearance: { skin: true } });
    try {
      const t = await api.tabs.update(tabId, { url: `${origin}/?bcv=setup`, active: true });
      if (t && t.windowId != null && api.windows?.update) await api.windows.update(t.windowId, { focused: true }).catch(() => {});
    } catch (e) {
      await noteFound({ origin, tabId, granted: true, error: `the tab could not be opened (${e?.message || e})` });
      return false;
    }
    await noteFound({ origin, tabId, granted: true });
    return true;
  }
  /** What the page after install found, checked again as that page opens: the tab it was found on
   *  may be long gone, and a note that outlives its tab reads as a Canvas that is not there. A live
   *  allowed tab has the setup opened on it now; a live tab not yet allowed is named; nothing live
   *  is nothing found. */
  async function checkFound() {
    try {
      const all = await api.storage.local.get(['setup:found', 'setup:done']);
      if (all['setup:done']) return { done: true };
      const f = all['setup:found'];
      if (!f || !f.origin) return { none: true };
      let tab = null;
      if (f.tabId != null) { try { tab = await api.tabs.get(f.tabId); } catch { tab = null; } }
      if (!tab || !tab.url || new URL(tab.url).origin !== f.origin) {
        const tabs = await api.tabs.query({ url: `${f.origin}/*` }).catch(() => []);
        tab = (tabs || []).find((t) => t.url) || null;
      }
      if (!tab) { await api.storage.local.remove('setup:found'); return { none: true }; }
      if (!(await siteGranted(f.origin))) {
        await noteFound({ origin: f.origin, tabId: tab.id, granted: false });
      } else if (new URL(tab.url).searchParams.get('bcv') !== 'setup') {
        await startSetupOn(f.origin, tab.id, { force: true });
      }
      return { found: (await api.storage.local.get('setup:found'))['setup:found'] || null };
    } catch (e) {
      return { none: true, error: e?.message || String(e) };
    }
  }
  async function noticeTab(tab) {
    try {
      if (!tab || !tab.url || tab.id == null || ourPage(tab.url)) return;
      const url = new URL(tab.url);
      if (!/^https?:$/.test(url.protocol) || url.searchParams.get('bcv') === 'setup') return;
      const state = await api.storage.local.get('setup:done');
      if (state['setup:done']) return;
      const origin = url.origin;
      const granted = await siteGranted(origin);
      // Is it Canvas? By its address where that says so. Where the browser allows every site the
      // page itself is asked (a tiny look at the DOM, nothing kept); where it allows this one site,
      // the user allowed it for Canvas, and that is the user's own word for it.
      let canvas = looksLikeCanvas(url, tab);
      if (!canvas && granted) canvas = (await broadGrant()) ? await sniff(tab.id) : true;
      if (!canvas) return;
      if (!granted) { await noteFound({ origin, tabId: tab.id, granted: false }); return; } // Safari, or a Chrome kept off this site: the page after install offers the press
      await startSetupOn(origin, tab.id);
    } catch {
      /* a tab that closed, or one we may not read */
    }
  }
  // a page that has loaded, not an address that moved in place: the setup card cleans ?bcv=setup
  // off the address as it opens, and that must not read as a fresh arrival
  // (a favicon or a title arriving is not an address moving, and the favicon is what tells a
  // school's own address apart)
  if (api.tabs?.onUpdated) api.tabs.onUpdated.addListener((tabId, info, tab) => { if (info.status === 'complete' || info.favIconUrl || info.title) noticeTab(tab); });
  /** Every open tab looked at once: a Canvas that was open before the page after install was.
   *  (Chrome only: Safari asks the user about every open site before it will say their addresses.) */
  async function scanTabs() {
    const tabs = await api.tabs.query({}).catch(() => []);
    for (const t of tabs || []) await noticeTab(t);
    return { ok: true };
  }
  /** A site allowed by whatever means — this page, the popup, or the browser's own settings, which
   *  is how Safari does it: its scripts registered, and the setup opened on a tab of it if the
   *  setup is still to do. */
  async function adoptOrigins(patterns, { open = true } = {}) {
    for (const pat of patterns || []) {
      if (/\*\./.test(pat) || /^\*:\/\/\*\//.test(pat)) continue; // a wildcard host: the built-in one, or everything
      const origin = normalizeOrigin(pat.replace(/^\*:\/\//, 'https://').replace(/\/\*$/, ''));
      if (!origin || /\.instructure\.com$/i.test(new URL(origin).hostname)) continue;
      try {
        const settings = await S.get();
        if (!settings.domains.includes(origin)) await registerDomain(origin);
        if (!open) continue;
        const state = await api.storage.local.get('setup:done');
        if (state['setup:done']) continue;
        const tabs = await api.tabs.query({ url: `${origin}/*` }).catch(() => []);
        const tab = (tabs || []).find((t) => t.url && new URL(t.url).searchParams.get('bcv') !== 'setup') || (tabs || [])[0];
        if (tab && tab.id != null) await startSetupOn(origin, tab.id);
      } catch {
        /* the next one */
      }
    }
  }
  if (api.permissions && api.permissions.onAdded) api.permissions.onAdded.addListener((added) => adoptOrigins(added && added.origins));
  /** Sites the browser already allows that this extension never registered — allowed from the
   *  browser's own settings, on Safari. Read on every wake, so a site allowed there works after. */
  async function adoptGranted() {
    try {
      const all = await api.permissions.getAll();
      await adoptOrigins(all && all.origins, { open: false });
    } catch {
      /* no permissions API, or nothing to adopt */
    }
  }

  // ---- lifecycle ----------------------------------------------------------
  api.runtime.onInstalled.addListener(async (details) => {
    await ensureDomains({ force: true });
    if (details.reason === 'install') await offerSetup();
  });
  if (api.runtime.onStartup) api.runtime.onStartup.addListener(() => ensureDomains());
  // Every time the background wakes: cheap check, repairs stale registrations
  // even when onInstalled/onStartup never fired (Safari rebuilds, reloads).
  ensureDomains().then(adoptGranted);
  offerSetup();
  BCV.background = { offerSetup, ensureDomains, looksLikeCanvas, sniffFn, forgetNoticed: () => noticed.clear() }; // the harness drives these directly
})();
