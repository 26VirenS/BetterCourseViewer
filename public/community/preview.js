// Simpl Community: a theme drawn on Simpl's Dashboard at the made-up school — the gallery's cards (small) and Preview
// (full size), one drawing scaled. Its colours, its drawn scenes and its ink come from the extension's own theme code
// (/js/theme.js: extension/lib/theme.js, copied in when the site is published), so a theme looks here as it will in
// Simpl: a picture inked tone on tone in its surface's colour, or — a photo with its Ink off — the photo as it is, the
// words over it dark with a white edge. SimplPreview.draw(theme, { dark, wide }) gives the drawing (`wide`: how fine the
// ink is, 260 for a gallery card); SimplPreview.fit(el) keeps it scaled to its box.
(function () {
  const W = 1200, H = 750;
  const REGULAR = '#0a84ff';
  const T = () => self.BCV && self.BCV.theme;
  const CARDS = [['today', 'Due today', '4', '35 points total'], ['tomorrow', 'Tomorrow', '3', '32 points total'], ['next', 'Next 7 days', '14', 'Across 5 courses'], ['overdue', 'Overdue', '1', '1 not submitted'], ['unread', 'Unread', '3', 'From 2 courses'], ['graded', 'Graded', '6', '79 / 109 points']];
  const COURSES = [['MATH 140: Calculus I', '#34c759'], ['PHYS 120: Mechanics', '#30b0c7'], ['PHYS 120L: Mechanics Lab', '#ff2d55'], ['SEM 101: First-Year Seminar', '#ff9500'], ['ENG 101: Composition', '#c8901c']];
  const NAV = ['Groups', 'To Do', 'Calendar', 'Grades', 'Notifications', 'Inbox', 'Tools'];
  const WORK = [['MATH 140: Calculus I', 'Assignment', 'Discussion 1', '10 pts'], ['MATH 140: Calculus I', 'Quiz', 'Quiz 1', '10 pts'], ['SEM 101: First-Year Seminar', 'Assignment', 'Week 2 reading response', '10 pts'], ['ENG 101: Composition', 'Assignment', 'Choosing a research topic', '5 pts']];
  const DEFAULT_PLACE = (key) => (key === 'side' ? { x: 50, y: 100, z: 1 } : { x: 100, y: 100, z: 1 });

  const el = (tag, cls, kids = [], style) => {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (style) Object.assign(e.style, style);
    for (const k of [].concat(kids)) if (k != null) e.append(k);
    return e;
  };

  /** The picture a place shows: its photo, or its drawing made here; where it sits; whether it is inked (a drawing
   *  always, a photo unless its Ink is off); or nothing. */
  function picture(theme, key, k) {
    const p = theme && theme.places && theme.places[key];
    if (!p) return null;
    const url = p.photo || (p.scene && T() ? T().sceneUrl(p.scene, k, key === 'side' ? 'side' : 'card') : null);
    if (!url) return null;
    const d = DEFAULT_PLACE(key);
    return { url, scene: !p.photo, ink: !p.photo || p.ink !== false, x: Number.isFinite(p.x) ? p.x : d.x, y: Number.isFinite(p.y) ? p.y : d.y, z: Number.isFinite(p.z) ? p.z : 1 };
  }
  // (the picture's point at x%, y% sits on its place's same point, zoomed z times about it — lib/theme.js placeVars)
  const place = (p) => ({ transformOrigin: `${p.x}% ${p.y}%`, transform: `scale(${p.z})` });
  const photo = (p) => {
    const img = el('img', 'sp-pic');
    img.alt = '';
    img.decoding = 'async';
    img.src = p.url;
    Object.assign(img.style, { objectPosition: `${p.x}% ${p.y}%` }, place(p));
    return img;
  };
  /** A place's picture laid into its box: inked — the picture's darkest part and its outlines (lib/theme.js inkFor) in
   *  a shade of the surface's own colour, once the ink is made — or the photo as it is, the box marked plain. A picture
   *  that will not ink is kept plain, as in Simpl. */
  function lay(box, p, { paper, accent, dark, wide }) {
    if (!p) return;
    box.classList.add('has-pic');
    const plain = () => { box.classList.remove('has-ink'); box.classList.add('is-plain'); box.prepend(photo(p)); };
    if (!p.ink || !T() || !T().inkFor) { plain(); return; }
    box.classList.add('has-ink');
    box.style.setProperty('--sp-paper', paper);
    const layer = el('i', 'sp-ink', [], { ...place(p), backgroundColor: p.scene ? T().sceneInkOn(paper, accent, dark) : T().inkOn(paper, dark) });
    box.prepend(layer);
    T().inkFor(p.url, { wide }).then((r) => {
      const mask = `url("${r.ink}") ${p.x}% ${p.y}% / cover no-repeat`;
      layer.style.webkitMask = mask;
      layer.style.mask = mask;
      layer.classList.add('is-on');
    }).catch(() => { layer.remove(); plain(); });
  }

  /** The colours the theme makes, in this look: the extension's own shades of its accent. */
  function colours(theme, dark) {
    const p = T() && T().palette(theme && theme.accent ? theme.accent : REGULAR, dark);
    if (!p) return {};
    const g = p.ground || {};
    return {
      paper: { side: g.bg, card: g.card }, accent: p.accent,
      '--sp-accent': p.accent, '--sp-icon': p.icon, '--sp-text': p.text, '--sp-fill': p.fill, '--sp-soft': p.soft,
      '--sp-bg': g.bg, '--sp-card': g.card, '--sp-ink2': g.ink2, '--sp-ink3': g.ink3, '--sp-sep': g.sep,
    };
  }

  /** The Dashboard in the theme: the sidebar with its picture, the six counters with theirs, the work coming up. */
  function draw(theme, { dark = false, wide = 800 } = {}) {
    const root = el('div', `sp ${dark ? 'sp--dark' : 'sp--light'}`);
    const { paper = {}, accent, ...vars } = colours(theme, dark);
    for (const [k, v] of Object.entries(vars)) if (v) root.style.setProperty(k, v);
    const ink = (where) => ({ paper: paper[where] || (dark ? '#1c1c1e' : '#ffffff'), accent, dark, wide });
    const side = el('aside', 'sp-side', [
      el('div', 'sp-brand', [el('b', 'sp-logo', 'S'), el('span', null, ['Simpl', el('i', null, '.')])]),
      el('div', 'sp-nav is-on', [el('i', 'sp-dot'), 'Dashboard']),
      el('div', 'sp-nav', [el('i', 'sp-dot'), 'All Courses']),
      ...COURSES.map(([n, c]) => el('div', 'sp-course', [el('span', null, n)], { color: c })),
      ...NAV.map((n) => el('div', 'sp-nav', [el('i', 'sp-dot'), n])),
      el('div', 'sp-who', [el('i', 'sp-face'), el('div', null, [el('b', null, 'Sam Student'), el('small', null, 'sstudent@lakeside.edu')])]),
    ]);
    lay(side, picture(theme, 'side', 0), ink('side'));
    // (a counter's drawing the variation Simpl draws there: its place among Simpl's counters, the site's 'next' its 'week')
    const variation = (key) => (T() && T().CARD_SLOTS ? T().CARD_SLOTS.indexOf(key === 'next' ? 'week' : key) + 1 : 1);
    const cards = el('div', 'sp-cards', CARDS.map(([key, label, n, sub]) => {
      const card = el('div', `sp-card sp-card--${key}`, [el('div', 'sp-card__in', [el('small', null, label), el('b', null, n), el('span', null, sub)])]);
      lay(card, picture(theme, key, variation(key)), ink('card'));
      return card;
    }));
    const list = el('div', 'sp-list', [
      el('div', 'sp-list__h', [el('b', null, 'Coming up'), el('span', null, 'Today · October 25')]),
      ...WORK.map(([course, kind, title, pts]) => el('div', 'sp-row', [el('i', 'sp-check'), el('div', null, [el('small', null, `${course} · ${kind}`), el('b', null, title)]), el('span', null, [pts, el('small', null, 'Due 11:59 PM')])])),
    ]);
    const main = el('main', 'sp-main', [
      el('div', 'sp-top', [el('span', 'sp-arrows', '‹ ›'), el('b', null, 'Dashboard'), el('span', 'sp-search', 'Search or type /')]),
      el('div', 'sp-hello', [el('h3', null, 'Good morning, Sam'), el('span', null, 'Sunday, October 25'), el('span', 'sp-btn', '+ New Task')]),
      cards,
      el('div', 'sp-split', [list, el('div', 'sp-grades', [el('small', null, 'Grades'), el('div', 'sp-bars', [92, 81, 0, 88, 95].map((v, i) => el('i', null, [], { height: `${v ? v * 0.9 : 6}%`, background: v ? COURSES[i][1] : 'transparent', outline: v ? '' : '1px dashed currentColor' })))])]),
    ]);
    root.append(side, main);
    return root;
  }

  /** The drawing kept scaled to its box's width (the box keeps its shape: 16 by 10). */
  function fit(box) {
    const set = () => { const s = box.clientWidth / W; box.style.setProperty('--sp-scale', String(s)); };
    set();
    if ('ResizeObserver' in self) new ResizeObserver(set).observe(box);
  }

  const css = `
  .sp-box { position: relative; aspect-ratio: ${W} / ${H}; overflow: hidden; border-radius: 12px; container-type: inline-size; }
  .sp { position: absolute; left: 0; top: 0; width: ${W}px; height: ${H}px; transform: scale(var(--sp-scale, .3)); transform-origin: 0 0; display: flex; font: 13px/1.35 -apple-system, BlinkMacSystemFont, "SF Pro Text", "Segoe UI", system-ui, sans-serif; letter-spacing: -.01em; color: var(--sp-ink, #1d1d1f); background: var(--sp-bg, #f2f2f6); text-align: left; }
  .sp--dark { --sp-ink: #f5f5f7; color: var(--sp-ink); }
  .sp b { font-weight: 600; }
  .sp-pic { position: absolute; inset: 0; width: 100%; height: 100%; object-fit: cover; pointer-events: none; }
  .sp-side { position: relative; width: 236px; flex: none; padding: 14px 12px; display: flex; flex-direction: column; gap: 2px; overflow: hidden; background: var(--sp-card, #fff); border-right: 1px solid var(--sp-sep, rgba(0,0,0,.08)); }
  .sp-side > :not(.sp-pic):not(.sp-ink) { position: relative; z-index: 1; }
  .sp-ink { position: absolute; inset: 0; z-index: 0; pointer-events: none; opacity: 0; transition: opacity .25s ease-out; }
  .sp-ink.is-on { opacity: 1; }
  .has-ink { background: var(--sp-paper); }
  .sp-brand { display: flex; align-items: center; gap: 8px; font-weight: 700; font-size: 19px; line-height: 1; margin: 2px 4px 14px; }
  .sp-brand i { color: var(--sp-accent); font-style: normal; }
  .sp-logo { display: grid; place-items: center; width: 26px; height: 26px; border-radius: 7px; background: #000; color: #fff; font-size: 16px; }
  .sp-nav { display: flex; align-items: center; gap: 10px; padding: 7px 10px; border-radius: 9px; font-weight: 500; }
  .sp-nav.is-on { background: var(--sp-soft); color: var(--sp-text); }
  .sp-dot { width: 14px; height: 14px; border-radius: 4px; border: 2px solid var(--sp-icon); box-sizing: border-box; }
  .sp-course { padding: 5px 10px 5px 34px; font-weight: 500; font-size: 12.5px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .sp-who { margin-top: auto; display: flex; align-items: center; gap: 9px; padding: 10px 6px 2px; border-top: 1px solid var(--sp-sep); }
  .sp-who small { display: block; color: var(--sp-ink3); font-size: 11px; }
  .sp-face { width: 26px; height: 26px; border-radius: 50%; background: linear-gradient(135deg, #d1d1d6, #aeaeb2); }
  .sp-main { flex: 1; min-width: 0; padding: 0 28px; display: flex; flex-direction: column; }
  .sp-top { height: 50px; display: flex; align-items: center; gap: 14px; border-bottom: 1px solid var(--sp-sep); margin: 0 -28px; padding: 0 20px; }
  .sp-arrows { color: var(--sp-ink3); letter-spacing: 6px; }
  .sp-search { margin-left: auto; width: 230px; padding: 6px 12px; border-radius: 999px; background: var(--sp-card); color: var(--sp-ink3); font-size: 12px; border: 1px solid var(--sp-sep); }
  .sp-hello { display: flex; align-items: baseline; gap: 12px; margin: 22px 0 18px; }
  .sp-hello h3 { margin: 0; font-weight: 700; font-size: 28px; line-height: 1.1; letter-spacing: -.02em; }
  .sp-hello span { color: var(--sp-ink3); }
  .sp-btn { margin-left: auto; padding: 6px 13px; border-radius: 999px; background: var(--sp-fill); color: #fff !important; font-weight: 600; font-size: 12.5px; }
  .sp-cards { display: grid; grid-template-columns: repeat(6, 1fr); gap: 12px; }
  .sp-card { position: relative; height: 104px; border-radius: 16px; overflow: hidden; background: var(--sp-card); box-shadow: 0 0 0 1px var(--sp-sep); }
  .sp-card__in { position: relative; z-index: 1; padding: 12px 14px; display: flex; flex-direction: column; height: 100%; box-sizing: border-box; }
  .sp-card__in small { font-weight: 600; color: var(--sp-icon); font-size: 12px; }
  .sp-card__in b { font-weight: 700; font-size: 30px; line-height: 1.15; margin-top: 4px; }
  .sp-card--overdue .sp-card__in b { color: #ff3b30; }
  .sp-card__in span { margin-top: auto; color: var(--sp-ink3); font-size: 11.5px; }
  .sp-split { display: grid; grid-template-columns: 1fr 300px; gap: 16px; margin-top: 18px; flex: 1; min-height: 0; }
  .sp-list, .sp-grades { background: var(--sp-card); border-radius: 16px; box-shadow: 0 0 0 1px var(--sp-sep); padding: 14px 16px; overflow: hidden; }
  .sp-list__h { display: flex; gap: 10px; align-items: baseline; margin-bottom: 6px; }
  .sp-list__h b { font-size: 16px; }
  .sp-list__h span { color: var(--sp-ink3); font-size: 12px; }
  .sp-row { display: flex; align-items: center; gap: 12px; padding: 10px 0; border-top: 1px solid var(--sp-sep); }
  .sp-row small { display: block; color: var(--sp-ink3); font-size: 11px; }
  .sp-row > span { margin-left: auto; text-align: right; font-weight: 600; font-size: 12.5px; }
  .sp-check { width: 18px; height: 18px; border-radius: 50%; border: 1.5px solid var(--sp-ink3); }
  .sp-grades small { font-weight: 600; color: var(--sp-ink3); text-transform: uppercase; font-size: 10.5px; letter-spacing: .04em; }
  .sp-bars { display: flex; align-items: flex-end; gap: 14px; height: 150px; margin-top: 18px; padding: 0 6px; }
  .sp-bars i { flex: 1; border-radius: 6px 6px 3px 3px; color: var(--sp-ink3); }
  /* a photo with its Ink off: as it is, and the words over it dark with a gentle white edge in either look
     (app.css --bcv-plain-halo, --bcv-plain-glyph) */
  .is-plain { --sp-halo: .8px 0 0 #fff, -.8px 0 0 #fff, 0 .8px 0 #fff, 0 -.8px 0 #fff, .6px .6px 0 #fff, -.6px .6px 0 #fff, .6px -.6px 0 #fff, -.6px -.6px 0 #fff, 0 0 6px rgba(255,255,255,.75); --sp-ink: #1c1c1e; --sp-ink3: rgba(28,28,30,.72); --sp-sep: rgba(0,0,0,.1); color: #1c1c1e; }
  .is-plain :is(b, small, span, .sp-nav, .sp-course) { text-shadow: var(--sp-halo); }
  .is-plain .sp-card__in small { color: #1c1c1e; }
  .is-plain :is(.sp-dot, .sp-logo, .sp-face) { filter: drop-shadow(0 0 .7px #fff) drop-shadow(0 0 .7px #fff) drop-shadow(0 0 2px rgba(255,255,255,.7)); }
  .is-plain .sp-nav.is-on { background: rgba(255,255,255,.62); color: #1c1c1e; }
  .is-plain .sp-who { border-top-color: rgba(255,255,255,.7); }
  `;
  const style = document.createElement('style');
  style.textContent = css;
  document.head.append(style);

  self.SimplPreview = { draw, fit, W, H };
})();
