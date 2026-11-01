// Simpl Community tools, the site's side: the handler behind simplcourses.com/api/community/tools on its own, over a
// stand-in for Workers KV — the gallery (Simpl's starters with the approved ones), one tool, a tool sent in and what is
// refused (with which code), the hidden field, the hourly limit and the waiting list's cap, the owner told, the review
// (its passcode, approve, turn down, take down) beside the themes'; Simpl's starters, each checked strictly and worked
// out; the reference's answers (tool-vectors.mjs --check); and then the pages themselves in Chromium, served with that
// handler: the gallery's Tools (a tool working in its card, the search and the groups, a phone's width, dark), the maker
// (a formula's problem as it is typed, then a working preview; Share sends a tool the site takes; the draft kept) and
// the review (a tool waiting, drawn working with its JSON; approved into the gallery; taken down).
// Run: node scripts/dev/community-tools-test.mjs
import { createRequire } from 'node:module';
import { execSync, spawnSync } from 'node:child_process';
import { createServer } from 'node:http';
import { readFileSync, existsSync, statSync, mkdirSync } from 'node:fs';
import { join, extname, normalize } from 'node:path';
import { fastMotion } from './harness.mjs';
const require = createRequire(import.meta.url);
let chromium;
try { ({ chromium } = require('playwright')); } catch { const p = execSync('npm root -g').toString().trim(); ({ chromium } = createRequire(join(p, 'x.js'))('playwright')); }

const root = new URL('../..', import.meta.url).pathname;
const out = join(root, 'scripts', 'dev', 'out');
mkdirSync(out, { recursive: true });
const C = await import(join(root, 'site', 'src', 'community.js'));
const F = await import(join(root, 'site', 'public', 'community', 'tool-format.js'));
const shim = await import(join(root, 'site', 'src', 'tool-format.js'));
const S = await import(join(root, 'site', 'src', 'tool-starters.js'));
const failures = [];
const check = (ok, label) => { console.log(`  ${ok ? '✓' : '✗'} ${label}`); if (!ok) failures.push(label); };

/** Workers KV, as far as the handler uses it. */
function memoryKV() {
  const m = new Map();
  return {
    m,
    async get(key, type) { const v = m.get(key); if (!v) return null; return type === 'arrayBuffer' || type?.type === 'arrayBuffer' ? v.value : typeof v.value === 'string' ? v.value : new TextDecoder().decode(v.value); },
    async getWithMetadata(key) { const v = m.get(key); return v ? { value: v.value instanceof Uint8Array ? v.value.buffer.slice(v.value.byteOffset, v.value.byteOffset + v.value.byteLength) : v.value, metadata: v.metadata || null } : { value: null, metadata: null }; },
    async put(key, value, opts = {}) { m.set(key, { value: value instanceof ArrayBuffer ? new Uint8Array(value) : value, metadata: opts.metadata }); },
    async delete(key) { m.delete(key); },
  };
}
const KEY = 'tools passcode for the test';
const kv = memoryKV();
const hooked = [];
const fake = async (url, init) => { hooked.push(String(init.body)); return new Response('ok'); };
const env = { COMMUNITY: kv, COMMUNITY_KEY: KEY, REPORT_WEBHOOK: 'https://hooks.slack.com/services/x' };
let ipN = 0;
const call = (path, { method = 'GET', body, headers = {}, ip } = {}) => C.handleCommunity(new Request(`https://simplcourses.com/api/community/${path}`, { method, body, headers: { 'cf-connecting-ip': ip || `10.1.0.${++ipN}`, ...headers } }), env, fake);
const send = (tool, extra = {}, ip) => call('tools', { method: 'POST', body: JSON.stringify({ tool, website: '', ...extra }), headers: { 'content-type': 'application/json' }, ip });
const body = async (res) => res.json();
const today = new Date().toISOString().slice(0, 10);
const good = {
  format: 1, name: 'Box Volume', maker: 'Ana', summary: 'Length × width × height.', group: 'math', symbol: 'ruler', color: '#30B0C7',
  inputs: [{ id: 'l', kind: 'number', label: 'Length', unit: 'cm', default: 2 }, { id: 'w', kind: 'number', label: 'Width', unit: 'cm', default: 3 }, { id: 'h', kind: 'number', label: 'Height', unit: 'cm', default: 4 }],
  outputs: [{ label: 'Volume', value: 'l * w * h', unit: 'cm³', decimals: 2 }],
};

console.log('the reference, in one place');
{
  check(shim.validateTool === F.validateTool && shim.runTool === F.runTool && shim.formatNumber === F.formatNumber, 'site/src/tool-format.js is the public reference itself (re-exported), not a second copy');
  const r = spawnSync(process.execPath, [join(root, 'scripts', 'dev', 'tool-vectors.mjs'), '--check'], { encoding: 'utf8' });
  check(r.status === 0, `docs/community-tools-vectors.json is what the reference gives (tool-vectors.mjs --check): ${(r.stdout || r.stderr).trim()}`);
  const ctor = F.validateTool({ ...good, outputs: [{ label: 'X', value: 'constructor(l)' }] });
  check(ctor.error?.code === 'SC-T-FORMULA' && Number.isNaN(F.evaluate(F.parse('constructor(1)'), { ...F.CONSTANTS })), `a name every JavaScript object has is no function: constructor(…) refused (${ctor.error?.code}), and worth NaN if ever worked out`);
  const lone = F.validateTool({ ...good, name: 'Lone \ud800 half', maker: `${'m'.repeat(29)}\u{1F600}` }).tool;
  check(lone?.name === 'Lone \ufffd half' && lone.maker === 'm'.repeat(29), `text stays well-formed: a lone half of a surrogate pair becomes U+FFFD, and a cut never splits a pair (${JSON.stringify([lone?.name, lone?.maker.length])})`);
  const p = F.formulaProblem('a +', ['a']), q = F.formulaProblem('b * 2', ['a'], ['b']), ok = F.formulaProblem('a * pi', ['a']);
  check(p?.code === 'SC-T-FORMULA' && q?.code === 'SC-T-NAME-USE' && /before it is worked out/.test(q.message) && ok === null, `formulaProblem (the maker's live check) says what validateTool says: ${JSON.stringify([p, q, ok])}`);
}

