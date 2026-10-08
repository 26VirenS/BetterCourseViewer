/* Which learning platform this page belongs to: Canvas, the interface's first home, or Brightspace (D2L). The
 * interface speaks Canvas's language everywhere — its screens, its store, its addresses — and Brightspace is made
 * to answer in it (lib/d2l-api.js). Here is only what has to be known before anything else runs:
 *
 *  - whether this is a Brightspace page (its own hosts, *.brightspace.com and *.d2l.com, or a school's own address
 *    whose pages all live under /d2l/);
 *  - the Brightspace page that stands for each of the interface's addresses, and back, so the address bar always
 *    shows a page that exists: a course's home is /d2l/home/<id>, a quiz Brightspace's own quiz page, and every
 *    other screen the interface draws the homepage (or the course's) with the interface's address in `?simpl=` —
 *    reloaded or bookmarked it is the same screen, and with the interface off a page that still loads;
 *  - a Canvas-style address that reached Brightspace anyway (opened in a new tab, typed) lands on Brightspace's
 *    404, which names it (targetUrl): sent on, at once, to the page it means. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  if (BCV.lms) return;

  const host = location.hostname.toLowerCase();
  const ownHost = /(^|\.)(brightspace\.com|d2l\.com|desire2learn\.com)$/.test(host);
  const underD2L = location.pathname === '/d2l' || location.pathname.startsWith('/d2l/');
  const NOTE = 'bcv:lms';
  let noted = false;
  try { noted = localStorage.getItem(NOTE) === 'd2l'; } catch { /* no note */ }
  const d2l = ownHost || underD2L || noted;
  if (d2l && !noted) try { localStorage.setItem(NOTE, 'd2l'); } catch { /* noted next time */ }
  if (d2l) document.documentElement.classList.add('bcv-d2l');

  const enc = (s) => encodeURIComponent(s);
  const n = (s) => /^\d+$/.test(String(s || ''));

  // ---- the interface's addresses → Brightspace's pages --------------------------------------------------
  // Each: a Canvas-style path pattern, and the Brightspace address for it. Ids in the ranges lib/d2l-api.js gives
  // the tools Canvas shares one id space for (a quiz 1e9 + its id, a discussion 2e9 + …, an announcement 3e9 + …, a
  // grade item 4e9 + …) say which tool an assignment is.
  const SPACE = { quiz: 1e9, topic: 2e9, news: 3e9, grade: 4e9 };
  const space = (id) => { const v = Number(id); return v >= SPACE.grade ? ['grade', v - SPACE.grade] : v >= SPACE.news ? ['news', v - SPACE.news] : v >= SPACE.topic ? ['topic', v - SPACE.topic] : v >= SPACE.quiz ? ['quiz', v - SPACE.quiz] : ['own', v]; };
  // Only what Brightspace itself draws needs its own page (the interface stands down there and shows it as it is): a
  // quiz, the content viewer. Every screen the interface draws stays on a page that always loads for whoever is
  // enrolled, whatever the school's Brightspace (its role pages, its old or new experiences, which differ from one
  // school to the next and redirect a stale address to an error): the homepage, or the course's own, with the
  // interface's address in ?simpl=.
  const TO_PAGE = [
    [/^\/?$/, () => '/d2l/home'],
    [/^\/dashboard$/, () => '/d2l/home'],
    [/^\/courses\/(\d+)$/, (m) => `/d2l/home/${m[1]}`],
    [/^\/courses\/(\d+)\/assignments\/(\d+)(?:\/.*)?$/, (m) => (space(m[2])[0] === 'quiz' ? `/d2l/lms/quizzing/user/quiz_summary.d2l?qi=${space(m[2])[1]}&ou=${m[1]}` : null)],
    [/^\/courses\/(\d+)\/quizzes\/(\d+)(?:\/.*)?$/, (m) => `/d2l/lms/quizzing/user/quiz_summary.d2l?qi=${m[2]}&ou=${m[1]}`],
    [/^\/courses\/(\d+)\/modules\/items\/(\d+)$/, (m) => `/d2l/le/content/${m[1]}/viewContent/${m[2]}/View`],
  ];
  const courseOf = (path) => /^\/courses\/(\d+)(?:\/|$)/.exec(path)?.[1] || null;

  /** The Brightspace address for one of the interface's (Canvas-style) addresses. Already a Brightspace page, or
   *  another site's: as it is. */
  function toPage(raw) {
    if (!d2l || !raw) return raw;
    let url;
    try { url = new URL(raw, location.origin); } catch { return raw; }
    if (url.origin !== location.origin || url.pathname.startsWith('/d2l/')) return raw;
    const path = url.pathname.replace(/\/+$/, '') || '/';
    const query = url.search;
    const hash = url.hash;
    // (a query of the interface's own — ?bcv=setup, ?view=feed — or anything Brightspace has no page for goes in ?simpl=)
    if (!query) {
      for (const [re, to] of TO_PAGE) {
        const m = path.match(re);
        const page = m && to(m);
        if (page) return page + hash;
      }
    }
    const ou = courseOf(path);
    return `/d2l/home${ou ? `/${ou}` : ''}?simpl=${enc(path + query)}${hash}`;
  }

  // ---- Brightspace's pages → the interface's addresses --------------------------------------------------------
  // A page the interface does not draw itself on Brightspace — a quiz (taken on Brightspace's own pages), the
  // content viewer, a page of Brightspace's own — is given as the route with ?bcv=native, which leaves the page as
  // Brightspace drew it.
  const FROM_PAGE = [
    [/^\/d2l\/home\/?$/, () => '/'],
    [/^\/d2l\/home\/(\d+)\/?$/, (m) => `/courses/${m[1]}`],
    [/^\/d2l\/lms\/dropbox\/user\/folders_list\.d2l$/, (m, q) => n(q.get('ou')) && `/courses/${q.get('ou')}/assignments`],
    [/^\/d2l\/lms\/dropbox\/user\/folder_submit_files\.d2l$/, (m, q) => n(q.get('ou')) && n(q.get('db')) && `/courses/${q.get('ou')}/assignments/${q.get('db')}`],
    [/^\/d2l\/lms\/quizzing\/user\/quizzes_list\.d2l$/, (m, q) => n(q.get('ou')) && `/courses/${q.get('ou')}/quizzes`],
    [/^\/d2l\/lms\/quizzing\/user\/quiz_summary\.d2l$/, (m, q) => n(q.get('ou')) && n(q.get('qi')) && `/courses/${q.get('ou')}/quizzes/${q.get('qi')}?bcv=native`],
    [/^\/d2l\/lms\/grades\/my_grades\/main\.d2l$/, (m, q) => n(q.get('ou')) && `/courses/${q.get('ou')}/grades`],
    [/^\/d2l\/le\/content\/(\d+)\/Home\/?$/, (m) => `/courses/${m[1]}/modules`],
    [/^\/d2l\/le\/lessons\/(\d+)\/?$/, (m) => `/courses/${m[1]}/modules`],
    [/^\/d2l\/le\/content\/(\d+)\/viewContent\/(\d+)\/View\/?$/, (m, q) => (q.get('simpl') === 'page' ? `/courses/${m[1]}/pages/${m[2]}` : q.get('simpl') === 'file' ? `/courses/${m[1]}/files/${m[2]}` : `/courses/${m[1]}/modules/items/${m[2]}?bcv=native`)],
    [/^\/d2l\/lms\/news\/main\.d2l$/, (m, q) => n(q.get('ou')) && `/courses/${q.get('ou')}/announcements`],
    [/^\/d2l\/le\/news\/(\d+)\/(\d+)\/view\/?$/, (m) => `/courses/${m[1]}/discussion_topics/${SPACE.news + Number(m[2])}`],
    [/^\/d2l\/le\/(\d+)\/discussions\/List\/?$/, (m) => `/courses/${m[1]}/discussion_topics`],
    [/^\/d2l\/le\/(\d+)\/discussions\/topics\/(\d+)\/View\/?$/, (m) => `/courses/${m[1]}/discussion_topics/${SPACE.topic + Number(m[2])}`],
    [/^\/d2l\/lms\/classlist\/classlist\.d2l$/, (m, q) => n(q.get('ou')) && `/courses/${q.get('ou')}/users`],
  ];

  /** The interface's (Canvas-style) address for a Brightspace page, for its router; null for a Brightspace page the
   *  interface has no screen for (its preferences, a tool of its own), which is left to Brightspace. */
  function fromPage(raw = location.href) {
    let url;
    try { url = new URL(raw, location.origin); } catch { return null; }
    if (!d2l) return url.pathname + url.search + url.hash;
    const q = url.searchParams;
    const own = q.get('simpl');
    if (own && own.startsWith('/')) return own + url.hash;
    const path = url.pathname;
    for (const [re, to] of FROM_PAGE) {
      const m = path.match(re);
      if (!m) continue;
      const got = to(m, q);
      if (got) return got + (path.startsWith('/d2l/home') ? url.hash : '');
    }
    return null;
  }

  // ---- a Canvas-style address that reached Brightspace's 404 ------------------------------------------------------
  if (d2l && window.self === window.top && /^\/d2l\/error\/404\/log\/?$/i.test(location.pathname)) {
    try {
      const target = new URL(new URLSearchParams(location.search).get('targetUrl') || '', location.origin);
      const page = target.pathname.startsWith('/d2l/') ? null : toPage(target.pathname + target.search + target.hash);
      if (page && page !== location.pathname + location.search) location.replace(page);
    } catch { /* Brightspace's own 404, then */ }
  }

  BCV.lms = {
    /** 'd2l' on a Brightspace page, 'canvas' otherwise. */
    kind: d2l ? 'd2l' : 'canvas',
    d2l,
    /** The platform's name, for the few words that say it ("Open in Brightspace"). */
    name: d2l ? 'Brightspace' : 'Canvas',
    /** The page the interface's dashboard is. */
    home: d2l ? '/d2l/home' : '/',
    toPage,
    fromPage,
    /** Brightspace's sign-in page (or its own landing that leads to it). */
    isLoginPage: () => d2l && /^\/d2l\/(login|lp\/auth\/login)/i.test(location.pathname),
  };
})();
