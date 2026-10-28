/* (2.99.124) Sync's calendar accounts, on the web: the apps' (Mac 1.3.184's CalendarAccounts.swift, Windows 0.3.81's
 * CalendarAccounts.cs) in the extension's background — a real sign-in in place of copying a calendar's private link.
 *
 *  - iCloud Calendar: the student's Apple ID and an app-specific password (made at account.apple.com), over CalDAV
 *    (caldav.icloud.com): the account's principal, then its calendar home on the iCloud server that keeps it, its
 *    calendars listed there and their events asked for a span (each repeat spelled out by iCloud), Simpl's calendar made
 *    there (MKCALENDAR) and an event of Simpl's written to a file of its own (PUT …/simpl-….ics).
 *  - Google Calendar and Outlook: signed in to in the browser's own sign-in window (identity.launchWebAuthFlow, an
 *    access token for an hour, asked for again quietly when it ends); read with calendar.readonly and Microsoft Graph,
 *    written only in a calendar Simpl makes there. Offered once this extension has its clients (KEYS) and the browser
 *    has the sign-in window (Safari has none): until then, "coming in a later beta", as in the apps.
 *
 * Each account, once signed in to: its calendars, each shown on Simpl's calendar or not (chosen with Calendars, beside
 * Sync; its main one at first) — the events of those shown, a month back to six months on, kept as a linked calendar's
 * are (cal:links with source "account", cal:feed:<id>), so the Calendar lays them over Canvas's and the planner counts
 * them busy; and Simpl's own calendar (content/app/feed.js events: the work, the student's own tasks, the plans) written
 * into a calendar named Simpl in the account, made there the first time: each event put in, changed when what it says
 * changed (its digest kept beside its id), taken out when it is gone from Simpl's. Read and written while a Canvas page
 * is open (feed.js syncAccounts: soon after it opens, then every fifteen minutes), when Sync opens and at Sync Now — the
 * page hands over Simpl's events, which only it can read.
 *
 * The accounts are a list in storage (cal:accounts: nothing secret); the passwords and tokens are sealed apart
 * (cal:accounts:sealed, AES-GCM) with a key the browser keeps in this extension's IndexedDB and never lets out
 * (non-extractable). Signed out: its secret forgotten (Google's token given back), its calendars gone from Simpl's; the
 * Simpl calendar it made in the account stays there. A site is asked for once, the first time: the browser's own
 * question, from a press on a page of Simpl's own where it needs one (setup/calendar.html#account:…).
 *
 * Plain code: the background loads it (importScripts / the manifest's background scripts); scripts/dev/calaccounts-test.mjs
 * runs it in Node against a CalDAV server of its own. */
