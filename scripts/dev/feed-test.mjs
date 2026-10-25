// (2.99.110) Simpl's own calendar feed, end to end without a browser: the calendar written (extension/lib/ics.js write:
// times in UTC, whole days, escaped and folded text), sealed (seal: AES-256-GCM bound to the feed's id), kept by the site
// (site/src/feed.js over its FeedStore, site/src/feed-store.js, with a stand-in for Durable Object storage), opened again
// with the key in the private link and read back (ics.js parse) — and what is refused: no token, another token, a
// wrong key, a bad id, too big; the copy forgotten on DELETE and after 120 days; the store's own check.
// Run: node scripts/dev/feed-test.mjs
process.env.TZ = 'America/Los_Angeles';
import { join } from 'node:path';

const root = new URL('../..', import.meta.url).pathname;
await import(join(root, 'extension', 'lib', 'ics.js'));
const { write, seal, secret, parse } = globalThis.BCV.ics;
const F = await import(join(root, 'site', 'src', 'feed.js'));
const { FeedStore } = await import(join(root, 'site', 'src', 'feed-store.js'));
const failures = [];
const check = (ok, label) => { console.log(`  ${ok ? '✓' : '✗'} ${label}`); if (!ok) failures.push(label); };

/** Durable Object storage, as far as FeedStore uses it. */
function memStorage() {
  const m = new Map();
  let alarm = null;
  return {
    m, get alarm() { return alarm; },
    async get(k) { return m.get(k); },
    async put(k, v) { if (typeof k === 'object') { for (const [a, b] of Object.entries(k)) m.set(a, b); } else m.set(k, v); },
    async deleteAll() { m.clear(); },
    async setAlarm(t) { alarm = t; },
    async deleteAlarm() { alarm = null; },
  };
}
const stores = new Map();
const FEEDS = {
  idFromName: (n) => n,
  get: (id) => {
    if (!stores.has(id)) { const storage = memStorage(); stores.set(id, { storage, obj: new FeedStore({ storage }) }); }
    const { obj } = stores.get(id);
    return { fetch: (url, init) => obj.fetch(new Request(url, init)) };
  },
};
const env = { FEEDS };
let ip = 0;
const call = (path, { method = 'GET', body, headers = {}, e = env } = {}) => F.handleFeed(new Request(`https://simplcourses.com/api/feed${path}`, { method, body, headers: { 'cf-connecting-ip': `10.1.0.${++ip}`, ...headers } }), e);

console.log('feed: the calendar written');
const DAY = 86400000;
const now = Date.UTC(2026, 9, 25, 18, 0);
const events = [
  { uid: 'assignment-101@simplcourses.com', title: 'W8 HW: Work, Energy; and Power · Physics 8', start: Date.UTC(2026, 9, 27, 6, 59), end: Date.UTC(2026, 9, 27, 6, 59), free: true,
    description: 'Physics 8 · Assignment · 100 pts\nDue Mon, Oct 26 at 11:59 pm\nhttps://school.instructure.com/courses/1/assignments/101', url: 'https://school.instructure.com/courses/1/assignments/101' },
  { uid: 'plan-assignment-101@simplcourses.com', title: 'Work on W8 HW: Work, Energy; and Power', start: Date.UTC(2026, 9, 26, 22, 0), end: Date.UTC(2026, 9, 26, 23, 0), description: 'Physics 8 · 1h set aside' },
  { uid: 'calendar_event-7@simplcourses.com', title: 'Reading day', allDay: true, day: '2026-10-29', days: 1 },
  { uid: 'planner_note-9@simplcourses.com', title: `Café notes — ${'a very long title that goes on and on '.repeat(3)}`, start: Date.UTC(2026, 9, 28, 17, 0), end: Date.UTC(2026, 9, 28, 17, 0), free: true },
];
const text = write({ name: 'Simpl', description: 'Your work', color: '#0A84FF', refreshMinutes: 60, events, now });
const lines = text.split('\r\n');
check(text.startsWith('BEGIN:VCALENDAR\r\nVERSION:2.0\r\n') && text.endsWith('END:VCALENDAR\r\n'), 'a calendar, its lines ended as RFC 5545 has them');
check(lines.includes('X-WR-CALNAME:Simpl') && lines.includes('REFRESH-INTERVAL;VALUE=DURATION:PT60M') && lines.includes('X-PUBLISHED-TTL:PT60M') && lines.includes('X-APPLE-CALENDAR-COLOR:#0A84FF'), 'named Simpl, coloured, and how often to look again said for Apple, Google and Outlook');
check(lines.every((l) => new TextEncoder().encode(l).length <= 75), 'no line longer than 75 bytes (long ones folded)');
check(text.includes('SUMMARY:W8 HW: Work\\, Energy\\; and Power · Physics 8'), 'commas and semicolons escaped');
check(text.includes('DTSTART:20261027T065900Z') && text.includes('TRANSP:TRANSPARENT'), 'a due time in UTC, and free (a due time is not time taken)');
check(text.includes('DTSTART;VALUE=DATE:20261029') && text.includes('DTEND;VALUE=DATE:20261030'), 'a whole day as dates');
const back = parse(text, { from: now - 30 * DAY, to: now + 60 * DAY });
const titles = back.events.map((e) => e.title);
check(back.name === 'Simpl' && back.events.length === 4, `read back whole: ${back.events.length} events`);
check(titles.includes('W8 HW: Work, Energy; and Power · Physics 8') && titles.some((t) => t.startsWith('Café notes — a very long title')), 'its titles read back as written (folded and escaped text undone)');
const plan = back.events.find((e) => e.title.startsWith('Work on'));
check(plan && plan.start === Date.UTC(2026, 9, 26, 22, 0) && plan.end === Date.UTC(2026, 9, 26, 23, 0), 'a plan read back at its time, as long as it was');

