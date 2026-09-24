/* Simpl Courses — the theme: one colour of the student's own, and the shades the interface draws
 * from it. Classic script (no modules), loaded at document_start before content/early.js so the
 * accent is on the page before first paint, and shared by the setup, the settings and the app.
 *
 * The accent replaces the interface's blue — the sidebar's glyphs and words, the loading washes,
 * the primary buttons, the active segment, links — and nothing else: the grounds (the page, the
 * cards, the chrome) keep their greys, so a pink is pink on white by day and pink on black by
 * night. One colour is never enough for that: a shade that reads as an icon on white is too dark
 * for black, a fill that carries white text needs more depth than a glyph does. So the accent is
 * the seed, and each mode derives its own set from it (palette()):
 *   icon  · the accent moved in lightness until it stands 3:1 against that mode's card (WCAG's
 *           floor for graphics), so a glyph or a loading wash never washes out
 *   text  · moved further, to 4.5:1 (the floor for words), for the sidebar's labels and links
 *   fill  · deep enough to carry white words at 4.5:1 — the primary button, the selected segment
 *   hover · the fill a step deeper (by day) or lighter (by night)
 *   soft  · the accent at a low alpha, for tints behind an active row
 * The picker offers only colours from which those shades can be drawn without leaving the hue
 * behind: a hue is free, a tone slides within the band where the seed already stands 3:1 against
 * both white and the dark card, and a typed hex outside the band is moved to its nearest point
 * in it (nearest()). Grey is not a colour: saturation stays at a quarter or more. */
