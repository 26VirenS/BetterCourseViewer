// Fits each spring preset of content/app/motion.js with the cubic-bezier() closest to it over its settle time, for the
// engines whose compositor cannot play a linear() easing (Safari's Core Animation: it would play the motion on the main
// thread instead, at the page's own rate). Prints the table motion.js carries as BEZIER; run it again when a preset changes.
//   node scripts/dev/fit-springs.mjs [--check]   (--check, quick: exit 1 if a curve of motion.js's table is off its spring by more than 2.5%)
import { readFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';
const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const src = readFileSync(join(root, 'extension/content/app/motion.js'), 'utf8');
const sandbox = { self: {}, console };
sandbox.self.self = sandbox.self;
vm.runInNewContext(src, sandbox);
const M = sandbox.self.BCV.motion;

/** y of the curve at x (bisection on the parameter: x(s) rises monotonically for x1, x2 in [0, 1]). */
function bez(x1, y1, x2, y2, x) {
  const c = (a, b, s) => 3 * a * s * (1 - s) ** 2 + 3 * b * s * s * (1 - s) + s ** 3;
  let lo = 0, hi = 1;
  for (let i = 0; i < 40; i++) { const m = (lo + hi) / 2; if (c(x1, x2, m) < x) lo = m; else hi = m; }
  return c(y1, y2, (lo + hi) / 2);
}
function errOf(p, target) {
  const [x1, y1, x2, y2] = p;
  if (x1 < 0 || x1 > 1 || x2 < 0 || x2 > 1) return 1e9;
  let e = 0, worst = 0;
  for (const [u, v] of target) { const d = bez(x1, y1, x2, y2, u) - v; e += d * d; worst = Math.max(worst, Math.abs(d)); }
  return e / target.length + worst * worst * 0.5;
}
function worstOf(p, target) { let w = 0; for (const [u, v] of target) w = Math.max(w, Math.abs(bez(...p, u) - v)); return w; }
/** Nelder–Mead from a few starts. */
function fit(target) {
  let best = null;
  for (const start of [[0.32, 0.72, 0, 1], [0.2, 0.9, 0.3, 1], [0.34, 1.15, 0.42, 1], [0.25, 1, 0.5, 1]]) {
    let simplex = [start, ...start.map((_, i) => start.map((v, j) => (i === j ? v + 0.1 : v)))].map((p) => ({ p, e: errOf(p, target) }));
    for (let it = 0; it < 4000; it++) {
      simplex.sort((a, b) => a.e - b.e);
      const n = 4, c = Array(n).fill(0);
      for (let i = 0; i < n; i++) for (let k = 0; k < n; k++) c[k] += simplex[i].p[k] / n;
      const w = simplex[n];
      const pt = (t) => c.map((v, k) => v + t * (w.p[k] - v));
      const r = { p: pt(-1) }; r.e = errOf(r.p, target);
      if (r.e < simplex[0].e) { const x = { p: pt(-2) }; x.e = errOf(x.p, target); simplex[n] = x.e < r.e ? x : r; }
      else if (r.e < simplex[n - 1].e) simplex[n] = r;
      else { const k = { p: pt(0.5) }; k.e = errOf(k.p, target); if (k.e < w.e) simplex[n] = k; else simplex = simplex.map((s, i) => (i === 0 ? s : { p: s.p.map((v, j) => simplex[0].p[j] + 0.5 * (v - simplex[0].p[j])) })).map((s) => ({ p: s.p, e: errOf(s.p, target) })); }
    }
    simplex.sort((a, b) => a.e - b.e);
    if (!best || simplex[0].e < best.e) best = simplex[0];
  }
  return best.p.map((v) => Math.round(v * 1000) / 1000);
}
if (process.argv.includes('--check')) {
  let off = false;
  for (const name of Object.keys(M.PRESETS)) {
    const sp = M.spring(name), T = sp.settle, p = M.BEZIER?.[name];
    const target = Array.from({ length: 121 }, (_, i) => [i / 120, i === 120 ? 1 : sp.at((T * i) / 120)]);
    const worst = p ? worstOf(p, target) : 1;
    console.log(`${name.padEnd(7)} ${p ? `cubic-bezier(${p.join(', ')})` : '(none)'} off by at most ${(worst * 100).toFixed(1)}%`);
    if (worst > 0.025) off = true;
  }
  process.exit(off ? 1 : 0);
}
const out = {};
let bad = false;
for (const name of Object.keys(M.PRESETS)) {
  const sp = M.spring(name), T = sp.settle;
  const target = Array.from({ length: 121 }, (_, i) => [i / 120, i === 120 ? 1 : sp.at((T * i) / 120)]);
  const p = fit(target);
  const worst = worstOf(p, target);
  out[name] = p;
  console.log(`${name.padEnd(7)} cubic-bezier(${p.join(', ')})  over ${Math.round(T * 1000)}ms, off by at most ${(worst * 100).toFixed(1)}%`);
  if (worst > 0.025) bad = true;
}
console.log(`\nconst BEZIER = ${JSON.stringify(out).replace(/"(\w+)":/g, ' $1: ').replace(/\]\,/g, '],')};`);
