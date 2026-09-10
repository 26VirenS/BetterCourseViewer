/* UI building blocks for the redesigned interface: tiny element helpers,
 * the components the mockup repeats (cards, rows, tiles, segmented
 * controls, switches, badges…), date formatting in the mockup's style and
 * the colour math that derives text/tint variants from a course colour. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
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

  const chip = (lbl, onClick, icon = null) => h('button', { type: 'button', class: 'bcv-chip', onclick: onClick }, [icon ? svg(icon, { size: 13, width: 1.9 }) : null, lbl]);
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
    document.body.append(t);
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => t.remove(), ms);
    return t;
  }

  /** Dropdown menu anchored under `anchor`. items: [{label, sub, active, onSelect}] */
  function menu(anchor, items) {
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
    const r = anchor.getBoundingClientRect();
    Object.assign(m.style, { position: 'fixed', top: `${r.bottom + 6}px`, left: `${Math.min(r.left, window.innerWidth - 260)}px` });
    document.body.append(m);
    setTimeout(() => document.addEventListener('click', closeMenus, { once: true }), 0);
    return m;
  }
  function closeMenus() {
    document.querySelectorAll('.bcv-menu').forEach((m) => m.remove());
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
    document.body.append(m);
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

  const initials = (name) => String(name || '').trim().split(/\s+/).slice(0, 2).map((w) => w[0]?.toUpperCase() || '').join('');

  BCV.ui = {
    svg, star, chev, el, text, tile, dot, card, row, label, h2, groupHead, badge, seg, search, switchEl, btn, iconbtn, chip, pill,
    empty, emptyCard, loading, errorBox, hint, avatar, toast, menu, closeMenus, colorMenu, COURSE_COLORS,
    DAY, startOfDay, addDays, sameDay, dayDiff, startOfWeek, parse, MONTHS, MONTHS_LONG, DAYS, DAYS_LONG,
    fmtTime, fmtTimeLower, fmtShort, fmtLong, fmtDateComma, fmtAt, fmtAtUpper, fmtBy, dayTitle, fmtDow, fmtRecent, whenShort, plural,
    hexToRgb, rgba, palette, FALLBACK_COLORS, initials,
  };
})();
