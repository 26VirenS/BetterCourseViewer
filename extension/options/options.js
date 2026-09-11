/* Options page: binds every control to the settings object (auto-saves). */
(async function () {
  const BCV = self.BCV;
  const api = BCV.api;
  const S = BCV.settings;
  const $ = (id) => document.getElementById(id);

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
  let toastTimer = null;
  const toast = (text = 'Saved') => {
    const el = $('toast');
    el.textContent = text;
    el.hidden = false;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => (el.hidden = true), 1400);
  };
  const save = async (patch) => {
    settings = await S.update(patch);
    toast();
    renderStatus();
  };

  try {
    $('version').textContent = `v${api.runtime.getManifest().version}`;
  } catch {
    /* ignore */
  }

  function showSection(id) {
    document.querySelectorAll('.section').forEach((s) => s.classList.toggle('is-active', s.id === id));
    document.querySelectorAll('.navlink').forEach((a) => a.classList.toggle('is-active', a.dataset.section === id));
  }
  document.querySelectorAll('.navlink').forEach((a) => a.addEventListener('click', (e) => {
    e.preventDefault();
    history.replaceState(null, '', `#${a.dataset.section}`);
    showSection(a.dataset.section);
  }));
  if (location.hash && document.getElementById(location.hash.slice(1))) showSection(location.hash.slice(1));

  const BINDINGS = [
    ['claudeKey', 'smart.claudeKey', 'text'],
    ['openaiKey', 'smart.openaiKey', 'text'],
    ['claudeModel', 'smart.claudeModel', 'text'],
    ['openaiModel', 'smart.openaiModel', 'text'],
    ['preferred', 'smart.preferred', 'select'],
    ['depth', 'smart.depth', 'select'],
    ['includePageContext', 'smart.includePageContext', 'check'],
    ['persistChat', 'smart.persistChat', 'check'],
    ['skin', 'appearance.skin', 'check'],
    ['darkMode', 'appearance.darkMode', 'select'],
    ['siteName', 'appearance.siteName', 'text'],
    ['logoUrl', 'appearance.logoUrl', 'text'],
  ];
  const getPath = (obj, path) => path.split('.').reduce((o, k) => (o == null ? undefined : o[k]), obj);
  const patchFor = (path, value) => path.split('.').reverse().reduce((acc, k) => ({ [k]: acc }), value);

  function bindAll() {
    for (const [id, path, type] of BINDINGS) {
      const el = $(id);
      if (!el) continue;
      const v = getPath(settings, path);
      if (type === 'check') el.checked = !!v;
      else el.value = v ?? '';
    }
    renderDomains();
    renderStatus();
  }
  for (const [id, path, type] of BINDINGS) {
    const el = $(id);
    if (!el) continue;
    el.addEventListener('change', () => {
      const value = type === 'check' ? el.checked : el.value.trim();
      save(patchFor(path, value));
    });
  }

  document.querySelectorAll('[data-reveal]').forEach((btn) => btn.addEventListener('click', () => {
    const input = $(btn.dataset.reveal);
    const show = input.type === 'password';
    input.type = show ? 'text' : 'password';
    btn.textContent = show ? 'Hide' : 'Show';
  }));

  async function test(provider, inputId, resultId, btnId) {
    const key = $(inputId).value.trim();
    const out = $(resultId);
    const btn = $(btnId);
    btn.disabled = true;
    out.className = 'result';
    out.textContent = 'Testing…';
    await save(patchFor(provider === 'openai' ? 'smart.openaiKey' : 'smart.claudeKey', key));
    const r = await send({ type: 'testKey', provider, key });
    out.textContent = r?.message || 'No response from the extension.';
    out.className = `result ${r?.ok ? 'ok' : 'err'}`;
    btn.disabled = false;
  }
  $('testClaude').addEventListener('click', () => test('claude', 'claudeKey', 'claudeResult', 'testClaude'));
  $('testOpenAI').addEventListener('click', () => test('openai', 'openaiKey', 'openaiResult', 'testOpenAI'));

  function renderStatus() {
    const provider = S.resolveProvider(settings.smart);
    const el = $('smartStatus');
    if (!provider) el.textContent = 'The smart panel stays quiet until you add a key.';
    else {
      const both = settings.smart.claudeKey.trim() && settings.smart.openaiKey.trim();
      el.textContent = `The smart panel will use ${S.providerLabel(provider)} (${S.modelFor(settings.smart, provider)})${both ? ' because both keys are set and it is your preferred provider' : ''}.`;
    }
  }

  // ---- domains ----------------------------------------------------------------
  function renderDomains() {
    const ul = $('domains');
    ul.replaceChildren();
    const li = document.createElement('li');
    li.innerHTML = '<span>*.instructure.com</span><span class="muted">built in</span>';
    ul.append(li);
    for (const origin of settings.domains || []) {
      const row = document.createElement('li');
      const name = document.createElement('span');
      name.textContent = origin;
      const rm = document.createElement('button');
      rm.type = 'button';
      rm.className = 'btn btn--small';
      rm.textContent = 'Remove';
      rm.addEventListener('click', async () => {
        await send({ type: 'unregisterDomain', origin });
        settings = await S.get();
        renderDomains();
        toast('Removed');
      });
      row.append(name, rm);
      ul.append(row);
    }
  }
  /** Turns Simpl Courses on for a Canvas address: built in for *.instructure.com, otherwise the
   *  browser asks for the site once and its scripts are registered. Returns the origin, or null. */
  async function enableSite(raw, msg) {
    let origin;
    try {
      origin = new URL(/^https?:\/\//i.test(raw) ? raw : `https://${raw}`).origin;
    } catch {
      msg.textContent = 'That does not look like a valid address.';
      return null;
    }
    if (/\.instructure\.com$/i.test(new URL(origin).hostname)) {
      msg.textContent = `${new URL(origin).hostname} is on already: every *.instructure.com site is built in.`;
      return origin;
    }
    msg.textContent = 'Asking for permission…';
    let granted = false;
    try {
      granted = await api.permissions.request({ origins: [`${origin}/*`] });
    } catch (e) {
      msg.textContent = `Permission request failed: ${e?.message || e}`;
      return null;
    }
    if (!granted) {
      msg.textContent = 'Permission was not granted.';
      return null;
    }
    const r = await send({ type: 'registerDomain', origin });
    msg.textContent = r?.ok ? `Enabled on ${origin}.` : (r?.message || 'Could not enable that site.');
    settings = await S.get();
    renderDomains();
    return r?.ok ? origin : null;
  }
  $('addDomain').addEventListener('click', async () => {
    const raw = $('newDomain').value.trim();
    if (!raw) return;
    const origin = await enableSite(raw, $('domainMsg'));
    if (origin) {
      $('domainMsg').textContent += ' Reload that tab.';
      $('newDomain').value = '';
    }
  });
  // the guided setup runs in its own page
  $('openSetup').addEventListener('click', async () => {
    try {
      await api.tabs.create({ url: api.runtime.getURL('setup/setup.html') });
    } catch {
      location.href = api.runtime.getURL('setup/setup.html');
    }
  });

  // ---- data ------------------------------------------------------------------------
  $('clearCache').addEventListener('click', async () => {
    const all = await api.storage.local.get(null);
    const keys = Object.keys(all).filter((k) => k.startsWith('cache:') || k.startsWith('smart:') || k.startsWith('prefs:'));
    await api.storage.local.remove(keys);
    toast(`Cleared ${keys.length} cached entries`);
  });
  $('exportSettings').addEventListener('click', () => {
    const copy = JSON.parse(JSON.stringify(settings));
    copy.smart.claudeKey = '';
    copy.smart.openaiKey = '';
    const blob = new Blob([JSON.stringify(copy, null, 2)], { type: 'application/json' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = 'bettercourseviewer-settings.json';
    a.click();
    setTimeout(() => URL.revokeObjectURL(a.href), 1000);
  });
  $('importSettings').addEventListener('click', () => $('importFile').click());
  $('importFile').addEventListener('change', async (e) => {
    const file = e.target.files?.[0];
    if (!file) return;
    try {
      const data = JSON.parse(await file.text());
      const keep = { smart: { claudeKey: settings.smart.claudeKey, openaiKey: settings.smart.openaiKey } };
      settings = await S.replace(S.deepMerge(data, keep));
      bindAll();
      toast('Imported');
    } catch {
      toast('Could not read that file');
    }
    e.target.value = '';
  });
  $('resetSettings').addEventListener('click', async () => {
    if (!confirm('Reset all settings to their defaults? Your API keys will be removed too.')) return;
    settings = await S.replace({});
    bindAll();
    toast('Reset');
  });

  bindAll();
  S.onChange((s) => {
    settings = s;
    bindAll();
  });
})();
