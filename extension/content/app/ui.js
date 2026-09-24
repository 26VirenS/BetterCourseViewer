/* UI building blocks for the redesigned interface: tiny element helpers,
 * the components the mockup repeats (cards, rows, tiles, segmented
 * controls, switches, badges…), date formatting in the mockup's style and
 * the colour math that derives text/tint variants from a course colour. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const { overlayRoot } = BCV.utils;
  const IC = BCV.IC;

  // ---- svg ----------------------------------------------------------------
  const NS = 'http://www.w3.org/2000/svg';
  function svg(path, { size = 15, stroke = 'currentColor', width = 1.8, fill = 'none', cls = '', style = null, cap = 'round', join = 'round' } = {}) {
    const el = document.createElementNS(NS, 'svg');
    el.setAttribute('viewBox', '0 0 24 24');
    el.setAttribute('width', size);
    el.setAttribute('height', size);
    el.setAttribute('fill', fill);
    if (fill === 'none') {
      el.setAttribute('stroke', stroke);
      el.setAttribute('stroke-width', width);
      el.setAttribute('stroke-linecap', cap);
      el.setAttribute('stroke-linejoin', join);
    } else el.setAttribute('stroke', 'none');
    el.setAttribute('aria-hidden', 'true');
    if (cls) el.setAttribute('class', cls);
    if (style) Object.assign(el.style, style);
    const p = document.createElementNS(NS, 'path');
    p.setAttribute('d', path);
    el.append(p);
    return el;
  }
  const star = (filled, size = 16) => svg(IC.star, filled ? { size, fill: 'rgba(255,255,255,.94)' } : { size, stroke: 'var(--bcv-ink3)', width: 1.7, cap: 'butt' });
  const chev = () => svg(IC.chevron, { size: 15, stroke: 'var(--bcv-ink3)', width: 2, cls: 'bcv-chev' });

  // ---- primitives -----------------------------------------------------------
  const el = (cls, children, attrs = {}) => h('div', { class: cls, ...attrs }, children);
  const text = (cls, str, tag = 'div') => h(tag, { class: cls, text: str });

  function tile(icon, { color = 'var(--bcv-ink3)', tint = 'var(--bcv-fill2)', size = 30, iconSize = 15, width = 1.8, cls = '' } = {}) {
    const mod = size === 32 ? ' bcv-tile--32' : size === 28 ? ' bcv-tile--28' : '';
    return h('span', { class: `bcv-tile${mod} ${cls}`, style: { background: tint } }, svg(icon, { size: iconSize, stroke: color, width }));
  }

  function dot(color, mod = '') {
    return h('span', { class: `bcv-dot ${mod}`, style: { background: color } });
  }

  function card(children, mod = '') {
    return el(`bcv-card ${mod}`, children);
  }

  /** A list row. Pass `onClick` to make it a button-like link. */
  function row(children, { mod = '', onClick = null, href = null, cls = '' } = {}) {
    const attrs = { class: `bcv-row ${mod} ${onClick || href ? 'bcv-row--link' : 'bcv-row--hover'} ${cls}` };
    if (href) {
      attrs.href = href;
      return h('a', { ...attrs, style: { color: 'inherit' } }, children);
    }
    if (onClick) {
      attrs.onclick = onClick;
      return h('button', { ...attrs, type: 'button' }, children);
    }
    return h('div', attrs, children);
  }

  const label = (str, mod = '') => text(`bcv-label ${mod}`, str);
  const h2 = (str, mod = '') => text(`bcv-h2 ${mod}`, str, 'h2');
  const groupHead = (title, sub, mod = '') => el(`bcv-group__head ${mod}`, [h2(title), sub ? text('bcv-group__sub', sub, 'span') : null]);

  function badge(str, kind = '', size = '') {
    const mod = { red: 'bcv-badge--red', blue: 'bcv-badge--blue', green: 'bcv-badge--green', orange: 'bcv-badge--orange' }[kind] || '';
    return h('span', { class: `bcv-badge ${mod} ${size}`, text: str });
  }
  /** A status word as a small badge, coloured by its kind (store.workStatus / store.workFlags):
   *  bad red, warn orange, good green, info blue, muted or none the plain fill. */
  function statusBadge(st, size = 'bcv-badge--xs') {
    if (!st || !st.word) return null;
    const b = badge(st.word, { bad: 'red', warn: 'orange', good: 'green', info: 'blue' }[st.kind] || '', size);
    b.classList.add('bcv-status');
    return b;
  }

  function seg(options, value, onChange, { wide = false } = {}) {
    const wrap = el(`bcv-seg ${wide ? 'bcv-seg--wide' : ''}`);
    for (const [key, lbl] of options) {
      wrap.append(h('button', {
        type: 'button',
        class: `bcv-seg__btn ${key === value ? 'is-active' : ''}`,
        text: lbl,
        dataset: { value: key },
        onclick: () => onChange(key),
      }));
    }
    return wrap;
  }

  function search(placeholder, onInput, mod = '') {
    const input = h('input', { type: 'search', placeholder, 'aria-label': placeholder });
    if (onInput) input.addEventListener('input', () => onInput(input.value.trim()));
    return el(`bcv-search ${mod}`, [svg(IC.search, { size: 14, stroke: 'var(--bcv-ink3)', width: 1.9, cap: 'round', join: 'miter' }), input]);
  }

  function switchEl(on, onToggle, ariaLabel = '') {
    const sw = h('button', { type: 'button', class: `bcv-switch ${on ? 'is-on' : ''}`, role: 'switch', 'aria-checked': on ? 'true' : 'false', 'aria-label': ariaLabel }, h('span', { class: 'bcv-switch__knob' }));
    sw.addEventListener('click', () => {
      const next = !sw.classList.contains('is-on');
      sw.classList.toggle('is-on', next);
      sw.setAttribute('aria-checked', next ? 'true' : 'false');
      onToggle(next);
    });
    return sw;
  }

  function btn(lbl, { icon = null, kind = '', onClick = null, iconColor = 'currentColor', iconSize = 14, cls = '', disabled = false, title = '' } = {}) {
    const mod = { primary: 'bcv-btn--primary', card: 'bcv-btn--card', fill36: 'bcv-btn--fill36', danger: 'bcv-btn--danger', dangerSolid: 'bcv-btn--danger-solid', sm: 'bcv-btn--sm', xs: 'bcv-btn--xs' }[kind] || '';
    const b = h('button', { type: 'button', class: `bcv-btn ${mod} ${cls}`, onclick: onClick, disabled: disabled || null, title: title || null }, [
      icon ? svg(icon, { size: iconSize, stroke: iconColor, width: 1.9 }) : null,
      lbl,
    ]);
    return b;
  }

  function iconbtn(icon, { onClick = null, size = 26, iconSize = 12, stroke = 'var(--bcv-ink3)', width = 2.3, title = '' } = {}) {
    const mod = size === 24 ? 'bcv-iconbtn--24' : size === 22 ? 'bcv-iconbtn--22' : size === 30 ? 'bcv-iconbtn--30' : '';
    return h('button', { type: 'button', class: `bcv-iconbtn ${mod}`, onclick: onClick, title: title || null, 'aria-label': title || null }, svg(icon, { size: iconSize, stroke, width }));
  }

  const pill = (lbl, onClick, mod = '') => h('button', { type: 'button', class: `bcv-pill ${mod}`, onclick: onClick, text: lbl });
  const empty = (str) => el('bcv-empty', str);
  const emptyCard = (str) => card(str, 'bcv-card--empty');
  /** Loading state (mockup 8): skeleton blocks shaped like the content they replace,
   *  so the layout does not jump when the data lands. `kind`: 'rows' (list rows,
   *  the default), 'cards' (course cards) or 'inset' (rows inside a card). Hidden
   *  from screen readers; a load faster than 150ms never shows them (CSS delay). */
  function loading(kind = 'rows', n = kind === 'cards' ? 6 : 3) {
    if (typeof kind !== 'string' || !['rows', 'cards', 'inset'].includes(kind)) kind = 'rows';
    const widths = ['72%', '58%', '84%', '64%', '76%', '52%'];
    const b = (cls, style = null) => h('span', { class: `bcv-skel__b ${cls}`, style });
    const items = Array.from({ length: n }, (_, i) => (kind === 'cards'
      ? el('bcv-skel__card', [
        el('bcv-skel__ctop', [b('bcv-skel__ring'), el('bcv-skel__lines', [b('bcv-skel__l1', { width: widths[i % 6] }), b('bcv-skel__l2', { width: '46%' }), b('bcv-skel__l3')])]),
        b('bcv-skel__bar'), b('bcv-skel__btn'),
      ])
      : el('bcv-skel__row', [b('bcv-skel__tile'), el('bcv-skel__lines', [b('bcv-skel__l1', { width: widths[i % 6] }), b('bcv-skel__l2')]), b('bcv-skel__badge')])));
    return el(`bcv-skel ${kind === 'cards' ? 'bcv-skel--cards' : kind === 'inset' ? 'bcv-skel--inset' : ''}`, items, { 'aria-hidden': 'true', role: 'presentation' });
  }
  const errorBox = (str) => el('bcv-error', str);
  const hint = (str, mod = '') => h('p', { class: `bcv-hint ${mod}`, text: str });

  function avatar(url, name, size = 38) {
    if (url && !/avatar-50|no_pic|dotted_pic|messages\/avatar-/.test(url)) {
      return h('img', { class: size === 30 ? 'bcv-account__avatar' : 'bcv-tile--38', src: url, alt: '', width: size, height: size, referrerpolicy: 'no-referrer' });
    }
    return h('span', { class: size === 30 ? 'bcv-account__avatar' : 'bcv-tile--38' });
  }

  let toastTimer = null;
  function toast(str, { error = false, ms = 2600 } = {}) {
    document.querySelectorAll('.bcv-toast').forEach((t) => t.remove());
    const t = el(`bcv-toast ${error ? 'bcv-toast--error' : ''}`, str, { role: 'status' });
    overlayRoot().append(t);
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => dismiss(t), ms); // (it leaves the way it came, on its spring)
    return t;
  }

  /** A dropdown that can show what a native one cannot.
   *
   *  A `<select>` renders its options as plain text, so an option that is a formula — which Canvas
   *  serves as a picture — comes out blank in one. This is a button that opens a list, so each
   *  option is drawn the way the rest of the interface draws Canvas content: pictures, formulas and
   *  all. It answers the keyboard the way a select does (arrows move, Enter and Space pick, Escape
   *  shuts, typing jumps to a label) and reports itself as a listbox.
   *
   *  options: [{ value, text, html }] — `html` is Canvas's own content for that option, when it has
   *  any. `value` is what onChange is given. */
  function picker(options, value, onChange, { label = '', placeholder = 'Choose…', cls = '' } = {}) {
    let cur = value === null || value === undefined ? '' : String(value);
    let list = null;
    let typed = '';
    let typedAt = 0;
    const opt = (v) => options.find((o) => String(o.value) === String(v)) || null;
    const face = el('bcv-picker__face');
    const btn = h('button', {
      type: 'button', class: `bcv-picker ${cls}`, 'aria-haspopup': 'listbox', 'aria-expanded': 'false', ...(label ? { 'aria-label': label } : {}),
    }, [face, svg('M6 9l6 6 6-6', { size: 13, stroke: 'var(--bcv-ink3)', width: 2.2, cls: 'bcv-picker__chev' })]);

    const drawFace = () => {
      const o = opt(cur);
      face.replaceChildren(o
        ? (String(o.html || '').trim() && !String(o.text || '').trim()
          ? BCV.screens.course.prose(o.html, { cls: 'bcv-picker__rich' })
          : text('bcv-picker__label bcv-ellip', o.text || BCV.utils.htmlToText(o.html || '', 80) || String(o.value), 'span'))
        : text('bcv-picker__label bcv-picker__label--none bcv-ellip', placeholder, 'span'));
      btn.classList.toggle('is-set', !!o);
    };

    function onAway(e) { if (!list?.contains(e.target) && !btn.contains(e.target)) close(); }
    function close() {
      if (!list) return;
      list.remove();
      list = null;
      btn.setAttribute('aria-expanded', 'false');
      document.removeEventListener('pointerdown', onAway, true);
      document.removeEventListener('keydown', listKeys, true);
      window.removeEventListener('resize', close);
      window.removeEventListener('scroll', onScroll, true);
    }
    // the page moving under it takes it away — but the list scrolling inside itself does not
    function onScroll(e) { if (!list?.contains(e.target)) close(); }
    const pick = (v) => {
      cur = v === null || v === undefined ? '' : String(v);
      drawFace();
      close();
      btn.focus();
      onChange(cur);
    };

    function open() {
      if (list) { close(); return; }
      closeMenus();
      const r = btn.getBoundingClientRect();
      list = el('bcv-picker__list', options.map((o) => h('button', {
        type: 'button', class: `bcv-picker__opt ${String(o.value) === cur ? 'is-on' : ''}`, role: 'option',
        'aria-selected': String(o.value) === cur ? 'true' : 'false', dataset: { value: String(o.value) },
        onclick: () => pick(o.value),
      }, [
        String(o.html || '').trim() && !String(o.text || '').trim()
          ? BCV.screens.course.prose(o.html, { cls: 'bcv-picker__rich' })
          : text('bcv-picker__opttext', o.text || BCV.utils.htmlToText(o.html || '', 120) || String(o.value), 'span'),
        svg('M20 6L9 17l-5-5', { size: 14, stroke: 'var(--bcv-blue)', width: 2.4, cls: 'bcv-picker__tick' }),
      ])), { role: 'listbox', ...(label ? { 'aria-label': label } : {}) });
      // over everything, under the button — or above it when the room is above (anchor)
      overlayRoot().append(list);
      anchor(list, r, { side: 'below', minWidth: 220 });
      btn.setAttribute('aria-expanded', 'true');
      (list.querySelector('.bcv-picker__opt.is-on') || list.firstElementChild)?.focus({ preventScroll: true });
      document.addEventListener('keydown', listKeys, true); // (the list's keys, for as long as it is open — not one more listener per picker for the page's life)
      requestAnimationFrame(() => {
        if (!list) return;
        document.addEventListener('pointerdown', onAway, true);
        window.addEventListener('resize', close);
        window.addEventListener('scroll', onScroll, true);
      });
    }

    const move = (from, step) => {
      const all = [...(list?.querySelectorAll('.bcv-picker__opt') || [])];
      if (!all.length) return;
      const i = all.indexOf(from);
      all[Math.max(0, Math.min(all.length - 1, (i < 0 ? 0 : i) + step))].focus();
    };
    const jump = (key) => {
      const now = Date.now();
      typed = now - typedAt > 900 ? key : typed + key;
      typedAt = now;
      const all = [...(list?.querySelectorAll('.bcv-picker__opt') || [])];
      all.find((x) => x.textContent.trim().toLowerCase().startsWith(typed.toLowerCase()))?.focus();
    };
    btn.addEventListener('click', open);
    btn.addEventListener('keydown', (e) => {
      if (e.key === 'ArrowDown' || e.key === 'ArrowUp' || e.key === 'Enter' || e.key === ' ') { e.preventDefault(); open(); }
    });
    btn.addEventListener('keydown', (e) => { if (e.key === 'Escape') close(); });
    // the list's own keys: the buttons in it have focus while it is open
    function listKeys(e) {
      if (!list || !list.contains(e.target)) return;
      if (e.key === 'ArrowDown') { e.preventDefault(); move(e.target, 1); }
      else if (e.key === 'ArrowUp') { e.preventDefault(); move(e.target, -1); }
      else if (e.key === 'Home') { e.preventDefault(); move(null, 0); }
      else if (e.key === 'Escape') { e.preventDefault(); close(); btn.focus(); }
      else if (e.key === 'Tab') close();
      else if (e.key.length === 1) jump(e.key);
    }
    drawFace();
    btn.bcvPicker = { set: (v) => { cur = v === null || v === undefined ? '' : String(v); drawFace(); }, get value() { return cur; }, close };
    return btn;
  }

  /** Dropdown menu anchored under `anchor`. items: [{label, sub, active, onSelect}] */
  function menu(at, items) {
    closeMenus();
    const m = el('bcv-menu', items.map((it) => h('button', {
      type: 'button',
      class: `bcv-menu__item ${it.active ? 'is-active' : ''}`,
      onclick: () => {
        closeMenus();
        it.onSelect?.();
      },
    }, [
      it.color ? dot(it.color, 'bcv-dot--9') : null,
      h('span', { style: { flex: '1', minWidth: '0' } }, [
        h('span', { class: 'bcv-ellip', style: { display: 'block' }, text: it.label }),
        it.sub ? h('span', { class: 'bcv-menu__sub', text: it.sub }) : null,
      ]),
    ])));
    overlayRoot().append(m);
    anchor(m, at, { side: 'below' }); // (under its button, or above it when the room is above; on screen either way)
    setTimeout(() => document.addEventListener('click', closeMenus, { once: true }), 0);
    return m;
  }
  // ---- the mechanisms every floating or moving thing uses -----------------------------------------
  // One way to wait for motion, one way to know an element has gone, one way to follow a box's
  // place on the page, one way to put a floating element beside another and keep it on screen.
  // Nothing waits a guessed number of milliseconds, measures once and hopes, or carries its own
  // observer: a change to a duration in the CSS, a slower machine, a zoomed window or a font that
  // arrives late are all answered here, once.

  /** Resolves when the element's (and its direct children's) running animations and transitions
   *  have ended — their own timing, read from the page — capped so a stuck one never holds a caller;
   *  at once under reduced motion. Call it right after setting the class that starts the motion. */
  function afterMotion(el, cap = 450) {
    return new Promise((resolve) => {
      if (!el || reducedMotion()) { resolve(); return; }
      const t = setTimeout(resolve, cap);
      const done = () => { clearTimeout(t); resolve(); };
      let list = [];
      try {
        list = [el, ...el.children].flatMap((n) => n.getAnimations()).filter((a) => a.playState !== 'finished' && a.playState !== 'idle' && a.effect?.getTiming?.().iterations !== Infinity);
      } catch { list = []; }
      if (!list.length) { requestAnimationFrame(() => requestAnimationFrame(done)); return; } // (two frames: a class set this turn starts its motion on the next)
      Promise.all(list.map((a) => a.finished.catch(() => {}))).then(done);
    });
  }

  /** Calls fn once the element has left the document (swept away by a screen change, closed by
   *  another path, removed by a caller). One observer serves every registration. Returns forget(). */
  const goneList = new Map(); // element → { fn, armed }: armed once it has been seen on the page (registered before its append, it is not "gone" yet)
  let goneMo = null;
  function sweepGone() {
    for (const [el, w] of goneList) {
      if (!w.armed) { if (el.isConnected) w.armed = true; continue; }
      if (!el.isConnected) { goneList.delete(el); try { w.fn(); } catch { /* the caller's own */ } }
    }
    if (!goneList.size && goneMo) { goneMo.disconnect(); goneMo = null; }
  }
  function onGone(el, fn) {
    if (!el) return () => {};
    goneList.set(el, { fn, armed: el.isConnected });
    if (!goneMo) { goneMo = new MutationObserver(sweepGone); goneMo.observe(document.documentElement, { childList: true, subtree: true }); }
    return () => goneList.delete(el);
  }

  /** Follows an element's box: fn(rect) when it is first measured and whenever it may have moved —
   *  its own or the page's size changing, something around it (not inside it) drawn or restyled, an
   *  animation or a transition ending, the fonts arriving, the window resizing or zooming — and
   *  then again each frame until it has held still for three, so a slide that is still going at the
   *  first measure is measured at its end. Returns stop(). */
  function watchLayout(el, fn, { within = null, frames = 3, limit = 240 } = {}) {
    let last = '', quiet = 0, n = 0, raf = 0, stopped = false;
    const tick = () => {
      raf = 0;
      if (stopped || !el.isConnected) return;
      const r = el.getBoundingClientRect();
      const key = `${Math.round(r.top)},${Math.round(r.left)},${Math.round(r.width)},${Math.round(r.height)}`;
      if (key !== last) { last = key; quiet = 0; try { fn(r); } catch { /* the caller's own */ } } else quiet++;
      if (quiet < frames && n++ < limit) raf = requestAnimationFrame(tick);
    };
    const kick = () => { if (stopped) return; quiet = 0; n = 0; if (!raf) raf = requestAnimationFrame(tick); };
    const scope = within || el.closest('.bcv-screen') || document.body;
    const ro = typeof ResizeObserver !== 'undefined' ? new ResizeObserver(kick) : null;
    ro?.observe(el);
    ro?.observe(document.documentElement);
    if (scope !== el) ro?.observe(scope);
    const mo = new MutationObserver((recs) => { if (recs.some((m) => m.target !== el && !el.contains(m.target))) kick(); });
    mo.observe(scope, { childList: true, subtree: true, attributes: true, attributeFilter: ['class', 'style', 'hidden'] });
    scope.addEventListener('animationend', kick, true);
    scope.addEventListener('transitionend', kick, true);
    window.addEventListener('resize', kick);
    document.fonts?.ready?.then(kick).catch(() => {});
    kick();
    return () => { stopped = true; ro?.disconnect(); mo.disconnect(); scope.removeEventListener('animationend', kick, true); scope.removeEventListener('transitionend', kick, true); window.removeEventListener('resize', kick); if (raf) cancelAnimationFrame(raf); };
  }

  /** Puts a floating element (already in the document, so it has a size) beside a rect or an
   *  element and keeps it on screen: below it — or above when the room is above — or to its right —
   *  or left; aligned to its start, centre or end; clamped to the viewport by a margin; a list placed
   *  above or below gets a maxHeight so it scrolls rather than runs off the edge. Call again after
   *  the element grows. */
  function anchor(el, at, { side = 'below', gap = 6, margin = 8, align = 'start', minWidth = 0 } = {}) {
    const r = at && at.getBoundingClientRect ? at.getBoundingClientRect() : at;
    if (!el || !r) return;
    const vw = window.innerWidth, vh = window.innerHeight;
    el.style.position = 'fixed';
    el.style.bottom = '';
    if (minWidth) el.style.minWidth = `${Math.max(r.width, minWidth)}px`;
    let m = el.getBoundingClientRect();
    let top, left;
    if (side === 'right' || side === 'left') {
      const roomRight = vw - r.right - gap - margin, roomLeft = r.left - gap - margin;
      const goRight = side === 'right' ? (m.width <= roomRight || roomRight >= roomLeft) : !(m.width <= roomLeft || roomLeft >= roomRight);
      left = goRight ? r.right + gap : r.left - gap - m.width;
      top = align === 'center' ? r.top + r.height / 2 - m.height / 2 : align === 'end' ? r.bottom - m.height : r.top;
    } else {
      const below = vh - r.bottom - gap - margin, above = r.top - gap - margin;
      const goBelow = side === 'below' ? (m.height <= below || below >= above) : !(m.height <= above || above >= below);
      el.style.maxHeight = `${Math.max(120, Math.floor(goBelow ? below : above))}px`;
      m = el.getBoundingClientRect();
      top = goBelow ? r.bottom + gap : r.top - gap - m.height;
      left = align === 'center' ? r.left + r.width / 2 - m.width / 2 : align === 'end' ? r.right - m.width : r.left;
    }
    el.style.left = `${Math.round(Math.max(margin, Math.min(left, vw - m.width - margin)))}px`;
    el.style.top = `${Math.round(Math.max(margin, Math.min(top, vh - m.height - margin)))}px`;
    // it grows out of the edge it hangs from (the entrance scales from the transform origin): under
    // its button from the top, above it from the bottom, beside it from the near side
    const ox = side === 'right' ? 'left' : side === 'left' ? 'right' : align === 'center' ? 'center' : align === 'end' ? 'right' : 'left';
    const oy = side === 'right' || side === 'left' ? (align === 'center' ? 'center' : align === 'end' ? 'bottom' : 'top') : (top < r.top ? 'bottom' : 'top');
    el.style.transformOrigin = `${ox} ${oy}`;
  }
  /** Keeps a fixed element that was placed by other means inside the viewport. */
  function keepOnScreen(el, margin = 16) {
    const m = el.getBoundingClientRect();
    const left = Math.max(margin, Math.min(m.left, window.innerWidth - m.width - margin));
    const top = Math.max(margin, Math.min(m.top, window.innerHeight - m.height - margin));
    if (Math.round(left) !== Math.round(m.left)) el.style.left = `${Math.round(left)}px`;
    if (Math.round(top) !== Math.round(m.top)) el.style.top = `${Math.round(top)}px`;
  }
  /** The box an element and everything drawn in it take up (a switch with a pill popped out of it). */
  function boundsOf(el) {
    const r = el.getBoundingClientRect();
    let left = r.left, top = r.top, right = r.right, bottom = r.bottom, n = 0;
    for (const c of el.querySelectorAll('*')) {
      if (n++ > 400) break;
      const cs = getComputedStyle(c);
      if (cs.display === 'none' || cs.visibility === 'hidden' || cs.opacity === '0') continue;
      const q = c.getBoundingClientRect();
      if (!q.width || !q.height) continue;
      left = Math.min(left, q.left); top = Math.min(top, q.top); right = Math.max(right, q.right); bottom = Math.max(bottom, q.bottom);
    }
    return { left, top, right, bottom, width: right - left, height: bottom - top };
  }

  /** Closes an overlay, a menu or a sheet with its exit: is-closing starts the CSS's animation, and
   *  the element goes when that has ended (at once under reduced motion). */
  /** Which spring an element leaves on (docs/MOTION.md): a scrim fades while its sheet shrinks (a
   *  phone sheet slides down), a menu or a toast pops out, anything else fades. */
  function exitKindOf(ov) {
    const c = ov.classList;
    if (c.contains('bcv-sheet-ov')) return document.documentElement.classList.contains('bcv-phone') ? 'down' : 'shrink';
    if (c.contains('bcv-toast')) return 'toast';
    if (c.contains('bcv-quicknav')) return 'left';
    if (c.contains('bcv-menu')) return 'pop';
    return 'fade';
  }
  function dismiss(ov) {
    if (!ov || !ov.isConnected || ov.classList.contains('is-closing')) return Promise.resolve();
    const M = BCV.motion;
    if (M && !reducedMotion()) {
      // the exit is a spring from wherever the entrance has got to (a sheet closed while still growing
      // shrinks back from there): the scrim fades, the sheet inside it moves. The springs are started
      // before the closing class goes on — that class stands the stylesheet's motion down, and the
      // entrance has to be read for its progress while it is still running
      const kind = exitKindOf(ov);
      const sheet = ov.classList.contains('bcv-sheet-ov') ? ov.querySelector(':scope > .bcv-sheet') : null;
      const parts = [M.exit(ov, sheet ? 'scrim' : kind)];
      if (sheet) parts.push(M.exit(sheet, kind));
      ov.classList.add('is-closing', 'bcv-sprung');
      return Promise.all(parts.map((p) => p.finished)).then(() => ov.remove());
    }
    ov.classList.add('is-closing');
    return afterMotion(ov).then(() => ov.remove());
  }
  function closeMenus() {
    document.querySelectorAll('.bcv-menu:not([data-keep]):not(.is-closing)').forEach((m) => dismiss(m)); // (a box that only wears a menu's look stays: the inbox's recipient results)
  }

  /** Canvas's own course colour palette (the picker on its dashboard cards), plus a custom colour. */
  const COURSE_COLORS = [['#BD3C14', 'Brick'], ['#FF2717', 'Red'], ['#E71F63', 'Magenta'], ['#8F3E97', 'Purple'], ['#65499D', 'Deep purple'], ['#4554A4', 'Indigo'], ['#1770AB', 'Blue'], ['#0B9BE3', 'Light blue'], ['#06A3B7', 'Cyan'], ['#009688', 'Teal'], ['#009606', 'Green'], ['#8D9900', 'Olive'], ['#D97900', 'Pumpkin'], ['#FD5D10', 'Orange'], ['#F06291', 'Pink']];
  function colorMenu(anchor, current, onPick) {
    closeMenus();
    const cur = String(current || '').toLowerCase();
    const custom = h('input', { type: 'color', class: 'bcv-swatch__input', value: /^#[0-9a-f]{6}$/i.test(cur) ? cur : '#8e8e93', 'aria-label': 'Custom colour' });
    custom.addEventListener('change', () => { closeMenus(); onPick(custom.value.toUpperCase()); });
    const m = el('bcv-menu bcv-menu--colors', [
      text('bcv-menu__title', 'Course colour'),
      el('bcv-swatches', [
        ...COURSE_COLORS.map(([hex, name]) => h('button', {
          type: 'button', class: `bcv-swatch ${hex.toLowerCase() === cur ? 'is-current' : ''}`, title: name, 'aria-label': name, dataset: { color: hex }, style: { background: hex },
          onclick: () => { closeMenus(); onPick(hex); },
        }, hex.toLowerCase() === cur ? svg('M20 6L9 17l-5-5', { size: 12, stroke: '#fff', width: 2.6 }) : null)),
        h('label', { class: 'bcv-swatch bcv-swatch--custom', title: 'Custom colour' }, custom),
      ]),
    ]);
    m.addEventListener('click', (e) => e.stopPropagation());
    const r = anchor.getBoundingClientRect();
    Object.assign(m.style, { position: 'fixed', top: `${r.bottom + 6}px`, left: `${Math.max(8, Math.min(r.left, window.innerWidth - 230))}px` });
    overlayRoot().append(m);
    setTimeout(() => document.addEventListener('click', closeMenus, { once: true }), 0);
    return m;
  }

  // ---- dates in the mockup's style ----------------------------------------------
  const DAY = 864e5;
  const startOfDay = (d) => {
    const x = new Date(d);
    x.setHours(0, 0, 0, 0);
    return x;
  };
  const addDays = (d, n) => {
    const x = new Date(d);
    x.setDate(x.getDate() + n);
    return x;
  };
  const sameDay = (a, b) => a && b && startOfDay(a).getTime() === startOfDay(b).getTime();
  const dayDiff = (d, now = new Date()) => Math.round((startOfDay(d) - startOfDay(now)) / DAY);
  /** Monday-start week (ISO), matching "Week of Sep 7". */
  const startOfWeek = (d) => {
    const x = startOfDay(d);
    const dow = (x.getDay() + 6) % 7;
    return addDays(x, -dow);
  };
  const parse = (v) => {
    if (!v) return null;
    const d = v instanceof Date ? v : new Date(v);
    return isNaN(d) ? null : d;
  };
  const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  const MONTHS_LONG = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
  const DAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
  const DAYS_LONG = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

  /** "11:59 PM" */
  function fmtTime(v) {
    const d = parse(v);
    if (!d) return '';
    let hr = d.getHours();
    const min = d.getMinutes();
    const ampm = hr >= 12 ? 'PM' : 'AM';
    hr = hr % 12 || 12;
    return `${hr}:${String(min).padStart(2, '0')} ${ampm}`;
  }
  /** "11:59pm" */
  const fmtTimeLower = (v) => fmtTime(v).replace(' ', '').toLowerCase();
  /** "Sep 10" */
  const fmtShort = (v) => {
    const d = parse(v);
    return d ? `${MONTHS[d.getMonth()]} ${d.getDate()}` : '';
  };
  /** "September 10" */
  const fmtLong = (v) => {
    const d = parse(v);
    return d ? `${MONTHS_LONG[d.getMonth()]} ${d.getDate()}` : '';
  };
  /** "Sep 1, 2026" */
  const fmtDateComma = (v) => {
    const d = parse(v);
    return d ? `${MONTHS[d.getMonth()]} ${d.getDate()}, ${d.getFullYear()}` : '';
  };
  /** "Sep 10 at 11:59pm" */
  const fmtAt = (v) => {
    const d = parse(v);
    return d ? `${fmtShort(d)} at ${fmtTimeLower(d)}` : '';
  };
  /** "Sep 10 at 5:47 AM" */
  const fmtAtUpper = (v) => {
    const d = parse(v);
    return d ? `${fmtShort(d)} at ${fmtTime(d)}` : '';
  };
  /** "Sep 10 by 10:30am" (grades table) */
  const fmtBy = (v) => {
    const d = parse(v);
    return d ? `${fmtShort(d)} by ${fmtTimeLower(d)}` : '';
  };
  /** Day heading like the dashboard list: Today / Tomorrow / Sunday / Sep 21 */
  function dayTitle(v, now = new Date()) {
    const d = parse(v);
    if (!d) return 'No date';
    const diff = dayDiff(d, now);
    if (diff === 0) return 'Today';
    if (diff === 1) return 'Tomorrow';
    if (diff === -1) return 'Yesterday';
    if (diff > 1 && diff < 7) return DAYS_LONG[d.getDay()];
    return fmtShort(d);
  }
  /** "Thu, Sep 10" */
  const fmtDow = (v) => {
    const d = parse(v);
    return d ? `${DAYS[d.getDay()]}, ${fmtShort(d)}` : '';
  };
  /** Relative like "Friday", "modified Aug 26, 2026" helper */
  function fmtRecent(v, now = new Date()) {
    const d = parse(v);
    if (!d) return '';
    const diff = dayDiff(d, now);
    if (diff === 0) return 'today';
    if (diff === -1) return 'yesterday';
    if (diff < 0 && diff > -7) return DAYS_LONG[d.getDay()];
    return fmtDateComma(d);
  }
  /** Short "when" for list rows: today/tomorrow → time; within a week → weekday; else date */
  function whenShort(v, now = new Date()) {
    const d = parse(v);
    if (!d) return 'No date';
    const diff = dayDiff(d, now);
    if (diff === 0 || diff === 1) return fmtTime(d);
    if (diff > 1 && diff < 7) return DAYS[d.getDay()];
    return fmtShort(d);
  }
  function plural(n, one, many = `${one}s`) {
    return `${n} ${n === 1 ? one : many}`;
  }

  // ---- colours --------------------------------------------------------------------
  function hexToRgb(hex) {
    let s = String(hex || '').trim().replace(/^#/, '');
    if (s.length === 3) s = s.split('').map((c) => c + c).join('');
    if (!/^[0-9a-f]{6}$/i.test(s)) return null;
    const n = parseInt(s, 16);
    return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
  }
  function rgbToHsl([r, g, b]) {
    r /= 255; g /= 255; b /= 255;
    const max = Math.max(r, g, b), min = Math.min(r, g, b);
    let hh = 0, s = 0;
    const l = (max + min) / 2;
    if (max !== min) {
      const d = max - min;
      s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
      if (max === r) hh = (g - b) / d + (g < b ? 6 : 0);
      else if (max === g) hh = (b - r) / d + 2;
      else hh = (r - g) / d + 4;
      hh /= 6;
    }
    return [hh, s, l];
  }
  function hslToHex([hh, s, l]) {
    const f = (p, q, t) => {
      if (t < 0) t += 1;
      if (t > 1) t -= 1;
      if (t < 1 / 6) return p + (q - p) * 6 * t;
      if (t < 1 / 2) return q;
      if (t < 2 / 3) return p + (q - p) * (2 / 3 - t) * 6;
      return p;
    };
    let r, g, b;
    if (s === 0) r = g = b = l;
    else {
      const q = l < 0.5 ? l * (1 + s) : l + s - l * s;
      const p = 2 * l - q;
      r = f(p, q, hh + 1 / 3); g = f(p, q, hh); b = f(p, q, hh - 1 / 3);
    }
    const c = (v) => Math.round(v * 255).toString(16).padStart(2, '0');
    return `#${c(r)}${c(g)}${c(b)}`;
  }
  const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));
  const rgba = (hex, a) => {
    const c = hexToRgb(hex);
    return c ? `rgba(${c[0]},${c[1]},${c[2]},${a})` : hex;
  };
  /** Text-legible variant of a course colour and its soft tint. */
  function palette(hex, dark) {
    const rgb = hexToRgb(hex);
    if (!rgb) return { text: dark ? '#c7c7cc' : '#3c3c43', tint: dark ? 'rgba(118,118,128,.28)' : 'rgba(118,118,128,.12)', raw: hex || '#8e8e93' };
    const [hh, s, l] = rgbToHsl(rgb);
    const textL = dark ? clamp(l + 0.16, 0.6, 0.82) : clamp(l - 0.19, 0.28, 0.46);
    const textS = dark ? clamp(s, 0.5, 1) : clamp(s, 0.45, 1);
    return { text: hslToHex([hh, textS, textL]), tint: rgba(hex, dark ? 0.2 : 0.14), raw: hex };
  }
  const FALLBACK_COLORS = ['#34c759', '#30b0c7', '#ff2d55', '#ff9500', '#c8901c', '#5856d6', '#0a84ff', '#6b5f7a', '#af52de', '#ff6b22'];

  /** Staggered entry (mockup 8/9): the element rises in after index × step ms, capped at
   *  420ms so a long list never crawls. A keyframe with a both fill; reduced motion drops it. */
  /** The word-mark's dot (the setup's and What's New's intro): placed where the word "Simpl" ends
   *  as this system's font draws it, and the drawing widened to hold it. */
  function placeDot(intro) {
    try {
      const svg = intro.querySelector('svg');
      const text = intro.querySelector('text');
      const dot = intro.querySelector('.intro__dot');
      if (!svg || !text || !dot) return;
      const b = text.getBBox();
      if (!(b.width > 0)) return;
      const cx = Math.round(b.x + b.width + 14);
      dot.setAttribute('cx', String(cx));
      dot.style.transformOrigin = `${cx}px 90px`;
      const w = Math.max(304, cx + 16);
      svg.setAttribute('viewBox', `0 0 ${w} 142`);
      svg.setAttribute('width', String(Math.round(w * (356 / 304))));
    } catch { /* the design's place */ }
  }

  // ---- a redraw is not an arrival ------------------------------------------------------------------
  // Entrances (the stagger below, the screen root's rise) belong to a screen arriving under the
  // student. A list drawn again because one row changed, or a screen drawn again silently because
  // the tab was away, must land in place, still: the same rows must not vanish and fade back in.
  // still(fn) runs fn — sync or async, nested as deep as it likes — with entrances off; enter() then
  // hands the node back untouched, and app.render({ quiet }) lands its root with bcv-screen--still.
  let stillDepth = 0;
  const isStill = () => stillDepth > 0;
  function still(fn) {
    stillDepth += 1;
    let out;
    try {
      out = fn();
    } catch (e) {
      stillDepth -= 1;
      throw e;
    }
    if (out && typeof out.then === 'function') return out.finally(() => { stillDepth -= 1; });
    stillDepth -= 1;
    return out;
  }

  function enter(node, i = 0, step = 55, dur = 380) {
    if (!node || isStill()) return node;
    node.classList.add('bcv-enter');
    node.style.setProperty('--bcv-delay', `${Math.min((i || 0) * step, 420)}ms`);
    if (dur !== 380) node.style.setProperty('--bcv-dur', `${dur}ms`);
    return node;
  }

  const initials = (name) => String(name || '').trim().split(/\s+/).slice(0, 2).map((w) => w[0]?.toUpperCase() || '').join('');

  const reducedMotion = () => {
    try {
      return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    } catch {
      return false;
    }
  };

  /** A number arrives rather than appears (mockup 11): 16 steps at 52ms — plausible digits
   *  below step 9 (under half a second), convergence above it — and the last step is always
   *  the exact value. Entry only: callers never roll a number the student is already reading.
   *  Reduced motion jumps straight to the final step. */
  const ROLL_STEPS = 16;
  const ROLL_MS = 52;
  function roll(node, target, { decimals = 0, seed = 0, format = null } = {}) {
    const fmt = format || ((v) => (decimals ? v.toFixed(decimals) : String(Math.round(v))));
    const finish = () => {
      clearInterval(node._bcvRoll);
      node._bcvRoll = null;
      node.textContent = fmt(target);
      delete node.dataset.rolling;
    };
    if (node._bcvRoll) clearInterval(node._bcvRoll);
    if (!Number.isFinite(target) || reducedMotion()) {
      finish();
      return node;
    }
    const at = (n) => {
      if (n < 9) return decimals ? Math.abs(Math.sin(n * 1.9 + seed)) * Math.max(target, 1) : 1 + Math.floor(Math.abs(Math.sin(n * 1.7 + seed)) * Math.max(target, 4));
      const p = (n - 9) / (ROLL_STEPS - 9);
      return decimals ? target * (0.65 + 0.35 * p) : Math.max(0, Math.round(target * (0.6 + 0.4 * p)));
    };
    let n = 0;
    node.dataset.rolling = '1';
    node.textContent = fmt(at(0));
    node._bcvRoll = setInterval(() => {
      n += 1;
      if (n >= ROLL_STEPS || !node.isConnected) { // never a fabricated digit left behind: the last step is the real value
        finish();
        return;
      }
      node.textContent = fmt(at(n));
    }, ROLL_MS);
    return node;
  }

  /** A sheet grows out of the control that opened it (mockup 11): its transform origin is
   *  the anchor's centre, measured against the sheet's laid-out box (transforms aside, so
   *  the running animation does not skew the measurement). No anchor: from the centre. */
  function morphFrom(sheet, from) {
    if (!sheet) return sheet;
    const anchor = from && from.currentTarget ? from.currentTarget : from;
    let r = null;
    try {
      r = anchor && anchor.getBoundingClientRect ? anchor.getBoundingClientRect() : null;
    } catch {
      r = null;
    }
    if (!r || !r.width) return sheet;
    const ov = sheet.offsetParent;
    const box = ov ? ov.getBoundingClientRect() : { left: 0, top: 0 };
    const x = r.left + r.width / 2 - (box.left + sheet.offsetLeft);
    const y = r.top + r.height / 2 - (box.top + sheet.offsetTop);
    sheet.style.transformOrigin = `${Math.round(x)}px ${Math.round(y)}px`;
    return sheet;
  }

  // ---- a date field and its calendar (after the date helpers it uses) ----
  /** A day the way people say it: "Today · Fri, Sep 11", "Tomorrow · Sat, Sep 12", "Fri, Sep 18", "Fri, Jan 8, 2027". */
  function fmtDay(v, now = new Date()) {
    const d = parse(v);
    if (!d) return 'No date';
    const diff = dayDiff(d, now);
    if (diff === 0) return `Today · ${fmtDow(d)}`;
    if (diff === 1) return `Tomorrow · ${fmtDow(d)}`;
    if (d.getFullYear() !== now.getFullYear()) return `${DAYS[d.getDay()]}, ${fmtDateComma(d)}`;
    return fmtDow(d);
  }
  /** A calendar popover under `anchor`: a month grid (today marked, the chosen day filled), ‹ › for
   *  the months, Today / Tomorrow / Next Monday shortcuts. Picking a day closes it and calls onPick(date). */
  function datePop(at, value, onPick) {
    closeMenus();
    const now = new Date();
    const sel = parse(value) ? startOfDay(parse(value)) : null;
    let month = new Date((sel || now).getFullYear(), (sel || now).getMonth(), 1);
    const m = el('bcv-menu bcv-datepop', null, { role: 'dialog', 'aria-label': 'Pick a date' });
    m.addEventListener('click', (e) => e.stopPropagation()); // moving months keeps it open
    const pick = (d) => { closeMenus(); onPick(startOfDay(d)); };
    const nextMonday = addDays(now, ((8 - now.getDay()) % 7) || 7);
    function draw() {
      const start = addDays(month, -month.getDay());
      const cells = [];
      for (let i = 0; i < 42; i++) {
        const d = addDays(start, i);
        const off = d.getMonth() !== month.getMonth();
        const today = sameDay(d, now);
        const on = !!sel && sameDay(d, sel);
        cells.push(h('button', { type: 'button', class: `bcv-datepop__day ${off ? 'is-off' : ''} ${today ? 'is-today' : ''} ${on ? 'is-on' : ''}`, text: String(d.getDate()), 'aria-label': `${DAYS_LONG[d.getDay()]}, ${fmtLong(d)}`, 'aria-pressed': on ? 'true' : 'false', onclick: () => pick(d) }));
      }
      m.replaceChildren(
        el('bcv-datepop__head', [
          h('button', { type: 'button', class: 'bcv-datepop__nav', 'aria-label': 'Previous month', onclick: () => { month = new Date(month.getFullYear(), month.getMonth() - 1, 1); draw(); } }, svg('M15 5l-7 7 7 7', { size: 14, width: 2.2 })),
          text('bcv-datepop__month', `${MONTHS_LONG[month.getMonth()]}${month.getFullYear() !== now.getFullYear() ? ` ${month.getFullYear()}` : ''}`, 'span'),
          h('button', { type: 'button', class: 'bcv-datepop__nav', 'aria-label': 'Next month', onclick: () => { month = new Date(month.getFullYear(), month.getMonth() + 1, 1); draw(); } }, svg('M9 6l6 6-6 6', { size: 14, width: 2.2 })),
        ]),
        el('bcv-datepop__dows', DAYS.map((x) => text('bcv-datepop__dow', x[0], 'span'))),
        el('bcv-datepop__grid', cells),
        el('bcv-datepop__quick', [
          h('button', { type: 'button', class: 'bcv-datepop__q', text: 'Today', onclick: () => pick(now) }),
          h('button', { type: 'button', class: 'bcv-datepop__q', text: 'Tomorrow', onclick: () => pick(addDays(now, 1)) }),
          h('button', { type: 'button', class: 'bcv-datepop__q', text: 'Next Monday', onclick: () => pick(nextMonday) }),
        ]),
      );
    }
    draw();
    overlayRoot().append(m);
    anchor(m, at, { side: 'below' }); // (under the field, or above it when the room is above; on screen either way)
    m.style.maxHeight = ''; // (a calendar is not a list: it is never cut, it moves)
    setTimeout(() => document.addEventListener('click', closeMenus, { once: true }), 0);
    m.querySelector('.bcv-datepop__day.is-on, .bcv-datepop__day.is-today')?.focus();
    return m;
  }
  /** A date field: a calendar glyph, the day as people say it, a chevron; a tap opens the calendar. */
  function dateField(value, onChange, { cls = '', label = 'Due date' } = {}) {
    let cur = parse(value) ? startOfDay(parse(value)) : null;
    const lbl = text('bcv-date__label bcv-ellip', fmtDay(cur), 'span');
    const b = h('button', { type: 'button', class: `bcv-date ${cls}`, 'aria-label': label, 'aria-haspopup': 'dialog' }, [
      svg(IC.cal, { size: 14, stroke: 'var(--bcv-blue)', width: 1.9 }),
      lbl,
      svg('M6 9l6 6 6-6', { size: 12, stroke: 'var(--bcv-ink3)', width: 2.2, cls: 'bcv-date__chev' }),
    ]);
    b.addEventListener('click', (e) => {
      e.stopPropagation();
      datePop(b, cur, (d) => { cur = d; lbl.textContent = fmtDay(d); onChange(d); });
    });
    return b;
  }
  /** A small sheet with one text field: a title, a note, the field, Save (and an optional clear
   *  action, which saves an empty value). onSave may throw: the sheet stays and says why. */
  function promptSheet({ label = '', title, note = '', value = '', placeholder = '', maxLength = 120, saveLabel = 'Save', clearLabel = null, onSave, from = null }) {
    document.querySelector('.bcv-sheet-ov')?.remove();
    const ov = el('bcv-sheet-ov', null, { role: 'dialog', 'aria-label': label || title });
    const close = () => dismiss(ov);
    ov.addEventListener('click', (e) => { if (e.target === ov) close(); });
    ov.addEventListener('keydown', (e) => { if (e.key === 'Escape') close(); });
    const input = h('input', { class: 'bcv-input bcv-prompt__input', type: 'text', value, placeholder, maxlength: String(maxLength), 'aria-label': title });
    let busy = false;
    let saveBtn = null;
    const save = async (v) => {
      if (busy) return;
      busy = true;
      saveBtn.disabled = true;
      try {
        await onSave(v);
        close();
      } catch (e) {
        busy = false;
        saveBtn.disabled = false;
        toast(`Could not save: ${e?.message || e}`, { error: true });
      }
    };
    saveBtn = btn(saveLabel, { kind: 'primary', cls: 'bcv-prompt__save', onClick: () => save(input.value.trim()) });
    input.addEventListener('keydown', (e) => { if (e.key === 'Enter') save(input.value.trim()); });
    ov.append(el('bcv-sheet bcv-sheet--prompt', [
      el('bcv-sheet__head', [
        el('bcv-sheet__titles', [text('bcv-sheet__title', title), note ? text('bcv-sheet__note bcv-pretty', note) : null]),
        h('button', { type: 'button', class: 'bcv-sheet__close', 'aria-label': 'Close', onclick: close }, svg(IC.close, { size: 13, stroke: 'var(--bcv-ink2)', width: 2.3 })),
      ]),
      el('bcv-prompt', [
        input,
        el('bcv-prompt__btns', [
          clearLabel ? btn(clearLabel, { kind: 'danger', cls: 'bcv-prompt__clear', onClick: () => save('') }) : null,
          h('span', { style: { flex: '1' } }),
          btn('Cancel', { onClick: close }),
          saveBtn,
        ]),
      ]),
    ]));
    overlayRoot().append(ov);
    if (from) morphFrom(ov.firstElementChild, from);
    ov.tabIndex = -1;
    setTimeout(() => { input.focus(); input.select(); }, 30);
    return { close, input };
  }

  /** A question with two answers, or a note with one (cancelLabel null): resolves true for the main
   *  button, false for Cancel, Escape, the scrim or the X. */
  function askSheet({ label = '', title, note = '', okLabel = 'OK', cancelLabel = 'Cancel', danger = false, from = null } = {}) {
    document.querySelector('.bcv-sheet-ov')?.remove();
    return new Promise((resolve) => {
      const ov = el('bcv-sheet-ov', null, { role: 'dialog', 'aria-label': label || title });
      let settled = false;
      const done = (v) => { if (settled) return; settled = true; dismiss(ov); resolve(v); };
      ov.addEventListener('click', (e) => { if (e.target === ov) done(false); });
      ov.addEventListener('keydown', (e) => { if (e.key === 'Escape') { e.stopPropagation(); done(false); } });
      const ok = btn(okLabel, { kind: danger ? 'dangerSolid' : 'primary', cls: 'bcv-ask__ok', onClick: () => done(true) });
      ov.append(el('bcv-sheet bcv-sheet--prompt bcv-sheet--ask', [
        el('bcv-sheet__head', [
          el('bcv-sheet__titles', [text('bcv-sheet__title bcv-pretty', title)]),
          h('button', { type: 'button', class: 'bcv-sheet__close', 'aria-label': 'Close', onclick: () => done(false) }, svg(IC.close, { size: 13, stroke: 'var(--bcv-ink2)', width: 2.3 })),
        ]),
        el('bcv-prompt bcv-ask', [
          note ? text('bcv-ask__note bcv-pretty', note) : null,
          el('bcv-prompt__btns', [h('span', { style: { flex: '1' } }), cancelLabel ? btn(cancelLabel, { cls: 'bcv-ask__cancel', onClick: () => done(false) }) : null, ok]),
        ]),
      ]));
      overlayRoot().append(ov);
      if (from) morphFrom(ov.firstElementChild, from);
      ov.tabIndex = -1;
      setTimeout(() => ok.focus(), 30);
    });
  }

  BCV.ui = {
    svg, star, chev, el, text, tile, dot, card, row, label, h2, groupHead, badge, statusBadge, seg, search, switchEl, btn, iconbtn, pill, placeDot,
    empty, emptyCard, loading, errorBox, hint, avatar, toast, menu, closeMenus, picker, colorMenu, COURSE_COLORS, fmtDay, datePop, dateField, promptSheet, askSheet,
    DAY, startOfDay, addDays, sameDay, dayDiff, startOfWeek, parse, MONTHS, MONTHS_LONG, DAYS, DAYS_LONG,
    fmtTime, fmtTimeLower, fmtShort, fmtLong, fmtDateComma, fmtAt, fmtAtUpper, fmtBy, dayTitle, fmtDow, fmtRecent, whenShort, plural,
    hexToRgb, rgba, palette, FALLBACK_COLORS, initials, enter, still, isStill, roll, morphFrom, reducedMotion, dismiss,
    afterMotion, onGone, watchLayout, anchor, keepOnScreen, boundsOf,
  };
})();
