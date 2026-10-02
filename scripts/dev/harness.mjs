// What the browser suites share: the harness's copy of the extension, with the product's longest
// timers run short.
//
// The product holds each done step of the guided tour a moment and a half, plays a two-second word-mark before
// the setup card and What's New, gives a screen fifteen seconds before loading it again, counts
// three seconds down before an Away Refresh, keeps a tray island up for six, and rolls a counter for
// most of a second. A suite walks through those dozens of times, and spent minutes watching timers
// run. So the copy the suites load runs them short — the same code, the same order of events, only
// the waits — and every shipped value is asserted here, exactly: a change to any of them fails the
// suite until the table below is brought up to date. The suites' own timing checks read the short
// values from TIMERS, never the shipped ones.
import { readFileSync, writeFileSync, rmSync } from 'node:fs';
import { join } from 'node:path';

/** The short values the copy runs with (ms). */
export const TIMERS = {
  tourAfter: 450, // welcome.js AFTER: a done step's line holds this long before the next step (2200 shipped)
  introFade: 700, // setup.js / whatsnew.js: the word-mark starts to fade (1900 shipped)
  introDone: 900, // …and the card rises under it (2340 shipped)
  patience: 4000, // app.js SCREEN_PATIENCE: a screen still not drawn after this gives way (15000 shipped)
  awayCount: 1500, // app.js AWAY_COUNT: the Away Refresh pill's count (3000 shipped)
  awayHold: 1200, // app.js AWAY_HOLD: held this long, the pill turns Away Refresh off (900 shipped; longer here, so a read mid-hold lands inside it under load)
  awayOffStay: 1000, // app.js AWAY_OFF_STAY: the pill says it is off for this long before it goes (1600 shipped)
  island: 1500, // tools.js islandOpen: a pressed island stays up this long (6000 shipped)
  rollMs: 10, // ui.js ROLL_MS: a counter's step (52 shipped; 16 steps either way)
  gradePoll: 800, // course-detail.js: how often a tool assignment's grade is asked for after a launch (2500 shipped)
};

/** file → [shipped text, the copy's text]; each shipped text must occur exactly once. */
const PATCHES = [
  ['content/app/welcome.js', 'const AFTER = 2200;', `const AFTER = ${TIMERS.tourAfter};`],
  ['content/app/setup.js', "ui.intro.classList.add('is-fading'); }, 1900));", `ui.intro.classList.add('is-fading'); }, ${TIMERS.introFade}));`],
  ['content/app/setup.js', "ui.main.classList.add('is-in'); } }, 2340));", `ui.main.classList.add('is-in'); } }, ${TIMERS.introDone}));`],
  ['content/app/whatsnew.js', "ui.intro.classList.add('is-fading'); }, 1900));", `ui.intro.classList.add('is-fading'); }, ${TIMERS.introFade}));`],
  ['content/app/whatsnew.js', "ui.main.classList.add('is-in'); } }, 2340));", `ui.main.classList.add('is-in'); } }, ${TIMERS.introDone}));`],
  ['content/app/app.js', 'const SCREEN_PATIENCE = 15000;', `const SCREEN_PATIENCE = ${TIMERS.patience};`],
  ['content/app/app.js', 'const AWAY_COUNT = 3000;', `const AWAY_COUNT = ${TIMERS.awayCount};`],
  ['content/app/app.js', 'const AWAY_HOLD = 900;', `const AWAY_HOLD = ${TIMERS.awayHold};`],
  ['content/app/app.js', 'const AWAY_OFF_STAY = 1600;', `const AWAY_OFF_STAY = ${TIMERS.awayOffStay};`],
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

// ---- motion at test speed ---------------------------------------------------------------------
// Headless Chromium draws every frame of every animation in software, and the interface moves a lot:
// a Dashboard counter opening into its box and closing again cost 2.4 s of CPU, most of a suite's
// time went on frames nobody looks at, and with several suites side by side the machine ran out of
// cores. So every page a suite opens runs its animations MOTION_RATE times faster (the DevTools
// protocol's Animation.setPlaybackRate: CSS animations and transitions and the Web Animations the
// springs are made of, all of it, the same frames in the same order, only sooner). A check that reads
// an animation mid-flight — a box still growing, a highlight still gliding — runs inside realMotion().
// BCV_MOTION_RATE=1 runs a suite at real speed.
export const MOTION_RATE = Math.max(1, Number(process.env.BCV_MOTION_RATE) || 50);
const motionSessions = new WeakMap();
/** Sets one page's animation speed (1 = real time). It holds across the page's navigations. */
export async function motionAt(page, rate) {
  let s = motionSessions.get(page);
  if (!s) { s = await page.context().newCDPSession(page); motionSessions.set(page, s); }
  await s.send('Animation.setPlaybackRate', { playbackRate: rate });
}
/** Every page of the context, those open and those to come, at test speed. Returns the context. */
export function fastMotion(context, rate = MOTION_RATE) {
  if (rate === 1) return context;
  const on = (p) => { motionAt(p, rate).catch(() => {}); };
  for (const p of context.pages()) on(p);
  context.on('page', on);
  return context;
}
/** Runs fn with the page's animations at real speed (a check that watches motion as it happens), then back. */
export async function realMotion(page, fn) {
  await motionAt(page, 1);
  try { return await fn(); } finally { await motionAt(page, MOTION_RATE).catch(() => {}); }
}

/** Opens Chromium on a profile at userDataDir with the extension in extDir loaded, every page at test
 *  speed (fastMotion), and waits for the extension's background to start. `options` are Playwright's
 *  launch options (viewport, isMobile…). Now and then, on a busy machine, a fresh profile's background
 *  never starts at all — the event a suite waits for never comes — so a browser that has not shown it
 *  in 12 s is closed, its profile cleared, and opened once more. Returns { context, sw }. */
export async function launchExtension(chromium, userDataDir, extDir, options = {}) {
  for (let attempt = 1; ; attempt++) {
    const context = await chromium.launchPersistentContext(userDataDir, { channel: 'chromium', headless: true, ...options, args: [`--disable-extensions-except=${extDir}`, `--load-extension=${extDir}`, ...(options.args || [])] });
    fastMotion(context);
    const sw = context.serviceWorkers()[0] || await context.waitForEvent('serviceworker', { timeout: 12000 }).catch(() => null);
    if (sw) return { context, sw };
    await context.close().catch(() => {});
    if (attempt === 2) throw new Error("the extension's background did not start, in two browsers");
    rmSync(userDataDir, { recursive: true, force: true });
    console.log("  (the extension's background did not start: the browser is opened again on a fresh profile)");
  }
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