console.log('Simpl’s starters');
{
  const ids = S.STARTER_SOURCES.map((t) => t.id);
  check(S.STARTERS.length === S.STARTER_SOURCES.length && S.STARTERS.length >= 6 && S.STARTERS.length <= 8, `${S.STARTERS.length} starters, every one taken (strictly checked)`);
  check(JSON.stringify(ids) === JSON.stringify(['s-ohms', 's-quadratic', 's-percent', 's-tip', 's-compound', 's-density', 's-energy', 's-gpa']), `their ids are the stable ones the apps keep them by: ${ids.join(' ')}`);
  for (const src of S.STARTER_SOURCES) {
    const got = F.validateTool(src, { strict: true });
    const runs = got.tool ? F.runTool(got.tool) : [];
    check(!got.error && got.tool.maker === 'Simpl' && got.tool.version === 1 && got.tool.created && JSON.stringify(src).length < F.LIMITS.json && runs.filter((r) => r.shown).every((r) => r.text !== '—'), `${src.id}: valid strictly, by Simpl, and every result shown at its defaults is a number: ${runs.filter((r) => r.shown).map((r) => `${r.label} ${r.text}${r.unit ? ` ${r.unit}` : ''}`).join(' · ')}`);
  }
  const by = (id) => S.STARTERS.find((t) => t.id === id);
  const shown = (id, given) => F.runTool(by(id), given).filter((r) => r.shown).map((r) => `${r.label}=${r.text}`).join(', ');
  const expect = [
    ['s-ohms', {}, 'Current=2, Power=24'], ['s-ohms', { find: 2, i: 1.5, r: 10 }, 'Voltage=15, Power=22.5'], ['s-ohms', { find: 3, v: 9, i: 0.5 }, 'Resistance=18, Power=4.5'],
    ['s-quadratic', {}, 'x₁=2, x₂=1, Discriminant=1, Real roots=2, Vertex x=1.5, Vertex y=-0.25'], ['s-quadratic', { a: 1, b: 2, c: 5 }, 'x₁=—, x₂=—, Discriminant=-16, Real roots=0, Vertex x=-1, Vertex y=4'], ['s-quadratic', { a: 0, b: 2, c: -4 }, 'x₁=2, Discriminant=4, Real roots=1'],
    ['s-percent', {}, 'Change=25, Difference=20, New as a share of old=125'], ['s-percent', { from: 50, to: 40 }, 'Change=-20, Difference=-10, New as a share of old=80'],
    ['s-tip', {}, 'Each pays=25.17, Tip=11.52, Total=75.52'], ['s-tip', { roundup: true }, 'Each pays=26, Tip=11.52, Total=75.52, Paid in all=78, Tip with rounding=21.9'],
    ['s-compound', {}, 'Balance=1647.01, Put in=1000, Interest earned=647.01, Doubling time=13.9'], ['s-compound', { rate: 0, add: 100 }, 'Balance=13,000, Put in=13,000, Interest earned=0'],
    ['s-density', {}, 'Density=2.7, Density in SI=2700'], ['s-density', { find: 2, rho: 19.3, vol: 2 }, 'Mass=38.6, Density in SI=19,300'],
    ['s-energy', {}, 'Kinetic energy=9, Potential energy=98.1, Total energy=107.1, Speed at the ground=10.35, Momentum=6'],
    ['s-gpa', {}, 'GPA=3.67, Credits=9, Quality points=33'], ['s-gpa', { c1: 0, c2: 0, c3: 0 }, 'GPA=—, Credits=0, Quality points=0'],
  ];
  for (const [id, given, want] of expect) { const got = shown(id, given); check(got === want, `${id} ${JSON.stringify(given)}: ${got}`); }
}

console.log('the handler: the gallery and one tool');
{
  const none = await C.handleCommunity(new Request('https://simplcourses.com/api/community/tools'), {});
  const nb = await body(none);
  const nopost = await C.handleCommunity(new Request('https://simplcourses.com/api/community/tools', { method: 'POST', body: '{}' }), {});
  check(none.ok && nb.tools.length === S.STARTERS.length && nopost.status === 503 && (await body(nopost)).code === 'SC-C-503', 'no namespace bound yet: the gallery is Simpl’s starters all the same; nothing can be sent in (SC-C-503)');
  const res = await call('tools');
  const b = await body(res);
  check(res.ok && b.ok && b.tools.length === S.STARTERS.length && b.tools.every((t) => /^s-/.test(t.id) && t.format === 1 && !('status' in t)) && res.headers.get('access-control-allow-origin') === '*' && /public, max-age=60/.test(res.headers.get('cache-control')), 'the gallery: the starters, open to any app (CORS *), kept a minute');
  const one = await call('tools/s-ohms');
  const ob = await body(one);
  check(one.ok && ob.tool?.name === 'Ohm’s Law' && one.headers.get('access-control-allow-origin') === '*', 'one tool by its id: { ok, tool }');
  const miss = await call('tools/nothing-here');
  const bad = await call('tools/../theme');
  check(miss.status === 404 && (await body(miss)).code === 'SC-C-404' && bad.status === 404, 'a tool that is not there: SC-C-404');
  const pre = await call('tools', { method: 'OPTIONS', headers: { origin: 'https://example.app', 'access-control-request-method': 'POST' } });
  check(pre.status === 204 && pre.headers.get('access-control-allow-origin') === '*' && /POST/.test(pre.headers.get('access-control-allow-methods')) && /content-type/.test(pre.headers.get('access-control-allow-headers')), 'a browser elsewhere asking first (OPTIONS): POST with JSON allowed');
  const put = await call('tools', { method: 'PUT', body: '{}' });
  check(put.status === 405 && (await body(put)).code === 'SC-C-405', 'anything but GET or POST: SC-C-405');
}

