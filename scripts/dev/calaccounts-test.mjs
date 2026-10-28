// (2.99.124) Sync's calendar accounts on the web (extension/lib/calaccounts.js), in Node against a CalDAV server of its
// own (two of them: the first sends the student on to the second, as iCloud's caldav.icloud.com sends each account on
// to the server that keeps it). The readers (XML with any prefixes, a calendar list, Google's and Outlook's events and
// what is written to them, a sign-in's redirect), then iCloud end to end: the browser's question for the site, a wrong
// password said in words, the right one (its secret sealed: no password in storage), its calendars and events read into
// the page's linked calendars beside one linked by address, Simpl's calendar made once and its events written, left
// alone, changed and taken out; a calendar shown and hidden, writing turned off, a sign-in ended by iCloud and given
// again, and signing out.
// Run: node scripts/dev/calaccounts-test.mjs
process.env.TZ = 'America/Los_Angeles';
import { createServer } from 'node:http';
import { join } from 'node:path';

const root = new URL('../..', import.meta.url).pathname;
const failures = [];
const check = (ok, label) => { console.log(`  ${ok ? '✓' : '✗'} ${label}`); if (!ok) failures.push(label); };
const DAY = 86400000;
const MIN = 60000;

// ---- the browser, as far as the background needs it --------------------------------------------------------------
let data = {};
const copy = (v) => (v === undefined ? v : structuredClone(v));
const keysOf = (k) => (k == null ? Object.keys(data) : Array.isArray(k) ? k : typeof k === 'string' ? [k] : Object.keys(k));
let allowedOrigins = null; // (null: every site allowed)
const tabs = [];
globalThis.BCV = {
  api: {
    storage: {
      local: {
        async get(k) { const o = {}; for (const key of keysOf(k)) if (key in data) o[key] = copy(data[key]); return o; },
        async set(o) { for (const [key, v] of Object.entries(o)) data[key] = copy(v); },
        async remove(k) { for (const key of keysOf(k)) delete data[key]; },
      },
    },
    permissions: {
      async contains({ origins }) { return allowedOrigins == null || origins.every((o) => allowedOrigins.includes(o)); },
      async request() { return false; }, // (no press here: the page offers one)
    },
    runtime: { getURL: (p) => `chrome-extension://simpl/${p}` },
    tabs: { async create(o) { tabs.push(o); return { id: tabs.length }; } },
  },
};
await import(join(root, 'extension', 'lib', 'ics.js'));
await import(join(root, 'extension', 'lib', 'calaccounts.js'));
const A = globalThis.BCV.calAccounts;
A.vault.keys = () => crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, false, ['encrypt', 'decrypt']);

