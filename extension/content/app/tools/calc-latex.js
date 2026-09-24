/* The calculator's line as LaTeX. content/app/tools/tools.js draws the calculator and works its
 * line out; this reads the same line — the same tokens, the same grammar — into a tree and writes
 * the tree out as LaTeX, so the tool can typeset the sum as it is typed and hand the LaTeX over:
 * 2 ÷ 3 is \frac{2}{3}, √(16) is \sqrt{16}, x^(1÷y) the y-th root, sin⁻¹ is \sin^{-1}, log(x, y)
 * is \log_{y}, 2E−3 is 2 × 10^{-3}; a bracket still open is closed for it and a number still wanted
 * is a small square. No DOM, no fetch: pure functions, checked by the api suite. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  // the calculator's own tokens (tools.js CALC_TOKEN), kept the same
  const TOKEN = /\s*(?:(\d+(?:\.\d*)?(?:E−?\d+)?|\.\d+(?:E−?\d+)?)|(π|e)|(sinh⁻¹|cosh⁻¹|tanh⁻¹|sin⁻¹|cos⁻¹|tan⁻¹|sinh|cosh|tanh|sin|cos|tan|log₂|log|ln|√|∛)\(|(⁻¹|[()+−×÷^,!%²³]))/uy;
  /** The line's tokens, brackets left open closed for it; null where a character is not the calculator's. */
  function tokens(src) {
    const s = String(src || '');
    let open = 0;
    for (const ch of s) { if (ch === '(') open++; else if (ch === ')') open--; }
    const closed = s + ')'.repeat(Math.max(0, open));
    const out = [];
    for (let i = 0; i < closed.length;) {
      TOKEN.lastIndex = i;
      const m = TOKEN.exec(closed);
      if (!m || !m[0].length) return null;
      i = TOKEN.lastIndex;
      if (m[1] !== undefined) out.push({ t: 'num', v: m[1] });
      else if (m[2]) out.push({ t: 'const', v: m[2] });
      else if (m[3]) out.push({ t: 'fn', v: m[3] });
      else if (m[4]) out.push({ t: 'sym', v: m[4] });
    }
    return out;
  }
  /** The line as a tree (the calculator's grammar: + − over × ÷ and things side by side, − in front,
   *  ^ from the right, ! % ² ³ ⁻¹ after their number, a function over its bracket); a number still
   *  wanted is a hole. Null where the line cannot be read at all. */
  function ast(src) {
    const toks = tokens(src);
    if (!toks) return null;
    let p = 0;
    const sym = (s) => toks[p] !== undefined && toks[p].t === 'sym' && toks[p].v === s;
    const startsValue = () => toks[p] !== undefined && (toks[p].t !== 'sym' || toks[p].v === '(');
    const expr = () => { let v = term(); while (sym('+') || sym('−')) { const op = toks[p++].v; v = { t: 'bin', op, l: v, r: term() }; } return v; };
    const term = () => { let v = unary(); for (;;) { if (sym('×') || sym('÷')) { const op = toks[p++].v; v = { t: 'bin', op, l: v, r: unary() }; } else if (startsValue()) v = { t: 'bin', op: '', l: v, r: unary() }; else return v; } };
    const unary = () => { if (sym('−')) { p++; return { t: 'neg', a: unary() }; } if (sym('+')) { p++; return unary(); } return power(); };
    const power = () => { const b = postfix(); if (sym('^')) { p++; return { t: 'pow', b, e: unary() }; } return b; };
    const postfix = () => { let v = atom(); for (;;) { if (sym('!') || sym('%') || sym('²') || sym('³') || sym('⁻¹')) v = { t: 'post', op: toks[p++].v, a: v }; else return v; } };
    const atom = () => {
      const t = toks[p];
      if (!t) return { t: 'hole' };
      if (t.t === 'num') { p++; return { t: 'num', v: t.v }; }
      if (t.t === 'const') { p++; return { t: 'const', v: t.v }; }
      if (t.t === 'fn') { p++; const args = [expr()]; while (sym(',')) { p++; args.push(expr()); } if (sym(')')) p++; return { t: 'fn', name: t.v, args }; }
      if (sym('(')) { p++; const a = expr(); if (sym(')')) p++; return { t: 'paren', a }; }
      return { t: 'hole' };
    };
    const node = expr();
    return p < toks.length ? null : node;
  }
  // a number as typed: a leading point gets its zero, a trailing point goes, E is × 10 to the power
  const num = (v) => {
    let s = String(v);
    if (s.startsWith('.')) s = `0${s}`;
    const m = /^(.*?)\.?E(−?)(\d+)$/.exec(s);
    if (m) return `${m[1]} \\times 10^{${m[2] ? '-' : ''}${m[3]}}`;
    return s.endsWith('.') ? s.slice(0, -1) : s;
  };
  const FN = { sin: '\\sin', cos: '\\cos', tan: '\\tan', 'sin⁻¹': '\\sin^{-1}', 'cos⁻¹': '\\cos^{-1}', 'tan⁻¹': '\\tan^{-1}', sinh: '\\sinh', cosh: '\\cosh', tanh: '\\tanh', 'sinh⁻¹': '\\sinh^{-1}', 'cosh⁻¹': '\\cosh^{-1}', 'tanh⁻¹': '\\tanh^{-1}', ln: '\\ln', 'log₂': '\\log_{2}' };
  const wrap = (s) => `\\left(${s}\\right)`;
  function emit(n) {
    switch (n.t) {
      case 'hole': return '\\square';
      case 'num': return num(n.v);
      case 'const': return n.v === 'π' ? '\\pi' : 'e';
      case 'paren': return wrap(emit(n.a));
      case 'neg': return `-${emit(n.a)}`;
      case 'bin':
        if (n.op === '÷') return `\\frac{${emit(n.l)}}{${emit(n.r)}}`;
        if (n.op === '×') return `${emit(n.l)} \\times ${emit(n.r)}`;
        if (n.op === '') return `${emit(n.l)} ${emit(n.r)}`; // side by side: 2π, 2(3 + 4)
        return `${emit(n.l)} ${n.op === '−' ? '-' : '+'} ${emit(n.r)}`;
      case 'pow': {
        const e = n.e;
        if (e.t === 'paren' && e.a.t === 'bin' && e.a.op === '÷' && e.a.l.t === 'num' && e.a.l.v === '1') return `\\sqrt[${emit(e.a.r)}]{${emit(n.b)}}`; // x^(1÷y): the y-th root, as the key says
        return `${emit(n.b)}^{${emit(e.t === 'paren' ? e.a : e)}}`; // (e^(x), 10^(x): the bracket the key wrote is the braces)
      }
      case 'post': {
        const a = emit(n.a);
        return n.op === '!' ? `${a}!` : n.op === '%' ? `${a}\\%` : n.op === '²' ? `${a}^{2}` : n.op === '³' ? `${a}^{3}` : `${a}^{-1}`;
      }
      case 'fn': {
        const [x, y] = n.args.map(emit);
        if (n.name === '√') return `\\sqrt{${x}}`;
        if (n.name === '∛') return `\\sqrt[3]{${x}}`;
        if (n.name === 'log') return `\\log_{${y === undefined ? '10' : y}}${wrap(x)}`;
        return `${FN[n.name] || `\\operatorname{${n.name}}`}${wrap(x)}`;
      }
      default: return '';
    }
  }
  /** The line as LaTeX; null where it cannot be read. */
  const latex = (src) => { const n = ast(src); return n ? emit(n) : null; };
  /** A worked-out number as LaTeX: plain digits, a very large or small one as a × 10 to a power (the display's own cut-offs). */
  function numTex(v) {
    if (!Number.isFinite(v)) return '\\text{Error}';
    if (Object.is(v, -0)) v = 0;
    const abs = Math.abs(v);
    const s = abs >= 1e15 || (abs > 0 && abs < 1e-9) ? v.toExponential(8).replace(/\.?0+e/, 'e') : String(Number(v.toPrecision(12)));
    const m = /^(-?[\d.]+)e([+-]?)(\d+)$/.exec(s);
    return m ? `${m[1]} \\times 10^{${m[2] === '-' ? '-' : ''}${m[3]}}` : s;
  }
  BCV.calcTex = { tokens, ast, latex, numTex };
})();