console.log('the handler: a tool sent in');
{
  const raw = (text, ip) => call('tools', { method: 'POST', body: text, headers: { 'content-type': 'application/json' }, ip });
  const cases = [
    [raw('{not json'), 'SC-T-JSON', 400, 'a body that is not JSON'],
    [raw('[1, 2]'), 'SC-T-JSON', 400, 'JSON that is not { tool, website }'],
    [raw('{"website": ""}'), 'SC-T-JSON', 400, 'no tool in it'],
    [raw(JSON.stringify({ tool: good, pad: 'x'.repeat(C.LIMITS.toolBody) })), 'SC-T-BIG', 413, 'a body over the limit'],
    [send({ ...good, note: 'n'.repeat(F.LIMITS.json) }), 'SC-T-BIG', 413, 'a tool over 32 KB as JSON (its body under the limit)'],
    [send({ ...good, format: 2 }), 'SC-T-FORMAT', 400, 'a later format'],
    [send({ ...good, script: 'alert(1)' }), 'SC-T-KEY', 400, 'a key format 1 does not have (strict)'],
    [send({ ...good, inputs: [{ ...good.inputs[0], onchange: 'x' }, ...good.inputs.slice(1)] }), 'SC-T-KEY', 400, 'an input with a key format 1 does not have'],
    [send({ ...good, outputs: [{ ...good.outputs[0], html: '<b>' }] }), 'SC-T-KEY', 400, 'a result with a key format 1 does not have'],
    [send({ ...good, name: '   ' }), 'SC-T-NAME', 400, 'no name'],
    [send({ ...good, color: 'teal' }), 'SC-T-COLOUR', 400, 'a colour that is not #rrggbb'],
    [send({ ...good, symbol: 'rocket' }), 'SC-T-SYMBOL', 400, 'a symbol Simpl does not have'],
    [send({ ...good, outputs: [{ label: 'V', value: 'l * w *' }] }), 'SC-T-FORMULA', 400, 'a formula that stops too soon'],
    [send({ ...good, outputs: [{ label: 'V', value: 'l * depth' }] }), 'SC-T-NAME-USE', 400, 'a formula reading a name that is not there'],
    [send({ ...good, outputs: [{ label: 'V', value: 'constructor(l)' }] }), 'SC-T-FORMULA', 400, 'constructor(…), which only JavaScript would have found'],
    [send({ ...good, inputs: [{ id: 'sqrt', kind: 'number', label: 'S' }] }), 'SC-T-ID', 400, 'a function’s name as an input’s'],
  ];
  const got = [];
  for (const [p, code, status, label] of cases) {
    const res = await p;
    const b = await body(res);
    got.push(b.code);
    check(!res.ok && res.status === status && b.ok === false && b.code === code && typeof b.message === 'string' && b.message.length > 3 && res.headers.get('access-control-allow-origin') === '*', `refused: ${label} (${b.code}, ${res.status}${b.path ? `, at ${b.path}` : ''}: ${b.message})`);
  }
  const at = await body(await send({ ...good, outputs: [{ label: 'V', value: 'l * w' }, { label: 'W', value: 'l +' }] }));
  check(at.path === 'outputs.1.value', `a refusal says where (path ${at.path}), for the maker to point at`);
  check(kv.m.size === 0 && hooked.length === 0, 'nothing refused is kept, and nobody is told of it');
  const trap = await send(good, { website: 'https://spam.example' });
  check(trap.ok && (await body(trap)).ok && kv.m.size === 0 && hooked.length === 0, 'the hidden field filled in: answered politely, nothing kept, nobody told');
}
let first;
{
  const res = await send({ ...good, id: 'mine-mine', version: 7, created: '1999-01-01', name: '  Box\tVolume ' });
  const b = await body(res);
  first = b.id;
  const rec = JSON.parse(await kv.get(`tool:${first}`));
  check(res.ok && b.ok && b.status === 'pending' && /^[a-z0-9]{8}$/.test(first) && res.headers.get('access-control-allow-origin') === '*', `a tool sent in is taken (id ${first}), waiting`);
  check(rec.status === 'pending' && rec.submitted === today && rec.tool.id === first && !('version' in rec.tool) && !('created' in rec.tool) && rec.tool.name === 'Box Volume' && rec.tool.color === '#30b0c7' && !F.validateTool(rec.tool, { strict: true }).error, `kept as checked: the site’s own id (not the sender’s), no version or day until approved, its text cleaned: ${JSON.stringify({ id: rec.tool.id, name: rec.tool.name, color: rec.tool.color })}`);
  check(JSON.parse(await kv.get('list:tools-pending'))[0] === first, 'it waits in list:tools-pending');
  check(hooked.length === 1 && /Community tool/.test(hooked[0]) && /Box Volume/.test(hooked[0]) && /\/community\/review/.test(hooked[0]), 'the owner is told, with where to review it');
  const g = await body(await call('tools'));
  const one = await call(`tools/${first}`);
  check(g.tools.length === S.STARTERS.length && one.status === 404, 'not in the gallery yet, and not to be fetched by its id');
}
// the hourly limit, and the waiting list's cap
{
  let held = false;
  for (let i = 0; i < C.LIMITS.toolSubmitsPerHour + 1; i++) { const r = await send({ ...good, name: `Many ${i}` }, {}, '10.8.8.8'); if (r.status === 429) held = (await body(r)).code === 'SC-C-429'; }
  check(held, `more than ${C.LIMITS.toolSubmitsPerHour} tools an hour from one address: held off (SC-C-429)`);
  const theme = new FormData();
  theme.set('theme', JSON.stringify({ name: 'Own limit', accent: '#5e5ce6' }));
  const t = await C.handleCommunity(new Request('https://simplcourses.com/api/community/submit', { method: 'POST', body: theme, headers: { 'cf-connecting-ip': '10.8.8.8' } }), env, fake);
  check(t.ok, 'the tools’ limit is their own: a theme from that address is still taken');
  const kept = await kv.get('list:tools-pending');
  await kv.put('list:tools-pending', JSON.stringify(Array.from({ length: C.LIMITS.toolsPendingMax }, (_, i) => `full${i}`)));
  const full = await send(good);
  check(full.status === 503 && (await body(full)).code === 'SC-C-FULL', `${C.LIMITS.toolsPendingMax} waiting already: no more for now (SC-C-FULL)`);
  await kv.put('list:tools-pending', kept);
}

