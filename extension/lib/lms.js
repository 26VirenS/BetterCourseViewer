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
 *  - the interface's own links carry its own addresses, which its router takes to Brightspace's pages; a link opened
 *    any other way (a middle or ⌘-click, its menu, a drag, window.open) is pointed at the Brightspace page first —
 *    Brightspace answers an address outside /d2l/ with a bare 404 that does not say what was asked for. One that
 *    reached its 404 anyway with the address named (targetUrl) is sent on, at once, to the page it means. */
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
    [/^\/courses\/(\d+)\/files\/(\d+)$/, (m) => `/d2l/le/content/${m[1]}/viewContent/${m[2]}/View`], // (a file is a content topic: Brightspace's viewer shows it)
  ];
  const courseOf = (path) => /^\/courses\/(\d+)(?:\/|$)/.exec(path)?.[1] || null;
  // Brightspace's own page for a screen the interface draws, when the platform's own is asked for (?bcv=native: "Open in
  // Brightspace", the look turned off for a page): it carries ?bcv=native itself, so the interface leaves it as it is.
  const NATIVE = [
    [/^\/courses\/(\d+)\/assignments\/(\d+)$/, (m) => {
      const [kind, id] = space(m[2]);
      if (kind === 'quiz') return `/d2l/lms/quizzing/user/quiz_summary.d2l?qi=${id}&ou=${m[1]}`;
      if (kind === 'topic') return `/d2l/le/${m[1]}/discussions/topics/${id}/View`;
      if (kind === 'news') return `/d2l/le/news/${m[1]}/${id}/view`;
      if (kind === 'grade') return `/d2l/lms/grades/my_grades/main.d2l?ou=${m[1]}`;
      return `/d2l/lms/dropbox/user/folder_submit_files.d2l?db=${id}&ou=${m[1]}`;
    }],
    [/^\/courses\/(\d+)\/discussion_topics\/(\d+)$/, (m) => { const [kind, id] = space(m[2]); return kind === 'news' ? `/d2l/le/news/${m[1]}/${id}/view` : kind === 'topic' ? `/d2l/le/${m[1]}/discussions/topics/${id}/View` : null; }],
    [/^\/courses\/(\d+)\/assignments$/, (m) => `/d2l/lms/dropbox/user/folders_list.d2l?ou=${m[1]}`],
    [/^\/courses\/(\d+)\/quizzes$/, (m) => `/d2l/lms/quizzing/user/quizzes_list.d2l?ou=${m[1]}`],
    [/^\/courses\/(\d+)\/grades$/, (m) => `/d2l/lms/grades/my_grades/main.d2l?ou=${m[1]}`],
    [/^\/courses\/(\d+)\/modules$/, (m) => `/d2l/le/content/${m[1]}/Home`],
    [/^\/courses\/(\d+)\/pages\/(\d+)$/, (m) => `/d2l/le/content/${m[1]}/viewContent/${m[2]}/View`],
    [/^\/courses\/(\d+)\/announcements$/, (m) => `/d2l/lms/news/main.d2l?ou=${m[1]}`],
    [/^\/courses\/(\d+)\/discussion_topics$/, (m) => `/d2l/le/${m[1]}/discussions/List`],
    [/^\/courses\/(\d+)\/users$/, (m) => `/d2l/lms/classlist/classlist.d2l?ou=${m[1]}`],
    [/^\/calendar$/, () => (orgId() ? `/d2l/le/calendar/${orgId()}` : null)],
  ];
  const orgId = () => { try { return JSON.parse(document.documentElement.getAttribute('data-global-context') || '{}').orgId || null; } catch { return null; } };
  const withNative = (page) => `${page}${page.includes('?') ? '&' : '?'}bcv=native`;

  /** The Brightspace address for one of the interface's (Canvas-style) addresses. Already a Brightspace page, or
   *  another site's: as it is. */
  function toPage(raw) {
    if (!d2l || !raw) return raw;
    let url;
    try { url = new URL(raw, location.origin); } catch { return raw; }
    if (url.origin !== location.origin || url.pathname.startsWith('/d2l/')) return raw;
    const path = url.pathname.replace(/\/+$/, '') || '/';
    // (?bcv=native asks for the platform's own page for an address: Brightspace's, where it has one)
    const native = url.searchParams.get('bcv') === 'native';
    if (native) url.searchParams.delete('bcv');
    const query = url.search;
    const hash = url.hash;
    // (a query of the interface's own — ?bcv=setup, ?view=feed — or anything Brightspace has no page for goes in ?simpl=)
    if (!query) {
      for (const [re, to] of TO_PAGE) {
        const m = path.match(re);
        const page = m && to(m);
        if (page) return page + hash;
      }
      if (native) {
        for (const [re, to] of NATIVE) {
          const m = path.match(re);
          const page = m && to(m);
          if (page) return withNative(page) + hash;
        }
      }
    }
    const ou = courseOf(path);
    const back = native ? `${query ? `${query}&` : '?'}bcv=native` : query;
    return `/d2l/home${ou ? `/${ou}` : ''}?simpl=${enc(path + back)}${hash}`;
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
    [/^\/d2l\/le\/lessons\/(\d+)\/units\/\d+\/?$/, (m) => `/courses/${m[1]}/modules`],
    [/^\/d2l\/le\/lessons\/(\d+)\/topics\/(\d+)\/?$/, (m) => `/courses/${m[1]}/modules/items/${m[2]}?bcv=native`], // (the newer content experience's viewer, Brightspace's to show)
    [/^\/d2l\/le\/content\/(\d+)\/viewContent\/(\d+)\/View\/?$/, (m, q) => (q.get('simpl') === 'page' ? `/courses/${m[1]}/pages/${m[2]}` : q.get('simpl') === 'file' ? `/courses/${m[1]}/files/${m[2]}` : `/courses/${m[1]}/modules/items/${m[2]}?bcv=native`)],
    [/^\/d2l\/lms\/news\/main\.d2l$/, (m, q) => n(q.get('ou')) && `/courses/${q.get('ou')}/announcements`],
    [/^\/d2l\/le\/news\/(\d+)\/(\d+)\/view\/?$/, (m) => `/courses/${m[1]}/discussion_topics/${SPACE.news + Number(m[2])}`],
    [/^\/d2l\/le\/(\d+)\/discussions\/List\/?$/, (m) => `/courses/${m[1]}/discussion_topics`],
    [/^\/d2l\/le\/(\d+)\/discussions\/topics\/(\d+)\/View\/?$/, (m) => `/courses/${m[1]}/discussion_topics/${SPACE.topic + Number(m[2])}`],
    [/^\/d2l\/lms\/classlist\/classlist\.d2l$/, (m, q) => n(q.get('ou')) && `/courses/${q.get('ou')}/users`],
    [/^\/d2l\/le\/calendar\/\d+\/?$/, () => '/calendar'],
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
      let got = to(m, q);
      // (Brightspace's own page asked for as itself — ?bcv=native — is the interface's to leave alone)
      if (got && q.get('bcv') === 'native' && !/[?&]bcv=/.test(got)) got = withNative(got);
      if (got) return got + (path.startsWith('/d2l/home') ? url.hash : '');
    }
    return null;
  }

  /** The interface's own address for this page, as a URL to read or change (on Canvas, the page's own): its query
   *  (?bcv=setup, ?bcv=whatsnew…) is the interface's, which on Brightspace rides inside ?simpl=. */
  function routeUrl(raw = location.href) {
    const u = new URL(raw, location.origin);
    return new URL(fromPage(u.href) || u.pathname + u.search + u.hash, location.origin);
  }
  /** The page address (path, query, hash) for one of the interface's, as routeUrl gives it: what history.replaceState
   *  and location.replace are handed after an edit. */
  const pageFor = (url) => toPage(url.pathname + url.search + url.hash);

  // ---- one of the interface's links, opened any way but by its router ----------------------------------------------
  /** The Brightspace page for a link's own address, when it is one of the interface's (same site, outside /d2l/). */
  function pageOfLink(raw) {
    if (!raw || raw.startsWith('#')) return null;
    let u;
    try { u = new URL(raw, location.href); } catch { return null; }
    if (u.origin !== location.origin || u.pathname.startsWith('/d2l/') || u.pathname === '/d2l') return null;
    return toPage(u.pathname + u.search + u.hash);
  }
  if (d2l) {
    // A plain click is the router's (it goes to the Brightspace page itself, content/app/app.js go). Before the browser
    // can take the link anywhere else — the press that opens it in a tab or a window, its menu (open, copy), the key
    // that opens it elsewhere — its address is the Brightspace page's, until the next press; a drag carries that page.
    let swapped = null;
    const restore = () => { if (swapped) { try { swapped.a.setAttribute('href', swapped.raw); } catch { /* gone */ } swapped = null; } };
    const point = (a) => {
      const raw = a?.getAttribute?.('href');
      const page = pageOfLink(raw);
      if (!page) return;
      a.setAttribute('href', page);
      swapped = { a, raw };
    };
    const linkOf = (e) => e.target?.closest?.('a[href]') || null;
    document.addEventListener('pointerdown', (e) => {
      restore();
      const a = linkOf(e);
      // (a link that opens a tab of its own — target=_blank — is the browser's to open, its router never sees it)
      if (e.button === 1 || e.button === 2 || (e.button === 0 && (e.metaKey || e.ctrlKey || e.shiftKey || a?.target === '_blank'))) point(a);
    }, true);
    document.addEventListener('contextmenu', (e) => { if (!swapped) point(linkOf(e)); }, true);
    document.addEventListener('keydown', (e) => {
      restore();
      if ((e.key === 'Enter' && (e.metaKey || e.ctrlKey || e.shiftKey || linkOf(e)?.target === '_blank')) || e.key === 'ContextMenu' || (e.key === 'F10' && e.shiftKey)) point(linkOf(e));
    }, true);
    document.addEventListener('dragstart', (e) => {
      const page = pageOfLink(linkOf(e)?.getAttribute('href'));
      if (!page || !e.dataTransfer) return;
      const abs = new URL(page, location.origin).href;
      try { e.dataTransfer.setData('text/uri-list', abs); e.dataTransfer.setData('text/plain', abs); } catch { /* the browser's own */ }
    }, true);
    // the interface's own window.open (this world's: the page's is untouched) — "Open in a new tab" and the like
    const open = window.open;
    window.open = function (url, ...rest) {
      let to = url;
      try { to = (typeof url === 'string' || url instanceof URL) ? (pageOfLink(String(url)) ? new URL(pageOfLink(String(url)), location.origin).href : url) : url; } catch { /* as it is */ }
      return open.call(window, to, ...rest);
    };
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
    routeUrl,
    pageFor,
    pageOfLink,
    /** Brightspace's sign-in page (or its own landing that leads to it). */
    isLoginPage: () => d2l && /^\/d2l\/(login|lp\/auth\/login)/i.test(location.pathname),
  };
})();
