/* Toolbar popup: quick toggles, panel shortcuts, enable-on-this-site. */
(async function () {
  const BCV = self.BCV;
  const api = BCV.api;
  const S = BCV.settings;
  const $ = (id) => document.getElementById(id);

  const send = (msg) => new Promise((resolve) => {
    try {
      const p = api.runtime.sendMessage(msg, (r) => resolve(r));
      if (p && typeof p.then === 'function') p.then(resolve, () => resolve(null));
    } catch {
      resolve(null);
    }
  });
  const sendToTab = (tabId, msg) => new Promise((resolve) => {
    try {
      const p = api.tabs.sendMessage(tabId, msg, (r) => resolve(r));
      if (p && typeof p.then === 'function') p.then(resolve, () => resolve(null));
    } catch {
      resolve(null);
    }
  });

  // Theme options
  const themeSel = $('theme');
  for (const [id, t] of Object.entries(S.THEMES)) themeSel.append(new Option(t.label, id));

  let settings = await S.get();
  const bind = () => {
    $('darkMode').value = settings.appearance.darkMode;
    themeSel.value = settings.appearance.theme;
    $('skin').checked = settings.appearance.skin !== false;
    $('minimal').checked = settings.appearance.minimal;
    $('compact').checked = settings.appearance.density === 'compact';
    $('hideRightSidebar').checked = settings.clean.hideRightSidebar;
    $('reminders').checked = settings.dueDates.reminders;
  };
  bind();

  $('darkMode').addEventListener('change', (e) => S.update({ appearance: { darkMode: e.target.value } }));
  themeSel.addEventListener('change', (e) => S.update({ appearance: { theme: e.target.value } }));
  $('skin').addEventListener('change', (e) => S.update({ appearance: { skin: e.target.checked } }));
  $('minimal').addEventListener('change', (e) => S.update({ appearance: { minimal: e.target.checked } }));
  $('compact').addEventListener('change', (e) => S.update({ appearance: { density: e.target.checked ? 'compact' : 'comfortable' } }));
  $('hideRightSidebar').addEventListener('change', (e) => S.update({ clean: { hideRightSidebar: e.target.checked } }));
  $('reminders').addEventListener('change', (e) => S.update({ dueDates: { reminders: e.target.checked } }));
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

  // Smart status
  const st = await send({ type: 'providerStatus' });
  $('smart-status').textContent = st?.configured ? `Smart features: ${st.label} · ${st.model}` : 'Smart features: add a key in Settings';

  // Current tab
  let tab = null;
  try {
    [tab] = await api.tabs.query({ active: true, currentWindow: true });
  } catch {
    tab = null;
  }
  const status = $('status');
  const actions = $('page-actions');
  let onCanvas = false;
  if (tab?.id) {
    const pong = await sendToTab(tab.id, { type: 'ping' });
    onCanvas = !!pong?.ok && !!pong.canvas;
  }
  let host = '';
  try {
    host = tab?.url ? new URL(tab.url).host : '';
  } catch {
    host = '';
  }
  if (onCanvas) {
    status.textContent = `Active on ${host}`;
  } else {
    status.textContent = host ? `Not active on ${host}` : 'Open Canvas in a tab to use page tools';
    actions.hidden = true;
    if (tab?.url && /^https?:/.test(tab.url) && host && !/instructure\.com$/.test(host)) {
      $('enable-card').hidden = false;
      $('enable-host').textContent = host;
    }
  }

  const panel = (name) => async () => {
    if (!tab?.id) return;
    await sendToTab(tab.id, name === 'palette' ? { type: 'openPalette' } : name === 'help' ? { type: 'openHelp' } : { type: 'togglePanel', panel: name });
    window.close();
  };
  $('open-todo').addEventListener('click', panel('todo'));
  $('open-smart').addEventListener('click', panel('smart'));
  $('open-palette').addEventListener('click', panel('palette'));
  $('open-help').addEventListener('click', panel('help'));

  $('enable-site').addEventListener('click', async () => {
    const msg = $('enable-msg');
    let origin;
    try {
      origin = new URL(tab.url).origin;
    } catch {
      return;
    }
    let granted = false;
    try {
      granted = await api.permissions.request({ origins: [`${origin}/*`] });
    } catch (e) {
      msg.textContent = `Could not request permission: ${e.message}`;
      return;
    }
    if (!granted) {
      msg.textContent = 'Permission was not granted.';
      return;
    }
    const r = await send({ type: 'registerDomain', origin });
    if (r?.ok) {
      msg.textContent = 'Enabled. Reloading the tab…';
      try {
        await api.tabs.reload(tab.id);
      } catch {
        /* ignore */
      }
      setTimeout(() => window.close(), 400);
    } else {
      msg.textContent = r?.message || 'Could not enable on this site.';
    }
  });
})();