console.log('the handler: the review');
let cookie;
{
  const right = await call('login', { method: 'POST', body: JSON.stringify({ key: KEY }) });
  cookie = (right.headers.get('set-cookie') || '').split(';')[0];
  const no = await call('review');
  const noDecide = await call('decide', { method: 'POST', body: JSON.stringify({ kind: 'tool', id: first, decision: 'approve' }) });
  const noHeader = await call('decide', { method: 'POST', body: JSON.stringify({ kind: 'tool', id: first, decision: 'approve' }), headers: { cookie } });
  const elsewhere = await call('decide', { method: 'POST', body: JSON.stringify({ kind: 'tool', id: first, decision: 'approve' }), headers: { cookie, 'x-simpl-review': '1', origin: 'https://evil.example' } });
  check(right.ok && no.status === 401 && noDecide.status === 401 && noHeader.status === 403 && elsewhere.status === 403 && JSON.parse(await kv.get(`tool:${first}`)).status === 'pending', 'without the passcode’s cookie, or from another page: no review, no decision (401, 403)');
  const r = await body(await call('review', { headers: { cookie } }));
  const p = r.tools?.pending?.find((x) => x.id === first);
  check(r.ok && Array.isArray(r.pending) && Array.isArray(r.approved) && p && p.submitted === today && p.tool.name === 'Box Volume' && Array.isArray(r.tools.approved) && r.tools.approved.length === 0, `the review: the themes as before, and the tools beside them — each waiting one with its day (${r.tools?.pending?.length} waiting)`);
}
const decide = (b) => call('decide', { method: 'POST', body: JSON.stringify(b), headers: { cookie, 'x-simpl-review': '1', origin: 'https://simplcourses.com' } });
const remove = (b) => call('remove', { method: 'POST', body: JSON.stringify(b), headers: { cookie, 'x-simpl-review': '1' } });
{
  const odd = await decide({ kind: 'tool', id: first, decision: 'maybe' });
  check(odd.status === 400 && JSON.parse(await kv.get('list:tools-pending')).includes(first), 'neither approve nor deny: refused, and it stays waiting');
  const res = await decide({ kind: 'tool', id: first, decision: 'approve' });
  const b = await body(res);
  check(res.ok && b.status === 'approved' && b.tool.id === first && b.tool.version === 1 && b.tool.created === today, `approved: given version 1 and the day (${b.tool?.created})`);
  const g = await body(await call('tools'));
  const t = g.tools.find((x) => x.id === first);
  const dates = g.tools.map((x) => x.created);
  check(g.tools.length === S.STARTERS.length + 1 && t && t.name === 'Box Volume' && t.format === 1 && !('status' in t) && dates.every((d, i) => !i || dates[i - 1] >= d) && !F.validateTool(t, { strict: true }).error, `now in the gallery, beside the starters, newest first: ${g.tools.map((x) => x.id).join(' ')}`);
  check(Object.keys(t).join() === 'format,id,version,name,maker,summary,group,symbol,color,inputs,outputs,created', `in the document’s own order: ${Object.keys(t).join(', ')}`);
  const one = await body(await call(`tools/${first}`));
  check(one.ok && one.tool.id === first, 'and to be fetched by its id');
  const again = await decide({ kind: 'tool', id: first, decision: 'deny' });
  check(again.status === 409 && JSON.parse(await kv.get('list:tools-pending')).every((x) => x !== first), 'decided twice: refused (SC-C-409); no longer waiting');
  const starter = await remove({ kind: 'tool', id: 's-ohms' });
  check(starter.status === 409 && (await body(await call('tools'))).tools.some((x) => x.id === 's-ohms'), 'Simpl’s own starters cannot be taken down');
  // one turned down
  const nb = await body(await send({ ...good, name: 'Turned down' }));
  const den = await decide({ kind: 'tool', id: nb.id, decision: 'deny' });
  check(den.ok && (await body(den)).status === 'denied' && !kv.m.get(`tool:${nb.id}`) && !JSON.parse(await kv.get('list:tools-pending')).includes(nb.id), 'turned down: gone, and no longer waiting');
  // the themes' review is as it was
  const tr = await body(await call('review', { headers: { cookie } }));
  const th = tr.pending.find((x) => x.name === 'Own limit');
  const ta = await decide({ id: th.id, decision: 'approve' });
  check(ta.ok && (await body(await call('themes'))).themes.some((x) => x.id === th.id), 'a theme decided beside them, with no kind given: approved as before');
  // one taken down
  const rm = await remove({ kind: 'tool', id: first });
  const after = await body(await call('tools'));
  check(rm.ok && (await body(rm)).status === 'removed' && !after.tools.some((x) => x.id === first) && !kv.m.get(`tool:${first}`) && (await call(`tools/${first}`)).status === 404, 'taken down: out of the gallery, and gone');
  const gone = await remove({ kind: 'tool', id: first });
  check(gone.status === 404, 'taken down twice: SC-C-404');
  // (JSON's \u escapes can carry half a character; what is kept, and later served, never does)
  const half = await body(await call('tools', { method: 'POST', body: '{"tool":' + JSON.stringify({ ...good, name: 'X' }).replace('"X"', '"Half \\ud83d"') + '}', headers: { 'content-type': 'application/json' } }));
  const hrec = half.ok ? JSON.parse(await kv.get(`tool:${half.id}`)) : null;
  check(hrec?.tool.name === 'Half \ufffd' && JSON.stringify(hrec.tool).isWellFormed(), `a name sent with half a character in it is kept well-formed: ${JSON.stringify(hrec?.tool.name)}`);
}

