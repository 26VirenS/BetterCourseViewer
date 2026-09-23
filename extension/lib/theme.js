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
    return {
      accent: seed, icon, text, fill,
      hover: shift(fill, dark ? 0.08 : -0.08),
      soft: alpha(seed, dark ? 0.22 : 0.14),
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
    const set = shadeSet(seed);
    const out = [];
    for (let i = 0; i < n; i++) { const base = set[i % set.length]; out.push({ icon: reach(base, ground, ICON_RATIO, dark), text: readableOn(base, ground) }); }
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
      try { images.tones[key] = await imageTone(imageAt(images, key)); added++; } catch { /* left without a tone: the ground colour serves */ }
    }
    if (added) await saveImages(images).catch(() => {});
    return images;
  }
  const emptyImages = () => ({ side: null, cards: {}, headers: {}, tones: {} });
  async function loadImages() {
    try {
      const r = await BCV.api.storage.local.get(IMAGES_KEY);
      const v = r?.[IMAGES_KEY];
      return v && typeof v === 'object' ? { side: v.side || null, cards: { ...(v.cards || {}) }, headers: { ...(v.headers || {}) }, tones: { ...(v.tones || {}) } } : emptyImages();
    } catch { return emptyImages(); }
  }
  const saveImages = (images) => BCV.api.storage.local.set({ [IMAGES_KEY]: { side: images?.side || null, cards: { ...(images?.cards || {}) }, headers: { ...(images?.headers || {}) }, tones: Object.fromEntries(toneKeys(images).filter((k) => images?.tones?.[k]).map((k) => [k, images.tones[k]])) } });
  const countImages = (images) => (images?.side ? 1 : 0) + Object.values(images?.cards || {}).filter(Boolean).length + Object.values(images?.headers || {}).filter(Boolean).length;
  const countHeaders = (images) => Object.values(images?.headers || {}).filter(Boolean).length;

  BCV.theme = {
    hexToRgb, rgbToHex, rgbToHsl, hslToRgb, hslToHex, luminance, contrast, normalize,
    GROUND, MIN_SAT, ICON_RATIO, TEXT_RATIO, PRESETS, REGULAR, CARD_SLOTS, HEADER_SLOTS, IMAGES_KEY,
    palette, shades, shadeSet, cssVars, apply, readable, readableOn, fillFor, mix, tint, customHex, controlsOf, veilBase, picCss, band, nearest, fromControls, toControls,
    resizeImage, readImage, imageTone, fillTones, loadImages, saveImages, countImages, countHeaders, emptyImages,
  };
})();
