/* Simpl's solver page, as Mathway works: a problem sent appears on the right; on the left, the ways it can be worked
 * ("Choose an option"), then — one picked — its answer, with its steps and its graph a tap away. The problem is typed
 * at the foot, read back as maths while it is typed, with the keypad under it. The app hosting it (the Mac's and
 * Windows' Calculator tool, its pin) gives its look in the address: ?theme=dark&accent=%230a84ff&os=mac|win&compact=1&q=… */
(function () {
  'use strict';
  const $ = (id) => document.getElementById(id);
  // (the look in the address: its query, or — the Mac's, a file's query not kept by every WebKit — its fragment)
  const P = new URLSearchParams(location.search || location.hash.slice(1));
  const root = document.documentElement;
  root.dataset.theme = P.get('theme') === 'dark' ? 'dark' : 'light';
  root.dataset.os = P.get('os') || (/Windows/.test(navigator.userAgent) ? 'win' : 'mac');
  const compact = P.get('compact') === '1';
  if (P.get('clear') === '1') root.dataset.clear = '1';
  if (compact) root.dataset.compact = '1';
  function setAccent(a) {
    if (!a || !/^#[0-9a-f]{6}$/i.test(a)) return;
    root.style.setProperty('--accent', a);
    const n = parseInt(a.slice(1), 16);
    const lum = (c) => { c /= 255; return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); };
    const L = 0.2126 * lum(n >> 16 & 255) + 0.7152 * lum(n >> 8 & 255) + 0.0722 * lum(n & 255);
    root.style.setProperty('--accent-ink', 1.05 / (L + 0.05) >= 3 ? '#fff' : '#111');
  }
  setAccent(P.get('accent'));

  // (KaTeX's style sheet, its fonts where the extension keeps them)
  try {
    const st = document.createElement('style');
    st.textContent = String(self.BCV_KATEX_CSS || '').split('url(fonts/').join('url(../../lib/vendor/katex/fonts/');
    document.head.appendChild(st);
  } catch (e) { /* no KaTeX: TeX as written */ }

  const esc = (s) => String(s).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
  const math = (t, display) => {
    try { return window.katex ? katex.renderToString(t, { throwOnError: false, displayMode: !!display, strict: false }) : esc(t); }
    catch (e) { return esc(t); }
  };
  const words = (s) => String(s || '').split(/(\$[^$]+\$)/g).map((p) => (/^\$[^$]+\$$/.test(p) ? math(p.slice(1, -1)) : esc(p))).join('');
  const el = (tag, cls, html) => { const e = document.createElement(tag); if (cls) e.className = cls; if (html !== undefined) e.innerHTML = html; return e; };

  function post(msg) {
    try {
      if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.simplSolver) window.webkit.messageHandlers.simplSolver.postMessage(msg);
      else if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(JSON.stringify(Object.assign({ solver: true }, msg)));
    } catch (e) { /* not in the app */ }
  }
  function localGet(k, d) { try { const v = localStorage.getItem('simplSolver.' + k); return v === null ? d : v; } catch (e) { return d; } }
  function localSet(k, v) { try { localStorage.setItem('simplSolver.' + k, v); } catch (e) { /* none */ } }

  const q = $('q'), thread = $('thread');

  // ---------------------------------------------------------------- as it is typed

  function readInput() {
    const text = q.value.trim();
    $('clear').hidden = !text;
    post({ kind: 'text', text });
    const pv = $('preview');
    if (!text) { pv.className = 'preview empty'; pv.textContent = 'Your problem appears here as maths'; return; }
    try { pv.className = 'preview'; pv.innerHTML = math(SimplSolver.analyze(text).tex); }
    catch (e) { pv.className = 'preview err'; pv.textContent = e.message || 'Not understood yet'; }
  }

  // ---------------------------------------------------------------- the conversation

  function scrollDown() { requestAnimationFrame(() => { thread.scrollTop = thread.scrollHeight; }); }
  /** A reply shown from its start: with the problem it answers above it when both fit, else from its own top (a tall
   *  answer in a short window was shown by its foot, its title and first answer scrolled away). */
  function reveal(m) {
    requestAnimationFrame(() => {
      const prev = m.previousElementSibling;
      const both = prev && prev.classList.contains('me') && m.offsetTop + m.offsetHeight - prev.offsetTop <= thread.clientHeight;
      thread.scrollTop = both ? thread.scrollHeight : m.offsetTop - 10;
    });
  }
  function me(text, tex) {
    $('hello').hidden = true;
    const m = el('div', 'msg me');
    const b = el('div', 'bubble', tex ? math(tex) : esc(text));
    b.title = text;
    m.appendChild(b);
    thread.appendChild(m);
    scrollDown();
  }
  function bot(cont) {
    const m = el('div', 'msg bot' + (cont ? ' cont' : ''));
    m.appendChild(el('div', 'avatar', '∑'));
    const b = el('div', 'bubble');
    m.appendChild(b);
    thread.appendChild(m);
    scrollDown();
    return b;
  }

  /** A problem sent: shown on the right, then its options — or, asked plainly (factor …, d/dx …, a number), its answer. */
  function send(text, opt) {
    text = String(text || '').trim();
    if (!text) { q.focus(); return; }
    let info;
    try { info = SimplSolver.analyze(text); }
    catch (e) {
      me(text, null);
      const b = bot();
      b.innerHTML = '<div class="err">' + esc(e.message || 'That is not understood') + '</div><div class="note">Check the brackets and the operators, then send it again.</div>';
      return;
    }
    me(text, info.tex);
    remember(text);
    q.value = '';
    readInput();
    const direct = opt || (info.params && Object.keys(info.params).length && read(text).command ? info.chosen : null) || (read(text).command ? info.chosen : null) || (info.kind === 'number' ? 'evaluate' : null);
    if (direct) { answerFor(text, info, direct, {}); return; }
    choose(text, info);
  }
  const read = (t) => SimplSolver.read(t);

  /** "Choose an option": the ways it can be worked, the likeliest first, the rest under More. */
  function choose(text, info) {
    const b = bot();
    b.appendChild(el('div', 'choose', 'Choose an option'));
    const list = el('div', 'choices');
    const opts = info.options;
    const first = compact ? 4 : 6;
    const add = (o) => {
      const c = el('button', 'choice', '<span>' + esc(o.label) + '</span><span class="arrow">›</span>');
      c.onclick = () => {
        list.querySelectorAll('.choice').forEach((x) => x.classList.toggle('on', x === c));
        answerFor(text, info, o.id, {});
      };
      list.appendChild(c);
    };
    opts.slice(0, first).forEach(add);
    if (opts.length > first) {
      const more = el('button', 'more', 'More options');
      more.onclick = () => { more.remove(); opts.slice(first).forEach(add); };
      list.appendChild(more);
    }
    b.appendChild(list);
  }

  /** The option worked, in a reply: its answer, Show steps, its graph; or what it still needs (a point, the limits). */
  function answerFor(text, info, option, params) {
    const r = SimplSolver.run(text, option, params);
    const label = (info.options.find((o) => o.id === option) || { label: r.title || 'Answer' }).label;
    const last = thread.lastElementChild;
    const b = bot(!!(last && last.classList.contains('bot')));
    if (r.error) {
      b.innerHTML = '<div class="title">' + esc(label) + '</div><div class="err">' + esc(r.error) + '</div>';
      return;
    }
    if (r.ask) { askFor(b, text, info, option, label, r.ask); return; }
    b.appendChild(el('div', 'title', esc(label)));
    const ans = el('div', 'answers');
    for (const a of r.answers) ans.appendChild(el('div', 'answer' + (a.none ? ' none' : ''), math('\\displaystyle ' + a.tex) + (a.approx ? '<span class="approx">≈ ' + esc(a.approx) + '</span>' : '')));
    b.appendChild(ans);
    if (r.alternates && r.alternates.length) {
      const alts = el('div', 'alts');
      for (const a of r.alternates) alts.appendChild(el('div', 'alt', '<span class="label">' + esc(a.label) + '</span>' + math(a.tex)));
      b.appendChild(alts);
    }
    if (r.note) b.appendChild(el('div', 'note', esc(r.note)));
    const acts = el('div', 'actions');
    let stepsBox = null, plotBox = null;
    if (r.steps && r.steps.length) {
      const s = el('button', 'act primary', 'Show steps');
      s.onclick = () => {
        if (compact) { post({ kind: 'openFull', text, option }); return; }
        if (!stepsBox) { stepsBox = stepsOf(r.steps); b.insertBefore(stepsBox, acts.nextSibling); }
        else stepsBox.hidden = !stepsBox.hidden;
        s.textContent = stepsBox.hidden ? 'Show steps' : 'Hide steps';
      };
      acts.appendChild(s);
      if (localGet('steps', '0') === '1' && !compact) setTimeout(() => s.click());
    }
    if (r.graph && r.graph.fns && r.graph.fns.length && !compact && option !== 'graph') {
      const g = el('button', 'act', 'Graph');
      g.onclick = () => {
        if (!plotBox) { plotBox = plotOf(r.graph); b.appendChild(plotBox); plotBox.draw(); }
        else { plotBox.hidden = !plotBox.hidden; if (!plotBox.hidden) plotBox.draw(); }
        g.textContent = plotBox.hidden ? 'Graph' : 'Hide graph';
        scrollDown();
      };
      acts.appendChild(g);
    }
    const c = el('button', 'act', 'Copy');
    c.onclick = () => { copy(r.answers.map((a) => a.text || '').filter(Boolean).join('; ')); c.textContent = 'Copied'; setTimeout(() => { c.textContent = 'Copy'; }, 1200); };
    acts.appendChild(c);
    if (compact) {
      const f = el('button', 'act', 'Open full screen');
      f.onclick = () => post({ kind: 'openFull', text, option });
      acts.appendChild(f);
    }
    b.appendChild(acts);
    // (Graph picked: drawn at once)
    if (option === 'graph' && r.graph && !compact) { plotBox = plotOf(r.graph); b.appendChild(plotBox); plotBox.draw(); }
    reveal(b.parentNode);
  }

  /** What an option still needs: x = …, x → …, from … to … (an integral's limits may stay empty). */
  function askFor(b, text, info, option, label, ask) {
    b.appendChild(el('div', 'title', esc(label)));
    const line = el('div', 'ask-line');
    const fields = {};
    const field = (lab, key, ph) => {
      if (lab) line.appendChild(el('span', '', math(lab)));
      const i = el('input');
      i.placeholder = ph;
      i.onkeydown = (e) => { if (e.key === 'Enter') go(); };
      fields[key] = i;
      line.appendChild(i);
      return i;
    };
    if (ask.what === 'to') field(info.variable + ' \\to', 'to', '0, inf, 2');
    else if (ask.what === 'at') field(info.variable + ' =', 'at', '3');
    const btn = el('button', 'go2', 'Solve');
    btn.onclick = go;
    line.appendChild(btn);
    b.appendChild(line);
    setTimeout(() => { const f = Object.values(fields)[0]; if (f) f.focus(); });
    function go() {
      const params = {};
      for (const [k, i] of Object.entries(fields)) { if (!i.value.trim()) { i.focus(); return; } params[k] = i.value.trim(); }
      line.querySelectorAll('input,button').forEach((x) => { x.disabled = true; });
      answerFor(text, info, option, params);
    }
  }

  function stepsOf(steps) {
    const box = el('div', 'steps');
    for (const s of steps) box.appendChild(el('div', 'step', '<div class="n"></div><div><div>' + words(s.text) + '</div>' + (s.tex ? '<div class="math">' + math(s.tex, true) + '</div>' : '') + '</div>'));
    localSet('steps', '1');
    return box;
  }

  function copy(t) {
    post({ kind: 'copy', text: t });
    try { navigator.clipboard.writeText(t); } catch (e) { /* the app copies it */ }
  }

  // ---------------------------------------------------------------- a graph in a reply

  const COLORS = ['var(--accent)', '#ff9f0a', '#30d158', '#bf5af2', '#ff375f'];
  const css = (name) => getComputedStyle(root).getPropertyValue(name).trim();
  function plotOf(graph) {
    const box = el('div', 'plot');
    const canvas = el('canvas');
    box.appendChild(canvas);
    const bar = el('div', 'plotbar');
    const legend = el('div', 'legend', graph.fns.map((f, i) => '<span><i style="background:' + COLORS[i % COLORS.length] + '"></i>' + math('y = ' + f.tex) + '</span>').join(''));
    const zoom = el('div', 'zoom', '<button title="Zoom in">＋</button><button title="Zoom out">－</button><button title="Back to the start">⟲</button>');
    bar.appendChild(legend); bar.appendChild(zoom);
    box.appendChild(bar);
    const fs = graph.fns.map((f) => SimplSolver.plotter(f.js, graph.x));
    let view = home();
    function home() {
      let xs = [-10, 10];
      const pts = (graph.points || []).map((p) => p[0]).concat(graph.shade || []).filter(isFinite);
      if (pts.length) { const lo = Math.min(...pts), hi = Math.max(...pts); const pad = Math.max(2, (hi - lo) * 0.6); xs = [lo - pad, hi + pad]; }
      const ys = [];
      for (const f of fs) for (let i = 0; i <= 200; i++) { const v = f(xs[0] + (xs[1] - xs[0]) * i / 200); if (isFinite(v)) ys.push(v); }
      ys.sort((a, b) => a - b);
      let lo = ys.length ? ys[Math.floor(ys.length * 0.05)] : -10, hi = ys.length ? ys[Math.ceil(ys.length * 0.95) - 1] : 10;
      if (hi - lo < 1e-9) { lo -= 5; hi += 5; }
      const pad = (hi - lo) * 0.15;
      return { x0: xs[0], x1: xs[1], y0: Math.min(lo - pad, -pad * 0.2), y1: Math.max(hi + pad, pad * 0.2) };
    }
    function draw() {
      const dpr = window.devicePixelRatio || 1, w = canvas.clientWidth, h = canvas.clientHeight;
      if (!w || !h) return;
      canvas.width = Math.round(w * dpr); canvas.height = Math.round(h * dpr);
      const g = canvas.getContext('2d');
      g.setTransform(dpr, 0, 0, dpr, 0, 0);
      g.clearRect(0, 0, w, h);
      const X = (x) => (x - view.x0) / (view.x1 - view.x0) * w, Y = (y) => h - (y - view.y0) / (view.y1 - view.y0) * h;
      const stepOf = (span, px) => { const raw = span / (px / 70); const p = Math.pow(10, Math.floor(Math.log10(raw))); return [1, 2, 5, 10].map((m) => m * p).find((s) => s >= raw); };
      const sx = stepOf(view.x1 - view.x0, w), sy = stepOf(view.y1 - view.y0, h);
      g.lineWidth = 1; g.strokeStyle = css('--line'); g.fillStyle = css('--ink2'); g.font = '11px ' + css('--font');
      for (let x = Math.ceil(view.x0 / sx) * sx; x <= view.x1; x += sx) { g.beginPath(); g.moveTo(X(x), 0); g.lineTo(X(x), h); g.stroke(); if (Math.abs(x) > sx / 2) g.fillText(fmt(x), X(x) + 3, Math.min(h - 4, Math.max(12, Y(0) + 13))); }
      for (let y = Math.ceil(view.y0 / sy) * sy; y <= view.y1; y += sy) { g.beginPath(); g.moveTo(0, Y(y)); g.lineTo(w, Y(y)); g.stroke(); if (Math.abs(y) > sy / 2) g.fillText(fmt(y), Math.min(w - 30, Math.max(3, X(0) + 4)), Y(y) - 3); }
      g.strokeStyle = css('--ink2'); g.lineWidth = 1.2;
      g.beginPath(); g.moveTo(0, Y(0)); g.lineTo(w, Y(0)); g.moveTo(X(0), 0); g.lineTo(X(0), h); g.stroke();
      if (graph.shade && fs[0]) {
        const [a, b] = graph.shade;
        g.fillStyle = 'rgba(127,127,127,0.2)';
        g.beginPath(); g.moveTo(X(a), Y(0));
        for (let i = 0; i <= 300; i++) { const t = a + (b - a) * i / 300, v = fs[0](t); g.lineTo(X(t), Y(isFinite(v) ? v : 0)); }
        g.lineTo(X(b), Y(0)); g.closePath(); g.fill();
      }
      fs.forEach((f, k) => {
        g.strokeStyle = k === 0 ? css('--accent') : COLORS[k % COLORS.length]; g.lineWidth = 2.2; g.lineJoin = 'round';
        g.beginPath();
        let pen = false, prev = null;
        const n = Math.max(400, w * 2);
        for (let i = 0; i <= n; i++) {
          const x = view.x0 + (view.x1 - view.x0) * i / n, y = f(x);
          if (!isFinite(y) || (prev !== null && Math.abs(Y(y) - Y(prev)) > h * 2)) { pen = false; prev = isFinite(y) ? y : null; continue; }
          if (!pen) { g.moveTo(X(x), Y(y)); pen = true; } else g.lineTo(X(x), Y(y));
          prev = y;
        }
        g.stroke();
      });
      for (const [px, py] of graph.points || []) {
        if (!isFinite(px) || !isFinite(py)) continue;
        g.fillStyle = css('--card'); g.strokeStyle = css('--accent'); g.lineWidth = 2.2;
        g.beginPath(); g.arc(X(px), Y(py), 4.5, 0, Math.PI * 2); g.fill(); g.stroke();
      }
    }
    const zoomBy = (k, cx, cy) => {
      cx = cx === undefined ? (view.x0 + view.x1) / 2 : cx; cy = cy === undefined ? (view.y0 + view.y1) / 2 : cy;
      view = { x0: cx + (view.x0 - cx) * k, x1: cx + (view.x1 - cx) * k, y0: cy + (view.y0 - cy) * k, y1: cy + (view.y1 - cy) * k };
      draw();
    };
    let drag = null;
    canvas.addEventListener('pointerdown', (e) => { canvas.setPointerCapture(e.pointerId); drag = { x: e.clientX, y: e.clientY, v: Object.assign({}, view) }; });
    canvas.addEventListener('pointermove', (e) => {
      if (!drag) return;
      const w = canvas.clientWidth, h = canvas.clientHeight;
      const dx = (e.clientX - drag.x) / w * (drag.v.x1 - drag.v.x0), dy = (e.clientY - drag.y) / h * (drag.v.y1 - drag.v.y0);
      view = { x0: drag.v.x0 - dx, x1: drag.v.x1 - dx, y0: drag.v.y0 + dy, y1: drag.v.y1 + dy };
      draw();
    });
    canvas.addEventListener('pointerup', () => { drag = null; });
    canvas.addEventListener('wheel', (e) => {
      e.preventDefault();
      const r = canvas.getBoundingClientRect();
      zoomBy(Math.exp(e.deltaY * 0.0015), view.x0 + (e.clientX - r.left) / r.width * (view.x1 - view.x0), view.y1 - (e.clientY - r.top) / r.height * (view.y1 - view.y0));
    }, { passive: false });
    const zb = zoom.querySelectorAll('button');
    zb[0].onclick = () => zoomBy(0.7); zb[1].onclick = () => zoomBy(1 / 0.7); zb[2].onclick = () => { view = home(); draw(); };
    window.addEventListener('resize', draw);
    box.draw = () => requestAnimationFrame(draw);
    return box;
  }
  function fmt(v) { const r = Math.round(v * 1e6) / 1e6; return Math.abs(r) >= 1e5 || (Math.abs(r) < 1e-3 && r !== 0) ? r.toExponential(1) : String(r); }

  // ---------------------------------------------------------------- the keypad

  // [what it types, its face as TeX (else the text), how it looks: op | alt | wide]
  const KEYS = {
    basic: [['7'], ['8'], ['9'], ['/', '\\div', 'op'], ['(', '(', 'alt'], [')', ')', 'alt'], ['^', 'x^{\\square}', 'alt'], ['sqrt(', '\\sqrt{\\square}', 'alt'], ['pi', '\\pi', 'alt'], ['<-', '\\leftarrow', 'alt'],
      ['4'], ['5'], ['6'], ['*', '\\times', 'op'], ['x', 'x', 'alt'], ['^2', '\\square^2', 'alt'], ['|', '|\\square|', 'alt'], ['!', 'n!', 'alt'], ['e', 'e', 'alt'], ['C', '\\text{C}', 'alt'],
      ['1'], ['2'], ['3'], ['-', '-', 'op'], ['y', 'y', 'alt'], ['=', '=', 'alt'], ['<', '<', 'alt'], ['>', '>', 'alt'], [',', ',', 'alt'], ['%', '\\%', 'alt'],
      ['0'], ['.'], ['+', '+', 'op'], ['/', '\\frac{\\square}{\\square}', 'alt'], ['<=', '\\le', 'alt'], ['>=', '\\ge', 'alt'], ['inf', '\\infty', 'alt'], ['ln(', '\\ln', 'alt'], ['log(', '\\log', 'alt'], ['GO', '\\text{Solve}', 'op']],
    algebra: [['x', 'x'], ['y', 'y'], ['z', 'z'], ['a', 'a'], ['b', 'b'], ['^', 'x^{\\square}', 'alt'], ['^2', 'x^2', 'alt'], ['^3', 'x^3', 'alt'], ['sqrt(', '\\sqrt{\\square}', 'alt'], ['cbrt(', '\\sqrt[3]{\\square}', 'alt'],
      ['(', '('], [')', ')'], ['=', '='], ['<', '<'], ['>', '>'], ['<=', '\\le'], ['>=', '\\ge'], ['|', '|\\square|', 'alt'], ['log_', '\\log_{\\square}', 'alt'], ['e^', 'e^{\\square}', 'alt'],
      ['solve ', '\\text{solve}', 'wide op'], ['factor ', '\\text{factor}', 'wide op'], ['expand ', '\\text{expand}', 'wide op'], ['simplify ', '\\text{simplify}', 'wide op'], [', ', '\\text{and } (,)', 'wide alt']],
    trig: [['sin('], ['cos('], ['tan('], ['sec('], ['csc('], ['cot('], ['pi', '\\pi', 'alt'], ['^2', '\\square^2', 'alt'], ['(', '('], [')', ')'],
      ['arcsin(', '\\sin^{-1}'], ['arccos(', '\\cos^{-1}'], ['arctan(', '\\tan^{-1}'], ['sinh('], ['cosh('], ['tanh('], ['x', 'x', 'alt'], ['t', '\\theta', 'alt'], ['=', '='], ['/', '\\frac{\\square}{\\square}', 'alt']],
    calc: [['d/dx ', '\\frac{d}{dx}', 'wide op'], ['integrate ', '\\int', 'wide op'], [' dx', 'dx', 'alt'], [' from 0 to 1', '\\int_a^b', 'wide alt'], ['lim x->0 ', '\\lim_{x\\to a}', 'wide op'], ['inf', '\\infty', 'alt'],
      ['x', 'x'], ['^', 'x^{\\square}', 'alt'], ['e^', 'e^{\\square}', 'alt'], ['ln(', '\\ln'], ['sin('], ['cos('], ['sqrt(', '\\sqrt{\\square}', 'alt'], ['(', '('], [')', ')'], ['/', '\\frac{\\square}{\\square}', 'alt']],
  };
  // (faces that are words or plain signs in the keypad's own type, as a calculator's are; only the maths in KaTeX's)
  const PLAIN = { '\\div': '÷', '\\times': '×', '-': '−', '+': '+', '(': '(', ')': ')', '=': '=', '<': '<', '>': '>', ',': ',', '\\%': '%',
    '\\leftarrow': '⌫', '\\le': '≤', '\\ge': '≥', '\\ln': 'ln', '\\log': 'log', '\\infty': '∞' };
  function faceOf(k, face) {
    if (!face) return '<span class="word">' + esc(k.replace(/\($/, '')) + '</span>';
    if (PLAIN[face]) return '<span class="word">' + esc(PLAIN[face]) + '</span>';
    const w = face.match(/^\\text\{([^}]*)\}(.*)$/);
    if (w) return '<span class="word">' + esc(w[1] + w[2]) + '</span>';
    return math(face);
  }
  function drawKeys(tab) {
    const box = $('keys');
    box.innerHTML = '';
    for (const [k, face, kind] of KEYS[tab]) {
      const b = el('button', 'key ' + (kind || ''), faceOf(k, face));
      b.title = k === 'GO' ? 'Solve' : k === '<-' ? 'Delete' : k === 'C' ? 'Clear' : k.trim();
      b.onmousedown = (e) => e.preventDefault();
      b.onclick = () => press(k);
      box.appendChild(b);
    }
  }
  const FRONT = ['solve ', 'factor ', 'expand ', 'simplify ', 'd/dx ', 'integrate ', 'lim x->0 '];
  function press(k) {
    if (k === 'GO') { send(q.value); return; }
    if (k === 'C') { q.value = ''; readInput(); q.focus(); return; }
    if (k === '<-') {
      const s = q.selectionStart ?? q.value.length, e = q.selectionEnd ?? s;
      if (s !== e) q.value = q.value.slice(0, s) + q.value.slice(e);
      else if (s > 0) { q.value = q.value.slice(0, s - 1) + q.value.slice(s); q.setSelectionRange(s - 1, s - 1); }
      readInput(); q.focus(); return;
    }
    if (FRONT.includes(k)) { q.value = k + q.value.replace(/^(solve|factor|expand|simplify|d\/dx|integrate|lim x->\S+)\s*/, ''); q.focus(); q.setSelectionRange(q.value.length, q.value.length); readInput(); return; }
    const s = q.selectionStart ?? q.value.length, e = q.selectionEnd ?? s;
    q.value = q.value.slice(0, s) + k + q.value.slice(e);
    q.setSelectionRange(s + k.length, s + k.length);
    q.focus();
    readInput();
  }
  document.querySelectorAll('.ktab').forEach((t) => {
    t.onclick = () => { document.querySelectorAll('.ktab').forEach((u) => u.classList.toggle('on', u === t)); drawKeys(t.dataset.tab); localSet('tab', t.dataset.tab); };
  });
  const tab = localGet('tab', 'basic');
  document.querySelectorAll('.ktab').forEach((u) => u.classList.toggle('on', u.dataset.tab === tab));
  drawKeys(KEYS[tab] ? tab : 'basic');
  // (folded at first in the pin, and in a window too short for the keypad and an answer both)
  const kbOpen = localGet('keypad', compact || innerHeight < 560 ? '0' : '1') === '1';
  $('keypad').hidden = !kbOpen;
  $('kbToggle').setAttribute('aria-expanded', String(kbOpen));
  $('kbToggle').onclick = () => {
    const kb = $('keypad');
    kb.hidden = !kb.hidden;
    $('kbToggle').setAttribute('aria-expanded', String(!kb.hidden));
    localSet('keypad', kb.hidden ? '0' : '1');
    scrollDown();
  };

  // ---------------------------------------------------------------- the start

  const EXAMPLES = [
    ['Solve', '2x + 5 = 3x - 4'], ['Quadratic', 'x^2 - 5x + 6 = 0'], ['Factor', 'factor x^3 - 8'], ['Simplify', 'simplify (x^2 - 9)/(x + 3)'],
    ['Derivative', 'd/dx x^2 sin(x)'], ['Integral', 'integrate x e^x dx'], ['Limit', 'lim x->0 sin(x)/x'], ['System', 'x + y = 7, 2x - y = 2'],
  ];
  function remember(text) {
    let h = [];
    try { h = JSON.parse(localGet('history', '[]')) || []; } catch (e) { h = []; }
    h = h.filter((x) => x !== text);
    h.unshift(text);
    localSet('history', JSON.stringify(h.slice(0, 12)));
  }
  function drawStart() {
    const ex = $('examples');
    ex.innerHTML = '';
    let recent = [];
    try { recent = JSON.parse(localGet('history', '[]')) || []; } catch (e) { recent = []; }
    const list = recent.length ? recent.slice(0, compact ? 3 : 6).map((t) => ['Recent', t]).concat(EXAMPLES.slice(0, compact ? 2 : 4)) : (compact ? EXAMPLES.slice(0, 4) : EXAMPLES);
    for (const [label, t] of list) {
      let shown;
      try { shown = math(SimplSolver.analyze(t).tex); } catch (e) { shown = esc(t); }
      const b = el('button', 'example', '<small>' + esc(label) + '</small>' + shown);
      b.onclick = () => send(t);
      ex.appendChild(b);
    }
  }

  q.addEventListener('input', readInput);
  q.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') { e.preventDefault(); send(q.value); }
    if (e.key === 'Escape' && q.value) { e.preventDefault(); q.value = ''; readInput(); }
  });
  $('go').onclick = () => send(q.value);
  $('clear').onclick = () => { q.value = ''; readInput(); q.focus(); };

  /** From the app: a problem to work (a pin's carried over, the screenshot suite's), and its option. */
  window.simplSolverOpen = (text, opt) => { if (text) send(text, opt || null); };
  window.simplSolverTheme = (theme, acc) => { root.dataset.theme = theme === 'dark' ? 'dark' : 'light'; setAccent(acc); };

  drawStart();
  readInput();
  const first = P.get('q');
  if (first) send(first, P.get('option'));
  q.focus();
  post({ kind: 'ready' });
})();
