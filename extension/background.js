/* BetterCourseViewer background script.
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
      default:
        return false;
    }
  });

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
      const existing = await api.scripting.getRegisteredContentScripts({ ids: scripts.map((s) => s.id) }).catch(() => []);
      const existingIds = new Set((existing || []).map((s) => s.id));
      const toRegister = scripts.filter((s) => !existingIds.has(s.id));
      const toUpdate = scripts.filter((s) => existingIds.has(s.id));
      if (toRegister.length) await api.scripting.registerContentScripts(toRegister);
      if (toUpdate.length && api.scripting.updateContentScripts) await api.scripting.updateContentScripts(toUpdate);
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

  /** Re-register scripts for saved domains (in case registrations were lost). */
  async function ensureDomains() {
    try {
      const settings = await S.get();
      for (const origin of settings.domains || []) {
        const ok = await api.permissions.contains({ origins: [`${origin}/*`] }).catch(() => true);
        if (ok) await registerDomain(origin);
      }
    } catch {
      /* ignore */
    }
  }

  // ---- lifecycle ----------------------------------------------------------
  api.runtime.onInstalled.addListener(async (details) => {
    await ensureDomains();
    if (details.reason === 'install') {
      try {
        await api.runtime.openOptionsPage();
      } catch {
        /* ignore */
      }
    }
  });
  if (api.runtime.onStartup) api.runtime.onStartup.addListener(ensureDomains);
})();