// ---- the readers ---------------------------------------------------------------------------------------------------
console.log('readers');
const D = 'DAV:';
const C = 'urn:ietf:params:xml:ns:caldav';
{
  const t = A.xml('<?xml version="1.0"?><!-- a note --><x:multistatus xmlns:x="DAV:"><x:response><x:href>/a&amp;b/</x:href>'
    + '<x:prop note="1 > 0"><cal:calendar-data xmlns:cal="urn:ietf:params:xml:ns:caldav"><![CDATA[BEGIN:VCALENDAR <&>]]></cal:calendar-data>'
    + '<y xmlns="DAV:">&#x41;&#66;</y><empty/></x:prop></x:response></x:multistatus>');
  const ms = t.kids[0];
  const prop = ms.kids[0].kids[1];
  check(ms.ns === D && ms.name === 'multistatus' && A.textOf(ms.kids[0].kids[0]) === '/a&b/', 'XML: an element by its namespace whatever its prefix; entities read');
  check(prop.attrs.note === '1 > 0' && A.textOf(prop.kids[0]) === 'BEGIN:VCALENDAR <&>' && prop.kids[0].ns === C && prop.kids[1].ns === D && A.textOf(prop.kids[1]) === 'AB',
    'XML: a > inside a quoted attribute, CDATA kept as it is, a default namespace, numbered entities');
  check(A.hrefUnder('<multistatus xmlns="DAV:"><response><propstat><prop><current-user-principal><href> /1/principal/ </href></current-user-principal></prop></propstat></response></multistatus>', D, 'current-user-principal') === '/1/principal/',
    'the principal\'s href found under it');
  const cals = A.davCalendars(listing([
    { href: '/1/calendars/', collection: true },
    { href: '/1/calendars/home/', name: 'Home', color: '#FF2D55FF' },
    { href: '/1/calendars/todo/', name: 'Reminders', comps: ['VTODO'] },
    { href: '/1/calendars/inbox/', inbox: true },
    { href: '/1/calendars/club/', name: '', color: 'red' },
  ]), 'https://p42-caldav.icloud.com/1/calendars/');
  check(cals.length === 2 && cals[0].id === 'https://p42-caldav.icloud.com/1/calendars/home/' && cals[0].color === '#ff2d55' && cals[1].name === 'Calendar' && cals[1].color === '#0a84ff' && cals.every((c) => c.on === false),
    `a calendar list: event calendars only (not the home, a reminders list or the inbox), #RRGGBBAA cut, a name and colour given: ${JSON.stringify(cals)}`);
  const ask = A.eventsAsk(Date.UTC(2026, 9, 1), Date.UTC(2026, 10, 1));
  check(/<c:expand start="20261001T000000Z" end="20261101T000000Z"\/>/.test(ask) && /<c:time-range start="20261001T000000Z"/.test(ask), 'the events asked for a span, each repeat spelled out by the server (expand)');
}
{
  const t = Date.UTC(2026, 10, 3, 17);
  const moment = A.span({ start: t, end: t });
  const day = A.span({ allDay: true, day: '2026-10-31', days: 2 });
  check(moment.end - moment.start === 15 * MIN && A.span({ start: t, end: t + 60 * MIN }).end === t + 60 * MIN, 'a due moment written a quarter of an hour long; a plan as long as it is');
  check(day.allDay && day.day === '2026-10-31' && day.end === '2026-11-02' && A.span({ title: 'no time' }) === null, `whole days end the day after the last: ${JSON.stringify(day)}; no time, nothing`);
  check(JSON.stringify(A.repeats('FREQ=WEEKLY;INTERVAL=2')) === '{"freq":"WEEKLY","interval":2}' && A.repeats('FREQ=YEARLY') === null && A.repeats('') === null, 'a repeat read: weekly every two; yearly and none not written');
  const e = { uid: 'plan-1@simplcourses.com', title: 'Work on Essay', start: t, end: t + 60 * MIN, rrule: 'FREQ=WEEKLY;INTERVAL=2', alarm: 10, description: 'Planned in Simpl' };
  const o = A.objectText(e, t);
  check(/^BEGIN:VCALENDAR\r\n/.test(o) && /UID:plan-1@simplcourses\.com/.test(o) && /RRULE:FREQ=WEEKLY;INTERVAL=2/.test(o) && /TRIGGER:-PT10M/.test(o) && !/METHOD:|X-WR-CALNAME|REFRESH-INTERVAL/.test(o) && (o.match(/BEGIN:VEVENT/g) || []).length === 1,
    'an event as a calendar object: one VEVENT, its repeat and alert, none of a published calendar\'s own lines');
  const m = A.objectText({ uid: 'x@simplcourses.com', title: 'Due', start: t, end: t, free: true }, t);
  check(/DTSTART:20261103T170000Z\r\nDTEND:20261103T171500Z/.test(m) && /TRANSP:TRANSPARENT/.test(m), 'a due moment written with its quarter of an hour, and free');
  const f1 = await A.fileName('a@simplcourses.com');
  check(/^simpl-[0-9a-f]{24}\.ics$/.test(f1) && f1 === await A.fileName('a@simplcourses.com') && f1 !== await A.fileName('b@simplcourses.com'), `an event's file: the same name each time (${f1})`);
  const l1 = await A.linkId('icloud:sam', 'https://x/home/');
  check(/^a[0-9a-f]{16}$/.test(l1) && l1 === await A.linkId('icloud:sam', 'https://x/home/') && l1 !== await A.linkId('icloud:sam', 'https://x/work/'), `a calendar's link id: short, the same each time (${l1})`);
  check(await A.signature(e) === await A.signature({ ...e }) && await A.signature(e) !== await A.signature({ ...e, title: 'Work on Essay draft' }), 'an event\'s digest changes with what it says, only');
}
{
  const g = A.googlePulled({ id: 'g1', summary: 'Trip', start: { date: '2026-10-30' }, end: { date: '2026-11-01' }, transparency: 'transparent' });
  check(g.allDay && g.days === 2 && g.start === new Date(2026, 9, 30).getTime() && g.end === new Date(2026, 10, 1).getTime() && g.free, `Google: two whole days from midnight here, free: ${JSON.stringify(g)}`);
  check(A.googlePulled({ id: 'g2', status: 'cancelled', start: {}, end: {} }) === null && A.googlePulled({ id: 'g3', start: { dateTime: '2026-10-28T09:00:00-07:00' }, end: { dateTime: '2026-10-28T10:00:00-07:00' } }).start === Date.UTC(2026, 9, 28, 16),
    'Google: a cancelled event left out; a time with its offset');
  const gb = A.googleBody({ uid: 'u1', title: 'Due', start: Date.UTC(2026, 10, 3, 17), end: Date.UTC(2026, 10, 3, 17), free: true, rrule: 'FREQ=DAILY', alarm: 0 });
  check(gb.start.dateTime === '2026-11-03T17:00:00Z' && gb.end.dateTime === '2026-11-03T17:15:00Z' && gb.transparency === 'transparent' && gb.recurrence[0] === 'RRULE:FREQ=DAILY'
    && gb.extendedProperties.private.simplUid === 'u1' && gb.reminders.overrides[0].minutes === 0, 'Google: an event written (its times, free, repeat, alert at the time, Simpl\'s id)');
  check(A.googleBody({ uid: 'u2', title: 'Day', allDay: true, day: '2026-11-04', days: 1 }).end.date === '2026-11-05', 'Google: a whole day written as its dates');
  const o = A.outlookPulled({ id: 'o1', subject: 'Advisor', start: { dateTime: '2026-11-02T14:00:00.0000000' }, end: { dateTime: '2026-11-02T15:00:00.0000000' }, showAs: 'free' });
  const od = A.outlookPulled({ id: 'o2', subject: 'Break', isAllDay: true, start: { dateTime: '2026-11-25T00:00:00.0000000' }, end: { dateTime: '2026-11-28T00:00:00.0000000' } });
  check(o.start === Date.UTC(2026, 10, 2, 14) && o.free && od.allDay && od.day === '2026-11-25' && od.days === 3 && od.start === new Date(2026, 10, 25).getTime() && A.outlookPulled({ id: 'o3', isCancelled: true }) === null,
    'Outlook: a time asked for in UTC (seven digits of fraction), whole days by their dates, a cancelled one left out');
  const ob = A.outlookBody({ uid: 'u3', title: 'Lab', start: new Date(2026, 10, 4, 15).getTime(), end: new Date(2026, 10, 4, 16).getTime(), rrule: 'FREQ=WEEKLY' });
  check(ob.start.timeZone === 'UTC' && ob.start.dateTime === '2026-11-04T23:00:00' && ob.recurrence.pattern.type === 'weekly' && ob.recurrence.pattern.daysOfWeek[0] === 'wednesday' && ob.recurrence.range.startDate === '2026-11-04' && ob.isReminderOn === false,
    'Outlook: an event written in UTC, its weekly repeat on the day it falls on here');
  const url = new URL(A.authorizeUrl('google', { clientId: 'cid', redirect: 'https://ext.chromiumapp.org/', state: 's1', interactive: false, hint: 'sam@gmail.com' }));
  check(url.searchParams.get('response_type') === 'token' && url.searchParams.get('prompt') === 'none' && url.searchParams.get('login_hint') === 'sam@gmail.com' && /calendar\.readonly/.test(url.searchParams.get('scope')),
    'a quiet sign-in asks for a token, names the account, and shows nothing');
  const t = A.readRedirect('https://ext.chromiumapp.org/#access_token=tok&expires_in=3600&state=s1', 's1', 1000);
  check(t.access === 'tok' && t.expires === 1000 + 3540 * 1000, 'the sign-in\'s token read, ending a minute early');
  const kind = (f) => { try { f(); return 'none'; } catch (e) { return e.kind; } };
  check(kind(() => A.readRedirect('https://ext.chromiumapp.org/#access_token=tok&state=other', 's1')) === 'other' && kind(() => A.readRedirect('https://ext.chromiumapp.org/#error=access_denied&state=s1', 's1')) === 'cancelled'
    && kind(() => A.readRedirect('https://ext.chromiumapp.org/#error=login_required', 's1')) === 'signedOut', 'a sign-in with the wrong check refused; cancelled and ended said as such');
}

