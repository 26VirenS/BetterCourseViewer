// Simpl Community, the site's side (site/src/community.js): the handler behind simplcourses.com/api/community/* on its
// own, over a stand-in for Workers KV — a theme sent in and what is refused (and with which code), the owner told, the
// review's passcode and its cookie, a theme approved into the gallery, one turned down and one taken down (their photos
// with them), and what anyone can and cannot fetch on the way.
// Run: node scripts/dev/community-test.mjs
import { join } from 'node:path';

const root = new URL('../..', import.meta.url).pathname;
const C = await import(join(root, 'site', 'src', 'community.js'));
const failures = [];
const check = (ok, label) => { console.log(`  ${ok ? '✓' : '✗'} ${label}`); if (!ok) failures.push(label); };

/** Workers KV, as far as the handler uses it: text and bytes, with metadata. */
function memoryKV() {
  const m = new Map();
  return {
    m,
    async get(key, type) { const v = m.get(key); if (!v) return null; return type === 'arrayBuffer' || type?.type === 'arrayBuffer' ? v.value : typeof v.value === 'string' ? v.value : new TextDecoder().decode(v.value); },
    async getWithMetadata(key) { const v = m.get(key); return v ? { value: v.value instanceof Uint8Array ? v.value.buffer.slice(v.value.byteOffset, v.value.byteOffset + v.value.byteLength) : v.value, metadata: v.metadata || null } : { value: null, metadata: null }; },
    async put(key, value, opts = {}) { m.set(key, { value, metadata: opts.metadata }); },
    async delete(key) { m.delete(key); },
  };
}

const PNG = new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0, 0, 0, 13, 73, 72, 68, 82, 0, 0, 0, 1]);
const JPEG = new Uint8Array([0xff, 0xd8, 0xff, 0xe0, 0, 16, 74, 70, 73, 70]);
const KEY = 'correct horse battery staple';
const kv = memoryKV();
const hooked = [];
const fake = async (url, init) => { hooked.push(String(init.body)); return new Response('ok'); };
const env = { COMMUNITY: kv, COMMUNITY_KEY: KEY, REPORT_WEBHOOK: 'https://hooks.slack.com/services/x' };
let ipN = 0;
const call = (path, { method = 'GET', body, headers = {}, ip, now } = {}) => C.handleCommunity(new Request(`https://simplcourses.com/api/community/${path}`, { method, body, headers: { 'cf-connecting-ip': ip || `10.0.0.${++ipN}`, ...headers } }), env, fake, now);
const form = (theme, files = {}, extra = {}) => { const f = new FormData(); f.set('theme', typeof theme === 'string' ? theme : JSON.stringify(theme)); for (const [k, v] of Object.entries(files)) f.set(`photo-${k}`, new Blob([v]), `${k}.bin`); for (const [k, v] of Object.entries(extra)) f.set(k, v); return f; };
const submit = (theme, files, extra, ip) => call('submit', { method: 'POST', body: form(theme, files, extra), ip });
const body = async (res) => res.json();

