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
  /** A drawn scene (a ready-made theme's) is inked in the look's own colour instead: the accent mixed
   *  into the surface — a third of it by day, a little more by night — its dark masses a light wash
   *  of that (inkOf's fill), so the drawing reads as the theme's own (app.css .is-scene says the same). */
  const SCENE_INK = { light: 0.3, dark: 0.34 };
  const SCENE_FILL = 0.42;
  const sceneInkOn = (paper, accent, dark) => mix(paper, accent || '#0a84ff', SCENE_INK[dark ? 'dark' : 'light']);
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

  // ---- the six drawn scenes, each drawn to the three places' own shapes -------------------------
  // Drawn, not photographed: SVG, so they are vector — crisp at any size on any screen — and a few
  // kilobytes each, kept in storage like a photo would be (a data URL) so the page treats them the
  // same. Each scene is drawn three times, to the shape of the place it goes on (2.98.52): the
  // sidebar's tall strip (450×1600), a counter's wide band (1600×420) and the header's long one
  // (3200×330) — so the whole drawing shows, never a slice cut from a wide picture — and with a few
  // large shapes each (a sun, a ridge, a row of waves, a skyline), lines that ink well, nothing
  // small and busy. A counter's drawing comes in nine variations (the sun moved, the composition
  // turned, the shapes jittered from a seed) so six counters each wear their own; the sidebar's and
  // the header's in three. A key names one: 'Dusk@card', 'Dusk@card#3', 'Dusk@side', 'Dusk@head'.
  const PLACES = ['side', 'card', 'head'];
  const FRAME = { side: [450, 1600], card: [1600, 420], head: [3200, 330], back: [1600, 1000] }; // (back: the apps' window, behind their glass — Simpl for Mac 1.3)
  const SCENE_VARIANTS = { side: 3, card: 9, head: 3 };
  const scene = (place, body, defs) => { const [w, h] = FRAME[place]; return `data:image/svg+xml;charset=utf-8,${encodeURIComponent(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${w} ${h}" preserveAspectRatio="xMidYMid slice"><defs>${defs}</defs><rect width="${w}" height="${h}" fill="url(#sky)"/>${body}</svg>`.replace(/\n\s*/g, ''))}`; };
  const rng = (seed) => { let s = (Math.imul(seed + 1, 2654435761) + 40503) >>> 0; return () => { s = (Math.imul(s ^ (s >>> 15), 2246822519) + 0x9e3779b9) >>> 0; return ((s >>> 8) & 0xffffff) / 0x1000000; }; };
  const turn = (k, w, inner) => (k % 2 ? `<g transform="translate(${w} 0) scale(-1 1)">${inner}</g>` : inner);
  const P = (x, y) => `${Math.round(x)} ${Math.round(y)}`;
  const ticks = (cx, cy, r1, r2, n) => Array.from({ length: n }, (_, i) => { const a = (i * 2 * Math.PI) / n; return `<line x1="${P(cx + r1 * Math.cos(a), cy + r1 * Math.sin(a)).replace(' ', '" y1="')}" x2="${P(cx + r2 * Math.cos(a), cy + r2 * Math.sin(a)).replace(' ', '" y2="')}"/>`; }).join('');
  /** A cloud: three bumps on a flat base, outlined. */
  const cloud = (x, y, w, fill, line) => { const u = w / 10; return `<path d="M${x} ${y}h${w}q0-${u * 2.2}-${u * 2.4}-${u * 2.2}q-${u * 0.6}-${u * 2.8}-${u * 3.2}-${u * 2.4}q-${u * 1.6}-${u * 1.8}-${u * 3.2}${u * 0.4}q-${u * 2.2}0-${u * 1.6}${u * 4.2}z" fill="${fill}" stroke="${line}" stroke-width="4" stroke-linejoin="round"/>`; };
  /** A row of buildings from x0 to x1 standing on `base`, some of their windows lit. */
  const skyline = (r, x0, x1, base, [lo, hi], fill, win, p = 0.5) => { let s = '', x = x0; while (x < x1) { const w = 46 + Math.round(r() * 58), hh = lo + Math.round(r() * (hi - lo)); s += `<rect x="${x}" y="${base - hh}" width="${w}" height="${hh + 2}" fill="${fill}"/>`; if (r() > 0.6) s += `<rect x="${x + w / 2 - 3}" y="${base - hh - 26}" width="6" height="28" fill="${fill}"/>`; if (win) for (let wy = base - hh + 16; wy < base - 18; wy += 24) for (let wx = x + 9; wx < x + w - 12; wx += 17) if (r() > p) s += `<rect x="${wx}" y="${wy}" width="8" height="11" fill="${win}"/>`; x += w + 3 + Math.round(r() * 12); } return s; };
  /** A range of peaks along `base`: each a triangle with a snow cap and a shaded flank. */
  const peaks = (r, xs, base, [lo, hi], fill, shade, snow) => xs.map((x) => { const hh = lo + Math.round(r() * (hi - lo)), w = hh * (0.9 + r() * 0.5), top = base - hh, cap = hh * 0.26; return `<path d="M${Math.round(x - w)} ${base}L${Math.round(x)} ${top}L${Math.round(x + w)} ${base}Z" fill="${fill}"/><path d="M${Math.round(x)} ${top}L${Math.round(x + w)} ${base}L${Math.round(x + w * 0.2)} ${base}Z" fill="${shade}"/>${snow ? `<path d="M${Math.round(x - cap * 0.95)} ${Math.round(top + cap)}L${Math.round(x)} ${top}L${Math.round(x + cap * 0.95)} ${Math.round(top + cap)}l-${Math.round(cap * 0.35)}-${Math.round(cap * 0.3)}l-${Math.round(cap * 0.3)} ${Math.round(cap * 0.35)}l-${Math.round(cap * 0.3)}-${Math.round(cap * 0.35)}Z" fill="${snow}"/>` : ''}`; }).join('');
  const pines = (r, y, xs, w, hs, fill) => xs.map((x, i) => { const xx = Math.round(x + r() * 60 - 30), hh = Math.round(hs[i % hs.length] + r() * 30 - 15); return `<path d="M${xx} ${y}l${w} ${-hh * 0.45}l${-w * 0.45} 0l${w * 0.9} ${-hh * 0.3}l${-w * 0.4} 0l${w * 0.55} ${-hh * 0.25}l${w * 0.55} ${hh * 0.25}l${-w * 0.4} 0l${w * 0.9} ${hh * 0.3}l${-w * 0.45} 0l${w} ${hh * 0.45}z" fill="${fill}"/>`; }).join('');
  /** A lighthouse on its rocky point, its light thrown both ways: drawn about its base at (x, base), scaled by s. */
  const lighthouse = (x, base, s) => `<g transform="translate(${x} ${base}) scale(${s})"><path d="M0 -266L-260 -316L-260 -216Z" fill="#fff6d6" fill-opacity=".5"/><path d="M0 -266L260 -316L260 -216Z" fill="#fff6d6" fill-opacity=".5"/><path d="M-130 0C-90 -100 80 -110 150 0Z" fill="#35556f"/><path d="M-34 -80L-24 -250H24L34 -80Z" fill="#f3f7fb"/><path d="M-31 -120H31V-150H-29ZM-28 -190H28V-220H-27Z" fill="#d6455a"/><rect x="-30" y="-284" width="60" height="36" rx="4" fill="#fff6d6"/><path d="M-38 -284L0 -314L38 -284Z" fill="#d6455a"/></g>`;
  /** A pyramid at x standing on `base`, its lit face and its shaded one, courses across it. */
  const pyramid = (x, base, w, hh) => `<path d="M${x - w} ${base}L${x} ${base - hh}L${x + w} ${base}Z" fill="#e7b577"/><path d="M${x} ${base - hh}L${x + w} ${base}H${x + w * 0.15}Z" fill="#c98b4c"/><g stroke="#b77a3f" stroke-opacity=".6" stroke-width="3">${[0.3, 0.55, 0.8].map((t) => `<path d="M${Math.round(x - w * t)} ${Math.round(base - hh * (1 - t))}H${Math.round(x + w * t)}"/>`).join('')}</g>`;
  const cactus = (x, y, hh) => `<g fill="#6f7a3a"><rect x="${x - 14}" y="${y - hh}" width="28" height="${hh}" rx="14"/><path d="M${x - 14} ${y - hh * 0.45}h-30a14 14 0 0 1-14-14v-${Math.round(hh * 0.25)}a12 12 0 0 1 24 0v${Math.round(hh * 0.12)}h20z"/><path d="M${x + 14} ${y - hh * 0.6}h26a14 14 0 0 0 14-14v-${Math.round(hh * 0.18)}a12 12 0 0 0-24 0v${Math.round(hh * 0.06)}h-16z"/></g>`;
  /** Where the sun (or the moon) goes: one of three places along the width, by the variation. */
  const sunX = (k, W) => [Math.round(W * 0.82), Math.round(W * 0.2), Math.round(W * 0.5)][k % 3];
  const DEFS = {
    dusk: '<linearGradient id="sky" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#1e1748"/><stop offset=".35" stop-color="#5f2f6b"/><stop offset=".55" stop-color="#c8645f"/><stop offset=".67" stop-color="#f4b06a"/></linearGradient><linearGradient id="sea" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#6a3358"/><stop offset=".5" stop-color="#2b1a42"/><stop offset="1" stop-color="#100b20"/></linearGradient>',
    ocean: '<linearGradient id="sky" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#cfeafc"/><stop offset=".55" stop-color="#7cc0f0"/></linearGradient>',
    forest: '<linearGradient id="sky" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#e8f5ec"/><stop offset=".55" stop-color="#b9e0c6"/></linearGradient>',
    sand: '<linearGradient id="sky" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#fdedd2"/><stop offset=".5" stop-color="#f6cd93"/></linearGradient>',
    peaks: '<linearGradient id="sky" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#e9e6fb"/><stop offset=".6" stop-color="#fbe3d6"/></linearGradient>',
    city: '<linearGradient id="sky" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#dff3f1"/><stop offset=".6" stop-color="#fde6cf"/></linearGradient>',
  };
  const DRAW = {
    /** A low sun in two rings of light, a far ridge, a shore of lit windows across from it, the light in bars on the water. */
    dusk(k, place) {
      const r = rng(k); const [W, H] = FRAME[place];
      const L = { side: { sun: [sunX(k, W), 980, 100], hz: 1120, city: [40, 410, 30, 90], bars: 4, ripples: 5 }, card: { sun: [sunX(k, W), 175, 80], hz: 262, city: null, bars: 3, ripples: 2 }, head: { sun: [sunX(k, W), 150, 70], hz: 232, city: [200, 900, 30, 110], bars: 3, ripples: 3 }, back: { sun: [sunX(k, W), 470, 110], hz: 640, city: [200, 900, 40, 170], bars: 4, ripples: 4 } }[place];
      const [sx, sy, sr] = L.sun;
      const pts = []; for (let x = 0; x <= W; x += Math.round(W / 8)) pts.push(P(x, Math.abs(x - sx) < sr * 2.2 ? L.hz - 8 + r() * 10 : L.hz - 40 - r() * 60));
      const ridge = `<path d="M0 ${L.hz}L${pts.join('L')}L${W} ${L.hz}Z" fill="#3b1f4f"/><path d="M${pts.join('L')}" stroke="#ff9e7a" stroke-opacity=".7" stroke-width="4" fill="none"/>`;
      const city = L.city ? (() => { const [c0, c1, lo, hi] = L.city; const far = sx > W / 2; return skyline(r, far ? c0 : W - c1, far ? c1 : W - c0, L.hz + 2, [lo, hi], '#24123a', '#ffcf8a', 0.62); })() : '';
      const gap = Math.round((H - L.hz - 40) / (L.bars + 1));
      const bars = Array.from({ length: L.bars }, (_, i) => `<rect x="${Math.round(sx - sr * 1.1 + r() * sr * 0.5)}" y="${L.hz + 30 + i * gap}" width="${Math.round(sr * 1.3 + r() * sr)}" height="${6 + (i % 2) * 2}" rx="4"/>`).join('');
      const ripples = Array.from({ length: L.ripples }, (_, i) => `<path d="M${Math.round(r() * (W - 200))} ${L.hz + 50 + Math.round(((i + 0.5) * (H - L.hz - 60)) / L.ripples)}h${Math.round(60 + r() * 120)}"/>`).join('');
      return scene(place, turn(k, W, `<g stroke="#ffd9a6" stroke-opacity=".4" stroke-width="4" fill="none"><circle cx="${sx}" cy="${sy}" r="${Math.round(sr * 1.5)}"/><circle cx="${sx}" cy="${sy}" r="${Math.round(sr * 2.1)}"/></g><circle cx="${sx}" cy="${sy}" r="${sr}" fill="#ffe6b0"/>${ridge}${city}<rect y="${L.hz}" width="${W}" height="${H - L.hz}" fill="url(#sea)"/><rect y="${L.hz - 2}" width="${W}" height="4" fill="#ffb98a" opacity=".8"/><g fill="#ffb27a" fill-opacity=".7">${bars}</g><g stroke="#ffb27a" stroke-opacity=".35" stroke-width="3" stroke-linecap="round">${ripples}</g>`), DEFS.dusk);
    },
    /** A sun with short rays, a lighthouse on its point across from it throwing its light, rows of scalloped waves with a crest each, a boat on the long one. */
    ocean(k, place) {
      const r = rng(k + 9); const [W, H] = FRAME[place];
      const L = { side: { sun: [sunX(k + 1, W), 300, 70], waves: [820, 130, 6], amp: 46, lh: [W / 2, 780, 1], boat: false }, card: { sun: [sunX(k + 1, W), 130, 60], waves: [250, 70, 3], amp: 30, lh: [null, 262, 0.55], boat: false }, head: { sun: [sunX(k + 1, W), 120, 55], waves: [200, 60, 3], amp: 28, lh: [null, 214, 0.6], boat: true }, back: { sun: [sunX(k + 1, W), 250, 80], waves: [560, 90, 5], amp: 40, lh: [null, 600, 1], boat: true } }[place];
      const [sx, sy, sr] = L.sun;
      const lx = L.lh[0] ?? (sx > W / 2 ? Math.round(W * 0.14) : Math.round(W * 0.86)); // the lighthouse, across from the sun
      const wave = (y, phase, fill) => { const d = `M${phase} ${y} ${Array(Math.ceil(W / 200) + 1).fill(`q100 -${L.amp} 200 0`).join(' ')}`; return `<path d="${d} V${H} H${phase} Z" fill="${fill}"/><path d="${d}" stroke="#eaf7ff" stroke-opacity=".85" stroke-width="5" fill="none"/>`; };
      const fills = ['#5db8ee', '#3494dc', '#2273c2', '#164f9a', '#0d356e', '#082548'];
      const waves = Array.from({ length: L.waves[2] }, (_, i) => wave(L.waves[0] + i * L.waves[1] + Math.round(r() * 16 - 8), (i + k) % 2 ? -100 : 0, fills[i])).join('');
      const boat = L.boat && k % 3 !== 1 ? (() => { const bx = sx > W / 2 ? 700 : W - 800, by = L.waves[0] - 6; return `<path d="M${bx} ${by}l50-120 24 120z" fill="#fff6d6"/><rect x="${bx - 16}" y="${by}" width="120" height="14" rx="6" fill="#2b5f8e"/>`; })() : '';
      return scene(place, turn(k, W, `<circle cx="${sx}" cy="${sy}" r="${sr}" fill="#fff6d6"/><g stroke="#fff6d6" stroke-width="6" stroke-linecap="round">${ticks(sx, sy, sr * 1.4, sr * 1.85, 8)}</g>${lighthouse(lx, L.lh[1], L.lh[2])}${boat}${waves}`), DEFS.ocean);
    },
    /** A moon in a ring, a far range with snow, a hill with a light contour, a row of pines standing on the dark ground — and a lake at the sidebar's foot. */
    forest(k, place) {
      const r = rng(k + 18); const [W, H] = FRAME[place];
      const L = { side: { moon: [sunX(k, W), 260, 70], peaks: [[60, 225, 400], 720, [180, 280]], hill: 860, pines: [1180, [30, 130, 240, 340, 440], 30, [170, 130, 200]], lake: 1400 }, card: { moon: [sunX(k, W), 120, 50], peaks: [[200, 560, 960, 1380], 300, [100, 170]], hill: 340, pines: [420, [40, 260, 480, 700, 920, 1140, 1360, 1560], 30, [130, 100, 160, 120]], lake: null }, head: { moon: [sunX(k, W), 110, 50], peaks: [[200, 700, 1200, 1700, 2200, 2700, 3100], 250, [90, 150]], hill: 280, pines: [330, Array.from({ length: 16 }, (_, i) => 60 + i * 210), 26, [100, 80, 120]], lake: null }, back: { moon: [sunX(k, W), 220, 70], peaks: [[200, 560, 960, 1380], 560, [200, 320]], hill: 650, pines: [830, [40, 260, 480, 700, 920, 1140, 1360, 1560], 34, [230, 180, 270, 210]], lake: null } }[place];
      const [mx, my, mr] = L.moon;
      const j = () => Math.round(r() * 30 - 15);
      const hill = `M0 ${L.hill + j()}C${Math.round(W * 0.25)} ${L.hill - 40 + j()} ${Math.round(W * 0.5)} ${L.hill + 20 + j()} ${Math.round(W * 0.75)} ${L.hill - 30 + j()}S${Math.round(W * 0.9)} ${L.hill + j()} ${W} ${L.hill - 10 + j()}`;
      const [py, pxs, pw, phs] = L.pines;
      const lake = L.lake ? `<path d="M${mx - 200} ${L.lake}q200-40 400 0q-200 50-400 0z" fill="#9fd3e0"/><path d="M${mx - 120} ${L.lake - 4}h100M${mx + 20} ${L.lake + 8}h120" stroke="#e8fbff" stroke-width="3" stroke-linecap="round" stroke-opacity=".8"/>` : '';
      return scene(place, turn(k, W, `<circle cx="${mx}" cy="${my}" r="${mr}" fill="#fff9e0"/><circle cx="${mx}" cy="${my}" r="${Math.round(mr * 1.4)}" stroke="#fff9e0" stroke-opacity=".6" stroke-width="4" fill="none"/>${peaks(r, L.peaks[0], L.peaks[1], L.peaks[2], '#8cc4a2', '#79b490', '#f4fbf6')}<path d="${hill}V${H}H0Z" fill="#3f8a5c"/><path d="${hill}" stroke="#d6f0dd" stroke-opacity=".6" stroke-width="4" fill="none"/>${pines(r, py, pxs, pw, phs, '#1f5a38')}<rect y="${py}" width="${W}" height="${Math.max(0, H - py)}" fill="#15402a"/>${lake}`), DEFS.forest);
    },
    /** A sun with a ring of ticks, a pyramid or two across from it, dunes with a light crest and two contours each — and a cactus at the sidebar's foot. */
    sand(k, place) {
      const r = rng(k + 27); const [W, H] = FRAME[place];
      const L = { side: { sun: [sunX(k + 2, W), 300, 70], dunes: [820, 1100, 1400], amp: 60, pyr: [[225, 700, 150, 170]], cactus: [120, 1570, 140] }, card: { sun: [sunX(k + 2, W), 140, 60], dunes: [300, 380], amp: 40, pyr: null, cactus: null }, head: { sun: [sunX(k + 2, W), 120, 55], dunes: [230, 300], amp: 34, pyr: null, cactus: null }, back: { sun: [sunX(k + 2, W), 250, 80], dunes: [600, 760, 900], amp: 70, pyr: null, cactus: null } }[place];
      const [sx, sy, sr] = L.sun;
      const far = sx > W / 2;
      const pyr = L.pyr || (place === 'card' ? [[far ? 300 : W - 300, L.dunes[0] - 10, 120, 130]] : [[far ? 500 : W - 500, L.dunes[0] - 10, 140, 150], [far ? 760 : W - 760, L.dunes[0] - 10, 90, 100]]);
      const j = () => Math.round(r() * L.amp - L.amp / 2);
      const dune = (y) => `M0 ${y + j()}C${Math.round(W * 0.2)} ${y - L.amp + j()} ${Math.round(W * 0.35)} ${y + j()} ${Math.round(W * 0.5)} ${Math.round(y - L.amp * 0.8) + j()}S${Math.round(W * 0.8)} ${Math.round(y - L.amp * 1.2) + j()} ${W} ${y + j()}`;
      const lower = (d, by) => d.replace(/(-?\d+) (-?\d+)/g, (m, x, y) => `${x} ${Number(y) + by}`);
      const fills = ['#f1c88e', '#dfa161', '#c07a3c'], lines = ['#fff0cf', '#fff0cf', '#ffe3b8'], contours = ['#d9a05e', '#c48542', '#a9602f'];
      const dunes = L.dunes.map((y, i) => { const d = dune(y); return `<path d="${d}V${H}H0Z" fill="${fills[i]}"/><path d="${d}" stroke="${lines[i]}" stroke-width="4" fill="none"/>${[1, 2].map((n) => `<path d="${lower(d, 22 * n)}" stroke="${contours[i]}" stroke-opacity=".55" stroke-width="3" fill="none"/>`).join('')}`; }).join('');
      return scene(place, turn(k, W, `<circle cx="${sx}" cy="${sy}" r="${sr}" fill="#fff4d6"/><g stroke="#f5c27a" stroke-width="6" stroke-linecap="round">${ticks(sx, sy, sr * 1.4, sr * 1.9, 12)}</g>${pyr.map(([x, base, w, hh]) => pyramid(x, base, w, hh)).join('')}${dunes}${L.cactus ? cactus(...L.cactus) : ''}`), DEFS.sand);
    },
    /** A sun in a ring, ranges of snow-capped peaks each nearer one deeper — three down the sidebar with a river out of them, two across a counter or the header. */
    peaks(k, place) {
      const r = rng(k + 36); const [W, H] = FRAME[place];
      const L = { side: { sun: [sunX(k + 1, W), 260, 60], ranges: [[[60, 225, 400], 760, [220, 320]], [[-20, 150, 320, 470], 1000, [200, 280]], [[100, 300, 450], 1240, [160, 220]]], river: true, cloud: false }, card: { sun: [sunX(k + 1, W), 110, 50], ranges: [[[100, 420, 760, 1100, 1450], 330, [130, 200]], [[-40, 280, 600, 940, 1280, 1620], 420, [110, 170]]], river: false, cloud: false }, head: { sun: [sunX(k + 1, W), 100, 48], ranges: [[Array.from({ length: 9 }, (_, i) => 150 + i * 360), 250, [110, 180]], [Array.from({ length: 9 }, (_, i) => -50 + i * 380), 330, [90, 140]]], river: false, cloud: true }, back: { sun: [sunX(k + 1, W), 220, 70], ranges: [[[100, 420, 760, 1100, 1450], 560, [260, 380]], [[-40, 280, 600, 940, 1280, 1620], 740, [220, 320]], [[150, 550, 950, 1350], 900, [160, 240]]], river: false, cloud: true } }[place];
      const [sx, sy, sr] = L.sun;
      const cols = [['#b6b0e6', '#a19ad8', '#ffffff'], ['#7f78c8', '#6b63b6', '#f2f0ff'], ['#4b438f', '#3b347a', '#e6e3ff']];
      const ranges = L.ranges.map(([xs, base, hs], i) => peaks(r, xs, base, hs, ...cols[Math.min(i, 2)])).join('');
      const last = L.ranges[L.ranges.length - 1][1];
      const river = L.river ? `<path d="M${sx - 30} ${last}C${sx - 150} ${last + 80} ${sx + 120} ${last + 160} ${sx - 60} ${last + 240}S${sx - 200} ${H} ${sx - 100} ${H}H${sx + 60}C${sx - 30} ${H - 40} ${sx + 150} ${last + 200} ${sx + 60} ${last + 160}S${sx + 40} ${last + 60} ${sx + 20} ${last}Z" fill="#bfe3f6"/>` : '';
      return scene(place, turn(k, W, `<circle cx="${sx}" cy="${sy}" r="${sr}" fill="#fff4dd"/><circle cx="${sx}" cy="${sy}" r="${Math.round(sr * 1.5)}" stroke="#fff4dd" stroke-opacity=".7" stroke-width="4" fill="none"/>${L.cloud ? cloud(sx > W / 2 ? sx - 700 : sx + 300, sy, 240, '#ffffff', '#c9c2ef') : ''}${ranges}<rect y="${last - 2}" width="${W}" height="${Math.max(0, H - last + 2)}" fill="#2e285e"/>${river}`), DEFS.peaks);
    },
    /** A low sun behind a far skyline, a near one with its windows lit, the lights in the water — and a bridge across from the sun on the header. */
    city(k, place) {
      const r = rng(k + 45); const [W, H] = FRAME[place];
      const L = { side: { sun: [sunX(k, W), 330, 80], far: [900, [100, 260]], near: [960, [80, 220]], lights: 10, bridge: false }, card: { sun: [sunX(k, W), 150, 70], far: [330, [60, 160]], near: [360, [40, 130]], lights: 5, bridge: false }, head: { sun: [sunX(k, W), 120, 60], far: [250, [50, 150]], near: [280, [40, 120]], lights: 12, bridge: true }, back: { sun: [sunX(k, W), 380, 100], far: [700, [160, 400]], near: [780, [120, 330]], lights: 12, bridge: true } }[place];
      const [sx, sy, sr] = L.sun;
      const water = L.near[0] - 4;
      const lights = Array.from({ length: L.lights }, () => `<rect x="${Math.round(r() * (W - 60))}" y="${Math.round(water + 16 + r() * Math.max(8, H - water - 26))}" width="${Math.round(20 + r() * 50)}" height="4" rx="2"/>`).join('');
      const bridge = L.bridge ? (() => { const bx0 = sx > W / 2 ? 200 : W - 1000, piers = [bx0 + 200, bx0 + 600], top = water - 190, deck = water - 60; return `<rect x="${bx0}" y="${deck}" width="800" height="14" fill="#3b4a5e"/>${piers.map((px) => `<rect x="${px - 10}" y="${top - 6}" width="20" height="${H - top + 6}" fill="#3b4a5e"/>`).join('')}<path d="M${bx0} ${deck}Q${bx0 + 200} ${top + 30} ${piers[0]} ${top}Q${bx0 + 400} ${deck - 20} ${piers[1]} ${top}Q${bx0 + 700} ${top + 30} ${bx0 + 800} ${deck}" stroke="#3b4a5e" stroke-width="5" fill="none"/><g stroke="#3b4a5e" stroke-width="3">${piers.map((px) => [-120, -60, 60, 120].map((dx) => `<path d="M${px} ${top}L${px + dx} ${deck}"/>`).join('')).join('')}</g>`; })() : '';
      return scene(place, turn(k, W, `<circle cx="${sx}" cy="${sy}" r="${sr}" fill="#ffd9a8"/>${skyline(r, -20, W + 20, L.far[0], L.far[1], '#9fb3c4', null)}${skyline(r, -10, W + 20, L.near[0], L.near[1], '#56697f', '#ffe3a8', 0.72)}<rect y="${water}" width="${W}" height="${H - water}" fill="#3d6b86"/><g fill="#ffe3a8" fill-opacity=".55">${lights}</g>${bridge}`), DEFS.city);
    },
  };
  const SCENE_TONES = { Dusk: '#b8527a', Ocean: '#3a8fd6', Forest: '#2f8f4e', Sand: '#e8a767', Peaks: '#6b63b6', City: '#56697f' };
  const SCENE_NAMES = Object.keys(SCENE_TONES);
  // every drawing, built once — the first time one is asked for. (This file runs before every Canvas
  // page paints; a page that shows no scene never draws the ninety of them, a few hundred kilobytes of text.)
  const SCENE_URLS = new Map();
  const sceneKey = (name, place, k) => `${name}@${place}${k ? `#${k}` : ''}`;
  const scenes = () => { if (!SCENE_URLS.size) for (const name of SCENE_NAMES) for (const place of PLACES) for (let k = 0; k < SCENE_VARIANTS[place]; k++) SCENE_URLS.set(sceneKey(name, place, k), DRAW[name.toLowerCase()](k, place)); return SCENE_URLS; };
  /** A scene's drawing for a place ('side', 'card', 'head'): its first variation, or a variation k (wrapped round the place's count). */
  const sceneUrl = (name, k = 0, place = 'card') => { const n = SCENE_VARIANTS[place] || 1; const kk = ((k % n) + n) % n; return scenes().get(sceneKey(name, place, kk)) || null; };
  /** The drawing a scene key stands for ('Dusk@card', 'Dusk@card#3'), or null for anything else (a key from before 2.98.52 too: its raster serves). */
  const sceneUrlOf = (key) => (key ? scenes().get(key) || null : null);
  /** A scene drawn to the apps' window (Simpl for Mac 1.3: behind its glass), not kept with the page's: 'Dusk', 'Ocean'… */
  const backScene = (name, k = 0) => DRAW[String(name || '').toLowerCase()]?.(k, 'back') || null;
  /** A scene key taken apart: { name, place, k }, or null. */
  const sceneParts = (key) => { const m = /^([A-Za-z]+)@(side|card|head)(?:#(\d+))?$/.exec(key || ''); return m ? { name: m[1], place: m[2], k: Number(m[3] || 0) } : null; };
  /** The scene a key names, whatever its place or variation ('Dusk@head#2' → 'Dusk'), or null. */
  const sceneBaseOf = (key) => sceneParts(key)?.name || (key && SCENE_NAMES.includes(String(key).split('#')[0]) ? String(key).split('#')[0] : null);
  /** [name, picture, tone]: the tone is what the veils warm to, as a read photo's would be; the picture a counter's drawing. Built when first asked for. */
  let presetPhotos = null;
  const presetPhotosOf = () => (presetPhotos ||= SCENE_NAMES.map((n) => [n, sceneUrl(n, 0, 'card'), SCENE_TONES[n]]));

  // ---- the photos: read here, scaled here, kept here ------------------------------------------------
  /** Where a photo can go: the six counters of the Dashboard, and the sidebar. */
  const CARD_SLOTS = ['today', 'week', 'unread', 'overdue', 'tomorrow', 'graded'];
  /** The screens whose header can carry a photo (the route's screen key, and the title it shows). */
  const HEADER_SLOTS = [['dashboard', 'Dashboard'], ['courses', 'All Courses'], ['groups', 'Groups'], ['todo', 'To Do'], ['calendar', 'Calendar'], ['notifications', 'Notifications'], ['inbox', 'Inbox'], ['gpa', 'Grades'], ['tools', 'Tools']];
  const IMAGES_KEY = 'theme:images'; // storage.local: { side: dataURL | null, cards: { [slot]: dataURL }, headers: { [screen]: dataURL } } — never in the settings, which the Mac app carries
  // ---- where a photo sits (2.98.51) ------------------------------------------------------------------
  // A photo covers its place (the sidebar, a counter, a header) and is pinned there by a point: the
  // point of the picture at x%, y% sits on the point of the box at x%, y% — 100%, 100% keeps the
  // picture's bottom-right corner in the box's, as a counter always did — and is zoomed about that
  // point (1 = just covering the box, up to 3). Kept under theme:images as place[key], the keys the
  // tones use ('side', a counter's slot, 'head:<screen>'), only where it is not the place's own
  // default; Personalize sets it by dragging the picture on its preview, and the page and the
  // preview draw it the same way (app.css, setup-css.js: the pin as the background's position and
  // the zoom a scale about it).
  const PLACE_DEFAULT = (key) => (key === 'side' ? { x: 50, y: 100, z: 1 } : String(key).startsWith('head') ? { x: 100, y: 50, z: 1 } : { x: 100, y: 100, z: 1 });
  // (2.98.74) a photo zooms out to half its place's fill (the rest of the place its own ground round it) and in to three times
  const ZOOM_MIN = 0.5, ZOOM_MAX = 3;
  const numIn = (v, lo, hi, d) => { const n = Number(v); return Number.isFinite(n) ? Math.min(hi, Math.max(lo, n)) : d; };
  /** Where the photo at `key` sits: the kept place, clamped, or the place's default. */
  const placeOf = (images, key) => { const d = PLACE_DEFAULT(key); const p = images?.place?.[key]; return p && typeof p === 'object' ? { x: numIn(p.x, 0, 100, d.x), y: numIn(p.y, 0, 100, d.y), z: numIn(p.z, ZOOM_MIN, ZOOM_MAX, 1) } : d; };
  const isDefaultPlace = (key, p) => { const d = PLACE_DEFAULT(key); return !p || (Math.abs(Number(p.x) - d.x) < 0.05 && Math.abs(Number(p.y) - d.y) < 0.05 && Math.abs(Number(p.z) - 1) < 0.005); };
  const roundPlace = (p) => ({ x: Math.round(p.x * 10) / 10, y: Math.round(p.y * 10) / 10, z: Math.round(p.z * 100) / 100 });
  /** The style variables a photo's layers read for its place (app.css --bcv-pic-x/-y/-z; the preview's --pic-x/-y/-z). */
  const placeVars = (p, prefix = '--bcv-pic') => ({ [`${prefix}-x`]: `${p.x}%`, [`${prefix}-y`]: `${p.y}%`, [`${prefix}-z`]: String(p.z) });
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
  function inkOf(img, wide = 800, { fill = 1 } = {}) {
    const ratio = img.naturalWidth ? img.naturalHeight / img.naturalWidth : 0.5625;
    const w = Math.max(8, Math.round(Math.sqrt((wide * wide * 0.5625) / ratio))); // (by area — `wide` across for a 16:9 picture — so a tall or a long drawing keeps its lines)
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
      const shape = sm[i] < deep ? Math.min(255, (deep - sm[i]) * 24) * fill : 0; // (soft at the split, so the shapes are not jagged; a drawn scene's masses lighter than its lines)
      d[i * 4] = 255; d[i * 4 + 1] = 255; d[i * 4 + 2] = 255; d[i * 4 + 3] = Math.max(shape, edge);
    }
    cx.putImageData(out, 0, 0);
    const ink = cv.toDataURL('image/png');
    // the blurred copy: the mask drawn small and blurred twice — scaled up by the page, it is the soft side
    const bw = Math.max(8, Math.round(Math.sqrt(5184 / ratio))), bh = Math.max(8, Math.round(bw * ratio)); // (96 × 54 for a 16:9 picture, the same area for any shape)
    const bc = document.createElement('canvas'); bc.width = bw; bc.height = bh;
    const bx = bc.getContext('2d');
    bx.drawImage(cv, 0, 0, bw, bh);
    const bd = bx.getImageData(0, 0, bw, bh);
    boxBlurAlpha(bd.data, bw, bh, 3); boxBlurAlpha(bd.data, bw, bh, 3);
    bx.putImageData(bd, 0, 0);
    return { ink, inkBlur: bc.toDataURL('image/png') };
  }
  /** The ink of a raw picture (a data URL: an upload or a drawn scene), computed once and kept in memory, keyed by the
   *  picture and its size — the page's (800 across for a 16:9 picture) or, for a thumbnail (`wide` 260: Personalize's
   *  theme tiles and photo choices, a tenth of the work), a small one of its own. */
  const inks = new Map();
  const inking = new Map(); // in hand: the same picture asked for twice at once is inked once
  const inkCached = (v, wide = 800) => (v ? inks.get(`${hashOf(v)}@${wide}`) || null : null);
  async function inkFor(v, { wide = 800 } = {}) {
    if (!v) return null;
    const id = `${hashOf(v)}@${wide}`;
    if (inks.has(id)) return inks.get(id);
    if (inking.has(id)) return inking.get(id);
    // a drawn scene (an SVG) is inked as an illustration: its lines full, its dark masses — a night sky, the sea, a skyline — a light wash, so the drawing reads rather than a block
    const scene = String(v).startsWith('data:image/svg+xml');
    const p = loadPicture(v, false).then((img) => { const r = inkOf(img, wide, scene ? { fill: SCENE_FILL } : {}); inks.set(id, r); inking.delete(id); return r; }).catch((e) => { inking.delete(id); throw e; });
    inking.set(id, p);
    return p;
  }
  /** A drawn scene as a picture, rasterised once at a size that stays crisp on a wide header. */
  async function rasterOf(svgUrl, long = 2000) {
    const img = await loadPicture(svgUrl, false);
    const nw = img.naturalWidth || 1600, nh = img.naturalHeight || 900, k = long / Math.max(nw, nh); // (the longer side: a tall or a long drawing keeps its size)
    const w = Math.max(1, Math.round(nw * k)), h = Math.max(1, Math.round(nh * k));
    const cv = document.createElement('canvas'); cv.width = w; cv.height = h;
    cv.getContext('2d').drawImage(img, 0, 0, w, h);
    return { data: cv.toDataURL('image/jpeg', 0.86), img };
  }
  /** Every raw picture in `images` (a data URL: an upload, or a drawn scene) becomes an asset, kept once;
   *  assets nothing wears any more go. Needs a document (a canvas): elsewhere the pictures are kept as they are. */
  async function packImages(images) {
    const out = { side: null, cards: {}, headers: {}, tones: { ...(images?.tones || {}) }, place: { ...(images?.place || {}) }, assets: { ...(images?.assets || {}) } };
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

  const emptyImages = () => ({ side: null, cards: {}, headers: {}, tones: {}, place: {}, assets: {} });
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
  const indexOf = (p) => ({ side: p.side, cards: p.cards, headers: p.headers, tones: Object.fromEntries(toneKeys(p).filter((k) => p.tones?.[k]).map((k) => [k, p.tones[k]])), place: Object.fromEntries(toneKeys(p).filter((k) => p.place?.[k] && !isDefaultPlace(k, placeOf(p, k))).map((k) => [k, roundPlace(placeOf(p, k))])), assetIds: Object.keys(p.assets || {}), ...(hasRaw(p) ? { packStuck: true } : {}) }); // (a place kept only where a photo sits, and only off its default)
  /** The set, with the pictures of `need` (an array of slots) read in — or of every slot ('all'). */
  async function loadImages({ need = 'all' } = {}) {
    try {
      const r = await BCV.api.storage.local.get(IMAGES_KEY);
      const v = r?.[IMAGES_KEY];
      if (!v || typeof v !== 'object') return emptyImages();
      const images = { side: v.side || null, cards: { ...(v.cards || {}) }, headers: { ...(v.headers || {}) }, tones: { ...(v.tones || {}) }, place: { ...(v.place || {}) }, assets: {}, ...(v.packStuck ? { packStuck: true } : {}) };
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
  // ---- whether this browser draws the glass's blur ----------------------------------------------
  // Chrome without graphics acceleration (switched off in its settings, or its GPU refused) draws
  // the page in software, and software skips every backdrop-filter while CSS.supports still says
  // yes: the bars and panels show the page through them sharp. A WebGL context tells the two apart:
  // none at all, one that refuses a software renderer (failIfMajorPerformanceCaveat), or one whose
  // renderer is a software one by name (SwiftShader, llvmpipe — what Chrome falls back to). Asked at
  // most every half hour, the answer kept in the page's own storage and the context let go at once.
  // Safari and Firefox draw the blur either way and are never asked (only Chromium has
  // navigator.userAgentData). blurDrawn() → true | false, or null when the kept answer is stale;
  // checkBlur() asks afresh.
  const BLUR_KEY = 'bcv:blur';
  const BLUR_FOR = 30 * 60 * 1000;
  function blurDrawn() {
    try {
      if (!self.navigator?.userAgentData) return true;
      const kept = JSON.parse(localStorage.getItem(BLUR_KEY) || 'null');
      return kept && Date.now() - kept.at < BLUR_FOR ? !!kept.drawn : null;
    } catch { return true; }
  }
  function checkBlur() {
    try {
      if (!self.navigator?.userAgentData) return true;
      const gl = document.createElement('canvas').getContext('webgl', { failIfMajorPerformanceCaveat: true });
      const named = gl?.getExtension('WEBGL_debug_renderer_info');
      const renderer = named ? String(gl.getParameter(named.UNMASKED_RENDERER_WEBGL) || '') : '';
      const drawn = !!gl && !/swiftshader|llvmpipe|softpipe|software|basic render/i.test(renderer);
      gl?.getExtension('WEBGL_lose_context')?.loseContext();
      try { localStorage.setItem(BLUR_KEY, JSON.stringify({ drawn, at: Date.now() })); } catch { /* asked again next time */ }
      return drawn;
    } catch { return true; }
  }

  BCV.theme = {
    hexToRgb, rgbToHex, rgbToHsl, hslToRgb, hslToHex, luminance, contrast, normalize,
    GROUND, MIN_SAT, ICON_RATIO, PRESETS, REGULAR, SCENE_VARIANTS, SCENE_PLACES: PLACES, sceneUrl, sceneNameOf, sceneParts, sceneBaseOf, CARD_SLOTS, HEADER_SLOTS, IMAGES_KEY, PLACE_DEFAULT, placeOf, placeVars, isDefaultPlace, ZOOM_MIN, ZOOM_MAX,
    palette, shades, shadeSet, cssVars, apply, readable, readableOn, fillFor, mix, tint, customHex, controlsOf, veilBase, picCss, band, nearest,
    backScene, readImage, imageTone, fillTones, loadImages, ensureAssets, saveImages, emptyImages, packImages, picOf, rawOf, CAST, INK_LIFT, inkOn, SCENE_INK, sceneInkOn, inkFor, inkCached,
    blurDrawn, checkBlur, BLUR_KEY,
    get PRESET_PHOTOS() { return presetPhotosOf(); }, // (the drawings, made on the first ask)
  };
})();
