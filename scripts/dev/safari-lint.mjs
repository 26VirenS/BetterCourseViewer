// Safari lint: what in the extension's scripts and styles Safari cannot do, or could not do until a
// version newer than the floor the Mac app supports.
//
// The suites run in Chromium; the product is a Safari extension. So every feature that Safari lacks
// (there is no WebKit here to run) is checked by hand, against a table: each script API or CSS feature
// with the Safari version that first had it, or `null` for one Safari has never had. A script API that
// Safari lacks must be guarded on the same line — `typeof x`, `window.x ?`, `self.x ||`, `'x' in`,
// `?.` — or the line fails the lint. A CSS property Safari lacks is ignored by Safari and does no
// harm on its own, so it only fails when a rule would need it to work (the table says which); a CSS
// feature newer than the floor is listed so the fallback beside it can be checked.
//
//   node scripts/dev/safari-lint.mjs [extensionDir] [--floor 17.0] [--json]
//
// Exits 1 on any failure. The `api` lane of the runner runs it over extension/ (scripts/dev/test-all.mjs).
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative, extname } from 'node:path';

const args = process.argv.slice(2);
const root = args.find((a) => !a.startsWith('--')) || join(process.cwd(), 'extension');
const floorAt = args.indexOf('--floor');
export const FLOOR = floorAt >= 0 ? args[floorAt + 1] : '17.0'; // macOS 13 Ventura's last Safari is 18; an unupdated one is 17
const asJson = args.includes('--json');

const ver = (s) => s.split('.').map(Number);
const older = (a, b) => { const x = ver(a), y = ver(b); for (let i = 0; i < Math.max(x.length, y.length); i++) { const d = (x[i] || 0) - (y[i] || 0); if (d) return d < 0; } return false; };

/** Script APIs: pattern → [first Safari with it (null: never), what to do]. `guard` is the pattern a
 *  guarding line must match (the default guards, or its own). */
