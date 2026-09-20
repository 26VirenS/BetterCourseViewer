// The extension taking its settings from the Mac app.
//
// On a Mac the app holds the settings (background.js, "the Mac app"): the background asks the
// native handler for them, applies what is newer, writes its own switches up, runs the commands the
// app leaves and reports them done, and opens the app in place of a settings page. Here the
// extension runs in Chromium with a fake handler in place of the native one, so every one of those
// moves can be watched: a newer revision applied; a change here written up once and not echoed;
// a wipe run and acknowledged, then the extension's defaults handed to an app with none; Settings
// opening the app.
// Run: node scripts/dev/app-sync-test.mjs
import { createRequire } from 'node:module';
import { execSync } from 'node:child_process';
import { cpSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
const require = createRequire(import.meta.url);
let chromium;
try { ({ chromium } = require('playwright')); } catch { const p = execSync('npm root -g').toString().trim(); ({ chromium } = createRequire(join(p, 'x.js'))('playwright')); }

const root = new URL('../..', import.meta.url).pathname;
const extDir = join(tmpdir(), `bcv-sync-ext-${Date.now()}`);
cpSync(join(root, 'extension'), extDir, { recursive: true });
const manifest = JSON.parse(readFileSync(join(extDir, 'manifest.json'), 'utf8'));
writeFileSync(join(extDir, 'manifest.json'), JSON.stringify(manifest, null, 2));

const failures = [];
const check = (ok, label) => { console.log(`  ${ok ? '✓' : '✗'} ${label}`); if (!ok) failures.push(label); };

const userDataDir = join(tmpdir(), `bcv-sync-profile-${Date.now()}`);
const context = await chromium.launchPersistentContext(userDataDir, { channel: 'chromium', headless: true, args: [`--disable-extensions-except=${extDir}`, `--load-extension=${extDir}`] });
try {
  let [sw] = context.serviceWorkers();
  if (!sw) sw = await context.waitForEvent('serviceworker', { timeout: 15000 });
  for (const p of context.pages()) if (p.url().endsWith('/setup/setup.html')) await p.close();
  await sw.evaluate((v) => self.BCV.api.storage.local.set({ 'setup:offered': true, 'setup:done': true, 'setup:flow': 3, 'whatsnew:seen': v, 'prefs:canvas.test': { gpaGoal: 3.5 }, 'site:last': { host: 'canvas.test', origin: 'https://canvas.test' } }), manifest.version);
  check((await sw.evaluate(() => self.BCV.background.app.on)) === false, 'in Chromium there is no app to ask: the sync is off');

  // the fake handler: a store with a revision, the settings, the commands, and a log of every message
  await sw.evaluate(() => {
    const B = self.BCV.background;
    self.__native = { store: { revision: 3, settings: { appearance: { darkMode: 'on', skin: true, sideCourses: 'hover' }, domains: [] }, commands: [{ id: 'c1', type: 'wipeSiteNotes' }] }, log: [], opened: 0 };
    B.app.on = true;
    B.app.native = async (msg) => {
      const n = self.__native; const st = n.store;
      n.log.push(JSON.parse(JSON.stringify(msg)));
      if (msg.type === 'getSettings') return { ok: true, revision: st.revision, settings: st.revision !== msg.revision && st.settings ? st.settings : undefined, hasSettings: !!st.settings, commands: st.commands };
      if (msg.type === 'setSettings') { st.settings = msg.settings; st.revision += 1; return { ok: true, revision: st.revision }; }
      if (msg.type === 'done') { st.commands = st.commands.filter((c) => c.id !== msg.id); return { ok: true }; }
      if (msg.type === 'openApp') { n.opened += 1; return { ok: true }; }
      return { ok: false };
    };
  });
  const log = () => sw.evaluate(() => self.__native.log.map((m) => m.type + (m.id ? `:${m.id}` : '')));
  const settings = () => sw.evaluate(() => self.BCV.settings.get());

  console.log("the app's settings, taken");
  await sw.evaluate(() => self.BCV.background.syncApp());
  let s = await settings();
  check(s.appearance.darkMode === 'on' && s.appearance.sideCourses === 'hover' && (await sw.evaluate(() => self.BCV.background.app.revision)) === 3, `a newer revision is applied: dark ${s.appearance.darkMode}, courses ${s.appearance.sideCourses}, revision 3`);
  let l = await log();
  check(l[0] === 'getSettings' && l.includes('done:c1') && (await sw.evaluate(() => self.__native.store.commands.length)) === 0, `the command the app left is run and reported done: ${l.join(',')}`);
  const asked = await sw.evaluate(() => self.__native.log[0]);
  check(asked.site && asked.site.host === 'canvas.test' && asked.setupDone === true, 'and the ask carries the extension\'s word: the site it last drew, the setup done');
  await sw.evaluate(() => self.BCV.background.syncApp());
  check((await log()).filter((x) => x === 'setSettings').length === 0, 'nothing is written up for settings that only came down');

  console.log('a switch pressed here');
  await sw.evaluate(() => self.BCV.settings.update({ appearance: { skin: false } }));
  await new Promise((r) => setTimeout(r, 400));
  l = await log();
  const up = await sw.evaluate(() => self.__native.log.filter((m) => m.type === 'setSettings'));
  check(up.length === 1 && up[0].settings.appearance.skin === false && up[0].settings.appearance.darkMode === 'on' && (await sw.evaluate(() => self.BCV.background.app.revision)) === 4, `is written up to the app once, whole, and the new revision is taken as known (${up.length} write, revision ${await sw.evaluate(() => self.BCV.background.app.revision)})`);
  await sw.evaluate(() => self.BCV.background.syncApp());
  s = await settings();
  check(s.appearance.skin === false && (await sw.evaluate(() => self.__native.log.filter((m) => m.type === 'setSettings').length)) === 1, 'the next sync has nothing new: the write is not echoed back down or up again');

  console.log('a wipe from the app, and an app with no settings');
  await sw.evaluate(() => { const st = self.__native.store; st.settings = null; st.revision = 10; st.commands = [{ id: 'c2', type: 'wipe' }]; });
  await sw.evaluate(() => self.BCV.background.syncApp());
  const left = await sw.evaluate(async () => Object.keys(await self.BCV.api.storage.local.get(null)));
  const stored = await sw.evaluate(() => self.__native.store);
  s = await settings();
  check(!left.includes('prefs:canvas.test') && !left.includes('site:last') && (await log()).includes('done:c2'), `the wipe clears the extension's storage and is reported done (${left.join(',') || 'nothing left'})`);
  check(stored.settings && stored.settings.appearance.skin === true && stored.settings.appearance.darkMode === 'system' && stored.revision === 11 && s.appearance.skin === true, `an app with no settings is handed the extension's, which are the defaults after the wipe (revision ${stored.revision})`);

  console.log('settings open the app');
  await sw.evaluate(() => self.BCV.background.openOptions());
  check((await sw.evaluate(() => self.__native.opened)) === 1, 'Settings (the popup, the account panel) asks the app to come forward');
} catch (e) {
  console.error('crashed:', e?.stack || e);
  failures.push('crash: ' + e.message);
} finally {
  await context.close();
  rmSync(userDataDir, { recursive: true, force: true });
  rmSync(extDir, { recursive: true, force: true });
}
console.log(failures.length ? `\n${failures.length} check(s) failed:\n - ${failures.join('\n - ')}` : '\nAll checks passed.');
process.exit(failures.length ? 1 : 0);
