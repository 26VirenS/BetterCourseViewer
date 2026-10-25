// (2.99.107) A linked calendar's .ics read into events (extension/lib/ics.js), on its own: Google's, iCloud's and
// Outlook's ways of writing a time (UTC, a named zone, a Windows zone, none), whole days, folded and escaped text,
// repeats (weekly on days, a count, an end date, the last Friday, a yearly birthday begun long ago), a time taken out
// and one moved, a cancelled event, one marked free, and an address as pasted made one to fetch.
// Run: node scripts/dev/ics-test.mjs
process.env.TZ = 'America/Los_Angeles'; // (a whole day and a time with no zone are read in this device's zone)
import { join } from 'node:path';

const root = new URL('../..', import.meta.url).pathname;
await import(join(root, 'extension', 'lib', 'ics.js'));
const { parse, feedHref, zoneOf } = globalThis.BCV.ics;
const failures = [];
const check = (ok, label) => { console.log(`  ${ok ? '✓' : '✗'} ${label}`); if (!ok) failures.push(label); };
const iso = (ms) => new Date(ms).toISOString().replace(':00.000Z', 'Z');
const from = Date.UTC(2026, 9, 20);
const to = Date.UTC(2026, 11, 1);

console.log('ics');
const google = [
  'BEGIN:VCALENDAR', 'PRODID:-//Google Inc//Google Calendar 70.9054//EN', 'VERSION:2.0', 'X-WR-CALNAME:Sam\\, personal', 'X-WR-TIMEZONE:America/New_York',
  'BEGIN:VTIMEZONE', 'TZID:America/New_York', 'BEGIN:DAYLIGHT', 'TZOFFSETFROM:-0500', 'DTSTART:19700308T020000', 'END:DAYLIGHT', 'END:VTIMEZONE',
  // a plain event in a named zone, its title folded onto the next line and escaped
  'BEGIN:VEVENT', 'DTSTART;TZID=America/New_York:20261027T090000', 'DTEND;TZID=America/New_York:20261027T103000', 'UID:a1@google.com',
  'SUMMARY:Dentist\\, then', '  groceries', 'LOCATION:Main St\\; Suite 4', 'BEGIN:VALARM', 'TRIGGER:-PT10M', 'SUMMARY:Alarm', 'END:VALARM', 'END:VEVENT',
  // a shift on Tuesdays and Thursdays, Pacific time, the 29th taken out, the 5th moved to the 6th at six
  'BEGIN:VEVENT', 'DTSTART;TZID=America/Los_Angeles:20261006T160000', 'DTEND;TZID=America/Los_Angeles:20261006T200000',
  'RRULE:FREQ=WEEKLY;BYDAY=TU,TH;UNTIL=20261119T000000Z', 'EXDATE;TZID=America/Los_Angeles:20261029T160000', 'UID:shift@google.com', 'SUMMARY:Work shift', 'END:VEVENT',
  'BEGIN:VEVENT', 'RECURRENCE-ID;TZID=America/Los_Angeles:20261105T160000', 'DTSTART;TZID=America/Los_Angeles:20261106T180000', 'DTEND;TZID=America/Los_Angeles:20261106T220000',
  'UID:shift@google.com', 'SUMMARY:Work shift (swapped)', 'END:VEVENT',
  // two whole days, a cancelled event, a free one, a time in UTC
  'BEGIN:VEVENT', 'DTSTART;VALUE=DATE:20261030', 'DTEND;VALUE=DATE:20261101', 'UID:trip', 'SUMMARY:Trip home', 'END:VEVENT',
  'BEGIN:VEVENT', 'DTSTART:20261028T170000Z', 'DTEND:20261028T180000Z', 'UID:gone', 'STATUS:CANCELLED', 'SUMMARY:Cancelled thing', 'END:VEVENT',
  'BEGIN:VEVENT', 'DTSTART:20261028T190000Z', 'DURATION:PT45M', 'UID:free', 'TRANSP:TRANSPARENT', 'SUMMARY:Maybe gym', 'END:VEVENT',
  'END:VCALENDAR',
].join('\r\n');
const g = parse(google, { from, to });
const by = (t) => g.events.filter((e) => e.title === t);
check(g.name === 'Sam, personal', `the calendar's name, unescaped: "${g.name}"`);
const dentist = by('Dentist, then groceries')[0];
check(dentist && iso(dentist.start) === '2026-10-27T13:00Z' && iso(dentist.end) === '2026-10-27T14:30Z' && dentist.location === 'Main St; Suite 4' && !dentist.allDay,
  `a time in a named zone, its folded and escaped title and place (an alarm inside it ignored): ${JSON.stringify(dentist)}`);
