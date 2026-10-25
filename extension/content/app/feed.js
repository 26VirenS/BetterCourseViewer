/* (2.99.110) Simpl's own calendar. The student's work (what is due, from Canvas's planner), their own tasks and the
 * plans they made (content/app/plan.js) put together as one calendar, named Simpl, that Apple Calendar, Google Calendar
 * or Outlook subscribes to — in place of Canvas's own feed, so what a calendar app shows is what Simpl shows: the
 * courses by the names given them here, the tasks of their own, and the time set aside for each piece of work.
 *
 * The events are put together here, on the page that can read Canvas; the background writes them as a calendar, seals
 * it and sends it to simplcourses.com (background.js feed), which can open it only with the key in the private link.
 * It is sent again while Simpl is open: soon after a page opens, when a plan changes, and every half hour — and only
 * when something in it changed (or once in six hours, to keep it). Two weeks back to four months on.
 *
 * Plans no longer go on Canvas's calendar (the Send plans of 2.99.107): a student who had it on has the copies Simpl
 * put there taken off once, as this loads (BCV.plan.retireSending). */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const U = BCV.ui;
  const store = BCV.store;
  const api = BCV.api;

  const MIN = 60000;
  const BACK_DAYS = 14;
  const AHEAD_DAYS = 120;
  const EVERY = 30 * MIN; // (while a page is open)
  const FIRST = 8000; // (after a page opens: once it has settled)
  const AFTER_PLAN = 4000; // (a plan made, moved or cleared)
  const QUIET = 10 * MIN; // (a page opened within this of the last copy sent, nothing changed here: nothing read again)

  const ask = (op, extra = {}) => Promise.resolve(api.runtime.sendMessage({ type: 'feed', op, host: location.host, ...extra }))
    .then((r) => (r && r.ok === false && r.message ? Promise.reject(new Error(r.message)) : r));
  const listeners = new Set();
  let last = null; // what the background said last: { on, link, at, count, error }
  const told = (st) => { last = st || last; for (const fn of [...listeners]) { try { fn(last); } catch { /* a sheet gone */ } } return last; };

  /** The private link and the ways a calendar app takes it: Apple's (webcal), Google's and Outlook's add-by-address. */
  function links(link) {
    const https = String(link || '');
    const webcal = https.replace(/^https?:/i, 'webcal:');
    const name = encodeURIComponent('Simpl');
    return {
      https, apple: webcal,
      google: `https://calendar.google.com/calendar/r?cid=${encodeURIComponent(webcal)}`,
      office: `https://outlook.office.com/calendar/0/addfromweb?url=${encodeURIComponent(https)}&name=${name}`,
      outlook: `https://outlook.live.com/calendar/0/addfromweb?url=${encodeURIComponent(https)}&name=${name}`,
    };
  }

  const abs = (url) => { try { return url && !/^#/.test(url) ? new URL(url, location.origin).href : ''; } catch { return ''; } };
  const when = (ms) => `${U.fmtDow(ms)} at ${U.fmtTimeLower(ms)}`;
  const KIND = { assignment: 'Assignment', quiz: 'Quiz', discussion_topic: 'Discussion', assessment_request: 'Peer review', planner_note: 'Your task', calendar_event: 'Event' };

  /** The calendar's events: { uid, title, start, end, allDay, day, days, description, url, location, free }. */
  async function events() {
    const today = U.startOfDay(new Date());
    const from = U.addDays(today, -BACK_DAYS);
    const to = U.addDays(today, AHEAD_DAYS);
    const items = await store.plannerRange(from, to); // (a refusal throws: the copy sent last stays)
    const out = [];
    const seen = new Set();
    for (const it of items || []) {
      if (!it.date || seen.has(it.id)) continue;
      if (!(it.isDue || it.custom || it.type === 'calendar_event')) continue; // (pages, announcements, ungraded talk: not on a calendar)
      seen.add(it.id);
      const p = it.raw?.plannable || {};
      const course = it.custom && !it.courseId ? '' : it.courseName || '';
      const done = !!(it.submitted || it.complete || it.excused || it.graded);
      const kind = KIND[it.type] || it.kind || '';
      const facts = [course, kind, it.points != null ? `${store.fmtPts(it.points)} pts` : ''].filter(Boolean).join(' · ');
      const state = it.excused ? 'Excused' : it.submitted ? 'Handed in' : it.complete ? 'Done' : it.missing ? 'Missing' : '';
      const title = `${done ? '✓ ' : ''}${it.title}${course && !it.custom ? ` · ${course}` : ''}`;
      const base = { uid: `${it.id.replace(/[^A-Za-z0-9_.-]/g, '-')}@simplcourses.com`, title, url: abs(it.url), location: p.location_name || '' };
      if (it.type === 'calendar_event') {
        const end = U.parse(p.end_at);
        if (p.all_day && p.all_day_date) out.push({ ...base, allDay: true, day: p.all_day_date, days: 1, description: [facts, base.url].filter(Boolean).join('\n') });
        else out.push({ ...base, start: it.date.getTime(), end: end ? end.getTime() : it.date.getTime(), description: [facts, base.url].filter(Boolean).join('\n') });
      } else {
        // work, and a task of one's own: a moment (when it is due), shown free — a due time is not time taken
        const t = it.date.getTime();
        out.push({ ...base, start: t, end: t, free: true, description: [facts, `${it.custom ? 'For' : 'Due'} ${when(t)}`, state, base.url].filter(Boolean).join('\n') });
      }
    }
    for (const [id, p] of Object.entries(BCV.plan?.all() || {})) {
      const a = Date.parse(p.at);
      if (!a || a < from.getTime() || a > to.getTime()) continue;
      out.push({
        uid: `plan-${String(id).replace(/[^A-Za-z0-9_.-]/g, '-')}@simplcourses.com`,
        title: `Work on ${p.title}`,
        start: a, end: a + (p.mins || 60) * MIN,
        url: abs(p.url),
        description: [[p.course, `${BCV.plan.lenWord(p.mins || 60)} set aside`].filter(Boolean).join(' · '), p.due ? `Due ${when(Date.parse(p.due))}` : '', abs(p.url), 'Planned in Simpl'].filter(Boolean).join('\n'),
      });
    }
    out.sort((x, y) => (x.start ?? Date.parse(x.day)) - (y.start ?? Date.parse(y.day)) || x.uid.localeCompare(y.uid));
    return out;
  }

  let sending = null;
  let dirty = false; // (a plan changed since the last copy was sent)
  /** The calendar sent (when it is on, and something in it changed): what the background said. */
  async function publish({ force = false } = {}) {
    if (sending && !force) return sending;
    // (one sent on its own still on its way, read before the calendar was made: this one, forced, goes after it)
    if (sending) { await sending.catch(() => {}); return publish({ force }); }
    sending = (async () => {
      const st = await state();
      if (!st?.on) return st;
      if (!force && !dirty && !st.error && Date.now() - (st.at || 0) < QUIET) return st;
      dirty = false;
      let list;
      try { list = await events(); } catch { return st; } // (Canvas did not answer: the copy sent last stays, and is tried again)
      return told(await ask('publish', { events: list, force, name: 'Simpl' }));
    })().catch((e) => told({ ...(last || {}), error: e?.message || String(e) })).finally(() => { sending = null; });
    return sending;
  }
  const state = async () => told(await ask('state').catch(() => last));
  async function turnOn() {
    const st = told(await ask('on'));
    await publish({ force: true });
    return last || st;
  }
  async function turnOff() { return told(await ask('off')); }
  const onChange = (fn) => { listeners.add(fn); return () => listeners.delete(fn); };

  // ---- kept up to date while a page is open ---------------------------------------------------------------------
  let planTimer = null;
  function start() {
    if (start.done || !api?.runtime?.sendMessage) return; // (Brightspace too: lib/d2l-api.js answers the planner)
    start.done = true;
    setTimeout(() => { BCV.plan?.retireSending?.().catch(() => {}); publish().catch(() => {}); }, FIRST);
    setInterval(() => { if (document.visibilityState === 'visible') publish().catch(() => {}); }, EVERY);
    BCV.plan?.onChange?.(() => { dirty = true; clearTimeout(planTimer); planTimer = setTimeout(() => publish().catch(() => {}), AFTER_PLAN); });
  }

  BCV.feed = { state, publish, turnOn, turnOff, onChange, links, events, start };
  if (document.readyState === 'complete') start(); else addEventListener('load', start, { once: true });
})();
