/* The citation generator: MLA 9, APA 7 or Chicago 17 from the fields you fill in, for a website,
 * a journal article, a book, a video or a course file. Style and type pick the template; the
 * fields fill it. The values live in ONE shared object keyed by field name, so author, title and
 * year survive a change of type — and every read goes through a guard (val), so a value the active
 * type has no field for can never leak into the citation (a website's URL on a book). The preview
 * renders continuously from whatever is typed; what is still needed is named, and Copy and Save
 * wait until the citation is whole. Saved citations stay on this device; Copy list emits them
 * alphabetically, sorted at copy time. Nothing is sent to Canvas. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const T = BCV.tools;

  const KEY = 'tools:citations';
  const STYLES = [['mla', 'MLA 9'], ['apa', 'APA 7'], ['chicago', 'Chicago 17']];
  const STYLE_NAME = Object.fromEntries(STYLES);
  const TYPES = [['website', 'Website', IC.globe], ['journal', 'Journal article', IC.journal], ['book', 'Book', IC.book], ['video', 'Video', IC.video], ['coursefile', 'Course file', IC.slides]];
  const EMPTY = { author: '', title: '', container: '', publisher: '', year: '', month: '', day: '', url: '', accessed: '', volume: '', issue: '', pages: '', doi: '', city: '', edition: '', course: '', institution: '' };

  const F = (key, label, hint, flex = '1 1 100%', req = false) => ({ key, label, hint, flex, req });
  function fieldDefs(type) {
    if (type === 'journal') return [
      F('author', 'Author', 'Jane R. Okonkwo', '1 1 100%', true), F('title', 'Article title', 'Reading Habits in First-Year Courses', '1 1 100%', true),
      F('container', 'Journal', 'Journal of Higher Education', '1 1 100%', true), F('volume', 'Volume', '47', '1 1 20%'), F('issue', 'Issue', '3', '1 1 20%'),
      F('year', 'Year', '2026', '1 1 20%', true), F('pages', 'Pages', '112–137', '1 1 28%'), F('doi', 'DOI', '10.1080/00221546.2026.1234567', '1 1 100%'),
    ];
    if (type === 'book') return [
      F('author', 'Author', 'Jane R. Okonkwo', '1 1 100%', true), F('title', 'Book title', 'The Unread Syllabus', '1 1 100%', true), F('edition', 'Edition', '2nd ed.', '1 1 30%'),
      F('city', 'City', 'Chicago', '1 1 30%'), F('publisher', 'Publisher', 'University of Chicago Press', '1 1 100%', true), F('year', 'Year', '2026', '1 1 24%', true),
    ];
    if (type === 'video') return [
      F('author', 'Uploader', 'Simpl Courses', '1 1 100%', true), F('title', 'Video title', 'Reading a Rubric in Two Minutes', '1 1 100%', true), F('container', 'Platform', 'YouTube', '1 1 45%', true),
      F('month', 'Month', 'March', '1 1 28%'), F('day', 'Day', '4', '1 1 18%'), F('year', 'Year', '2026', '1 1 24%', true), F('url', 'URL', 'youtube.com/watch?v=…', '1 1 100%', true),
    ];
    if (type === 'coursefile') return [
      F('author', 'Instructor', 'Yuwen Li', '1 1 100%', true), F('title', 'File title', 'Lec06 — Composition of Functions', '1 1 100%', true), F('course', 'Course', 'MATH 021', '1 1 45%', true),
      F('institution', 'Institution', 'UC Merced', '1 1 45%', true), F('year', 'Year', '2026', '1 1 24%', true), F('url', 'URL', 'Optional Canvas link', '1 1 100%'),
    ];
    return [
      F('author', 'Author', 'Jane R. Okonkwo; Amir Haddad', '1 1 100%', true), F('title', 'Page title', 'How Students Actually Read Syllabi', '1 1 100%', true), F('container', 'Website', 'The Atlantic', '1 1 45%', true),
      F('publisher', 'Publisher', 'Optional if same as site', '1 1 45%'), F('month', 'Month', 'March', '1 1 28%'), F('day', 'Day', '4', '1 1 18%'), F('year', 'Year', '2026', '1 1 24%', true),
      F('url', 'URL', 'theatlantic.com/…', '1 1 100%', true), F('accessed', 'Date accessed', '12 September 2026', '1 1 45%'),
    ];
  }

  // ---- names ---------------------------------------------------------------------------------
  const splitNames = (raw) => String(raw || '').split(/\s*;\s*|\s+and\s+/i).map((s) => s.trim()).filter(Boolean);
  const parts = (name) => { const b = name.split(/\s+/).filter(Boolean); return b.length === 1 ? { first: '', middle: '', last: b[0] } : { first: b[0], middle: b.slice(1, -1).join(' '), last: b[b.length - 1] }; };
  const inverted = (name) => { const n = parts(name); return n.first ? `${n.last}, ${[n.first, n.middle].filter(Boolean).join(' ')}` : n.last; };
  const initials = (name) => { const n = parts(name); if (!n.first) return n.last; return `${n.last}, ${[n.first, ...(n.middle ? n.middle.split(/\s+/) : [])].map((w) => `${w.charAt(0).toUpperCase()}.`).join(' ')}`; };
  function authorString(raw, style) {
    const list = splitNames(raw);
    if (!list.length) return '';
    if (style === 'apa') {
      const f = list.map(initials);
      if (f.length === 1) return f[0];
      if (f.length === 2) return `${f[0]}, & ${f[1]}`;
      return `${f.slice(0, -1).join(', ')}, & ${f[f.length - 1]}`;
    }
    if (style === 'mla') {
      if (list.length === 1) return inverted(list[0]);
      if (list.length === 2) return `${inverted(list[0])}, and ${list[1]}`;
      return `${inverted(list[0])}, et al.`;
    }
    if (list.length === 1) return inverted(list[0]);
    if (list.length === 2) return `${inverted(list[0])} and ${list[1]}`;
    return `${inverted(list[0])} et al.`;
  }
  function surname(raw) {
    const list = splitNames(raw);
    if (!list.length) return '';
    const n = parts(list[0]);
    if (list.length === 1) return n.last;
    if (list.length === 2) return `${n.last} and ${parts(list[1]).last}`;
    return `${n.last} et al.`;
  }

  // ---- formatting: one template per style, switching on type, as {text, italic} segments ------
  function build(style, type, src) {
    const f = {};
    for (const d of fieldDefs(type)) f[d.key] = String(src[d.key] || '').trim(); // (the guard: only the active type's keys)
    const A = authorString(f.author, style);
    const P = [];
    const put = (text, italic = false) => { if (text) P.push({ text, italic }); };
    const dot = (s) => { const v = String(s || '').trim(); if (!v) return ''; return /[.?!]$/.test(v) ? `${v} ` : `${v}. `; };
    const url = String(f.url || '').replace(/^https?:\/\//, '');
    if (style === 'mla') {
      put(dot(A));
      if (type === 'book') { put(f.title, true); put('. '); } else put(`“${f.title || ''}.” `);
      if (type === 'website' || type === 'journal' || type === 'video') { put(f.container, true); put(', '); }
      if (type === 'journal') {
        put([f.volume ? `vol. ${f.volume}` : '', f.issue ? `no. ${f.issue}` : ''].filter(Boolean).join(', '));
        put(f.volume || f.issue ? ', ' : '');
        put(dot([f.year, f.pages ? `pp. ${f.pages}` : ''].filter(Boolean).join(', ')));
      } else if (type === 'book') put(dot([f.edition, f.publisher, f.year].filter(Boolean).join(', ')));
      else if (type === 'coursefile') { put(f.course || ''); put(f.course ? ', ' : ''); put(dot([f.institution, f.year].filter(Boolean).join(', '))); put('Canvas. '); }
      else put(dot([f.publisher, [f.day, f.month, f.year].filter(Boolean).join(' ')].filter(Boolean).join(', ')));
      if (url) put(`${url}.`);
      if (url && f.accessed && type === 'website') put(` Accessed ${f.accessed}.`);
      return P;
    }
    if (style === 'apa') {
      put(dot(A));
      const d = type === 'journal' || type === 'book' || type === 'coursefile' ? (f.year || 'n.d.') : [f.year || 'n.d.', [f.month, f.day].filter(Boolean).join(' ')].filter(Boolean).join(', ');
      put(`(${d}). `);
      if (type === 'book') { put(f.title, true); put(f.edition ? ` (${f.edition})` : ''); put('. '); }
      else if (type === 'journal') put(dot(f.title));
      else if (type === 'video') { put(f.title, true); put(' [Video]. '); }
      else if (type === 'coursefile') { put(f.title, true); put(' [Lecture slides]. '); }
      else { put(f.title, true); put('. '); }
      if (type === 'journal') {
        put(f.container, true); put(f.volume ? ', ' : ''); put(f.volume || '', true); put(f.issue ? `(${f.issue})` : ''); put(f.pages ? `, ${f.pages}` : ''); put('. ');
        if (f.doi) put(`https://doi.org/${String(f.doi).replace(/^https?:\/\/doi\.org\//, '')}`);
      } else if (type === 'book') put(dot(f.publisher));
      else if (type === 'coursefile') { put(dot([f.institution, 'Canvas'].filter(Boolean).join('. '))); if (url) put(`https://${url}`); }
      else { put(dot(f.container)); if (url) put(`https://${url}`); }
      return P;
    }
    put(dot(A));
    if (type === 'book') {
      put(f.title, true); put('. '); put(dot(f.edition));
      const imp = [[f.city, f.publisher].filter(Boolean).join(': '), f.year].filter(Boolean).join(', ');
      put(imp ? `${imp}.` : '');
      return P;
    }
    put(`“${f.title || ''}.” `);
    if (type === 'journal') {
      put(f.container, true); put(' ');
      put([f.volume, f.issue ? `no. ${f.issue}` : ''].filter(Boolean).join(', '));
      put(f.year ? ` (${f.year})` : '');
      put(f.pages ? `: ${f.pages}.` : '.');
      if (f.doi) put(` https://doi.org/${String(f.doi).replace(/^https?:\/\/doi\.org\//, '')}.`);
      return P;
    }
    if (type === 'coursefile') { put([f.course ? `${f.course} lecture slides` : '', f.institution, f.year].filter(Boolean).join(', ')); put('. Canvas.'); return P; }
    put(f.container, true); put('. ');
    put(dot([f.month, f.day].filter(Boolean).join(' ') + (f.year ? `${f.month ? ', ' : ''}${f.year}` : '')));
    if (url) put(`https://${url}.`);
    return P;
  }
  const plain = (P) => P.map((p) => p.text).join('').replace(/\s+/g, ' ').trim();
  function inText(style, type, src) {
    const has = (k) => fieldDefs(type).some((d) => d.key === k);
    const val = (k) => (has(k) ? String(src[k] || '').trim() : '');
    const s = surname(val('author')) || 'Author';
    const year = val('year') || 'n.d.';
    const pg = val('pages').split(/[–-]/)[0].trim();
    if (style === 'mla') return `(${s}${pg ? ` ${pg}` : ''})`;
    if (style === 'apa') return `(${s}, ${year}${pg ? `, p. ${pg}` : ''})`;
    return `(${s} ${year}${pg ? `, ${pg}` : ''})`;
  }

  // ---- the popup -----------------------------------------------------------------------------
  async function open(app, { from = null, url = '' } = {}) {
    const tool = T.toolOf('cite');
    const dark = app.isDark();
    const st = { style: 'mla', type: 'website', f: { ...EMPTY, ...(url ? { url: String(url) } : {}) }, saved: [], copied: 0 }; // (a link handed in — the quick menu on the pin — is filled in, on Website)
    const raw = await T.load(KEY, []);
    st.saved = Array.isArray(raw) ? raw.filter((x) => x && Array.isArray(x.parts)) : [];

    const left = U.el('bcv-cite__col');
    const right = U.el('bcv-cite__col');
    const body = U.el('bcv-cite', [left, right]);
    const p = T.popup({ tool, title: 'Citation generator', sub: '', width: 960, body, from });

    // left: style, type, the fields
    const styleSeg = T.seg(STYLES, st.style, (k) => { st.style = k; paint(); });
    const typeRow = U.el('bcv-cite__types');
    const fieldsWrap = U.el('bcv-cite__fields');
    const clearBtn = h('button', { type: 'button', class: 'bcv-tool__link', text: 'Clear', onclick: () => { st.f = { ...EMPTY }; buildFields(); paint(); } });
    left.append(
      T.card([T.label('Style'), styleSeg, U.el('bcv-tool__divider'), T.label('Source type'), typeRow]),
      T.card([U.el('bcv-tool__cardhead', [T.label('Details'), clearBtn]), fieldsWrap]),
    );
    function buildTypes() {
      typeRow.replaceChildren(...TYPES.map(([k, name, icon]) => h('button', {
        type: 'button', class: `bcv-cite__type ${st.type === k ? 'is-on' : ''}`, dataset: { type: k },
        onclick: () => { if (st.type === k) return; st.type = k; buildTypes(); buildFields(); paint(); },
      }, [U.svg(icon, { size: 14, stroke: 'currentColor', width: 1.9 }), h('span', { text: name })])));
    }
    const inputs = {};
    function buildFields() {
      fieldsWrap.replaceChildren(...fieldDefs(st.type).map((d, i) => {
        const inp = T.input({ value: st.f[d.key] || '', placeholder: d.hint, 'aria-label': d.label, dataset: { field: d.key } });
        inp.addEventListener('input', () => { st.f[d.key] = inp.value; st.copied = 0; paint(); });
        inputs[d.key] = inp;
        const req = U.text('bcv-cite__req', '', 'span');
        const lab = h('label', { class: 'bcv-cite__field', style: { flex: d.flex } }, [
          U.el('bcv-cite__fieldlabel', [h('span', { text: d.label }), req]),
          inp,
        ]);
        lab.dataset.key = d.key;
        U.enter(lab, i, 35, 300);
        return lab;
      }));
    }

    // right: the preview, in text, what is missing, Copy and Save; the saved list
    const readyBadge = h('span', { class: 'bcv-cite__badge' });
    const wcLabel = T.label('');
    const preview = h('p', { class: 'bcv-cite__preview' });
    const inTextEl = U.text('bcv-cite__intext', '', 'span');
    const copyInText = U.iconbtn(IC.copy, { size: 28, iconSize: 13, title: 'Copy in-text citation', onClick: () => { T.copyText(inText(st.style, st.type, st.f)); U.toast('In-text citation copied.'); } });
    const missing = U.el('bcv-cite__missing');
    const copyBtn = U.btn('Copy citation', { kind: 'primary', icon: IC.copy, cls: 'bcv-cite__copy', onClick: () => { if (!ready()) return; T.copyText(plain(build(st.style, st.type, st.f))); st.copied = Date.now(); paint(); setTimeout(() => { if (Date.now() - st.copied >= 1500) { st.copied = 0; paint(); } }, 1600); } });
    const saveBtn = U.btn('Save', { cls: 'bcv-cite__save', onClick: async () => {
      if (!ready()) return;
      const P = build(st.style, st.type, st.f);
      st.saved = [...st.saved, { id: T.uid('c'), style: STYLE_NAME[st.style], parts: P, plain: plain(P) }];
      await T.save(KEY, st.saved);
      st.copied = 0;
      paintSaved();
      U.toast('Saved. It stays on this device.');
    } });
    const savedLabel = T.label('Saved');
    const copyAll = h('button', { type: 'button', class: 'bcv-tool__link bcv-tool__link--blue', text: 'Copy list', onclick: () => { T.copyText(st.saved.map((x) => x.plain).sort().join('\n')); U.toast('List copied, alphabetically.'); } });
    const savedList = U.el('bcv-cite__saved');
    right.append(
      T.card([
        U.el('bcv-tool__cardhead', [wcLabel, readyBadge]),
        preview,
        U.el('bcv-cite__intextrow', [T.label('In text'), inTextEl, copyInText]),
        missing,
        U.el('bcv-tool__btns', [copyBtn, saveBtn]),
      ]),
      T.card([U.el('bcv-tool__cardhead', [savedLabel, copyAll]), savedList], 'bcv-cite__savedcard'),
      T.hint('Double-check it against your class’s style guide.'),
    );
    const missingDefs = () => fieldDefs(st.type).filter((d) => d.req && !String(st.f[d.key] || '').trim());
    const ready = () => missingDefs().length === 0;
    function paint() {
      const miss = missingDefs();
      const ok = ready();
      styleSeg.querySelectorAll('.bcv-seg__btn').forEach((b) => b.classList.toggle('is-active', b.dataset.value === st.style));
      p.setSub(`${STYLE_NAME[st.style]} · ${ok ? 'Ready' : `${miss.length} to fill`}`);
      wcLabel.textContent = `${STYLE_NAME[st.style]} · works cited`;
      readyBadge.textContent = ok ? 'Ready' : `${miss.length} to fill`;
      readyBadge.classList.toggle('is-ready', ok);
      const wasText = preview.textContent;
      preview.replaceChildren(...build(st.style, st.type, st.f).map((seg) => h('span', { class: seg.italic ? 'bcv-cite__i' : '', text: seg.text })));
      if (wasText && wasText !== preview.textContent) { preview.classList.remove('is-fresh'); void preview.offsetWidth; preview.classList.add('is-fresh'); } // a change flashes, once
      inTextEl.textContent = inText(st.style, st.type, st.f);
      missing.hidden = ok;
      missing.replaceChildren(U.svg(IC.warn, { size: 15, stroke: 'var(--bcv-orange)', width: 2 }), h('span', { class: 'bcv-pretty', text: `Add ${miss.map((m) => m.label.toLowerCase()).join(', ')} to finish this citation.` }));
      copyBtn.disabled = !ok;
      saveBtn.disabled = !ok;
      copyBtn.replaceChildren(U.svg(st.copied ? IC.check : IC.copy, { size: 14, stroke: 'currentColor', width: 2.1 }), h('span', { text: st.copied ? 'Copied' : 'Copy citation' }));
      for (const d of fieldDefs(st.type)) {
        const lab = fieldsWrap.querySelector(`.bcv-cite__field[data-key="${d.key}"]`);
        if (!lab) continue;
        const empty = d.req && !String(st.f[d.key] || '').trim();
        lab.classList.toggle('is-needed', empty);
        lab.querySelector('.bcv-cite__req').textContent = empty ? 'needed' : '';
      }
    }
    function paintSaved() {
      savedLabel.textContent = st.saved.length ? `Saved · ${st.saved.length}` : 'Saved';
      copyAll.hidden = !st.saved.length;
      savedList.replaceChildren(...(st.saved.length ? st.saved.map((x, i) => {
        const row = U.el('bcv-cite__savedrow', [
          h('span', { class: 'bcv-cite__tag', text: x.style }),
          h('p', { class: 'bcv-cite__savedtext' }, x.parts.map((seg) => h('span', { class: seg.italic ? 'bcv-cite__i' : '', text: seg.text }))),
          U.iconbtn(IC.close, { size: 26, iconSize: 12, title: 'Remove', onClick: async () => { st.saved = st.saved.filter((y) => y.id !== x.id); await T.save(KEY, st.saved); paintSaved(); } }),
        ]);
        U.enter(row, i, 35, 300);
        return row;
      }) : [T.hint('Saved citations stay on this device.')]));
    }
    buildTypes();
    buildFields();
    paint();
    paintSaved();
    void dark;
    return p;
  }

  BCV.toolsCite = { open, build, plain, inText, fieldDefs, authorString };
})();
