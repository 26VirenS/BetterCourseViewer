/* (2.99.82) The chemistry calculators' shared reading, the Mac's (Chemistry/Model/ChemFormula.swift, ChemBalance.swift,
 * ChemStoich.swift): a formula read into its elements and their counts (parentheses, hydrates, charges, phase labels),
 * its molar mass from the periodic table's own masses (tools/ptable-data.js), an equation read and balanced exactly
 * (the null space of its element matrix, in fractions — BigInts here, so they never overflow), and the stoichiometry:
 * an amount in grams, moles, particles or litres at STP; a reaction's limiting reagent, yields and excess; an empirical
 * formula from each element's share. The Equation Balancer is here; Molar Mass & Stoichiometry (tools/moles.js) draws
 * with the rest. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const T = BCV.tools;

  const MASS = new Map((self.BCV_PTABLE || []).map((r) => [r[1], Number(r[3])]));
  const isElement = (s) => MASS.has(s);
  const SUBS = '₀₁₂₃₄₅₆₇₈₉', SUPS = '⁰¹²³⁴⁵⁶⁷⁸⁹';
  const AVOGADRO = 6.02214076e23, STP = 22.4;

  // ---- a formula ----------------------------------------------------------------------------------------------------
  function stripPhase(s) {
    let t = String(s).trim();
    for (const l of ['(s)', '(l)', '(g)', '(aq)']) if (t.toLowerCase().endsWith(l)) { t = t.slice(0, -l.length).trim(); break; }
    return t;
  }
  function normalised(s) {
    let out = '', inSup = false;
    for (const ch of s) {
      const si = SUBS.indexOf(ch), pi = SUPS.indexOf(ch);
      if (si >= 0) { out += String(si); inSup = false; }
      else if (pi >= 0) { if (!inSup) { out += '^'; inSup = true; } out += String(pi); }
      else if (ch === '⁺' || ch === '⁻') { if (!inSup) out += '^'; out += ch === '⁺' ? '+' : '-'; inSup = false; }
      else if (ch === '−' || ch === '–') { out += '-'; inSup = false; }
      else { out += ch; inSup = false; }
    }
    return out;
  }
  function readCharge(s) {
    if (!s) return null;
    let t = s, sign = 0;
    if (t.endsWith('+') || t.endsWith('-')) { sign = t.endsWith('+') ? 1 : -1; t = t.slice(0, -1); }
    else if (t.startsWith('+') || t.startsWith('-')) { sign = t[0] === '+' ? 1 : -1; t = t.slice(1); }
    if (!sign) return null;
    if (!t) return sign;
    const n = /^\d+$/.test(t) ? Number(t) : NaN;
    return n > 0 && n < 100 ? sign * n : null;
  }
  function readGroup(chars, pos, close) {
    const counts = new Map(), order = [];
    const add = (sym, n) => { if (!counts.has(sym)) order.push(sym); counts.set(sym, (counts.get(sym) || 0) + n); };
    const readCount = () => { let d = ''; while (pos.i < chars.length && /[0-9]/.test(chars[pos.i])) d += chars[pos.i++]; return d ? Number(d) : 1; };
    while (pos.i < chars.length) {
      const c = chars[pos.i];
      if (close && c === close) { pos.i++; return { counts, order }; }
      if (c === '(' || c === '[') {
        pos.i++;
        const inner = readGroup(chars, pos, c === '(' ? ')' : ']');
        if (!inner) return null;
        const n = readCount();
        if (!(n > 0)) return null;
        for (const sym of inner.order) add(sym, inner.counts.get(sym) * n);
        continue;
      }
      if (!/[A-Z]/.test(c)) return null;
      let sym = c;
      pos.i++;
      while (pos.i < chars.length && /[a-z]/.test(chars[pos.i]) && sym.length < 3) sym += chars[pos.i++];
      if (!isElement(sym)) return null;
      const n = readCount();
      if (!(n > 0)) return null;
      add(sym, n);
    }
    return close ? null : { counts, order };
  }
  /** The formula in `raw`, or { error } with the line the tools show. */
  function parse(raw) {
    const token = String(raw ?? '').trim();
    let s = token;
    if (!s) return { error: 'Type a formula.' };
    s = normalised(stripPhase(s)).replace(/\s+/g, '');
    if (!s) return { error: 'Type a formula.' };
    const bad = { error: `Can't read \`${token}\` — is it a real formula?` };
    let body = s, charge = 0;
    const caret = s.indexOf('^');
    if (caret >= 0) {
      body = s.slice(0, caret);
      const q = readCharge(s.slice(caret + 1));
      if (q === null) return bad;
      charge = q;
    } else if (/[+-]$/.test(s)) {
      let i = s.length - 1;
      while (i > 0 && /[0-9]/.test(s[i - 1])) i--;
      // (digits before the sign are the charge's after a bare element — "Fe3+" — and a count after more: "NH4+")
      if (i < s.length - 1 && !isElement(s.slice(0, i))) i = s.length - 1;
      const q = readCharge(s.slice(i));
      if (q === null) return bad;
      body = s.slice(0, i);
      charge = q;
    }
    if (body === 'e') return charge === -1 ? { text: s, body, counts: new Map(), elements: [], charge: -1 } : bad;
    if (!body || /^[0-9]/.test(body) || /^[·•*.]/.test(body)) return bad;
    const counts = new Map(), order = [];
    const parts = body.split(/[·•*.]/).filter(Boolean);
    for (let k = 0; k < parts.length; k++) {
      let part = parts[k], times = 1;
      if (k > 0) { const m = /^(\d+)/.exec(part); if (m) { times = Number(m[1]); if (!(times > 0)) return bad; part = part.slice(m[1].length); } }
      const pos = { i: 0 };
      const g = readGroup([...part], pos, null);
      if (!g || pos.i !== [...part].length) return bad;
      for (const sym of g.order) { if (!counts.has(sym)) order.push(sym); counts.set(sym, (counts.get(sym) || 0) + g.counts.get(sym) * times); }
    }
    if (!counts.size) return bad;
    return { text: s, body, counts, elements: order, charge };
  }
  const molarMass = (f) => [...f.counts].reduce((t, [sym, n]) => t + n * (MASS.get(sym) || 0), 0);
  /** The formula typeset: counts under, the charge over ("Ca(OH)₂", "CuSO₄·5H₂O", "SO₄²⁻"). */
  function pretty(f) {
    let out = '', prev = ' ', under = false;
    for (const ch of f.body) {
      if (/[0-9]/.test(ch) && (/[A-Za-z)\]]/.test(prev) || under)) { out += SUBS[ch]; under = true; }
      else { out += ch; under = false; }
      prev = ch;
    }
    if (f.charge) { const n = Math.abs(f.charge); if (n > 1) out += String(n).split('').map((d) => SUPS[d]).join(''); out += f.charge > 0 ? '⁺' : '⁻'; }
    return out;
  }
  function breakdown(f) {
    const total = molarMass(f);
    return { total, rows: f.elements.map((sym) => { const n = f.counts.get(sym), m = MASS.get(sym) || 0; return { sym, n, mass: m, sub: n * m, pct: total > 0 ? (n * m * 100) / total : 0 }; }) };
  }

  // ---- an equation, balanced -------------------------------------------------------------------------------------------
  function splitSide(side) {
    const chars = [...side], parts = [];
    let cur = '';
    chars.forEach((ch, i) => {
      if (ch !== '+') { cur += ch; return; }
      const prevSpace = i === 0 || /\s/.test(chars[i - 1]);
      const atEnd = i + 1 >= chars.length;
      const nextSpace = !atEnd && /\s/.test(chars[i + 1]);
      const nextPhase = !atEnd && chars[i + 1] === '(';
      const lastNon = cur.replace(/[0-9]+$/, '');
      const afterCaret = lastNon.endsWith('^');
      if (!prevSpace && (atEnd || nextSpace || nextPhase || afterCaret)) cur += ch; else { parts.push(cur); cur = ''; }
    });
    parts.push(cur);
    return parts.map((p) => p.trim()).filter(Boolean);
  }
  function readEquation(raw) {
    let s = String(raw ?? '').trim();
    if (!s) return { error: 'Type an equation, like Fe + O2 → Fe2O3.' };
    for (const a of ['⟶', '→', '⇌', '⇒', '-->', '==>', '=>', '<->', '<=>']) s = s.split(a).join('->');
    if (!s.includes('->')) s = s.replace(/=/g, '->');
    const sides = s.split('->');
    if (sides.length !== 2) return { error: 'Put one arrow (→, -> or =) between the reactants and the products.' };
    const species = [];
    for (let k = 0; k < 2; k++) {
      const tokens = splitSide(sides[k]);
      if (!tokens.length) return { error: 'Both sides of the arrow need something on them.' };
      for (const token of tokens) {
        let t = stripPhase(token);
        const m = /^(\d+)/.exec(t);
        let typed = null;
        if (m) { typed = Number(m[1]); if (!(typed > 0)) return { error: `Can't read \`${token}\` — is it a real formula?` }; t = t.slice(m[1].length).trim(); }
        const f = parse(t);
        if (f.error) return { error: `Can't read \`${token.trim()}\` — is it a real formula?` };
        species.push({ text: f.text, formula: f, reactant: k === 0, typed });
      }
    }
    return { species };
  }
  const bgcd = (a, b) => { a = a < 0n ? -a : a; b = b < 0n ? -b : b; while (b) [a, b] = [b, a % b]; return a; };
  const fr = (n, d = 1n) => { if (d < 0n) { n = -n; d = -d; } const g = bgcd(n, d) || 1n; return [n / g, d / g]; };
  const fsub = (a, b) => fr(a[0] * b[1] - b[0] * a[1], a[1] * b[1]);
  const fmul = (a, b) => fr(a[0] * b[0], a[1] * b[1]);
  const fdiv = (a, b) => fr(a[0] * b[1], a[1] * b[0]);
  function balance(raw) {
    const read = typeof raw === 'string' ? readEquation(raw) : { species: raw };
    if (read.error) return read;
    const species = read.species;
    if (!species.some((s) => s.reactant) || !species.some((s) => !s.reactant)) return { error: 'Both sides of the arrow need something on them.' };
    const elements = [];
    for (const s of species) for (const e of s.formula.elements) if (!elements.includes(e)) elements.push(e);
    const oneSide = elements.filter((e) => species.some((s) => s.reactant && s.formula.counts.get(e)) !== species.some((s) => !s.reactant && s.formula.counts.get(e)));
    if (oneSide.length) return { error: `This can't be balanced — check the formulas (an element on only one side? ${oneSide.map((e) => `\`${e}\``).join(', ')})` };
    const rows = elements.map((e) => species.map((s) => fr(BigInt((s.formula.counts.get(e) || 0) * (s.reactant ? 1 : -1)))));
    if (species.some((s) => s.formula.charge)) rows.push(species.map((s) => fr(BigInt(s.formula.charge * (s.reactant ? 1 : -1)))));
    const n = species.length, a = rows.map((r) => [...r]), pivots = [];
    let r = 0;
    for (let c = 0; c < n && r < a.length; c++) {
      const p = a.findIndex((row, i) => i >= r && row[c][0] !== 0n);
      if (p < 0) continue;
      [a[r], a[p]] = [a[p], a[r]];
      const lead = a[r][c];
      for (let j = 0; j < n; j++) a[r][j] = fdiv(a[r][j], lead);
      for (let i = 0; i < a.length; i++) if (i !== r && a[i][c][0] !== 0n) { const k = a[i][c]; for (let j = 0; j < n; j++) a[i][j] = fsub(a[i][j], fmul(k, a[r][j])); }
      pivots.push(c);
      r++;
    }
    const free = [...Array(n).keys()].filter((c) => !pivots.includes(c));
    if (!free.length) return { error: "This can't be balanced as written — check the formulas." };
    if (free.length > 1) return { error: 'More than one way to balance this — split it into separate equations.' };
    const f = free[0];
    const x = Array.from({ length: n }, () => fr(0n));
    x[f] = fr(1n);
    pivots.forEach((pc, i) => { x[pc] = fsub(fr(0n), a[i][f]); });
    let lcm = 1n;
    for (const [, d] of x) lcm = (lcm / (bgcd(lcm, d) || 1n)) * d;
    let ints = x.map(([nn, d]) => nn * (lcm / d));
    const g = ints.reduce((t, v) => bgcd(t, v), 0n) || 1n;
    ints = ints.map((v) => v / g);
    if (ints.every((v) => v <= 0n)) ints = ints.map((v) => -v);
    const badAt = ints.findIndex((v) => v <= 0n);
    if (badAt >= 0) return { error: `This can't be balanced as written — \`${species[badAt].text}\` would need a coefficient of 0 or less. Check the formulas.` };
    const coefficients = ints.map(Number);
    return { species, coefficients, changed: species.some((s, i) => (s.typed ?? 1) !== coefficients[i]) };
  }
  function counts(b) {
    const order = [];
    for (const s of b.species) for (const e of s.formula.elements) if (!order.includes(e)) order.push(e);
    const rows = order.map((e) => { let l = 0, r = 0; b.species.forEach((s, i) => { const v = (s.formula.counts.get(e) || 0) * b.coefficients[i]; if (s.reactant) l += v; else r += v; }); return { label: e, left: l, right: r }; });
    if (b.species.some((s) => s.formula.charge)) { let l = 0, r = 0; b.species.forEach((s, i) => { if (s.reactant) l += s.formula.charge * b.coefficients[i]; else r += s.formula.charge * b.coefficients[i]; }); rows.push({ label: 'Charge', left: l, right: r }); }
    return rows;
  }
  const sideMass = (b, reactant) => b.species.reduce((t, s, i) => t + (s.reactant === reactant ? b.coefficients[i] * molarMass(s.formula) : 0), 0);
  const equationText = (b, arrow = '→') => [true, false].map((rx) => b.species.map((s, i) => [s, i]).filter(([s]) => s.reactant === rx).map(([s, i]) => `${b.coefficients[i] === 1 ? '' : b.coefficients[i]}${pretty(s.formula)}`).join(' + ')).join(` ${arrow} `);

  // ---- stoichiometry -----------------------------------------------------------------------------------------------------
  function convert(v, unit, mm) {
    if (!(mm > 0) || !Number.isFinite(v)) return null;
    const mol = unit === 'g' ? v / mm : unit === 'mol' ? v : unit === 'particles' ? v / AVOGADRO : v / STP;
    return { g: mol * mm, mol, particles: mol * AVOGADRO, L: mol * STP };
  }
  function reaction(b, given) {
    let best = null;
    const moles = new Map();
    b.species.forEach((s, i) => {
      if (!s.reactant || !given[i] || !(given[i].value >= 0)) return;
      const nmol = given[i].grams ? given[i].value / molarMass(s.formula) : given[i].value;
      moles.set(i, nmol);
      const extent = nmol / b.coefficients[i];
      if (!best || extent < best.extent) best = { i, extent };
    });
    if (!best) return null;
    const yields = [], excess = [];
    b.species.forEach((s, i) => {
      const c = b.coefficients[i];
      if (!s.reactant) { const nmol = c * best.extent; yields.push({ s, mol: nmol, g: nmol * molarMass(s.formula) }); }
      else if (i !== best.i && moles.has(i)) { const left = Math.max(0, moles.get(i) - c * best.extent); excess.push({ s, mol: left, g: left * molarMass(s.formula) }); }
    });
    return { limiting: b.species[best.i], extent: best.extent, yields, excess };
  }
  /** Each element's share (mass % or grams) to the empirical formula: moles, over the least, then the smallest multiple
   *  (up to 8) that brings every ratio within 0.1 of a whole number. */
  function empirical(shares) {
    const list = shares.filter(([sym, v]) => isElement(sym) && v > 0).map(([sym, v]) => [sym, v / MASS.get(sym)]);
    if (!list.length) return null;
    const least = Math.min(...list.map(([, m]) => m));
    const ratios = list.map(([sym, m]) => [sym, m / least]);
    for (let k = 1; k <= 8; k++) {
      if (ratios.every(([, r]) => Math.abs(r * k - Math.round(r * k)) <= 0.1)) {
        const counts2 = ratios.map(([sym, r]) => [sym, Math.round(r * k)]);
        const body = counts2.map(([sym, nn]) => `${sym}${nn === 1 ? '' : nn}`).join('');
        return { formula: parse(body), ratios };
      }
    }
    const counts2 = ratios.map(([sym, r]) => [sym, Math.max(1, Math.round(r))]);
    return { formula: parse(counts2.map(([sym, nn]) => `${sym}${nn === 1 ? '' : nn}`).join('')), ratios, rough: true };
  }
  const text = (v, sig = 6) => (BCV.toolsStats?.text ? BCV.toolsStats.text(v, sig) : String(Number(v.toPrecision(sig))));

  // ---- the Equation Balancer ------------------------------------------------------------------------------------------
  const EXAMPLES = ['Fe + O2 → Fe2O3', 'C3H8 + O2 → CO2 + H2O', 'Al + HCl → AlCl3 + H2', 'KMnO4 + HCl → KCl + MnCl2 + H2O + Cl2'];
  async function openBalance(app, { from = null } = {}) {
    const tool = T.toolOf('balance');
    const kept = await T.load('chem:balance', EXAMPLES[1]);
    const body = U.el('bcv-chem');
    const p = T.popup({ tool, title: 'Equation Balancer', sub: 'Type an equation; it balances as you type.', width: 680, body, from });
    const field = T.input({ class: 'bcv-input bcv-tool__input bcv-chem__eq', 'aria-label': 'Equation', placeholder: 'e.g. Fe + O2 → Fe2O3', autocomplete: 'off', spellcheck: 'false' });
    field.value = typeof kept === 'string' ? kept : EXAMPLES[1];
    const examples = U.el('bcv-chem__examples', EXAMPLES.map((e) => h('button', { type: 'button', class: 'bcv-chem__example', text: e, onclick: () => { field.value = e; T.save('chem:balance', e); paint(); } })));
    const out = U.el('bcv-chem__out');
    let t0 = 0;
    field.addEventListener('input', () => { clearTimeout(t0); t0 = setTimeout(() => { T.save('chem:balance', field.value); paint(); }, 220); });
    field.addEventListener('keydown', (e) => { if (e.key === 'Enter') { clearTimeout(t0); paint(); } });
    body.append(...T.rise([T.card([T.label('Equation'), field, examples]), out]));
    function paint() {
      const b = balance(field.value);
      if (b.error) { out.replaceChildren(T.card([U.text('bcv-chem__msg bcv-pretty', b.error)])); return; }
      const eq = h('div', { class: 'bcv-chem__balanced' });
      [true, false].forEach((rx, side) => {
        if (side) eq.append(h('span', { class: 'bcv-chem__arrow', text: '→' }));
        b.species.map((s, i) => [s, i]).filter(([s]) => s.reactant === rx).forEach(([s, i], k) => {
          if (k) eq.append(h('span', { class: 'bcv-chem__plus', text: '+' }));
          eq.append(h('span', { class: 'bcv-chem__sp' }, [b.coefficients[i] === 1 ? null : h('b', { class: 'bcv-chem__coef', text: String(b.coefficients[i]) }), pretty(s.formula)].filter(Boolean)));
        });
      });
      const ml = sideMass(b, true), mr = sideMass(b, false);
      out.replaceChildren(
        T.card([T.label(b.changed ? 'Balanced for you' : 'Already balanced'), eq, h('button', { type: 'button', class: 'bcv-chem__copy', text: 'Copy', onclick: () => { T.copyText(equationText(b, '->').replace(/[₀-₉]/g, (c) => SUBS.indexOf(c)).replace(/[⁰-⁹⁺⁻]/g, '')); U.toast('Copied.'); } })]),
        U.el('bcv-chem__cols', [
          T.card([T.label('Atoms on each side'), h('table', { class: 'bcv-chem__table' }, [h('tr', {}, ['', 'Left', 'Right', ''].map((x) => h('th', { text: x }))), ...counts(b).map((r) => h('tr', { dataset: { el: r.label } }, [h('td', { text: r.label }), h('td', { text: String(r.left) }), h('td', { text: String(r.right) }), h('td', { class: r.left === r.right ? 'is-ok' : 'is-off', text: r.left === r.right ? '✓' : '✗' })]))])]),
          T.card([T.label('Mass check'), U.el('bcv-chem__mass', [U.text('bcv-chem__massv', `${text(ml, 6)} g`, 'span'), U.text('bcv-chem__massk', 'reactants', 'span'), U.text('bcv-chem__massv', `${text(mr, 6)} g`, 'span'), U.text('bcv-chem__massk', 'products', 'span')]), T.hint('The coefficients taken as moles, from the molar masses.')]),
        ]),
      );
    }
    paint();
    setTimeout(() => field.focus(), 60);
    return p;
  }

  BCV.toolsChem = { openBalance, parse, pretty, molarMass, breakdown, balance, counts, equationText, convert, reaction, empirical, text, SUBS };
})();