// ---- a CalDAV server (two: the first sends the student on to the second) --------------------------------------------
const APPLE_ID = 'sam@icloud.com';
let password = 'test-only-app-pass';
const asked = []; // { server, method, path, auth }
const objects = new Map(); // path → ics text (the student's own and Simpl's)
const calendars = new Map(); // path → { name, color, comps }
let secondPort = 0;
const today = new Date();
const at = (days, h, m = 0) => new Date(today.getFullYear(), today.getMonth(), today.getDate() + days, h, m).getTime();
const stamp = (ms) => new Date(ms).toISOString().replace(/[-:]/g, '').replace(/\.\d{3}/, '');
const dateOf = (days) => { const d = new Date(today.getFullYear(), today.getMonth(), today.getDate() + days); return `${d.getFullYear()}${String(d.getMonth() + 1).padStart(2, '0')}${String(d.getDate()).padStart(2, '0')}`; };
const vevent = (uid, title, lines) => ['BEGIN:VCALENDAR', 'VERSION:2.0', 'BEGIN:VEVENT', `UID:${uid}`, `SUMMARY:${title}`, ...lines, 'END:VEVENT', 'END:VCALENDAR'].join('\r\n');
calendars.set('/123/calendars/home/', { name: 'Home', color: '#FF2D55FF' });
calendars.set('/123/calendars/work/', { name: 'Work', color: '#34C759FF' });
calendars.set('/123/calendars/todo/', { name: 'Reminders', comps: ['VTODO'] });
objects.set('/123/calendars/home/dentist.ics', vevent('dentist', 'Dentist & then groceries', [`DTSTART:${stamp(at(1, 9))}`, `DTEND:${stamp(at(1, 10))}`, 'LOCATION:Main St']));
objects.set('/123/calendars/home/trip.ics', vevent('trip', 'Trip home', [`DTSTART;VALUE=DATE:${dateOf(3)}`, `DTEND;VALUE=DATE:${dateOf(5)}`]));
objects.set('/123/calendars/home/old.ics', vevent('old', 'Long ago', ['DTSTART:20200101T100000Z', 'DTEND:20200101T110000Z']));
objects.set('/123/calendars/work/shift.ics', vevent('shift', 'Work shift', [`DTSTART:${stamp(at(2, 16))}`, `DTEND:${stamp(at(2, 20))}`, 'TRANSP:OPAQUE']));