const shifts = by('Work shift').map((e) => iso(e.start));
check(shifts.join(' ') === '2026-10-20T23:00Z 2026-10-22T23:00Z 2026-10-27T23:00Z 2026-11-04T00:00Z 2026-11-11T00:00Z 2026-11-13T00:00Z 2026-11-18T00:00Z',
  `a weekly repeat on two days keeps 4 pm across the change of clocks, the 29th taken out, the 5th moved, ending at its UNTIL: ${shifts.join(' ')}`);
const swapped = by('Work shift (swapped)')[0];
check(swapped && iso(swapped.start) === '2026-11-07T02:00Z' && iso(swapped.end) === '2026-11-07T06:00Z' && by('Work shift').every((e) => e.repeats), `the moved one stands alone at its new time: ${swapped && iso(swapped.start)}`);
const trip = by('Trip home')[0];
check(trip && trip.allDay && trip.day === '2026-10-30' && trip.days === 2 && trip.start === new Date(2026, 9, 30).getTime() && trip.end === new Date(2026, 10, 1).getTime(), `two whole days, from midnight here: ${JSON.stringify(trip)}`);
check(!by('Cancelled thing').length, 'a cancelled event is left out');
const gym = by('Maybe gym')[0];
check(gym && gym.free && gym.end - gym.start === 45 * 60000, 'an event marked free is kept and said to be free; DURATION gives its end');
check(g.events.every((e, i, a) => !i || a[i - 1].start <= e.start) && new Set(g.events.map((e) => e.id)).size === g.events.length, 'in order, each with an id of its own');

const outlook = [
  'BEGIN:VCALENDAR', 'METHOD:PUBLISH', 'X-WR-CALNAME:Calendar',
  'BEGIN:VEVENT', 'DTSTART;TZID=Eastern Standard Time:20261102T090000', 'DTEND;TZID=Eastern Standard Time:20261102T100000', 'UID:o1', 'SUMMARY:Advisor meeting', 'END:VEVENT',
  'BEGIN:VEVENT', 'DTSTART;TZID="/mozilla.org/20050126_1/Europe/London":20261102T120000', 'DTEND;TZID="/mozilla.org/20050126_1/Europe/London":20261102T130000', 'UID:o2', 'SUMMARY:Call home', 'END:VEVENT',
  'BEGIN:VEVENT', 'DTSTART:20261102T080000', 'DTEND:20261102T083000', 'UID:o3', 'SUMMARY:Floating', 'END:VEVENT',
  'BEGIN:VEVENT', 'DTSTART;TZID=Nowhere/Made_Up:20261102T080000', 'UID:o4', 'SUMMARY:Unknown zone', 'END:VEVENT',
  'END:VCALENDAR',
].join('\n');
const o = parse(outlook, { from, to });
const oby = (t) => o.events.find((e) => e.title === t);
check(iso(oby('Advisor meeting').start) === '2026-11-02T14:00Z', `Outlook's Windows zone name (Eastern Standard Time, after the change: -5): ${iso(oby('Advisor meeting').start)}`);
check(iso(oby('Call home').start) === '2026-11-02T12:00Z', `a prefixed zone name read for its IANA end (London, back on GMT): ${iso(oby('Call home').start)}`);
check(oby('Floating').start === new Date(2026, 10, 2, 8, 0).getTime() && oby('Unknown zone').start === new Date(2026, 10, 2, 8, 0).getTime() && oby('Unknown zone').end === oby('Unknown zone').start,
  'a time with no zone, or a zone no one knows, is this device\'s; with no end it is an instant');

