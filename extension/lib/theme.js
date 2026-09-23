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
  const TEXT_RATIO = 4.5; // WCAG 1.4.3: words

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
  /** A seed from the picker's three controls: the hue, the saturation, and the tone (0 lightest to
   *  1 deepest) along the readable band for them. */
  function fromControls(h, s, tone) {
    const sat = clamp(s, MIN_SAT, 1);
    const [lo, hi] = band(h, sat);
    return hslToHex([h, sat, hi - (hi - lo) * clamp(tone, 0, 1)]);
  }
  /** The picker's controls for a seed: { h, s, tone }. */
  function toControls(hex) {
    const [h, s, l] = rgbToHsl(hexToRgb(nearest(hex) || '#0a6cff'));
    const sat = Math.max(s, MIN_SAT);
    const [lo, hi] = band(h, sat);
    return { h, s: sat, tone: hi === lo ? 0.5 : clamp((hi - l) / (hi - lo), 0, 1) };
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
  const SCENES = {
    dusk: scene(`<rect width="1600" height="900" fill="url(#sky)"/>
<rect width="1600" height="560" filter="url(#cloud)" opacity=".5"/>
<circle cx="1090" cy="548" r="330" fill="url(#glow)"/>
<circle cx="1090" cy="548" r="92" fill="#ffd9a4" opacity=".45" filter="url(#soft)"/>
<circle cx="1090" cy="548" r="76" fill="#ffe4b3"/>
<rect y="560" width="1600" height="340" fill="url(#sea)"/>
<rect y="560" width="1600" height="6" fill="#ffb98a" opacity=".55"/>
<rect x="1030" y="560" width="120" height="260" fill="url(#refl)" filter="url(#soft)"/>
<path d="M0 640 Q400 618 800 640 T1600 634" stroke="#ff9e7a" stroke-opacity=".16" stroke-width="7" fill="none" filter="url(#soft2)"/>
<path d="M0 712 Q400 690 800 712 T1600 706" stroke="#ff9e7a" stroke-opacity=".12" stroke-width="9" fill="none" filter="url(#soft2)"/>
<path d="M0 800 Q400 780 800 800 T1600 792" stroke="#c96a7c" stroke-opacity=".14" stroke-width="12" fill="none" filter="url(#soft2)"/>`, `<linearGradient id="sky" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#1e1748"/><stop offset=".32" stop-color="#5f2f6b"/><stop offset=".52" stop-color="#c8645f"/><stop offset=".62" stop-color="#f4b06a"/></linearGradient>
<linearGradient id="sea" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#6a3358"/><stop offset=".45" stop-color="#2b1a42"/><stop offset="1" stop-color="#100b20"/></linearGradient>
<radialGradient id="glow"><stop offset="0" stop-color="#ffd7a0" stop-opacity=".9"/><stop offset=".45" stop-color="#ff9a6a" stop-opacity=".3"/><stop offset="1" stop-color="#ff9a6a" stop-opacity="0"/></radialGradient>
<linearGradient id="refl" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#ffcf8f" stop-opacity=".7"/><stop offset="1" stop-color="#ffcf8f" stop-opacity="0"/></linearGradient>
<filter id="soft" x="-30%" y="-30%" width="160%" height="160%"><feGaussianBlur stdDeviation="16"/></filter>
<filter id="soft2" x="-5%" y="-100%" width="110%" height="300%"><feGaussianBlur stdDeviation="5"/></filter>
<filter id="cloud" x="0" y="0" width="100%" height="100%"><feTurbulence type="fractalNoise" baseFrequency="0.0016 0.005" numOctaves="3" seed="4"/><feColorMatrix values="0 0 0 0 1  0 0 0 0 .8  0 0 0 0 .84  0 0 0 1.5 -0.62"/><feGaussianBlur stdDeviation="2"/></filter>`),
    ocean: scene(`<rect width="1600" height="900" fill="url(#sky)"/>
<rect width="1600" height="470" filter="url(#cloud)" opacity=".8"/>
<circle cx="420" cy="220" r="260" fill="url(#glow)"/>
<circle cx="420" cy="220" r="64" fill="#fff8dc"/>
<rect y="470" width="1600" height="430" fill="url(#sea)"/>
<rect y="470" width="1600" height="5" fill="#bfe9ff" opacity=".7"/>
<rect y="470" width="1600" height="430" filter="url(#glint)" opacity=".5"/>
<path d="M0 560 Q300 540 600 560 T1200 560 T1600 552" stroke="#dff4ff" stroke-opacity=".3" stroke-width="6" fill="none" filter="url(#soft2)"/>
<path d="M0 660 Q300 636 600 660 T1200 660 T1600 650" stroke="#dff4ff" stroke-opacity=".22" stroke-width="9" fill="none" filter="url(#soft2)"/>
<path d="M0 780 Q300 752 600 780 T1200 780 T1600 770" stroke="#dff4ff" stroke-opacity=".16" stroke-width="12" fill="none" filter="url(#soft2)"/>
<rect x="340" y="470" width="160" height="300" fill="url(#refl)" filter="url(#soft)"/>`, `<linearGradient id="sky" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#cfeafc"/><stop offset=".52" stop-color="#63b3ec"/></linearGradient>
<linearGradient id="sea" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#3ea3e6"/><stop offset=".4" stop-color="#1a6fb8"/><stop offset="1" stop-color="#062f57"/></linearGradient>
<radialGradient id="glow"><stop offset="0" stop-color="#fff9e0" stop-opacity=".95"/><stop offset=".4" stop-color="#fff2c4" stop-opacity=".3"/><stop offset="1" stop-color="#fff2c4" stop-opacity="0"/></radialGradient>
<linearGradient id="refl" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#ffffff" stop-opacity=".5"/><stop offset="1" stop-color="#ffffff" stop-opacity="0"/></linearGradient>
<filter id="soft" x="-30%" y="-30%" width="160%" height="160%"><feGaussianBlur stdDeviation="16"/></filter>
<filter id="soft2" x="-5%" y="-100%" width="110%" height="300%"><feGaussianBlur stdDeviation="4"/></filter>
<filter id="cloud" x="0" y="0" width="100%" height="100%"><feTurbulence type="fractalNoise" baseFrequency="0.0014 0.004" numOctaves="4" seed="11"/><feColorMatrix values="0 0 0 0 1  0 0 0 0 1  0 0 0 0 1  0 0 0 1.7 -0.72"/><feGaussianBlur stdDeviation="3"/></filter>
<filter id="glint" x="0" y="0" width="100%" height="100%"><feTurbulence type="fractalNoise" baseFrequency="0.02 0.09" numOctaves="2" seed="3"/><feColorMatrix values="0 0 0 0 1  0 0 0 0 1  0 0 0 0 1  0 0 0 1.4 -0.9"/></filter>`),
    forest: scene(`<rect width="1600" height="900" fill="url(#sky)"/>
<circle cx="1240" cy="150" r="320" fill="url(#glow)"/>
<path d="M0 470 C200 420 320 400 520 440 S900 520 1100 460 S1420 400 1600 430 V900 H0Z" fill="#9dcfae"/>
<rect y="440" width="1600" height="200" fill="url(#mist)"/>
<path d="M0 560 C240 500 420 540 640 560 S980 600 1200 550 S1460 500 1600 540 V900 H0Z" fill="#5fa877"/>
<rect y="540" width="1600" height="220" fill="url(#mist)"/>
<path d="M0 680 C180 620 380 660 560 680 S860 720 1040 670 S1360 620 1600 660 V900 H0Z" fill="#2f7f4e"/>
<path d="M0 680 C180 620 380 660 560 680 S860 720 1040 670 S1360 620 1600 660 V900 H0Z" filter="url(#leaf)" opacity=".35"/>
<path d="M0 800 C260 760 520 800 780 790 S1300 760 1600 790 V900 H0Z" fill="#1b4d31"/>
<path d="M0 800 C260 760 520 800 780 790 S1300 760 1600 790 V900 H0Z" filter="url(#leaf)" opacity=".4"/>`, `<linearGradient id="sky" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#e8f5ec"/><stop offset=".55" stop-color="#b9e0c6"/></linearGradient>
<radialGradient id="glow"><stop offset="0" stop-color="#fffbe6" stop-opacity=".9"/><stop offset=".5" stop-color="#fff6cc" stop-opacity=".25"/><stop offset="1" stop-color="#fff6cc" stop-opacity="0"/></radialGradient>
<linearGradient id="mist" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#ffffff" stop-opacity="0"/><stop offset=".5" stop-color="#ffffff" stop-opacity=".42"/><stop offset="1" stop-color="#ffffff" stop-opacity="0"/></linearGradient>
<filter id="leaf" x="0" y="0" width="100%" height="100%"><feTurbulence type="fractalNoise" baseFrequency="0.02 0.03" numOctaves="3" seed="9"/><feColorMatrix values="0 0 0 0 .05  0 0 0 0 .25  0 0 0 0 .12  0 0 0 1.3 -0.5"/></filter>`),
    sand: scene(`<rect width="1600" height="900" fill="url(#sky)"/>
<circle cx="1230" cy="240" r="300" fill="url(#glow)"/>
<circle cx="1230" cy="240" r="70" fill="#fff6dc"/>
<path d="M0 520 C300 470 560 500 820 480 S1300 430 1600 470 V900 H0Z" fill="url(#dune1)"/>
<path d="M0 640 C260 590 520 640 800 610 S1240 560 1600 600 V900 H0Z" fill="url(#dune2)"/>
<path d="M0 640 C260 590 520 640 800 610 S1240 560 1600 600" stroke="#fff0cf" stroke-opacity=".55" stroke-width="3" fill="none"/>
<path d="M0 790 C240 740 560 780 900 750 S1320 720 1600 760 V900 H0Z" fill="url(#dune3)"/>
<path d="M0 790 C240 740 560 780 900 750 S1320 720 1600 760" stroke="#ffe3b8" stroke-opacity=".5" stroke-width="3" fill="none"/>
<rect y="470" width="1600" height="430" filter="url(#grain)" opacity=".28"/>`, `<linearGradient id="sky" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#fdedd2"/><stop offset=".5" stop-color="#f6cd93"/></linearGradient>
<radialGradient id="glow"><stop offset="0" stop-color="#fff8e4" stop-opacity=".95"/><stop offset=".5" stop-color="#ffe9b8" stop-opacity=".3"/><stop offset="1" stop-color="#ffe9b8" stop-opacity="0"/></radialGradient>
<linearGradient id="dune1" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#f0c98c"/><stop offset=".55" stop-color="#e9b672"/><stop offset="1" stop-color="#f3d09a"/></linearGradient>
<linearGradient id="dune2" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#d9995a"/><stop offset=".5" stop-color="#e8ad6c"/><stop offset="1" stop-color="#cf8a4b"/></linearGradient>
<linearGradient id="dune3" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#b8703c"/><stop offset=".5" stop-color="#cf8a4b"/><stop offset="1" stop-color="#a9602f"/></linearGradient>
<filter id="grain" x="0" y="0" width="100%" height="100%"><feTurbulence type="fractalNoise" baseFrequency="0.6" numOctaves="1" seed="2"/><feColorMatrix values="0 0 0 0 .4  0 0 0 0 .2  0 0 0 0 .05  0 0 0 .9 -0.35"/></filter>`),
  };
  /** [name, picture, tone]: the tone is what the veils warm to, as a read photo's would be. */
  const PRESET_PHOTOS = [['Dusk', SCENES.dusk, '#b8527a'], ['Ocean', SCENES.ocean, '#3a8fd6'], ['Forest', SCENES.forest, '#2f8f4e'], ['Sand', SCENES.sand, '#e8a767']];

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
  const resizeImage = (file, max, quality) => readImage(file, max, quality).then((r) => r.data);
  /** The tone of a photo already kept (a data URL). */
  const imageTone = (data) => loadPicture(data, false).then(toneOf);
  // the keys the tones are kept under: 'side', a counter's slot, 'head:<screen>'
  const toneKeys = (images) => [...(images?.side ? ['side'] : []), ...Object.keys(images?.cards || {}).filter((k) => images.cards[k]), ...Object.keys(images?.headers || {}).filter((k) => images.headers[k]).map((k) => `head:${k}`)];
  const imageAt = (images, key) => (key === 'side' ? images.side : key.startsWith('head:') ? images.headers?.[key.slice(5)] : images.cards?.[key]);
  /** Photos kept before tones were (2.60–2.63) get theirs read now, and saved; the images come back with them. */
  async function fillTones(images) {
    if (!images) return images;
    images.tones = images.tones || {};
    let added = 0;
    for (const key of toneKeys(images)) {
      if (images.tones[key]) continue;
      try { images.tones[key] = await imageTone(picOf(images, imageAt(images, key))?.sharp); added++; } catch { /* left without a tone: the ground colour serves */ }
    }
    if (added || hasRaw(images)) await saveImages(images).catch(() => {}); // (pictures kept before assets are packed now, once)
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
  const sceneNameOf = (v) => (PRESET_PHOTOS.find((p) => p[1] === v) || [])[0] || null;
  /** A box blur over RGBA pixels, clamped at the edges: across, then down. */
  function boxBlur(d, w, h, r) {
    const tmp = new Float32Array(d.length);
    const n = 2 * r + 1;
    for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) { let R = 0, G = 0, B = 0; for (let k = -r; k <= r; k++) { const i = (y * w + Math.min(w - 1, Math.max(0, x + k))) * 4; R += d[i]; G += d[i + 1]; B += d[i + 2]; } const o = (y * w + x) * 4; tmp[o] = R / n; tmp[o + 1] = G / n; tmp[o + 2] = B / n; }
    for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) { let R = 0, G = 0, B = 0; for (let k = -r; k <= r; k++) { const i = (Math.min(h - 1, Math.max(0, y + k)) * w + x) * 4; R += tmp[i]; G += tmp[i + 1]; B += tmp[i + 2]; } const o = (y * w + x) * 4; d[o] = R / n; d[o + 1] = G / n; d[o + 2] = B / n; d[o + 3] = 255; }
  }
  /** A picture's blurred copy: drawn tiny, blurred twice, and scaled up by the page — soft, at no cost per frame. */
  function blurredOf(img, w = 96) {
    const ratio = img.naturalWidth ? img.naturalHeight / img.naturalWidth : 0.5625;
    const h = Math.max(8, Math.round(w * ratio));
    const cv = document.createElement('canvas'); cv.width = w; cv.height = h;
    const cx = cv.getContext('2d');
    cx.drawImage(img, 0, 0, w, h);
    const id = cx.getImageData(0, 0, w, h);
    boxBlur(id.data, w, h, 3); boxBlur(id.data, w, h, 3);
    cx.putImageData(id, 0, 0);
    return cv.toDataURL('image/jpeg', 0.8);
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
      if (ASSET.test(v)) return out.assets[v.slice(6)] ? v : null;
      if (!canDraw) return v;
      const id = hashOf(v);
      if (!out.assets[id]) {
        try {
          const scene = sceneNameOf(v);
          let sharp = v, img;
          if (/^data:image\/svg\+xml/.test(v)) { const r = await rasterOf(v); sharp = r.data; img = r.img; } else img = await loadPicture(v, false);
          out.assets[id] = { sharp, blur: blurredOf(img), ...(scene ? { scene } : {}) };
        } catch { return v; } // (a picture that will not draw is kept as it is: the page blurs it itself)
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
    if (ASSET.test(v)) { const a = images?.assets?.[v.slice(6)]; return a?.sharp ? { sharp: a.sharp, blur: a.blur || null, scene: a.scene || null } : null; }
    return { sharp: v, blur: null, scene: sceneNameOf(v) };
  };
  /** The raw value a slot stands for, for editing: a scene's own drawing, or the picture itself. */
  const rawOf = (images, v) => { const p = picOf(images, v); if (!p) return null; return p.scene ? (PRESET_PHOTOS.find((x) => x[0] === p.scene) || [])[1] || p.sharp : p.sharp; };
  const hasRaw = (images) => [images?.side, ...Object.values(images?.cards || {}), ...Object.values(images?.headers || {})].some((v) => v && !ASSET.test(v));

  const emptyImages = () => ({ side: null, cards: {}, headers: {}, tones: {}, assets: {} });
  async function loadImages() {
    try {
      const r = await BCV.api.storage.local.get(IMAGES_KEY);
      const v = r?.[IMAGES_KEY];
      return v && typeof v === 'object' ? { side: v.side || null, cards: { ...(v.cards || {}) }, headers: { ...(v.headers || {}) }, tones: { ...(v.tones || {}) }, assets: { ...(v.assets || {}) } } : emptyImages();
    } catch { return emptyImages(); }
  }
  const saveImages = async (images) => { const p = await packImages(images || emptyImages()); return BCV.api.storage.local.set({ [IMAGES_KEY]: { side: p.side, cards: p.cards, headers: p.headers, tones: Object.fromEntries(toneKeys(p).filter((k) => p.tones?.[k]).map((k) => [k, p.tones[k]])), assets: p.assets } }); };
  const countImages = (images) => (images?.side ? 1 : 0) + Object.values(images?.cards || {}).filter(Boolean).length + Object.values(images?.headers || {}).filter(Boolean).length;
  const countHeaders = (images) => Object.values(images?.headers || {}).filter(Boolean).length;

  BCV.theme = {
    hexToRgb, rgbToHex, rgbToHsl, hslToRgb, hslToHex, luminance, contrast, normalize,
    GROUND, MIN_SAT, ICON_RATIO, TEXT_RATIO, PRESETS, REGULAR, PRESET_PHOTOS, CARD_SLOTS, HEADER_SLOTS, IMAGES_KEY,
    palette, shades, shadeSet, cssVars, apply, readable, readableOn, fillFor, mix, tint, customHex, controlsOf, veilBase, picCss, band, nearest, fromControls, toControls,
    resizeImage, readImage, imageTone, fillTones, loadImages, saveImages, countImages, countHeaders, emptyImages, packImages, picOf, rawOf, CAST,
  };
})();
