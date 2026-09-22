#!/usr/bin/env node
// Every suite, in the least wall-clock time: the smoke suite's two halves in a lane each, and
// everything else one after another in a third lane beside them (each suite has its own mock port
// and its own browser profile, so the lanes never meet). One line per suite as it finishes, the
// failed checks of any suite that failed, the total at the end; the exit code is the number of
// suites that failed. Each suite's full output is kept in scripts/dev/out/logs/<suite>.log.
//
//   node scripts/dev/test-all.mjs                 # everything, three lanes
//   node scripts/dev/test-all.mjs --serial        # one suite at a time (a small machine)
//   node scripts/dev/test-all.mjs --only smoke,phone
//   node scripts/dev/test-all.mjs --skip chrome-setup
import { spawn } from 'node:child_process';
import { mkdirSync, openSync, closeSync, readFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const logs = join(root, 'scripts', 'dev', 'out', 'logs');
mkdirSync(logs, { recursive: true });

// the lanes: the smoke suite's two halves each alone (smoke-test.mjs --part 1|2: the screens, and
// the setup + the tools + getting unstuck), the rest in turn (roughly longest first, so the lane ends sooner)
const LANES = [
  ['smoke:1'],
  ['smoke:2'],
  ['phone', 'chrome-setup', 'mac-window', 'side-courses', 'app-sync', 'group-late', 'null', 'api'],
];
const args = process.argv.slice(2);
const opt = (name) => { const i = args.indexOf(name); return i >= 0 ? (args[i + 1] || '') : null; };
const only = opt('--only')?.split(',').map((s) => s.trim()).filter(Boolean);
const skip = new Set((opt('--skip') || '').split(',').map((s) => s.trim()).filter(Boolean));
const suiteOf = (s) => s.split(':')[0]; // smoke:1 → smoke
const wanted = (s) => (!only || only.includes(suiteOf(s)) || only.includes(s)) && !skip.has(suiteOf(s)) && !skip.has(s);
let lanes = LANES.map((l) => l.filter(wanted)).filter((l) => l.length);
if (args.includes('--serial')) lanes = [[...new Set(lanes.flat().map(suiteOf))]]; // (one after another: the smoke suite whole)
if (!lanes.length) { console.log('nothing to run'); process.exit(0); }

const secs = (ms) => `${(ms / 1000).toFixed(1)}s`;
const children = new Set();
process.on('SIGINT', () => { for (const c of children) c.kill('SIGINT'); process.exit(130); });

/** Runs one suite to its log; returns { name, code, passed, failed, ms, failures } */
function run(name) {
  return new Promise((resolve) => {
    const [suite, part] = name.split(':');
    const log = join(logs, `${name.replace(':', '-')}.log`);
    const fd = openSync(log, 'w');
    const t = Date.now();
    const child = spawn(process.execPath, [join(root, 'scripts', 'dev', `${suite}-test.mjs`), ...(part ? ['--part', part] : [])], { cwd: root, stdio: ['ignore', fd, fd] });
    children.add(child);
    child.on('exit', (code) => {
      children.delete(child);
      closeSync(fd);
      const text = readFileSync(log, 'utf8');
      const passed = (text.match(/^\s*✓/gm) || []).length;
      const failed = (text.match(/^\s*✗/gm) || []).length;
      const tail = text.match(/\d+ check\(s\) failed:\n([\s\S]*?)(?:\n\(\d+\.\ds\)|\n*$)/);
      const failures = tail ? tail[1].split('\n').filter((l) => l.startsWith(' - ')).map((l) => l.slice(3)) : [];
      const crash = text.match(/(?:crashed|failed to start)[^\n]*/)?.[0] || null;
      resolve({ name, code: code ?? 1, passed, failed, ms: Date.now() - t, failures, crash, log });
    });
  });
}

const t0 = Date.now();
const results = [];
const report = (r) => {
  const ok = r.code === 0 && r.failed === 0;
  console.log(`${ok ? '✓' : '✗'} ${r.name.padEnd(13)} ${String(r.passed).padStart(4)} passed${r.failed ? `, ${r.failed} failed` : ''}${r.code && !r.failed ? ` (exit ${r.code})` : ''}  ${secs(r.ms)}`);
  if (!ok) {
    for (const f of r.failures) console.log(`    - ${f}`);
    if (r.crash) console.log(`    ${r.crash}`);
    if (!r.failures.length && !r.crash) console.log(`    (see ${r.log})`);
  }
};
console.log(`${lanes.length} lane${lanes.length === 1 ? '' : 's'}: ${lanes.map((l) => l.join(' → ')).join('  ‖  ')}\n`);
await Promise.all(lanes.map(async (lane) => {
  for (const name of lane) {
    const r = await run(name);
    results.push(r);
    report(r);
  }
}));
const bad = results.filter((r) => r.code !== 0 || r.failed);
const total = results.reduce((n, r) => n + r.passed, 0);
console.log(`\n${bad.length ? `${bad.length} suite${bad.length === 1 ? '' : 's'} failed` : 'All suites passed'}: ${total} checks, ${secs(Date.now() - t0)} wall (${secs(results.reduce((n, r) => n + r.ms, 0))} of suites)`);
process.exit(bad.length);
