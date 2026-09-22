/* The capture settings, as a short code you can read out and type back in.
 *
 * Catching the window a framed tool opens for itself depends on what the browser tells an
 * extension about a new tab, and browsers differ: Safari has not always filled in which tab opened
 * it, and pendingUrl is Chrome's alone. So the matching is not one fixed rule here — it is a few
 * settings, and this file turns them into a code like SC1-3151011 that says exactly what is on.
 * Read the code off the Developer section, send it on, and whoever reads it knows the setup.
 *
 * One digit per setting, in the order below, after the version:
 *
 *   SC1- m w a b c l t
 *        │ │ │ │ │ │ └─ toast:   say on the page what was seen                 0 no · 1 yes
 *        │ │ │ │ │ └─── log:     keep the last events to read back             0 no · 1 yes
 *        │ │ │ │ └───── close:   take the tab away once it is caught           0 leave · 1 close
 *        │ │ │ └─────── blank:   catch one that starts blank, wait for its URL 0 no · 1 yes
 *        │ │ └───────── wait:    how long to wait for an address               0 none · 1 250ms · 2 500ms · 3 1s · 4 2s · 5 4s
 *        │ └─────────── windows: watch new windows as well as new tabs         0 no · 1 yes
 *        └───────────── mode:    which new tab counts as the tool's            0 off · 1 opener · 2 window · 3 any
 */
(function () {
  const BCV = (self.BCV = self.BCV || {});

  const FIELDS = [
    { key: 'mode', max: 3, label: 'What counts as the tool’s window', words: ['Off — catch nothing', 'The tab says this one opened it', 'Any new tab from this window', 'Any new tab at all'] },
    { key: 'windows', max: 1, label: 'Watch new windows too', words: ['No', 'Yes'] },
    { key: 'wait', max: 5, label: 'Wait for an address', words: ['Do not wait', '250ms', '500ms', '1 second', '2 seconds', '4 seconds'] },
    { key: 'blank', max: 1, label: 'Catch one that starts blank', words: ['No', 'Yes'] },
    { key: 'close', max: 1, label: 'Once caught, the tab', words: ['Stays open', 'Is closed'] },
    { key: 'log', max: 1, label: 'Keep a log of what was seen', words: ['No', 'Yes'] },
    { key: 'toast', max: 1, label: 'Say on the page what was seen', words: ['No', 'Yes'] },
  ];
  const WAIT_MS = [0, 250, 500, 1000, 2000, 4000];
  // What ships. Not the opener: Safari does not always name the tab that opened a window, so a new
  // tab in the same window while a framed popup is up is what counts, closed once its address is
  // handed over. A framed popup fills the screen, so little else is opening a tab just then.
  const SHIPPED = { mode: 2, windows: 1, wait: 2, blank: 0, close: 1, log: 0, toast: 0 };
  // what to turn on to find out what a browser actually reports: catch everything, close nothing
  const EVERYTHING = { mode: 3, windows: 1, wait: 5, blank: 1, close: 0, log: 1, toast: 1 };

  const clamp = (n, max) => Math.max(0, Math.min(max, Number(n) || 0));
  /** The settings as a code: SC1-3151011. */
  function encode(s) {
    return `SC1-${FIELDS.map((f) => clamp(s?.[f.key], f.max)).join('')}`;
  }
  /** A code back to settings; anything unreadable gives what ships, and says so. */
  function decode(code) {
    const digits = String(code || '').trim().toUpperCase().replace(/^SC1-?/, '').replace(/[^0-9]/g, '');
    if (digits.length !== FIELDS.length) return { ok: false, settings: { ...SHIPPED }, message: `A code is SC1- and ${FIELDS.length} digits.` };
    const settings = {};
    for (let i = 0; i < FIELDS.length; i += 1) settings[FIELDS[i].key] = clamp(digits[i], FIELDS[i].max);
    return { ok: true, settings, message: '' };
  }
  /** The code in words, a line per setting — what to read out when the code alone is not enough. */
  function explain(s) {
    return FIELDS.map((f) => `${f.label}: ${f.words[clamp(s?.[f.key], f.max)]}`);
  }

  BCV.devcode = { FIELDS, WAIT_MS, SHIPPED, EVERYTHING, encode, decode, explain, KEY: 'dev:capture' };
})();
