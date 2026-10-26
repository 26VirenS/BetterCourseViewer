/* (2.99.107) A calendar's .ics file read into its events, for the calendars a student links to Simpl
 * (background.js calSync): a Google, iCloud or Outlook calendar's private address, read every half
 * hour, its events laid over Simpl's Calendar and kept clear of when a plan is offered.
 *
 * What a calendar app writes is read: lines folded onto the next, escaped text, a time in UTC, in a
 * named zone (an IANA name, a Windows one as Outlook writes them, or Google's prefixed form) or in
 * no zone at all (the calendar's own, else this device's), a whole day, an end or a length. A
 * repeating event is laid out over the window asked for — daily, weekly, monthly or yearly, every
 * so many, on which days, until a date or so many times — keeping its time of day across a change
 * of clocks, less the times taken out (EXDATE) and the ones moved (RECURRENCE-ID, which come as
 * events of their own). A cancelled event is left out; one marked free (TRANSP:TRANSPARENT) is
 * kept, and said to be free.
 *
 * Plain code over a string: the background loads it (importScripts / the manifest's background
 * scripts), scripts/dev/ics-test.mjs runs it in Node. */
(function (root) {
  const BCV = (root.BCV = root.BCV || {});

  // the zones Outlook and Exchange name in Windows' own words, as the IANA names the browser knows
  const WINDOWS_ZONES = {
    'Dateline Standard Time': 'Etc/GMT+12', 'Hawaiian Standard Time': 'Pacific/Honolulu', 'Alaskan Standard Time': 'America/Anchorage',
    'Pacific Standard Time': 'America/Los_Angeles', 'US Mountain Standard Time': 'America/Phoenix', 'Mountain Standard Time': 'America/Denver',
    'Central Standard Time': 'America/Chicago', 'Canada Central Standard Time': 'America/Regina', 'Central America Standard Time': 'America/Guatemala',
    'Eastern Standard Time': 'America/New_York', 'US Eastern Standard Time': 'America/Indianapolis', 'Atlantic Standard Time': 'America/Halifax',
    'Newfoundland Standard Time': 'America/St_Johns', 'E. South America Standard Time': 'America/Sao_Paulo', 'SA Pacific Standard Time': 'America/Bogota',
    'Pacific SA Standard Time': 'America/Santiago', 'Argentina Standard Time': 'America/Buenos_Aires', 'Mexico Standard Time': 'America/Mexico_City',
    'Central Standard Time (Mexico)': 'America/Mexico_City', 'UTC': 'UTC', 'GMT Standard Time': 'Europe/London', 'Greenwich Standard Time': 'Atlantic/Reykjavik',
    'W. Europe Standard Time': 'Europe/Berlin', 'Central Europe Standard Time': 'Europe/Budapest', 'Romance Standard Time': 'Europe/Paris',
    'Central European Standard Time': 'Europe/Warsaw', 'GTB Standard Time': 'Europe/Bucharest', 'FLE Standard Time': 'Europe/Kiev',
    'E. Europe Standard Time': 'Europe/Chisinau', 'Israel Standard Time': 'Asia/Jerusalem', 'Turkey Standard Time': 'Europe/Istanbul',
    'Russian Standard Time': 'Europe/Moscow', 'South Africa Standard Time': 'Africa/Johannesburg', 'Egypt Standard Time': 'Africa/Cairo',
    'Arabian Standard Time': 'Asia/Dubai', 'Arab Standard Time': 'Asia/Riyadh', 'Iran Standard Time': 'Asia/Tehran', 'Pakistan Standard Time': 'Asia/Karachi',
    'India Standard Time': 'Asia/Kolkata', 'Nepal Standard Time': 'Asia/Katmandu', 'Bangladesh Standard Time': 'Asia/Dhaka', 'SE Asia Standard Time': 'Asia/Bangkok',
    'China Standard Time': 'Asia/Shanghai', 'Singapore Standard Time': 'Asia/Singapore', 'Taipei Standard Time': 'Asia/Taipei', 'W. Australia Standard Time': 'Australia/Perth',
    'Tokyo Standard Time': 'Asia/Tokyo', 'Korea Standard Time': 'Asia/Seoul', 'Cen. Australia Standard Time': 'Australia/Adelaide',
    'AUS Eastern Standard Time': 'Australia/Sydney', 'E. Australia Standard Time': 'Australia/Brisbane', 'New Zealand Standard Time': 'Pacific/Auckland',
  };
  const DAY = 86400000;
  const WD = { SU: 0, MO: 1, TU: 2, WE: 3, TH: 4, FR: 5, SA: 6 };

  // ---- zones ----------------------------------------------------------------------------------------
  const formats = new Map();
  const formatFor = (tz) => {
    let f = formats.get(tz);
    if (!f) {
      f = new Intl.DateTimeFormat('en-US', { timeZone: tz, hourCycle: 'h23', year: 'numeric', month: 'numeric', day: 'numeric', hour: 'numeric', minute: 'numeric', second: 'numeric' });
      formats.set(tz, f);
    }
    return f;
  };
  /** The zone a TZID names, as the browser knows it, or null (then the time is read as having none). */
  function zoneOf(tzid) {
    if (!tzid) return null;
    let t = String(tzid).replace(/^"|"$/g, '').trim();
    if (WINDOWS_ZONES[t]) return WINDOWS_ZONES[t];
    if (/^(utc|gmt|z)$/i.test(t)) return 'UTC';
    const m = /([A-Za-z]+(?:[_-][A-Za-z]+)*\/[A-Za-z0-9_+-]+(?:\/[A-Za-z0-9_+-]+)?)$/.exec(t); // (Google's "/mozilla.org/20050126_1/America/New_York")
    if (m) t = m[1];
    try { formatFor(t); return t; } catch { return null; }
  }
  /** Minutes the zone is ahead of UTC at an instant. */
  function offsetOf(tz, ms) {
    const p = {};
    for (const x of formatFor(tz).formatToParts(new Date(ms))) p[x.type] = x.value;
    return (Date.UTC(+p.year, +p.month - 1, +p.day, +p.hour % 24, +p.minute, +p.second) - ms) / 60000;
  }
  /** A wall-clock time in a zone (none: this device's), as an instant. */
  function instant(w, tz) {
    if (!tz) return new Date(w.y, w.mo, w.d, w.h, w.mi, w.s).getTime();
    const guess = Date.UTC(w.y, w.mo, w.d, w.h, w.mi, w.s);
    if (tz === 'UTC') return guess;
    const first = guess - offsetOf(tz, guess) * 60000;
    return guess - offsetOf(tz, first) * 60000; // (asked again at the answer: right across a change of clocks)
  }

  // ---- wall-clock dates -----------------------------------------------------------------------------
  const wall = (y, mo, d, h = 0, mi = 0, s = 0) => {
    const t = new Date(Date.UTC(y, mo, d));
    return { y: t.getUTCFullYear(), mo: t.getUTCMonth(), d: t.getUTCDate(), h, mi, s };
  };
  const addDays = (w, n) => wall(w.y, w.mo, w.d + n, w.h, w.mi, w.s);
  const dayNum = (w) => Date.UTC(w.y, w.mo, w.d) / DAY;
  const weekday = (w) => new Date(Date.UTC(w.y, w.mo, w.d)).getUTCDay();
  const daysIn = (y, mo) => new Date(Date.UTC(y, mo + 1, 0)).getUTCDate();
  const before = (a, b) => (a.y - b.y || a.mo - b.mo || a.d - b.d || a.h - b.h || a.mi - b.mi || a.s - b.s) < 0;
  const dayKey = (w) => `${w.y}-${String(w.mo + 1).padStart(2, '0')}-${String(w.d).padStart(2, '0')}`;

  // ---- lines ----------------------------------------------------------------------------------------
  const unescape = (v) => String(v || '').replace(/\\([\\;,nN])/g, (_, c) => (c === 'n' || c === 'N' ? '\n' : c));
  function splitUnquoted(s, sep) {
    const out = [];
    let cur = '';
    let q = false;
    for (const c of s) {
      if (c === '"') q = !q;
      if (c === sep && !q) { out.push(cur); cur = ''; } else cur += c;
    }
    out.push(cur);
    return out;
  }
  function readLine(line) {
    let q = false;
    let colon = -1;
    for (let i = 0; i < line.length; i++) {
      const c = line[i];
      if (c === '"') q = !q;
      else if (c === ':' && !q) { colon = i; break; }
    }
    if (colon < 1) return null;
    const parts = splitUnquoted(line.slice(0, colon), ';');
    const params = {};
    for (const p of parts.slice(1)) {
      const eq = p.indexOf('=');
      if (eq > 0) params[p.slice(0, eq).toUpperCase()] = p.slice(eq + 1).replace(/^"|"$/g, '');
    }
    return { name: parts[0].toUpperCase(), params, value: line.slice(colon + 1) };
  }

  /** A DATE or DATE-TIME value: { w, tz, allDay }, or null. calTz: the calendar's own zone, for a time with none. */
  function readWhen(prop, calTz) {
    if (!prop) return null;
    const m = /^(\d{4})(\d{2})(\d{2})(?:T(\d{2})(\d{2})(\d{2})?(Z)?)?$/.exec(String(prop.value).trim());
    if (!m) return null;
    const allDay = !m[4] || prop.params.VALUE === 'DATE';
    const w = wall(+m[1], +m[2] - 1, +m[3], allDay ? 0 : +m[4], allDay ? 0 : +m[5], allDay ? 0 : +(m[6] || 0));
    if (allDay) return { w, tz: null, allDay: true };
    return { w, tz: m[7] ? 'UTC' : zoneOf(prop.params.TZID) || calTz || null, allDay: false };
  }
  function readDuration(v) {
    const m = /^([+-])?P(?:(\d+)W)?(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?$/.exec(String(v || '').trim());
    if (!m) return null;
    const ms = ((+(m[2] || 0) * 7 + +(m[3] || 0)) * 24 * 3600 + +(m[4] || 0) * 3600 + +(m[5] || 0) * 60 + +(m[6] || 0)) * 1000;
    return m[1] === '-' ? -ms : ms;
  }
  function readRule(v) {
    const r = {};
    for (const part of String(v || '').split(';')) {
      const eq = part.indexOf('=');
      if (eq > 0) r[part.slice(0, eq).toUpperCase()] = part.slice(eq + 1);
    }
    if (!/^(DAILY|WEEKLY|MONTHLY|YEARLY)$/.test(r.FREQ || '')) return null;
    const list = (s) => (s ? s.split(',').map((x) => x.trim()).filter(Boolean) : []);
    return {
      freq: r.FREQ,
      interval: Math.max(1, parseInt(r.INTERVAL, 10) || 1),
      count: r.COUNT ? Math.max(0, parseInt(r.COUNT, 10) || 0) : null,
      until: r.UNTIL || null,
      byday: list(r.BYDAY).map((d) => { const mm = /^([+-]?\d{1,2})?(SU|MO|TU|WE|TH|FR|SA)$/i.exec(d); return mm ? { n: mm[1] ? parseInt(mm[1], 10) : 0, wd: WD[mm[2].toUpperCase()] } : null; }).filter(Boolean),
      bymonthday: list(r.BYMONTHDAY).map((x) => parseInt(x, 10)).filter((x) => x && Math.abs(x) <= 31),
      bymonth: list(r.BYMONTH).map((x) => parseInt(x, 10) - 1).filter((x) => x >= 0 && x < 12),
      wkst: WD[(r.WKST || 'MO').toUpperCase()] ?? 1,
    };
  }

  // ---- repeats --------------------------------------------------------------------------------------
  /** The days of a month a rule picks (BYDAY with or without an ordinal, BYMONTHDAY, else the start's own day). */
  function monthDays(y, mo, rule, startDay) {
    const n = daysIn(y, mo);
    const out = new Set();
    if (rule.byday.length) {
      for (const { n: ord, wd } of rule.byday) {
        const all = [];
        for (let d = 1; d <= n; d++) if (new Date(Date.UTC(y, mo, d)).getUTCDay() === wd) all.push(d);
        if (!ord) all.forEach((d) => out.add(d));
        else { const d = ord > 0 ? all[ord - 1] : all[all.length + ord]; if (d) out.add(d); }
      }
      if (rule.bymonthday.length) for (const d of [...out]) if (!rule.bymonthday.some((x) => (x > 0 ? x : n + 1 + x) === d)) out.delete(d);
    } else if (rule.bymonthday.length) {
      for (const x of rule.bymonthday) { const d = x > 0 ? x : n + 1 + x; if (d >= 1 && d <= n) out.add(d); }
    } else if (startDay <= n) out.add(startDay); // (the 31st: a month without one has no occurrence, as the standard says)
    return [...out].sort((a, b) => a - b);
  }
  /** The wall-clock starts a rule gives in its period p (0 the start's own), in order. */
  function periodStarts(rule, start, p) {
    const t = (y, mo, d) => wall(y, mo, d, start.h, start.mi, start.s);
    const inMonths = (w) => !rule.bymonth.length || rule.bymonth.includes(w.mo);
    if (rule.freq === 'DAILY') {
      const w = addDays(start, p * rule.interval);
      return inMonths(w) && (!rule.byday.length || rule.byday.some((b) => b.wd === weekday(w))) ? [w] : [];
    }
    if (rule.freq === 'WEEKLY') {
      const base = addDays(start, p * rule.interval * 7);
      const first = addDays(base, -((weekday(base) - rule.wkst + 7) % 7)); // the week's first day (WKST)
      const days = rule.byday.length ? rule.byday.map((b) => b.wd) : [weekday(start)];
      const out = [];
      for (let i = 0; i < 7; i++) { const w = addDays(first, i); if (days.includes(weekday(w)) && inMonths(w)) out.push(w); }
      return out;
    }
    if (rule.freq === 'MONTHLY') {
      const m0 = wall(start.y, start.mo + p * rule.interval, 1);
      if (!inMonths(m0)) return [];
      return monthDays(m0.y, m0.mo, rule, start.d).map((d) => t(m0.y, m0.mo, d));
    }
    const y = start.y + p * rule.interval; // YEARLY
    const months = rule.bymonth.length ? rule.bymonth : [start.mo];
    const out = [];
    for (const mo of months) {
      const days = rule.byday.length || rule.bymonthday.length ? monthDays(y, mo, rule, start.d) : start.d <= daysIn(y, mo) ? [start.d] : [];
      for (const d of days) out.push(t(y, mo, d));
    }
    return out;
  }
  /** The first day of period p (the start's time of day). */
  function periodFirst(rule, start, p) {
    if (rule.freq === 'DAILY') return addDays(start, p * rule.interval);
    if (rule.freq === 'WEEKLY') { const base = addDays(start, p * rule.interval * 7); return addDays(base, -((weekday(base) - rule.wkst + 7) % 7)); }
    if (rule.freq === 'MONTHLY') return wall(start.y, start.mo + p * rule.interval, 1, start.h, start.mi, start.s);
    return wall(start.y + p * rule.interval, 0, 1, start.h, start.mi, start.s);
  }
  /** The period a date falls in, counted from the start (to begin near the window rather than at a start years back). */
  function periodOf(rule, start, w) {
    if (rule.freq === 'DAILY') return Math.floor((dayNum(w) - dayNum(start)) / rule.interval);
    if (rule.freq === 'WEEKLY') return Math.floor((dayNum(w) - dayNum(start)) / (7 * rule.interval));
    if (rule.freq === 'MONTHLY') return Math.floor(((w.y - start.y) * 12 + (w.mo - start.mo)) / rule.interval);
    return Math.floor((w.y - start.y) / rule.interval);
  }

  /** Reads a calendar file. Returns { name, color, events }, each event { id, title, start, end, allDay, day, days, location, free, repeats },
   *  start and end instants (an all-day event's are its first and last days' midnights here) and day its first day (YYYY-MM-DD) —
   *  only those touching [from, to), at most max of them. */
  function parse(text, { from = Date.now() - 60 * DAY, to = Date.now() + 365 * DAY, max = 3000 } = {}) {
    const lines = String(text || '').replace(/\r\n?/g, '\n').replace(/\n[ \t]/g, '').split('\n');
    if (!lines.some((l) => /^BEGIN:VCALENDAR/i.test(l))) throw new Error('That is not a calendar file.');
    const cal = { name: '', color: '', tz: null };
    const raw = [];
    let ev = null;
    let depth = 0; // (components inside an event — its alarms — are skipped)
    for (const line of lines) {
      const p = readLine(line);
      if (!p) continue;
      if (p.name === 'BEGIN') {
        if (/^VEVENT$/i.test(p.value)) { ev = {}; depth = 0; } else if (ev) depth++;
        continue;
      }
      if (p.name === 'END') {
        if (/^VEVENT$/i.test(p.value) && ev) { raw.push(ev); ev = null; } else if (ev) depth--;
        continue;
      }
      if (ev) { if (!depth) (ev[p.name] ||= []).push(p); continue; }
      if (p.name === 'X-WR-CALNAME' && !cal.name) cal.name = unescape(p.value).trim();
      else if (p.name === 'X-APPLE-CALENDAR-COLOR' && /^#[0-9a-f]{6}/i.test(p.value)) cal.color = p.value.slice(0, 7);
      else if (p.name === 'X-WR-TIMEZONE' && !cal.tz) cal.tz = zoneOf(p.value);
    }

    const one = (e, k) => e[k]?.[0] || null;
    const moved = new Map(); // uid -> the original starts (instants) moved by a RECURRENCE-ID event
    for (const e of raw) {
      const rid = readWhen(one(e, 'RECURRENCE-ID'), cal.tz);
      if (!rid) continue;
      const uid = one(e, 'UID')?.value || '';
      if (!moved.has(uid)) moved.set(uid, new Set());
      moved.get(uid).add(rid.allDay ? dayKey(rid.w) : instant(rid.w, rid.tz));
    }

    const out = [];
    const push = (base, startMs, endMs, startW, days) => {
      if (out.length >= max) return;
      if (endMs <= from || startMs >= to) return;
      out.push({ ...base, id: `${base.uid}:${base.allDay ? dayKey(startW) : startMs}`, start: startMs, end: endMs, day: dayKey(startW), days });
    };
    for (const e of raw) {
      if (/^CANCELLED$/i.test(one(e, 'STATUS')?.value || '')) continue;
      const s = readWhen(one(e, 'DTSTART'), cal.tz);
      if (!s) continue;
      const endW = readWhen(one(e, 'DTEND'), cal.tz);
      const dur = readDuration(one(e, 'DURATION')?.value);
      const uid = one(e, 'UID')?.value || `${one(e, 'SUMMARY')?.value || ''}@${one(e, 'DTSTART').value}`;
      const base = {
        uid, title: unescape(one(e, 'SUMMARY')?.value || '').trim() || 'Busy', location: unescape(one(e, 'LOCATION')?.value || '').trim(),
        allDay: s.allDay, free: /^TRANSPARENT$/i.test(one(e, 'TRANSP')?.value || ''), repeats: false,
      };
      // an all-day event's length in days; a timed one's in milliseconds
      const spanDays = s.allDay ? Math.max(1, endW?.allDay ? dayNum(endW.w) - dayNum(s.w) : dur ? Math.round(dur / DAY) : 1) : 0;
      const startMs = s.allDay ? null : instant(s.w, s.tz);
      const spanMs = s.allDay ? 0 : Math.max(0, endW && !endW.allDay ? instant(endW.w, endW.tz) - startMs : dur ?? 0);
      const at = (w) => {
        if (s.allDay) { const a = new Date(w.y, w.mo, w.d).getTime(); return [a, new Date(w.y, w.mo, w.d + spanDays).getTime()]; }
        const a = instant(w, s.tz);
        return [a, a + spanMs];
      };
      const rule = one(e, 'RECURRENCE-ID') ? null : readRule(one(e, 'RRULE')?.value);
      if (!rule) { const [a, b] = at(s.w); push(base, a, b, s.w, spanDays); continue; }

      base.repeats = true;
      const skip = new Set(moved.get(uid) || []);
      for (const x of e.EXDATE || []) {
        for (const v of String(x.value).split(',')) {
          const w = readWhen({ value: v, params: x.params }, cal.tz);
          if (w) skip.add(w.allDay || s.allDay ? dayKey(w.w) : instant(w.w, w.tz));
        }
      }
      let until = null;
      if (rule.until) {
        const u = readWhen({ value: rule.until, params: {} }, cal.tz);
        if (u) until = u.allDay ? new Date(u.w.y, u.w.mo, u.w.d + 1).getTime() - 1 : instant(u.w, u.tz);
      }
      const fromW = (() => { const d = new Date(from); return wall(d.getFullYear(), d.getMonth(), d.getDate()); })();
      let p = rule.count != null ? 0 : Math.max(0, periodOf(rule, s.w, fromW) - 1); // (counted ones are counted from the start)
      let n = 0;
      let done = false;
      for (let guard = 0; guard < 4000 && !done; guard++, p++) {
        const starts = periodStarts(rule, s.w, p);
        for (const w of starts) {
          if (before(w, s.w)) continue;
          const [a, b] = at(w);
          if (until != null && a > until) { done = true; break; }
          n++;
          if (rule.count != null && n > rule.count) { done = true; break; }
          if (a >= to || out.length >= max) { done = true; break; }
          if (!skip.has(s.allDay ? dayKey(w) : a)) push(base, a, b, w, spanDays);
        }
        // a period that starts past the window ends it, whether or not it gave a start
        if (!done && at(periodFirst(rule, s.w, p + 1))[0] >= to) done = true;
      }
    }
    out.sort((x, y) => x.start - y.start || x.title.localeCompare(y.title));
    return { name: cal.name, color: cal.color, events: out };
  }

  /** A calendar address as pasted (webcal://, or with no scheme) made one the browser fetches, or null. */
  function feedHref(raw) {
    let s = String(raw || '').trim().replace(/^<|>$/g, '');
    if (!s) return null;
    s = s.replace(/^webcals?:\/\//i, 'https://');
    if (!/^[a-z][a-z0-9+.-]*:/i.test(s)) s = `https://${s}`;
    try {
      const u = new URL(s);
      return /^https?:$/.test(u.protocol) && (u.hostname.includes('.') || u.hostname === 'localhost') ? u.href : null;
    } catch {
      return null;
    }
  }

  // ---- (2.99.110) Simpl's own calendar, written ----------------------------------------------------------------
  // What background.js feedPublish sends to simplcourses.com (site/src/feed.js), sealed: every time in UTC, a whole
  // day as dates, text escaped and lines folded at 75 bytes as RFC 5545 has them, the calendar's name and how often to
  // look for changes said in the words Apple, Google and Outlook each read.

  const esc = (s) => String(s ?? '').replace(/\\/g, '\\\\').replace(/;/g, '\\;').replace(/,/g, '\\,').replace(/\r?\n/g, '\\n');
  const pad = (n) => String(n).padStart(2, '0');
  const utc = (ms) => { const d = new Date(ms); return `${d.getUTCFullYear()}${pad(d.getUTCMonth() + 1)}${pad(d.getUTCDate())}T${pad(d.getUTCHours())}${pad(d.getUTCMinutes())}${pad(d.getUTCSeconds())}Z`; };
  const dateOf = (day, plus = 0) => { const [y, m, d] = String(day).split('-').map(Number); const t = new Date(Date.UTC(y, m - 1, d + plus)); return `${t.getUTCFullYear()}${pad(t.getUTCMonth() + 1)}${pad(t.getUTCDate())}`; };
  /** A line folded at 75 bytes (never inside a character), each part after the first begun with a space. */
  function fold(line) {
    const enc = new TextEncoder();
    if (enc.encode(line).length <= 75) return line;
    const out = [];
    let part = '';
    let bytes = 0;
    for (const ch of line) {
      const n = enc.encode(ch).length;
      if (bytes + n > (out.length ? 74 : 75)) { out.push(part); part = ''; bytes = 0; }
      part += ch;
      bytes += n;
    }
    out.push(part);
    return out.join('\r\n ');
  }
  /**
   * A calendar as an .ics file. events: [{ uid, title, start, end?, allDay?, day?, days?, description?, url?, location?,
   * free?, categories?, rrule?, alarm? }] — start and end in ms (a moment with no length, end the same as start: a due
   * time); a whole day by day ("2026-10-25") and how many days; rrule a repeat ("FREQ=WEEKLY"); alarm the minutes before
   * it starts that the calendar app should say so.
   */
  function write({ name = 'Simpl', description = '', color = '', refreshMinutes = 60, events = [], now = Date.now() } = {}) {
    const L = ['BEGIN:VCALENDAR', 'VERSION:2.0', 'PRODID:-//Simpl Courses//Simpl calendar//EN', 'CALSCALE:GREGORIAN', 'METHOD:PUBLISH',
      `X-WR-CALNAME:${esc(name)}`, `NAME:${esc(name)}`];
    if (description) L.push(`X-WR-CALDESC:${esc(description)}`);
    if (color) L.push(`X-APPLE-CALENDAR-COLOR:${color}`, `COLOR:${color}`);
    L.push(`REFRESH-INTERVAL;VALUE=DURATION:PT${refreshMinutes}M`, `X-PUBLISHED-TTL:PT${refreshMinutes}M`);
    const stamp = utc(now);
    for (const ev of events) {
      L.push('BEGIN:VEVENT', `UID:${esc(ev.uid)}`, `DTSTAMP:${stamp}`);
      if (ev.allDay && /^\d{4}-\d{2}-\d{2}$/.test(String(ev.day || ''))) L.push(`DTSTART;VALUE=DATE:${dateOf(ev.day)}`, `DTEND;VALUE=DATE:${dateOf(ev.day, Math.max(1, ev.days || 1))}`);
      else L.push(`DTSTART:${utc(ev.start)}`, `DTEND:${utc(ev.end && ev.end > ev.start ? ev.end : ev.start)}`);
      L.push(`SUMMARY:${esc(ev.title)}`);
      if (ev.description) L.push(`DESCRIPTION:${esc(ev.description)}`);
      if (ev.location) L.push(`LOCATION:${esc(ev.location)}`);
      if (ev.url) L.push(`URL:${String(ev.url).replace(/[\r\n]/g, '')}`);
      if (ev.categories?.length) L.push(`CATEGORIES:${ev.categories.map(esc).join(',')}`);
      if (ev.rrule && /^FREQ=(DAILY|WEEKLY|MONTHLY|YEARLY)(;INTERVAL=\d{1,2})?$/.test(ev.rrule)) L.push(`RRULE:${ev.rrule}`);
      L.push(`TRANSP:${ev.free ? 'TRANSPARENT' : 'OPAQUE'}`);
      if (Number.isFinite(ev.alarm) && ev.alarm >= 0) L.push('BEGIN:VALARM', 'ACTION:DISPLAY', `DESCRIPTION:${esc(ev.title)}`, `TRIGGER:${ev.alarm ? `-PT${Math.round(ev.alarm)}M` : 'PT0S'}`, 'END:VALARM');
      L.push('END:VEVENT');
    }
    L.push('END:VCALENDAR');
    return `${L.map(fold).join('\r\n')}\r\n`;
  }

  /** n random bytes, base64url (an id, a key, a token). */
  function secret(n) {
    const b = new Uint8Array(n);
    root.crypto.getRandomValues(b);
    let s = '';
    for (const x of b) s += String.fromCharCode(x);
    return btoa(s).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  }
  const unb64 = (s) => { const t = atob(String(s).replace(/-/g, '+').replace(/_/g, '/') + '='.repeat((4 - (String(s).length % 4)) % 4)); const out = new Uint8Array(t.length); for (let i = 0; i < t.length; i++) out[i] = t.charCodeAt(i); return out; };
  /** The calendar sealed for simplcourses.com: a fresh 12-byte nonce, then the text under AES-256-GCM with the key
   *  (base64url, 32 bytes), bound to the feed's id. The site opens it only with the key in the private link. */
  async function seal(text, keyB64, id) {
    const subtle = root.crypto.subtle;
    const key = await subtle.importKey('raw', unb64(keyB64), { name: 'AES-GCM' }, false, ['encrypt']);
    const iv = new Uint8Array(12);
    root.crypto.getRandomValues(iv);
    const body = new Uint8Array(await subtle.encrypt({ name: 'AES-GCM', iv, additionalData: new TextEncoder().encode(id) }, key, new TextEncoder().encode(text)));
    const out = new Uint8Array(12 + body.length);
    out.set(iv, 0);
    out.set(body, 12);
    return out;
  }

  BCV.ics = { parse, feedHref, zoneOf, write, secret, seal };
})(typeof self !== 'undefined' ? self : globalThis);