(function () {
  const BCV = (self.BCV = self.BCV || {});

  const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
  const hexToRgb = (hex) => {
    const m = /^#?([0-9a-f]{3}|[0-9a-f]{6})$/i.exec(String(hex || '').trim());
    if (!m) return null;
    let s = m[1];
    if (s.length === 3) s = s.split('').map((c) => c + c).join('');
    const n = parseInt(s, 16);
    return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
  };
  const rgbToHex = ([r, g, b]) => `#${[r, g, b].map((v) => Math.round(clamp(v, 0, 255)).toString(16).padStart(2, '0')).join('')}`;
  const rgbToHsl = ([r, g, b]) => {
    r /= 255; g /= 255; b /= 255;
    const max = Math.max(r, g, b), min = Math.min(r, g, b);
    const l = (max + min) / 2;
    if (max === min) return [0, 0, l];
    const d = max - min;
    const s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
    let h;
    if (max === r) h = (g - b) / d + (g < b ? 6 : 0);
    else if (max === g) h = (b - r) / d + 2;
    else h = (r - g) / d + 4;
    return [h * 60, s, l];
  };
  const hslToRgb = ([h, s, l]) => {
    h = ((h % 360) + 360) % 360 / 360;
    if (s === 0) return [l * 255, l * 255, l * 255];
    const q = l < 0.5 ? l * (1 + s) : l + s - l * s;
    const p = 2 * l - q;
    const f = (t) => { t = ((t % 1) + 1) % 1; if (t < 1 / 6) return p + (q - p) * 6 * t; if (t < 1 / 2) return q; if (t < 2 / 3) return p + (q - p) * (2 / 3 - t) * 6; return p; };
    return [f(h + 1 / 3) * 255, f(h) * 255, f(h - 1 / 3) * 255];
  };
  const hslToHex = (hsl) => rgbToHex(hslToRgb(hsl));
  const luminance = ([r, g, b]) => {
    const f = (c) => { c /= 255; return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4; };
    return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b);
  };
  /** WCAG contrast ratio between two colours (hex or rgb), 1 to 21. */
  const contrast = (a, b) => {
    const la = luminance(Array.isArray(a) ? a : hexToRgb(a) || [0, 0, 0]);
    const lb = luminance(Array.isArray(b) ? b : hexToRgb(b) || [0, 0, 0]);
    return (Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05);
  };

  // the grounds the shades are measured against: the card by day and by night (the chrome and
  // the page are within a shade of them), and white, for words on a fill
  const GROUND = { light: '#ffffff', dark: '#1c1c1e' };
  const MIN_SAT = 0.25;
  const ICON_RATIO = 3; // WCAG 1.4.11: graphics and interface parts

  /** The colour moved along lightness — darker on a light ground, lighter on a dark one — until it
   *  stands `ratio` against the ground; the hue and the saturation are kept. Already there: itself. */
  function reach(hex, ground, ratio, dark) {
    const rgb = hexToRgb(hex);
    if (!rgb) return hex;
    let [h, s, l] = rgbToHsl(rgb);
    for (let i = 0; i < 100 && contrast(hslToRgb([h, s, l]), ground) < ratio; i++) l = clamp(l + (dark ? 0.01 : -0.01), 0, 1);
    return hslToHex([h, s, l]);
  }
  const shift = (hex, dl) => { const [h, s, l] = rgbToHsl(hexToRgb(hex)); return hslToHex([h, s, clamp(l + dl, 0, 1)]); };
  const alpha = (hex, a) => { const [r, g, b] = hexToRgb(hex); return `rgba(${Math.round(r)},${Math.round(g)},${Math.round(b)},${a})`; };

  /** The shades one mode draws from the accent (see the head of this file). */
  function palette(accent, dark) {
    const seed = normalize(accent);
    if (!seed) return null;
    const ground = dark ? GROUND.dark : GROUND.light;
    const icon = reach(seed, ground, ICON_RATIO, dark);
    const text = readableOn(seed, ground);
    const fill = fillFor(seed); // white words on it, in either mode
    // the greys around the colour take a soft cast of it — the ground, the cards, the fills, the
    // hairlines, the secondary inks — so nothing sits apart from the colour: a few percent, never more
    const C = dark ? CAST.dark : CAST.light;
    const G = dark
      ? { bg: ['#000000', C.bg], card: ['#1c1c1e', C.card], hover: ['#2c2c2e', C.hover], ink2: ['#c7c7cc', C.ink2], ink3: ['#8e8e93', C.ink3], glass: ['#1c1c1e', C.glass], glassA: 0.74, line: '#ffffff', sepA: 0.08, edgeA: 0.1, fillA: 0.28, fill2A: 0.14, chromeA: 0.6 }
      : { bg: ['#f2f2f6', C.bg], card: ['#ffffff', C.card], hover: ['#fafafc', C.hover], ink2: ['#3c3c43', C.ink2], ink3: ['#8e8e93', C.ink3], glass: ['#ffffff', C.glass], glassA: 0.78, line: '#3c3c43', sepA: 0.09, edgeA: 0.1, fillA: 0.12, fill2A: 0.06, chromeA: 0.7 };
    const cast = ([base, k]) => mix(base, seed, k);
    const line = mix(G.line, seed, C.line), fillBase = mix('#767680', seed, C.fill);
    return {
      accent: seed, icon, text, fill,
      hover: shift(fill, dark ? 0.08 : -0.08),
      soft: alpha(seed, dark ? 0.22 : 0.14),
      ring: alpha(seed, dark ? 0.45 : 0.32),
      ground: { bg: cast(G.bg), card: cast(G.card), hover: cast(G.hover), ink2: cast(G.ink2), ink3: cast(G.ink3), sep: alpha(line, G.sepA), edge: alpha(line, G.edgeA), fill: alpha(fillBase, G.fillA), fill2: alpha(fillBase, G.fill2A), chrome: alpha(cast(G.bg), G.chromeA), glass: alpha(cast(G.glass), G.glassA) },
    };
  }
  /** The ink a photo is drawn in on a surface: the surface's own colour lifted a shade — a touch of
   *  white on the dark look, a touch of black on the light — so the picture sits tone on tone in it
   *  (app.css --bcv-ink-lift, --bcv-ink-k say the same to the page). */
  const INK_LIFT = { dark: ['#ffffff', 0.1], light: ['#000000', 0.07] };
  const inkOn = (paper, dark) => mix(paper, INK_LIFT[dark ? 'dark' : 'light'][0], INK_LIFT[dark ? 'dark' : 'light'][1]);
  /** One shade per sidebar row, so the rail is not a single flat colour: the accent's hue drifts a
   *  little across the rows and its lightness walks the readable band from lighter to deeper (the
   *  band is readable in both modes, so the walk is the same in each), and every glyph shade is
   *  then pushed to this mode's icon contrast, its words to the text contrast. */
  function shades(accent, dark, n) {
    const seed = normalize(accent);
    if (!seed || !(n > 0)) return [];
    const ground = dark ? GROUND.dark : GROUND.light;
    const set = shadeSet(seed);
    const out = [];
    for (let i = 0; i < n; i++) { const base = set[i % set.length]; out.push({ icon: reach(base, ground, ICON_RATIO, dark), text: readableOn(base, ground) }); }
    return out;
  }
  /** The CSS custom properties the stylesheet reads (app.css: html.bcv-themed). */
  /** How much of the seed the greys take, per mode: the ground, the cards, the hover, the secondary
   *  inks, the glass, the hairlines and the fills. (The Personalize preview casts its grounds the same.) */
  const CAST = {
    light: { bg: 0.12, card: 0.05, hover: 0.08, ink2: 0.16, ink3: 0.2, glass: 0.05, line: 0.5, fill: 0.5 },
    dark: { bg: 0.1, card: 0.11, hover: 0.1, ink2: 0.14, ink3: 0.2, glass: 0.1, line: 0.4, fill: 0.5 },
  };
  const GROUND_VARS = { bg: '--bcv-bg', card: '--bcv-card', hover: '--bcv-hover', ink2: '--bcv-ink2', ink3: '--bcv-ink3', sep: '--bcv-sep', edge: '--bcv-edge', fill: '--bcv-fill', fill2: '--bcv-fill2', chrome: '--bcv-chrome', glass: '--bcv-glass' };
  const cssVars = (p) => ({ '--bcv-accent': p.accent, '--bcv-accent-icon': p.icon, '--bcv-accent-text': p.text, '--bcv-accent-fill': p.fill, '--bcv-accent-hover': p.hover, '--bcv-accent-soft': p.soft, '--bcv-accent-ring': p.ring, ...Object.fromEntries(Object.entries(GROUND_VARS).map(([k, v]) => [v, p.ground[k]])) });
  const VAR_NAMES = ['--bcv-accent', '--bcv-accent-icon', '--bcv-accent-text', '--bcv-accent-fill', '--bcv-accent-hover', '--bcv-accent-soft', '--bcv-accent-ring', ...Object.values(GROUND_VARS)];
  /** Puts the accent on an element (the page's <html>): the variables for this mode and the class
   *  the stylesheet keys on. No accent: takes them off. */
  function apply(el, accent, dark) {
    const p = palette(accent, dark);
    if (!p) { for (const k of VAR_NAMES) el.style.removeProperty(k); el.classList.remove('bcv-themed'); return null; }
    for (const [k, v] of Object.entries(cssVars(p))) el.style.setProperty(k, v);
    el.classList.add('bcv-themed');
    return p;
  }

  /** A six-digit lower-case hex, or null for anything that is not one. */
  const normalize = (hex) => { const rgb = hexToRgb(hex); return rgb ? rgbToHex(rgb) : null; };
  /** Whether a seed is one the picker would offer: a colour (not grey), and standing 3:1 against
   *  both grounds as it is — so every shade drawn from it keeps its hue. */
  function readable(hex) {
    const rgb = hexToRgb(hex);
    if (!rgb) return false;
    const [, s] = rgbToHsl(rgb);
    return s >= MIN_SAT - 1e-9 && contrast(rgb, GROUND.light) >= ICON_RATIO && contrast(rgb, GROUND.dark) >= ICON_RATIO;
  }
  /** The band of lightness, for a hue and a saturation, within which the seed is readable:
   *  [deepest, lightest], as HSL lightness 0–1. Empty (lightest < deepest) for nothing. */
  function band(h, s) {
    let lo = null, hi = null;
    for (let i = 5; i <= 95; i++) {
      const rgb = hslToRgb([h, s, i / 100]);
      if (contrast(rgb, GROUND.light) >= ICON_RATIO && contrast(rgb, GROUND.dark) >= ICON_RATIO) { if (lo === null) lo = i / 100; hi = i / 100; }
    }
    return lo === null ? [0.4, 0.4] : [lo, hi];
  }
  /** The nearest readable seed to a colour: grey is given a quarter of saturation, and the
   *  lightness is moved to the edge of the band it fell outside. A readable colour is itself. */
  function nearest(hex) {
    const seed = normalize(hex);
    if (!seed) return null;
    if (readable(seed)) return seed;
    let [h, s, l] = rgbToHsl(hexToRgb(seed));
    s = Math.max(s, MIN_SAT);
    const [lo, hi] = band(h, s);
    l = clamp(l, lo, hi);
    return hslToHex([h, s, l]);
  }
  // a few starting points, each readable as it is
  // the themes on offer (the system's own colours); Regular is not one of them — it is the interface's
  // blue with every sidebar glyph in its own hue
  const PRESETS = [['#ff375f', 'Pink'], ['#ff453a', 'Red'], ['#ff9f0a', 'Amber'], ['#30d158', 'Green'], ['#40c8e0', 'Teal'], ['#5e5ce6', 'Indigo'], ['#bf5af2', 'Purple']];
  const REGULAR = '#0a84ff';
  /** A mix of two colours, t of the way from a to b. */
  const mix = (a, b, t) => { const A = hexToRgb(a) || [0, 0, 0], B = hexToRgb(b) || [0, 0, 0]; return rgbToHex(A.map((v, i) => v + (B[i] - v) * t)); };
  /** The colour as words on `on`: stepped towards black (a light ground) or white (a dark one), 4%
   *  at a time, until it reads 4.5:1 — readability enforced, not hoped for. */
  function readableOn(hex, on) {
    const toward = luminance(hexToRgb(on)) < 0.2 ? '#ffffff' : '#000000';
    for (let t = 0; t <= 1.0001; t += 0.04) { const c = mix(hex, toward, t); if (contrast(hexToRgb(c), on) >= 4.5) return c; }
    return toward;
  }
  /** The colour as a button with white words on it: darkened until they read 3:1. */
  function fillFor(hex) {
    for (let t = 0; t <= 1.0001; t += 0.04) { const c = mix(hex, '#000000', t); if (contrast(hexToRgb(c), '#ffffff') >= 3) return c; }
    return '#000000';
  }
  /** The tint: the colour at 14% by day, 22% by night, over whatever is under it. */
  const tint = (hex, dark) => `color-mix(in srgb, ${hex} ${dark ? 22 : 14}%, transparent)`;
  /** The custom colour from the picker's controls: hue, saturation, and a depth that maps to lightness (0.72 down to 0.32). */
  const customHex = (h, s, depth) => hslToHex([Number(h) || 0, Math.max(0, Math.min(1, Number(s) || 0)), (72 - Math.max(0, Math.min(100, Number(depth) || 0)) * 0.4) / 100]);
  /** The picker's controls for a colour (the depth clamped to what the picker reaches). */
  function controlsOf(hex) {
    const rgb = hexToRgb(hex);
    if (!rgb) return { h: 211, s: 1, depth: 40 };
    const [h, s, l] = rgbToHsl(rgb);
    return { h, s, depth: Math.max(0, Math.min(100, (72 - l * 100) / 0.4)) };
  }
  /** The sidebar's five shades of one accent: the accent, then lighter and deeper mixes of it. */
  const shadeSet = (A) => [A, mix(A, '#ffffff', 0.28), mix(A, '#000000', 0.22), mix(A, '#ffffff', 0.5), mix(A, '#000000', 0.4)];
  /** The colour a photo's veil is made of: the accent deepened by `k`, warmed by the photo's own tone where one is known. */
  const veilBase = (A, tone, k) => { const base = mix(A || REGULAR, '#000000', k); return tone ? mix(base, tone, 0.35) : base; };
  /** A kept photo as a CSS image: a data URL wrapped, a drawn one (a gradient) as it is. */
  const picCss = (v) => (!v ? 'none' : /^data:|^https?:|^blob:/.test(v) ? `url("${v}")` : v);

  // ---- the four drawn photos ---------------------------------------------------------------------
  // Drawn, not photographed: SVG scenes, so they are vector — crisp at any size on any screen, with
  // turbulence for cloud, water and grain rather than stripes — and a few kilobytes each, kept in
  // storage like a photo would be (a data URL) so the page treats them the same.
  const scene = (body, defs) => `data:image/svg+xml;charset=utf-8,${encodeURIComponent(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1600 900" preserveAspectRatio="xMidYMid slice"><defs>${defs}</defs>${body}</svg>`.replace(/\n\s*/g, ''))}`;
  // The four drawn scenes — Dusk, Ocean, Forest, Sand — each in nine variations (k = 0..8): lines and
  // shapes that ink well (ridges, rings, waves, rows of trees, dune contours, rays), no filters (they
  // rasterise once, fast). A variation moves the sun, turns the composition round for odd k, and
  // jitters the shapes from k, so a set of counters or headers each wears its own drawing of one scene.
  const rng = (seed) => { let s = (Math.imul(seed + 1, 2654435761) + 40503) >>> 0; return () => { s = (Math.imul(s ^ (s >>> 15), 2246822519) + 0x9e3779b9) >>> 0; return ((s >>> 8) & 0xffffff) / 0x1000000; }; };
  const turn = (k, inner) => (k % 2 ? `<g transform="translate(1600 0) scale(-1 1)">${inner}</g>` : inner);
  const SUN_X = [1180, 420, 800];
  const P = (x, y) => `${Math.round(x)} ${Math.round(y)}`;
  const ticks = (cx, cy, r1, r2, n) => Array.from({ length: n }, (_, i) => { const a = (i * 2 * Math.PI) / n; return `<line x1="${P(cx + r1 * Math.cos(a), cy + r1 * Math.sin(a)).replace(' ', '" y1="')}" x2="${P(cx + r2 * Math.cos(a), cy + r2 * Math.sin(a)).replace(' ', '" y2="')}"/>`; }).join('');
  const birdsAt = (spots, n, ink) => `<g stroke="${ink}" stroke-width="5" fill="none" stroke-linecap="round">${spots.slice(0, n).map(([x, y]) => `<path d="M${x} ${y}q20-22 40 0q20-22 40 0"/>`).join('')}</g>`;
  const DEFS = {
    dusk: '<linearGradient id="sky" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#1e1748"/><stop offset=".35" stop-color="#5f2f6b"/><stop offset=".55" stop-color="#c8645f"/><stop offset=".67" stop-color="#f4b06a"/></linearGradient><linearGradient id="sea" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#6a3358"/><stop offset=".5" stop-color="#2b1a42"/><stop offset="1" stop-color="#100b20"/></linearGradient>',
    ocean: '<linearGradient id="sky" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#cfeafc"/><stop offset=".55" stop-color="#7cc0f0"/></linearGradient>',
    forest: '<linearGradient id="sky" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#e8f5ec"/><stop offset=".55" stop-color="#b9e0c6"/></linearGradient>',
    sand: '<linearGradient id="sky" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#fdedd2"/><stop offset=".5" stop-color="#f6cd93"/></linearGradient>',
  };
  const SKY = '<rect width="1600" height="900" fill="url(#sky)"/>';
  const DRAW = {
    /** A sun low over the water inside rings of light, a ridge across the horizon dipping under it, a dark near ridge, its light in bars on the water, birds. */
    dusk(k) {
      const r = rng(k); const sx = SUN_X[k % 3], sy = 470 - Math.round(r() * 60);
      const rings = [140, 220, 310, 410].slice(0, 3 + (k % 2)).map((rad) => `<circle cx="${sx}" cy="${sy}" r="${rad}"/>`).join('');
      const pts = []; for (let x = 0; x <= 1600; x += 140) pts.push(P(x, Math.abs(x - sx) < 220 ? 565 + r() * 30 : 380 + r() * 170));
      const bars = [0, 1, 2, 3, 4, 5].map((i) => `<rect x="${Math.round(sx - 110 + r() * 60)}" y="${630 + i * 34}" width="${Math.round(120 + r() * 140)}" height="${6 + (i % 2) * 2}" rx="4"/>`).join('');
      const near = sx > 800 ? 'M0 700L120 640L260 690L420 620L560 680L680 650L780 720' : 'M1600 700L1480 640L1340 690L1180 620L1040 680L920 650L820 720';
      return scene(`${SKY}${turn(k, `<g stroke="#ffd9a6" stroke-opacity=".4" stroke-width="4" fill="none">${rings}</g><circle cx="${sx}" cy="${sy}" r="${80 + (k % 3) * 10}" fill="#ffe6b0"/>${birdsAt([[560, 250], [650, 300], [500, 330], [1100, 240]], 2 + (k % 3), '#2a1440')}<path d="M0 600L${pts.join('L')}L1600 600Z" fill="#3b1f4f"/><path d="M${pts.join('L')}" stroke="#ff9e7a" stroke-opacity=".7" stroke-width="4" fill="none"/><rect y="600" width="1600" height="300" fill="url(#sea)"/><rect y="598" width="1600" height="4" fill="#ffb98a" opacity=".8"/><g fill="#ffb27a" fill-opacity=".7">${bars}</g><path d="${near}V900H${sx > 800 ? 0 : 1600}Z" fill="#1a0d2a"/><path d="${near}" stroke="#6b3d78" stroke-width="4" fill="none"/>`)}`, DEFS.dusk);
    },
    /** A sun with short rays, rows of scalloped waves with a light crest each, a sailing boat, birds. */
    ocean(k) {
      const r = rng(k + 9); const sx = SUN_X[(k + 1) % 3], sy = 200 + Math.round(r() * 60);
      const amp = 40 + Math.round(r() * 16);
      const wave = (y, p, f) => { const d = `M${p} ${y} ${Array(9).fill(`q100 -${amp} 200 0`).join(' ')}`; return `<path d="${d} V900 H${p} Z" fill="${f}"/><path d="${d}" stroke="#eaf7ff" stroke-opacity=".85" stroke-width="5" fill="none"/>`; };
      const waves = [[500, '#5db8ee'], [590, '#3494dc'], [680, '#2273c2'], [770, '#164f9a'], [850, '#0d356e']].map(([y, f], i) => wave(y + Math.round(r() * 20 - 10), (i + k) % 2 ? -100 : 0, f)).join('');
      const bx = sx < 800 ? 1240 : 300;
      const boat = k % 3 === 1 ? '' : `<path d="M${bx} 420l60-150 30 150z" fill="#fff6d6"/><path d="M${bx + 105} 420l-40-110v110z" fill="#e6eef5"/><rect x="${bx - 20}" y="420" width="150" height="16" rx="6" fill="#2b5f8e"/>`;
      return scene(`${SKY}${turn(k, `<circle cx="${sx}" cy="${sy}" r="72" fill="#fff6d6"/><g stroke="#fff6d6" stroke-width="6" stroke-linecap="round">${ticks(sx, sy, 100, 132, 8)}</g>${birdsAt([[bx - 340, 180], [bx - 250, 240], [bx - 150, 200], [bx + 60, 150]], 2 + (k % 3), '#2b5f8e')}${boat}${waves}`)}`, DEFS.ocean);
    },
    /** A moon in a ring, hills with a light contour each, two rows of pines — small ones far, tall ones near — and the ground's lines. */
    forest(k) {
      const r = rng(k + 18); const mx = [1230, 300, 800][k % 3];
      const trees = (y, xs, w, hs, fill, trunk) => xs.map((x, i) => { const xx = Math.round(x + r() * 80 - 40), hh = Math.round(hs[i % hs.length] + r() * 40 - 20); return `<path d="M${xx} ${y}l${w} ${-hh}l${w} ${hh}z" fill="${fill}"/>${trunk ? `<rect x="${xx + w - 5}" y="${y}" width="10" height="${trunk}" fill="${fill}"/>` : ''}`; }).join('');
      const j = () => Math.round(r() * 50 - 25);
      const back = `M0 ${520 + j()}C260 ${430 + j()} 420 ${470 + j()} 640 ${500 + j()}S1000 ${560 + j()} 1200 ${480 + j()} 1460 ${420 + j()} 1600 ${470 + j()}`;
      const mid = `M0 ${640 + j()}C240 ${590 + j()} 480 ${630 + j()} 700 ${620 + j()}S1100 ${590 + j()} 1600 ${640 + j()}`;
      const front = `M0 ${780 + j()}C300 ${740 + j()} 600 ${800 + j()} 900 ${760 + j()}S1300 ${730 + j()} 1600 ${790 + j()}`;
      return scene(`${SKY}${turn(k, `<circle cx="${mx}" cy="190" r="86" fill="#fff9e0"/><circle cx="${mx}" cy="190" r="118" stroke="#fff9e0" stroke-opacity=".6" stroke-width="4" fill="none"/><path d="${back}V900H0Z" fill="#a6d5b4"/><path d="${back}" stroke="#fff" stroke-opacity=".6" stroke-width="4" fill="none"/>${trees(560, [40, 150, 250, 380, 470, 590, 700, 820, 910, 1040, 1150, 1270, 1380, 1500], 36, [110, 140, 90, 130, 100], '#4f9e6b')}<path d="${mid}V900H0Z" fill="#3f8a5c"/><path d="${mid}" stroke="#d6f0dd" stroke-opacity=".6" stroke-width="4" fill="none"/>${trees(700, [-20, 140, 300, 470, 640, 820, 990, 1160, 1330, 1500], 62, [190, 150, 220, 170], '#1f5a38', 24)}<path d="${front}V900H0Z" fill="#15402a"/><path d="${front}" stroke="#5fb07f" stroke-opacity=".7" stroke-width="4" fill="none"/><path d="${front.replace(/(\d+)(?=[CS ]|$)/g, (m) => String(Number(m) + 50)).replace(/^M0 (\d+)/, (m, y) => `M0 ${Number(y)}`)}" stroke="#5fb07f" stroke-opacity=".4" stroke-width="3" fill="none"/>`)}`, DEFS.forest);
    },
    /** A sun with a ring of ticks, wind in dashed lines, three dunes with a light crest and parallel contours each. */
    sand(k) {
      const r = rng(k + 27); const sx = SUN_X[(k + 2) % 3], sy = 230 + Math.round(r() * 50);
      const j = () => Math.round(r() * 40 - 20);
      const d1 = `M0 ${540 + j()}C300 ${470 + j()} 560 ${520 + j()} 820 ${470 + j()}S1300 ${420 + j()} 1600 ${480 + j()}`;
      const d2 = `M0 ${660 + j()}C260 ${600 + j()} 520 ${660 + j()} 800 ${610 + j()}S1240 ${570 + j()} 1600 ${640 + j()}`;
      const d3 = `M0 ${800 + j()}C240 ${740 + j()} 560 ${790 + j()} 900 ${750 + j()}S1320 ${720 + j()} 1600 ${780 + j()}`;
      const lower = (d, by) => d.replace(/(-?\d+) (-?\d+)/g, (m, x, y) => `${x} ${Number(y) + by}`);
      const contours = (d, off, n, ink) => Array.from({ length: n }, (_, i) => `<path d="${lower(d, off * (i + 1))}" stroke="${ink}" stroke-opacity=".55" stroke-width="3" fill="none"/>`).join('');
      const wx = sx > 800 ? 120 : 900;
      return scene(`${SKY}${turn(k, `<circle cx="${sx}" cy="${sy}" r="80" fill="#fff4d6"/><g stroke="#f5c27a" stroke-width="6" stroke-linecap="round">${ticks(sx, sy, 110, 150, 12)}</g><g stroke="#fff" stroke-opacity=".6" stroke-width="4" stroke-dasharray="30 22" stroke-linecap="round" fill="none"><path d="M${wx} 300q120-30 260 0t260 0"/><path d="M${wx + 100} 380q100-26 220 0t220 0"/>${k % 2 ? `<path d="M${wx + 40} 450q90-22 200 0t200 0"/>` : ''}</g><path d="${d1}V900H0Z" fill="#f1c88e"/>${contours(d1, 26, 2 + (k % 3), '#d9a05e')}<path d="${d2}V900H0Z" fill="#dfa161"/><path d="${d2}" stroke="#fff0cf" stroke-width="4" fill="none"/>${contours(d2, 30, 2 + ((k + 1) % 3), '#c48542')}<path d="${d3}V900H0Z" fill="#c07a3c"/><path d="${d3}" stroke="#ffe3b8" stroke-width="4" fill="none"/>${contours(d3, 32, 2, '#a9602f')}`)}`, DEFS.sand);
    },
  };
  const SCENE_VARIANTS = 9;
  const SCENE_TONES = { Dusk: '#b8527a', Ocean: '#3a8fd6', Forest: '#2f8f4e', Sand: '#e8a767' };
  const SCENE_NAMES = Object.keys(SCENE_TONES);
  // every drawing, built once — the first time one is asked for: 'Dusk' is the first variation,
  // 'Dusk#3' the fourth. (This file runs before every Canvas page paints; a page that shows no
  // scene never draws the thirty-six of them, a few hundred kilobytes of text.)
  const SCENE_URLS = new Map();
  const scenes = () => { if (!SCENE_URLS.size) for (const name of SCENE_NAMES) for (let k = 0; k < SCENE_VARIANTS; k++) SCENE_URLS.set(k ? `${name}#${k}` : name, DRAW[name.toLowerCase()](k)); return SCENE_URLS; };
  /** A scene's drawing: `name` alone is its first variation, or a variation k (wrapped round the nine). */
  const sceneUrl = (name, k = 0) => { const n = ((k % SCENE_VARIANTS) + SCENE_VARIANTS) % SCENE_VARIANTS; return scenes().get(n ? `${name}#${n}` : name) || null; };
  /** The drawing a scene key stands for ('Dusk', 'Dusk#3'), or null for anything else. */
  const sceneUrlOf = (key) => (key ? scenes().get(key) || null : null);
  /** [name, picture, tone]: the tone is what the veils warm to, as a read photo's would be. Built when first asked for. */
  let presetPhotos = null;
  const presetPhotosOf = () => (presetPhotos ||= SCENE_NAMES.map((n) => [n, sceneUrl(n), SCENE_TONES[n]]));

  // ---- the photos: read here, scaled here, kept here ------------------------------------------------
  /** Where a photo can go: the six counters of the Dashboard, and the sidebar. */
  const CARD_SLOTS = ['today', 'week', 'unread', 'overdue', 'tomorrow', 'graded'];
  /** The screens whose header can carry a photo (the route's screen key, and the title it shows). */
  const HEADER_SLOTS = [['dashboard', 'Dashboard'], ['courses', 'All Courses'], ['groups', 'Groups'], ['todo', 'To Do'], ['calendar', 'Calendar'], ['notifications', 'Notifications'], ['inbox', 'Inbox'], ['gpa', 'Grades'], ['tools', 'Tools']];
  const IMAGES_KEY = 'theme:images'; // storage.local: { side: dataURL | null, cards: { [slot]: dataURL }, headers: { [screen]: dataURL } } — never in the settings, which the Mac app carries
  /** A picture file scaled to fit `max` on its longer side and encoded as a JPEG data URL, so a
   *  phone's photo does not sit in storage at twelve megapixels. Needs a document (a content script). */
  /** An image element for a file or a data URL, once it has loaded. */
  function loadPicture(src, isFile) {
    return new Promise((resolve, reject) => {
      const url = isFile ? URL.createObjectURL(src) : src;
      const img = new Image();
      img.onload = () => { if (isFile) URL.revokeObjectURL(url); resolve(img); };
      img.onerror = () => { if (isFile) URL.revokeObjectURL(url); reject(new Error('The picture could not be read')); };
      img.src = url;
    });
  }
  /** The photo's own colour: its pixels averaged over a small copy, the vivid ones counting for
   *  more than the grey, so a photo of a sky gives its blue rather than the grey of its clouds.
   *  The blur fades into a mix of this and the ground (app.css), never into plain black or white. */
  function toneOf(img) {
    const cv = document.createElement('canvas');
    cv.width = 24; cv.height = 24;
    const cx = cv.getContext('2d');
    cx.drawImage(img, 0, 0, 24, 24);
    const d = cx.getImageData(0, 0, 24, 24).data;
    let r = 0, g = 0, b = 0, w = 0;
    for (let i = 0; i < d.length; i += 4) {
      const [, s, l] = rgbToHsl([d[i], d[i + 1], d[i + 2]]);
      const k = (0.2 + s) * (1 - Math.abs(l - 0.5) * 0.8); // vivid and mid-toned pixels count most
      r += d[i] * k; g += d[i + 1] * k; b += d[i + 2] * k; w += k;
    }
    return w ? rgbToHex([r / w, g / w, b / w]) : '#808080';
  }
  /** A file scaled to `max` on its longer side as a JPEG data URL, with its tone. */
  async function readImage(file, max = 1280, quality = 0.84) {
    if (!file || !/^image\//.test(file.type)) throw new Error('Not a picture');
    const img = await loadPicture(file, true);
    const scale = Math.min(1, max / Math.max(img.naturalWidth, img.naturalHeight));
    const cv = document.createElement('canvas');
    cv.width = Math.max(1, Math.round(img.naturalWidth * scale));
    cv.height = Math.max(1, Math.round(img.naturalHeight * scale));
    cv.getContext('2d').drawImage(img, 0, 0, cv.width, cv.height);
    return { data: cv.toDataURL('image/jpeg', quality), tone: toneOf(img) };
  }
  /** The tone of a photo already kept (a data URL). */
  const imageTone = (data) => loadPicture(data, false).then(toneOf);
  // the keys the tones are kept under: 'side', a counter's slot, 'head:<screen>'
  const toneKeys = (images) => [...(images?.side ? ['side'] : []), ...Object.keys(images?.cards || {}).filter((k) => images.cards[k]), ...Object.keys(images?.headers || {}).filter((k) => images.headers[k]).map((k) => `head:${k}`)];
  const imageAt = (images, key) => (key === 'side' ? images.side : key.startsWith('head:') ? images.headers?.[key.slice(5)] : images.cards?.[key]);
  /** Photos kept before tones were (2.60–2.63) get theirs read now, and saved; the images come back with them. */
  async function fillTones(images) {
    if (!images) return images;
    images.tones = images.tones || {};
    const missing = toneKeys(images).filter((key) => !images.tones[key]);
    // pictures kept before assets are packed now, once — and a set that would not pack (a picture
    // that will not draw) is not packed again on every page load, each time rewriting the whole set
    if (!missing.length && !(hasRaw(images) && !images.packStuck)) return images;
    await ensureAssets(images); // every slot's picture: the tones are read off them, and a save packs them all
    let added = 0;
    for (const key of missing) {
      try { images.tones[key] = await imageTone(picOf(images, imageAt(images, key))?.sharp); added++; } catch { /* left without a tone: the ground colour serves */ }
    }
    if (added || (hasRaw(images) && !images.packStuck)) await saveImages(images).catch(() => {});
    return images;
  }
  // ---- kept as assets --------------------------------------------------------------------------
  // A photo is kept once however many places wear it: a slot holds `asset:<id>` and `assets[id]`
  // holds the picture — a raster (a drawn scene is rasterised here, once, so the page never renders
  // SVG filters) and a small pre-blurred copy of it (so the page draws its blur as a picture scaled
  // up, not as a filter: filtered layers are what cost Safari the memory and the frames) and, for a
  // scene, the scene's name, so Personalize can show it as the scene it is.
  const ASSET = /^asset:/;
  const hashOf = (str) => { let h = 2166136261; const step = Math.max(1, Math.floor(str.length / 4096)); for (let i = 0; i < str.length; i += step) { h ^= str.charCodeAt(i); h = Math.imul(h, 16777619); } return (h >>> 0).toString(16) + str.length.toString(36); };
  /** The scene key a raw drawing is ('Dusk', 'Dusk#3'), or null for a photo. */
  const sceneNameOf = (v) => { if (!v || !v.startsWith('data:image/svg+xml')) return null; for (const [key, url] of scenes()) if (url === v) return key; return null; };
  /** A blur over one channel of RGBA pixels (the alpha of a mask), clamped at the edges. */
  function boxBlurAlpha(d, w, h, r) {
    const tmp = new Float32Array(w * h);
    const n = 2 * r + 1;
    for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) { let A = 0; for (let k = -r; k <= r; k++) A += d[(y * w + Math.min(w - 1, Math.max(0, x + k))) * 4 + 3]; tmp[y * w + x] = A / n; }
    for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) { let A = 0; for (let k = -r; k <= r; k++) A += tmp[Math.min(h - 1, Math.max(0, y + k)) * w + x]; const o = (y * w + x) * 4; d[o] = 255; d[o + 1] = 255; d[o + 2] = 255; d[o + 3] = A / n; }
  }
  /** Otsu's threshold over a grey image (0–255): the split that keeps the two tones most apart. */
  function otsu(g) {
    const hist = new Float64Array(256);
    for (let i = 0; i < g.length; i++) hist[Math.min(255, Math.max(0, Math.round(g[i])))]++;
    const total = g.length;
    let sum = 0; for (let t = 0; t < 256; t++) sum += t * hist[t];
    let sumB = 0, wB = 0, best = 0, T = 128;
    for (let t = 0; t < 256; t++) {
      wB += hist[t]; if (!wB) continue;
      const wF = total - wB; if (!wF) break;
      sumB += t * hist[t];
      const mB = sumB / wB, mF = (sum - sumB) / wF;
      const v = wB * wF * (mB - mF) * (mB - mF);
      if (v > best) { best = v; T = t; }
    }
    return T;
  }
  /** A photo inked down to two tones: the darker half of it (Otsu's split) and its outlines (Sobel)
   *  are the ink, the rest the paper — kept as a mask (white where the ink goes, clear elsewhere)
   *  the page colours in the two inks of the moment, with a small blurred copy for the blurred layer. */
  function inkOf(img, w = 800) {
    const ratio = img.naturalWidth ? img.naturalHeight / img.naturalWidth : 0.5625;
    const h = Math.max(8, Math.round(w * ratio));
    const cv = document.createElement('canvas'); cv.width = w; cv.height = h;
    const cx = cv.getContext('2d');
    cx.drawImage(img, 0, 0, w, h);
    const src = cx.getImageData(0, 0, w, h).data;
    const n = w * h;
    const lum = new Float32Array(n);
    for (let i = 0; i < n; i++) lum[i] = 0.2126 * src[i * 4] + 0.7152 * src[i * 4 + 1] + 0.0722 * src[i * 4 + 2];
    // a light blur first: grain is not an outline
    const sm = new Float32Array(n);
    for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) { let a = 0; for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++) a += lum[Math.min(h - 1, Math.max(0, y + dy)) * w + Math.min(w - 1, Math.max(0, x + dx))]; sm[y * w + x] = a / 9; }
    const T = otsu(sm);
    const out = cx.createImageData(w, h);
    const d = out.data;
    for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
      const i = y * w + x;
      const x0 = Math.max(0, x - 1), x1 = Math.min(w - 1, x + 1), y0 = Math.max(0, y - 1), y1 = Math.min(h - 1, y + 1);
      const gx = -sm[y0 * w + x0] - 2 * sm[y * w + x0] - sm[y1 * w + x0] + sm[y0 * w + x1] + 2 * sm[y * w + x1] + sm[y1 * w + x1];
      const gy = -sm[y0 * w + x0] - 2 * sm[y0 * w + x] - sm[y0 * w + x1] + sm[y1 * w + x0] + 2 * sm[y1 * w + x] + sm[y1 * w + x1];
      const edge = Math.min(255, Math.max(0, (Math.hypot(gx, gy) - 22) * 3.5)); // (gentle horizons and dune lines count too)
      const deep = T * 0.8; // the darkest part alone is filled, not the whole darker half: outlines carry the rest, and the paper stays
      const shape = sm[i] < deep ? Math.min(255, (deep - sm[i]) * 24) : 0; // (soft at the split, so the shapes are not jagged)
      d[i * 4] = 255; d[i * 4 + 1] = 255; d[i * 4 + 2] = 255; d[i * 4 + 3] = Math.max(shape, edge);
    }
    cx.putImageData(out, 0, 0);
    const ink = cv.toDataURL('image/png');
    // the blurred copy: the mask drawn small and blurred twice — scaled up by the page, it is the soft side
    const bw = 96, bh = Math.max(8, Math.round(bw * ratio));
    const bc = document.createElement('canvas'); bc.width = bw; bc.height = bh;
    const bx = bc.getContext('2d');
    bx.drawImage(cv, 0, 0, bw, bh);
    const bd = bx.getImageData(0, 0, bw, bh);
    boxBlurAlpha(bd.data, bw, bh, 3); boxBlurAlpha(bd.data, bw, bh, 3);
    bx.putImageData(bd, 0, 0);
    return { ink, inkBlur: bc.toDataURL('image/png') };
  }
  /** The ink of a raw picture (a data URL: an upload or a drawn scene), computed once and kept in memory, keyed by the picture. */
  const inks = new Map();
  const inking = new Map(); // in hand: the same picture asked for twice at once is inked once
  const inkCached = (v) => (v ? inks.get(hashOf(v)) || null : null);
  async function inkFor(v) {
    if (!v) return null;
    const id = hashOf(v);
    if (inks.has(id)) return inks.get(id);
    if (inking.has(id)) return inking.get(id);
    const p = loadPicture(v, false).then((img) => { const r = inkOf(img); inks.set(id, r); inking.delete(id); return r; }).catch((e) => { inking.delete(id); throw e; });
    inking.set(id, p);
    return p;
  }
  /** A drawn scene as a picture, rasterised once at a size that stays crisp on a wide header. */
  async function rasterOf(svgUrl, w = 1600) {
    const img = await loadPicture(svgUrl, false);
    const h = Math.round((w * (img.naturalHeight || 900)) / (img.naturalWidth || 1600));
    const cv = document.createElement('canvas'); cv.width = w; cv.height = h;
    cv.getContext('2d').drawImage(img, 0, 0, w, h);
    return { data: cv.toDataURL('image/jpeg', 0.86), img };
  }
  /** Every raw picture in `images` (a data URL: an upload, or a drawn scene) becomes an asset, kept once;
   *  assets nothing wears any more go. Needs a document (a canvas): elsewhere the pictures are kept as they are. */
  async function packImages(images) {
    const out = { side: null, cards: {}, headers: {}, tones: { ...(images?.tones || {}) }, assets: { ...(images?.assets || {}) } };
    const canDraw = typeof document !== 'undefined' && !!document.createElement;
    const put = async (v) => {
      if (!v) return null;
      if (ASSET.test(v)) {
        const a = out.assets[v.slice(6)];
        if (!a) return null;
        if (a.ink || a.inkFailed || !canDraw) return v;
        try { const raw = a.scene ? sceneUrlOf(a.scene) || a.sharp : a.sharp; const { ink, inkBlur } = await inkFor(raw); delete a.blur; Object.assign(a, { ink, inkBlur }); } catch { a.inkFailed = true; } // (a picture that will not ink is kept plain, and not tried again on every page)
        return v;
      }
      if (!canDraw) return v;
      const id = hashOf(v);
      if (!out.assets[id]?.ink) {
        try {
          const scene = sceneNameOf(v);
          let sharp = v;
          if (/^data:image\/svg\+xml/.test(v)) sharp = (await rasterOf(v)).data;
          const { ink, inkBlur } = await inkFor(v);
          out.assets[id] = { sharp, ink, inkBlur, ...(scene ? { scene } : {}) };
        } catch { return v; } // (a picture that will not draw is kept as it is: the page shows it plain)
      }
      return `asset:${id}`;
    };
    out.side = await put(images?.side);
    for (const [k, v] of Object.entries(images?.cards || {})) { const a = await put(v); if (a) out.cards[k] = a; }
    for (const [k, v] of Object.entries(images?.headers || {})) { const a = await put(v); if (a) out.headers[k] = a; }
    const used = new Set([out.side, ...Object.values(out.cards), ...Object.values(out.headers)].filter((v) => v && ASSET.test(v)).map((v) => v.slice(6)));
    for (const id of Object.keys(out.assets)) if (!used.has(id)) delete out.assets[id];
    return out;
  }
  /** What a slot wears, resolved: { sharp, blur, scene } — a picture kept raw (from before assets) has no blur, and the page blurs it itself. */
  const picOf = (images, v) => {
    if (!v) return null;
    if (ASSET.test(v)) { const a = images?.assets?.[v.slice(6)]; return a?.sharp ? { sharp: a.sharp, ink: a.ink || null, inkBlur: a.inkBlur || null, scene: a.scene || null } : null; }
    return { sharp: v, ink: null, inkBlur: null, scene: sceneNameOf(v) };
  };
  /** The raw value a slot stands for, for editing: a scene's own drawing, or the picture itself. */
  const rawOf = (images, v) => { const p = picOf(images, v); if (!p) return null; return p.scene ? sceneUrlOf(p.scene) || p.sharp : p.sharp; };
  /** Is anything still raw — a picture not packed into an asset, or an asset in hand that has no ink and
   *  did not fail to take one? (an asset not read into memory is taken as packed: it was, to be kept) */
  const hasRaw = (images) => [images?.side, ...Object.values(images?.cards || {}), ...Object.values(images?.headers || {})].some((v) => { if (!v) return false; if (!ASSET.test(v)) return true; const a = images?.assets?.[v.slice(6)]; return !!a && !a.ink && !a.inkFailed; });

  const emptyImages = () => ({ side: null, cards: {}, headers: {}, tones: {}, assets: {} });
  // ---- kept in storage: the index under theme:images, the pictures under a key each --------------
  // A tab used to read the whole set at boot — every picture, sharp and inked, for every slot: a few
  // megabytes held by every Canvas tab for the one or two pictures its page shows. The index says
  // what each slot wears and lists the pictures kept; the pictures live under their own keys and
  // are read as a page needs them (the sidebar's, this screen's header's, the Dashboard's counters').
  const ASSET_KEY = (id) => `theme:asset:${id}`;
  /** The value a slot wears: 'side', 'card:<slot>' (or the bare slot), 'head:<screen>'. */
  const slotValue = (images, slot) => (slot === 'side' ? images?.side : slot.startsWith('head:') ? images?.headers?.[slot.slice(5)] : images?.cards?.[slot.startsWith('card:') ? slot.slice(5) : slot]);
  const allSlots = (images) => ['side', ...Object.keys(images?.cards || {}).map((k) => `card:${k}`), ...Object.keys(images?.headers || {}).map((k) => `head:${k}`)];
  /** The pictures the slots wear, read into `images.assets` where they are not there yet (no slots: every slot's). */
  async function ensureAssets(images, slots = null) {
    if (!images) return images;
    images.assets = images.assets || {};
    const want = new Set();
    for (const s of slots || allSlots(images)) { const v = slotValue(images, s); if (v && ASSET.test(v) && !images.assets[v.slice(6)]) want.add(v.slice(6)); }
    if (!want.size) return images;
    try {
      const r = await BCV.api.storage.local.get([...want].map(ASSET_KEY));
      for (const id of want) { const a = r?.[ASSET_KEY(id)]; if (a && typeof a === 'object') images.assets[id] = a; }
    } catch { /* drawn without them */ }
    return images;
  }
  /** The index as written: the slots, the tones, the ids of the pictures kept (those go under their keys). */
  const indexOf = (p) => ({ side: p.side, cards: p.cards, headers: p.headers, tones: Object.fromEntries(toneKeys(p).filter((k) => p.tones?.[k]).map((k) => [k, p.tones[k]])), assetIds: Object.keys(p.assets || {}), ...(hasRaw(p) ? { packStuck: true } : {}) });
  /** The set, with the pictures of `need` (an array of slots) read in — or of every slot ('all'). */
  async function loadImages({ need = 'all' } = {}) {
    try {
      const r = await BCV.api.storage.local.get(IMAGES_KEY);
      const v = r?.[IMAGES_KEY];
      if (!v || typeof v !== 'object') return emptyImages();
      const images = { side: v.side || null, cards: { ...(v.cards || {}) }, headers: { ...(v.headers || {}) }, tones: { ...(v.tones || {}) }, assets: {}, ...(v.packStuck ? { packStuck: true } : {}) };
      if (v.assets && typeof v.assets === 'object' && Object.keys(v.assets).length) {
        // kept as one blob until 2.90: the pictures are moved out to a key each, once
        images.assets = { ...v.assets };
        try {
          await BCV.api.storage.local.set(Object.fromEntries(Object.entries(v.assets).map(([id, a]) => [ASSET_KEY(id), a])));
          await BCV.api.storage.local.set({ [IMAGES_KEY]: indexOf(images) });
        } catch { /* read whole next time too */ }
        return images;
      }
      return await ensureAssets(images, need === 'all' ? null : need);
    } catch { return emptyImages(); }
  }
  /** Writes the set: every raw picture packed into an asset, the pictures under their keys first and the
   *  index last (a tab that reads the index finds its pictures there already), pictures nothing wears any more removed. */
  async function saveImages(images) {
    const p = await packImages(images || emptyImages());
    let before = [];
    try { before = (await BCV.api.storage.local.get(IMAGES_KEY))?.[IMAGES_KEY]?.assetIds || []; } catch { /* none known */ }
    const ids = Object.keys(p.assets);
    if (ids.length) await BCV.api.storage.local.set(Object.fromEntries(ids.map((id) => [ASSET_KEY(id), p.assets[id]])));
    await BCV.api.storage.local.set({ [IMAGES_KEY]: indexOf(p) });
    const gone = before.filter((id) => !ids.includes(id));
    if (gone.length) await BCV.api.storage.local.remove(gone.map(ASSET_KEY)).catch(() => {});
  }
  BCV.theme = {
    hexToRgb, rgbToHex, rgbToHsl, hslToRgb, hslToHex, luminance, contrast, normalize,
    GROUND, MIN_SAT, ICON_RATIO, PRESETS, REGULAR, SCENE_VARIANTS, sceneUrl, sceneNameOf, CARD_SLOTS, HEADER_SLOTS, IMAGES_KEY,
    palette, shades, shadeSet, cssVars, apply, readable, readableOn, fillFor, mix, tint, customHex, controlsOf, veilBase, picCss, band, nearest,
    readImage, imageTone, fillTones, loadImages, ensureAssets, saveImages, emptyImages, packImages, picOf, rawOf, CAST, INK_LIFT, inkOn, inkFor, inkCached,
    get PRESET_PHOTOS() { return presetPhotosOf(); }, // (the drawings, made on the first ask)
  };
})();
