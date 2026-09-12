/* Simpl Courses background script.
 * Runs as a service worker (Safari 16.4+, Chrome) or a non-persistent
 * background page (older Safari, Firefox). Responsibilities:
 *  - stream smart-assistant replies to content scripts over a port
 *  - validate API keys for the options page
 *  - keep the toolbar badge in sync with due-soon counts
 *  - register content scripts for user-added Canvas domains
 */
if (typeof importScripts === 'function' && !self.BCV?.providers) {
  importScripts('lib/settings.js', 'lib/providers.js');
}

(function () {
  const BCV = self.BCV;
  const api = BCV.api;
  const S = BCV.settings;
  const P = BCV.providers;

  const streams = new Map(); // request id -> AbortController

  // ---- smart assistant streaming ----------------------------------------
  api.runtime.onConnect.addListener((port) => {
    if (port.name !== 'bcv-smart') return;
    const owned = new Set();
    port.onMessage.addListener(async (msg) => {
      if (!msg || typeof msg !== 'object') return;
      if (msg.type === 'abort') {
        streams.get(msg.id)?.abort();
        streams.delete(msg.id);
        return;
      }
      if (msg.type !== 'chat') return;
      const id = msg.id;
      const controller = new AbortController();
      streams.set(id, controller);
      owned.add(id);
      const post = (m) => {
        try {
          port.postMessage(m);
        } catch {
          controller.abort();
        }
      };
      try {
        const settings = await S.get();
        const smart = settings.smart;
        const provider = S.resolveProvider(smart);
        if (!provider) {
          post({ type: 'error', id, message: 'Add a Claude or ChatGPT key in Settings to turn on smart features.' });
          return;
        }
        const model = S.modelFor(smart, provider);
        const common = {
          apiKey: provider === 'openai' ? smart.openaiKey.trim() : smart.claudeKey.trim(),
          model,
          system: msg.system || '',
          messages: Array.isArray(msg.messages) ? msg.messages : [],
          depth: smart.depth,
          signal: controller.signal,
          onStart: (info) => post({ type: 'start', id, provider, model: info.model || model }),
          onDelta: (text) => post({ type: 'delta', id, text }),
        };
        const result = provider === 'openai' ? await P.streamOpenAI(common) : await P.streamClaude(common);
        post({ type: 'done', id, provider, model: result.model, stopReason: result.stopReason });
      } catch (e) {
        if (e?.name === 'AbortError') post({ type: 'done', id, aborted: true });
        else post({ type: 'error', id, message: e?.message || 'Something went wrong.' });
      } finally {
        streams.delete(id);
        owned.delete(id);
      }
    });
    port.onDisconnect.addListener(() => {
      for (const id of owned) {
        streams.get(id)?.abort();
        streams.delete(id);
      }
      owned.clear();
    });
  });

  // ---- one-shot messages --------------------------------------------------
  api.runtime.onMessage.addListener((msg, sender, sendResponse) => {
    if (!msg || typeof msg !== 'object') return false;
    const reply = (p) => Promise.resolve(p).then(sendResponse, (e) => sendResponse({ ok: false, message: e?.message || String(e) }));
    switch (msg.type) {
      case 'providerStatus':
        reply(providerStatus());
        return true;
      case 'testKey':
        reply(P.testKey(msg.provider, msg.key));
        return true;
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
      default:
        return false;
    }
  });

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

  async function providerStatus() {
    const settings = await S.get();
    const provider = S.resolveProvider(settings.smart);
    return {
      configured: !!provider,
      provider,
      label: S.providerLabel(provider),
      model: provider ? S.modelFor(settings.smart, provider) : null,
      enabled: settings.smart.enabled !== false,
    };
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
  const SETUP_FLOW = 2;
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
  /** The page that says how to start, once: on install, and on the first run of a build whose
   *  setup flow never showed it (an update, Safari enabling the extension without an install event). */
  async function offerSetup() {
    try {
      await migrateSetup();
      const flag = await api.storage.local.get('setup:offered');
      if (flag && flag['setup:offered']) return;
      await api.storage.local.set({ 'setup:offered': true });
      await api.tabs.create({ url: api.runtime.getURL('setup/setup.html') });
    } catch {
      /* ignore */
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

  // ---- lifecycle ----------------------------------------------------------
  api.runtime.onInstalled.addListener(async (details) => {
    await ensureDomains({ force: true });
    if (details.reason === 'install') await offerSetup();
  });
  if (api.runtime.onStartup) api.runtime.onStartup.addListener(() => ensureDomains());
  // Every time the background wakes: cheap check, repairs stale registrations
  // even when onInstalled/onStartup never fired (Safari rebuilds, reloads).
  ensureDomains();
  offerSetup();
})();
