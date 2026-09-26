/* Runs at document_start: decides before first paint whether the redesigned
 * interface is on and which appearance (light/dark) to use, so Canvas's own
 * chrome never flashes. The authoritative values come from extension
 * storage; a per-origin localStorage copy is applied instantly. */
(function () {
  // Canvas pages loaded inside a frame (a tool's file picker, a file preview,
  // an LTI return page) are someone else's UI: leave them exactly as they are.
  if (window.self !== window.top) return;
  // A tool launched from the interface gets a tab of its own, with the extension's bar over the
  // tool rather than the interface's shell round it (content/toolbar.js). This is that tab.
  try { if (new URLSearchParams(location.search).get('bcv') === 'tool') return; } catch { /* no search here */ }
  const BCV = self.BCV;
  const S = BCV.settings;
  const html = document.documentElement;
  const CACHE_KEY = 'bcv:early';
  // On a Mac the settings live in the app: the background takes them from it as this page loads
  // (nothing happens anywhere else). The cached look below paints first; a change lands as a push.
  try { Promise.resolve(BCV.api.runtime.sendMessage({ type: 'syncApp' })).catch(() => {}); } catch { /* no runtime here */ }
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
      if (msg.type !== 'wipeSiteNote') return false;
      wiped = true;
      try { localStorage.removeItem(CACHE_KEY); } catch { /* ignore */ }
      sendResponse({ ok: true });
      return false;
    });
  } catch { /* no runtime here */ }

  const systemDark = () => !!window.matchMedia?.('(prefers-color-scheme: dark)').matches;

  // ---- the layout tiers: the one place for the widths the CSS's media queries repeat ------------
  // phone: at most 700 CSS px across (the iPhone layout, content/app/phone.js); compact: at most
  // 1100 (the sidebar and the course rail narrow, columns stack); regular above that. A CSS pixel is
  // whatever the browser's zoom and the OS's scaling make of it, so a laptop zoomed to 200% is a
  // phone here, and one at 150% is compact. The tier is read once the document is parsed and kept
  // for the page's life — the two shells are different code, and a screen is never re-flowed into
  // the other under the student. When a zoom crosses the phone line while the interface is up, the
  // page is loaded afresh into the other layout once the zoom has settled, unless something on it
  // would be lost (a hand-in being written, a quiz attempt: app.holds).
  const LAYOUT = { PHONE_MAX: 700, COMPACT_MAX: 1100 };
  const tierOf = (w = window.innerWidth) => (w <= LAYOUT.PHONE_MAX ? 'phone' : w <= LAYOUT.COMPACT_MAX ? 'compact' : 'regular');
  BCV.layout = { ...LAYOUT, tier: tierOf };
  const phoneQuery = () => window.matchMedia?.(`(max-width: ${LAYOUT.PHONE_MAX}px)`);
  // (before the viewport meta is read, a phone's layout viewport can still be the 980px default, so
  // the answer is only trusted once the document is past loading)
  let phoneDecided = null;
  const phone = () => {
    if (phoneDecided !== null) return phoneDecided;
    const now = !!phoneQuery()?.matches;
    if (document.readyState !== 'loading') phoneDecided = now;
    return now;
  };
  let retierT = 0;
  const onTierChange = () => {
    clearTimeout(retierT);
    retierT = setTimeout(() => { // (the zoom settled: Ctrl and + pressed three times is one change)
      if (!html.classList.contains('bcv-on') || !document.getElementById('bcv-app') || self.BCVBridge?.native) return;
      if ((tierOf() === 'phone') === html.classList.contains('bcv-phone')) return; // (back where it was, or never crossed)
      if (BCV.app?.holds?.()) return;
      location.reload();
    }, 600);
  };
  try { phoneQuery()?.addEventListener('change', onTierChange); } catch { /* no media queries here */ }

  function apply({ skin, dark, accent }) {
    html.classList.toggle('bcv-on', skin !== false);
    html.classList.toggle('bcv-phone', phone());
    html.setAttribute('data-bcv-theme', dark ? 'dark' : 'light');
    // the student's colour, with the shades this mode draws from it (lib/theme.js), on the page before it paints
    try { BCV.theme?.apply(html, skin !== false ? accent || '' : '', !!dark); } catch { /* the interface's own blue */ }
  }

  // 1. Instant: cached values from the page origin's localStorage (the one-page note wins for the look;
  // a turn-off for a while is over once its time has come — the cache carries the time, `until`).
  try {
    const cached = JSON.parse(localStorage.getItem(CACHE_KEY) || 'null');
    const base = cached && typeof cached === 'object' ? cached : { skin: true, dark: systemDark() };
    const cachedOn = base.skin !== false || (Number(base.until) > 0 && Date.now() >= Number(base.until));
    apply({ ...base, skin: override ?? cachedOn });
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
    // (on by the saved settings — S.lookOn: a turn-off for a while whose time has come counts as on.
    // Nothing is written back when it does: another tab still showing stock Canvas is left as it is,
    // never reloaded under someone, and picks Simpl up at its own next page)
    const stored = S.lookOn(current);
    if (lastStored !== null && stored !== lastStored) override = null; // a saved change beats the one-page note
    lastStored = stored;
    const state = { skin: override ?? stored, dark: S.isDark(current, systemDark()), accent: current.appearance?.theme?.accent || '' };
    const flipped = !!shown && shown.skin !== state.skin;
    apply(state);
    shown = state;
    try {
      if (!wiped) localStorage.setItem(CACHE_KEY, JSON.stringify({ skin: current.appearance.skin !== false, until: Number(current.appearance.offUntil) || 0, dark: state.dark, accent: state.accent })); // the saved look (and when a turn-off for a while ends), never the one-page note
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

  /** The look the other way round, for this page view alone: a one-page note, and a reload or
   *  the next page brings the saved look back. The page is loaded afresh (stock Canvas has to come
   *  back whole; our shell has to be built over a page Canvas drew without it), except mid-quiz,
   *  where Canvas's own attempt page is the place to land (every answer is already saved there).
   *  The app's web view repaints in place instead. */
  async function flipLook(on) {
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
  /** The switch at the top right: green on, red off (2.98.18). Three states under it. 1: the look
   *  on. 0: stock Canvas for this page view only (flipLook's one-page note: the red list's "This
   *  page only"). -1: turned off — saved, so every page is stock Canvas until it is turned on again
   *  or, for a turn-off for a while, until its time comes (Settings and the popup show the same
   *  saved switch). Turning on saves the look on again; set to 0 from there, this page stays stock
   *  Canvas on a one-page note and the look is back on the next page. */
  const locked = () => !!current && !S.lookOn(current);
  const lookPos = () => (locked() ? -1 : html.classList.contains('bcv-on') ? 1 : 0);
  /** When a turn-off for a while ends (ms), while it is still running; 0 otherwise. */
  const offUntil = () => (locked() ? Number(current.appearance.offUntil) || 0 : 0);
  /** Off: 'page' for this page view alone, a length of time (ms) for a while, or null (or 0) until
   *  turned on again. Saved but for 'page'; the page loads afresh as stock Canvas. */
  async function offFor(what) {
    if (!current) await ready;
    if (what === 'page') return setLook(0);
    const ms = Number(what) || 0;
    const next = await S.update(S.lookPatch(false, ms > 0 ? Date.now() + ms : 0));
    await sync(next); // a storage change may never reach this page (Safari): applied here, the reload follows (already off before: the new length saved, the page left as it is)
  }
  async function setLook(pos) {
    if (!current) await ready;
    if (pos === lookPos()) return;
    if (pos === -1) return offFor(null);
    if (locked()) {
      const next = await S.update(S.lookPatch(true));
      if (pos === 0 && !self.BCVBridge?.native) {
        try {
          sessionStorage.setItem(ONCE_KEY, 'off');
          location.reload();
          return;
        } catch { /* no note to leave: on, then */ }
      }
      await sync(next);
      if (pos === 0) { override = false; await sync(next); } // the app's web view: off for this page, in place
      return;
    }
    await flipLook(pos === 1);
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
    lookPos,
    setLook,
    offFor,
    offUntil,
    flipLook,
    onChange: (fn) => {
      listeners.add(fn);
      return () => listeners.delete(fn);
    },
  };
})();
