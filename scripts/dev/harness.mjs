// What the browser suites share: the harness's copy of the extension, with the product's longest
// timers run short.
//
// The product waits three seconds before the welcome's Continue, plays a two-second word-mark before
// the setup card and What's New, gives a screen fifteen seconds before loading it again, counts
// three seconds down before an Away Refresh, keeps a tray island up for six, and rolls a counter for
// most of a second. A suite walks through those dozens of times, and spent minutes watching timers
// run. So the copy the suites load runs them short — the same code, the same order of events, only
// the waits — and every shipped value is asserted here, exactly: a change to any of them fails the
// suite until the table below is brought up to date. The suites' own timing checks read the short
// values from TIMERS, never the shipped ones.
import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

/** The short values the copy runs with (ms). */
export const TIMERS = {
  welcomeWait: 900, // welcome.js WAIT: Continue comes in after this (3000 shipped; a suite reaching a stage late — after a settle or a roll — still finds it held back)
  introFade: 700, // setup.js / whatsnew.js: the word-mark starts to fade (1900 shipped)
  introDone: 900, // …and the card rises under it (2340 shipped)
  patience: 4000, // app.js SCREEN_PATIENCE: a screen still not drawn after this gives way (15000 shipped)
  awayCount: 1500, // app.js AWAY_COUNT: the Away Refresh pill's count (3000 shipped)
  island: 1500, // tools.js islandOpen: a pressed island stays up this long (6000 shipped)
  rollMs: 10, // ui.js ROLL_MS: a counter's step (52 shipped; 16 steps either way)
  gradePoll: 800, // course-detail.js: how often a tool assignment's grade is asked for after a launch (2500 shipped)
};

/** file → [shipped text, the copy's text]; each shipped text must occur exactly once. */
const PATCHES = [
  ['content/app/welcome.js', 'const WAIT = 3000;', `const WAIT = ${TIMERS.welcomeWait};`],
  ['content/app/setup.js', "ui.intro.classList.add('is-fading'); }, 1900));", `ui.intro.classList.add('is-fading'); }, ${TIMERS.introFade}));`],
  ['content/app/setup.js', "ui.main.classList.add('is-in'); } }, 2340));", `ui.main.classList.add('is-in'); } }, ${TIMERS.introDone}));`],
  ['content/app/whatsnew.js', "ui.intro.classList.add('is-fading'); }, 1900));", `ui.intro.classList.add('is-fading'); }, ${TIMERS.introFade}));`],
  ['content/app/whatsnew.js', "ui.main.classList.add('is-in'); } }, 2340));", `ui.main.classList.add('is-in'); } }, ${TIMERS.introDone}));`],
  ['content/app/app.js', 'const SCREEN_PATIENCE = 15000;', `const SCREEN_PATIENCE = ${TIMERS.patience};`],
  ['content/app/app.js', 'const AWAY_COUNT = 3000;', `const AWAY_COUNT = ${TIMERS.awayCount};`],
  ['content/app/tools/tools.js', 'function islandOpen(item, ms = 6000, focusMain = false) {', `function islandOpen(item, ms = ${TIMERS.island}, focusMain = false) {`],
  ['content/app/ui.js', 'const ROLL_MS = 52;', `const ROLL_MS = ${TIMERS.rollMs};`],
  ['content/app/screens/course-detail.js', 'timer = setTimeout(poll, 2500);', `timer = setTimeout(poll, ${TIMERS.gradePoll});`],
];

/** Rewrites the timers in the copy at extDir. Returns one line per patch — `${file}: ${shipped}` —
 *  and `missing`: the shipped texts not found exactly once (the suite fails on any). */
export function shortenTimers(extDir) {
  const done = [];
  const missing = [];
  for (const [file, from, to] of PATCHES) {
    const path = join(extDir, file);
    const src = readFileSync(path, 'utf8');
    if (src.split(from).length !== 2) { missing.push(`${file}: ${from}`); continue; }
    writeFileSync(path, src.replace(from, to));
    done.push(`${file}: ${from}`);
  }
  return { done, missing };
}

/** A step's duration, for the log: `(+4.2s)`. */
export const secs = (ms) => `${(ms / 1000).toFixed(1)}s`;

/** Wraps a page's waits so any single one that takes long is named in the log — where a suite's
 *  time goes, without a profiler. `threshold` in ms; `log` gets one line per slow call. */
export function reportSlow(page, { threshold = 3000, log = console.log } = {}) {
  const wrap = (name, describe) => {
    const orig = page[name].bind(page);
    page[name] = async (...a) => {
      const t = Date.now();
      try { return await orig(...a); } finally {
        const d = Date.now() - t;
        if (d >= threshold) log(`    (slow: ${describe(a)} took ${secs(d)})`);
      }
    };
  };
  const arg = (a) => (typeof a[0] === 'string' ? a[0].replace(/^http:\/\/localhost:\d+/, '') : typeof a[0] === 'function' ? 'a condition' : String(a[0]));
  wrap('goto', (a) => `goto ${arg(a)}`);
  wrap('reload', () => 'reload');
  wrap('click', (a) => `click ${arg(a)}`);
  wrap('waitForSelector', (a) => `waitForSelector ${arg(a)}`);
  wrap('waitForFunction', (a) => 'waitForFunction');
  wrap('waitForNavigation', () => 'waitForNavigation');
  wrap('waitForTimeout', (a) => `waitForTimeout ${a[0]}`);
}

/** Waits until the background's one-time setup migration has left its mark (setup:flow), so a suite's
 *  own flags go in after it — it clears them when it finds an older flow, and on a busy machine it
 *  can run after a suite has already written them. */
export async function afterMigration(sw, flow = 3) {
  for (let i = 0; i < 80; i++) {
    const cur = await sw.evaluate(async () => (await self.BCV.api.storage.local.get('setup:flow'))['setup:flow']).catch(() => null);
    if (cur === flow) return true;
    await new Promise((r) => setTimeout(r, 100));
  }
  return false;
}
