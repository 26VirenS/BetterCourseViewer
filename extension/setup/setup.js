/* The page after install (Safari and Chrome): black, the way the welcome on the page is — a splash,
 * then the one thing to do: Open your Canvas. Setup will begin there. Nothing to press: the setup
 * runs over the Canvas page on its own (content/app/setup.js opens over the first signed-in Canvas
 * page until it is done), and this page closes itself the moment it has begun. Chrome's build finds
 * the school's own Canvas address by itself (content/sniff.js); Safari asks for each site as it is
 * opened, so its page carries the one line about that. */
(async function () {
  const BCV = self.BCV;
  const api = BCV.api;
  const { h } = BCV.utils;
  const safari = /apple/i.test(navigator.vendor || '') && !/chrome|crios|edg/i.test(navigator.userAgent);
  const SPLASH = 1600; // the wordmark holds this long before the words
  const SHEET = '<svg viewBox="0 0 120 120" width="100%" height="100%"><rect x="16" y="18" width="53" height="84" rx="14" fill="rgba(255,255,255,.35)"/><path d="M28 30 H69 A30 30 0 0 1 69 90 H28" fill="none" stroke="#fff" stroke-width="8" stroke-linecap="round" stroke-linejoin="round"/><path d="M35 42 H69 A18 18 0 0 1 69 78 H35" fill="none" stroke="rgba(255,255,255,.72)" stroke-width="8" stroke-linecap="round" stroke-linejoin="round"/><path d="M42 54 H69 A6 6 0 0 1 69 66 H42" fill="none" stroke="rgba(255,255,255,.46)" stroke-width="8" stroke-linecap="round" stroke-linejoin="round"/></svg>';

  const root = document.getElementById('splash');
  document.documentElement.classList.add('is-splash');
  let current = null;
  let splashTimer = 0; // the splash moving on by itself
  /** The next screen in, the last one out. */
  function show(el) {
    clearTimeout(splashTimer);
    const old = current;
    if (old) { old.classList.add('is-leaving'); setTimeout(() => old.remove(), 300); }
    current = el;
    root.append(el);
  }

  // ---- the two screens -------------------------------------------------------------------------
  function splash() {
    return h('div', { class: 'splash__stage splash__stage--word', dataset: { stage: 'splash' } }, [
      h('div', { class: 'splash__brand' }, [
        h('span', { class: 'mark mark--splash', 'aria-hidden': 'true', html: SHEET }),
        h('span', { class: 'splash__word', text: 'Simpl.' }),
      ]),
    ]);
  }
  function go() {
    return h('div', { class: `splash__stage splash__stage--go${safari ? ' is-safari' : ''}`, dataset: { stage: 'go' } }, [
      h('div', { class: 'splash__text' }, [
        h('h1', { class: 'splash__title', text: 'Open your Canvas' }),
        h('p', { class: 'splash__hint', text: 'Setup will begin there.' }),
        // Safari asks before an extension may run on a site: the one thing to know
        safari ? h('p', { class: 'splash__note' }, ['If Safari asks, press the ', h('b', { text: 'Simpl Courses' }), ' button in the toolbar and choose ', h('b', { text: 'Always Allow on This Website' }), '.']) : null,
      ]),
    ]);
  }

  // a press anywhere on the splash moves it along
  show(splash());
  const onward = () => { if (current?.dataset.stage === 'splash') show(go()); };
  splashTimer = setTimeout(onward, SPLASH);
  root.addEventListener('click', onward);
  await api.storage.local.set({ 'setup:offered': true }).catch(() => {});

  // The setup beginning on a Canvas tab (the card leaves a note in storage as it opens) is the end
  // of this page: it asks the background to close its tab (a page cannot close a tab it did not
  // open itself). The same when the setup is finished, for a page left open past it.
  const closeMe = () => { try { Promise.resolve(api.runtime.sendMessage({ type: 'closeSetupTab' })).catch(() => {}); } catch { /* stays open */ } };
  try {
    api.storage.onChanged.addListener((changes, area) => {
      if (area && area !== 'local') return;
      if ((changes['setup:begun'] && changes['setup:begun'].newValue) || (changes['setup:done'] && changes['setup:done'].newValue)) closeMe();
    });
  } catch { /* no storage events: the page stays until it is closed */ }
})();
