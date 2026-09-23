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
    const text = reach(seed, ground, TEXT_RATIO, dark);
    const fill = reach(seed, '#ffffff', TEXT_RATIO, false); // white words on it, in either mode
    return {
      accent: seed, icon, text, fill,
      hover: shift(fill, dark ? 0.08 : -0.08),
      soft: alpha(seed, dark ? 0.2 : 0.12),
      ring: alpha(seed, dark ? 0.45 : 0.32),
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
    const [h, s] = rgbToHsl(hexToRgb(seed));
    const out = [];
    for (let i = 0; i < n; i++) {
      const t = n === 1 ? 0.5 : i / (n - 1);
      const hue = (h + (t - 0.5) * 36 + 360) % 360; // ±18° across the rows
      const sat = clamp(s + (t - 0.5) * 0.16, MIN_SAT, 1);
      const [lo, hi] = band(hue, sat);
      const l = dark ? lo + (hi - lo) * (0.85 - t * 0.7) : lo + (hi - lo) * (0.75 - t * 0.6); // lighter rows first, deeper below
      const base = hslToHex([hue, sat, l]);
      out.push({ icon: reach(base, ground, ICON_RATIO, dark), text: reach(base, ground, TEXT_RATIO, dark) });
    }
    return out;
  }
  /** The CSS custom properties the stylesheet reads (app.css: html.bcv-themed). */
  const cssVars = (p) => ({ '--bcv-accent': p.accent, '--bcv-accent-icon': p.icon, '--bcv-accent-text': p.text, '--bcv-accent-fill': p.fill, '--bcv-accent-hover': p.hover, '--bcv-accent-soft': p.soft, '--bcv-accent-ring': p.ring });
  const VAR_NAMES = ['--bcv-accent', '--bcv-accent-icon', '--bcv-accent-text', '--bcv-accent-fill', '--bcv-accent-hover', '--bcv-accent-soft', '--bcv-accent-ring'];
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
  const PRESETS = [['#0a6cff', 'Blue'], ['#d63b7a', 'Pink'], ['#c8401f', 'Red'], ['#b76b00', 'Amber'], ['#1e8f4e', 'Green'], ['#0f8a9c', 'Teal'], ['#5e5ce6', 'Indigo'], ['#9347b3', 'Purple']];

  // ---- the photos: read here, scaled here, kept here ------------------------------------------------
  /** Where a photo can go: the six counters of the Dashboard, and the sidebar. */
  const CARD_SLOTS = ['today', 'week', 'unread', 'overdue', 'tomorrow', 'graded'];
  /** The screens whose header can carry a photo (the route's screen key, and the title it shows). */
  const HEADER_SLOTS = [['dashboard', 'Dashboard'], ['courses', 'All Courses'], ['groups', 'Groups'], ['todo', 'To Do'], ['calendar', 'Calendar'], ['notifications', 'Notifications'], ['inbox', 'Inbox'], ['gpa', 'Grades'], ['tools', 'Tools']];
  const IMAGES_KEY = 'theme:images'; // storage.local: { side: dataURL | null, cards: { [slot]: dataURL }, headers: { [screen]: dataURL } } — never in the settings, which the Mac app carries
  /** A picture file scaled to fit `max` on its longer side and encoded as a JPEG data URL, so a
   *  phone's photo does not sit in storage at twelve megapixels. Needs a document (a content script). */
  function resizeImage(file, max = 1280, quality = 0.84) {
    return new Promise((resolve, reject) => {
      if (!file || !/^image\//.test(file.type)) { reject(new Error('Not a picture')); return; }
      const url = URL.createObjectURL(file);
      const img = new Image();
      img.onload = () => {
        try {
          const scale = Math.min(1, max / Math.max(img.naturalWidth, img.naturalHeight));
          const cv = document.createElement('canvas');
          cv.width = Math.max(1, Math.round(img.naturalWidth * scale));
          cv.height = Math.max(1, Math.round(img.naturalHeight * scale));
          cv.getContext('2d').drawImage(img, 0, 0, cv.width, cv.height);
          resolve(cv.toDataURL('image/jpeg', quality));
        } catch (e) { reject(e); } finally { URL.revokeObjectURL(url); }
      };
      img.onerror = () => { URL.revokeObjectURL(url); reject(new Error('The picture could not be read')); };
      img.src = url;
    });
  }
  const emptyImages = () => ({ side: null, cards: {}, headers: {} });
  async function loadImages() {
    try {
      const r = await BCV.api.storage.local.get(IMAGES_KEY);
      const v = r?.[IMAGES_KEY];
      return v && typeof v === 'object' ? { side: v.side || null, cards: { ...(v.cards || {}) }, headers: { ...(v.headers || {}) } } : emptyImages();
    } catch { return emptyImages(); }
  }
  const saveImages = (images) => BCV.api.storage.local.set({ [IMAGES_KEY]: { side: images?.side || null, cards: { ...(images?.cards || {}) }, headers: { ...(images?.headers || {}) } } });
  const countImages = (images) => (images?.side ? 1 : 0) + Object.values(images?.cards || {}).filter(Boolean).length + Object.values(images?.headers || {}).filter(Boolean).length;
  const countHeaders = (images) => Object.values(images?.headers || {}).filter(Boolean).length;

  BCV.theme = {
    hexToRgb, rgbToHex, rgbToHsl, hslToRgb, hslToHex, luminance, contrast, normalize,
    GROUND, MIN_SAT, ICON_RATIO, TEXT_RATIO, PRESETS, CARD_SLOTS, HEADER_SLOTS, IMAGES_KEY,
    palette, shades, cssVars, apply, readable, band, nearest, fromControls, toControls,
    resizeImage, loadImages, saveImages, countImages, countHeaders, emptyImages,
  };
})();
