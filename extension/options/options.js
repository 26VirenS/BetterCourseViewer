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

  // ---- sections ------------------------------------------------------------
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

  // ---- generic bindings ----------------------------------------------------
  // [elementId, path, type]
  const BINDINGS = [
    ['claudeKey', 'smart.claudeKey', 'text'],
    ['openaiKey', 'smart.openaiKey', 'text'],
    ['claudeModel', 'smart.claudeModel', 'text'],
    ['openaiModel', 'smart.openaiModel', 'text'],
    ['preferred', 'smart.preferred', 'select'],
    ['depth', 'smart.depth', 'select'],
    ['includePageContext', 'smart.includePageContext', 'check'],
    ['persistChat', 'smart.persistChat', 'check'],
    ['smartEnabled', 'smart.enabled', 'check'],
    ['skin', 'appearance.skin', 'check'],
    ['darkMode', 'appearance.darkMode', 'select'],
    ['font', 'appearance.font', 'select'],
    ['density', 'appearance.density', 'select'],
    ['contentWidth', 'appearance.contentWidth', 'select'],
    ['minimal', 'appearance.minimal', 'check'],
    ['hideRightSidebar', 'clean.hideRightSidebar', 'check'],
    ['compactCards', 'clean.compactCards', 'check'],
    ['hideCardImages', 'clean.hideCardImages', 'check'],
    ['hideCardActions', 'clean.hideCardActions', 'check'],
    ['hideCardTerm', 'clean.hideCardTerm', 'check'],
    ['hideAnnouncementsBanner', 'clean.hideAnnouncementsBanner', 'check'],
    ['cardColumns', 'clean.cardColumns', 'select'],
    ['hideFooter', 'clean.hideFooter', 'check'],
    ['hideBreadcrumbs', 'clean.hideBreadcrumbs', 'check'],
    ['hideHelpNav', 'clean.hideHelpNav', 'check'],
    ['hideHistoryNav', 'clean.hideHistoryNav', 'check'],
    ['hideGroupsNav', 'clean.hideGroupsNav', 'check'],
    ['hideToolNav', 'clean.hideToolNav', 'check'],
    ['highlight', 'dueDates.highlight', 'check'],
    ['dashboardPanel', 'dueDates.dashboardPanel', 'check'],
    ['badge', 'dueDates.badge', 'check'],
    ['remindersOn', 'dueDates.reminders', 'check'],
    ['reminderWindowHours', 'dueDates.reminderWindowHours', 'number'],
    ['lookaheadDays', 'dueDates.lookaheadDays', 'number'],
    ['todoEnabled', 'todo.enabled', 'check'],
    ['showCanvasItems', 'todo.showCanvasItems', 'check'],
    ['keyboardEnabled', 'keyboard.enabled', 'check'],
    ['palette', 'keyboard.palette', 'check'],
    ['openInNewTabButton', 'embeds.openInNewTabButton', 'check'],
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
    renderThemes();
    renderCustom();
    renderDomains();
    renderStatus();
  }

  for (const [id, path, type] of BINDINGS) {
    const el = $(id);
    if (!el) continue;
    const evt = type === 'text' ? 'change' : 'change';
    el.addEventListener(evt, () => {
      let value;
      if (type === 'check') value = el.checked;
      else if (type === 'number') value = Number(el.value);
      else value = el.value.trim();
      save(patchFor(path, value));
    });
  }

  // reveal buttons
  document.querySelectorAll('[data-reveal]').forEach((btn) => btn.addEventListener('click', () => {
    const input = $(btn.dataset.reveal);
    const show = input.type === 'password';
    input.type = show ? 'text' : 'password';
    btn.textContent = show ? 'Hide' : 'Show';
  }));

  // key tests
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
    if (!provider) {
      el.textContent = 'Smart features are off until you add a key.';
    } else {
      const both = settings.smart.claudeKey.trim() && settings.smart.openaiKey.trim();
      el.textContent = `Smart features will use ${S.providerLabel(provider)} (${S.modelFor(settings.smart, provider)})${both ? ' because both keys are set and it is your preferred provider' : ''}.`;
    }
  }

  // ---- themes ------------------------------------------------------------------
  function renderThemes() {
    const host = $('themes');
    host.replaceChildren();
    for (const [id, t] of Object.entries(S.THEMES)) {
      const accent = t.accent || (id === 'custom' ? settings.appearance.custom.accent : '#0374b5');
      const nav = t.nav || (id === 'custom' ? settings.appearance.custom.nav : '#394b58');
      const btn = document.createElement('button');
      btn.type = 'button';
      btn.className = `theme${settings.appearance.theme === id ? ' is-active' : ''}`;
      btn.innerHTML = `<div class="theme__swatch"><i style="background:${nav}"></i><i style="background:${accent}"></i><i style="background:${BCV.color.lighten(accent, 0.75)}"></i></div><div class="theme__name">${t.label}</div>`;
      btn.addEventListener('click', async () => {
        await save({ appearance: { theme: id } });
        renderThemes();
        renderCustom();
      });
      host.append(btn);
    }
  }
  function renderCustom() {
    const c = settings.appearance.custom;
    $('customTheme').hidden = settings.appearance.theme !== 'custom';
    $('c-accent').value = c.accent;
    $('c-link').value = c.link;
    $('c-nav').value = c.nav;
    $('c-navText').value = c.navText;
  }
  for (const [id, key] of [['c-accent', 'accent'], ['c-link', 'link'], ['c-nav', 'nav'], ['c-navText', 'navText']]) {
    $(id).addEventListener('change', (e) => save({ appearance: { custom: { [key]: e.target.value } } }));
  }

  // ---- domains ------------------------------------------------------------------
  function renderDomains() {
    const ul = $('domains');
    ul.replaceChildren();
    const li = document.createElement('li');
    li.innerHTML = '<span>*.instructure.com</span><span class="muted">built in</span>';
    ul.append(li);
    for (const origin of settings.domains || []) {
      const row = document.createElement('li');
      const label = document.createElement('span');
      label.textContent = origin;
      const remove = document.createElement('button');
      remove.className = 'btn btn--ghost';
      remove.textContent = 'Remove';
      remove.addEventListener('click', async () => {
        await send({ type: 'unregisterDomain', origin });
        settings = await S.get();
        renderDomains();
        toast('Removed');
      });
      row.append(label, remove);
      ul.append(row);
    }
  }
  $('addDomain').addEventListener('click', async () => {
    const msg = $('domainMsg');
    const raw = $('newDomain').value.trim();
    let origin;
    try {
      origin = new URL(/^https?:\/\//i.test(raw) ? raw : `https://${raw}`).origin;
    } catch {
      msg.textContent = 'Enter a valid address like https://canvas.myschool.edu';
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
      $('newDomain').value = '';
      msg.textContent = `Enabled on ${origin}. Reload any open Canvas tabs.`;
      settings = await S.get();
      renderDomains();
    } else {
      msg.textContent = r?.message || 'Could not add that site.';
    }
  });

  // ---- import / export / reset --------------------------------------------------
  $('exportSettings').addEventListener('click', () => {
    const copy = S.clone(settings);
    copy.smart.claudeKey = '';
    copy.smart.openaiKey = '';
    const blob = new Blob([JSON.stringify(copy, null, 2)], { type: 'application/json' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = 'bettercourseviewer-settings.json';
    a.click();
    setTimeout(() => URL.revokeObjectURL(a.href), 2000);
  });
  $('importSettings').addEventListener('click', () => $('importFile').click());
  $('importFile').addEventListener('change', async (e) => {
    const file = e.target.files?.[0];
    if (!file) return;
    try {
      const data = JSON.parse(await file.text());
      // keep existing keys unless the import has non-empty ones
      data.smart = { ...(data.smart || {}) };
      if (!data.smart.claudeKey) data.smart.claudeKey = settings.smart.claudeKey;
      if (!data.smart.openaiKey) data.smart.openaiKey = settings.smart.openaiKey;
      settings = await S.replace(data);
      bindAll();
      toast('Imported');
    } catch (err) {
      toast('Import failed');
    }
    e.target.value = '';
  });
  $('resetSettings').addEventListener('click', async () => {
    if (!confirm('Reset every setting to its default? Your API keys will be cleared too.')) return;
    settings = await S.replace({});
    bindAll();
    toast('Reset');
  });

  S.onChange((s) => {
    settings = s;
    bindAll();
  });
  bindAll();
})();