// ---- the pages, served with the handler -----------------------------------------------------------------------
console.log('the pages');
const pub = join(root, 'site', 'public');
const TYPES = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css', '.png': 'image/png', '.json': 'application/json', '.svg': 'image/svg+xml' };
const siteKV = memoryKV();
const siteEnv = { COMMUNITY: siteKV, COMMUNITY_KEY: KEY };
const site = createServer(async (req, res) => {
  const url = new URL(req.url, 'http://localhost');
  if (url.pathname.startsWith('/api/')) {
    const chunks = [];
    for await (const c of req) chunks.push(c);
    const r = await C.handleCommunity(new Request(`http://${req.headers.host}${url.pathname}${url.search}`, { method: req.method, headers: req.headers, body: ['GET', 'HEAD'].includes(req.method) ? undefined : Buffer.concat(chunks) }), siteEnv, fake);
    res.writeHead(r.status, Object.fromEntries(r.headers));
    res.end(Buffer.from(await r.arrayBuffer()));
    return;
  }
  // (the theme previews' shades: the extension's own theme code, copied in when the site is published)
  let p = url.pathname === '/js/theme.js' ? join(root, 'extension', 'lib', 'theme.js') : normalize(join(pub, decodeURIComponent(url.pathname)));
  if (!p.startsWith(pub) && !p.endsWith('theme.js')) { res.writeHead(404); res.end(); return; }
  if (existsSync(p) && statSync(p).isDirectory()) p = join(p, 'index.html');
  if (!existsSync(p)) { res.writeHead(404); res.end('not found'); return; }
  res.writeHead(200, { 'content-type': TYPES[extname(p)] || 'application/octet-stream', 'x-content-type-options': 'nosniff' });
  res.end(readFileSync(p));
});
await new Promise((r) => site.listen(0, 'localhost', r));
const BASE = `http://localhost:${site.address().port}`;
const browser = await chromium.launch({ headless: true });
const until = async (fn, ms = 6000) => { const end = Date.now() + ms; for (;;) { const v = await fn().catch(() => null); if (v) return v; if (Date.now() > end) return null; await new Promise((r) => setTimeout(r, 60)); } };
const errors = [];
const watch = (page, name) => { page.on('pageerror', (e) => errors.push(`${name}: ${e.message}`)); page.on('console', (m) => { if (m.type() === 'error' && !/favicon|icon-128|apple-touch|Failed to load resource/.test(m.text())) errors.push(`${name}: ${m.text()}`); }); return page; };
const overflow = (page) => page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
const heroOf = (page, id) => page.locator(`[data-tool="${id}"] .tv-hero`).innerText().then((t) => t.replace(/\s+/g, ' ').trim());
try {
  // the gallery's Tools
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 }, colorScheme: 'light' });
  fastMotion(ctx);
  const page = watch(await ctx.newPage(), 'gallery');
  await page.goto(`${BASE}/community/?tab=tools`);
  const cards = await until(() => page.locator('.tcard').count().then((n) => (n === S.STARTERS.length ? n : null)));
  const shape = await page.evaluate(() => ({ tab: document.querySelector('[aria-selected="true"]')?.dataset.tab, themes: document.querySelector('[data-panel="themes"]').hidden, tools: document.querySelector('[data-panel="tools"]').hidden, tiles: document.querySelectorAll('.tcard .tv-tile svg path, .tcard .tv-tile svg circle').length > 8, nudge: /Tools page of Simpl for Mac and Simpl for Windows/.test(document.querySelector('[data-panel="tools"] .share').textContent), adds: document.querySelectorAll('.tcard [data-add], .tcard.theme').length }));
  await page.screenshot({ path: join(out, 'community-tools-tab.png') });
  check(cards === S.STARTERS.length && shape.tab === 'tools' && shape.themes && !shape.tools && shape.tiles && shape.nudge && shape.adds === 0, `?tab=tools: the Tools tab, a card for each tool with its symbol, and the nudge to add them in Simpl for Mac and Windows: ${JSON.stringify({ cards, ...shape })}`);
  const ohm0 = await heroOf(page, 's-ohms');
  await page.locator('[data-tool="s-ohms"] [data-input="v"]').fill('24');
  const ohm1 = await until(() => heroOf(page, 's-ohms').then((t) => (t !== ohm0 ? t : null)));
  check(ohm0 === 'Current 2 A' && ohm1 === 'Current 4 A', `a tool works in its card: Ohm’s Law’s voltage 12 → 24 V and its current follows (${ohm0} → ${ohm1})`);
  await page.locator('[data-tool="s-ohms"] [data-input="find"] [role="radio"]', { hasText: 'Resistance' }).click();
  const ohm2 = await heroOf(page, 's-ohms');
  await page.locator('[data-tool="s-tip"] .tv-toggle').click();
  const tipRows = await page.locator('[data-tool="s-tip"] [data-result]').evaluateAll((r) => r.map((x) => x.dataset.result));
  await page.locator('[data-tool="s-gpa"] select[data-input="g1"]').selectOption({ label: 'B' });
  const gpa = await heroOf(page, 's-gpa');
  check(ohm2 === 'Resistance 12 Ω' && tipRows.includes('Paid in all') && gpa === 'GPA 3.33', `a choice and a switch work too: Ohm’s Law finding resistance (${ohm2}), Tip & Split rounding up (${tipRows.join(', ')}), a GPA with a B (${gpa})`);
  await page.fill('[data-tsearch]', 'density');
  const found = await page.locator('.tcard:visible').evaluateAll((c) => c.map((x) => x.dataset.tool));
  await page.fill('[data-tsearch]', '');
  await page.click('[data-chips] [data-group="money"]');
  const money = await page.locator('.tcard:visible').evaluateAll((c) => c.map((x) => x.dataset.tool));
  await page.fill('[data-tsearch]', 'zebra');
  const nothing = await page.locator('[data-tools-state]').innerText();
  await page.fill('[data-tsearch]', '');
  await page.click('[data-chips] [data-group=""]');
  check(found.join() === 's-density' && money.join() === 's-compound' && /No tools match/.test(nothing), `the search (density: ${found}) and the groups (Money: ${money}); nothing found says so`);
  await page.click('[data-tab="themes"]');
  const themes = await page.evaluate(() => ({ tools: document.querySelector('[data-panel="tools"]').hidden, themes: document.querySelector('[data-panel="themes"]').hidden, url: location.search }));
  await page.goto(`${BASE}/community/?tool=s-energy`);
  const picked = await until(() => page.locator('[data-tool="s-energy"].is-picked').count().then((n) => n || null));
  check(themes.tools && !themes.themes && themes.url === '' && picked === 1, `the Themes tab as it was (its address plain again), and ?tool=<id> opens Tools at that tool: ${JSON.stringify(themes)}`);
  await page.screenshot({ path: join(out, 'community-tools-gallery.png'), fullPage: false });
  await ctx.close();

  // a phone, dark
  const phone = await browser.newContext({ viewport: { width: 390, height: 844 }, colorScheme: 'dark', deviceScaleFactor: 2, isMobile: true, hasTouch: true });
  const pp = watch(await phone.newPage(), 'phone');
  await pp.goto(`${BASE}/community/?tab=tools`);
  await until(() => pp.locator('.tcard').count().then((n) => (n === S.STARTERS.length ? n : null)));
  const dark = await pp.evaluate(() => ({ bg: getComputedStyle(document.body).backgroundColor, card: getComputedStyle(document.querySelector('.tcard')).backgroundColor, text: getComputedStyle(document.querySelector('.tv-name')).color }));
  const phoneOver = await overflow(pp);
  await pp.locator('[data-tool="s-percent"] [data-input="to"]').fill('40');
  const pct = await until(() => heroOf(pp, 's-percent').then((t) => (/-50/.test(t) ? t : null)));
  await pp.screenshot({ path: join(out, 'community-tools-phone-dark.png') });
  await pp.goto(`${BASE}/community/make/`);
  await pp.waitForSelector('[data-preview] .tv');
  const makerOver = await overflow(pp);
  check(phoneOver <= 0 && makerOver <= 0 && dark.bg === 'rgb(0, 0, 0)' && dark.card !== 'rgb(245, 245, 247)' && dark.text === 'rgb(245, 245, 247)' && !!pct, `a phone’s width, dark: the gallery and the maker fit with no sideways scroll (${phoneOver}, ${makerOver}), drawn dark (${JSON.stringify(dark)}), a tool still works (${pct})`);
  await phone.close();

  // the maker
  const mctx = await browser.newContext({ viewport: { width: 1280, height: 1000 }, colorScheme: 'light' });
  fastMotion(mctx);
  const mk = watch(await mctx.newPage(), 'maker');
  await mk.goto(`${BASE}/community/make/`);
  await mk.waitForSelector('[data-preview] .tv');
  const start = await mk.evaluate(() => ({ hero: document.querySelector('[data-preview] .tv-hero')?.innerText.replace(/\s+/g, ' '), share: document.querySelector('[data-share]').disabled, problem: document.querySelector('[data-problem]').textContent, syms: document.querySelectorAll('[data-syms] [data-symbol]').length }));
  check(/Area 12 cm²/.test(start.hero) && start.share && /name/.test(start.problem) && start.syms === F.SYMBOLS.length, `it starts working (${start.hero}), every symbol to pick from (${start.syms}), Share waiting on a name: ${start.problem}`);
  await mk.fill('[data-top="name"]', 'Box Volume');
  await mk.fill('[data-top="summary"]', 'Width times height times depth.');
  await mk.fill('[data-top="maker"]', 'Sam');
  await mk.click('[data-syms] [data-symbol="dice"]');
  await mk.click('[data-swatches] [data-color="#ff9500"]');
  await mk.click('[data-add-input]');
  await mk.locator('[data-inputs] .item').nth(2).locator('[data-f="label"]').fill('Depth');
  const autoId = await mk.locator('[data-inputs] .item').nth(2).locator('[data-f="id"]').inputValue();
  await mk.locator('[data-inputs] .item').nth(2).locator('[data-f="def"]').fill('2');
  const out0 = mk.locator('[data-outputs] .item').nth(0);
  await out0.locator('[data-f="label"]').fill('Volume');
  await out0.locator('[data-f="unit"]').fill('cm³');
  await out0.locator('[data-f="value"]').fill('width * height *');
  const bad = await mk.evaluate(() => ({ err: document.querySelector('[data-err="outputs.0.value"]').textContent, invalid: document.querySelector('[data-outputs] [data-f="value"]').getAttribute('aria-invalid'), share: document.querySelector('[data-share]').disabled, problem: document.querySelector('[data-problem]').textContent }));
  await out0.locator('[data-f="value"]').fill('width * height * dept');
  const unknown = await mk.locator('[data-err="outputs.0.value"]').textContent();
  check(autoId === 'depth' && bad.err === 'The formula stops too soon.' && bad.invalid === 'true' && bad.share && /Before it can be shared: The formula stops too soon/.test(bad.problem) && unknown === 'There is nothing called “dept”.', `a formula’s problem shown as it is typed, in the reference’s words (“${bad.err}”, then “${unknown}”), Share held back; a new input named from its label (${autoId})`);
  await out0.locator('[data-f="value"]').fill('width * height * depth');
  const fixed = await until(() => mk.evaluate(() => {
    const hero = document.querySelector('[data-preview] .tv-hero')?.innerText.replace(/\s+/g, ' ');
    return /24 cm³/.test(hero || '') ? { hero, err: document.querySelector('[data-err="outputs.0.value"]').textContent, share: document.querySelector('[data-share]').disabled, name: document.querySelector('[data-preview] .tv-name').textContent, tile: document.querySelector('[data-preview] .tv-tile').style.background, can: document.querySelector('[data-outputs] [data-can]').textContent } : null;
  }));
  check(!!fixed && fixed.err === '' && !fixed.share && fixed.name === 'Box Volume' && /255, 149, 0|ff9500/.test(fixed.tile) && /width, height, depth/.test(fixed.can), `fixed: the preview works (${fixed?.hero}), named, in its colour, with what a formula can read (${fixed?.can}), and Share is ready`);
  await mk.locator('[data-preview] [data-input="width"]').fill('10');
  const pv1 = await until(() => mk.locator('[data-preview] .tv-hero').innerText().then((t) => (/60/.test(t) ? t.replace(/\s+/g, ' ') : null)));
  await out0.locator('[data-f="decimals"]').selectOption('1');
  const kept = await mk.locator('[data-preview] [data-input="width"]').inputValue();
  check(!!pv1 && kept === '10', `the preview’s own inputs work (width 10: ${pv1}), and keep what was put in while the tool is changed (${kept})`);
  // a choice and a table, then Share
  await mk.click('[data-add-input]');
  const ch = mk.locator('[data-inputs] .item').nth(3);
  await ch.locator('[data-f="label"]').fill('Units');
  await ch.locator('[data-f="kind"] [data-v="choice"]').click();
  await mk.locator('[data-inputs] .item').nth(3).locator('[data-opt="0"] [data-o="label"]').fill('cm');
  await mk.locator('[data-inputs] .item').nth(3).locator('[data-opt="1"] [data-o="label"]').fill('inches');
  await mk.locator('[data-inputs] .item').nth(3).locator('[data-opt="1"] [data-o="value"]').fill('16.387');
  await mk.check('[data-table-on]');
  await mk.fill('[data-table] [data-col="0"]', 'Box');
  await mk.fill('[data-table] [data-col="1"]', 'Litres');
  await mk.fill('[data-table] [data-row="0"] [data-cell="0"]', 'Shoebox');
  await mk.fill('[data-table] [data-row="0"] [data-cell="1"]', '8');
  await mk.fill('[data-top="note"]', 'V = w × h × d.');
  await until(() => mk.evaluate(() => !document.querySelector('[data-share]').disabled && !!document.querySelector('[data-preview] .tv-table') && document.querySelectorAll('[data-preview] [data-input="units"] [role="radio"]').length === 2));
  await mk.screenshot({ path: join(out, 'community-tools-maker.png'), fullPage: true });
  await mk.click('[data-share]');
  const sent = await until(() => mk.evaluate(() => { const n = document.querySelector('[data-sent]'); return n.dataset.kind ? { kind: n.dataset.kind, text: n.textContent, id: n.dataset.id, hidden: document.querySelector('[data-share]').hidden } : null; }), 8000);
  const recM = sent?.id ? JSON.parse((await siteKV.get(`tool:${sent.id}`)) || 'null') : null;
  const strictM = recM ? F.validateTool(recM.tool, { strict: true }) : { error: 'none kept' };
  check(sent?.kind === 'ok' && /waiting to be looked at/.test(sent.text) && sent.hidden && recM?.status === 'pending' && !strictM.error && recM.tool.name === 'Box Volume' && recM.tool.maker === 'Sam' && recM.tool.symbol === 'dice' && recM.tool.color === '#ff9500' && recM.tool.inputs.map((i) => `${i.id}:${i.kind}`).join() === 'width:number,height:number,depth:number,units:choice' && recM.tool.outputs[0].value === 'width * height * depth' && recM.tool.outputs[0].decimals === 1 && recM.tool.table?.rows?.[0]?.join() === 'Shoebox,8' && recM.tool.note === 'V = w × h × d.', `Share: thanks, waiting to be looked at — and what arrived is the tool as made, valid strictly: ${JSON.stringify(recM?.tool?.inputs?.map((i) => i.id))}, “${recM?.tool?.outputs?.[0]?.value}”`);
  await mk.reload();
  await mk.waitForSelector('[data-preview] .tv');
  const back = await mk.evaluate(() => ({ name: document.querySelector('[data-top="name"]').value, inputs: document.querySelectorAll('[data-inputs] .item').length, table: document.querySelector('[data-table-on]').checked, sym: document.querySelector('[data-syms] [aria-checked="true"]')?.dataset.symbol }));
  check(back.name === 'Box Volume' && back.inputs === 4 && back.table && back.sym === 'dice', `the draft is kept in this browser: back after a reload (${JSON.stringify(back)})`);
  // a refusal from the site is shown as it comes
  await mk.route('**/api/community/tools', (route) => route.fulfill({ status: 429, contentType: 'application/json', body: JSON.stringify({ ok: false, code: 'SC-C-429', message: 'That is a lot of tools at once. Try again in an hour.' }) }));
  await mk.fill('[data-top="summary"]', 'Width times height times depth, again.');
  await mk.click('[data-share]');
  const refused = await until(() => mk.evaluate(() => { const n = document.querySelector('[data-sent]'); return n.dataset.kind === 'bad' ? { text: n.textContent, share: document.querySelector('[data-share]').disabled } : null; }));
  await mk.unroute('**/api/community/tools');
  check(refused?.text === 'That is a lot of tools at once. Try again in an hour. (SC-C-429)' && refused.share === false, `a refusal is shown with its code, and Share can be tried again: ${refused?.text}`);
  // made from a gallery tool
  await mk.goto(`${BASE}/community/make/?from=s-gpa`);
  const fromGpa = await until(() => mk.evaluate(() => (document.querySelector('[data-top="name"]').value === 'Grade Points' ? { inputs: document.querySelectorAll('[data-inputs] .item').length, maker: document.querySelector('[data-top="maker"]').value, hero: document.querySelector('[data-preview] .tv-hero')?.innerText.replace(/\s+/g, ' '), url: location.search } : null)));
  check(fromGpa?.inputs === 10 && fromGpa.maker === '' && /GPA 3\.67/.test(fromGpa.hero) && fromGpa.url === '', `“Make your own from it”: a gallery tool opened in the maker, working, its maker left for you: ${JSON.stringify(fromGpa)}`);
  await mctx.close();

  // the review
  const rctx = await browser.newContext({ viewport: { width: 1280, height: 1000 } });
  const rv = watch(await rctx.newPage(), 'review');
  rv.on('dialog', (d) => d.accept());
  await rv.goto(`${BASE}/community/review/`);
  await rv.waitForSelector('[data-login]:not([hidden])');
  await rv.fill('#key', KEY);
  await rv.click('[data-login] button[type="submit"]');
  const card = rv.locator(`[data-tools-pending] [data-tool-card="${sent?.id}"]`);
  await card.waitFor({ timeout: 6000 }).catch(() => {});
  const seen = await card.evaluate((c) => ({ tv: !!c.querySelector('.tv .tv-hero'), json: JSON.parse(c.querySelector('pre').textContent).name, acts: [...c.querySelectorAll('.acts button')].map((b) => b.textContent).join(), warn: !!c.querySelector('.warn') })).catch(() => null);
  await card.locator('[data-input="width"]').fill('5').catch(() => {});
  const works = await until(() => card.locator('.tv-hero').innerText().then((t) => (/30/.test(t) ? t.replace(/\s+/g, ' ') : null)));
  check(!!seen && seen.tv && seen.json === 'Box Volume' && seen.acts === 'Approve,Turn down' && !seen.warn && !!works, `the review: the tool waiting beside the themes, drawn working (${works}), with its JSON, Approve and Turn down: ${JSON.stringify(seen)}`);
  await card.scrollIntoViewIfNeeded().catch(() => {});
  await rv.screenshot({ path: join(out, 'community-tools-review.png') });
  await card.locator('button', { hasText: 'Approve' }).click();
  const inGallery = await until(() => rv.locator(`[data-tools-approved] [data-tool-card="${sent?.id}"]`).count().then((n) => n || null));
  const api = await (await fetch(`${BASE}/api/community/tools`)).json();
  check(inGallery === 1 && api.tools.some((t) => t.id === sent?.id && t.version === 1), 'approved: under In the gallery, and in the gallery’s list');
  await rv.locator(`[data-tools-approved] [data-tool-card="${sent?.id}"] button`, { hasText: 'Take down' }).click();
  const down = await until(() => rv.locator(`[data-tools-approved] [data-tool-card="${sent?.id}"]`).count().then((n) => (n === 0 ? 'gone' : null)));
  const api2 = await (await fetch(`${BASE}/api/community/tools`)).json();
  check(down === 'gone' && !api2.tools.some((t) => t.id === sent?.id) && api2.tools.length === S.STARTERS.length, 'taken down from the review: out of the gallery');
  await rctx.close();
  check(errors.length === 0, `no errors on any page${errors.length ? `: ${errors.join(' | ')}` : ''}`);
} finally {
  await browser.close();
  site.close();
}

console.log(failures.length ? `\n${failures.length} check(s) failed:\n - ${failures.join('\n - ')}` : '\nAll checks passed.');
process.exit(failures.length ? 1 : 0);
