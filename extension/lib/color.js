/* Colour helpers: hex parsing, lighten/darken, and the inverse of the
 * dark-mode page filter (invert(1) hue-rotate(180deg)) so colours we set
 * while dark mode is active still render as intended. */
(function () {
  const BCV = (self.BCV = self.BCV || {});

  function parseHex(hex) {
    if (!hex) return null;
    let h = String(hex).trim().replace(/^#/, '');
    if (h.length === 3) h = h.split('').map((c) => c + c).join('');
    if (!/^[0-9a-f]{6}$/i.test(h)) return null;
    const n = parseInt(h, 16);
    return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
  }

  function toHex([r, g, b]) {
    const c = (v) => Math.max(0, Math.min(255, Math.round(v))).toString(16).padStart(2, '0');
    return '#' + c(r) + c(g) + c(b);
  }

  function mix(hex, target, amount) {
    const a = parseHex(hex);
    const t = parseHex(target);
    if (!a || !t) return hex;
    return toHex(a.map((v, i) => v + (t[i] - v) * amount));
  }

  const lighten = (hex, amt) => mix(hex, '#ffffff', amt);
  const darken = (hex, amt) => mix(hex, '#000000', amt);

  function luminance(hex) {
    const c = parseHex(hex);
    if (!c) return 0;
    const f = (v) => {
      v /= 255;
      return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
    };
    return 0.2126 * f(c[0]) + 0.7152 * f(c[1]) + 0.0722 * f(c[2]);
  }

  const isLight = (hex) => luminance(hex) > 0.5;

  // CSS hue-rotate(180deg) matrix (Filter Effects spec, sRGB).
  const H = [
    [-0.574, 1.43, 0.144],
    [0.426, 0.43, 0.144],
    [0.426, 1.43, -0.856],
  ];

  function invert3(m) {
    const [[a, b, c], [d, e, f], [g, h, i]] = m;
    const A = e * i - f * h, B = -(d * i - f * g), C = d * h - e * g;
    const det = a * A + b * B + c * C;
    if (Math.abs(det) < 1e-9) return null;
    return [
      [A / det, -(b * i - c * h) / det, (b * f - c * e) / det],
      [B / det, (a * i - c * g) / det, -(a * f - c * d) / det],
      [C / det, -(a * h - b * g) / det, (a * e - b * d) / det],
    ];
  }
  const Hinv = invert3(H);

  /** Apply the page's dark filter to a colour: invert, then hue-rotate. */
  function applyDarkFilter(hex) {
    const c = parseHex(hex);
    if (!c) return hex;
    const inv = c.map((v) => 1 - v / 255);
    const out = H.map((row) => row[0] * inv[0] + row[1] * inv[1] + row[2] * inv[2]);
    return toHex(out.map((v) => Math.max(0, Math.min(1, v)) * 255));
  }

  /** Pre-transform a colour so that after the page's dark filter it renders as `hex`. */
  function preDarkFilter(hex) {
    const c = parseHex(hex);
    if (!c || !Hinv) return hex;
    const t = c.map((v) => v / 255);
    const y = Hinv.map((row) => row[0] * t[0] + row[1] * t[1] + row[2] * t[2]);
    const x = y.map((v) => 1 - Math.max(0, Math.min(1, v)));
    return toHex(x.map((v) => v * 255));
  }

  BCV.color = { parseHex, toHex, mix, lighten, darken, luminance, isLight, applyDarkFilter, preDarkFilter };
})();
