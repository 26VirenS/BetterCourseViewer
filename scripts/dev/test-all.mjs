#!/usr/bin/env node
// Every suite, in the least wall-clock time: a pool of workers (one per core by default) takes the
// suites longest first — each suite's time comes from the last run (scripts/dev/out/timings.json) —
// so the long ones start at once and the short ones fill the gaps. The smoke suite comes in shards
// (smoke-test.mjs --shard <name>), each with its own mock ports and browser profile, so any of them
// can run beside any other. One line per suite as it finishes, the failed checks of any suite that
// failed, the total at the end; the exit code is the number of suites that failed. Each suite's full
// output is kept in scripts/dev/out/logs/<suite>.log.
//
// Two things a run can no longer do to the machine: hang, and fill the disk. Each suite gets a
// temporary directory of its own (TMPDIR: its copy of the extension, its browser profile, the
// browser's own scratch files), removed when the suite ends however it ends; and a suite that
// writes nothing for --silent seconds (120 by default) is stopped, its browsers with it, and
// reported with the last thing it said.
//
//   node scripts/dev/test-all.mjs                   # everything
//   node scripts/dev/test-all.mjs --jobs 2          # two at a time (a small machine; --serial is one)
//   node scripts/dev/test-all.mjs --only smoke,phone
//   node scripts/dev/test-all.mjs --only smoke:widgets
//   node scripts/dev/test-all.mjs --skip chrome-setup
import { spawn, spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, openSync, closeSync, readFileSync, writeFileSync, statSync, rmSync, readdirSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { cpus, tmpdir } from 'node:os';
import { SMOKE_SHARDS } from './smoke-shards.mjs';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const logs = join(root, 'scripts', 'dev', 'out', 'logs');
const timingsFile = join(root, 'scripts', 'dev', 'out', 'timings.json');
mkdirSync(logs, { recursive: true });

// every suite; smoke as its shards (smoke-shards.mjs)
const SUITES = [...Object.keys(SMOKE_SHARDS).map((s) => `smoke:${s}`), 'phone', 'zoom', 'chrome-setup', 'mac-window', 'side-courses', 'app-sync', 'group-late', 'null', 'stale', 'report', 'api', 'ios', 'toolbar'];
const args = process.argv.slice(2);
const opt = (name) => { const i = args.indexOf(name); return i >= 0 ? (args[i + 1] || '') : null; };
const only = opt('--only')?.split(',').map((s) => s.trim()).filter(Boolean);
const skip = new Set((opt('--skip') || '').split(',').map((s) => s.trim()).filter(Boolean));
const suiteOf = (s) => s.split(':')[0]; // smoke:widgets → smoke
const wanted = (s) => (!only || only.includes(suiteOf(s)) || only.includes(s)) && !skip.has(suiteOf(s)) && !skip.has(s);
const SILENT_MS = (Number(opt('--silent')) || 120) * 1000;
const jobs = args.includes('--serial') ? 1 : Math.max(1, Number(opt('--jobs')) || cpus().length);

// the last run's times, longest first (a suite never timed goes first: it may be long)
let timings = {};
try { timings = JSON.parse(readFileSync(timingsFile, 'utf8')); } catch { /* first run */ }
const queue = SUITES.filter(wanted).sort((a, b) => (timings[b] ?? Infinity) - (timings[a] ?? Infinity));
if (!queue.length) { console.log('nothing to run'); process.exit(0); }

// a run killed half way leaves its suites' directories behind: the next run clears those whose runner is gone
const alive = (pid) => { try { process.kill(pid, 0); return true; } catch { return false; } };
for (const name of readdirSync(tmpdir())) {
  const m = name.match(/^bcv-run-(\d+)-/);
  if (m && !alive(Number(m[1]))) rmSync(join(tmpdir(), name), { recursive: true, force: true });
}

const secs = (ms) => `${(ms / 1000).toFixed(1)}s`;
const children = new Set();
/** Stops a suite and every browser it started (they run in process groups of their own, found by the suite's TMPDIR). */
function stop(child, dir) {
  try { child.kill('SIGTERM'); } catch { /* gone */ }
  setTimeout(() => {
    try { child.kill('SIGKILL'); } catch { /* gone */ }
    spawnSync('pkill', ['-KILL', '-f', '--', dir], { stdio: 'ignore' });
  }, 3000).unref();
}
const quit = () => { for (const { child, dir } of children) stop(child, dir); setTimeout(() => process.exit(130), 3500); };
process.on('SIGINT', quit);
process.on('SIGTERM', quit);

/** Runs one suite to its log; returns { name, code, passed, failed, ms, failures, crash, hung } */
function run(name) {
  return new Promise((resolve) => {
    const [suite, part] = name.split(':');
    const log = join(logs, `${name.replace(':', '-')}.log`);
    const fd = openSync(log, 'w');
    const dir = mkdtempSync(join(tmpdir(), `bcv-run-${process.pid}-${name.replace(':', '-')}-`));
    const t = Date.now();
    const argv = [join(root, 'scripts', 'dev', `${suite}-test.mjs`), ...(part ? ['--shard', part] : [])];
    const child = spawn(process.execPath, argv, { cwd: root, stdio: ['ignore', fd, fd], env: { ...process.env, TMPDIR: dir } });
    const entry = { child, dir };
    children.add(entry);
    // the watchdog: the log has to grow at least once every SILENT_MS
    let size = 0, quietSince = Date.now(), hung = null;
    const dog = setInterval(() => {
      let now = size;
      try { now = statSync(log).size; } catch { /* not yet */ }
      if (now !== size) { size = now; quietSince = Date.now(); return; }
      if (!hung && Date.now() - quietSince > SILENT_MS) {
        const last = readFileSync(log, 'utf8').trim().split('\n').pop() || '(nothing)';
        hung = `hung: no output for ${Math.round(SILENT_MS / 1000)}s after: ${last.trim().slice(0, 200)}`;
        stop(child, dir);
      }
    }, 2000);
    child.on('exit', (code) => {
      clearInterval(dog);
      children.delete(entry);
      closeSync(fd);
      spawnSync('pkill', ['-KILL', '-f', '--', dir], { stdio: 'ignore' }); // (a browser the suite left running)
      rmSync(dir, { recursive: true, force: true });
      const text = readFileSync(log, 'utf8');
      const passed = (text.match(/^\s*✓/gm) || []).length;
      const failed = (text.match(/^\s*✗/gm) || []).length;
      const tail = text.match(/\d+ check\(s\) failed:\n([\s\S]*?)(?:\n\(\d+\.\ds\)|\n*$)/);
      const failures = tail ? tail[1].split('\n').filter((l) => l.startsWith(' - ')).map((l) => l.slice(3)) : [];
      const crash = text.match(/(?:crashed|failed to start)[^\n]*/)?.[0] || null;
      resolve({ name, code: hung ? 1 : (code ?? 1), passed, failed, ms: Date.now() - t, failures, crash, hung, log });
    });
  });
}

const t0 = Date.now();
const results = [];
const report = (r) => {
  const ok = r.code === 0 && r.failed === 0;
  console.log(`${ok ? '✓' : '✗'} ${r.name.padEnd(18)} ${String(r.passed).padStart(4)} passed${r.failed ? `, ${r.failed} failed` : ''}${r.code && !r.failed && !r.hung ? ` (exit ${r.code})` : ''}  ${secs(r.ms)}`);
  if (!ok) {
    for (const f of r.failures) console.log(`    - ${f}`);
    if (r.hung) console.log(`    ${r.hung}`);
    else if (r.crash) console.log(`    ${r.crash}`);
    if (!r.failures.length && !r.crash && !r.hung) console.log(`    (see ${r.log})`);
  }
};
const width = Math.min(jobs, queue.length);
console.log(`${queue.length} suite${queue.length === 1 ? '' : 's'}, ${width} at a time, longest first\n`);
await Promise.all(Array.from({ length: width }, async () => {
  for (let name = queue.shift(); name; name = queue.shift()) {
    const r = await run(name);
    results.push(r);
    report(r);
  }
}));
// this run's times for the next run's order (a suite that failed keeps its old time: a crash is quick)
for (const r of results) if (r.code === 0 && !r.failed) timings[r.name] = r.ms;
try { writeFileSync(timingsFile, `${JSON.stringify(timings, null, 2)}\n`); } catch { /* read-only checkout */ }
const bad = results.filter((r) => r.code !== 0 || r.failed);
const total = results.reduce((n, r) => n + r.passed, 0);
console.log(`\n${bad.length ? `${bad.length} suite${bad.length === 1 ? '' : 's'} failed` : 'All suites passed'}: ${total} checks, ${secs(Date.now() - t0)} wall (${secs(results.reduce((n, r) => n + r.ms, 0))} of suites)`);
process.exit(bad.length);
