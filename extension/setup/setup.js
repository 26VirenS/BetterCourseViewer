/* The page after install (Safari and Chrome): one line, in big letters — head over to your courses
 * website — and then it watches. The background notices a tab arriving on Canvas (noticeTab in
 * background.js): a site already allowed opens the setup there on its own and this page closes;
 * any other site is named here with one button to allow it, the press being the gesture a
 * permission request needs. A Canvas that is not called one can be typed in. */
(async function () {
  const BCV = self.BCV;
  const api = BCV.api;
  const S = BCV.settings;
  const { h } = BCV.utils;

  // ---- appearance: the extension's choice, else the system's ------------------------------------
  const mq = window.matchMedia?.('(prefers-color-scheme: dark)');
  let settings = await S.get();
  const paint = () => {
    const mode = settings.appearance?.darkMode || 'system';
    const dark = mode === 'on' || (mode === 'system' && !!mq?.matches);
    document.documentElement.setAttribute('data-theme', dark ? 'dark' : 'light');
  };
  paint();
  mq?.addEventListener?.('change', paint);
  S.onChange((s) => { settings = s; paint(); });

  const body = document.getElementById('body');
  const foot = document.getElementById('foot');
  // Safari says a tab's address only once the user has allowed the site — and asks them about every
  // open site if asked for all the addresses at once — so there the page waits for a tab to arrive
  // rather than looking around, and says what Safari will ask.
  const safari = /apple/i.test(navigator.vendor || '') && !/chrome|crios|edg/i.test(navigator.userAgent);
  const mark = h('span', { class: 'mark mark--lg', 'aria-hidden': 'true' });
  mark.innerHTML = '<svg viewBox="0 0 120 120" width="76" height="76"><defs><linearGradient id="sheetLg" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#fff" stop-opacity=".9"/><stop offset="1" stop-color="#fff" stop-opacity="0"/></linearGradient></defs><rect class="sheet" x="16" y="18" width="53" height="84" rx="14" fill="url(#sheetLg)"/><path class="arc arc--1" style="--len:178" d="M28 30 H69 A30 30 0 0 1 69 90 H28" fill="none" stroke="#fff" stroke-width="8" stroke-linecap="round" stroke-linejoin="round" stroke-dasharray="178"/><path class="arc arc--2" style="--len:126" d="M35 42 H69 A18 18 0 0 1 69 78 H35" fill="none" stroke="rgba(255,255,255,.72)" stroke-width="8" stroke-linecap="round" stroke-linejoin="round" stroke-dasharray="126"/><path class="arc arc--3" style="--len:74" d="M42 54 H69 A6 6 0 0 1 69 66 H42" fill="none" stroke="rgba(255,255,255,.46)" stroke-width="8" stroke-linecap="round" stroke-linejoin="round" stroke-dasharray="74"/></svg>';

  const statusText = h('span', { id: 'statusText', text: 'Waiting for a Canvas tab to open…' });
  const status = h('p', { class: 'watch', id: 'status' }, [h('span', { class: 'watch__dot', 'aria-hidden': 'true' }), statusText]);
  const found = h('div', { class: 'found', id: 'found', hidden: '' });
  const setStatus = (text, on = false) => { statusText.textContent = text; status.classList.toggle('is-on', on); };
  const hostOf = (origin) => { try { return new URL(origin).host; } catch { return origin; } };
  const normalise = (raw) => {
    const v = String(raw || '').trim();
    if (!v) return null;
    try { return new URL(/^https?:\/\//i.test(v) ? v : `https://${v}`).origin; } catch { return null; }
  };

  body.append(
    h('div', { class: 'welcome welcome--big' }, [
      mark,
      h('h1', { class: 'h1 h1--huge', text: 'Head over to your courses website.' }),
      h('p', { class: 'lead', text: safari
        ? 'When Safari asks, allow Simpl Courses on the site — the setup starts on your Canvas page right after.'
        : 'This page notices when you get there, and the setup starts on your Canvas page.' }),
      status,
      found,
    ]),
  );

  // a Canvas that is not called one: its address, typed in, allowed from that press
  const input = h('input', { class: 'addr', id: 'addr', type: 'url', placeholder: 'https://lms.myschool.edu', spellcheck: 'false', 'aria-label': 'Your Canvas address' });
  const addrGo = h('button', { type: 'button', class: 'btn btn--sm', id: 'addrGo', text: 'Allow and open', onclick: () => allow(normalise(input.value), null) });
  input.addEventListener('keydown', (e) => { if (e.key === 'Enter') addrGo.click(); });
  foot.append(h('details', { class: 'other' }, [
    h('summary', { class: 'other__s', text: 'Not found by itself? Enter the address' }),
    h('div', { class: 'other__row' }, [input, addrGo]),
  ]));

  let leaving = false;
  const leave = (ms) => { if (leaving) return; leaving = true; setTimeout(() => window.close(), ms); };

  /** Permission for the site (from the press), its scripts registered, then the setup over that
   *  tab — the popup's own flow, on this page. */
  async function allow(origin, tabId) {
    if (!origin) { setStatus('That does not look like an address.'); return; }
    setStatus('Asking for permission…');
    // Safari refuses permissions.request() unless it is still the browser's idea of a user
    // gesture, so nothing is waited for between the press and the call; the note lets the
    // background finish from it (continuePending) should this page be gone when the permission lands.
    api.storage.local.set({ 'setup:pending': { origin, tabId, next: 'setup', at: Date.now() } }).catch(() => {});
    let ok = false;
    try {
      ok = await api.permissions.request({ origins: [`${origin}/*`] });
    } catch (e) {
      await api.storage.local.remove('setup:pending').catch(() => {});
      setStatus(`Permission request failed: ${e?.message || e}`);
      return;
    }
    await api.storage.local.remove('setup:pending').catch(() => {}); // still here: this page finishes it
    if (!ok) { setStatus('Permission was not granted. Simpl Courses can only run on a site you allow.'); return; }
    const r = await api.runtime.sendMessage({ type: 'registerDomain', origin }).catch(() => null);
    if (r && r.ok === false) { setStatus(r.message || 'Could not enable this site.'); return; }
    await S.update({ appearance: { skin: true } }).catch(() => {});
    setStatus(`Opening the setup on ${hostOf(origin)}…`, true);
    const target = `${origin}/?bcv=setup`;
    try {
      if (tabId != null) await api.tabs.update(tabId, { url: target, active: true });
      else await api.tabs.create({ url: target });
    } catch {
      await api.tabs.create({ url: target }).catch(() => {});
    }
    leave(600);
  }

  /** What the background found: a site already allowed is where the setup has gone; any other is
   *  named here, with the one press that allows it. */
  function showFound(f) {
    if (!f || !f.origin || leaving) return;
    const host = hostOf(f.origin);
    if (f.granted && f.error) { // allowed, but the setup could not be opened there: say why, and offer the press
      setStatus(`Found ${host}, but ${f.error}.`);
      found.replaceChildren(h('button', { type: 'button', class: 'btn', id: 'allow', text: `Open the setup on ${host}`, onclick: () => { setStatus(`Opening the setup on ${host}…`, true); api.runtime.sendMessage({ type: 'startSetup', origin: f.origin, tabId: f.tabId }).catch(() => {}); } }));
      found.hidden = false;
      return;
    }
    if (f.granted) {
      found.hidden = true;
      setStatus(`Found ${host} — the setup is opening there.`, true);
      leave(1800);
      return;
    }
    setStatus(`Found ${host}.`);
    found.replaceChildren(
      h('p', { class: 'found__t', text: `Simpl Courses needs to be allowed on ${host} to run there.` }),
      h('button', { type: 'button', class: 'btn', id: 'allow', text: `Allow Simpl Courses on ${host}`, onclick: () => allow(f.origin, f.tabId) }),
    );
    found.hidden = false;
  }

  api.storage.onChanged.addListener((changes, area) => {
    if (area !== 'local') return;
    if (changes['setup:found']?.newValue) showFound(changes['setup:found'].newValue);
    if (changes['setup:done']?.newValue) leave(0); // set up from elsewhere: this page has nothing left to say
  });
  // what was found before this page opened is checked, not believed: the background looks for the
  // tab again, and only a tab still there counts
  const first = await api.runtime.sendMessage({ type: 'checkFound' }).catch(() => null);
  if (first && first.found) showFound(first.found);
  if (!safari) api.runtime.sendMessage({ type: 'scanTabs' }).catch(() => {}); // a Canvas that is already open
  await api.storage.local.set({ 'setup:offered': true }).catch(() => {});
})();
