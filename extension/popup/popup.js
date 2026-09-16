/* Toolbar popup: skin + appearance switches, enable-on-this-site. Before the guided setup has run
 * it is nothing but a Set up button, which asks for the site (from the click) and opens the setup
 * over the Canvas page in the current tab. */
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

  const setupDone = async () => {
    try {
      const flag = await api.storage.local.get('setup:done');
      return !!(flag && flag['setup:done']); // written only by the setup card's last step
    } catch {
      return true; // storage unreadable: never hide the switches over it
    }
  };

  let settings = await S.get();
  const bind = () => {
    $('skin').checked = settings.appearance.skin !== false;
    $('darkMode').value = settings.appearance.darkMode;
  };
  bind();
  // Saving is not enough on its own: a content script cannot count on hearing a storage change (in
  // Safari it often never fires for one written here), so the background is asked to hand the new
  // settings to the open tabs. Without this the look switch left the page it was pressed for
  // exactly as it was until it was reloaded by hand.
  const push = () => api.runtime.sendMessage({ type: 'pushSettings' }).catch(() => {});
  const saveAppearance = async (patch) => { await S.update(patch); await push(); };
  $('skin').addEventListener('change', (e) => saveAppearance({ appearance: { skin: e.target.checked } }));
  $('darkMode').addEventListener('change', (e) => saveAppearance({ appearance: { darkMode: e.target.value } }));
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

  // which tab are we on?
  let tab = null;
  try {
    const tabs = await api.tabs.query({ active: true, currentWindow: true });
    tab = tabs?.[0] || null;
  } catch {
    /* ignore */
  }
  const url = tab?.url ? new URL(tab.url) : null;
  const onWeb = !!url && /^https?:$/.test(url.protocol);
  const origin = onWeb ? url.origin : '';
  const builtIn = onWeb && /\.instructure\.com$/i.test(url.hostname);
  const saved = onWeb && (settings.domains || []).includes(origin);
  let granted = builtIn || saved;
  if (onWeb && !granted) {
    try {
      granted = await api.permissions.contains({ origins: [`${origin}/*`] });
    } catch {
      granted = false;
    }
  }

  /** Permission for this site (from the click), its scripts registered, then the setup over the page. */
  const askSite = async (msg, next) => {
    if (!granted) {
      msg.textContent = 'Asking for permission…';
      // Chrome closes this popup when its permission dialog opens, so the background finishes the
      // job from this note once the permission lands (see continuePending in background.js). The
      // note is written without waiting on purpose: permissions.request() has to be called while
      // the press is still the browser's idea of a user gesture, and awaiting anything first loses
      // it — Safari then refuses with "Must be called during a user gesture".
      api.storage.local.set({ 'setup:pending': { origin, tabId: tab?.id ?? null, next, at: Date.now() } }).catch(() => {});
      let ok = false;
      try {
        ok = await api.permissions.request({ origins: [`${origin}/*`] });
      } catch (e) {
        await api.storage.local.remove('setup:pending').catch(() => {});
        msg.textContent = `Permission request failed: ${e?.message || e}`;
        return false;
      }
      await api.storage.local.remove('setup:pending').catch(() => {}); // still here: this popup finishes it
      if (!ok) {
        msg.textContent = 'Permission was not granted. Simpl Courses can only run on a site you allow.';
        return false;
      }
      granted = true;
    }
    if (!builtIn) {
      const r = await send({ type: 'registerDomain', origin });
      if (r && r.ok === false) {
        msg.textContent = r.message || 'Could not enable this site.';
        return false;
      }
    }
    return true;
  };
  /** Does this tab answer like Canvas, signed in? Only a definite "no" stops the setup. */
  const canvasHere = async (msg) => {
    msg.textContent = 'Checking Canvas…';
    let r;
    try {
      r = await fetch(`${origin}/api/v1/users/self`, { credentials: 'include', headers: { Accept: 'application/json' } });
    } catch {
      return true; // unreachable from here (Safari keeps some fetches to the page): let the page decide
    }
    if (r.status === 401 || r.status === 403) {
      msg.textContent = 'Sign in to Canvas on this tab first, then press Set up again.';
      return false;
    }
    if (!r.ok) {
      msg.textContent = 'This does not look like a Canvas page. Open your Canvas courses page and try again.';
      return false;
    }
    try {
      JSON.parse((await r.text()).replace(/^while\(1\);/, ''));
    } catch {
      msg.textContent = 'This does not look like a Canvas page. Open your Canvas courses page and try again.';
      return false;
    }
    return true;
  };
  /** The guided setup over the Canvas page in this tab (the interface on), else the page that says how. */
  const openSetup = async (msg) => {
    msg.hidden = false;
    if (!onWeb) {
      msg.textContent = 'Open your Canvas courses page first, then press Set up.';
      return;
    }
    if (!(await askSite(msg, 'setup'))) return;
    if (!(await canvasHere(msg))) return;
    msg.textContent = 'Opening the setup…';
    await S.update({ appearance: { skin: true } });
    try {
      await api.tabs.update(tab.id, { url: `${origin}/?bcv=setup` });
    } catch {
      msg.textContent = 'The tab could not be opened. Add ?bcv=setup to your Canvas address to start.';
      return;
    }
    setTimeout(() => window.close(), 300);
  };
  $('start-setup').addEventListener('click', () => openSetup($('setup-msg')));
  $('foot-setup').addEventListener('click', async (e) => {
    e.preventDefault();
    if (onWeb && granted) {
      await openSetup($('foot-msg'));
      return;
    }
    try {
      await api.tabs.create({ url: api.runtime.getURL('setup/setup.html') });
    } catch {
      /* ignore */
    }
    window.close();
  });

  const status = $('status');
  // Until the guided setup has been finished the popup shows nothing but the
  // Set up button.
  if (!(await setupDone())) {
    document.body.classList.add('is-fresh');
    status.textContent = onWeb ? `Not set up yet · ${url.hostname}` : 'Not set up yet';
    $('setup-card').hidden = false;
    $('setup-hint').textContent = onWeb ? (granted ? 'Already allowed on this site: Set up runs right here, over this page.' : 'Set up asks to run on this site, then continues here.') : 'Open your Canvas courses page first.';
    return;
  }


  if (!onWeb) {
    status.textContent = 'Open a Canvas page to use it.';
    return;
  }
  if (granted) {
    status.textContent = `On for ${url.hostname}`;
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
    if (!(await askSite(msg, 'reload'))) return;
    msg.textContent = 'Enabled. Reloading the page…';
    try {
      await api.tabs.reload(tab.id);
    } catch {
      /* ignore */
    }
    setTimeout(() => window.close(), 600);
  });
})();
