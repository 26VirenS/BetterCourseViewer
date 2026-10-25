/* (2.99.107) Pull to plan. A piece of work on To Do or the Dashboard, pulled to the right, opens a rail of the free
 * times between now and when it is due under it; the further it is pulled the later the time, and let go on one, it is
 * planned there ("Planned Tue 4:00 PM" on the row, a block on the Calendar). Pulled back short, or Escape, plans
 * nothing. There is no grid to drop it on: the time comes to the row.
 *
 * The free times are found, not drawn: half hours from 8 am to 11 pm, from a quarter of an hour on until the work is
 * due (two weeks at most; a week for work with no date or already late), clear of what is already on — the calendars
 * linked in Calendar → Sync, the classes and events on Canvas's calendar, and the other plans — with ten minutes kept
 * either side. Each free stretch offers its start and then every two hours through it, four a day at most.
 *
 * The same rail comes up from the keyboard (P on a row: arrows to choose, up and down for how long, Enter, Escape) and
 * from the plan's own pill, where it can be cleared. A plan is this Canvas's own, kept with the other preferences;
 * with Send plans on (Calendar → Sync) each also goes on the student's Canvas calendar, so the calendar feed they
 * subscribed to carries it to Apple, Google or Outlook. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const store = BCV.store;
  const api = BCV.api;
  const C = BCV.canvas;

  const MIN = 60000;
  const HOUR = 60 * MIN;
  const DAY = 24 * HOUR;
  const DAY_FROM = 8; // the hours a plan may sit in (the Calendar's week shows the same)
  const DAY_TO = 23;
  const LOOK_AHEAD = 14 * DAY;
  const NO_DUE = 7 * DAY; // work with no date, or late already: a week to choose from
  const BUFFER = 10 * MIN;
  const PER_DAY = 4;
  const MAX_SLOTS = 40;
  const ENGAGE = 12; // px sideways before a press is a pull
  const LEAD = 26; // px of pull before the first time: let go short of it and nothing is planned
  const STEP = 26; // px of pull a time
  const CHIP = 104; // px a time takes on the rail (its width and the gap after it)
  const LENGTHS = [30, 60, 90, 120];
  const SEND = 'planSend';

  // ---- the plans ------------------------------------------------------------------------------------
  // prefs 'plans' (this Canvas's own): itemId -> { at, mins, title, url, course, color, due, ev } — ev: the Canvas event it was sent as
  let plans = {};
  let loaded = null;
  const listeners = new Set();
  const ready = () => (loaded ||= store.pref('plans', {}).then((p) => { plans = p && typeof p === 'object' ? { ...p } : {}; changed(); return plans; }).catch(() => plans)); // (pills drawn before this paint when it lands)
  const get = (id) => plans[id] || null;
  const all = () => plans;
  const onChange = (fn) => { listeners.add(fn); return () => listeners.delete(fn); };
  // (a function declaration: ready, above, calls it)
  function changed() {
    for (const fn of [...listeners]) { try { fn(plans); } catch { /* a screen gone */ } }
    for (const el of document.querySelectorAll('.bcv-planpill[data-plan-pill]')) paintPill(el, el.dataset.planPill);
  }
  try { // another tab planned something: this one follows
    api.storage.onChanged.addListener((changes, area) => {
      if (area !== 'local' || !changes[`prefs:${location.host}`]) return;
      store.pref('plans', {}).then((p) => {
        const next = p && typeof p === 'object' ? p : {};
        if (JSON.stringify(next) === JSON.stringify(plans)) return;
        plans = { ...next };
        changed();
      }).catch(() => {});
    });
  } catch { /* no change events: this tab's own plans still show */ }

  const lengthFor = (it) => (/quiz|discussion/.test(it.type || '') ? 30 : 60);
  const isDoneWork = (it) => !!(it.complete || it.submitted || it.graded || it.dismissed || it.excused);
  async function put(it, at, mins) {
    const prev = plans[it.id] || null;
    const plan = { at: new Date(at).toISOString(), mins, title: it.title, url: it.url || '', course: it.custom ? (it.course?.name || 'My task') : (it.courseName || ''), color: it.course?.color || null, due: it.date ? new Date(it.date).toISOString() : null, ev: prev?.ev || null };
    const patch = { [it.id]: plan };
    const cut = Date.now() - 30 * DAY; // (plans a month past are let go)
    for (const [k, p] of Object.entries(plans)) if (k !== it.id && Date.parse(p.at) < cut) patch[k] = null;
    plans = Object.fromEntries(Object.entries({ ...plans, ...patch }).filter(([, v]) => v));
    changed();
    await store.mergePref('plans', patch);
    sendOut(it.id).catch(() => {});
    return plan;
  }
  /** (2.99.109) A plan moved to another time, as it is (its block dragged on the Calendar's week). */
  async function move(id, at) {
    const p = plans[id];
    if (!p) return null;
    const next = { ...p, at: new Date(at).toISOString() };
    plans = { ...plans, [id]: next };
    changed();
    await store.mergePref('plans', { [id]: next });
    sendOut(id).catch(() => {});
    return next;
  }
  async function clear(id) {
    const prev = plans[id];
    if (!prev) return;
    const { [id]: _gone, ...rest } = plans;
    plans = rest;
    changed();
    await store.mergePref('plans', { [id]: null });
    if (prev.ev) C.del(`/api/v1/calendar_events/${prev.ev}`).catch(() => {});
  }

  // ---- plans on the student's Canvas calendar (Send plans) ---------------------------------------------
  const canSend = () => !BCV.lms?.d2l; // (Brightspace's stand-in has no calendar of the student's own to write to)
  const sending = async () => canSend() && !!(await store.pref(SEND, false));
  const esc = (s) => String(s).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
  async function sendOut(id) {
    if (!(await sending())) return;
    const p = plans[id];
    if (!p) return;
    const me = await store.me();
    if (!me?.id) return;
    const href = p.url ? new URL(p.url, location.origin).href : '';
    const body = { calendar_event: {
      context_code: `user_${me.id}`, title: `Plan: ${p.title}`, start_at: p.at, end_at: new Date(Date.parse(p.at) + p.mins * MIN).toISOString(),
      description: `<p>Planned in Simpl for ${href ? `<a href="${esc(href)}">${esc(p.title)}</a>` : esc(p.title)}${p.due ? `, due ${esc(U.fmtDow(p.due))} at ${esc(U.fmtTimeLower(p.due))}` : ''}.</p>`,
    } };
    try {
      const r = p.ev ? await C.put(`/api/v1/calendar_events/${p.ev}`, body) : await C.post('/api/v1/calendar_events', body);
      if (r?.id != null && String(r.id) !== String(p.ev) && plans[id]) {
        plans[id] = { ...plans[id], ev: String(r.id) };
        await store.mergePref('plans', { [id]: plans[id] });
      }
    } catch (e) {
      if (p.ev && /404|not exist/i.test(e?.message || '')) { plans[id] = { ...p, ev: null }; await store.mergePref('plans', { [id]: plans[id] }); return sendOut(id); } // (deleted in Canvas meanwhile: made again)
      U.toast(`Planned here, but ${BCV.lms?.name || 'Canvas'} would not put it on your calendar: ${e.message}`, { error: true });
    }
  }
  /** Send plans switched: on, every plan still ahead goes on the Canvas calendar; off, those sent come off it. */
  async function setSending(on) {
    await store.setPref(SEND, !!on);
    const ahead = Object.keys(plans).filter((k) => Date.parse(plans[k].at) + plans[k].mins * MIN > Date.now());
    if (on) { for (const k of ahead) await sendOut(k).catch(() => {}); return; }
    const patch = {};
    for (const k of Object.keys(plans)) {
      if (!plans[k].ev) continue;
      C.del(`/api/v1/calendar_events/${plans[k].ev}`).catch(() => {});
      plans[k] = { ...plans[k], ev: null };
      patch[k] = plans[k];
    }
    if (Object.keys(patch).length) await store.mergePref('plans', patch);
  }

  // ---- the calendars linked in Calendar → Sync (read by the background: background.js cal) -------------
  async function linked() {
    try {
      const links = (await api.storage.local.get('cal:links'))['cal:links'];
      const list = Array.isArray(links) ? links : [];
      const feeds = list.length ? await api.storage.local.get(list.map((l) => `cal:feed:${l.id}`)) : {};
      return { links: list, feeds: Object.fromEntries(list.map((l) => [l.id, feeds[`cal:feed:${l.id}`] || null])) };
    } catch {
      return { links: [], feeds: {} };
    }
  }
  /** The linked calendars' events touching [from, to), each with its calendar (only the calendars shown). */
  async function linkedEvents(from, to) {
    const { links, feeds } = await linked();
    const out = [];
    for (const l of links) {
      if (l.on === false) continue;
      for (const ev of feeds[l.id]?.events || []) if (ev.end > from && ev.start < to) out.push({ ...ev, link: l });
    }
    return out;
  }

  // ---- free time ----------------------------------------------------------------------------------------
  let busyMemo = null; // { at, key, list }: what is on, read once a minute at most
  async function busyTimes(from, to) {
    const key = `${Math.floor(from / HOUR)}:${Math.floor(to / HOUR)}`;
    if (busyMemo && busyMemo.key === key && Date.now() - busyMemo.at < MIN) return busyMemo.list;
    const out = [];
    for (const ev of await linkedEvents(from, to)) if (!ev.allDay && !ev.free && ev.end > ev.start) out.push({ a: ev.start, b: ev.end });
    const sent = new Set(Object.values(plans).map((p) => p.ev).filter(Boolean));
    try { // Canvas's own: classes, office hours, a time reserved — never longer than a moment to wait for
      const contexts = await store.calendarContexts();
      const sel = await store.selectedContexts(contexts);
      const res = await Promise.race([store.calendarEvents(new Date(from), new Date(to), sel), new Promise((r) => setTimeout(() => r(null), 1500))]);
      for (const e of (Array.isArray(res) ? res : res?.events) || []) {
        if (e.type === 'assignment' || e.assignment || e.all_day || sent.has(String(e.id))) continue;
        const a = Date.parse(e.start_at);
        const b = Date.parse(e.end_at || '');
        if (a && b > a) out.push({ a, b });
      }
    } catch { /* the linked calendars and the plans still count */ }
    busyMemo = { at: Date.now(), key, list: out };
    return out;
  }
  const atHour = (d, hr) => new Date(d.getFullYear(), d.getMonth(), d.getDate(), hr).getTime();
  const half = 30 * MIN;
  /** The times offered: [{ at, end }] in order. busy: [{ a, b }] (the plans are added by the caller). */
  function freeSlots({ now = Date.now(), due = null, mins = 60, busy = [] } = {}) {
    const len = mins * MIN;
    const start = Math.ceil((now + 15 * MIN) / half) * half;
    const last = due && due >= start + len ? Math.min(due, now + LOOK_AHEAD) : now + NO_DUE;
    const blocks = busy.map(({ a, b }) => [a - BUFFER, b + BUFFER]).sort((x, y) => x[0] - y[0]);
    const out = [];
    for (let d = new Date(start); out.length < MAX_SLOTS; d = new Date(d.getFullYear(), d.getMonth(), d.getDate() + 1)) {
      const dayA = Math.max(start, atHour(d, DAY_FROM));
      const dayB = Math.min(last, atHour(d, DAY_TO));
      if (atHour(d, 0) >= last) break;
      if (dayA >= dayB) continue;
      const gaps = [];
      let cursor = dayA;
      for (const [a, b] of blocks) {
        if (b <= cursor) continue;
        if (a >= dayB) break;
        if (a > cursor) gaps.push([cursor, a]);
        cursor = Math.max(cursor, b);
      }
      if (cursor < dayB) gaps.push([cursor, dayB]);
      let n = 0;
      for (const [a, b] of gaps) {
        for (let t = Math.ceil(a / half) * half; t + len <= b && n < PER_DAY; t += 2 * HOUR) { out.push({ at: t, end: t + len }); n++; }
        if (n >= PER_DAY) break;
      }
    }
    return out.slice(0, MAX_SLOTS);
  }
  async function slotsFor(it, mins) {
    const now = Date.now();
    const due = it.date ? new Date(it.date).getTime() : null;
    const busy = [...await busyTimes(now, now + LOOK_AHEAD + DAY)]; // (a copy: the memo is shared)
    for (const [k, p] of Object.entries(plans)) if (k !== it.id) { const a = Date.parse(p.at); busy.push({ a, b: a + p.mins * MIN }); }
    return freeSlots({ now, due, mins, busy });
  }

  // ---- words -----------------------------------------------------------------------------------------------
  function dayWord(ms, now = new Date()) {
    const diff = U.dayDiff(new Date(ms), now);
    if (diff === 0) return 'Today';
    if (diff === 1) return 'Tomorrow';
    if (diff > 1 && diff < 7) return U.DAYS[new Date(ms).getDay()];
    return U.fmtDow(ms);
  }
  const whenWords = (ms) => `${dayWord(ms)} ${U.fmtTime(ms)}`;
  const lenWord = (m) => (m < 60 ? `${m}m` : m % 60 ? `${Math.floor(m / 60)}½h` : `${m / 60}h`);

  // ---- the pill on a planned row ----------------------------------------------------------------------------
  function paintPill(el, id) {
    const p = plans[id];
    el.hidden = !p;
    if (!p) { el.replaceChildren(); return; }
    const at = Date.parse(p.at);
    const past = at + p.mins * MIN < Date.now();
    el.classList.toggle('is-past', past);
    el.replaceChildren(U.svg(IC.clock, { size: 11, stroke: 'currentColor', width: 2.3 }), h('span', { text: `${past ? 'Was planned' : 'Planned'} ${whenWords(at)}` }));
    el.title = `${lenWord(p.mins)} from ${U.fmtTime(at)} · press to move or clear it`;
  }
  /** The row's plan, as a pill that opens the rail to move or clear it (hidden while there is none). */
  function pill(it) {
    const el = h('button', { type: 'button', class: 'bcv-planpill', dataset: { planPill: it.id } });
    paintPill(el, it.id);
    el.addEventListener('click', (e) => {
      e.preventDefault();
      e.stopPropagation();
      if (bar?.from === el) { closeBar(); return; } // (pressed again: put away)
      openBar(el.closest('.bcv-row') || el, it, { mode: 'pick', from: el });
    });
    return el;
  }

  // ---- the rail ---------------------------------------------------------------------------------------------
  let bar = null; // the rail up now: { el, close }
  const closeBar = () => bar?.close();
  function openBar(rowEl, it, { mode = 'pick', from = null } = {}) {
    closeBar();
    let mins = plans[it.id]?.mins || lengthFor(it);
    let slots = null;
    let sel = mode === 'pick' ? 0 : -1;
    let pulled = 0;
    let closed = false;
    const planned = plans[it.id] ? Date.parse(plans[it.id].at) : null;
    const track = U.el('bcv-planbar__track');
    const status = h('div', { class: 'bcv-planbar__status', 'aria-live': 'polite' });
    const lens = mode === 'pull' ? U.el('bcv-planbar__lens') : null;
    const lenBtns = LENGTHS.map((m) => h('button', { type: 'button', class: 'bcv-planbar__lenbtn', dataset: { mins: String(m) }, text: lenWord(m), title: `${m} minutes`, onclick: () => setLen(m) }));
    const head = U.el('bcv-planbar__head', [
      U.svg(IC.clock, { size: 13, stroke: 'var(--bcv-blue)', width: 2.2 }),
      h('span', { class: 'bcv-planbar__title bcv-ellip', text: `Plan ${it.title}` }),
      U.el('bcv-planbar__len', lenBtns, { role: 'group', 'aria-label': 'How long' }),
      planned != null && mode === 'pick' ? U.btn('Clear', { kind: 'xs', cls: 'bcv-planbar__clear', onClick: () => { clear(it.id); close(); } }) : null,
    ]);
    const el = U.el(`bcv-planbar bcv-planbar--${mode}`, [head, U.el('bcv-planbar__rail', [track, lens]), status], { role: 'dialog', 'aria-label': `Plan a time for ${it.title}` });
    const due = it.date ? new Date(it.date).getTime() : null;

    function paint() {
      for (const b of lenBtns) { const on = Number(b.dataset.mins) === mins; b.classList.toggle('is-on', on); b.setAttribute('aria-pressed', on ? 'true' : 'false'); }
      if (!slots) { track.replaceChildren(U.el('bcv-planbar__wait', 'Finding free time…')); status.textContent = ''; return; }
      // (2.99.109) the day said once, over the first of its times, and a little more room before each new day: the times
      // read as a day's handful, not "Today" four times over
      const chips = slots.map((s, i) => {
        const prevDay = i && U.sameDay(new Date(slots[i - 1].at), new Date(s.at));
        return h('button', { type: 'button', class: `bcv-planbar__slot ${i === sel ? 'is-on' : ''} ${prevDay ? '' : 'is-day'} ${planned === s.at ? 'is-current' : ''}`, dataset: { at: String(s.at) }, tabIndex: mode === 'pick' ? (i === sel ? 0 : -1) : -1, 'aria-label': `${whenWords(s.at)}, ${lenWord(mins)}`, onclick: () => commit(i) }, [
          h('span', { class: 'bcv-planbar__day', text: prevDay ? '' : dayWord(s.at), 'aria-hidden': prevDay ? 'true' : null }),
          h('span', { class: 'bcv-planbar__time', text: U.fmtTime(s.at) }),
        ]);
      });
      const end = due ? h('span', { class: 'bcv-planbar__due', text: due > Date.now() ? `Due ${dayWord(due)} ${U.fmtTime(due)}` : 'Past due' }) : null;
      track.replaceChildren(...chips, end);
      if (!slots.length) status.textContent = due && due > Date.now() ? 'No free time left before it is due.' : 'No free time in the week ahead.';
      else if (mode === 'pull') status.textContent = sel < 0 ? 'Pull further to choose a time' : `Let go to plan ${whenWords(slots[sel].at)}`;
      else status.textContent = '← → to choose · ↑ ↓ for how long · Enter to plan';
      if (mode === 'pull') glide();
      else track.querySelector('.is-on')?.scrollIntoView?.({ block: 'nearest', inline: 'nearest' });
    }
    // the rail moves with the pull, a time at a time: the one chosen slides into the lens (the second place along, the time
    // before it still in view), gliding there rather than jumping; short of the first, the lens stands empty
    function glide() {
      if (!slots?.length) return;
      const f = Math.max(-1, Math.min(slots.length - 1, (pulled - LEAD) / STEP));
      const next = f < -0.5 ? -1 : Math.round(f);
      // (by where the chip is: a new day has a little more room before it)
      const chipEls = track.querySelectorAll('.bcv-planbar__slot');
      const x = next < 0 ? -CHIP : chipEls[next]?.offsetLeft ?? next * CHIP;
      track.style.transform = `translateX(${CHIP - x}px)`;
      if (rowEl.classList.contains('is-pulling')) rowEl.classList.toggle('is-armed', next >= 0);
      if (next !== sel) {
        sel = next;
        for (const [i, c] of [...track.querySelectorAll('.bcv-planbar__slot')].entries()) c.classList.toggle('is-on', i === sel);
        status.textContent = sel < 0 ? 'Pull further to choose a time' : `Let go to plan ${whenWords(slots[sel].at)}`;
        el.classList.toggle('is-armed', sel >= 0);
        if (sel >= 0) U.haptic?.('select');
      }
    }
    async function load() {
      const want = mins;
      const list = await slotsFor(it, mins).catch(() => []);
      if (closed || want !== mins) return;
      slots = list;
      if (mode === 'pick') {
        const at = planned != null ? slots.findIndex((s) => s.at === planned) : -1;
        sel = slots.length ? Math.max(0, at) : -1;
      }
      paint();
      if (mode === 'pick') track.querySelector('.bcv-planbar__slot.is-on')?.focus({ preventScroll: true });
    }
    function setLen(m) {
      if (m === mins) return;
      mins = m;
      slots = null;
      paint();
      load();
    }
    function place() {
      if (closed) return;
      if (!rowEl.isConnected) { close(); return; }
      const r = rowEl.getBoundingClientRect();
      const vw = document.documentElement.clientWidth;
      const w = Math.min(Math.max(r.width, 380), vw - 24);
      el.style.width = `${w}px`;
      el.style.left = `${Math.max(12, Math.min(r.left, vw - w - 12))}px`;
      const hgt = el.offsetHeight || 132;
      const below = r.bottom + 8 + hgt <= window.innerHeight - 8;
      el.style.top = `${below ? r.bottom + 8 : Math.max(8, r.top - hgt - 8)}px`;
      el.classList.toggle('is-above', !below);
    }
    function commit(i) {
      const s = slots?.[i];
      if (!s) return;
      close();
      put(it, s.at, mins).then(() => {
        U.haptic?.('success');
        const pillEl = rowEl.querySelector?.('.bcv-planpill');
        if (pillEl) { pillEl.classList.remove('is-new'); void pillEl.offsetWidth; pillEl.classList.add('is-new'); }
      }).catch((e) => U.toast(`Could not plan it: ${e.message}`, { error: true }));
    }
    function onKey(e) {
      if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); close(); return; }
      if (mode !== 'pick') return;
      if (e.key === 'ArrowRight' || e.key === 'ArrowLeft') {
        e.preventDefault();
        if (!slots?.length) return;
        sel = Math.max(0, Math.min(slots.length - 1, sel + (e.key === 'ArrowRight' ? 1 : -1)));
        paint();
        track.querySelector('.bcv-planbar__slot.is-on')?.focus({ preventScroll: true });
      } else if (e.key === 'ArrowUp' || e.key === 'ArrowDown') {
        e.preventDefault();
        const i = LENGTHS.indexOf(mins);
        setLen(LENGTHS[Math.max(0, Math.min(LENGTHS.length - 1, i + (e.key === 'ArrowUp' ? 1 : -1)))]);
      } else if (e.key === 'Enter' && e.target.closest?.('.bcv-planbar__slot, .bcv-planbar__rail') && sel >= 0) {
        e.preventDefault();
        commit(sel);
      } else if (e.key === 'Tab') { // (kept inside the rail while it is up)
        const f = [...el.querySelectorAll('button:not([disabled])')].filter((b) => b.tabIndex !== -1);
        if (!f.length) return;
        const i = f.indexOf(document.activeElement);
        if (e.shiftKey && i <= 0) { e.preventDefault(); f[f.length - 1].focus(); } else if (!e.shiftKey && i === f.length - 1) { e.preventDefault(); f[0].focus(); }
      }
    }
    const away = (e) => { if (!el.contains(e.target) && !(from && from.contains(e.target))) close(); };
    const reflow = () => requestAnimationFrame(place);
    function close() {
      if (closed) return;
      closed = true;
      if (bar?.el === el) bar = null;
      document.removeEventListener('keydown', onKey, true);
      document.removeEventListener('pointerdown', away, true);
      window.removeEventListener('scroll', reflow, true);
      window.removeEventListener('resize', reflow);
      el.classList.add('is-closing');
      const gone = () => el.remove();
      if (BCV.motion?.reduced?.()) gone(); else { el.addEventListener('animationend', gone, { once: true }); setTimeout(gone, 300); }
      if (mode === 'pick' && el.contains(document.activeElement)) (from || rowEl.querySelector?.('a, button') || rowEl).focus?.({ preventScroll: true });
    }

    document.body.append(el);
    place();
    paint();
    document.addEventListener('keydown', onKey, true);
    window.addEventListener('scroll', reflow, true);
    window.addEventListener('resize', reflow);
    if (mode === 'pick') setTimeout(() => { if (!closed) document.addEventListener('pointerdown', away, true); }, 0);
    load();
    bar = {
      el, close, from,
      pull(px) { pulled = px; if (slots) glide(); },
      chosen() { return sel >= 0 && slots?.[sel] ? { ...slots[sel], mins, i: sel } : null; },
      commit,
    };
    return bar;
  }

  // ---- the pull ---------------------------------------------------------------------------------------------
  // the click a pull ends with goes nowhere: the next click, unless a press comes first (a real click always starts with one)
  let swallowUntil = 0;
  window.addEventListener('pointerdown', () => { swallowUntil = 0; }, true);
  const rubber = (x, max = 64) => (x <= 0 ? 0 : (max * x) / (x + max)); // (follows at first, then resists)
  const typing = (t) => !!t?.closest?.('input, textarea, select, [contenteditable="true"]');
  function pullStart(e, rowEl, it) {
    if (e.button !== 0 || !e.isPrimary || bar || isDoneWork(it)) return;
    if (e.target.closest('button, input, textarea, select, label, .bcv-switch, .bcv-pri, [contenteditable="true"]')) return; // (the row's own controls)
    const x0 = e.clientX;
    const y0 = e.clientY;
    const pid = e.pointerId;
    let engaged = false;
    let rail = null;
    let reveal = null;
    const move = (ev) => {
      if (ev.pointerId !== pid) return;
      const dx = ev.clientX - x0;
      const dy = ev.clientY - y0;
      if (!engaged) {
        if ((Math.abs(dy) > 10 && Math.abs(dy) > Math.abs(dx)) || dx < -ENGAGE) { stop(); return; }
        if (dx < ENGAGE || dx < Math.abs(dy) * 1.4) return;
        engaged = true;
        try { rowEl.setPointerCapture(pid); } catch { /* the window still hears it */ }
        try { window.getSelection()?.removeAllRanges(); } catch { /* none */ }
        rowEl.classList.add('is-pulling');
        rowEl.style.transition = 'none';
        // (2.99.109) what the pull will do, uncovered behind the row as it moves: a clock that grows with the pull and
        // fills once a time is chosen (as a list's swipe shows its action under the row)
        reveal = U.el('bcv-pullreveal', U.svg(IC.clock, { size: 16, stroke: 'currentColor', width: 2.3 }), { 'aria-hidden': 'true' });
        rowEl.prepend(reveal);
        rail = openBar(rowEl, it, { mode: 'pull' });
      }
      ev.preventDefault();
      const shift = rubber(dx - ENGAGE);
      rowEl.style.transform = `translateX(${shift.toFixed(1)}px)`;
      rowEl.style.setProperty('--bcv-pull', `${shift.toFixed(1)}px`);
      rowEl.style.setProperty('--bcv-pull-k', Math.min(1, shift / 40).toFixed(3));
      rail.pull(dx - ENGAGE);
    };
    const up = (ev) => { if (ev.pointerId === pid) finish(ev.type === 'pointerup'); };
    const key = (ev) => { if (ev.key === 'Escape' && engaged) finish(false); };
    function stop() {
      window.removeEventListener('pointermove', move, true);
      window.removeEventListener('pointerup', up, true);
      window.removeEventListener('pointercancel', up, true);
      window.removeEventListener('keydown', key, true);
    }
    function finish(letGo) {
      stop();
      if (!engaged) return;
      swallowUntil = performance.now() + 1000;
      const pick = letGo ? rail.chosen() : null;
      rowEl.classList.remove('is-pulling', 'is-armed');
      rowEl.style.transition = ''; // (springs home: the stylesheet's transition on .bcv-pullable)
      rowEl.style.transform = '';
      rowEl.style.removeProperty('--bcv-pull');
      rowEl.style.removeProperty('--bcv-pull-k');
      reveal?.remove();
      if (pick) rail.commit(pick.i); else rail.close();
    }
    window.addEventListener('pointermove', move, { capture: true, passive: false });
    window.addEventListener('pointerup', up, true);
    window.addEventListener('pointercancel', up, true);
    window.addEventListener('keydown', key, true);
  }

  /** A row of work made pullable: the pull, P from the keyboard, and the click a pull ends with swallowed. */
  function attach(rowEl, it) {
    if (!rowEl || !it?.id || rowEl.dataset.planId) return rowEl;
    rowEl.dataset.planId = it.id;
    rowEl.classList.add('bcv-pullable');
    rowEl.setAttribute('draggable', 'false'); // (a link would otherwise be dragged off as a link)
    rowEl.setAttribute('aria-keyshortcuts', 'P'); // (P plans it: said to a screen reader)
    rowEl.addEventListener('dragstart', (e) => e.preventDefault());
    rowEl.addEventListener('pointerdown', (e) => pullStart(e, rowEl, it));
    rowEl.addEventListener('click', (e) => { if (performance.now() < swallowUntil) { swallowUntil = 0; e.preventDefault(); e.stopPropagation(); } }, true);
    rowEl.addEventListener('keydown', (e) => {
      if ((e.key !== 'p' && e.key !== 'P') || e.metaKey || e.ctrlKey || e.altKey || typing(e.target) || bar) return;
      if (isDoneWork(it)) { U.toast('That is done already: nothing to plan.'); return; }
      e.preventDefault();
      openBar(rowEl, it, { mode: 'pick' });
    });
    return rowEl;
  }

  BCV.plan = { ready, get, all, onChange, put, move, clear, attach, pill, open: openBar, close: closeBar, freeSlots, slotsFor, linked, linkedEvents, sending, setSending, canSend, whenWords, dayWord, lenWord, lengthFor };
})();
