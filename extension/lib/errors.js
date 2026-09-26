/* Error codes (2.98.19). Every error Simpl shows — a toast, a block on a screen that could not be
 * loaded — carries a code, so a report can say exactly what went wrong and where:
 *
 *   SC-<screen>-<what>      e.g. SC-Q-403, SC-D-NET, SC-W-LOAD
 *
 *   screen  D Dashboard · T To Do · K Calendar · I Inbox · N Notifications · G Grades · W Tools ·
 *           C Courses and a course's pages · R Groups · Q a quiz · S a hand-in · F feedback ·
 *           P the setup and Personalize · X a page Canvas draws itself
 *   what    Canvas's own answer (401 signed out, 403 not allowed, 404 not there, 409/422 refused,
 *           429 too busy, 500-504 Canvas's side) · NET no answer at all (offline, timed out) ·
 *           LOAD a part of Simpl that did not load · APP anything else, Simpl's own doing
 *
 * The request layer (lib/canvas-api.js) and the on-demand loader (content/app/lazy.js) say when
 * something failed (failed()); an error shown within a few seconds of that takes its kind, and one
 * shown with the error itself in hand takes the error's own (codeFor(err)). The codes shown are kept
 * — a dozen in memory for the page, the last day's twenty in extension storage — for the Report a
 * bug button to carry to simplcourses.com/report, where they are filled in. docs/ERROR-CODES.md
 * lists them. Nothing here leaves the browser by itself. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const AREAS = { dashboard: 'D', todo: 'T', calendar: 'K', inbox: 'I', notifications: 'N', gpa: 'G', tools: 'W', courses: 'C', course: 'C', groups: 'R', group: 'R', quiz: 'Q', submit: 'S', feedback: 'F', setup: 'P', personalize: 'P', native: 'X' };
  const KINDS = ['401', '403', '404', '409', '422', '429', '500', '502', '503', '504', 'NET', 'LOAD', 'APP'];
  const RECENT_MS = 15000; // a failure this recent is taken to be what an error shown now is about
  const KEY = 'errors:recent';
  const DAY = 24 * 60 * 60 * 1000;
  let last = null; // { kind, at }
  const seen = []; // [{ code, at }], newest first, this page's
  const code = (area, kind) => `SC-${area}-${kind}`;
  const PATTERN = /^SC-[A-Z]-(\d{3}|NET|LOAD|APP)$/;

  /** A failure's kind: Canvas's status (0 is no answer at all), or a word. */
  function kindOf(v) {
    if (v === null || v === undefined) return null;
    if (typeof v === 'object') {
      if (typeof v.kind === 'string') return kindOf(v.kind);
      if (v.status !== undefined) return kindOf(Number(v.status));
      if (v.name === 'TypeError' || /network|fetch|timed? ?out/i.test(String(v.message || ''))) return 'NET';
      return null;
    }
    if (typeof v === 'number') return !v ? 'NET' : v >= 500 ? (KINDS.includes(String(v)) ? String(v) : '500') : String(v);
    const s = String(v).toUpperCase();
    return /^\d{3}$/.test(s) || ['NET', 'LOAD', 'APP'].includes(s) ? s : null;
  }
  /** Something failed just now (a request, a part of Simpl loading): its kind is kept for a moment. */
  function failed(kind) {
    const k = kindOf(kind);
    if (k) last = { kind: k, at: Date.now() };
  }
  /** The screen the student is on, as its letter. */
  function area() {
    const st = BCV.app?.state;
    if (st?.quizOpen) return 'Q';
    if (document.getElementById('bcv-setup') || document.documentElement.classList.contains('bcv-setup-open')) return 'P';
    const r = st?.route;
    if (r?.screen === 'course' && (r.tab === 'quizzes' || r.sub === 'take')) return 'Q';
    return AREAS[r?.screen] || (document.documentElement.classList.contains('bcv-on') ? 'D' : 'X');
  }
  /** The code for an error shown now: the error's own kind when it is in hand, a failure of the last
   *  few seconds otherwise, and APP when nothing failed underneath (Simpl's own doing). */
  function codeFor(err = null) {
    const k = kindOf(err) || (last && Date.now() - last.at < RECENT_MS ? last.kind : null) || 'APP';
    return code(area(), k);
  }
  let writing = Promise.resolve(); // one write at a time: codes shown together each read what the last one wrote
  /** A code shown: kept for the report (this page's dozen, and the last day's twenty in extension storage). */
  function note(c) {
    if (!PATTERN.test(String(c))) return;
    const at = Date.now();
    seen.unshift({ code: c, at });
    if (seen.length > 12) seen.length = 12;
    const api = BCV.api;
    if (!api?.storage?.local?.get) return; // kept for the page alone
    writing = writing.then(async () => {
      const r = await api.storage.local.get(KEY);
      const kept = [{ code: c, at }, ...((r && r[KEY]) || [])].filter((x) => x && PATTERN.test(x.code) && at - x.at < DAY).slice(0, 20);
      await api.storage.local.set({ [KEY]: kept });
    }).catch(() => {});
    return writing;
  }
  /** The codes to carry to a report: this page's, then the last hour's kept ones, newest first, each once, eight at most. */
  async function recent() {
    let kept = [];
    try { kept = ((await BCV.api?.storage?.local?.get?.(KEY)) || {})[KEY] || []; } catch { /* this page's alone */ }
    const now = Date.now();
    const out = [];
    for (const x of [...seen, ...kept.filter((k) => now - k.at < 60 * 60 * 1000)]) if (x && PATTERN.test(x.code) && !out.includes(x.code)) out.push(x.code);
    return out.slice(0, 8);
  }

  BCV.errors = { AREAS, KINDS, PATTERN, kindOf, failed, area, codeFor, note, recent };
})();
