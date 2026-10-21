/* (2.99.81) Statistics, the Mac's (Statistics.swift, its arithmetic in Model/StatsModel.swift): four pages under one
 * heading — Describe (a data set's n, mean, median, modes, quartiles, spreads and outliers, with a histogram and a box
 * plot), Regression (two columns, the least-squares line, r and r², a scatter with the line, a prediction),
 * Distributions (the normal curve's probabilities, z and inverse; Student's t and its p) and Tests (one-sample, Welch's
 * two-sample and paired t-tests with their 95% intervals). The sums are the Mac's, line for line, so both show the
 * same figures; what is typed on each page is kept on this device. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const T = BCV.tools;
  const NS = 'http://www.w3.org/2000/svg';

  // ---- the numbers, read and written as the Mac's StudyMath does --------------------------------------------------
  function number(s) {
    let t = String(s ?? '').trim().replace(/[,\s]/g, '').replace(/[−–]/g, '-');
    if (t.endsWith('%')) t = t.slice(0, -1);
    if (!t || !/^[-+]?(\d+\.?\d*|\.\d+)(e[-+]?\d+)?$/i.test(t)) return null;
    const v = Number(t);
    return Number.isFinite(v) ? v : null;
  }
  const numbers = (text) => String(text || '').split(/[ ,;\t\r\n]+/).map(number).filter((v) => v !== null);
  /** v rounded half up at `places` decimals, read to 15 figures first (the binary value's noise dropped). */
  function halfUp(v, places) {
    if (!Number.isFinite(v) || v === 0) return v;
    const clean = Number(v.toPrecision(15));
    const m = 10 ** places;
    return Math.sign(clean) * Math.round(Math.abs(clean) * m + 1e-9) / m;
  }
  /** Six significant figures at most, trailing zeros dropped; very large and small in scientific form ("3.343e23"). */
  function text(v, sig = 6) {
    if (!Number.isFinite(v)) return Number.isNaN(v) ? '—' : (v > 0 ? '∞' : '−∞');
    if (v === 0) return '0';
    const a = Math.abs(v);
    if (a >= 1e12 || a < 1e-5) {
      const [m, e] = v.toExponential(sig - 1).split('e');
      return `${m.includes('.') ? m.replace(/0+$/, '').replace(/\.$/, '') : m}e${Number(e)}`.replace(/-/g, '−');
    }
    const places = Math.max(sig - 1 - Math.floor(Math.log10(a)), 0);
    let s = halfUp(v, places).toFixed(places);
    if (s.includes('.')) s = s.replace(/0+$/, '').replace(/\.$/, '');
    if (s === '-0') s = '0';
    return s.replace(/-/g, '−');
  }
  const fixed = (v, places) => (Number.isFinite(v) ? halfUp(v, places).toFixed(places).replace(/^-(0\.?0*)$/, '$1').replace(/-/g, '−') : '—');
  const pText = (p) => (Number.isNaN(p) ? '—' : p < 0.001 ? '< 0.001' : fixed(p, 3));

  // ---- describing a data set ---------------------------------------------------------------------------------------
  const median = (s) => (!s.length ? NaN : s.length % 2 ? s[(s.length - 1) / 2] : (s[s.length / 2 - 1] + s[s.length / 2]) / 2);
  function quartiles(s) {
    const n = s.length;
    if (n <= 1) return { q1: s[0] ?? NaN, median: s[0] ?? NaN, q3: s[0] ?? NaN };
    const half = Math.floor(n / 2);
    return { q1: median(s.slice(0, half)), median: median(s), q3: median(s.slice(n - half)) };
  }
  function modes(s) {
    const counts = new Map();
    for (const v of s) counts.set(v, (counts.get(v) || 0) + 1);
    const top = Math.max(0, ...counts.values());
    return top > 1 ? [...counts].filter(([, c]) => c === top).map(([v]) => v).sort((a, b) => a - b) : [];
  }
  function describe(data) {
    if (!data.length) return null;
    const s = [...data].sort((a, b) => a - b);
    const n = s.length;
    const sum = s.reduce((t, v) => t + v, 0);
    const mean = sum / n;
    const devs = s.reduce((t, v) => t + (v - mean) * (v - mean), 0);
    const popVar = devs / n, sampVar = n > 1 ? devs / (n - 1) : 0;
    const q = quartiles(s);
    const iqr = q.q3 - q.q1;
    const low = q.q1 - 1.5 * iqr, high = q.q3 + 1.5 * iqr;
    return { sorted: s, n, mean, median: q.median, modes: modes(s), min: s[0], max: s[n - 1], range: s[n - 1] - s[0], q1: q.q1, q3: q.q3, iqr, sampleSD: Math.sqrt(sampVar), populationSD: Math.sqrt(popVar), sampleVariance: sampVar, populationVariance: popVar, standardError: n > 1 ? Math.sqrt(sampVar) / Math.sqrt(n) : 0, sum, sumOfSquares: s.reduce((t, v) => t + v * v, 0), outliers: s.filter((v) => v < low || v > high) };
  }
  function histogram(data) {
    if (!data.length) return [];
    const lo = Math.min(...data), hi = Math.max(...data);
    if (!(hi > lo)) return [{ lower: lo, upper: hi, count: data.length }];
    const k = Math.ceil(Math.log2(data.length)) + 1;
    const w = (hi - lo) / k;
    const counts = new Array(k).fill(0);
    for (const v of data) counts[Math.min(Math.floor((v - lo) / w), k - 1)]++;
    return counts.map((count, i) => ({ lower: lo + i * w, upper: i === k - 1 ? hi : lo + (i + 1) * w, count }));
  }
  function regression(x, y) {
    const n = x.length;
    if (n < 2 || y.length !== n) return null;
    const mx = x.reduce((a, b) => a + b, 0) / n, my = y.reduce((a, b) => a + b, 0) / n;
    let sxx = 0, syy = 0, sxy = 0;
    for (let i = 0; i < n; i++) { sxx += (x[i] - mx) ** 2; syy += (y[i] - my) ** 2; sxy += (x[i] - mx) * (y[i] - my); }
    if (!(sxx > 0)) return null;
    const slope = sxy / sxx;
    const r = syy > 0 ? sxy / Math.sqrt(sxx * syy) : 1;
    return { n, slope, intercept: my - slope * mx, r, rSquared: r * r, predict: (v) => slope * v + (my - slope * mx) };
  }

  // ---- distributions -----------------------------------------------------------------------------------------------
  function erf(x) {
    if (Number.isNaN(x)) return NaN;
    if (x < 0) return -erf(-x);
    if (x > 6) return 1;
    if (x <= 2.5) {
      let term = x, sum = x, n = 0;
      for (;;) { n++; term *= (-x * x) / n; const add = term / (2 * n + 1); sum += add; if (Math.abs(add) < 1e-17 * Math.abs(sum) || n > 200) break; }
      return (2 / Math.sqrt(Math.PI)) * sum;
    }
    return 1 - erfc(x);
  }
  function erfc(x) {
    if (Number.isNaN(x)) return NaN;
    if (x < 2.5) return 1 - erf(x);
    let f = x;
    for (let k = 80; k >= 1; k--) f = x + k / 2 / f;
    return Math.exp(-x * x) / (Math.sqrt(Math.PI) * f);
  }
  const normalCDF = (x, mean = 0, sd = 1) => (sd > 0 ? 0.5 * erfc(-(x - mean) / sd / Math.SQRT2) : (x < mean ? 0 : 1));
  const normalPDF = (x, mean = 0, sd = 1) => (sd > 0 ? Math.exp(-0.5 * ((x - mean) / sd) ** 2) / (sd * Math.sqrt(2 * Math.PI)) : 0);
  function normalInverse(p, mean = 0, sd = 1) {
    if (!(p > 0 && p < 1 && sd > 0)) return null;
    const a = [-3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02, 1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00];
    const b = [-5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02, 6.680131188771972e+01, -1.328068155288572e+01];
    const c = [-7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00, -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00];
    const d = [7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00, 3.754408661907416e+00];
    const low = 0.02425;
    let x;
    if (p < low) { const q = Math.sqrt(-2 * Math.log(p)); x = (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1); }
    else if (p <= 1 - low) { const q = p - 0.5, r = q * q; x = (((((a[0] * r + a[1]) * r + a[2]) * r + a[3]) * r + a[4]) * r + a[5]) * q / (((((b[0] * r + b[1]) * r + b[2]) * r + b[3]) * r + b[4]) * r + 1); }
    else { const q = Math.sqrt(-2 * Math.log(1 - p)); x = -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1); }
    const e = 0.5 * erfc(-x / Math.SQRT2) - p;
    const u = e * Math.sqrt(2 * Math.PI) * Math.exp((x * x) / 2);
    x -= u / (1 + (x * u) / 2);
    return mean + sd * x;
  }
  function logGamma(x) {
    const cof = [76.18009172947146, -86.50532032941677, 24.01409824083091, -1.231739572450155, 0.1208650973866179e-2, -0.5395239384953e-5];
    let y = x, tmp = x + 5.5;
    tmp -= (x + 0.5) * Math.log(tmp);
    let ser = 1.000000000190015;
    for (const c of cof) { y += 1; ser += c / y; }
    return -tmp + Math.log((2.5066282746310005 * ser) / x);
  }
  function betacf(a, b, x) {
    const fpmin = 1e-300, eps = 1e-15, qab = a + b, qap = a + 1, qam = a - 1;
    let c = 1, d = 1 - (qab * x) / qap;
    if (Math.abs(d) < fpmin) d = fpmin;
    d = 1 / d;
    let hh = d;
    for (let m = 1; m <= 300; m++) {
      const m2 = 2 * m;
      let aa = (m * (b - m) * x) / ((qam + m2) * (a + m2));
      d = 1 + aa * d; if (Math.abs(d) < fpmin) d = fpmin;
      c = 1 + aa / c; if (Math.abs(c) < fpmin) c = fpmin;
      d = 1 / d; hh *= d * c;
      aa = (-(a + m) * (qab + m) * x) / ((a + m2) * (qap + m2));
      d = 1 + aa * d; if (Math.abs(d) < fpmin) d = fpmin;
      c = 1 + aa / c; if (Math.abs(c) < fpmin) c = fpmin;
      d = 1 / d;
      const del = d * c;
      hh *= del;
      if (Math.abs(del - 1) < eps) break;
    }
    return hh;
  }
  function incompleteBeta(a, b, x) {
    if (x <= 0) return 0;
    if (x >= 1) return 1;
    const bt = Math.exp(logGamma(a + b) - logGamma(a) - logGamma(b) + a * Math.log(x) + b * Math.log(1 - x));
    return x < (a + 1) / (a + b + 2) ? (bt * betacf(a, b, x)) / a : 1 - (bt * betacf(b, a, 1 - x)) / b;
  }
  const tCDF = (t, df) => { if (!(df > 0)) return NaN; const tail = 0.5 * incompleteBeta(df / 2, 0.5, df / (df + t * t)); return t >= 0 ? 1 - tail : tail; };
  const tTwoTailed = (t, df) => (df > 0 ? incompleteBeta(df / 2, 0.5, df / (df + t * t)) : NaN);
  const tPDF = (t, df) => (df > 0 ? (Math.exp(logGamma((df + 1) / 2) - logGamma(df / 2)) / Math.sqrt(df * Math.PI)) * (1 + (t * t) / df) ** (-(df + 1) / 2) : 0);
  function tQuantile(p, df) {
    if (!(p > 0 && p < 1 && df > 0)) return null;
    if (p === 0.5) return 0;
    if (p < 0.5) { const q = tQuantile(1 - p, df); return q === null ? null : -q; }
    let lo = 0, hi = 1;
    while (tCDF(hi, df) < p && hi < 1e12) hi *= 2;
    for (let i = 0; i < 200; i++) { const mid = (lo + hi) / 2; if (mid === lo || mid === hi) break; if (tCDF(mid, df) < p) lo = mid; else hi = mid; }
    return (lo + hi) / 2;
  }
  const tCritical = (alpha, df) => tQuantile(1 - alpha / 2, df);

  // ---- t-tests ------------------------------------------------------------------------------------------------------
  const sample = (n, mean, sd) => (n >= 2 && sd >= 0 && Number.isFinite(mean) && Number.isFinite(sd) ? { n, mean, sd } : null);
  const sampleOf = (data) => { const d = describe(data); return d && d.n >= 2 ? sample(d.n, d.mean, d.sampleSD) : null; };
  function oneSample(s, mu0) {
    if (!s || !(s.sd > 0)) return null;
    const se = s.sd / Math.sqrt(s.n), df = s.n - 1, t = (s.mean - mu0) / se, crit = tCritical(0.05, df);
    if (crit === null) return null;
    return { t, df, p2: tTwoTailed(t, df), estimate: s.mean, se, lo: s.mean - crit * se, hi: s.mean + crit * se };
  }
  function welch(a, b) {
    if (!a || !b) return null;
    const va = (a.sd * a.sd) / a.n, vb = (b.sd * b.sd) / b.n;
    if (!(va + vb > 0)) return null;
    const se = Math.sqrt(va + vb), df = ((va + vb) ** 2) / ((va * va) / (a.n - 1) + (vb * vb) / (b.n - 1)), diff = a.mean - b.mean, t = diff / se, crit = tCritical(0.05, df);
    if (crit === null) return null;
    return { t, df, p2: tTwoTailed(t, df), estimate: diff, se, lo: diff - crit * se, hi: diff + crit * se };
  }
  function paired(x, y) {
    if (x.length !== y.length || x.length < 2) return null;
    return oneSample(sampleOf(x.map((v, i) => v - y[i])), 0);
  }

  // ---- drawing -----------------------------------------------------------------------------------------------------
  const svgEl = (tag, attrs = {}) => { const e = document.createElementNS(NS, tag); for (const [k, v] of Object.entries(attrs)) e.setAttribute(k, v); return e; };
  function histogramSvg(bins) {
    const W = 520, H = 150, top = 16, bot = 20;
    const s = svgEl('svg', { viewBox: `0 0 ${W} ${H}`, class: 'bcv-stats-t__chart', role: 'img', 'aria-label': 'Histogram' });
    const max = Math.max(1, ...bins.map((b) => b.count));
    const bw = W / bins.length;
    bins.forEach((b, i) => {
      const hh = ((H - top - bot) * b.count) / max;
      s.append(svgEl('rect', { x: (i * bw + 2).toFixed(1), y: (H - bot - hh).toFixed(1), width: Math.max(1, bw - 4).toFixed(1), height: hh.toFixed(1), rx: 3, class: 'bcv-stats-t__bar' }));
      const t = svgEl('text', { x: (i * bw + bw / 2).toFixed(1), y: (H - bot - hh - 4).toFixed(1), 'text-anchor': 'middle', class: 'bcv-stats-t__lbl' });
      t.textContent = String(b.count);
      s.append(t);
    });
    const lo = svgEl('text', { x: 2, y: H - 4, class: 'bcv-stats-t__lbl' }); lo.textContent = text(bins[0].lower, 4);
    const hi = svgEl('text', { x: W - 2, y: H - 4, 'text-anchor': 'end', class: 'bcv-stats-t__lbl' }); hi.textContent = text(bins[bins.length - 1].upper, 4);
    s.append(lo, hi);
    return s;
  }
  function boxSvg(d) {
    const W = 520, H = 64, pad = 14;
    const s = svgEl('svg', { viewBox: `0 0 ${W} ${H}`, class: 'bcv-stats-t__chart', role: 'img', 'aria-label': 'Box plot' });
    const span = d.max - d.min || 1;
    const X = (v) => pad + ((v - d.min) / span) * (W - 2 * pad);
    const lowW = Math.min(...d.sorted.filter((v) => !d.outliers.includes(v))), highW = Math.max(...d.sorted.filter((v) => !d.outliers.includes(v)));
    const y = 28;
    s.append(svgEl('line', { x1: X(lowW), x2: X(d.q1), y1: y, y2: y, class: 'bcv-stats-t__whisk' }), svgEl('line', { x1: X(d.q3), x2: X(highW), y1: y, y2: y, class: 'bcv-stats-t__whisk' }));
    s.append(svgEl('line', { x1: X(lowW), x2: X(lowW), y1: y - 8, y2: y + 8, class: 'bcv-stats-t__whisk' }), svgEl('line', { x1: X(highW), x2: X(highW), y1: y - 8, y2: y + 8, class: 'bcv-stats-t__whisk' }));
    s.append(svgEl('rect', { x: X(d.q1), y: y - 14, width: Math.max(1, X(d.q3) - X(d.q1)), height: 28, rx: 5, class: 'bcv-stats-t__box' }));
    s.append(svgEl('line', { x1: X(d.median), x2: X(d.median), y1: y - 14, y2: y + 14, class: 'bcv-stats-t__med' }));
    for (const o of d.outliers) s.append(svgEl('circle', { cx: X(o), cy: y, r: 3.5, class: 'bcv-stats-t__out' }));
    return s;
  }
  function scatterSvg(x, y, line) {
    const W = 520, H = 220, pad = 24;
    const s = svgEl('svg', { viewBox: `0 0 ${W} ${H}`, class: 'bcv-stats-t__chart', role: 'img', 'aria-label': 'Scatter plot' });
    const x0 = Math.min(...x), x1 = Math.max(...x), y0 = Math.min(...y), y1 = Math.max(...y);
    const sx = (v) => pad + ((v - x0) / ((x1 - x0) || 1)) * (W - 2 * pad), sy = (v) => H - pad - ((v - y0) / ((y1 - y0) || 1)) * (H - 2 * pad);
    s.append(svgEl('line', { x1: pad, x2: W - pad, y1: H - pad, y2: H - pad, class: 'bcv-stats-t__axis' }), svgEl('line', { x1: pad, x2: pad, y1: pad, y2: H - pad, class: 'bcv-stats-t__axis' }));
    if (line) s.append(svgEl('line', { x1: sx(x0), y1: sy(line.predict(x0)), x2: sx(x1), y2: sy(line.predict(x1)), class: 'bcv-stats-t__fit' }));
    x.forEach((v, i) => s.append(svgEl('circle', { cx: sx(v).toFixed(1), cy: sy(y[i]).toFixed(1), r: 4, class: 'bcv-stats-t__dot' })));
    return s;
  }
  function curveSvg(pdf, from, to, shadeFrom, shadeTo) {
    const W = 520, H = 130, pad = 8, N = 120;
    const s = svgEl('svg', { viewBox: `0 0 ${W} ${H}`, class: 'bcv-stats-t__chart', role: 'img', 'aria-label': 'The curve, the probability shaded' });
    const xs = Array.from({ length: N + 1 }, (_, i) => from + ((to - from) * i) / N);
    const ys = xs.map(pdf);
    const max = Math.max(...ys) || 1;
    const X = (v) => pad + ((v - from) / (to - from)) * (W - 2 * pad), Y = (v) => H - pad - (v / max) * (H - 2 * pad);
    const lo = Math.max(from, shadeFrom), hi = Math.min(to, shadeTo);
    if (hi > lo) {
      const pts = xs.filter((v) => v >= lo && v <= hi);
      const d = `M ${X(lo)} ${Y(0)} L ${X(lo)} ${Y(pdf(lo))} ${pts.map((v) => `L ${X(v).toFixed(1)} ${Y(pdf(v)).toFixed(1)}`).join(' ')} L ${X(hi)} ${Y(pdf(hi))} L ${X(hi)} ${Y(0)} Z`;
      s.append(svgEl('path', { d, class: 'bcv-stats-t__shade' }));
    }
    s.append(svgEl('path', { d: xs.map((v, i) => `${i ? 'L' : 'M'} ${X(v).toFixed(1)} ${Y(ys[i]).toFixed(1)}`).join(' '), class: 'bcv-stats-t__curve' }));
    return s;
  }

  // ---- the tool ----------------------------------------------------------------------------------------------------
  const PAGES = [['describe', 'Describe'], ['regression', 'Regression'], ['dist', 'Distributions'], ['tests', 'Tests']];
  async function open(app, { from = null } = {}) {
    const tool = T.toolOf('stats');
    const kept = (await T.load('stats:state', null)) || {};
    const st = Object.assign({ page: 'describe', data: '', x: '', y: '', px: '', dist: 'normal', mean: '0', sd: '1', a: '-1', b: '1', p: '0.95', df: '10', t: '2', test: 'one', d1: '', d2: '', mu0: '0' }, kept);
    let t0 = 0;
    const keep = () => { clearTimeout(t0); t0 = setTimeout(() => T.save('stats:state', st), 250); };
    const body = U.el('bcv-stats-t');
    const p = T.popup({ tool, title: 'Statistics', sub: 'Mean, SD, regression, z and t.', width: 760, body, from });
    const tabs = U.el('bcv-stats-t__tabs');
    const page = U.el('bcv-stats-t__page');
    body.append(...T.rise([tabs, page]));
    const area = (key, ph, rows = 4) => { const a = h('textarea', { class: 'bcv-input bcv-stats-t__area', rows, placeholder: ph, spellcheck: 'false', dataset: { field: key } }); a.value = st[key]; a.addEventListener('input', () => { st[key] = a.value; keep(); paintResult(); }); return a; };
    const field = (key, label, ph = '') => { const i = T.input({ inputmode: 'decimal', placeholder: ph, 'aria-label': label, dataset: { field: key }, autocomplete: 'off' }); i.value = st[key]; i.addEventListener('input', () => { st[key] = i.value; keep(); paintResult(); }); return U.el('bcv-stats-t__field', [U.text('bcv-stats-t__flabel', label, 'span'), i]); };
    const rows = (list) => U.el('bcv-stats-t__rows', list.filter(Boolean).map(([k, v, note]) => h('div', { class: 'bcv-stats-t__row', dataset: { k } }, [h('span', { class: 'bcv-stats-t__k', text: k }), h('span', { class: 'bcv-stats-t__v', text: v }), note ? h('span', { class: 'bcv-stats-t__note', text: note }) : null])));
    const result = U.el('bcv-stats-t__result');
    let paintResult = () => {};

    function paint() {
      tabs.replaceChildren(T.seg(PAGES, st.page, (v) => { st.page = v; keep(); paint(); }));
      const pg = st.page;
      if (pg === 'describe') {
        page.replaceChildren(T.card([T.label('Data'), area('data', 'Numbers set apart by spaces, commas or new lines — or a column pasted from a spreadsheet.', 5)]), result);
        paintResult = () => {
          const d = describe(numbers(st.data));
          if (!d) { result.replaceChildren(T.hint('Type or paste numbers to see their mean, median, spread and more.')); return; }
          result.replaceChildren(
            T.card([U.el('bcv-stats-t__big', [[`Mean`, text(d.mean)], ['Median', text(d.median)], ['Sample SD', text(d.sampleSD)]].map(([k, v]) => h('div', { class: 'bcv-stats-t__bigone', dataset: { k } }, [h('span', { class: 'bcv-stats-t__bigv', text: v }), h('span', { class: 'bcv-stats-t__bigk', text: k })]))), U.text('bcv-tool__hint', `n = ${d.n}`)]),
            U.el('bcv-stats-t__cols', [
              T.card([T.label('Centre and spread'), rows([['Mode', d.modes.length ? d.modes.map((v) => text(v)).join(', ') : 'None'], ['Minimum', text(d.min)], ['Q1', text(d.q1)], ['Q3', text(d.q3)], ['Maximum', text(d.max)], ['Range', text(d.range)], ['IQR', text(d.iqr)]])]),
              T.card([T.label('More'), rows([['Population SD', text(d.populationSD)], ['Sample variance', text(d.sampleVariance)], ['Population variance', text(d.populationVariance)], ['Standard error', text(d.standardError)], ['Sum', text(d.sum)], ['Sum of squares', text(d.sumOfSquares)], ['Outliers', d.outliers.length ? d.outliers.map((v) => text(v)).join(', ') : 'None', d.outliers.length ? 'past 1.5 IQR' : null]])]),
            ]),
            T.card([T.label('Histogram'), histogramSvg(histogram(d.sorted)), T.label('Box plot'), boxSvg(d)]),
          );
        };
      } else if (pg === 'regression') {
        const pred = field('px', 'x =', 'a value of x');
        const predv = U.text('bcv-stats-t__predv', 'ŷ = —', 'span');
        const predCard = T.card([T.label('Predict'), U.el('bcv-stats-t__pred', [pred, predv])]);
        page.replaceChildren(U.el('bcv-stats-t__cols', [T.card([T.label('x'), area('x', 'One column of x values', 6)]), T.card([T.label('y'), area('y', 'The y values, in the same order', 6)])]), result, predCard);
        let drawn = '';
        paintResult = () => {
          const x = numbers(st.x), y = numbers(st.y);
          const line = regression(x, y);
          predCard.hidden = !line;
          const px = number(st.px);
          predv.textContent = line && px !== null ? `ŷ = ${text(line.predict(px))}` : 'ŷ = —';
          const key = `${st.x}|${st.y}`;
          if (key === drawn) return; // (only the prediction changed: the line and its plot stay as they are)
          drawn = key;
          if (!line) { result.replaceChildren(T.hint(x.length && y.length && x.length !== y.length ? `The columns differ in length: ${x.length} x and ${y.length} y.` : 'Two columns of the same length, and the line through them appears here.')); return; }
          result.replaceChildren(
            T.card([U.text('bcv-stats-t__eq', `ŷ = ${text(line.slope)}x ${line.intercept < 0 ? '−' : '+'} ${text(Math.abs(line.intercept))}`), rows([['Slope', text(line.slope)], ['Intercept', text(line.intercept)], ['r', text(line.r)], ['r²', text(line.rSquared)], ['n', String(line.n)]])]),
            T.card([T.label('Scatter, with the line'), scatterSvg(x, y, line)]),
          );
        };
      } else if (pg === 'dist') {
        const which = T.seg([['normal', 'Normal'], ['t', 'Student’s t']], st.dist, (v) => { st.dist = v; keep(); paint(); });
        const inputs = st.dist === 'normal'
          ? U.el('bcv-stats-t__grid', [field('mean', 'Mean μ'), field('sd', 'SD σ'), field('a', 'From a'), field('b', 'To b'), field('p', 'Probability p')])
          : U.el('bcv-stats-t__grid', [field('df', 'Degrees of freedom'), field('t', 't')]);
        page.replaceChildren(U.el('bcv-stats-t__which', which), T.card([inputs]), result);
        paintResult = () => {
          if (st.dist === 'normal') {
            const m = number(st.mean), s = number(st.sd), a = number(st.a), b = number(st.b), pp = number(st.p);
            if (m === null || s === null || !(s > 0)) { result.replaceChildren(T.hint('A mean and a standard deviation above zero, and the probabilities appear here.')); return; }
            const list = [];
            if (a !== null) list.push([`P(X < ${text(a)})`, fixed(normalCDF(a, m, s), 4)], [`P(X > ${text(a)})`, fixed(1 - normalCDF(a, m, s), 4)], [`z of ${text(a)}`, text((a - m) / s)]);
            if (a !== null && b !== null) list.push([`P(${text(Math.min(a, b))} < X < ${text(Math.max(a, b))})`, fixed(Math.abs(normalCDF(b, m, s) - normalCDF(a, m, s)), 4)]);
            const inv = pp === null ? null : normalInverse(pp, m, s);
            list.push([`x with P(X < x) = ${pp === null ? '—' : text(pp)}`, inv === null ? '—' : text(inv)]);
            const lo = m - 4 * s, hi = m + 4 * s;
            result.replaceChildren(T.card([curveSvg((v) => normalPDF(v, m, s), lo, hi, a !== null && b !== null ? Math.min(a, b) : lo, a !== null && b !== null ? Math.max(a, b) : a ?? lo)]), T.card([rows(list)]));
          } else {
            const df = number(st.df), tt = number(st.t);
            if (df === null || !(df > 0)) { result.replaceChildren(T.hint('Degrees of freedom above zero, and the p-values appear here.')); return; }
            const list = [];
            if (tt !== null) list.push([`P(T < ${text(tt)})`, fixed(tCDF(tt, df), 4)], ['Two-tailed p', pText(tTwoTailed(tt, df))], ['One-tailed p', pText(tTwoTailed(tt, df) / 2)]);
            list.push(['Critical t (α = 0.05, two-tailed)', text(tCritical(0.05, df) ?? NaN)]);
            result.replaceChildren(T.card([curveSvg((v) => tPDF(v, df), -5, 5, tt === null ? 0 : -Math.abs(tt) - 99, tt === null ? 0 : -Math.abs(tt))]), T.card([rows(list)]));
          }
        };
      } else {
        const which = T.seg([['one', 'One sample'], ['two', 'Two samples (Welch)'], ['paired', 'Paired']], st.test, (v) => { st.test = v; keep(); paint(); });
        const ins = st.test === 'one'
          ? [T.card([T.label('Sample'), area('d1', 'Numbers set apart by spaces, commas or new lines.', 4), field('mu0', 'Against μ₀')])]
          : [U.el('bcv-stats-t__cols', [T.card([T.label(st.test === 'paired' ? 'Before' : 'Sample 1'), area('d1', st.test === 'paired' ? 'One value a row, in order' : 'Numbers', 5)]), T.card([T.label(st.test === 'paired' ? 'After' : 'Sample 2'), area('d2', st.test === 'paired' ? 'The same rows, after' : 'Numbers', 5)])])];
        page.replaceChildren(U.el('bcv-stats-t__which', which), ...ins, result);
        paintResult = () => {
          const a = numbers(st.d1), b = numbers(st.d2);
          const r = st.test === 'one' ? oneSample(sampleOf(a), number(st.mu0) ?? 0) : st.test === 'two' ? welch(sampleOf(a), sampleOf(b)) : paired(a, b);
          if (!r) { result.replaceChildren(T.hint(st.test === 'paired' && a.length !== b.length && a.length && b.length ? 'A paired test needs the same number of values before and after.' : 'At least two values in each, with some spread, and the test appears here.')); return; }
          const est = st.test === 'one' ? ['Sample mean', text(r.estimate)] : st.test === 'two' ? ['Difference of means', text(r.estimate), 'sample 1 − sample 2'] : ['Mean difference', text(r.estimate), 'before − after'];
          result.replaceChildren(T.card([
            U.el('bcv-stats-t__big', [['t', text(r.t)], ['df', text(r.df, 4)], ['p (two-tailed)', pText(r.p2)]].map(([k, v]) => h('div', { class: 'bcv-stats-t__bigone', dataset: { k } }, [h('span', { class: 'bcv-stats-t__bigv', text: v }), h('span', { class: 'bcv-stats-t__bigk', text: k })]))),
            rows([est, ['Standard error', text(r.se)], ['95% interval', `${text(r.lo)} to ${text(r.hi)}`], ['One-tailed p', pText(r.p2 / 2)]]),
            U.text('bcv-tool__hint bcv-pretty', r.p2 < 0.05 ? 'Significant at the 5% level.' : 'Not significant at the 5% level.'),
          ]));
        };
      }
      paintResult();
    }
    paint();
    return p;
  }

  BCV.toolsStats = { open, describe, histogram, regression, normalCDF, normalInverse, tCDF, tTwoTailed, tQuantile, oneSample, welch, paired, sampleOf, numbers, text };
})();
