/* Toolbar popup: skin + appearance switches, enable-on-this-site. */
(async function () {
  const BCV = self.BCV;
  const api = BCV.api;
  try {
    document.getElementById('version').textContent = `v${api.runtime.getManifest().version}`;
  } catch {
    /* ignore */
  }
  const S = BCV.settings;
  const $ = (id) => document.getElementById(id);

  // Promise-only messaging (Safari rejects callback arguments on these APIs).
  const send = (msg) => new Promise((resolve) => {
    try {
      const p = api.runtime.sendMessage(msg);
      if (p && typeof p.then === 'function') p.then(resolve, () => resolve(null));
      else resolve(p ?? null);
    } catch {
      resolve(null);
    }
  });

  let settings = await S.get();
  const bind = () => {
    $('skin').checked = settings.appearance.skin !== false;
    $('darkMode').value = settings.appearance.darkMode;
  };
  bind();
  $('skin').addEventListener('change', (e) => S.update({ appearance: { skin: e.target.checked } }));
  $('darkMode').addEventListener('change', (e) => S.update({ appearance: { darkMode: e.target.value } }));
  S.onChange((s) => {
    settings = s;
    bind();
  });

  const openOptions = (e) => {
    e?.preventDefault();
    send({ type: 'openOptions' });
    window.close();
  };
  $('open-settings').addEventListener('click', openOptions);
  $('foot-settings').addEventListener('click', openOptions);
  // the guided setup runs on the Canvas page itself; from elsewhere the settings page asks for the site first
  let setupOrigin = null;
  let setupTab = null;
  $('foot-setup').addEventListener('click', async (e) => {
    e.preventDefault();
    try {
      if (setupOrigin && setupTab?.id !== undefined) await api.tabs.update(setupTab.id, { url: `${setupOrigin}/?bcv=setup` });
      else await api.tabs.create({ url: api.runtime.getURL('options/options.html#setup') });
    } catch {
      /* ignore */
    }
    window.close();
  });

  // smart status
  send({ type: 'providerStatus' }).then((s) => {
    $('smart-status').textContent = s?.configured ? `Smart panel: ${s.label}` : 'Smart panel: add a key in Settings';
  });

  // which tab are we on?
  let tab = null;
  try {
    const tabs = await api.tabs.query({ active: true, currentWindow: true });
    tab = tabs?.[0] || null;
  } catch {
    /* ignore */
  }
  const url = tab?.url ? new URL(tab.url) : null;
  const status = $('status');
  if (!url || !/^https?:$/.test(url.protocol)) {
    status.textContent = 'Open a Canvas page to use it.';
    return;
  }
  const origin = url.origin;
  const builtIn = /\.instructure\.com$/i.test(url.hostname);
  const saved = (settings.domains || []).includes(origin);
  let granted = builtIn || saved;
  if (!granted) {
    try {
      granted = await api.permissions.contains({ origins: [`${origin}/*`] });
    } catch {
      granted = false;
    }
  }
  if (granted) {
    status.textContent = `On for ${url.hostname}`;
    setupOrigin = origin; // Guided setup opens on this very page
    setupTab = tab;
    // Self-heal: a saved custom site re-registers its scripts from the
    // current build every time the popup opens (cheap, idempotent).
    if (!builtIn) {
      const r = await send({ type: 'registerDomain', origin });
      if (r && r.ok === false) status.textContent = `Saved for ${url.hostname}, but: ${r.message}`;
    }
    return;
  }
  status.textContent = `Not enabled on ${url.hostname}`;
  $('enable-card').hidden = false;
  $('enable-host').textContent = url.hostname;
  $('enable-site').addEventListener('click', async () => {
    const msg = $('enable-msg');
    msg.textContent = 'Asking for permission…';
    let ok = false;
    try {
      ok = await api.permissions.request({ origins: [`${origin}/*`] });
    } catch (e) {
      msg.textContent = `Permission request failed: ${e?.message || e}`;
      return;
    }
    if (!ok) {
      msg.textContent = 'Permission was not granted.';
      return;
    }
    const r = await send({ type: 'registerDomain', origin });
    if (r?.ok) {
      msg.textContent = 'Enabled. Reloading the page…';
      try {
        await api.tabs.reload(tab.id);
      } catch {
        /* ignore */
      }
      setTimeout(() => window.close(), 600);
    } else msg.textContent = r?.message || 'Could not enable this site.';
  });
})();
