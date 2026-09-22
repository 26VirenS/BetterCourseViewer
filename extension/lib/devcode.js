/* The tool-tab settings, as a short code you can read out and type back in.
 *
 * A tool opens in a tab of its own with the extension's bar over it, and the one thing that is not
 * the same everywhere is when that tab has finished arriving: a launch redirects, a sign-in is
 * several pages, and browsers report the steps differently. So how long a tab has to hold still
 * before the bar stops saying "Authenticating" is a setting, and this file turns the settings into
 * a code like SC3-211 that says exactly what is on. Read the code off the Developer section, send
 * it on, and whoever reads it knows the setup.
 *
 * This used to carry seven more digits, for catching the window a framed tool opened for itself.
 * Nothing is framed any more, so there is nothing to catch and they are gone.
 *
 * One digit per setting, in the order below, after the version:
 *
 *   SC3- s l t
 *        │ │ └─ toast: say on the page what was seen        0 no · 1 yes
 *        │ └─── log:   keep the last events to read back    0 no · 1 yes
 *        └───── settle: how still a tab must be to count as arrived
 */
(function () {
  const BCV = (self.BCV = self.BCV || {});

  const FIELDS = [
    { key: 'settle', max: 5, label: 'Let a tool finish arriving first', words: ['Straight away', 'Still for 1s', 'Still for 2s', 'Still for 3s', 'Still for 5s', 'Still for 10s'] },
    { key: 'log', max: 1, label: 'Keep a log of what was seen', words: ['No', 'Yes'] },
    { key: 'toast', max: 1, label: 'Say on the page what was seen', words: ['No', 'Yes'] },
  ];
  const SETTLE_MS = [0, 1000, 2000, 3000, 5000, 10000];
  const SHIPPED = { settle: 2, log: 0, toast: 0 };
  // what to turn on to find out what a browser actually reports
  const EVERYTHING = { settle: 2, log: 1, toast: 1 };

  const clamp = (n, max) => Math.max(0, Math.min(max, Number(n) || 0));
  /** The settings as a code: SC3-211. */
  function encode(s) {
    return `SC3-${FIELDS.map((f) => clamp(s?.[f.key], f.max)).join('')}`;
  }
  /** A code back to settings; anything unreadable gives what ships, and says so. */
  function decode(code) {
    const digits = String(code || '').trim().toUpperCase().replace(/^SC\d-?/, '').replace(/[^0-9]/g, '');
    if (digits.length !== FIELDS.length) return { ok: false, settings: { ...SHIPPED }, message: `A code is SC3- and ${FIELDS.length} digits.` };
    const settings = {};
    for (let i = 0; i < FIELDS.length; i += 1) settings[FIELDS[i].key] = clamp(digits[i], FIELDS[i].max);
    return { ok: true, settings, message: '' };
  }
  /** The code in words, a line per setting — what to read out when the code alone is not enough. */
  function explain(s) {
    return FIELDS.map((f) => `${f.label}: ${f.words[clamp(s?.[f.key], f.max)]}`);
  }

  BCV.devcode = { FIELDS, SETTLE_MS, SHIPPED, EVERYTHING, encode, decode, explain, KEY: 'dev:capture' };
})();