const rules = [
  'BEGIN:VCALENDAR',
  'BEGIN:VEVENT', 'DTSTART:20261021T150000Z', 'DTEND:20261021T160000Z', 'RRULE:FREQ=DAILY;COUNT=3', 'UID:r1', 'SUMMARY:Three days', 'END:VEVENT',
  'BEGIN:VEVENT', 'DTSTART:20260130T170000Z', 'DTEND:20260130T180000Z', 'RRULE:FREQ=MONTHLY;BYDAY=-1FR', 'UID:r2', 'SUMMARY:Last Friday', 'END:VEVENT',
  'BEGIN:VEVENT', 'DTSTART;VALUE=DATE:20011104', 'DTEND;VALUE=DATE:20011105', 'RRULE:FREQ=YEARLY', 'UID:r3', 'SUMMARY:Birthday', 'END:VEVENT',
  'BEGIN:VEVENT', 'DTSTART:20160101T140000Z', 'DTEND:20160101T141500Z', 'RRULE:FREQ=DAILY;INTERVAL=2', 'UID:r4', 'SUMMARY:Every other day since 2016', 'END:VEVENT',
  'BEGIN:VEVENT', 'DTSTART:20261031T160000Z', 'RRULE:FREQ=MONTHLY', 'UID:r5', 'SUMMARY:The 31st', 'END:VEVENT',
  'END:VCALENDAR',
].join('\n');
const t0 = Date.now();
const r = parse(rules, { from, to: Date.UTC(2027, 1, 1) });
const ms = Date.now() - t0;
const rby = (t) => r.events.filter((e) => e.title === t);
check(rby('Three days').map((e) => iso(e.start)).join(' ') === '2026-10-21T15:00Z 2026-10-22T15:00Z 2026-10-23T15:00Z', 'COUNT ends a daily repeat');
check(rby('Last Friday').map((e) => iso(e.start).slice(0, 10)).join(' ') === '2026-10-30 2026-11-27 2026-12-25 2027-01-29', `BYDAY=-1FR: the last Friday of each month: ${rby('Last Friday').map((e) => iso(e.start).slice(0, 10)).join(' ')}`);
check(rby('Birthday').length === 1 && rby('Birthday')[0].day === '2026-11-04' && rby('Birthday')[0].allDay, 'a yearly whole day begun in 2001 lands on this year\'s');
const odd = rby('Every other day since 2016');
check(odd.length > 40 && odd.length < 55 && odd.every((e, i) => !i || e.start - odd[i - 1].start === 2 * 86400000) && ms < 500, `a repeat begun ten years back is laid out from near the window, quickly (${odd.length} in ${ms} ms)`);
check(rby('The 31st').map((e) => iso(e.start).slice(0, 10)).join(' ') === '2026-10-31 2026-12-31 2027-01-31', 'a month with no 31st has no occurrence');
check(parse(rules, { from, to: Date.UTC(2027, 1, 1), max: 5 }).events.length === 5, 'at most max events');

let threw = '';
try { parse('<html>Sign in</html>'); } catch (e) { threw = e.message; }
check(threw === 'That is not a calendar file.', `a page that is not a calendar says so: "${threw}"`);

check(feedHref('webcal://p01-caldav.icloud.com/published/2/abc') === 'https://p01-caldav.icloud.com/published/2/abc' && feedHref(' calendar.google.com/calendar/ical/x%40group/private-1/basic.ics ') === 'https://calendar.google.com/calendar/ical/x%40group/private-1/basic.ics'
  && feedHref('http://localhost:8080/x.ics') === 'http://localhost:8080/x.ics' && feedHref('javascript:alert(1)') === null && feedHref('ftp://example.com/a.ics') === null && feedHref('') === null && feedHref('not a link') === null,
  'an address as pasted: webcal made https, a bare host given https, anything but a web address refused');
check(zoneOf('Pacific Standard Time') === 'America/Los_Angeles' && zoneOf('Z') === 'UTC' && zoneOf('Mars/Olympus') === null, 'zone names: Windows, UTC, unknown');

if (failures.length) { console.log(`\n${failures.length} failed`); process.exit(1); }
console.log('\nics passed');