(function (root) {
  const BCV = (root.BCV = root.BCV || {});
  const DAY = 86400000;
  const MIN = 60000;
  const BACK = 30 * DAY;
  const AHEAD = 180 * DAY;
  const MAX = 4000;

  const KINDS = {
    icloud: { name: 'iCloud Calendar', short: 'iCloud', action: 'Sign In with iCloud', sub: 'Your Apple ID and an app password', tint: '#0a84ff', origins: ['https://*.icloud.com/*'] },
    google: { name: 'Google Calendar', short: 'Google', action: 'Sign In with Google', sub: 'Google Calendar', tint: '#4285f4', origins: ['https://www.googleapis.com/*'] },
    microsoft: { name: 'Outlook', short: 'Microsoft', action: 'Sign In with Microsoft', sub: 'Outlook, Microsoft 365, school accounts', tint: '#0078d4', origins: ['https://graph.microsoft.com/*'] },
  };
  /** This extension's sign-in clients — public identifiers, not secrets: a Google OAuth client of the "Web application"
   *  kind and a Microsoft Entra app registration (a single-page application with access tokens issued by the authorize
   *  page), each with the redirect identity.getRedirectURL() names (https://<extension id>.chromiumapp.org/). The
   *  Chrome build also asks for the identity permission then (scripts/chrome-manifest.py). Empty: not offered yet. */
  const KEYS = { google: '', microsoft: '' };

  // ---- what goes wrong, in the student's words --------------------------------------------------------------------
  class CalError extends Error {
    constructor(kind, message) { super(message); this.kind = kind; }
  }
  const signedOut = () => new CalError('signedOut', 'Sign in again: this account’s sign-in has ended.');
  const notFound = () => new CalError('notFound', 'That calendar is no longer there.');
  const cancelled = () => new CalError('cancelled', 'Sign-in was cancelled.');
  const other = (m) => new CalError('other', m);

  // ---- small things ---------------------------------------------------------------------------------------------
  const enc = (s) => new TextEncoder().encode(s);
  const hex = (buf) => [...new Uint8Array(buf)].map((x) => x.toString(16).padStart(2, '0')).join('');
  const sha256 = async (s) => hex(await root.crypto.subtle.digest('SHA-256', enc(s)));
  const b64 = (u8) => { let s = ''; for (let i = 0; i < u8.length; i += 0x8000) s += String.fromCharCode(...u8.subarray(i, i + 0x8000)); return btoa(s); };
  const unb64 = (s) => { const t = atob(String(s || '')); const u = new Uint8Array(t.length); for (let i = 0; i < t.length; i++) u[i] = t.charCodeAt(i); return u; };
  const randomBytes = (n) => { const u = new Uint8Array(n); root.crypto.getRandomValues(u); return u; };
  const randomId = () => hex(randomBytes(16));
  const pad = (n) => String(n).padStart(2, '0');
  const isoZ = (ms) => new Date(ms).toISOString().replace(/\.\d{3}Z$/, 'Z');
  const dayOfUtc = (ms) => { const d = new Date(ms); return `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}`; };
  const DAY_RE = /^\d{4}-\d{2}-\d{2}$/;
  const ymd = (day) => String(day).split('-').map(Number);
  /** A day's midnight here, and the one so many days on (as the page keeps a whole day: background.js calFetch). */
  const localMidnight = (day, plus = 0) => { const [y, m, d] = ymd(day); return new Date(y, m - 1, d + plus).getTime(); };
  const zone = () => { try { return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC'; } catch { return 'UTC'; } };

  /** A calendar's id among the page's linked calendars: short, and the same each time (the apps' LinkId). */
  const linkId = async (account, calendar) => `a${(await sha256(`${account}\u001f${calendar}`)).slice(0, 16)}`;
  /** The file an event of Simpl's is kept in on a CalDAV server: the same name for the same event, every time. */
  const fileName = async (uid) => `simpl-${(await sha256(uid)).slice(0, 24)}.ics`;

  // ---- XML, as much as a CalDAV answer needs ----------------------------------------------------------------------
  const ENT = { lt: '<', gt: '>', amp: '&', quot: '"', apos: "'" };
  const decode = (s) => s.replace(/&(#x[0-9a-f]+|#\d+|[a-z]+);/gi, (m, e) => {
    if (e[0] !== '#') return ENT[e] ?? m;
    const n = e[1] === 'x' || e[1] === 'X' ? parseInt(e.slice(2), 16) : parseInt(e.slice(1), 10);
    try { return String.fromCodePoint(n); } catch { return m; }
  });
  /** The > that ends a tag (not one inside a quoted attribute). */
  function tagEnd(s, from) {
    let q = null;
    for (let j = from + 1; j < s.length; j++) {
      const c = s[j];
      if (q) { if (c === q) q = null; } else if (c === '"' || c === "'") q = c; else if (c === '>') return j;
    }
    return -1;
  }
  /** An XML text as a tree of { ns, name, attrs, kids, text }: each element by its namespace and local name (a CalDAV
   *  server picks its own prefixes), its text and CDATA joined. No DOMParser: a service worker has none. */
  function xml(text) {
    const top = { ns: '', name: '#doc', attrs: {}, kids: [], text: '' };
    const stack = [{ node: top, scope: { xml: 'http://www.w3.org/XML/1998/namespace' } }];
    const s = String(text || '');
    const here = () => stack[stack.length - 1];
    let i = 0;
    while (i < s.length) {
      const lt = s.indexOf('<', i);
      if (lt < 0) { here().node.text += decode(s.slice(i)); break; }
      if (lt > i) here().node.text += decode(s.slice(i, lt));
      if (s.startsWith('<!--', lt)) { const e = s.indexOf('-->', lt + 4); i = e < 0 ? s.length : e + 3; continue; }
      if (s.startsWith('<![CDATA[', lt)) {
        const e = s.indexOf(']]>', lt + 9);
        here().node.text += s.slice(lt + 9, e < 0 ? s.length : e);
        i = e < 0 ? s.length : e + 3;
        continue;
      }
      if (s[lt + 1] === '?' || s[lt + 1] === '!') { const e = s.indexOf('>', lt); i = e < 0 ? s.length : e + 1; continue; }
      const gt = tagEnd(s, lt);
      if (gt < 0) break;
      const body = s.slice(lt + 1, gt);
      i = gt + 1;
      if (body[0] === '/') { if (stack.length > 1) stack.pop(); continue; }
      const closed = body.endsWith('/');
      const inner = closed ? body.slice(0, -1) : body;
      const qname = /^([^\s/>]+)/.exec(inner)?.[1];
      if (!qname) continue;
      const scope = { ...here().scope };
      const attrs = {};
      const re = /([^\s=]+)\s*=\s*(?:"([^"]*)"|'([^']*)')/g;
      const rest = inner.slice(qname.length);
      let a;
      while ((a = re.exec(rest))) {
        const k = a[1];
        const v = decode(a[2] ?? a[3] ?? '');
        if (k === 'xmlns') scope[''] = v;
        else if (k.startsWith('xmlns:')) scope[k.slice(6)] = v;
        else attrs[k.includes(':') ? k.slice(k.indexOf(':') + 1) : k] = v;
      }
      const colon = qname.indexOf(':');
      const node = { ns: scope[colon < 0 ? '' : qname.slice(0, colon)] ?? '', name: colon < 0 ? qname : qname.slice(colon + 1), attrs, kids: [], text: '' };
      here().node.kids.push(node);
      if (!closed) stack.push({ node, scope });
    }
    return top;
  }
  const all = (n, ns, name, out = []) => { for (const k of n?.kids || []) { if (k.ns === ns && k.name === name) out.push(k); all(k, ns, name, out); } return out; };
  const kid = (n, ns, name) => (n?.kids || []).find((k) => k.ns === ns && k.name === name) || null;
  const textOf = (n) => (n ? n.text + n.kids.map(textOf).join('') : '');

  // ---- CalDAV's questions and the reading of its answers (RFC 4791) -----------------------------------------------
  const D = 'DAV:';
  const C = 'urn:ietf:params:xml:ns:caldav';
  const A = 'http://apple.com/ns/ical/';
  const HEAD = '<?xml version="1.0" encoding="utf-8"?>';
  const ASK = {
    principal: `${HEAD}<d:propfind xmlns:d="DAV:"><d:prop><d:current-user-principal/></d:prop></d:propfind>`,
    home: `${HEAD}<d:propfind xmlns:d="DAV:" xmlns:c="${C}"><d:prop><c:calendar-home-set/></d:prop></d:propfind>`,
    name: `${HEAD}<d:propfind xmlns:d="DAV:"><d:prop><d:displayname/></d:prop></d:propfind>`,
    calendars: `${HEAD}<d:propfind xmlns:d="DAV:" xmlns:c="${C}" xmlns:a="${A}"><d:prop><d:resourcetype/><d:displayname/><a:calendar-color/><c:supported-calendar-component-set/></d:prop></d:propfind>`,
    make: `${HEAD}<c:mkcalendar xmlns:d="DAV:" xmlns:c="${C}" xmlns:a="${A}"><d:set><d:prop><d:displayname>Simpl</d:displayname><a:calendar-color>#0A84FFFF</a:calendar-color><c:supported-calendar-component-set><c:comp name="VEVENT"/></c:supported-calendar-component-set></d:prop></d:set></c:mkcalendar>`,
  };
  const davTime = (ms) => isoZ(ms).replace(/[-:]/g, '');
  /** The events touching a span, each repeat's times spelled out by the server (expand). */
  function eventsAsk(from, to) {
    const a = davTime(from);
    const b = davTime(to);
    return `${HEAD}<c:calendar-query xmlns:d="DAV:" xmlns:c="${C}"><d:prop><c:calendar-data><c:expand start="${a}" end="${b}"/></c:calendar-data></d:prop>`
      + `<c:filter><c:comp-filter name="VCALENDAR"><c:comp-filter name="VEVENT"><c:time-range start="${a}" end="${b}"/></c:comp-filter></c:comp-filter></c:filter></c:calendar-query>`;
  }
  /** The first href under the first element of that name in an answer. */
  function hrefUnder(text, ns, name) {
    const el = all(xml(text), ns, name)[0];
    const href = el && all(el, D, 'href')[0];
    return href ? textOf(href).trim() : null;
  }
  /** The event calendars under a calendar home (not the inbox and outbox, not a reminders list), by their address: name,
   *  colour (iCloud's #RRGGBBAA cut to #RRGGBB). */
  function davCalendars(text, at) {
    const out = [];
    for (const r of all(xml(text), D, 'response')) {
      const href = textOf(kid(r, D, 'href')).trim();
      const props = all(r, D, 'prop');
      const prop = props.find((p) => kid(p, D, 'resourcetype')) || props[0];
      if (!href || !prop) continue;
      if (!kid(kid(prop, D, 'resourcetype'), C, 'calendar')) continue;
      const set = kid(prop, C, 'supported-calendar-component-set');
      const comps = set ? all(set, C, 'comp').map((c) => c.attrs.name) : [];
      if (comps.length && !comps.includes('VEVENT')) continue;
      const color = textOf(kid(prop, A, 'calendar-color')).trim();
      let id;
      try { id = new URL(href, at).href; } catch { continue; }
      out.push({ id, name: textOf(kid(prop, D, 'displayname')).trim() || 'Calendar', color: /^#[0-9a-f]{6}/i.test(color) ? color.slice(0, 7).toLowerCase() : '#0a84ff', on: false });
    }
    return out;
  }
  /** Each calendar-data (an iCalendar text) in a multistatus answer. */
  const calendarData = (text) => all(xml(text), C, 'calendar-data').map(textOf).filter((t) => t.trim());

  // ---- Simpl's events, as an account writes them ------------------------------------------------------------------
  /** Its start and its end (a due time, a moment, given a quarter of an hour: a calendar takes no event with no length);
   *  a whole day by its first day and the day after its last. Null for one with no time. */
  function span(e) {
    if (e?.allDay && DAY_RE.test(String(e.day || ''))) {
      const [y, m, d] = ymd(e.day);
      return { allDay: true, day: e.day, end: dayOfUtc(Date.UTC(y, m - 1, d + Math.max(1, e.days || 1))), days: Math.max(1, e.days || 1) };
    }
    if (!Number.isFinite(e?.start)) return null;
    return { allDay: false, start: e.start, end: Number.isFinite(e.end) && e.end > e.start + 15 * MIN ? e.end : e.start + 15 * MIN };
  }
  /** The repeat, as a calendar keeps one: how often (DAILY, WEEKLY, MONTHLY), and every how many. */
  function repeats(rrule) {
    const f = {};
    for (const p of String(rrule || '').split(';')) { const [k, v] = p.split('='); if (k && v) f[k.trim().toUpperCase()] = v.trim().toUpperCase(); }
    if (!['DAILY', 'WEEKLY', 'MONTHLY'].includes(f.FREQ)) return null;
    return { freq: f.FREQ, interval: Math.max(1, parseInt(f.INTERVAL, 10) || 1) };
  }
  const ruleOf = (r) => (r ? `FREQ=${r.freq}${r.interval > 1 ? `;INTERVAL=${r.interval}` : ''}` : '');
  /** What it says, as a short digest: an event unchanged since it was last written is left alone. */
  const signature = async (e) => (await sha256([e.title, e.start ?? '', e.end ?? '', e.allDay ? 1 : 0, e.day || '', e.days || 1, e.description || '', e.url || '', e.location || '', e.free ? 1 : 0, e.rrule || '', e.alarm ?? ''].join('\u001f'))).slice(0, 24);
  /** One of Simpl's events as a CalDAV server keeps it (lib/ics.js write, as one calendar object). */
  function objectText(e, now = Date.now()) {
    const s = span(e);
    if (!s) return null;
    const ev = s.allDay ? { ...e, allDay: true, day: s.day, days: s.days } : { ...e, allDay: false, start: s.start, end: s.end };
    ev.rrule = ruleOf(repeats(e.rrule));
    return BCV.ics.write({ events: [ev], now, object: true });
  }

  // ---- iCloud Calendar (CalDAV) -------------------------------------------------------------------------------------
  function icloud(appleId, password, base = 'https://caldav.icloud.com/') {
    const auth = `Basic ${b64(enc(`${appleId}:${password}`))}`;
    let home = null;
    async function dav(method, url, body = null, { depth = null, type = 'application/xml; charset=utf-8' } = {}) {
      let at = url;
      for (let hop = 0; ; hop++) {
        const headers = { Authorization: auth };
        if (depth != null) headers.Depth = String(depth);
        if (body != null) headers['Content-Type'] = type;
        let r;
        try { r = await fetch(at, { method, headers, body, credentials: 'omit', cache: 'no-store', redirect: 'follow' }); } catch { throw other('iCloud could not be reached. Check the connection and try again.'); }
        // (a redirect to another of iCloud's servers is followed without the sign-in — fetch drops it on the way —
        //  so it is asked again there, with it)
        if (r.status === 401 && r.redirected && r.url && r.url !== at && hop < 3) { at = r.url; continue; }
        if (r.status === 401 || r.status === 403) throw signedOut();
        if (r.status === 404 || r.status === 410) throw notFound();
        if (r.status >= 400) throw other(`iCloud said no (${r.status}).`);
        return { text: await r.text(), url: r.url || at };
      }
    }
    return {
      /** The account's calendar home, found once (the Apple ID and the password asked about at once). */
      async home() {
        if (home) return home;
        const who = await dav('PROPFIND', base, ASK.principal, { depth: 0 });
        const principal = hrefUnder(who.text, D, 'current-user-principal');
        if (!principal) throw other('iCloud did not say where your calendars are.');
        const where = await dav('PROPFIND', new URL(principal, who.url).href, ASK.home, { depth: 0 });
        const h = hrefUnder(where.text, C, 'calendar-home-set');
        if (!h) throw other('iCloud did not say where your calendars are.');
        const url = new URL(h, where.url).href;
        home = url.endsWith('/') ? url : `${url}/`;
        return home;
      },
      async calendars() {
        const h = await this.home();
        const r = await dav('PROPFIND', h, ASK.calendars, { depth: 1 });
        const cals = davCalendars(r.text, r.url);
        // (at first, the one called Home — iCloud's own first calendar — or else the first)
        const main = cals.find((c) => c.name === 'Home') || cals[0];
        if (main) main.on = true;
        return cals;
      },
      async events(calendar, from, to) {
        const r = await dav('REPORT', calendar, eventsAsk(from, to), { depth: 1 });
        const out = [];
        for (const text of calendarData(r.text)) {
          for (const e of BCV.ics.parse(text, { from, to, max: MAX }).events) {
            out.push({ id: e.id, title: e.title, start: e.start, end: e.end, allDay: e.allDay, day: e.day, days: e.days, location: e.location, free: e.free });
            if (out.length >= MAX) return out;
          }
        }
        return out;
      },
      async simplCalendar(known) {
        if (known) {
          try { await dav('PROPFIND', known, ASK.name, { depth: 0 }); return known; } catch (e) { if (e?.kind !== 'notFound') throw e; }
        }
        const h = await this.home();
        const r = await dav('PROPFIND', h, ASK.calendars, { depth: 1 });
        const had = davCalendars(r.text, r.url).find((c) => c.name === 'Simpl'); // (one made on another computer: used, not another beside it)
        if (had) return had.id;
        const made = new URL(`simpl-${hex(randomBytes(4))}/`, h).href;
        await dav('MKCALENDAR', made, ASK.make);
        return made;
      },
      async write(e, calendar, existing) {
        const url = existing || new URL(await fileName(e.uid), calendar.endsWith('/') ? calendar : `${calendar}/`).href;
        const text = objectText(e);
        if (!text) throw other('An event with no time.');
        await dav('PUT', url, text, { type: 'text/calendar; charset=utf-8' });
        return url;
      },
      async remove(id) {
        try { await dav('DELETE', id); } catch (e) { if (e?.kind !== 'notFound') throw e; }
      },
    };
  }

  // ---- signing in (Google, Microsoft) --------------------------------------------------------------------------------
  const OAUTH = {
    google: {
      authorize: 'https://accounts.google.com/o/oauth2/v2/auth', who: 'https://www.googleapis.com/oauth2/v3/userinfo', revoke: 'https://oauth2.googleapis.com/revoke',
      scopes: ['openid', 'email', 'https://www.googleapis.com/auth/calendar.readonly', 'https://www.googleapis.com/auth/calendar.app.created'],
    },
    microsoft: {
      authorize: 'https://login.microsoftonline.com/common/oauth2/v2.0/authorize', who: 'https://graph.microsoft.com/v1.0/me',
      scopes: ['openid', 'profile', 'email', 'User.Read', 'Calendars.ReadWrite'],
    },
  };
  /** The provider's sign-in page: an access token back to this extension's redirect, in the address's fragment. Quietly
   *  (prompt=none, the account named) when one ran out; else the student picks the account. */
  function authorizeUrl(kind, { clientId, redirect, state, interactive = true, hint = '' }) {
    const o = OAUTH[kind];
    const q = new URLSearchParams({ client_id: clientId, redirect_uri: redirect, response_type: 'token', scope: o.scopes.join(' '), state, prompt: interactive ? 'select_account' : 'none' });
    if (kind === 'microsoft') q.set('response_mode', 'fragment');
    if (hint) q.set('login_hint', hint);
    return `${o.authorize}?${q}`;
  }
  /** What the sign-in came back with: the token and when it ends, or why not. */
  function readRedirect(back, state, now = Date.now()) {
    let u;
    try { u = new URL(String(back || '')); } catch { throw other('The sign-in came back without its token. Try again.'); }
    const p = new URLSearchParams(u.hash.replace(/^#/, '') || u.search.replace(/^\?/, ''));
    const err = p.get('error');
    if (err) throw err === 'access_denied' ? cancelled() : err === 'login_required' || err === 'interaction_required' || err === 'consent_required' ? signedOut() : other(p.get('error_description') || `The sign-in was refused (${err}).`);
    if (p.get('state') !== state) throw other('The sign-in came back without its check. Try again.');
    const access = p.get('access_token');
    if (!access) throw other('The sign-in came back without its token. Try again.');
    return { access, expires: now + (Math.max(60, Number(p.get('expires_in')) || 3600) - 60) * 1000, email: '' };
  }
  async function oauthSignIn(kind, { interactive = true, hint = '' } = {}) {
    const id = KEYS[kind];
    const identity = BCV.api?.identity;
    if (!id || !identity?.launchWebAuthFlow || !identity.getRedirectURL) throw other(`${KINDS[kind].name} sign-in is not ready in this version yet.`);
    const state = randomId();
    let back;
    try {
      back = await identity.launchWebAuthFlow({ url: authorizeUrl(kind, { clientId: id, redirect: identity.getRedirectURL(), state, interactive, hint }), interactive });
    } catch (e) {
      if (!interactive) throw signedOut();
      throw /cancel|closed|did not approve|not approve/i.test(String(e?.message || e)) ? cancelled() : other('The sign-in could not be finished. Try again.');
    }
    const t = readRedirect(back, state);
    const me = await fetch(OAUTH[kind].who, { headers: { Authorization: `Bearer ${t.access}` }, credentials: 'omit' }).then((r) => (r.ok ? r.json() : null)).catch(() => null);
    t.email = String(me?.email || me?.mail || me?.userPrincipalName || hint || '');
    return t;
  }

  /** A web API asked with an account's token: its JSON (null for an answer with no body), or why not. A token turned
   *  down is asked for again quietly, once. */
  function web(kind, tokens, saved) {
    let t = tokens;
    async function token() {
      if (t.expires > Date.now()) return t.access;
      const fresh = await oauthSignIn(kind, { interactive: false, hint: t.email }).catch(() => null);
      if (!fresh) throw signedOut();
      t = { ...fresh, email: fresh.email || t.email };
      await saved(t);
      return t.access;
    }
    return async function ask(method, url, body = null, headers = {}, again = true) {
      const h = { Authorization: `Bearer ${await token()}`, ...headers };
      if (body) h['Content-Type'] = 'application/json';
      let r;
      try { r = await fetch(url, { method, headers: h, body: body ? JSON.stringify(body) : undefined, credentials: 'omit', cache: 'no-store' }); } catch { throw other(`${KINDS[kind].name} could not be reached. Check the connection and try again.`); }
      if (r.status === 401 && again) { t = { ...t, expires: 0 }; return ask(method, url, body, headers, false); }
      if (r.status === 401) throw signedOut();
      if (r.status === 404 || r.status === 410) throw notFound();
      const text = await r.text();
      let j = null;
      try { j = text ? JSON.parse(text) : null; } catch { j = null; }
      if (!r.ok) {
        const e = j?.error;
        throw other(`${KINDS[kind].name} said no: ${(typeof e === 'object' ? e?.message : e) || `it answered ${r.status}`}`);
      }
      return j;
    };
  }
  const qs = (o) => Object.keys(o).sort().map((k) => `${k}=${encodeURIComponent(o[k])}`).join('&');

  // ---- Google Calendar ------------------------------------------------------------------------------------------------
  const G = 'https://www.googleapis.com/calendar/v3';
  /** A Google event in the page's shape: a whole day by its dates, else its times; cancelled ones left out. */
  function googlePulled(e) {
    if (!e?.id || e.status === 'cancelled' || !e.start || !e.end) return null;
    const title = e.summary || 'Busy';
    const location = e.location || '';
    const free = e.transparency === 'transparent';
    if (DAY_RE.test(e.start.date || '')) {
      const last = DAY_RE.test(e.end.date || '') ? e.end.date : null;
      const days = last ? Math.max(1, Math.round((Date.UTC(...ymd(last).map((v, i) => (i === 1 ? v - 1 : v))) - Date.UTC(...ymd(e.start.date).map((v, i) => (i === 1 ? v - 1 : v)))) / DAY)) : 1;
      return { id: e.id, title, start: localMidnight(e.start.date), end: localMidnight(e.start.date, days), allDay: true, day: e.start.date, days, location, free };
    }
    const a = Date.parse(e.start.dateTime || '');
    const b = Date.parse(e.end.dateTime || '');
    if (!Number.isFinite(a) || !Number.isFinite(b)) return null;
    return { id: e.id, title, start: a, end: Math.max(a, b), allDay: false, location, free };
  }
  function googleBody(e) {
    const s = span(e);
    if (!s) return null;
    const b = { summary: e.title, description: e.description || '', location: e.location || '', transparency: e.free ? 'transparent' : 'opaque', extendedProperties: { private: { simplUid: e.uid } } };
    if (s.allDay) { b.start = { date: s.day }; b.end = { date: s.end }; } else { b.start = { dateTime: isoZ(s.start), timeZone: zone() }; b.end = { dateTime: isoZ(s.end), timeZone: zone() }; }
    if (e.url) b.source = { title: 'Simpl', url: e.url };
    const r = repeats(e.rrule);
    if (r) b.recurrence = [`RRULE:${ruleOf(r)}`];
    b.reminders = { useDefault: false, overrides: Number.isFinite(e.alarm) && e.alarm >= 0 ? [{ method: 'popup', minutes: Math.round(e.alarm) }] : [] };
    return b;
  }
  function google(ask) {
    return {
      async calendars() {
        const out = [];
        let page = null;
        do {
          const j = await ask('GET', `${G}/users/me/calendarList?${qs({ minAccessRole: 'reader', maxResults: 250, ...(page ? { pageToken: page } : {}) })}`);
          for (const c of j?.items || []) if (c?.id && !c.deleted) out.push({ id: c.id, name: c.summaryOverride || c.summary || c.id, color: c.backgroundColor || '#4285f4', on: !!c.primary });
          page = j?.nextPageToken || null;
        } while (page);
        return out;
      },
      async events(calendar, from, to) {
        const out = [];
        let page = null;
        do {
          const j = await ask('GET', `${G}/calendars/${encodeURIComponent(calendar)}/events?${qs({ timeMin: isoZ(from), timeMax: isoZ(to), singleEvents: 'true', maxResults: 2500, showDeleted: 'false', ...(page ? { pageToken: page } : {}) })}`);
          for (const e of j?.items || []) { const p = googlePulled(e); if (p) out.push(p); }
          page = j?.nextPageToken || null;
        } while (page && out.length < MAX);
        return out;
      },
      async simplCalendar(known) {
        if (known) {
          try { await ask('GET', `${G}/calendars/${encodeURIComponent(known)}`); return known; } catch (e) { if (e?.kind !== 'notFound') throw e; }
        }
        const j = await ask('POST', `${G}/calendars`, { summary: 'Simpl', description: 'Your work, your own tasks and your plans, from Simpl.', timeZone: zone() });
        if (!j?.id) throw other('Google did not make the Simpl calendar.');
        return j.id;
      },
      async write(e, calendar, existing) {
        const body = googleBody(e);
        if (!body) throw other('An event with no time.');
        if (existing) {
          try { await ask('PUT', `${G}/calendars/${encodeURIComponent(calendar)}/events/${encodeURIComponent(existing)}`, body); return existing; } catch (x) { if (x?.kind !== 'notFound') throw x; }
        }
        const j = await ask('POST', `${G}/calendars/${encodeURIComponent(calendar)}/events`, body);
        if (!j?.id) throw other('Google did not keep an event.');
        return j.id;
      },
      async remove(id, calendar) {
        try { await ask('DELETE', `${G}/calendars/${encodeURIComponent(calendar)}/events/${encodeURIComponent(id)}`); } catch (e) { if (e?.kind !== 'notFound') throw e; }
      },
    };
  }

  // ---- Outlook (Microsoft Graph) --------------------------------------------------------------------------------------
  const M = 'https://graph.microsoft.com/v1.0';
  const UTC_PREFER = { Prefer: 'outlook.timezone="UTC"' };
  /** Outlook's named colours, as near as Simpl's own. */
  const MS_COLORS = { lightBlue: '#0a84ff', lightGreen: '#34c759', lightOrange: '#ff9500', lightGray: '#8e8e93', lightYellow: '#ffcc00', lightTeal: '#30b0c7', lightPink: '#ff2d55', lightBrown: '#a2845e', lightRed: '#ff3b30', maxColor: '#0078d4', auto: '#0078d4' };
  /** A time as Graph writes it when asked for UTC: no zone, seven digits of fraction. */
  const graphUtc = (s) => { const m = /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(\.\d+)?/.exec(String(s || '')); return m ? Date.parse(`${m[1]}${m[2] ? m[2].slice(0, 4) : ''}Z`) : NaN; };
  /** An Outlook event in the page's shape (its times asked for in UTC). */
  function outlookPulled(e) {
    if (!e?.id || e.isCancelled) return null;
    const a = graphUtc(e.start?.dateTime);
    const b = graphUtc(e.end?.dateTime);
    if (!Number.isFinite(a) || !Number.isFinite(b)) return null;
    const title = e.subject || 'Busy';
    const location = e.location?.displayName || '';
    const free = e.showAs === 'free';
    if (e.isAllDay) {
      // (a whole day's midnights, in UTC as asked: its days as written)
      const day = dayOfUtc(a);
      const days = Math.max(1, Math.round((b - a) / DAY));
      return { id: e.id, title, start: localMidnight(day), end: localMidnight(day, days), allDay: true, day, days, location, free };
    }
    return { id: e.id, title, start: a, end: Math.max(a, b), allDay: false, location, free };
  }
  const DOW = ['sunday', 'monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday'];
  function outlookBody(e) {
    const s = span(e);
    if (!s) return null;
    // (a timed event in UTC; a whole day as its dates, at midnight, in UTC as well — Graph takes them as written)
    const at = (ms) => isoZ(ms).replace(/Z$/, '');
    const startMs = s.allDay ? Date.UTC(...ymd(s.day).map((v, i) => (i === 1 ? v - 1 : v))) : s.start;
    const endMs = s.allDay ? Date.UTC(...ymd(s.end).map((v, i) => (i === 1 ? v - 1 : v))) : s.end;
    const b = {
      subject: e.title, body: { contentType: 'text', content: e.description || '' },
      start: { dateTime: at(startMs), timeZone: 'UTC' }, end: { dateTime: at(endMs), timeZone: 'UTC' },
      isAllDay: s.allDay, showAs: e.free ? 'free' : 'busy', location: { displayName: e.location || '' },
    };
    if (Number.isFinite(e.alarm) && e.alarm >= 0) { b.isReminderOn = true; b.reminderMinutesBeforeStart = Math.round(e.alarm); } else b.isReminderOn = false;
    const r = repeats(e.rrule);
    if (r) {
      const local = s.allDay ? new Date(...ymd(s.day).map((v, i) => (i === 1 ? v - 1 : v))) : new Date(s.start);
      const first = `${local.getFullYear()}-${pad(local.getMonth() + 1)}-${pad(local.getDate())}`;
      const pattern = r.freq === 'DAILY' ? { type: 'daily', interval: r.interval }
        : r.freq === 'WEEKLY' ? { type: 'weekly', interval: r.interval, daysOfWeek: [DOW[local.getDay()]], firstDayOfWeek: 'sunday' }
          : { type: 'absoluteMonthly', interval: r.interval, dayOfMonth: local.getDate() };
      b.recurrence = { pattern, range: { type: 'noEnd', startDate: first } };
    }
    return b;
  }
  function outlook(ask) {
    return {
      async calendars() {
        const j = await ask('GET', `${M}/me/calendars?${qs({ $top: 100, $select: 'id,name,color,hexColor,isDefaultCalendar' })}`);
        return (j?.value || []).filter((c) => c?.id).map((c) => ({ id: c.id, name: c.name || 'Calendar', color: c.hexColor || MS_COLORS[c.color || 'auto'] || '#0078d4', on: !!c.isDefaultCalendar }));
      },
      async events(calendar, from, to) {
        const out = [];
        let next = `${M}/me/calendars/${encodeURIComponent(calendar)}/calendarView?${qs({ startDateTime: isoZ(from), endDateTime: isoZ(to), $top: 500, $select: 'id,subject,start,end,isAllDay,location,showAs,isCancelled' })}`;
        while (next && out.length < MAX) {
          const j = await ask('GET', next, null, UTC_PREFER);
          for (const e of j?.value || []) { const p = outlookPulled(e); if (p) out.push(p); }
          next = j?.['@odata.nextLink'] || null;
        }
        return out;
      },
      async simplCalendar(known) {
        if (known) {
          try { await ask('GET', `${M}/me/calendars/${encodeURIComponent(known)}?$select=id`); return known; } catch (e) { if (e?.kind !== 'notFound') throw e; }
        }
        // (one named Simpl already there — made by Simpl on another computer — is used, not another made beside it)
        const list = await ask('GET', `${M}/me/calendars?${qs({ $top: 100, $select: 'id,name' })}`);
        const had = (list?.value || []).find((c) => c?.name === 'Simpl');
        if (had?.id) return had.id;
        const j = await ask('POST', `${M}/me/calendars`, { name: 'Simpl', color: 'lightBlue' });
        if (!j?.id) throw other('Outlook did not make the Simpl calendar.');
        return j.id;
      },
      async write(e, calendar, existing) {
        const body = outlookBody(e);
        if (!body) throw other('An event with no time.');
        if (existing) {
          try { await ask('PATCH', `${M}/me/events/${encodeURIComponent(existing)}`, body); return existing; } catch (x) { if (x?.kind !== 'notFound') throw x; }
        }
        const j = await ask('POST', `${M}/me/calendars/${encodeURIComponent(calendar)}/events`, body);
        if (!j?.id) throw other('Outlook did not keep an event.');
        return j.id;
      },
      async remove(id) {
        try { await ask('DELETE', `${M}/me/events/${encodeURIComponent(id)}`); } catch (e) { if (e?.kind !== 'notFound') throw e; }
      },
    };
  }

  // ---- kept: the list, its secrets sealed ---------------------------------------------------------------------------
  const LIST = 'cal:accounts';
  const BUSY = 'cal:accounts:busy';
  const SEALED = 'cal:accounts:sealed';
  const BOOK = (id) => `cal:acct:book:${id}`;
  const LINKS = 'cal:links';
  const FEED = (id) => `cal:feed:${id}`;
  const store = () => BCV.api.storage.local;
  const get = async (keys) => { try { return (await store().get(keys)) || {}; } catch { return {}; } };
  const put = (o) => store().set(o);
  const drop = (keys) => store().remove(keys);

  /** The key that seals the secrets: made once by the browser, kept in this extension's IndexedDB, never let out. (The
   *  tests hand one of their own: vault.keys.) */
  const vault = { keys: null };
  let keyP = null;
  function idbKey() {
    return new Promise((resolve, reject) => {
      const open = root.indexedDB.open('simpl-vault', 1);
      open.onupgradeneeded = () => open.result.createObjectStore('keys');
      open.onerror = () => reject(open.error);
      open.onsuccess = () => {
        const db = open.result;
        const read = db.transaction('keys', 'readonly').objectStore('keys').get('calendar-accounts');
        read.onerror = () => { db.close(); reject(read.error); };
        read.onsuccess = async () => {
          if (read.result) { db.close(); resolve(read.result); return; }
          try {
            const key = await root.crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, false, ['encrypt', 'decrypt']);
            const tx = db.transaction('keys', 'readwrite');
            tx.objectStore('keys').put(key, 'calendar-accounts');
            tx.oncomplete = () => { db.close(); resolve(key); };
            tx.onerror = () => { db.close(); reject(tx.error); };
          } catch (e) { db.close(); reject(e); }
        };
      };
    });
  }
  const vaultKey = () => (keyP ||= Promise.resolve(vault.keys ? vault.keys() : idbKey()).catch((e) => { keyP = null; throw e; }));
  const BOUND = enc('simpl-calendar-accounts');
  let secretsP = null;
  async function openSecrets() {
    const sealed = (await get(SEALED))[SEALED];
    if (!sealed?.ct) return {};
    try {
      const plain = await root.crypto.subtle.decrypt({ name: 'AES-GCM', iv: unb64(sealed.iv), additionalData: BOUND }, await vaultKey(), unb64(sealed.ct));
      return JSON.parse(new TextDecoder().decode(plain)) || {};
    } catch { return {}; } // (its key gone — the browser's site data cleared: each account is signed in to again)
  }
  const secrets = () => (secretsP ||= openSecrets());
  async function setSecret(id, s) {
    const allOf = { ...(await secrets()) };
    if (s) allOf[id] = s; else delete allOf[id];
    secretsP = Promise.resolve(allOf);
    if (!Object.keys(allOf).length) { await drop(SEALED); return; }
    const iv = randomBytes(12);
    const ct = new Uint8Array(await root.crypto.subtle.encrypt({ name: 'AES-GCM', iv, additionalData: BOUND }, await vaultKey(), enc(JSON.stringify(allOf))));
    await put({ [SEALED]: { v: 1, iv: b64(iv), ct: b64(ct) } });
  }

  async function list() { const v = (await get(LIST))[LIST]; return Array.isArray(v) ? v : []; }
  let chain = Promise.resolve();
  /** The list changed by `fn`, one change after another (a sync's end never undoes a switch pressed meanwhile). */
  function update(fn) {
    const run = chain.then(async () => { const l = await list(); const out = (await fn(l)) || l; await put({ [LIST]: out }); return out; });
    chain = run.catch(() => {});
    return run;
  }
  /** An account's fields set; calendars read meanwhile keep the switches the student pressed while they were read. */
  const patch = (id, fields) => update((l) => l.map((a) => {
    if (a.id !== id) return a;
    const next = { ...a, ...fields };
    if (fields.calendars && a.calendars?.length) next.calendars = fields.calendars.map((c) => ({ ...c, on: a.calendars.find((x) => x.id === c.id)?.on ?? c.on }));
    return next;
  }));

  const busy = new Set(); // the accounts being read or written ("new:<kind>" while one is signed in to)
  const again = new Set(); // read again once the read under way is done (a calendar turned on meanwhile)
  async function setBusy(key, on) {
    if (on) busy.add(key); else busy.delete(key);
    await put({ [BUSY]: [...busy] }).catch(() => {});
  }

  // ---- the browser's question for a site ---------------------------------------------------------------------------
  const icloudBase = async () => (await get('dev:icloudBase'))['dev:icloudBase'] || 'https://caldav.icloud.com/'; // (dev: the suites point it at a server of their own)
  async function originsFor(kind) {
    if (kind !== 'icloud') return KINDS[kind].origins;
    const base = await icloudBase();
    return /^https:\/\/caldav\.icloud\.com\//.test(base) ? KINDS.icloud.origins : [`${new URL(base).origin}/*`];
  }
  /** Whether Simpl may reach that kind's sites: asked for here when the browser asks without a press (Safari), else the
   *  page offers a press on a page of Simpl's own. */
  async function allowed(kind) {
    const p = BCV.api?.permissions;
    if (!p?.contains) return true;
    const origins = await originsFor(kind);
    try { if (await p.contains({ origins })) return true; } catch { return false; }
    try { return !!(await p.request({ origins })); } catch { return false; }
  }

  // ---- signing in and out ----------------------------------------------------------------------------------------------
  function available(kind) {
    if (kind === 'icloud') return true;
    return !!KEYS[kind] && !!BCV.api?.identity?.launchWebAuthFlow;
  }
  /** An account added, or signed in to again: what was chosen for it kept. */
  const add = (account) => update((l) => {
    const had = l.find((a) => a.id === account.id);
    const next = { calendars: [], push: true, simplCalendar: null, synced: null, problem: '', pulled: 0, pushed: 0, signedOut: false, ...account };
    if (had) { next.calendars = had.calendars || []; next.push = had.push !== false; next.simplCalendar = had.simplCalendar || null; }
    return had ? l.map((a) => (a.id === account.id ? next : a)) : [...l, next];
  });

  /** iCloud signed in to with the Apple ID and an app password (found, and read for the first time). */
  async function connectICloud(appleId, password) {
    const who = String(appleId || '').trim();
    const pass = String(password || '').trim();
    if (!who || !pass) return { ok: false, message: 'Type your Apple ID and an app-specific password.' };
    const key = 'new:icloud';
    if (busy.has(key)) return { ok: false, busy: true };
    if (!(await allowed('icloud'))) return { ok: false, ask: true, kind: 'icloud', origins: await originsFor('icloud') };
    await setBusy(key, true);
    try {
      await icloud(who, pass, await icloudBase()).home(); // (the Apple ID and password asked about at once: wrong, said now)
      const id = `icloud:${who.toLowerCase()}`;
      await setSecret(id, { password: pass });
      await add({ id, kind: 'icloud', name: who });
      await sync(id, null);
      return { ok: true, id };
    } catch (e) {
      if (e?.kind === 'signedOut') return { ok: false, message: 'iCloud did not take that Apple ID and password. Use an app-specific password, not your Apple ID’s own.' };
      return { ok: false, message: e instanceof CalError ? e.message : 'iCloud could not be reached. Check the connection and try again.' };
    } finally { await setBusy(key, false); }
  }
  /** Google or Microsoft signed in to in the browser's sign-in window, and read for the first time. */
  async function connectOAuth(kind) {
    if (!KINDS[kind] || kind === 'icloud') return { ok: false, message: 'Nothing to sign in to.' };
    if (!available(kind)) return { ok: false, message: `${KINDS[kind].name} sign-in comes in a later beta.` };
    const key = `new:${kind}`;
    if (busy.has(key)) return { ok: false, busy: true };
    if (!(await allowed(kind))) return { ok: false, ask: true, kind, origins: await originsFor(kind) };
    await setBusy(key, true);
    try {
      const t = await oauthSignIn(kind);
      const who = t.email || KINDS[kind].name;
      const id = `${kind}:${who.toLowerCase()}`;
      await setSecret(id, { tokens: t });
      await add({ id, kind, name: who });
      await sync(id, null);
      return { ok: true, id };
    } catch (e) {
      if (e?.kind === 'cancelled') return { ok: false, cancelled: true };
      return { ok: false, message: e instanceof CalError ? e.message : 'The sign-in could not be finished. Try again.' };
    } finally { await setBusy(key, false); }
  }
  /** Signed out: its secret forgotten (Google's token given back), its calendars gone from Simpl's, what was written for
   *  it forgotten. The Simpl calendar in the account stays. */
  async function disconnect(id) {
    const a = (await list()).find((x) => x.id === id);
    if (!a) return;
    await update((l) => l.filter((x) => x.id !== id));
    const s = (await secrets())[id];
    if (a.kind === 'google' && s?.tokens?.access) fetch(`${OAUTH.google.revoke}?token=${encodeURIComponent(s.tokens.access)}`, { method: 'POST', credentials: 'omit' }).catch(() => {});
    await setSecret(id, null);
    await drop(BOOK(id)).catch(() => {});
    await writeLinks();
  }

  // ---- choices ---------------------------------------------------------------------------------------------------------
  /** A calendar shown on Simpl's calendar, or not: its events read (or put away) at once. */
  async function show(id, calendar, on) {
    const a = (await list()).find((x) => x.id === id);
    if (!a?.calendars?.some((c) => c.id === calendar)) return;
    await update((l) => l.map((x) => (x.id === id ? { ...x, calendars: x.calendars.map((c) => (c.id === calendar ? { ...c, on } : c)) } : x)));
    if (on) await sync(id, null); else await writeLinks();
  }
  /** Simpl's own calendar written into the account, or no longer: turned off, what was written there is taken out. (Turned
   *  on, the page writes it with Simpl's events: feed.js syncAccounts.) */
  async function setPush(id, on) {
    await patch(id, { push: on });
    if (!on) await takeOut(id);
  }

  // ---- syncing an account -------------------------------------------------------------------------------------------
  async function service(a) {
    const s = (await secrets())[a.id];
    if (a.kind === 'icloud') {
      if (!s?.password) throw signedOut();
      return icloud(a.name, s.password, await icloudBase());
    }
    if (!available(a.kind)) throw other(`${KINDS[a.kind]?.name || 'This'} sign-in is not ready in this version.`);
    if (!s?.tokens) throw signedOut();
    const ask = web(a.kind, s.tokens, (t) => setSecret(a.id, { tokens: t }));
    return a.kind === 'google' ? google(ask) : outlook(ask);
  }
  /** One account read, and (events given, its switch on) Simpl's calendar written there. */
  async function sync(id, events) {
    if (busy.has(id)) { again.add(id); return { ok: true, busy: true }; }
    const a = (await list()).find((x) => x.id === id);
    if (!a) return { ok: false };
    await setBusy(id, true);
    try {
      const svc = await service(a);
      // (its calendars as the account has them now: a new one not shown, one gone gone; never Simpl's own, nor one named
      //  Simpl, shown back)
      const listed = (await svc.calendars()).filter((c) => c.id !== a.simplCalendar && c.name !== 'Simpl');
      if (a.calendars?.length) for (const c of listed) c.on = a.calendars.find((x) => x.id === c.id)?.on ?? false;
      const now = Date.now();
      const feeds = {};
      let pulled = 0;
      for (const c of listed.filter((x) => x.on)) {
        const evs = await svc.events(c.id, now - BACK, now + AHEAD);
        pulled += evs.length;
        feeds[FEED(await linkId(a.id, c.id))] = { at: now, ok: true, error: '', name: c.name, events: evs };
      }
      const fields = { calendars: listed, pulled, synced: now, problem: '', signedOut: false };
      if (a.push && Array.isArray(events)) {
        const cal = await svc.simplCalendar(a.simplCalendar);
        fields.simplCalendar = cal;
        fields.pushed = await push(events, svc, a.id, cal);
        fields.calendars = listed.filter((c) => c.id !== cal); // (made just now in an account read above: not one of the student's own)
      }
      if (Object.keys(feeds).length) await put(feeds);
      await patch(id, fields);
      await writeLinks();
      return { ok: true };
    } catch (e) {
      const message = e instanceof CalError ? e.message : 'Not synced: the calendar could not be reached.';
      await patch(id, e?.kind === 'signedOut' ? { signedOut: true, problem: message } : { problem: message });
      if (e?.kind === 'signedOut') await writeLinks();
      return { ok: false, message };
    } finally {
      await setBusy(id, false);
      if (again.delete(id)) sync(id, null).catch(() => {});
    }
  }
  /** Simpl's calendar written: each event put in or changed if what it says changed, one gone from Simpl's taken out.
   *  How many it holds. */
  async function push(events, svc, id, cal) {
    let book = (await get(BOOK(id)))[BOOK(id)];
    if (!book || book.calendar !== cal) book = { calendar: cal, events: {} }; // (a new calendar: everything again)
    const wanted = new Map();
    for (const e of events) if (e?.uid && span(e)) wanted.set(e.uid, e);
    let written = 0;
    for (const e of [...wanted.values()].sort((x, y) => (x.uid < y.uid ? -1 : x.uid > y.uid ? 1 : 0))) {
      const had = book.events[e.uid];
      const sig = await signature(e);
      if (had?.sig === sig) continue;
      book.events[e.uid] = { id: await svc.write(e, cal, had?.id || null), sig };
      if (++written % 25 === 0) await put({ [BOOK(id)]: book }); // (a long first sync kept as it goes)
    }
    for (const [uid, had] of Object.entries(book.events)) {
      if (wanted.has(uid)) continue;
      await svc.remove(had.id, cal);
      delete book.events[uid];
    }
    await put({ [BOOK(id)]: book });
    return wanted.size;
  }
  /** What was written into the account's Simpl calendar, taken out. */
  async function takeOut(id) {
    const a = (await list()).find((x) => x.id === id);
    if (!a?.simplCalendar || busy.has(id)) return;
    await setBusy(id, true);
    try {
      const svc = await service(a);
      const book = (await get(BOOK(id)))[BOOK(id)] || { calendar: a.simplCalendar, events: {} };
      for (const [uid, had] of Object.entries(book.events)) {
        try { await svc.remove(had.id, a.simplCalendar); } catch { /* gone or not, forgotten here */ }
        delete book.events[uid];
      }
      await put({ [BOOK(id)]: book });
      await patch(id, { pushed: 0 });
    } catch { /* tried again when it is next turned off */ } finally { await setBusy(id, false); }
  }
  /** Every account, or one; with `stale`, only those not read in that long. */
  async function syncAll(events, { id = null, stale = 0 } = {}) {
    let read = 0;
    for (const a of await list()) {
      if (a.signedOut || (id && a.id !== id) || (stale && a.synced && Date.now() - a.synced < stale)) continue;
      await sync(a.id, events);
      read++;
    }
    return { ok: true, read };
  }

  // ---- the page's linked calendars --------------------------------------------------------------------------------------
  /** Those linked by their address as they are, then every shown calendar of every account (source "account": the
   *  background's half-hourly read of links leaves them alone); the events of one no longer shown put away. */
  async function writeLinks() {
    const had = (await get(LINKS))[LINKS];
    const links = Array.isArray(had) ? had : [];
    const byAddress = links.filter((l) => l.source !== 'account');
    const hadIds = new Set(links.filter((l) => l.source === 'account').map((l) => l.id));
    const mine = [];
    for (const a of await list()) {
      if (a.signedOut) continue;
      for (const c of a.calendars || []) if (c.on) mine.push({ id: await linkId(a.id, c.id), name: c.name, color: c.color, on: true, source: 'account', account: a.kind, accountName: a.name });
    }
    const now = new Set(mine.map((l) => l.id));
    await put({ [LINKS]: [...byAddress, ...mine] });
    const gone = [...hadIds].filter((x) => !now.has(x)).map(FEED);
    if (gone.length) await drop(gone);
  }

  // ---- asked from a page ------------------------------------------------------------------------------------------------
  const kinds = () => Object.entries(KINDS).map(([kind, k]) => ({ kind, name: k.name, short: k.short, action: k.action, sub: k.sub, tint: k.tint, available: available(kind) }));
  async function rpc(msg) {
    switch (msg?.op) {
      case 'state': return { ok: true, kinds: kinds(), accounts: await list(), busy: [...busy] };
      case 'allow': { // the browser's question, from a press on a page of Simpl's own (setup/calendar.html)
        if (!KINDS[msg.kind]) return { ok: false };
        const origins = await originsFor(msg.kind);
        await BCV.api.tabs.create({ url: `${BCV.api.runtime.getURL('setup/calendar.html')}#account:${msg.kind}:${encodeURIComponent(JSON.stringify(origins))}`, active: true });
        return { ok: true };
      }
      case 'icloud': return connectICloud(msg.appleId, msg.password);
      case 'oauth': return connectOAuth(msg.kind);
      case 'signOut': await disconnect(msg.id); return { ok: true };
      case 'show': await show(msg.id, msg.calendar, !!msg.on); return { ok: true };
      case 'push': await setPush(msg.id, !!msg.on); return { ok: true };
      case 'sync': return syncAll(Array.isArray(msg.events) ? msg.events : null, { id: msg.id || null, stale: Number(msg.stale) || 0 });
      default: return { ok: false, message: `Nothing called ${msg?.op}.` };
    }
  }
  /** As the background starts: nothing is under way (a read the worker was stopped in is not). */
  const start = () => put({ [BUSY]: [] }).catch(() => {});

  BCV.calAccounts = {
    rpc, start, KINDS, KEYS, vault,
    // (for the tests)
    xml, textOf, hrefUnder, davCalendars, calendarData, eventsAsk, span, repeats, signature, objectText, fileName, linkId,
    googlePulled, googleBody, outlookPulled, outlookBody, authorizeUrl, readRedirect, icloud, list, writeLinks,
  };
})(typeof self !== 'undefined' ? self : globalThis);