function listing(rows) {
  const row = (r) => {
    const type = r.collection ? '<d:collection/>' : r.inbox ? '<d:collection/><c:schedule-inbox/>' : '<d:collection/><c:calendar/>';
    const comps = r.collection || r.inbox ? '' : `<c:supported-calendar-component-set>${(r.comps || ['VEVENT']).map((c) => `<c:comp name="${c}"/>`).join('')}</c:supported-calendar-component-set>`;
    const color = r.color ? `<x:calendar-color xmlns:x="http://apple.com/ns/ical/">${r.color}</x:calendar-color>` : '';
    return `<d:response><d:href>${r.href}</d:href><d:propstat><d:prop><d:resourcetype>${type}</d:resourcetype><d:displayname>${r.name ?? ''}</d:displayname>${color}${comps}</d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat>`
      + '<d:propstat><d:prop><d:getctag/></d:prop><d:status>HTTP/1.1 404 Not Found</d:status></d:propstat></d:response>';
  };
  return `<?xml version="1.0" encoding="UTF-8"?><d:multistatus xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav">${rows.map(row).join('')}</d:multistatus>`;
}
const xmlEsc = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
function serve(name) {
  return createServer((req, res) => {
    let body = '';
    req.on('data', (c) => { body += c; });
    req.on('end', () => {
      const path = new URL(req.url, 'http://x').pathname;
      const auth = req.headers.authorization || '';
      asked.push({ server: name, method: req.method, path, auth: !!auth, depth: req.headers.depth, body });
      const send = (code, text = '', type = 'application/xml; charset=utf-8') => { res.writeHead(code, { 'Content-Type': type }); res.end(text); };
      if (auth !== `Basic ${Buffer.from(`${APPLE_ID}:${password}`).toString('base64')}`) return send(401, '', 'text/plain');
      if (name === 'first') {
        if (req.method === 'PROPFIND' && path === '/') return send(207, `<?xml version="1.0"?><multistatus xmlns="DAV:"><response><href>/</href><propstat><prop><current-user-principal><href>/123/principal/</href></current-user-principal></prop></propstat></response></multistatus>`);
        // (sent on to the server that keeps the account, as iCloud does)
        res.writeHead(307, { Location: `http://127.0.0.1:${secondPort}${path}` });
        return res.end();
      }
      if (req.method === 'PROPFIND' && path === '/123/principal/') return send(207, `<?xml version="1.0"?><A:multistatus xmlns:A="DAV:"><A:response><A:href>/123/principal/</A:href><A:propstat><A:prop><B:calendar-home-set xmlns:B="${C}"><A:href>/123/calendars/</A:href></B:calendar-home-set></A:prop></A:propstat></A:response></A:multistatus>`);
      if (req.method === 'PROPFIND' && path === '/123/calendars/') {
        return send(207, listing([{ href: '/123/calendars/', collection: true }, { href: '/123/calendars/inbox/', inbox: true }, ...[...calendars].map(([href, c]) => ({ href, ...c }))]));
      }
      if (req.method === 'PROPFIND' && calendars.has(path)) return send(207, listing([{ href: path, ...calendars.get(path) }]));
      if (req.method === 'MKCALENDAR') {
        if (calendars.has(path)) return send(405);
        calendars.set(path, { name: /<d:displayname>([^<]*)</.exec(body)?.[1] || '', color: '#0A84FFFF' });
        return send(201);
      }
      if (req.method === 'REPORT' && calendars.has(path)) {
        if (!/<c:expand /.test(body) || !/<c:time-range /.test(body)) return send(400);
        const mine = [...objects].filter(([p]) => p.startsWith(path));
        // (the work calendar's as CDATA, the home's escaped: servers write either)
        const data = (t) => (path.includes('work') ? `<![CDATA[${t}]]>` : xmlEsc(t));
        return send(207, `<?xml version="1.0"?><d:multistatus xmlns:d="DAV:" xmlns:cal="${C}">${mine.map(([p, t]) => `<d:response><d:href>${p}</d:href><d:propstat><d:prop><cal:calendar-data>${data(t)}</cal:calendar-data></d:prop></d:propstat></d:response>`).join('')}</d:multistatus>`);
      }
      if (req.method === 'PUT') {
        if (!calendars.has(path.replace(/[^/]+$/, ''))) return send(409);
        const had = objects.has(path);
        objects.set(path, body);
        return send(had ? 204 : 201);
      }
      if (req.method === 'DELETE') return send(objects.delete(path) ? 204 : 404);
      return send(404);
    });
  });
}
const listen = (s) => new Promise((ok) => s.listen(0, '127.0.0.1', () => ok(s.address().port)));
const first = serve('first');
const second = serve('second');
const firstPort = await listen(first);
secondPort = await listen(second);
data['dev:icloudBase'] = `http://127.0.0.1:${firstPort}/`;
const rpc = (msg) => A.rpc(msg);
const acct = async () => (await A.list())[0];
const simplObjects = () => [...objects.keys()].filter((p) => /\/simpl-[0-9a-f]{8}\/simpl-[0-9a-f]{24}\.ics$/.test(p));
const since = (n) => asked.slice(n);
const links = () => data['cal:links'] || [];

