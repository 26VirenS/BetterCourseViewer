/* Runs at document_start: decides before first paint whether the redesigned
 * interface is on and which appearance (light/dark) to use, so Canvas's own
 * chrome never flashes. The authoritative values come from extension
 * storage; a per-origin localStorage copy is applied instantly. */
(function () {
  // Canvas pages loaded inside a frame (a tool's file picker, a file preview,
  // an LTI return page) are someone else's UI: leave them exactly as they are.
  if (window.self !== window.top) return;
  const BCV = self.BCV;
  const S = BCV.settings;
  const html = document.documentElement;
  const CACHE_KEY = 'bcv:early';
  // A one-page note left by the look switch when Persistent is off: this page view shows the look
  // the other way round, and the next load (a reload, the next page) goes back to the saved look.
  const ONCE_KEY = 'bcv:once';
  let override = null; // true/false for this page's life, or null for the saved look
  try {
    const once = sessionStorage.getItem(ONCE_KEY);
    if (once === 'on' || once === 'off') {
      override = once === 'on';
      sessionStorage.removeItem(ONCE_KEY);
    }
  } catch { /* no session storage: the saved look */ }

  // Reset everything (the settings page) reaches every open Canvas tab: the copy kept in this
  // site's own storage goes too, so nothing of the extension's stays behind on the site. It is
  // not written again for the rest of this page's life (the reset's own settings writes would).
  let wiped = false;
  try {
    BCV.api.runtime.onMessage.addListener((msg, sender, sendResponse) => {
      if (!msg) return false;
      // The background pushes every settings change here as well as writing it, because a content
      // script cannot count on hearing storage.onChanged — in Safari it often never fires for a
      // change made in the popup or the settings page, and the look switch then did nothing at all
      // to the page it was pressed for. Handed the new settings directly, this takes exactly the
      // path a storage change would have taken.
      if (msg.type === 'settingsPush') {
        sendResponse({ ok: true });
        if (msg.settings) sync(msg.settings);
        return false;
      }
      // the popup's look switch asks this tab what it shows, and flips it (see flipLook)
      if (msg.type === 'lookState') {
        sendResponse({ on: html.classList.contains('bcv-on'), persist: !!current?.appearance?.persistLook, once: override !== null });
        return false;
      }
      if (msg.type === 'lookFlip') {
        sendResponse({ ok: true });
        flipLook(!!msg.on);
        return false;
      }
      if (msg.type !== 'wipeSiteNote') return false;
      wiped = true;
      try { localStorage.removeItem(CACHE_KEY); } catch { /* ignore */ }
      sendResponse({ ok: true });
      return false;
    });
  } catch { /* no runtime here */ }

  const systemDark = () => !!window.matchMedia?.('(prefers-color-scheme: dark)').matches;
  // The phone layout (the iPhone mockup) for narrow viewports, decided before first paint and
  // kept for the page's life so a screen never re-flows into the other layout mid-way.
  const phone = () => !!window.matchMedia?.('(max-width: 700px)').matches;

  function apply({ skin, dark }) {
    html.classList.toggle('bcv-on', skin !== false);
    html.classList.toggle('bcv-phone', phone());
    html.setAttribute('data-bcv-theme', dark ? 'dark' : 'light');
  }

  // 1. Instant: cached values from the page origin's localStorage (the one-page note wins for the look).
  try {
    const cached = JSON.parse(localStorage.getItem(CACHE_KEY) || 'null');
    const base = cached && typeof cached === 'object' ? cached : { skin: true, dark: systemDark() };
    apply({ ...base, skin: override ?? base.skin });
  } catch {
    apply({ skin: override ?? true, dark: systemDark() });
  }

  // 2. Authoritative: extension storage.
  let current = null;
  let shown = null; // the state the page is currently drawn for, so a flip can be told from a first read
  let lastStored = null; // the saved look as last read, so a change to it can be told from a re-read
  const listeners = new Set();
  async function sync(settings) {
    current = settings || (await S.get());
    const stored = current.appearance.skin !== false;
    if (lastStored !== null && stored !== lastStored) override = null; // a saved change beats the one-page note
    lastStored = stored;
    const state = { skin: override ?? stored, dark: S.isDark(current, systemDark()) };
    const flipped = !!shown && shown.skin !== state.skin;
    apply(state);
    shown = state;
    try {
      if (!wiped) localStorage.setItem(CACHE_KEY, JSON.stringify({ skin: stored, dark: state.dark })); // the saved look, never the one-page note
    } catch {
      /* ignore */
    }
    for (const fn of listeners) {
      try {
        fn(state, current);
      } catch {
        /* ignore */
      }
    }
    // The look going on or off is a different page: stock Canvas has to come back whole, and our
    // shell has to be built over a page Canvas drew without it. app.js does that whenever it is
    // listening. When nothing is, nobody would — the class flips and the page just sits there until
    // the safety net below notices, eight seconds later. That happens for real: Safari re-injects a
    // site's content scripts when the extension looks at its permissions (opening the toolbar popup
    // does), and a re-injected app.js finds the interface already booted and stands down, leaving
    // this copy with no listener. So the reload is done here when there is no one to do it.
    if (flipped && !listeners.size && !self.BCVBridge?.native) location.reload();
    return state;
  }
  const ready = sync();
  S.onChange((s) => sync(s));

  /** The look the other way round. With Persistent on (the popup) it is saved, and Settings, the
   *  popup and every page follow; off, it is for this page view alone — a one-page note, and a
   *  reload or the next page brings the saved look back. Either way the page is loaded afresh
   *  (stock Canvas has to come back whole; our shell has to be built over a page Canvas drew
   *  without it), except mid-quiz, where Canvas's own attempt page is the place to land (every
   *  answer is already saved there). The app's web view repaints in place instead. */
  async function flipLook(on) {
    const settings = current || (await S.get());
    if (settings.appearance?.persistLook) {
      const next = await S.update({ appearance: { skin: on } });
      await sync(next); // a storage change may never reach this page (Safari): applied here, the reload follows
      return;
    }
    if (self.BCVBridge?.native) {
      override = on;
      await sync(current);
      return;
    }
    try {
      sessionStorage.setItem(ONCE_KEY, on ? 'on' : 'off');
    } catch {
      return; // no session storage to leave the note in: nothing to flip with
    }
    const raw = !on ? BCV.app?.rawQuizUrl?.() : null;
    if (raw) {
      if (BCV.app?.state) BCV.app.state.quizOpen = false; // our screen is being left on purpose
      location.href = raw;
      return;
    }
    location.reload();
  }
  // (No page-top loading bar: the sidebar row that was pressed is the progress indicator, mockup 14.)
  // Safety net: if the interface never mounts (a script error, a blocked page, an answer that
  // never comes), the page is loaded once more — a fresh load clears most of what wedges — and,
  // if it happens again within the minute, given back to Canvas rather than left blank.
  setTimeout(() => {
    if (!html.classList.contains('bcv-on') || document.getElementById('bcv-app')) return;
    const key = 'bcv:reloaded';
    let again = false;
    try {
      const m = JSON.parse(sessionStorage.getItem(key) || 'null');
      again = !!m && m.path === location.pathname + location.search && Date.now() - m.at < 60000;
      if (!again) sessionStorage.setItem(key, JSON.stringify({ path: location.pathname + location.search, at: Date.now() }));
    } catch {
      again = true; // no session storage to remember by: never risk a loop
    }
    if (again) html.classList.remove('bcv-on');
    else location.reload();
  }, 8000);
  try {
    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => sync(current));
  } catch {
    /* ignore */
  }

  BCV.early = {
    ready,
    sync,
    systemDark,
    settings: () => current,
    isDark: () => html.getAttribute('data-bcv-theme') === 'dark',
    isOn: () => html.classList.contains('bcv-on'),
    isOnce: () => override !== null, // this page view shows the look the other way round from the saved one
    flipLook,
    onChange: (fn) => {
      listeners.add(fn);
      return () => listeners.delete(fn);
    },
  };
})();