console.log('Simpl Community: the handler');
// not switched on yet
{
  const res = await C.handleCommunity(new Request('https://simplcourses.com/api/community/themes'), {});
  check(res.status === 503 && (await body(res)).code === 'SC-C-503', 'no namespace bound: the gallery says it is not switched on (SC-C-503)');
}
// an empty gallery, open to the apps
{
  const res = await call('themes');
  const b = await body(res);
  check(res.ok && Array.isArray(b.themes) && b.themes.length === 0 && res.headers.get('access-control-allow-origin') === '*', 'the gallery starts empty, and any app or extension may read it');
}
// what is refused
const good = { name: 'Sunset Library', maker: 'Maya', accent: '#FF375F', look: 'dark', places: { side: { photo: true, ink: false, x: 40, y: 90, z: 1.2 }, today: { scene: 'Dusk', ink: false, x: 100, y: 100, z: 1 } } };
for (const [theme, files, code, label] of [
  ['{not json', {}, 'SC-C-THEME', 'a theme that is not JSON'],
  [{ ...good, name: '  ' }, { side: PNG }, 'SC-C-NAME', 'no name'],
  [{ ...good, accent: 'red' }, { side: PNG }, 'SC-C-COLOUR', 'a colour that is not #rrggbb'],
  [{ ...good, places: { today: { scene: 'Mars' } } }, {}, 'SC-C-SCENE', 'a drawing Simpl does not have'],
  [good, {}, 'SC-C-PHOTO', 'a photo named but not sent'],
  [good, { side: new Uint8Array([60, 115, 118, 103, 62, 0, 0, 0, 0, 0, 0, 0, 0]) }, 'SC-C-TYPE', 'an SVG (or anything not JPEG, PNG or WebP) as a photo'],
  [good, { side: new Uint8Array(C.LIMITS.photoBytes + 1).fill(0xff) }, 'SC-C-BIG', 'a photo over 1.5 MB'],
  [{ name: 'Plain', places: {} }, {}, 'SC-C-EMPTY', 'neither a colour nor a picture'],
]) {
  const res = await submit(theme, files);
  const b = await body(res);
  check(!res.ok && b.code === code, `refused: ${label} (${b.code})`);
}
{
  const before = kv.m.size;
  const res = await submit(good, { side: PNG }, { website: 'https://spam.example' });
  check(res.ok && kv.m.size === before && hooked.length === 0, 'the hidden field filled: answered politely, nothing kept, nobody told');
}
// a theme sent in
let first;
{
  const res = await submit({ ...good, name: 'Sunset <b>Library</b>\n' }, { side: PNG, overdue: JPEG });
  const b = await body(res);
  first = b.id;
  const rec = JSON.parse(await kv.get(`theme:${first}`));
  check(res.ok && /^[a-z0-9]{8}$/.test(first) && res.headers.get('access-control-allow-origin') === '*', `a theme sent in is taken (id ${first})`);
  check(rec.status === 'pending' && rec.name === 'Sunset bLibrary/b' && rec.accent === '#ff375f' && rec.look === 'dark', `kept as pending, its name cleaned and its colour in lower case: ${JSON.stringify({ name: rec.name, accent: rec.accent })}`);
  check(!!kv.m.get(`photo:${first}:side`) && !kv.m.get(`photo:${first}:overdue`), 'its photo kept, and a photo for a place the theme does not use is not');
  check(JSON.parse(await kv.get('list:pending'))[0] === first, 'it waits in the pending list');
  check(hooked.length === 1 && /Community theme/.test(hooked[0]) && /\/community\/review/.test(hooked[0]), 'the owner is told, with where to review it');
}
// not public yet
{
  const g = await body(await call('themes'));
  const photo = await call(`photo/${first}/side`);
  check(g.themes.length === 0 && photo.status === 404, 'a pending theme is not in the gallery, and its photo is not served to anyone');
}
// the review's passcode
let cookie;
{
  const wrong = await call('login', { method: 'POST', body: JSON.stringify({ key: 'guess' }) });
  check(wrong.status === 401 && !wrong.headers.get('set-cookie'), 'a wrong passcode: refused, no cookie');
  const right = await call('login', { method: 'POST', body: JSON.stringify({ key: KEY }) });
  const set = right.headers.get('set-cookie') || '';
  cookie = set.split(';')[0];
  check(right.ok && /HttpOnly/.test(set) && /Secure/.test(set) && /SameSite=Strict/.test(set) && /Path=\/api\/community/.test(set), 'the right passcode: a cookie, HttpOnly, Secure, SameSite=Strict, for the API alone');
  let locked = false;
  for (let i = 0; i < C.LIMITS.loginTries + 1; i++) { const r = await call('login', { method: 'POST', body: JSON.stringify({ key: 'nope' }), ip: '10.9.9.9' }); if (r.status === 429) locked = true; }
  check(locked, `more than ${C.LIMITS.loginTries} tries in a quarter of an hour from one address: held off (SC-C-429)`);
  const none = await call('login', { method: 'POST', body: JSON.stringify({ key: KEY }) }).then(() => C.handleCommunity(new Request('https://simplcourses.com/api/community/login', { method: 'POST', body: '{}' }), { COMMUNITY: kv }, fake));
  check(none.status === 503 && (await body(none)).code === 'SC-C-KEY', 'no passcode set: the review cannot be opened at all (SC-C-KEY)');
}
// the review
{
  const no = await call('review');
  check(no.status === 401, 'the review without the cookie: refused');
  const forged = await call('review', { headers: { cookie: `${cookie.split('.')[0]}.AAAAforged` } });
  check(forged.status === 401, 'a cookie with a forged signature: refused');
  const later = await call('review', { headers: { cookie }, now: Date.now() + (C.LIMITS.sessionHours + 1) * 3600 * 1000 });
  check(later.status === 401, `the cookie after ${C.LIMITS.sessionHours} hours: refused`);
  const res = await call('review', { headers: { cookie } });
  const b = await body(res);
  check(res.ok && b.pending.length === 1 && b.pending[0].id === first && /\/api\/community\/photo\/\w+\/side$/.test(b.pending[0].places.side.photo), 'with it: the pending theme, its photo given as an address');
  const photo = await call(`photo/${first}/side`, { headers: { cookie } });
  check(photo.ok && photo.headers.get('content-type') === 'image/png' && /no-store/.test(photo.headers.get('cache-control')) && photo.headers.get('x-content-type-options') === 'nosniff', 'a pending photo for the review: served as what its bytes are, never cached');
}
// a decision
{
  const noHeader = await call('decide', { method: 'POST', body: JSON.stringify({ id: first, decision: 'approve' }), headers: { cookie } });
  check(noHeader.status === 403, 'a decision without the review page’s header: refused (SC-C-403)');
  const elsewhere = await call('decide', { method: 'POST', body: JSON.stringify({ id: first, decision: 'approve' }), headers: { cookie, 'x-simpl-review': '1', origin: 'https://evil.example' } });
  check(elsewhere.status === 403, 'a decision from another site: refused');
  const res = await call('decide', { method: 'POST', body: JSON.stringify({ id: first, decision: 'approve' }), headers: { cookie, 'x-simpl-review': '1', origin: 'https://simplcourses.com' } });
  check(res.ok && (await body(res)).status === 'approved', 'approved from the review page');
  const g = await body(await call('themes'));
  const t = g.themes[0];
  check(g.themes.length === 1 && t.name === 'Sunset bLibrary/b' && t.maker === 'Maya' && t.places.side.photo === `https://simplcourses.com/api/community/photo/${first}/side` && t.places.today.scene === 'Dusk' && t.places.side.x === 40 && t.places.side.ink === false && !('ink' in t.places.today), 'now in the gallery: its name, maker, places and photo address, a photo with its Ink off kept so (a drawing is always inked)');
  check(!('status' in t), 'the gallery does not say how a theme came to be there');
  const photo = await call(`photo/${first}/side`);
  check(photo.ok && /immutable/.test(photo.headers.get('cache-control')) && photo.headers.get('access-control-allow-origin') === '*', 'its photo is now open to anyone, and kept by browsers');
  const again = await call('decide', { method: 'POST', body: JSON.stringify({ id: first, decision: 'deny' }), headers: { cookie, 'x-simpl-review': '1' } });
  check(again.status === 409, 'decided twice: refused (SC-C-409)');
  check(JSON.parse(await kv.get('list:pending')).length === 0, 'no longer pending');
}
// one turned down
{
  const b = await body(await submit({ name: 'Nope', accent: '#30d158', places: { graded: { photo: true } } }, { graded: PNG }));
  const res = await call('decide', { method: 'POST', body: JSON.stringify({ id: b.id, decision: 'deny' }), headers: { cookie, 'x-simpl-review': '1' } });
  check(res.ok && !kv.m.get(`theme:${b.id}`) && !kv.m.get(`photo:${b.id}:graded`), 'turned down: the theme and its photo are gone');
}
// one taken down
{
  const res = await call('remove', { method: 'POST', body: JSON.stringify({ id: first }), headers: { cookie, 'x-simpl-review': '1' } });
  const g = await body(await call('themes'));
  check(res.ok && g.themes.length === 0 && !kv.m.get(`photo:${first}:side`) && (await call(`photo/${first}/side`)).status === 404, 'taken down: out of the gallery, its photo gone');
}
// too many at once
{
  let held = false;
  for (let i = 0; i < C.LIMITS.submitsPerHour + 1; i++) { const r = await submit({ name: `Many ${i}`, accent: '#5e5ce6' }, {}, {}, '10.7.7.7'); if (r.status === 429) held = true; }
  check(held, `more than ${C.LIMITS.submitsPerHour} themes an hour from one address: held off`);
}
{
  const res = await call('nothing');
  check(res.status === 404 && (await body(res)).code === 'SC-C-404', 'anything else: SC-C-404');
}
console.log(failures.length ? `\n${failures.length} check(s) failed:\n - ${failures.join('\n - ')}` : '\nAll checks passed.');
process.exit(failures.length ? 1 : 0);