try {
  console.log('signing in');
  const st = await rpc({ op: 'state' });
  check(st.ok && st.accounts.length === 0 && st.kinds.map((k) => `${k.kind}:${k.available}`).join(' ') === 'icloud:true google:false microsoft:false',
    `iCloud offered; Google and Microsoft not yet (no clients): ${st.kinds.map((k) => `${k.kind}:${k.available}`).join(' ')}`);
  check(/later beta/.test((await rpc({ op: 'oauth', kind: 'google' })).message || ''), 'Google pressed anyway: it comes in a later beta');
  check(/Apple ID and an app-specific password/.test((await rpc({ op: 'icloud', appleId: APPLE_ID, password: ' ' })).message || ''), 'no password: asked for one');

  allowedOrigins = [];
  const ask = await rpc({ op: 'icloud', appleId: APPLE_ID, password });
  check(ask.ask === true && ask.origins.join() === `http://127.0.0.1:${firstPort}/*` && !asked.length, `the site not allowed yet: the browser's question asked for first, nothing sent (${ask.origins})`);
  await rpc({ op: 'allow', kind: 'icloud' });
  const tab = tabs[0]?.url || '';
  const m = /setup\/calendar\.html#account:icloud:(.+)$/.exec(tab);
  check(!!m && JSON.parse(decodeURIComponent(m[1]))[0] === `http://127.0.0.1:${firstPort}/*` && !/test-only/.test(tab), 'Allow opens Simpl\'s own page with the site to ask about (never the password)');
  allowedOrigins = null;

  const wrong = await rpc({ op: 'icloud', appleId: APPLE_ID, password: 'not-it' });
  check(!wrong.ok && /app-specific password/.test(wrong.message) && !(await A.list()).length && !data['cal:accounts:sealed'], `a wrong password said in words, nothing kept: "${wrong.message}"`);

  data['cal:links'] = [{ id: 'club', name: 'Club', color: '#ff9500', on: true, href: 'https://example.com/club.ics' }];
  data['cal:feed:club'] = { at: 1, ok: true, events: [] };
  const n0 = asked.length;
  const ok = await rpc({ op: 'icloud', appleId: ` ${APPLE_ID} `, password });
  const a = await acct();
  check(ok.ok && ok.id === 'icloud:sam@icloud.com' && a?.name === APPLE_ID && a.kind === 'icloud' && a.push === true && !a.signedOut && a.problem === '', `signed in: ${JSON.stringify(ok)}`);
  const hop = since(n0).filter((r) => r.server === 'second' && r.path === '/123/principal/');
  check(hop.length >= 1 && hop[hop.length - 1].auth, `sent on to another server: asked there with the sign-in (${hop.map((r) => (r.auth ? 'signed' : 'bare')).join(', ')})`);
  check(a.calendars.map((c) => `${c.name}:${c.on}:${c.color}`).join(' ') === 'Home:true:#ff2d55 Work:false:#34c759', `its calendars: Home shown at first, Work not, no reminders or inbox: ${a.calendars.map((c) => `${c.name}:${c.on}`).join(' ')}`);
  const dump = JSON.stringify(data);
  const sealed = data['cal:accounts:sealed'];
  check(sealed?.v === 1 && sealed.iv && sealed.ct && !dump.includes(password) && !dump.includes(Buffer.from(password).toString('base64')), 'its password sealed: nowhere in storage as it is');
  const home = a.calendars[0];
  const homeLink = await A.linkId(a.id, home.id);
  const feed = data[`cal:feed:${homeLink}`];
  check(links().map((l) => `${l.name}:${l.source || 'address'}`).join(' ') === 'Club:address Home:account' && links()[1].accountName === APPLE_ID && links()[1].color === '#ff2d55',
    `the page's linked calendars: the one by address kept, Home beside it: ${links().map((l) => l.name).join(', ')}`);
  const titles = (feed?.events || []).map((e) => e.title).sort();
  check(feed?.ok && titles.join('|') === 'Dentist & then groceries|Trip home' && a.pulled === 2, `Home's events read (one long ago left out, & read): ${titles.join(' | ')}`);
  const trip = feed.events.find((e) => e.title === 'Trip home');
  check(trip.allDay && trip.days === 2 && trip.start === at(3, 0), 'a whole-day event read as the page keeps one');
  check(a.simplCalendar === null && !since(n0).some((r) => r.method === 'MKCALENDAR'), 'signed in with no events handed over: nothing written yet');
  check((data['cal:accounts:busy'] || []).length === 0, 'nothing left busy');

  console.log('writing Simpl\'s calendar');
  const t = at(2, 23, 59);
  const events = [
    { uid: 'a1@simplcourses.com', title: 'Essay · English', start: t, end: t, free: true, description: 'Due Friday' },
    { uid: 'plan-1@simplcourses.com', title: 'Work on Essay', start: at(1, 15), end: at(1, 16), rrule: 'FREQ=WEEKLY', alarm: 10 },
    { uid: 'n1@simplcourses.com', title: 'Study day', allDay: true, day: '2026-11-04', days: 1 },
    { uid: 'bad@simplcourses.com', title: 'No time' },
  ];
  let n = asked.length;
  await rpc({ op: 'sync', id: a.id, events });
  let b = await acct();
  const made = since(n).filter((r) => r.method === 'MKCALENDAR');
  check(made.length === 1 && /\/123\/calendars\/simpl-[0-9a-f]{8}\/$/.test(b.simplCalendar) && /<d:displayname>Simpl</.test(made[0].body), `Simpl's calendar made once: ${b.simplCalendar}`);
  check(since(n).filter((r) => r.method === 'PUT').length === 3 && simplObjects().length === 3 && b.pushed === 3, `its three events with a time written (${simplObjects().length}); the one with none left out`);
  const planText = objects.get(simplObjects().find((p) => /Work on Essay/.test(objects.get(p))));
  check(/RRULE:FREQ=WEEKLY/.test(planText) && /TRIGGER:-PT10M/.test(planText) && !/METHOD:/.test(planText), 'a plan written with its repeat and alert, as a calendar object');
  check(!b.calendars.some((c) => c.name === 'Simpl') && !links().some((l) => l.name === 'Simpl'), 'Simpl\'s own calendar never shown back among the student\'s');
  check(data[`cal:acct:book:${a.id}`]?.calendar === b.simplCalendar && Object.keys(data[`cal:acct:book:${a.id}`].events).length === 3, 'what was written kept beside each id');

  n = asked.length;
  await rpc({ op: 'sync', id: a.id, events });
  check(!since(n).some((r) => r.method === 'PUT' || r.method === 'MKCALENDAR' || r.method === 'DELETE'), 'synced again unchanged: nothing written, no second calendar');

  n = asked.length;
  await rpc({ op: 'sync', id: a.id, events: [{ ...events[0], title: '✓ Essay · English' }, events[1]] });
  const w = since(n);
  check(w.filter((r) => r.method === 'PUT').length === 1 && w.filter((r) => r.method === 'DELETE').length === 1 && simplObjects().length === 2 && (await acct()).pushed === 2,
    'one changed (handed in): written again; one gone from Simpl\'s: taken out');
  check(objects.get(simplObjects().find((p) => /Essay · English/.test(objects.get(p)))).includes('SUMMARY:✓ Essay · English'), 'the changed one says what it says now, in the same file');

  console.log('choices');
  await rpc({ op: 'show', id: a.id, calendar: a.calendars[1].id, on: true });
  const workLink = await A.linkId(a.id, a.calendars[1].id);
  check(links().map((l) => l.name).join() === 'Club,Home,Work' && data[`cal:feed:${workLink}`]?.events?.[0]?.title === 'Work shift', 'Work shown: read at once (its events written as CDATA)');
  await rpc({ op: 'show', id: a.id, calendar: home.id, on: false });
  check(links().map((l) => l.name).join() === 'Club,Work' && !data[`cal:feed:${homeLink}`] && data['cal:feed:club'], 'Home hidden: its events put away, the one by address left alone');

  n = asked.length;
  check((await rpc({ op: 'sync', stale: 60 * MIN, events })).read === 0 && asked.length === n, 'read a moment ago: a sync for those not read in a while leaves it alone');

  n = asked.length;
  await rpc({ op: 'push', id: a.id, on: false });
  b = await acct();
  check(b.push === false && b.pushed === 0 && simplObjects().length === 0 && since(n).filter((r) => r.method === 'DELETE').length === 2 && calendars.has(new URL(b.simplCalendar).pathname),
    'writing turned off: what Simpl wrote taken out, its calendar left there');
  n = asked.length;
  await rpc({ op: 'sync', id: a.id, events });
  check(!since(n).some((r) => r.method === 'PUT'), 'writing off: synced without writing');

  console.log('signed out by iCloud, signed in again');
  password = 'test-only-new-pass';
  await rpc({ op: 'sync', id: a.id, events });
  b = await acct();
  check(b.signedOut && /Sign in again/.test(b.problem) && links().map((l) => l.name).join() === 'Club', `the password no longer taken: signed out, said so, its calendars off Simpl's: "${b.problem}"`);
  n = asked.length;
  await rpc({ op: 'sync', events });
  check(asked.length === n, 'a signed-out account is not asked again on its own');
  const again = await rpc({ op: 'icloud', appleId: APPLE_ID, password });
  b = await acct();
  check(again.ok && (await A.list()).length === 1 && !b.signedOut && b.problem === '' && b.push === false && b.calendars.map((c) => `${c.name}:${c.on}`).join(' ') === 'Home:false Work:true',
    'signed in again: the same account, what was chosen for it kept');

  console.log('signing out');
  await rpc({ op: 'push', id: a.id, on: true });
  await rpc({ op: 'sync', id: a.id, events });
  const cal = (await acct()).simplCalendar;
  check(simplObjects().length === 3 && [...calendars.values()].filter((c) => c.name === 'Simpl').length === 1, 'writing on again: into the same Simpl calendar');
  await rpc({ op: 'signOut', id: a.id });
  check(!(await A.list()).length && !data['cal:accounts:sealed'] && !data[`cal:acct:book:${a.id}`] && links().map((l) => l.name).join() === 'Club' && !data[`cal:feed:${workLink}`] && data['cal:feed:club'],
    'signed out: its secret, its record and its calendars gone from Simpl; the one by address kept');
  check(calendars.has(new URL(cal).pathname), 'the Simpl calendar it made stays in the account');
  n = asked.length;
  const back = await rpc({ op: 'icloud', appleId: APPLE_ID, password });
  await rpc({ op: 'sync', id: back.id, events });
  check(!since(n).some((r) => r.method === 'MKCALENDAR') && (await acct()).simplCalendar === cal, 'signed in afresh: the Simpl calendar already there used, not another made beside it');
  check(JSON.stringify(await rpc({ op: 'nope' })).includes('Nothing called nope'), 'an unknown question answered as such');
} finally {
  first.close();
  second.close();
}

if (failures.length) { console.log(`\n${failures.length} failed`); process.exit(1); }
console.log('\ncalaccounts passed');