console.log('feed: sealed, kept, opened with its link');
const id = secret(16), key = secret(32), token = secret(32);
check(/^[A-Za-z0-9_-]{22}$/.test(id) && /^[A-Za-z0-9_-]{43}$/.test(key) && key !== token, 'an id, a key and a token, each made afresh');
const sealed = await seal(text, key, id);
check(sealed.length > 12 + text.length && !new TextDecoder().decode(sealed).includes('Physics'), 'sealed: nothing of the calendar readable in what is sent');
const auth = (t) => ({ authorization: `Bearer ${t}`, 'content-type': 'application/octet-stream' });
let r = await call('');
check(r.status === 200 && (await r.json()).store === true, 'the store is there (/api/feed)');
r = await call('', { e: {} });
check((await r.json()).store === false, 'and says so when it is not');
r = await call(`/${id}`, { method: 'PUT', body: sealed, e: {}, headers: auth(token) });
check(r.status === 503 && (await r.json()).code === 'SC-F-503', 'no store: refused, SC-F-503');
r = await call(`/${id}`, { method: 'PUT', body: sealed });
check(r.status === 401 && (await r.json()).code === 'SC-F-401', 'no token: refused, SC-F-401');
r = await call('/short', { method: 'PUT', body: sealed, headers: auth(token) });
check(r.status === 404, 'a malformed id: nothing there');
r = await call(`/${id}`, { method: 'PUT', body: new Uint8Array(20), headers: auth(token) });
check(r.status === 400 && (await r.json()).code === 'SC-F-400', 'too short to be sealed: refused, SC-F-400');
r = await call(`/${id}`, { method: 'PUT', body: new Uint8Array(F.LIMITS.bytes + 1), headers: auth(token) });
check(r.status === 413, 'too big: refused, SC-F-413');
r = await call(`/${id}`, { method: 'PUT', body: sealed, headers: auth(token) });
check(r.status === 200 && (await r.json()).ok === true, 'the sealed copy kept');
const kept = stores.get(id).storage;
check(kept.m.get('owner')?.length === 64 && kept.m.get('owner') !== token && kept.alarm > Date.now(), 'only the token\'s hash kept, and a day set to forget it by');
check(![...kept.m.values()].some((v) => typeof v === 'string' && v.includes(key)), 'the key is not kept');
r = await call(`/${id}/${key}.ics`);
const got = await r.text();
check(r.status === 200 && /text\/calendar/.test(r.headers.get('content-type')) && got === text, 'the private link opens it: the calendar, as written');
check(r.headers.get('access-control-allow-origin') === '*' && /private/.test(r.headers.get('cache-control')), 'with the headers a calendar app needs');
r = await call(`/${id}/${secret(32)}.ics`);
check(r.status === 404, 'another key: nothing there (no way to tell a wrong key from no calendar)');
r = await call(`/${secret(16)}/${key}.ics`);
check(r.status === 404, 'another id: nothing there');
const other = secret(32);
r = await call(`/${id}`, { method: 'PUT', body: await seal('BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n', key, id), headers: auth(other) });
check(r.status === 403 && (await r.json()).code === 'SC-F-403', 'another token cannot replace it (SC-F-403)');
r = await call(`/${id}/${key}.ics`);
check((await r.text()) === text, 'and it is unchanged');
// a copy sealed for one id cannot be put under another
const id2 = secret(16);
r = await call(`/${id2}`, { method: 'PUT', body: sealed, headers: auth(token) });
r = await call(`/${id2}/${key}.ics`);
check(r.status === 404, 'a sealed copy moved to another id does not open (the id is bound into it)');
r = await call(`/${id}`, { method: 'OPTIONS' });
check(r.status === 204 && /PUT/.test(r.headers.get('access-control-allow-methods')), 'a preflight answered (the apps send from a page)');
r = await call(`/${id}`, { method: 'DELETE', headers: auth(other) });
check(r.status === 403, 'another token cannot forget it');
r = await call(`/${id}`, { method: 'DELETE', headers: auth(token) });
check(r.status === 200, 'its own token forgets it');
r = await call(`/${id}/${key}.ics`);
check(r.status === 404 && kept.m.size === 0, 'and its link no longer opens anything');

console.log('feed: forgotten after 120 days unsent');
const id3 = secret(16);
await call(`/${id3}`, { method: 'PUT', body: await seal(text, key, id3), headers: auth(token) });
const s3 = stores.get(id3);
s3.storage.m.set('at', Date.now() - 121 * DAY);
await s3.obj.alarm();
check(s3.storage.m.size === 0, 'a copy 121 days old is forgotten when its alarm comes');
const id4 = secret(16);
await call(`/${id4}`, { method: 'PUT', body: await seal(text, key, id4), headers: auth(token) });
const s4 = stores.get(id4);
await s4.obj.alarm();
check(s4.storage.m.has('sealed') && s4.storage.alarm > Date.now(), 'a fresh one is kept, its alarm set again');

console.log(failures.length ? `\n${failures.length} failed` : '\nall passed');
process.exit(failures.length ? 1 : 0);
