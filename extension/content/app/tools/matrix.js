/* (2.99.81) The Matrix Calculator, the Mac's (MatrixCalculator.swift, its arithmetic in Model/MatrixModel.swift): two
 * matrices A and B typed into grids (1 to 6 rows and columns each; a block of rows pasted into a cell fills the grid),
 * an operation chosen from a row of chips — A + B, A − B, A × B, k·A, Aᵀ, det A, A⁻¹, rank, RREF, trace, A·x = b with B
 * as the column b, the eigenvalues, Aⁿ — and the result under them: exact fractions while the cells are whole numbers,
 * fractions or short decimals (a switch shows them as decimals), decimals otherwise; the RREF's row operations listed,
 * each with the matrix after it. The fractions are BigInts here, so they never overflow into decimals as the Mac's
 * 62-bit ones can; the eigenvalues are the Mac's Hessenberg reduction and shifted QR, line for line. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const T = BCV.tools;
  const MOST = 6;

  // ---- numbers: an exact fraction, or a decimal ---------------------------------------------------------------------
  const gcd = (a, b) => { a = a < 0n ? -a : a; b = b < 0n ? -b : b; while (b) [a, b] = [b, a % b]; return a || 1n; };
  const frac = (n, d = 1n) => { if (d === 0n) return null; if (d < 0n) { n = -n; d = -d; } const g = gcd(n, d); return { ex: true, n: n / g, d: d / g }; };
  const dec = (v) => ({ ex: false, v });
  const ZERO = frac(0n), ONE = frac(1n);
  const val = (x) => (x.ex ? Number(x.n) / Number(x.d) : x.v);
  const isZero = (x) => (x.ex ? x.n === 0n : Math.abs(x.v) < 1e-12);
  /** What was typed: "3", "-2", "3/4", "0.25", "1,5" exact; "1e5" and the like a decimal; nil when not a number. */
  function read(text) {
    const t = String(text ?? '').trim().replace(/−/g, '-');
    if (!t) return null;
    let m = /^([-+]?\d+)\s*\/\s*([-+]?\d+)$/.exec(t);
    if (m) return BigInt(m[2]) === 0n ? null : frac(BigInt(m[1]), BigInt(m[2]));
    if (/^[-+]?\d+$/.test(t)) return frac(BigInt(t));
    m = /^([-+]?)(\d*)[.,](\d{1,12})$/.exec(t);
    if (m) { const den = 10n ** BigInt(m[3].length); const n = BigInt(m[2] || '0') * den + BigInt(m[3]); return frac(m[1] === '-' ? -n : n, den); }
    const v = Number(t);
    return Number.isFinite(v) && /^[-+]?(\d+\.?\d*|\.\d+)(e[-+]?\d+)?$/i.test(t) ? dec(v) : null;
  }
  const add = (a, b) => (a.ex && b.ex ? frac(a.n * b.d + b.n * a.d, a.d * b.d) : dec(val(a) + val(b)));
  const neg = (a) => (a.ex ? frac(-a.n, a.d) : dec(-a.v));
  const sub = (a, b) => add(a, neg(b));
  const mul = (a, b) => (a.ex && b.ex ? frac(a.n * b.n, a.d * b.d) : dec(val(a) * val(b)));
  const div = (a, b) => (isZero(b) ? null : a.ex && b.ex ? frac(a.n * b.d, a.d * b.n) : dec(val(a) / val(b)));
  /** Six significant figures at most (the Mac's StudyMath.text), "−" for a minus. */
  const text = (v) => BCV.toolsStats?.text ? BCV.toolsStats.text(v) : (Number.isFinite(v) ? String(Number(v.toPrecision(6))).replace(/-/g, '−') : '—');
  const show = (x, decimals) => (x.ex && !decimals ? (x.d === 1n ? String(x.n) : `${x.n}/${x.d}`).replace(/-/g, '−') : text(val(x)));

  // ---- matrices ------------------------------------------------------------------------------------------------------
  const M = (rows, cols, cells) => ({ rows, cols, cells: cells && cells.length === rows * cols ? cells : new Array(rows * cols).fill(ZERO) });
  const at = (m, r, c) => m.cells[r * m.cols + c];
  const put = (m, r, c, v) => { m.cells[r * m.cols + c] = v; };
  const copy = (m) => M(m.rows, m.cols, [...m.cells]);
  const exact = (m) => m.cells.every((x) => x.ex);
  const identity = (n) => { const m = M(n, n); for (let i = 0; i < n; i++) put(m, i, i, ONE); return m; };
  const SUB = '₀₁₂₃₄₅₆₇₈₉';
  const subs = (i) => String(i).split('').map((d) => SUB[d]).join('');
  function swapped(m, r, s) { const o = copy(m); for (let c = 0; c < m.cols; c++) { put(o, r, c, at(m, s, c)); put(o, s, c, at(m, r, c)); } return o; }
  function pivotRow(a, from, c) {
    if (exact(a)) { for (let i = from; i < a.rows; i++) if (!isZero(at(a, i, c))) return i; return -1; }
    let p = -1, best = 1e-12;
    for (let i = from; i < a.rows; i++) if (Math.abs(val(at(a, i, c))) > best) { best = Math.abs(val(at(a, i, c))); p = i; }
    return p;
  }
  function rref(m) {
    let a = copy(m);
    const steps = [], pivots = [];
    let r = 0;
    for (let c = 0; c < a.cols && r < a.rows; c++) {
      const p = pivotRow(a, r, c);
      if (p < 0) continue;
      if (p !== r) { a = swapped(a, p, r); steps.push({ text: `R${subs(r + 1)} ↔ R${subs(p + 1)}`, after: copy(a) }); }
      const piv = at(a, r, c);
      if (val(piv) !== 1) { for (let j = 0; j < a.cols; j++) put(a, r, j, div(at(a, r, j), piv) || dec(NaN)); steps.push({ text: `R${subs(r + 1)} ← R${subs(r + 1)} ÷ ${show(piv)}`, after: copy(a) }); }
      for (let i = 0; i < a.rows; i++) {
        if (i === r || isZero(at(a, i, c))) continue;
        const f = at(a, i, c);
        for (let j = 0; j < a.cols; j++) put(a, i, j, sub(at(a, i, j), mul(f, at(a, r, j))));
        const negf = val(f) < 0, mag = show(negf ? neg(f) : f);
        steps.push({ text: `R${subs(i + 1)} ← R${subs(i + 1)} ${negf ? '+' : '−'} ${mag === '1' ? '' : mag}R${subs(r + 1)}`, after: copy(a) });
      }
      pivots.push(c);
      r++;
    }
    return { result: a, steps, pivots };
  }
  function determinant(m) {
    if (m.rows !== m.cols) return null;
    let a = copy(m), det = ONE;
    const n = a.rows;
    for (let c = 0; c < n; c++) {
      const p = pivotRow(a, c, c);
      if (p < 0) return ZERO;
      if (p !== c) { a = swapped(a, p, c); det = neg(det); }
      const piv = at(a, c, c);
      det = mul(det, piv);
      for (let i = c + 1; i < n; i++) {
        if (isZero(at(a, i, c))) continue;
        const f = div(at(a, i, c), piv) || dec(NaN);
        for (let j = c; j < n; j++) put(a, i, j, sub(at(a, i, j), mul(f, at(a, c, j))));
      }
    }
    return det;
  }
  function inverse(m) {
    if (m.rows !== m.cols) return null;
    const n = m.rows, aug = M(n, 2 * n);
    for (let r = 0; r < n; r++) { for (let c = 0; c < n; c++) put(aug, r, c, at(m, r, c)); put(aug, r, n + r, ONE); }
    const red = rref(aug);
    if (red.pivots.length < n || red.pivots.slice(0, n).some((p, i) => p !== i)) return null;
    const inv = M(n, n);
    for (let r = 0; r < n; r++) for (let c = 0; c < n; c++) put(inv, r, c, at(red.result, r, n + c));
    return inv;
  }
  const transpose = (m) => { const t = M(m.cols, m.rows); for (let r = 0; r < m.rows; r++) for (let c = 0; c < m.cols; c++) put(t, c, r, at(m, r, c)); return t; };
  const trace = (m) => (m.rows !== m.cols ? null : Array.from({ length: m.rows }, (_, i) => at(m, i, i)).reduce(add, ZERO));
  const addM = (a, b) => (a.rows === b.rows && a.cols === b.cols ? M(a.rows, a.cols, a.cells.map((x, i) => add(x, b.cells[i]))) : null);
  const subM = (a, b) => (a.rows === b.rows && a.cols === b.cols ? M(a.rows, a.cols, a.cells.map((x, i) => sub(x, b.cells[i]))) : null);
  function multiply(a, b) {
    if (a.cols !== b.rows) return null;
    const p = M(a.rows, b.cols);
    for (let r = 0; r < a.rows; r++) for (let c = 0; c < b.cols; c++) { let s = ZERO; for (let k = 0; k < a.cols; k++) s = add(s, mul(at(a, r, k), at(b, k, c))); put(p, r, c, s); }
    return p;
  }
  const scale = (a, k) => M(a.rows, a.cols, a.cells.map((x) => mul(x, k)));
  function power(a, n) {
    if (a.rows !== a.cols || Math.abs(n) > 60) return null;
    if (n === 0) return identity(a.rows);
    const base = n > 0 ? a : inverse(a);
    if (!base) return null;
    let res = base;
    for (let i = 1; i < Math.abs(n); i++) res = multiply(res, base) || res;
    return res;
  }
  function solve(a, b) {
    if (b.cols !== 1 || b.rows !== a.rows) return null;
    const aug = M(a.rows, a.cols + 1);
    for (let r = 0; r < a.rows; r++) { for (let c = 0; c < a.cols; c++) put(aug, r, c, at(a, r, c)); put(aug, r, a.cols, at(b, r, 0)); }
    const red = rref(aug);
    if (red.pivots.includes(a.cols)) return { kind: 'none' };
    if (red.pivots.length < a.cols) return { kind: 'many', free: a.cols - red.pivots.length };
    const x = M(a.cols, 1);
    red.pivots.forEach((c, i) => put(x, c, 0, at(red.result, i, a.cols)));
    return { kind: 'unique', x };
  }

  // ---- eigenvalues: Hessenberg, then the shifted QR (Numerical Recipes' elmhes and hqr, 1-based as written) -----------
  function hessenberg(a, n) {
    for (let m = 2; m < n; m++) {
      let x = 0, i = m;
      for (let j = m; j <= n; j++) if (Math.abs(a[j][m - 1]) > Math.abs(x)) { x = a[j][m - 1]; i = j; }
      if (i !== m) {
        for (let j = m - 1; j <= n; j++) [a[i][j], a[m][j]] = [a[m][j], a[i][j]];
        for (let j = 1; j <= n; j++) [a[j][i], a[j][m]] = [a[j][m], a[j][i]];
      }
      if (x !== 0) {
        for (let ii = m + 1; ii <= n; ii++) {
          let y = a[ii][m - 1];
          if (y !== 0) { y /= x; a[ii][m - 1] = y; for (let j = m; j <= n; j++) a[ii][j] -= y * a[m][j]; for (let j = 1; j <= n; j++) a[j][m] += y * a[j][ii]; }
        }
      }
    }
    for (let i = 1; i <= n; i++) for (let j = 1; j <= n; j++) if (j < i - 1) a[i][j] = 0;
  }
  function hqr(a, n) {
    const wr = new Array(n + 1).fill(0), wi = new Array(n + 1).fill(0);
    let anorm = 0;
    for (let i = 1; i <= n; i++) for (let j = Math.max(i - 1, 1); j <= n; j++) anorm += Math.abs(a[i][j]);
    let nn = n, t = 0, p = 0, q = 0, r = 0, s = 0, w = 0, x = 0, y = 0, z = 0;
    while (nn >= 1) {
      let its = 0, l = 1;
      do {
        l = 1;
        for (let k = nn; k >= 2; k--) {
          s = Math.abs(a[k - 1][k - 1]) + Math.abs(a[k][k]);
          if (s === 0) s = anorm;
          if (Math.abs(a[k][k - 1]) + s === s) { a[k][k - 1] = 0; l = k; break; }
        }
        x = a[nn][nn];
        if (l === nn) { wr[nn] = x + t; wi[nn] = 0; nn -= 1; }
        else {
          y = a[nn - 1][nn - 1];
          w = a[nn][nn - 1] * a[nn - 1][nn];
          if (l === nn - 1) {
            p = 0.5 * (y - x); q = p * p + w; z = Math.sqrt(Math.abs(q)); x += t;
            if (q >= 0) { z = p + (p >= 0 ? Math.abs(z) : -Math.abs(z)); wr[nn - 1] = x + z; wr[nn] = wr[nn - 1]; if (z !== 0) wr[nn] = x - w / z; wi[nn - 1] = 0; wi[nn] = 0; }
            else { wr[nn - 1] = x + p; wr[nn] = x + p; wi[nn - 1] = -z; wi[nn] = z; }
            nn -= 2;
          } else {
            if (its === 30) return null;
            if (its === 10 || its === 20) { t += x; for (let i = 1; i <= nn; i++) a[i][i] -= x; s = Math.abs(a[nn][nn - 1]) + Math.abs(a[nn - 1][nn - 2]); x = 0.75 * s; y = x; w = -0.4375 * s * s; }
            its += 1;
            let m = nn - 2;
            while (m >= l) {
              z = a[m][m]; r = x - z; s = y - z;
              p = (r * s - w) / a[m + 1][m] + a[m][m + 1]; q = a[m + 1][m + 1] - z - r - s; r = a[m + 2][m + 1];
              s = Math.abs(p) + Math.abs(q) + Math.abs(r); p /= s; q /= s; r /= s;
              if (m === l) break;
              const u = Math.abs(a[m][m - 1]) * (Math.abs(q) + Math.abs(r));
              const v = Math.abs(p) * (Math.abs(a[m - 1][m - 1]) + Math.abs(z) + Math.abs(a[m + 1][m + 1]));
              if (u + v === v) break;
              m -= 1;
            }
            for (let i = m + 2; i <= nn; i++) { a[i][i - 2] = 0; if (i !== m + 2) a[i][i - 3] = 0; }
            for (let k = m; k <= nn - 1; k++) {
              if (k !== m) {
                p = a[k][k - 1]; q = a[k + 1][k - 1]; r = 0;
                if (k !== nn - 1) r = a[k + 2][k - 1];
                x = Math.abs(p) + Math.abs(q) + Math.abs(r);
                if (x !== 0) { p /= x; q /= x; r /= x; }
              }
              const root = Math.sqrt(p * p + q * q + r * r);
              s = p >= 0 ? root : -root;
              if (s !== 0) {
                if (k === m) { if (l !== m) a[k][k - 1] = -a[k][k - 1]; } else a[k][k - 1] = -s * x;
                p += s; x = p / s; y = q / s; z = r / s; q /= p; r /= p;
                for (let j = k; j <= nn; j++) {
                  p = a[k][j] + q * a[k + 1][j];
                  if (k !== nn - 1) { p += r * a[k + 2][j]; a[k + 2][j] -= p * z; }
                  a[k + 1][j] -= p * y; a[k][j] -= p * x;
                }
                const mmin = nn < k + 3 ? nn : k + 3;
                for (let i = l; i <= mmin; i++) {
                  p = x * a[i][k] + y * a[i][k + 1];
                  if (k !== nn - 1) { p += z * a[i][k + 2]; a[i][k + 2] -= p * r; }
                  a[i][k + 1] -= p * q; a[i][k] -= p;
                }
              }
            }
          }
        }
      } while (l < nn - 1);
    }
    return Array.from({ length: n }, (_, i) => ({ re: wr[i + 1], im: wi[i + 1] }));
  }
  function eigenvalues(m) {
    if (m.rows !== m.cols || !m.rows) return null;
    const n = m.rows;
    if (n === 1) return [{ re: val(at(m, 0, 0)), im: 0 }];
    const a = Array.from({ length: n + 1 }, () => new Array(n + 1).fill(0));
    for (let i = 1; i <= n; i++) for (let j = 1; j <= n; j++) a[i][j] = val(at(m, i - 1, j - 1));
    hessenberg(a, n);
    let vals = hqr(a, n);
    if (!vals) return null;
    const sc = Math.max(1, ...m.cells.map((x) => Math.abs(val(x))));
    vals = vals.map(({ re, im }) => {
      if (Math.abs(im) < 1e-9 * sc) im = 0;
      if (Math.abs(re - Math.round(re)) < 1e-9 * sc) re = Math.round(re);
      if (Math.abs(im - Math.round(im)) < 1e-9 * sc) im = Math.round(im);
      return { re: re === 0 ? 0 : re, im: im === 0 ? 0 : im };
    });
    return vals.sort((x, y) => ((x.im === 0) !== (y.im === 0) ? (x.im === 0 ? -1 : 1) : x.re !== y.re ? x.re - y.re : y.im - x.im));
  }
  /** The eigenvalues as shown: a conjugate pair written once, "2 ± 3i". */
  function pairText(values) {
    const out = [], used = new Set();
    const one = (v) => { if (v.im === 0) return text(v.re); const imag = Math.abs(v.im) === 1 ? '' : text(Math.abs(v.im)); return v.re === 0 ? `${v.im < 0 ? '−' : ''}${imag}i` : `${text(v.re)} ${v.im < 0 ? '−' : '+'} ${imag}i`; };
    values.forEach((v, i) => {
      if (used.has(i)) return;
      used.add(i);
      const j = v.im !== 0 ? values.findIndex((w, k) => !used.has(k) && w.re === v.re && w.im === -v.im) : -1;
      if (j >= 0) { used.add(j); const imag = Math.abs(v.im) === 1 ? '' : text(Math.abs(v.im)); out.push(v.re === 0 ? `±${imag}i` : `${text(v.re)} ± ${imag}i`); }
      else out.push(one(v));
    });
    return out;
  }

  // ---- the tool ------------------------------------------------------------------------------------------------------
  const OPS = [['add', 'A + B'], ['subtract', 'A − B'], ['multiply', 'A × B'], ['scale', 'k·A'], ['transpose', 'Aᵀ'], ['det', 'det A'], ['inverse', 'A⁻¹'], ['rank', 'rank A'], ['rref', 'RREF A'], ['trace', 'trace A'], ['solve', 'A·x = b'], ['eigen', 'Eigenvalues'], ['power', 'Aⁿ']];
  const USES_B = new Set(['add', 'subtract', 'multiply', 'solve']);
  const draft = (rows = 2, cols = 2) => ({ rows, cols, cells: Array.from({ length: MOST }, () => new Array(MOST).fill('')) });
  /** "1 2;3 4" into a draft (rows by ";" or new lines, cells by spaces, commas or tabs); null past 6 × 6 or ragged. */
  function draftOf(textIn) {
    const lines = String(textIn).replace(/;/g, '\n').split(/\r?\n/).map((l) => l.trim()).filter(Boolean);
    if (!lines.length || lines.length > MOST) return null;
    const grid = lines.map((l) => l.replace(/[,\t]/g, ' ').split(/\s+/).filter(Boolean));
    const w = grid[0].length;
    if (!w || w > MOST || grid.some((g) => g.length !== w)) return null;
    const d = draft(lines.length, w);
    grid.forEach((row, r) => row.forEach((v, c) => { d.cells[r][c] = v; }));
    return d;
  }
  function readDraft(d) {
    const out = [];
    for (let r = 0; r < d.rows; r++) for (let c = 0; c < d.cols; c++) {
      const t = String(d.cells[r][c] || '').trim();
      if (!t) { out.push(ZERO); continue; }
      const n = read(t);
      if (!n) return { bad: { r, c } };
      out.push(n);
    }
    return { m: M(d.rows, d.cols, out) };
  }

  async function open(app, { from = null } = {}) {
    const tool = T.toolOf('matrix');
    const kept = (await T.load('matrix:state', null)) || {};
    const fix = (d) => (d && Array.isArray(d.cells) && d.cells.length === MOST ? d : null);
    const st = { a: fix(kept.a) || draftOf('2 1;1 3'), b: fix(kept.b) || draftOf('1 0;0 1'), op: OPS.some(([k]) => k === kept.op) ? kept.op : 'det', k: kept.k ?? '2', n: kept.n ?? '2', decimals: !!kept.decimals };
    let t0 = 0;
    const keep = () => { clearTimeout(t0); t0 = setTimeout(() => T.save('matrix:state', st), 250); };
    const body = U.el('bcv-matrix');
    const p = T.popup({ tool, title: 'Matrix Calculator', sub: 'Determinant, inverse, solve, RREF.', width: 780, body, from });
    const grids = U.el('bcv-matrix__grids');
    const ops = U.el('bcv-matrix__ops', null, { role: 'group', 'aria-label': 'Operation' });
    const extra = U.el('bcv-matrix__extra');
    const result = U.el('bcv-matrix__result');
    body.append(...T.rise([grids, ops, extra, result]));

    function gridCard(key, name) {
      const d = st[key];
      const size = (dim, delta) => { d[dim] = Math.min(MOST, Math.max(1, d[dim] + delta)); keep(); paintGrids(); paintResult(); };
      const step = (label, dim) => U.el('bcv-matrix__size', [h('span', { text: label }), h('button', { type: 'button', class: 'bcv-matrix__step', 'aria-label': `Fewer ${label.toLowerCase()}`, text: '−', onclick: () => size(dim, -1) }), h('span', { class: 'bcv-matrix__n', text: String(d[dim]) }), h('button', { type: 'button', class: 'bcv-matrix__step', 'aria-label': `More ${label.toLowerCase()}`, text: '+', onclick: () => size(dim, 1) })]);
      const cells = h('div', { class: 'bcv-matrix__cells', style: { gridTemplateColumns: `repeat(${d.cols}, minmax(0, 1fr))` } });
      for (let r = 0; r < d.rows; r++) for (let c = 0; c < d.cols; c++) {
        const i = h('input', { class: 'bcv-input bcv-matrix__cell', inputmode: 'decimal', autocomplete: 'off', placeholder: '0', 'aria-label': `${name} row ${r + 1} column ${c + 1}`, dataset: { m: key, r: String(r), c: String(c) } });
        i.value = d.cells[r][c] || '';
        i.addEventListener('input', () => {
          if (/[\n\t;]/.test(i.value)) { const nd = draftOf(i.value); if (nd) { st[key] = nd; keep(); paintGrids(); paintResult(); return; } }
          d.cells[r][c] = i.value; keep(); paintResult();
        });
        i.addEventListener('paste', (e) => { const tx = e.clipboardData?.getData('text') || ''; if (/[\n\t;]/.test(tx.trim())) { const nd = draftOf(tx); if (nd) { e.preventDefault(); st[key] = nd; keep(); paintGrids(); paintResult(); } } });
        cells.append(i);
      }
      const clear = h('button', { type: 'button', class: 'bcv-matrix__clear', text: 'Clear', onclick: () => { st[key] = draft(d.rows, d.cols); keep(); paintGrids(); paintResult(); } });
      return T.card([U.el('bcv-matrix__head', [U.text('bcv-matrix__name', name, 'span'), step('Rows', 'rows'), step('Columns', 'cols'), clear]), cells], `bcv-matrix__card ${key === 'b' && !USES_B.has(st.op) ? 'is-idle' : ''}`);
    }
    function paintGrids() { grids.replaceChildren(gridCard('a', 'A'), gridCard('b', st.op === 'solve' ? 'b' : 'B')); }
    function paintOps() {
      ops.replaceChildren(...OPS.map(([k, label]) => h('button', { type: 'button', class: `bcv-matrix__op ${st.op === k ? 'is-on' : ''}`, 'aria-pressed': st.op === k ? 'true' : 'false', dataset: { op: k }, text: label, onclick: () => { st.op = k; keep(); paintOps(); paintGrids(); paintExtra(); paintResult(); } })));
    }
    function paintExtra() {
      const kids = [];
      if (st.op === 'scale' || st.op === 'power') {
        const key = st.op === 'scale' ? 'k' : 'n';
        const i = T.input({ inputmode: 'decimal', 'aria-label': key, value: st[key], class: 'bcv-input bcv-tool__input bcv-matrix__kin', dataset: { field: key } });
        i.value = st[key];
        i.addEventListener('input', () => { st[key] = i.value; keep(); paintResult(); });
        kids.push(U.el('bcv-matrix__kfield', [U.text('bcv-matrix__klabel', key === 'k' ? 'k =' : 'n =', 'span'), i]));
      }
      kids.push(h('label', { class: 'bcv-matrix__dec' }, [h('input', { type: 'checkbox', checked: st.decimals || null, onchange: (e) => { st.decimals = e.target.checked; keep(); paintResult(); } }), h('span', { text: 'Show as decimals' })]));
      extra.replaceChildren(...kids);
    }
    const matrixEl = (m) => h('div', { class: 'bcv-matrix__out', style: { gridTemplateColumns: `repeat(${m.cols}, auto)` } }, m.cells.map((x) => h('span', { class: 'bcv-matrix__outcell', text: show(x, st.decimals) })));
    function paintResult() {
      const A = readDraft(st.a), B = readDraft(st.b);
      const bad = (A.bad && ['A', A.bad]) || (USES_B.has(st.op) && B.bad && [st.op === 'solve' ? 'b' : 'B', B.bad]);
      const say = (msg) => result.replaceChildren(T.card([U.text('bcv-matrix__msg bcv-pretty', msg)]));
      if (bad) { say(`${bad[0]}, row ${bad[1].r + 1}, column ${bad[1].c + 1} is not a number.`); return; }
      const a = A.m, b = B.m;
      const title = OPS.find(([k]) => k === st.op)[1];
      const shown = (node, note = null, more = []) => result.replaceChildren(T.card([T.label(title), node, note ? U.text('bcv-tool__hint bcv-pretty', note) : null, ...more].filter(Boolean)));
      const sq = 'A has to be square: as many rows as columns.';
      switch (st.op) {
        case 'add': case 'subtract': { const m = (st.op === 'add' ? addM : subM)(a, b); return m ? shown(matrixEl(m)) : say('A and B have to be the same size.'); }
        case 'multiply': { const m = multiply(a, b); return m ? shown(matrixEl(m)) : say(`A has ${a.cols} columns and B has ${b.rows} rows: they have to match.`); }
        case 'scale': { const k = read(st.k); return k ? shown(matrixEl(scale(a, k))) : say('k has to be a number.'); }
        case 'transpose': return shown(matrixEl(transpose(a)));
        case 'det': { const d = determinant(a); return d ? shown(U.text('bcv-matrix__scalar', show(d, st.decimals))) : say(sq); }
        case 'inverse': { if (a.rows !== a.cols) return say(sq); const inv = inverse(a); return inv ? shown(matrixEl(inv)) : say('A is singular (its determinant is 0): it has no inverse.'); }
        case 'rank': return shown(U.text('bcv-matrix__scalar', String(rref(a).pivots.length)));
        case 'trace': { const tr = trace(a); return tr ? shown(U.text('bcv-matrix__scalar', show(tr, st.decimals))) : say(sq); }
        case 'rref': {
          const red = rref(a);
          const steps = red.steps.length ? h('details', { class: 'bcv-matrix__steps' }, [h('summary', { text: `${U.plural(red.steps.length, 'row operation')}` }), ...red.steps.map((s) => U.el('bcv-matrix__step', [U.text('bcv-matrix__steptext', s.text, 'span'), matrixEl(s.after)]))]) : null;
          return shown(matrixEl(red.result), null, [steps]);
        }
        case 'solve': {
          const sol = solve(a, b);
          if (!sol) return say(`b has to be one column with ${a.rows} ${a.rows === 1 ? 'row' : 'rows'}, as A has.`);
          if (sol.kind === 'none') return say('No solution: the equations contradict each other.');
          if (sol.kind === 'many') return say(`Infinitely many solutions: ${U.plural(sol.free, 'free variable')}.`);
          return shown(U.el('bcv-matrix__xs', sol.x.cells.map((v, i) => U.text('bcv-matrix__x', `x${subs(i + 1)} = ${show(v, st.decimals)}`, 'span'))));
        }
        case 'eigen': { if (a.rows !== a.cols) return say(sq); const ev = eigenvalues(a); return ev ? shown(U.el('bcv-matrix__xs', pairText(ev).map((s, i) => U.text('bcv-matrix__x', `λ${subs(i + 1)} = ${s}`, 'span'))), 'Found numerically, so shown as decimals.') : say('The eigenvalues could not be found.'); }
        case 'power': {
          const n = Number(String(st.n).trim().replace(/−/g, '-'));
          if (!Number.isInteger(n)) return say('n has to be a whole number.');
          if (a.rows !== a.cols) return say(sq);
          if (Math.abs(n) > 60) return say('n can be at most 60 either way.');
          const m = power(a, n);
          return m ? shown(matrixEl(m)) : say('A is singular: it has no negative powers.');
        }
        default: return null;
      }
    }
    paintGrids();
    paintOps();
    paintExtra();
    paintResult();
    return p;
  }

  BCV.toolsMatrix = { open, read, rref, determinant, inverse, multiply, solve, eigenvalues, pairText, power, transpose, draftOf };
})();