const JS = [
  ['requestIdleCallback', null, 'guard it: window.requestIdleCallback ? … : setTimeout'],
  ['scrollend', null, "guard it: 'onscrollend' in window, with a settle timer beside it"],
  ['checkVisibility\\(', '17.4', 'guard it or use getBoundingClientRect'],
  ['Promise\\.withResolvers', '17.4', 'write the resolver by hand'],
  ['(Object|Map)\\.groupBy', '17.4', 'reduce instead'],
  ['URL\\.parse\\(', '18.0', 'new URL in a try'],
  ['startViewTransition', '18.0', 'guard it'],
  ['computedStyleMap\\(', null, 'getComputedStyle instead'],
  ['showOpenFilePicker|showSaveFilePicker', null, 'an <input type=file> / a download link'],
  ['for await', null, 'ReadableStream is not async-iterable in Safari: read with a reader loop'],
  ['\\.union\\(|\\.intersection\\(|\\.difference\\(|\\.symmetricDifference\\(', '17.0', 'set arithmetic by hand below the floor'],
  ['AbortSignal\\.any', '17.4', 'guard it'],
  ['\\bnavigator\\.userActivation', '16.4', 'guard it'],
  ['\\.isWellFormed\\(|\\.toWellFormed\\(', '17.0', 'guard it'],
  ['Intl\\.DurationFormat', '16.4', 'guard it'],
  ['Array\\.fromAsync', '16.4', 'guard it'],
  ['structuredClone\\(', '15.4', 'fine above the floor'],
  ['\\.toSorted\\(|\\.toReversed\\(|\\.toSpliced\\(', '16.0', 'fine above the floor'],
  ['\\.findLast\\(|\\.findLastIndex\\(', '15.4', 'fine above the floor'],
  ['adoptedStyleSheets', '16.4', 'a <style> element instead'],
  ['CSS\\.registerProperty', '16.4', 'guard it'],
  ['CSS\\.highlights', '17.2', 'guard it'],
  ['\\bPopoverInvoker|showPopover\\(|hidePopover\\(|togglePopover\\(', '17.0', 'guard it'],
  ['ContentVisibilityAutoStateChange', '18.0', 'guard it'],
  ['scheduler\\.postTask|scheduler\\.yield', null, 'setTimeout instead'],
  ['navigator\\.scheduling', null, 'guard it'],
  ['new (PerformanceObserver)', '11.0', 'fine'],
  ['\\bWebTransport\\b|\\bcompression\\b.*CompressionStream|new CompressionStream|new DecompressionStream', '16.4', 'guard it'],
  ['\\.at\\(-', '15.4', 'fine above the floor'],
  ['Object\\.hasOwn\\(', '15.4', 'fine above the floor'],
];
const GUARD = /typeof |window\.\w+ \?|self\.\w+ \?|window\.\w+ \|\||self\.\w+ \|\||'\w+' in |"\w+" in |\?\.\w|catch|supports\(/;

/** CSS features: pattern → [first Safari with it (null: never), whether a rule NEEDS it to work
 *  (`needs`: the lint fails below the floor unless the file has a fallback line — a `-webkit-`
 *  twin, an `@supports`, or a plain value before it), what to do]. */
const CSS = [
  ['(^|[^-])backdrop-filter\\s*:', '18.0', 'prefixed', 'write -webkit-backdrop-filter beside it (Safari 9–17 read only the prefix)'],
  ['corner-shape\\s*:', null, 'harmless', 'Safari ignores it: the corners stay round there, nothing else may depend on it'],
  ['linear\\(', '17.2', 'fallback', 'a cubic-bezier fallback for the same token, or the value in a var() with a fallback'],
  ['text-wrap\\s*:\\s*balance', '17.5', 'harmless', 'fine: the line just wraps as it would'],
  ['text-wrap\\s*:\\s*pretty', null, 'harmless', 'fine: ignored'],
  ['content-visibility\\s*:', '18.0', 'harmless', 'fine: the content is simply drawn'],
  ['contain-intrinsic-size', '17.0', 'harmless', 'fine'],
  ['@starting-style', '17.5', 'fallback', 'an entrance keyframe as well, or nothing enters on 17.0'],
  ['transition-behavior', '17.4', 'harmless', 'fine'],
  ['light-dark\\(', '17.5', 'fallback', 'the two colours by theme attribute instead'],
  ['color-mix\\(', '16.2', 'fine', 'fine above the floor'],
  ['oklch\\(|oklab\\(', '15.4', 'fine', 'fine above the floor'],
  ['animation-timeline|scroll-timeline|view-timeline', null, 'needs', 'scroll-driven animation never runs in Safari: script it or drop it'],
  ['anchor-name\\s*:|position-anchor\\s*:|anchor\\(', null, 'needs', 'anchor positioning never places anything in Safari: place it in script (ui.anchor)'],
  ['field-sizing', null, 'harmless', 'fine: ignored'],
  ['scrollbar-width|scrollbar-gutter', '18.2', 'harmless', 'fine'],
  ['view-transition-name', '18.0', 'harmless', 'fine'],
  ['@scope\\b', '17.4', 'needs', 'scoped rules are dropped whole below 17.4'],
  ['^\\s*&[\\s.:>#\\[]', '17.2', 'needs', 'CSS nesting: the nested rule is dropped whole below 17.2 — write it flat'],
  ['margin-trim', '16.4', 'harmless', 'fine'],
  ['@container\\b|container-type', '16.0', 'fine', 'fine above the floor'],
  ['text-box-trim|text-box-edge', null, 'harmless', 'fine: ignored'],
  ['@property\\b', '16.4', 'fine', 'fine above the floor'],
  ['inset-area|position-area', null, 'needs', 'never in Safari'],
];

const SKIP = /\/vendor\//;
const files = [];
(function walk(dir) {
  for (const name of readdirSync(dir)) {
    const p = join(dir, name);
    if (SKIP.test(p)) continue;
    const st = statSync(p);
    if (st.isDirectory()) walk(p);
    else if (/\.(js|css|html)$/.test(name)) files.push(p);
  }
})(root);

const findings = []; // { file, line, text, rule, since, level: 'fail' | 'note', fix }
for (const file of files) {
  const src = readFileSync(file, 'utf8');
  const lines = src.split('\n');
  const rel = relative(root, file);
  const isCss = extname(file) === '.css' || /<style/.test(src);
  const isJs = extname(file) === '.js' || /<script/.test(src);
  lines.forEach((text, i) => {
    const line = i + 1;
    const trimmed = text.trim();
    if (/^(\/\/|\*|\/\*)/.test(trimmed)) return; // a comment line
    if (isJs) {
      for (const [pat, since, fix] of JS) {
        if (!new RegExp(pat).test(text)) continue;
        const guarded = GUARD.test(text);
        const below = since === null || older(FLOOR, since);
        if (below && !guarded) findings.push({ file: rel, line, text: trimmed.slice(0, 140), rule: pat, since, level: 'fail', fix });
        else if (below) findings.push({ file: rel, line, text: trimmed.slice(0, 140), rule: pat, since, level: 'note', fix: `guarded: ${fix}` });
      }
    }
    if (isCss) {
      for (const [pat, since, kind, fix] of CSS) {
        if (!new RegExp(pat, 'm').test(text)) continue;
        const below = since === null || older(FLOOR, since);
        if (!below) continue;
        if (kind === 'prefixed') {
          // the prefixed twin has to be in the same declaration block: look back to the block's start
          let j = i, block = '';
          while (j >= 0 && !/\{/.test(lines[j]) ) { block = lines[j] + block; j--; }
          block = (lines[j] || '') + block;
          const bk = block.slice(block.lastIndexOf('{'));
          const ok = /-webkit-backdrop-filter\s*:/.test(bk) || /-webkit-backdrop-filter\s*:/.test(text);
          findings.push({ file: rel, line, text: trimmed.slice(0, 140), rule: pat, since, level: ok ? 'note' : 'fail', fix: ok ? 'prefixed twin present' : fix });
        } else if (kind === 'needs') findings.push({ file: rel, line, text: trimmed.slice(0, 140), rule: pat, since, level: 'fail', fix });
        else if (kind === 'fallback') {
          const ok = /cubic-bezier|@supports|,\s*[a-z-]+\)/.test(text) || /var\(--[\w-]+,\s*[^)]+\)/.test(text);
          findings.push({ file: rel, line, text: trimmed.slice(0, 140), rule: pat, since, level: ok ? 'note' : 'fail', fix: ok ? 'a fallback is on the line' : fix });
        } else findings.push({ file: rel, line, text: trimmed.slice(0, 140), rule: pat, since, level: 'note', fix });
      }
    }
  });
}

const fails = findings.filter((f) => f.level === 'fail');
const notes = findings.filter((f) => f.level === 'note');
if (asJson) console.log(JSON.stringify({ floor: FLOOR, root, files: files.length, fails, notes }, null, 2));
else {
  console.log(`Safari lint · floor ${FLOOR} · ${files.length} files under ${root}`);
  const byRule = (list) => { const m = new Map(); for (const f of list) { const k = `${f.rule}${f.since ? ` (Safari ${f.since})` : ' (never in Safari)'}`; m.set(k, [...(m.get(k) || []), f]); } return m; };
  for (const [k, list] of byRule(notes)) console.log(`  · ${list.length} × ${k}: ${list[0].fix}`);
  for (const f of fails) console.log(`  ✗ ${f.file}:${f.line} — ${f.rule}${f.since ? ` needs Safari ${f.since}` : ' is never in Safari'}; ${f.fix}\n      ${f.text}`);
  console.log(fails.length ? `${fails.length} failing line${fails.length === 1 ? '' : 's'}` : 'nothing Safari cannot do');
}
process.exit(fails.length ? 1 : 0);
