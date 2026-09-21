/* The periodic table: every element in its place, coloured by its kind, and a card in the table's
 * empty corner for the one pressed — symbol, number, name, mass, group, period and block, its state
 * at room temperature, electron configuration, electronegativity, melting and boiling points,
 * density — with a line about it under the table. A search by symbol, name or number lights the
 * matches (Enter picks the first); the legend's chips light a kind; the arrow keys walk the table.
 * The elements travel with the extension (tools/ptable-data.js); nothing is fetched. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const T = BCV.tools;

  const ELEMENTS = (self.BCV_PTABLE || []).map((r) => ({ number: r[0], symbol: r[1], name: r[2], mass: r[3], category: r[4], group: r[5], period: r[6], block: r[7], phase: r[8], config: r[9], en: r[10], melt: r[11], boil: r[12], density: r[13], found: r[14], x: r[15], y: r[16], summary: r[17] }));
  const CATS = [
    ['alkali metal', 'Alkali metal', '#ff453a'], ['alkaline earth metal', 'Alkaline earth', '#ff9f0a'], ['transition metal', 'Transition metal', '#ffd60a'],
    ['post-transition metal', 'Post-transition', '#30d158'], ['metalloid', 'Metalloid', '#64d2ff'], ['nonmetal', 'Nonmetal', '#0a84ff'],
    ['noble gas', 'Noble gas', '#bf5af2'], ['lanthanide', 'Lanthanide', '#ff375f'], ['actinide', 'Actinide', '#ac8e68'], ['unknown', 'Unknown', '#8e8e93'],
  ];
  const catOf = (k) => CATS.find((c) => c[0] === k) || CATS[CATS.length - 1];
  const cap = (s) => s.charAt(0).toUpperCase() + s.slice(1);
  const massText = (e) => (Number.isInteger(e.mass) ? `[${e.mass}]` : e.mass.toFixed(3)); // (a whole number is the longest-lived isotope's, the convention for elements with no stable one)
  const celsius = (k) => (k === null || k === undefined ? '—' : `${Math.round(k - 273.15)} °C`);
  const density = (e) => (e.density === null || e.density === undefined ? '—' : `${e.density} ${e.phase === 'Gas' ? 'g/L' : 'g/cm³'}`);
  const byNumber = (n) => ELEMENTS.find((e) => e.number === Number(n)) || null;
  /** What a search finds, best first: a symbol, name or number exactly; then ones starting with it; then names holding it. */
  function find(q) {
    const s = String(q || '').trim().toLowerCase();
    if (!s) return [];
    const exact = ELEMENTS.filter((e) => e.symbol.toLowerCase() === s || e.name.toLowerCase() === s || String(e.number) === s);
    const starts = ELEMENTS.filter((e) => !exact.includes(e) && (e.symbol.toLowerCase().startsWith(s) || e.name.toLowerCase().startsWith(s) || String(e.number).startsWith(s)));
    const within = ELEMENTS.filter((e) => !exact.includes(e) && !starts.includes(e) && e.name.toLowerCase().includes(s));
    return [...exact, ...starts, ...within];
  }
  const line = (e) => `${e.name} · ${e.number} · ${massText(e)}`;

  function open(app, { from = null, select = null } = {}) {
    const tool = T.toolOf('ptable');
    const st = { sel: byNumber(select), q: '', cat: null };
    const body = U.el('bcv-pt');
    const p = T.popup({ tool, title: 'Periodic table', sub: `${ELEMENTS.length} elements`, width: 1120, cls: 'bcv-tool--pt', body, from });
    const search = T.input({ placeholder: 'Symbol, name or number', 'aria-label': 'Find an element', autocomplete: 'off', spellcheck: 'false' });
    search.classList.add('bcv-pt__search');
    search.addEventListener('input', () => { st.q = search.value; paint(); });
    search.addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); const m = find(st.q)[0]; if (m) { st.sel = m; st.q = ''; search.value = ''; paint(); } } });
    const legend = U.el('bcv-pt__legend', CATS.map(([k, lbl, color]) => h('button', { type: 'button', class: 'bcv-pt__chip', dataset: { cat: k }, style: { '--c': color }, 'aria-pressed': 'false', onclick: () => { st.cat = st.cat === k ? null : k; paint(); } }, [h('span', { class: 'bcv-pt__chipdot' }), h('span', { text: lbl })])));
    const grid = U.el('bcv-pt__grid', null, { role: 'grid', 'aria-label': 'The periodic table' });
    const cells = new Map();
    for (const e of ELEMENTS) {
      const c = catOf(e.category);
      const cell = h('button', { type: 'button', class: 'bcv-pt__cell', dataset: { symbol: e.symbol, number: String(e.number), cat: e.category }, style: { '--c': c[2], gridColumn: String(e.x), gridRow: String(e.y) }, title: `${e.name} · ${e.number}`, 'aria-label': `${e.name}, ${e.number}`, onclick: () => { st.sel = e; paint(); } }, [
        h('span', { class: 'bcv-pt__num', text: String(e.number) }), h('span', { class: 'bcv-pt__sym', text: e.symbol }), h('span', { class: 'bcv-pt__nm', text: e.name }),
      ]);
      cells.set(e.number, cell);
      grid.append(cell);
    }
    grid.append(
      h('span', { class: 'bcv-pt__rowlabel', text: '57–71', title: 'The lanthanides, in the row below', style: { gridColumn: '3', gridRow: '6' } }),
      h('span', { class: 'bcv-pt__rowlabel', text: '89–103', title: 'The actinides, in the row below', style: { gridColumn: '3', gridRow: '7' } }),
    );
    const card = U.el('bcv-pt__card');
    grid.append(card);
    const summary = U.el('bcv-pt__summary');
    body.append(U.el('bcv-pt__top', [search, legend]), grid, summary);
    // the arrow keys walk the table from the element pressed, skipping the gaps
    grid.addEventListener('keydown', (e) => {
      if (!st.sel || !['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(e.key)) return;
      e.preventDefault();
      const dx = e.key === 'ArrowLeft' ? -1 : e.key === 'ArrowRight' ? 1 : 0, dy = e.key === 'ArrowUp' ? -1 : e.key === 'ArrowDown' ? 1 : 0;
      let x = st.sel.x + dx, y = st.sel.y + dy;
      for (let i = 0; i < 18 && x >= 1 && x <= 18 && y >= 1 && y <= 10; i++) {
        const n = ELEMENTS.find((z) => z.x === x && z.y === y);
        if (n) { st.sel = n; paint(); cells.get(n.number).focus({ preventScroll: true }); return; }
        x += dx; y += dy;
      }
    });
    const fact = (k, v) => U.el('bcv-pt__fact', [U.text('bcv-pt__factk', k, 'span'), U.text('bcv-pt__factv', String(v), 'span')]);
    function paint() {
      const q = st.q.trim();
      const matches = q ? new Set(find(q).map((e) => e.number)) : null;
      for (const e of ELEMENTS) {
        const cell = cells.get(e.number);
        cell.classList.toggle('is-sel', st.sel === e);
        cell.classList.toggle('is-match', !!matches && matches.has(e.number));
        cell.classList.toggle('is-dim', (!!matches && !matches.has(e.number)) || (!!st.cat && e.category !== st.cat));
        cell.setAttribute('aria-pressed', st.sel === e ? 'true' : 'false');
      }
      for (const chip of legend.children) { chip.classList.toggle('is-on', chip.dataset.cat === st.cat); chip.setAttribute('aria-pressed', chip.dataset.cat === st.cat ? 'true' : 'false'); }
      const e = st.sel;
      if (!e) {
        card.style.removeProperty('--c');
        card.replaceChildren(U.el('bcv-pt__cardempty', [U.text('bcv-pt__hint', 'Press an element, or type its symbol, name or number.')]));
        summary.hidden = true;
        p.setSub(`${ELEMENTS.length} elements`);
        return;
      }
      card.style.setProperty('--c', catOf(e.category)[2]);
      card.replaceChildren(
        U.el('bcv-pt__cardhead', [
          U.el('bcv-pt__big', [h('span', { class: 'bcv-pt__bigsym', text: e.symbol }), h('span', { class: 'bcv-pt__bignum', text: String(e.number) })]),
          U.el('bcv-pt__cardtitles', [U.text('bcv-pt__name', e.name), U.text('bcv-pt__cat', `${cap(e.category)} · ${massText(e)} u`)]),
        ]),
        U.el('bcv-pt__facts', [
          fact('Group · period · block', `${e.group ? `Group ${e.group}` : 'No group'} · Period ${e.period} · ${e.block}-block`),
          fact('At room temperature', e.phase || '—'),
          fact('Electrons', e.config || '—'),
          fact('Electronegativity', e.en ?? '—'),
          fact('Melts · boils', `${celsius(e.melt)} · ${celsius(e.boil)}`),
          fact('Density', density(e)),
        ]),
      );
      summary.hidden = false;
      summary.replaceChildren(
        h('span', { class: 'bcv-pt__summarytext', text: e.summary }),
        e.found ? h('span', { class: 'bcv-pt__found', text: `Found ${e.found}` }) : null,
        h('a', { class: 'bcv-pt__wiki', href: `https://en.wikipedia.org/wiki/${encodeURIComponent(e.name)}`, target: '_blank', rel: 'noopener', text: 'Wikipedia ↗' }),
      );
      p.setSub(`${e.name} · ${e.number}`);
    }
    paint();
    if (st.sel) setTimeout(() => { if (p.alive()) cells.get(st.sel.number)?.focus({ preventScroll: true }); }, 80);
    return p;
  }

  BCV.toolsPtable = { open, find, line, massText, byNumber, ELEMENTS, CATS };
})();
