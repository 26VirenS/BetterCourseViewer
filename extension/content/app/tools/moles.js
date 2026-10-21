/* (2.99.82) Molar Mass & Stoichiometry, the Mac's (Chemistry/MolarMass.swift): four tabs. Molar mass — a formula's molar
 * mass with its breakdown (each element, its count, atomic mass, subtotal and mass %). Convert — one amount of a formula
 * in grams, moles, particles or litres of gas at STP, and the other three. Reaction — a balanced equation (balanced for
 * you if it needs it) with what is given of each reactant: the limiting reagent, each product's theoretical yield and
 * what is left of the rest. Empirical — each element's mass % (or grams) to the empirical formula, and the molecular one
 * with a molar mass. The reading and the sums are tools/chem.js's; each tab keeps what was typed. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const T = BCV.tools;
  const { parse, pretty, molarMass, breakdown, balance, equationText, convert, reaction, empirical, text } = BCV.toolsChem;

  // ---- Molar Mass & Stoichiometry ----------------------------------------------------------------------------------------
  const TABS = [['molar', 'Molar mass'], ['convert', 'Convert'], ['reaction', 'Reaction'], ['empirical', 'Empirical']];
  const UNITS = [['g', 'grams'], ['mol', 'moles'], ['particles', 'molecules / formula units'], ['L', 'litres of gas at STP']];
  async function openMoles(app, { from = null } = {}) {
    const tool = T.toolOf('moles');
    const st = Object.assign({ tab: 'molar', formula: 'Ca(OH)2', amount: '10', unit: 'g', equation: '2H2 + O2 → 2H2O', given: {}, actual: '', shares: 'C=40, H=6.71, O=53.3', mm: '180' }, (await T.load('chem:moles', null)) || {});
    let t0 = 0;
    const keep = () => { clearTimeout(t0); t0 = setTimeout(() => T.save('chem:moles', st), 250); };
    const body = U.el('bcv-chem');
    const p = T.popup({ tool, title: 'Molar Mass & Stoichiometry', sub: 'Molar mass, moles, yields and formulas.', width: 720, body, from });
    const tabs = U.el('bcv-chem__tabs');
    const page = U.el('bcv-chem__page');
    const result = U.el('bcv-chem__result');
    body.append(...T.rise([tabs, page]));
    const input = (key, label, ph, mode = 'text') => { const i = T.input({ inputmode: mode, placeholder: ph, 'aria-label': label, dataset: { field: key }, autocomplete: 'off', spellcheck: 'false' }); i.value = st[key]; i.addEventListener('input', () => { st[key] = i.value; keep(); paintResult(); }); return U.el('bcv-chem__field', [U.text('bcv-chem__flabel', label, 'span'), i]); };
    const rows = (list) => U.el('bcv-chem__rows', list.map(([k, v]) => h('div', { class: 'bcv-chem__row', dataset: { k } }, [h('span', { class: 'bcv-chem__k', text: k }), h('span', { class: 'bcv-chem__v', text: v })])));
    let paintResult = () => {};
    function paint() {
      tabs.replaceChildren(T.seg(TABS, st.tab, (v) => { st.tab = v; keep(); paint(); }));
      if (st.tab === 'molar' || st.tab === 'convert') {
        const kids = [input('formula', 'Formula', 'e.g. CuSO4·5H2O')];
        if (st.tab === 'convert') {
          kids.push(U.el('bcv-chem__amount', [input('amount', 'Amount', 'e.g. 10', 'decimal'), U.el('bcv-chem__field', [U.text('bcv-chem__flabel', 'In', 'span'), U.picker(UNITS.map(([value, t]) => ({ value, text: t })), st.unit, (v) => { st.unit = v; keep(); paintResult(); }, { label: 'Unit' })])]));
        }
        page.replaceChildren(T.card(kids), result);
        paintResult = () => {
          const f = parse(st.formula);
          if (f.error) { result.replaceChildren(T.card([U.text('bcv-chem__msg', f.error)])); return; }
          const bd = breakdown(f);
          if (st.tab === 'molar') {
            result.replaceChildren(T.card([U.el('bcv-chem__head', [U.text('bcv-chem__formula', pretty(f), 'span'), U.text('bcv-chem__mm', `${text(bd.total, 6)} g/mol`, 'span')]),
              h('table', { class: 'bcv-chem__table' }, [h('tr', {}, ['Element', 'Atoms', 'Atomic mass', 'Subtotal', 'Mass %'].map((x) => h('th', { text: x }))), ...bd.rows.map((r) => h('tr', { dataset: { el: r.sym } }, [h('td', { text: r.sym }), h('td', { text: String(r.n) }), h('td', { text: text(r.mass, 6) }), h('td', { text: text(r.sub, 6) }), h('td', { text: `${r.pct.toFixed(2)}%` })]))])]));
          } else {
            const v = BCV.toolsStats?.numbers?.(st.amount)?.[0] ?? Number(String(st.amount).replace(/,/g, ''));
            const c = Number.isFinite(v) ? convert(v, st.unit, bd.total) : null;
            result.replaceChildren(c ? T.card([U.el('bcv-chem__head', [U.text('bcv-chem__formula', pretty(f), 'span'), U.text('bcv-chem__mm', `${text(bd.total, 6)} g/mol`, 'span')]), rows([['Grams', `${text(c.g)} g`], ['Moles', `${text(c.mol)} mol`], ['Particles', text(c.particles)], ['Litres at STP', `${text(c.L)} L`]])]) : T.card([U.text('bcv-chem__msg', 'Type an amount.')]));
          }
        };
      } else if (st.tab === 'reaction') {
        page.replaceChildren(T.card([input('equation', 'Equation', 'e.g. 2H2 + O2 → 2H2O')]), result);
        paintResult = () => {
          const b = balance(st.equation);
          if (b.error) { result.replaceChildren(T.card([U.text('bcv-chem__msg bcv-pretty', b.error)])); return; }
          const reactants = b.species.map((s, i) => [s, i]).filter(([s]) => s.reactant);
          const keyOf = (s) => s.text;
          const givenCard = T.card([T.label(b.changed ? `Balanced: ${equationText(b)}` : equationText(b)), ...reactants.map(([s]) => {
            const g = st.given[keyOf(s)] || { value: '', grams: true };
            const i = T.input({ inputmode: 'decimal', placeholder: 'not given', 'aria-label': `${s.text} given`, dataset: { given: s.text }, autocomplete: 'off' });
            i.value = g.value;
            i.addEventListener('input', () => { st.given[keyOf(s)] = { ...g, value: i.value }; keep(); paintYields(); });
            const unit = U.picker([{ value: 'g', text: 'g' }, { value: 'mol', text: 'mol' }], g.grams ? 'g' : 'mol', (v) => { st.given[keyOf(s)] = { ...(st.given[keyOf(s)] || g), grams: v === 'g' }; keep(); paintYields(); }, { label: 'Unit' });
            return U.el('bcv-chem__given', [U.text('bcv-chem__gname', pretty(s.formula), 'span'), i, unit]);
          })]);
          const yields = U.el('bcv-chem__yields');
          result.replaceChildren(givenCard, yields);
          function paintYields() {
            const given = {};
            b.species.forEach((s, i) => { const g = st.given[keyOf(s)]; const v = g && String(g.value).trim() !== '' ? Number(String(g.value).replace(/,/g, '')) : NaN; if (s.reactant && Number.isFinite(v)) given[i] = { value: v, grams: g.grams !== false }; });
            const r = reaction(b, given);
            if (!r) { yields.replaceChildren(T.hint('Give an amount of at least one reactant, and the yields appear here.')); return; }
            yields.replaceChildren(T.card([T.label('Limiting reagent'), U.text('bcv-chem__formula', pretty(r.limiting.formula)), rows([...r.yields.map((y) => [`${pretty(y.s.formula)} made`, `${text(y.g)} g · ${text(y.mol)} mol`]), ...r.excess.map((x) => [`${pretty(x.s.formula)} left over`, `${text(x.g)} g · ${text(x.mol)} mol`])])]));
          }
          paintYields();
        };
      } else {
        page.replaceChildren(T.card([input('shares', 'Each element’s mass % (or grams)', 'e.g. C=40, H=6.71, O=53.3'), input('mm', 'Molar mass (optional), g/mol', 'e.g. 180', 'decimal')]), result);
        paintResult = () => {
          const shares = String(st.shares).split(/[,;\n]+/).map((x) => x.split(/[=:]/).map((y) => y.trim())).filter((x) => x.length === 2).map(([sym, v]) => [sym, Number(v.replace('%', ''))]);
          const e = empirical(shares);
          if (!e || e.formula.error) { result.replaceChildren(T.card([U.text('bcv-chem__msg', 'Type each element and its share, like C=40, H=6.71, O=53.3.')])); return; }
          const emm = molarMass(e.formula);
          const mm = Number(String(st.mm).replace(/,/g, ''));
          const k = mm > 0 ? Math.round(mm / emm) : 0;
          const mol = k >= 1 ? parse([...e.formula.counts].map(([sym, n]) => `${sym}${n * k === 1 ? '' : n * k}`).join('')) : null;
          result.replaceChildren(T.card([T.label('Empirical formula'), U.el('bcv-chem__head', [U.text('bcv-chem__formula', pretty(e.formula), 'span'), U.text('bcv-chem__mm', `${text(emm, 6)} g/mol`, 'span')]), rows(e.ratios.map(([sym, r]) => [`${sym} ratio`, text(r, 4)])), mol && !mol.error ? U.el('bcv-chem__mol', [T.label('Molecular formula'), U.el('bcv-chem__head', [U.text('bcv-chem__formula', pretty(mol), 'span'), U.text('bcv-chem__mm', `× ${k}`, 'span')])]) : null, e.rough ? T.hint('No small whole-number ratio fits closely; check the shares.') : null].filter(Boolean)));
        };
      }
      paintResult();
    }
    paint();
    return p;
  }

  BCV.toolsMoles = { open: openMoles };
})();
