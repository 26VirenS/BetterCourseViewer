/* The file viewer's own pages (2.98.99). A PDF is drawn here with pdf.js, page by page as it comes
 * near the view (sharp at any zoom and screen density; a page far off lets its picture go), with a
 * bar over it: the page on show as a number to type over (Enter goes there), ‹ ›, zoom − / + and a
 * menu (fit the width, fit the page, 50 % … 400 %), turn the pages a quarter, and find — every
 * match lit on its page, the one chosen brighter, ↑ ↓ between them. ⌘/Ctrl and the wheel, a
 * trackpad's pinch, two fingers, + − 0, ← → Home End and ⌘/Ctrl-F do what they do anywhere. The
 * words can be selected and copied and the links work; nothing can be changed. A Word document
 * comes as the PDF Simpl makes of it on this device (office.js), a picture gets the same zoom, turn
 * and drag, a text file a size to read it at. Loaded on demand (lazy-modules.js: docview), the
 * first time such a file is opened in the viewer (viewer.js). */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;

  const PT = 96 / 72; // CSS pixels per PDF point: 100 % is the page at its printed size
  const STEPS = [0.25, 0.33, 0.5, 0.67, 0.75, 0.8, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2, 2.5, 3, 4, 5]; // − and + walk these
  const MENU = [0.5, 0.75, 1, 1.25, 1.5, 2, 3, 4]; // and the menu offers these
  const MIN = 0.25, MAX = 5;
  const PAD = 20; // round the pages (the CSS's padding)
  const MAX_PX = 4096 * 4096; // one page's picture at most: a huge page zoomed far is drawn a little softer, never out of memory
  const LONG = 40; // past this many pages, a page far from the view lets its words go too
  const GLYPH = {
    prev: 'M15 5l-7 7 7 7', next: 'M9 5l7 7-7 7', minus: 'M6 12h12', plus: 'M12 6v12M6 12h12',
    rotate: 'M19.5 12a7.5 7.5 0 1 1-2.2-5.3M19.5 4.5v4.5H15', up: 'M6 15l6-6 6 6', down: 'M6 9l6 6 6-6',
  };
  const btn = (glyph, title, onClick, cls = '') => h('button', { type: 'button', class: `bcv-dv__btn ${cls}`, title, 'aria-label': title, onclick: onClick }, U.svg(glyph, { size: 15, stroke: 'currentColor', width: 2 }));
  const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
  const pctOf = (z) => `${Math.round(z * 100)}%`;

  /** The zoom menu: the fits, the usual sizes, and the size on show (a size no item names gets an item of its own). */
  function zoomMenu(fits, onPick) {
    const sel = h('select', { class: 'bcv-dv__zoom', 'aria-label': 'Zoom', title: 'Zoom' });
    sel.addEventListener('change', () => { onPick(sel.value); });
    return {
      el: sel,
      paint(z, fit) {
        const opts = [h('option', { value: 'now', text: pctOf(z), hidden: true })];
        for (const [v, label] of fits) opts.push(h('option', { value: v, text: label }));
        for (const m of MENU) opts.push(h('option', { value: String(m), text: pctOf(m) }));
        sel.replaceChildren(...opts);
        sel.value = 'now'; // (what is on show reads as its size; a fit chosen is a size like any other once it lands)
        sel.dataset.fit = fit || '';
      },
    };
  }

  /** ⌘/Ctrl and the wheel, a trackpad's pinch (Safari's gesture events), two fingers on a screen: each
   *  asks `zoomAt(scale, point)` with a scale from where it started, once a frame. */
  function gestures(scroll, getScale, zoomAt) {
    let want = null, raf = 0, gest = false, g0 = 1;
    const at = (x, y) => { const r = scroll.getBoundingClientRect(); return { x: x - r.left, y: y - r.top }; };
    const ask = (s, p) => {
      want = { s, p };
      if (raf) return;
      raf = requestAnimationFrame(() => { raf = 0; const w = want; want = null; if (w) zoomAt(w.s, w.p); });
    };
    scroll.addEventListener('wheel', (e) => {
      if (!(e.ctrlKey || e.metaKey) || gest) return;
      e.preventDefault();
      const f = clamp(Math.exp(-e.deltaY * (e.deltaMode === 1 ? 0.05 : 0.01)), 0.8, 1.25);
      ask((want ? want.s : getScale()) * f, at(e.clientX, e.clientY));
    }, { passive: false });
    scroll.addEventListener('gesturestart', (e) => { e.preventDefault(); gest = true; g0 = getScale(); });
    scroll.addEventListener('gesturechange', (e) => { e.preventDefault(); ask(g0 * e.scale, at(e.clientX, e.clientY)); });
    scroll.addEventListener('gestureend', (e) => { e.preventDefault(); gest = false; });
    const touches = new Map();
    let pinch = null;
    const spread = () => { const [a, b] = [...touches.values()]; return { d: Math.hypot(a.x - b.x, a.y - b.y), x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 }; };
    scroll.addEventListener('pointerdown', (e) => {
      if (e.pointerType !== 'touch') return;
      touches.set(e.pointerId, { x: e.clientX, y: e.clientY });
      if (touches.size === 2) { const s = spread(); pinch = { d0: Math.max(1, s.d), s0: getScale() }; }
    });
    scroll.addEventListener('pointermove', (e) => {
      if (!touches.has(e.pointerId)) return;
      touches.set(e.pointerId, { x: e.clientX, y: e.clientY });
      if (pinch && touches.size === 2) { const s = spread(); ask(pinch.s0 * (s.d / pinch.d0), at(s.x, s.y)); }
    }, { passive: true });
    const lift = (e) => { touches.delete(e.pointerId); if (touches.size < 2) pinch = null; };
    scroll.addEventListener('pointerup', lift);
    scroll.addEventListener('pointercancel', lift);
    return () => { if (raf) cancelAnimationFrame(raf); };
  }

  /** The keys the viewer answers, on the sheet (`keys`), ahead of the sheet's own (Escape closes it): handlers by
   *  what was pressed. A key in a field is the field's; Escape while a find is up empties it first. */
  function keyboard(keys, on) {
    const onKey = (e) => {
      if (e.key === 'Escape' && on.escape?.()) { e.preventDefault(); e.stopPropagation(); return; }
      const t = e.target;
      const field = !!t?.closest?.('input, select, textarea');
      const mod = e.metaKey || e.ctrlKey;
      if (mod && !e.altKey && (e.key === 'f' || e.key === 'F') && on.find) { e.preventDefault(); e.stopPropagation(); on.find(); return; }
      if (mod && !e.altKey && (e.key === '=' || e.key === '+')) { e.preventDefault(); on.zoom(1); return; }
      if (mod && !e.altKey && e.key === '-') { e.preventDefault(); on.zoom(-1); return; }
      if (mod && !e.altKey && e.key === '0') { e.preventDefault(); on.fit(); return; }
      if (field || mod || e.altKey) return;
      if (e.key === '+' || e.key === '=') { e.preventDefault(); on.zoom(1); } else if (e.key === '-') { e.preventDefault(); on.zoom(-1); } else if (on.page && e.key === 'ArrowRight' && !on.sideways()) { e.preventDefault(); on.page(1); } else if (on.page && e.key === 'ArrowLeft' && !on.sideways()) { e.preventDefault(); on.page(-1); } else if (on.page && e.key === 'Home') { e.preventDefault(); on.page(-Infinity); } else if (on.page && e.key === 'End') { e.preventDefault(); on.page(Infinity); }
    };
    keys?.addEventListener('keydown', onKey, true);
    return () => keys?.removeEventListener('keydown', onKey, true);
  }

  // ---- a PDF ------------------------------------------------------------------------------------
  /** Draws the PDF `data` (its bytes) into `host`. `keys`: the element whose keys it answers (the
   *  sheet); `note`: a word on where the pages came from. Resolves to { destroy, state } once the
   *  document is open (its pages are drawn as they come into view); rejects if pdf.js cannot read it. */
  async function pdf(host, data, { keys = null, note = '' } = {}) {
    await BCV.tools.vendor('pdf');
    const doc = await self.pdfjsLib.getDocument({ data, isEvalSupported: false }).promise;
    let loaded;
    try {
      loaded = await Promise.all(Array.from({ length: doc.numPages }, (_, i) => doc.getPage(i + 1)));
    } catch (e) { try { doc.destroy(); } catch { /* gone */ } throw e; }
    const N = loaded.length;
    const st = {
      pages: loaded.map((page, i) => { const vp = page.getViewport({ scale: 1 }); return { n: i + 1, page, w: vp.width, h: vp.height, el: null, cv: null, text: null, links: null, drawn: 0, task: null }; }),
      scale: PT, fit: 'width', rot: 0, cur: 1, gen: 1, q: '', hits: [], hit: -1, near: new Set(), gone: false, lock: 0,
    };

    // ---- the pages, laid out whole before any is drawn
    const pagesEl = U.el('bcv-dv__pages');
    const scroll = h('div', { class: 'bcv-dv__scroll', tabindex: '0', role: 'document', 'aria-label': `${N === 1 ? '1 page' : `${N} pages`}` }, pagesEl);
    for (const pg of st.pages) {
      pg.cv = h('canvas', { class: 'bcv-dv__canvas', 'aria-hidden': 'true' });
      pg.text = h('div', { class: 'bcv-dv__text' });
      pg.links = h('div', { class: 'bcv-dv__links' });
      pg.el = h('div', { class: 'bcv-dv__page', dataset: { page: String(pg.n) }, 'aria-label': `Page ${pg.n}` }, [pg.cv, pg.text, pg.links]);
      pagesEl.append(pg.el);
    }

    // ---- the bar
    const pageIn = h('input', { class: 'bcv-dv__pagein', type: 'text', inputmode: 'numeric', value: '1', autocomplete: 'off', spellcheck: 'false', 'aria-label': `Page number, 1 to ${N}`, title: 'Type a page number and press Enter' });
    const prevB = btn(GLYPH.prev, 'Previous page', () => goPage(st.cur - 1));
    const nextB = btn(GLYPH.next, 'Next page', () => goPage(st.cur + 1));
    const outB = btn(GLYPH.minus, 'Zoom out', () => step(-1));
    const inB = btn(GLYPH.plus, 'Zoom in', () => step(1));
    const menu = zoomMenu([['width', 'Fit width'], ['page', 'Fit page']], (v) => {
      if (v === 'width' || v === 'page') zoomTo(fitScale(v), null, v);
      else if (v !== 'now') zoomTo(Number(v) * PT);
      scroll.focus({ preventScroll: true });
    });
    const rotB = btn(GLYPH.rotate, 'Rotate', () => rotate());
    const findIn = h('input', { class: 'bcv-dv__findin', type: 'search', placeholder: 'Find', autocomplete: 'off', spellcheck: 'false', 'aria-label': 'Find in document' });
    const findN = h('span', { class: 'bcv-dv__findn', 'aria-live': 'polite' });
    const upB = btn(GLYPH.up, 'Previous match', () => goHit(st.hit - 1));
    const downB = btn(GLYPH.down, 'Next match', () => goHit(st.hit + 1));
    const bar = h('div', { class: 'bcv-dv__bar', role: 'toolbar', 'aria-label': 'Pages' }, [
      U.el('bcv-dv__grp bcv-dv__nav', [prevB, pageIn, h('span', { class: 'bcv-dv__of', text: `of ${N}` }), nextB]),
      U.el('bcv-dv__grp bcv-dv__zoomgrp', [outB, menu.el, inB, h('span', { class: 'bcv-dv__sep', 'aria-hidden': 'true' }), rotB]),
      note ? U.text('bcv-dv__note', note, 'span') : null,
      U.el('bcv-dv__grp bcv-dv__find', [h('span', { class: 'bcv-dv__findic', 'aria-hidden': 'true' }, U.svg(IC.search, { size: 13, stroke: 'currentColor', width: 2 })), findIn, findN, upB, downB]),
    ]);
    const wrap = U.el('bcv-dv', [bar, scroll]);
    host.replaceChildren(wrap);

    // ---- sizes
    const dims = (pg) => (st.rot % 180 ? { w: pg.h, h: pg.w } : { w: pg.w, h: pg.h });
    const clampS = (s) => clamp(s, MIN * PT, MAX * PT);
    function fitScale(mode) {
      const W = Math.max(120, scroll.clientWidth - PAD * 2), H = Math.max(120, scroll.clientHeight - PAD * 2);
      let wide = 0;
      for (const pg of st.pages) wide = Math.max(wide, dims(pg).w);
      const d = dims(st.pages[st.cur - 1] || st.pages[0]);
      return clampS(mode === 'page' ? Math.min(W / d.w, H / d.h) : W / wide);
    }
    function sizePages() {
      pagesEl.style.setProperty('--scale-factor', String(st.scale));
      for (const pg of st.pages) {
        const d = dims(pg);
        pg.el.style.width = `${Math.floor(d.w * st.scale)}px`;
        pg.el.style.height = `${Math.floor(d.h * st.scale)}px`;
      }
    }
    /** The page under a point of the view (from its top-left), and where on that page, as a fraction of it. */
    function pageAtY(y) {
      let lo = 0, hi = N - 1, best = 0;
      while (lo <= hi) { const mid = (lo + hi) >> 1; if (st.pages[mid].el.offsetTop <= y) { best = mid; lo = mid + 1; } else hi = mid - 1; }
      return st.pages[best];
    }
    function pointAt(ax, ay) {
      const x = scroll.scrollLeft + ax, y = scroll.scrollTop + ay;
      const pg = pageAtY(y), el = pg.el;
      return { pg, fx: (x - el.offsetLeft) / (el.offsetWidth || 1), fy: (y - el.offsetTop) / (el.offsetHeight || 1) };
    }
    /** A new zoom, the point under `anchor` (the view's middle, or its top for a fit) kept where it is; the pictures follow when the zoom rests. */
    function zoomTo(s, anchor = null, fit = null) {
      s = clampS(s);
      st.fit = fit;
      if (Math.abs(s - st.scale) > 1e-4) {
        const ax = anchor ? anchor.x : scroll.clientWidth / 2, ay = anchor ? anchor.y : (fit ? 0 : scroll.clientHeight / 2);
        const at = pointAt(ax, ay);
        st.scale = s;
        st.gen += 1;
        sizePages();
        const el = at.pg.el;
        scroll.scrollLeft = el.offsetLeft + at.fx * el.offsetWidth - ax;
        scroll.scrollTop = el.offsetTop + at.fy * el.offsetHeight - ay;
        redrawSoon();
      }
      paintZoom();
    }
    function step(dir) {
      const z = st.scale / PT;
      const next = dir > 0 ? STEPS.find((v) => v > z + 0.005) : [...STEPS].reverse().find((v) => v < z - 0.005);
      if (next) zoomTo(next * PT);
    }
    function paintZoom() {
      const z = st.scale / PT;
      menu.paint(z, st.fit);
      outB.disabled = z <= MIN + 0.001;
      inB.disabled = z >= MAX - 0.001;
      wrap.dataset.zoom = String(Math.round(z * 100));
    }

    // ---- drawing, as the pages come near
    let redrawT = 0;
    function redrawSoon() { clearTimeout(redrawT); redrawT = setTimeout(() => { for (const pg of st.near) draw(pg); }, 140); }
    async function draw(pg) {
      if (st.gone || pg.drawn === st.gen) return;
      const gen = st.gen;
      pg.drawn = gen;
      const vp = pg.page.getViewport({ scale: st.scale, rotation: (pg.page.rotate + st.rot) % 360 });
      let dpr = U.dpr();
      if (vp.width * vp.height * dpr * dpr > MAX_PX) dpr = Math.sqrt(MAX_PX / (vp.width * vp.height));
      const cv = h('canvas', { class: 'bcv-dv__canvas', 'aria-hidden': 'true' });
      cv.width = Math.max(1, Math.floor(vp.width * dpr));
      cv.height = Math.max(1, Math.floor(vp.height * dpr));
      if (pg.task) { try { pg.task.cancel(); } catch { /* done */ } }
      const task = pg.page.render({ canvasContext: cv.getContext('2d', { alpha: false }), viewport: vp, transform: dpr !== 1 ? [dpr, 0, 0, dpr, 0, 0] : null });
      pg.task = task;
      try {
        await task.promise;
      } catch {
        if (pg.task === task) pg.task = null;
        if (pg.drawn === gen) pg.drawn = 0; // (stopped: drawn again when it is near)
        cv.width = 0;
        return;
      }
      if (pg.task === task) pg.task = null;
      if (st.gone || pg.drawn !== gen) { cv.width = 0; return; }
      // the new picture takes the old one's place only once it is whole: until then the old one, stretched, stands in
      const old = pg.cv;
      pg.cv = cv;
      old.replaceWith(cv);
      old.width = 0;
      old.height = 0;
      ensureText(pg);
      ensureLinks(pg);
    }
    function release(pg) {
      if (pg.task) { try { pg.task.cancel(); } catch { /* done */ } pg.task = null; }
      pg.drawn = 0;
      pg.cv.width = 0;
      pg.cv.height = 0;
      if (N > LONG && !st.hits.some((x) => x.pg === pg && st.hits[st.hit] === x)) { pg.text.replaceChildren(); pg.textKey = null; pg.divs = null; pg.lit = []; }
    }
    const io = new IntersectionObserver((entries) => {
      for (const en of entries) {
        const pg = st.pages[Number(en.target.dataset.page) - 1];
        if (!pg) continue;
        if (en.isIntersecting) { st.near.add(pg); draw(pg); } else { st.near.delete(pg); release(pg); }
      }
    }, { root: scroll, rootMargin: '120% 0px' });

    // ---- the words over each page: selectable, and where find lights its matches
    const words = (pg) => pg.page.getTextContent();
    async function ensureText(pg) {
      const key = st.rot;
      if (pg.textKey === key) return;
      pg.textKey = key;
      if (!pg.tc) pg.tc = words(pg);
      let tc;
      try { tc = await pg.tc; } catch { return; }
      if (st.gone || pg.textKey !== key) return;
      const vp = pg.page.getViewport({ scale: st.scale, rotation: (pg.page.rotate + st.rot) % 360 });
      pg.text.replaceChildren();
      const divs = [], strs = [];
      try {
        await self.pdfjsLib.renderTextLayer({ textContentSource: tc, container: pg.text, viewport: vp, textDivs: divs, textDivProperties: new WeakMap(), textContentItemsStr: strs }).promise;
      } catch { return; }
      if (st.gone || pg.textKey !== key) return;
      pg.divs = divs;
      pg.strs = strs;
      pg.lit = [];
      paintHits(pg);
    }
    async function ensureLinks(pg) {
      if (pg.linksKey === st.rot) return;
      const key = st.rot;
      pg.linksKey = key;
      if (!pg.anns) pg.anns = pg.page.getAnnotations({ intent: 'display' }).catch(() => []);
      const anns = await pg.anns;
      if (st.gone || pg.linksKey !== key) return;
      const vp = pg.page.getViewport({ scale: 1, rotation: (pg.page.rotate + st.rot) % 360 });
      const out = [];
      for (const a of anns || []) {
        if (a.subtype !== 'Link' || !a.rect) continue;
        const url = typeof a.url === 'string' && /^(https?:|mailto:)/i.test(a.url) ? a.url : null;
        if (!url && !a.dest) continue;
        const [x1, y1, x2, y2] = vp.convertToViewportRectangle(a.rect);
        const style = { left: `${(Math.min(x1, x2) / vp.width) * 100}%`, top: `${(Math.min(y1, y2) / vp.height) * 100}%`, width: `${(Math.abs(x2 - x1) / vp.width) * 100}%`, height: `${(Math.abs(y2 - y1) / vp.height) * 100}%` };
        out.push(url
          ? h('a', { class: 'bcv-dv__link', href: url, target: '_blank', rel: 'noopener noreferrer', title: url, style })
          : h('a', { class: 'bcv-dv__link', href: '#', title: 'Go to the page it points at', style, onclick: (e) => { e.preventDefault(); goDest(a.dest); } }));
      }
      pg.links.replaceChildren(...out);
    }
    async function goDest(dest) {
      try {
        const d = typeof dest === 'string' ? await doc.getDestination(dest) : dest;
        const ref = d?.[0];
        if (ref == null) return;
        goPage((typeof ref === 'object' ? await doc.getPageIndex(ref) : ref) + 1);
      } catch { /* a link to nowhere */ }
    }

    // ---- the page on show, and going to one
    function setCur(n) {
      st.cur = n;
      if (document.activeElement !== pageIn) pageIn.value = String(n);
      prevB.disabled = n <= 1;
      nextB.disabled = n >= N;
      wrap.dataset.page = String(n);
    }
    function track() {
      if (st.lock) { setCur(st.lock); st.lock = 0; return; }
      const atEnd = scroll.scrollTop + scroll.clientHeight >= scroll.scrollHeight - 2 && scroll.scrollTop > 0;
      setCur(atEnd ? N : pageAtY(scroll.scrollTop + 16).n);
    }
    let scrollRaf = 0;
    scroll.addEventListener('scroll', () => { if (scrollRaf) return; scrollRaf = requestAnimationFrame(() => { scrollRaf = 0; track(); }); }, { passive: true });
    function goPage(n) {
      const to = clamp(Math.round(n), 1, N);
      if (!Number.isFinite(to)) return;
      const pg = st.pages[to - 1];
      const y = Math.max(0, pg.el.offsetTop - 8);
      st.lock = Math.abs(scroll.scrollTop - y) > 1 ? to : 0;
      scroll.scrollTop = y;
      setCur(to);
    }
    const typedPage = () => {
      const n = parseInt(pageIn.value.replace(/[^\d]/g, ''), 10);
      if (Number.isFinite(n)) goPage(n); else pageIn.value = String(st.cur);
    };
    pageIn.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') { e.preventDefault(); typedPage(); pageIn.select(); } else if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); pageIn.value = String(st.cur); scroll.focus({ preventScroll: true }); } else if (e.key === 'ArrowUp' || e.key === 'ArrowDown') { e.preventDefault(); goPage(st.cur + (e.key === 'ArrowUp' ? -1 : 1)); pageIn.value = String(st.cur); pageIn.select(); }
    });
    pageIn.addEventListener('focus', () => pageIn.select());
    pageIn.addEventListener('blur', () => { if (pageIn.value !== String(st.cur)) typedPage(); });

    // ---- turning the pages
    function rotate() {
      const top = st.cur;
      st.rot = (st.rot + 90) % 360;
      st.gen += 1;
      for (const pg of st.pages) {
        if (pg.task) { try { pg.task.cancel(); } catch { /* done */ } pg.task = null; }
        pg.drawn = 0;
        pg.cv.width = 0; // (a picture drawn the other way round would stand in stretched: white until it is drawn again)
        pg.text.replaceChildren(); pg.textKey = null; pg.divs = null; pg.lit = [];
        pg.links.replaceChildren(); pg.linksKey = null;
      }
      if (st.fit) st.scale = fitScale(st.fit);
      sizePages();
      goPage(top);
      paintZoom();
      wrap.dataset.rot = String(st.rot);
      redrawSoon();
    }

    // ---- find
    async function wordsOf(pg) {
      if (pg.words) return pg.words;
      if (!pg.tc) pg.tc = words(pg);
      let tc;
      try { tc = await pg.tc; } catch { tc = { items: [] }; }
      let str = '';
      const starts = [], lens = [];
      for (const it of tc.items) {
        if (it.str === undefined) continue;
        starts.push(str.length);
        lens.push(it.str.length);
        str += it.str;
        if (it.hasEOL) str += ' ';
      }
      pg.words = { low: Array.from(str, (c) => { const l = c.toLowerCase(); return l.length === c.length ? l : c; }).join(''), starts, lens };
      return pg.words;
    }
    function paintFind() {
      findN.textContent = !st.q ? '' : st.busy ? 'Finding…' : !st.hits.length ? 'No matches' : `${st.hit + 1} of ${st.hits.length}`;
      upB.disabled = downB.disabled = st.hits.length < 2;
      wrap.classList.toggle('is-finding', !!st.q);
      findIn.classList.toggle('is-none', !!st.q && !st.busy && !st.hits.length);
    }
    /** The matches on a page lit in its words (the chosen one brighter); what was lit before is put back first. */
    function paintHits(pg) {
      if (!pg.divs || !pg.strs) return;
      for (const i of pg.lit || []) { const d = pg.divs[i]; if (d) d.textContent = pg.strs[i]; }
      pg.lit = [];
      if (!pg.words) return;
      const { starts, lens } = pg.words;
      const per = new Map();
      st.hits.forEach((x, k) => {
        if (x.pg !== pg) return;
        let lo = 0, hi = starts.length - 1, i = 0;
        while (lo <= hi) { const mid = (lo + hi) >> 1; if (starts[mid] <= x.s) { i = mid; lo = mid + 1; } else hi = mid - 1; }
        for (; i < starts.length && starts[i] < x.e; i++) {
          const a = starts[i], b = a + lens[i];
          const from = Math.max(x.s, a) - a, to = Math.min(x.e, b) - a;
          if (to <= from) continue;
          if (!per.has(i)) per.set(i, []);
          per.get(i).push([from, to, k === st.hit]);
        }
      });
      for (const [i, ranges] of per) {
        const d = pg.divs[i], s = pg.strs[i];
        if (!d || typeof s !== 'string') continue;
        ranges.sort((p, q) => p[0] - q[0]);
        const parts = [];
        let at = 0;
        for (const [from, to, cur] of ranges) {
          if (from < at) continue;
          if (from > at) parts.push(s.slice(at, from));
          parts.push(h('mark', { class: `bcv-dv__hit${cur ? ' is-cur' : ''}`, text: s.slice(from, to) }));
          at = to;
        }
        if (at < s.length) parts.push(s.slice(at));
        d.replaceChildren(...parts);
        pg.lit.push(i);
      }
    }
    let findSeq = 0, findT = 0;
    async function search(q) {
      const my = ++findSeq;
      const before = new Set(st.hits.map((x) => x.pg));
      st.q = q.trim().toLowerCase();
      st.hits = [];
      st.hit = -1;
      for (const pg of before) paintHits(pg);
      if (!st.q) { st.busy = false; paintFind(); return; }
      st.busy = true;
      paintFind();
      const hits = [];
      for (const pg of st.pages) {
        const w = await wordsOf(pg);
        if (my !== findSeq || st.gone) return;
        let i = 0;
        while ((i = w.low.indexOf(st.q, i)) !== -1) { hits.push({ pg, s: i, e: i + st.q.length }); i += st.q.length; }
      }
      st.busy = false;
      st.hits = hits;
      const from = hits.findIndex((x) => x.pg.n >= st.cur);
      if (hits.length) await goHit(from >= 0 ? from : 0);
      else paintFind();
    }
    async function goHit(k) {
      if (!st.hits.length) return;
      const was = st.hits[st.hit];
      st.hit = ((k % st.hits.length) + st.hits.length) % st.hits.length;
      const x = st.hits[st.hit];
      paintFind();
      if (was && was.pg !== x.pg) paintHits(was.pg);
      // the page into view, its words laid over it, and the match itself into the middle of the view
      const r0 = x.pg.el.offsetTop, r1 = r0 + x.pg.el.offsetHeight;
      if (r1 < scroll.scrollTop || r0 > scroll.scrollTop + scroll.clientHeight) goPage(x.pg.n);
      await ensureText(x.pg);
      if (st.gone || st.hits[st.hit] !== x) return;
      paintHits(x.pg);
      const m = x.pg.text.querySelector('mark.is-cur');
      if (!m) return;
      const mr = m.getBoundingClientRect(), sr = scroll.getBoundingClientRect();
      scroll.scrollTop += (mr.top + mr.height / 2) - (sr.top + scroll.clientHeight / 2);
      if (mr.left < sr.left || mr.right > sr.left + scroll.clientWidth) scroll.scrollLeft += (mr.left + mr.width / 2) - (sr.left + scroll.clientWidth / 2);
    }
    findIn.addEventListener('input', () => { clearTimeout(findT); findT = setTimeout(() => search(findIn.value), 180); });
    findIn.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') {
        e.preventDefault();
        clearTimeout(findT);
        if (findIn.value.trim().toLowerCase() !== st.q) search(findIn.value);
        else goHit(st.hit + (e.shiftKey ? -1 : 1));
      }
    });
    /** Escape while a find is up: the field and its marks emptied, the viewer left open (the next Escape closes it). */
    function endFind() {
      if (!st.q && !findIn.value) return false;
      clearTimeout(findT);
      findIn.value = '';
      search('');
      scroll.focus({ preventScroll: true });
      return true;
    }

    // ---- keys, gestures, a new size
    const offKeys = keyboard(keys, {
      zoom: (d) => step(d),
      fit: () => zoomTo(fitScale('width'), null, 'width'),
      page: (d) => goPage(d === Infinity ? N : d === -Infinity ? 1 : st.cur + d),
      sideways: () => scroll.scrollWidth > scroll.clientWidth + 1,
      find: () => { findIn.focus(); findIn.select(); },
      escape: endFind,
    });
    const offGest = gestures(scroll, () => st.scale, (s, p) => zoomTo(s, p));
    const ro = new ResizeObserver(() => { if (!st.gone && st.fit) zoomTo(fitScale(st.fit), { x: 0, y: 0 }, st.fit); });
    const offDpr = U.onDprChange(() => { if (st.gone) return; st.gen += 1; redrawSoon(); });

    // ---- open: fit the width, the first page at the top
    st.scale = fitScale('width');
    sizePages();
    paintZoom();
    setCur(1);
    for (const pg of st.pages) io.observe(pg.el);
    ro.observe(scroll);
    scroll.focus({ preventScroll: true });

    function destroy() {
      if (st.gone) return;
      st.gone = true;
      clearTimeout(redrawT); clearTimeout(findT);
      io.disconnect(); ro.disconnect(); offDpr(); offKeys(); offGest();
      for (const pg of st.pages) { if (pg.task) { try { pg.task.cancel(); } catch { /* done */ } } pg.cv.width = 0; pg.cv.height = 0; }
      try { doc.destroy(); } catch { /* gone */ }
    }
    return { destroy, state: st, goPage, zoomTo, rotate, search };
  }

  // ---- a picture --------------------------------------------------------------------------------
  /** Shows the picture at `src` in `host`, fitted, with the same zoom, turn and a drag to move about.
   *  Rejects if it cannot be shown. */
  async function image(host, src, { keys = null, name = '' } = {}) {
    const img = h('img', { class: 'bcv-viewer__img bcv-dv__img', alt: name, draggable: 'false' });
    await new Promise((resolve, reject) => {
      img.onload = resolve;
      img.onerror = () => reject(new Error('The image could not be shown.'));
      img.src = src;
    });
    const nw = img.naturalWidth || 1, nh = img.naturalHeight || 1;
    const st = { s: 1, fit: true, rot: 0, gone: false };
    const box = U.el('bcv-dv__imgbox', img);
    const stage = U.el('bcv-dv__stage', box);
    const scroll = h('div', { class: 'bcv-dv__scroll bcv-dv__scroll--img', tabindex: '0', role: 'img', 'aria-label': name || 'Picture' }, stage);
    const outB = btn(GLYPH.minus, 'Zoom out', () => step(-1));
    const inB = btn(GLYPH.plus, 'Zoom in', () => step(1));
    const menu = zoomMenu([['fit', 'Fit']], (v) => { if (v === 'fit') fit(); else if (v !== 'now') zoomTo(Number(v)); scroll.focus({ preventScroll: true }); });
    const rotB = btn(GLYPH.rotate, 'Rotate', () => { st.rot = (st.rot + 90) % 360; if (st.fit) st.s = fitS(); size(); paint(); });
    const bar = h('div', { class: 'bcv-dv__bar', role: 'toolbar', 'aria-label': 'Picture' }, [
      U.el('bcv-dv__grp bcv-dv__zoomgrp', [outB, menu.el, inB, h('span', { class: 'bcv-dv__sep', 'aria-hidden': 'true' }), rotB]),
      U.text('bcv-dv__note', `${nw} × ${nh}`, 'span'),
    ]);
    const wrap = U.el('bcv-dv bcv-dv--img', [bar, scroll]);
    host.replaceChildren(wrap);

    const dims = () => (st.rot % 180 ? { w: nh, h: nw } : { w: nw, h: nh });
    const fitS = () => { const d = dims(); return clamp(Math.min(1, (scroll.clientWidth - 32) / d.w, (scroll.clientHeight - 32) / d.h), 0.02, MAX); };
    function size() {
      const d = dims();
      box.style.width = `${Math.max(1, Math.round(d.w * st.s))}px`;
      box.style.height = `${Math.max(1, Math.round(d.h * st.s))}px`;
      img.style.width = `${Math.max(1, Math.round(nw * st.s))}px`;
      img.style.height = `${Math.max(1, Math.round(nh * st.s))}px`;
      img.style.transform = `translate(-50%, -50%) rotate(${st.rot}deg)`;
      scroll.classList.toggle('is-pan', scroll.scrollWidth > scroll.clientWidth + 1 || scroll.scrollHeight > scroll.clientHeight + 1);
    }
    function paint() {
      menu.paint(st.s, st.fit ? 'fit' : '');
      outB.disabled = st.s <= Math.min(MIN, fitS()) + 0.001;
      inB.disabled = st.s >= MAX - 0.001;
      wrap.dataset.zoom = String(Math.round(st.s * 100));
      wrap.dataset.rot = String(st.rot);
    }
    function zoomTo(s, anchor = null, asFit = false) {
      s = clamp(s, Math.min(MIN, fitS()), MAX);
      const ax = anchor ? anchor.x : scroll.clientWidth / 2, ay = anchor ? anchor.y : scroll.clientHeight / 2;
      const fx = (scroll.scrollLeft + ax - box.offsetLeft) / (box.offsetWidth || 1), fy = (scroll.scrollTop + ay - box.offsetTop) / (box.offsetHeight || 1);
      st.s = s;
      st.fit = asFit;
      size();
      scroll.scrollLeft = box.offsetLeft + fx * box.offsetWidth - ax;
      scroll.scrollTop = box.offsetTop + fy * box.offsetHeight - ay;
      paint();
    }
    const fit = () => zoomTo(fitS(), null, true);
    function step(dir) {
      const next = dir > 0 ? STEPS.find((v) => v > st.s + 0.005) : [...STEPS].reverse().find((v) => v < st.s - 0.005);
      if (next) zoomTo(next); else if (dir < 0) fit();
    }
    // a drag moves the picture about when it is larger than the view; a double click goes to its real size there and back
    let drag = null;
    scroll.addEventListener('pointerdown', (e) => {
      if (e.pointerType !== 'mouse' || e.button !== 0 || !scroll.classList.contains('is-pan')) return;
      drag = { x: e.clientX, y: e.clientY, l: scroll.scrollLeft, t: scroll.scrollTop };
      scroll.setPointerCapture(e.pointerId);
      scroll.classList.add('is-drag');
      e.preventDefault();
    });
    scroll.addEventListener('pointermove', (e) => { if (!drag) return; scroll.scrollLeft = drag.l - (e.clientX - drag.x); scroll.scrollTop = drag.t - (e.clientY - drag.y); });
    const endDrag = () => { drag = null; scroll.classList.remove('is-drag'); };
    scroll.addEventListener('pointerup', endDrag);
    scroll.addEventListener('pointercancel', endDrag);
    scroll.addEventListener('dblclick', (e) => {
      const r = scroll.getBoundingClientRect();
      if (st.fit) zoomTo(fitS() < 1 ? 1 : Math.min(MAX, fitS() * 2), { x: e.clientX - r.left, y: e.clientY - r.top });
      else fit();
    });
    const offKeys = keyboard(keys, { zoom: (d) => step(d), fit });
    const offGest = gestures(scroll, () => st.s, (s, p) => zoomTo(s, p));
    const ro = new ResizeObserver(() => { if (st.gone) return; if (st.fit) fit(); else size(); });
    st.s = fitS();
    size();
    paint();
    ro.observe(scroll);
    scroll.focus({ preventScroll: true });
    return {
      state: st, zoomTo, fit,
      destroy() { if (st.gone) return; st.gone = true; ro.disconnect(); offKeys(); offGest(); },
    };
  }

  // ---- a text file ------------------------------------------------------------------------------
  /** Shows `text` in `host` with a size to read it at (− / + and the keys, kept for the next file). */
  function text(host, str, { keys = null } = {}) {
    const SIZES = [11, 12, 13, 14, 15, 16, 18, 20, 22, 24];
    let size = 13;
    try { size = clamp(Number(localStorage.getItem('bcv:textSize')) || 13, 11, 24); } catch { /* the usual */ }
    const pre = h('pre', { class: 'bcv-viewer__text bcv-dv__textfile', tabindex: '0', text: str });
    const label = U.text('bcv-dv__size', '', 'span');
    const set = (n) => {
      size = clamp(n, 11, 24);
      pre.style.fontSize = `${size}px`;
      label.textContent = `${size} pt`;
      outB.disabled = size <= 11;
      inB.disabled = size >= 24;
      try { localStorage.setItem('bcv:textSize', String(size)); } catch { /* this time only */ }
    };
    const stepS = (d) => set(d > 0 ? (SIZES.find((v) => v > size) || 24) : ([...SIZES].reverse().find((v) => v < size) || 11));
    const outB = btn(GLYPH.minus, 'Smaller text', () => stepS(-1));
    const inB = btn(GLYPH.plus, 'Larger text', () => stepS(1));
    const bar = h('div', { class: 'bcv-dv__bar', role: 'toolbar', 'aria-label': 'Text' }, [U.el('bcv-dv__grp bcv-dv__zoomgrp', [outB, label, inB])]);
    host.replaceChildren(U.el('bcv-dv bcv-dv--text', [bar, pre]));
    set(size);
    const offKeys = keyboard(keys, { zoom: stepS, fit: () => set(13) });
    pre.focus({ preventScroll: true });
    return { destroy: offKeys };
  }

  /** A Word document's bytes → the PDF Simpl makes of it on this device (office.js, with jsPDF). */
  async function wordToPdf(buf) {
    await BCV.tools.vendor('jspdf');
    await BCV.tools.vendor('office');
    const out = await BCV.office.docxToPdf(buf, self.jspdf.jsPDF);
    return new Uint8Array(out.output('arraybuffer'));
  }

  BCV.docview = { pdf, image, text, wordToPdf, PT };
})();
