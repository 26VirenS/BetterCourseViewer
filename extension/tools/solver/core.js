/* Simpl's solver, the core: exact numbers, expressions, reading what is typed, writing it as TeX, simplifying,
 * expanding, and polynomials. Plain JavaScript, no page needed (the tests run it in Node); the page (index.html) and
 * the other parts (calculus.js, algebra.js, solver.js) use what it puts on SimplMath. */
(function (root) {
  'use strict';

  class MathError extends Error {}

  // ---------------------------------------------------------------- numbers

  const ZERO = 0n, ONE = 1n;
  function bgcd(a, b) { if (a < 0n) a = -a; if (b < 0n) b = -b; while (b) { const t = a % b; a = b; b = t; } return a; }
  function babs(a) { return a < 0n ? -a : a; }

  /** An exact rational n/d (d > 0, in lowest terms). */
  function Q(n, d) {
    n = BigInt(n); d = d === undefined ? ONE : BigInt(d);
    if (d === ZERO) throw new MathError('Division by zero');
    if (d < ZERO) { n = -n; d = -d; }
    const g = bgcd(n, d) || ONE;
    return { t: 'num', n: n / g, d: d / g };
  }
  /** A decimal, where an exact value is not known. */
  function R(v) { return { t: 'real', v: v }; }
  const isNum = (a) => a.t === 'num';
  const isReal = (a) => a.t === 'real';
  const isConst = (a) => a.t === 'num' || a.t === 'real';
  const isZero = (a) => (a.t === 'num' && a.n === ZERO) || (a.t === 'real' && a.v === 0);
  const isOne = (a) => (a.t === 'num' && a.n === ONE && a.d === ONE) || (a.t === 'real' && a.v === 1);
  const isInt = (a) => a.t === 'num' && a.d === ONE;
  const numVal = (a) => a.t === 'num' ? Number(a.n) / Number(a.d) : a.v;
  const neg = (a) => a.t === 'num' ? Q(-a.n, a.d) : R(-a.v);

  function addN(a, b) { return a.t === 'num' && b.t === 'num' ? Q(a.n * b.d + b.n * a.d, a.d * b.d) : R(numVal(a) + numVal(b)); }
  function mulN(a, b) { return a.t === 'num' && b.t === 'num' ? Q(a.n * b.n, a.d * b.d) : R(numVal(a) * numVal(b)); }
  function divN(a, b) {
    if (isZero(b)) throw new MathError('Division by zero');
    return a.t === 'num' && b.t === 'num' ? Q(a.n * b.d, a.d * b.n) : R(numVal(a) / numVal(b));
  }
  function cmpN(a, b) { const x = numVal(a), y = numVal(b); return x < y ? -1 : x > y ? 1 : 0; }
  /** a^k for a whole k (kept exact while it stays a reasonable size). */
  function powInt(a, k) {
    if (a.t === 'real') return R(Math.pow(a.v, Number(k)));
    k = BigInt(k);
    if (k === ZERO) return Q(1);
    if (k < ZERO) { if (a.n === ZERO) throw new MathError('Division by zero'); return powInt(Q(a.d, a.n), -k); }
    const bits = (babs(a.n).toString(2).length + a.d.toString(2).length) * Number(k);
    if (bits > 4000) return R(Math.pow(numVal(a), Number(k)));
    return Q(a.n ** k, a.d ** k);
  }
  /** The whole r with r^k = n exactly, or null. */
  function intRoot(n, k) {
    if (n < ZERO) { if (k % 2 === 0) return null; const r = intRoot(-n, k); return r === null ? null : -r; }
    if (n < 2n) return n;
    let r = BigInt(Math.round(Math.pow(Number(n), 1 / k)));
    for (const c of [r - 1n, r, r + 1n]) if (c >= ZERO && c ** BigInt(k) === n) return c;
    return null;
  }
  /** n = outside^k · inside, outside as large as it can be (√72 = 6√2): small factors only. */
  function extractRoot(n, k) {
    let out = ONE, inside = n;
    const kk = BigInt(k);
    for (let p = 2n; p <= 2000n && p ** kk <= babs(inside); p += (p === 2n ? 1n : 2n)) {
      const pk = p ** kk;
      while (inside % pk === ZERO) { inside /= pk; out *= p; }
    }
    return { out, inside };
  }

  // ---------------------------------------------------------------- expressions

  const sym = (n) => ({ t: 'sym', n });
  const PI = sym('pi'), E = sym('e'), INF = sym('inf'), I = sym('i');
  function add(list) { return { t: 'add', a: list }; }
  function mul(list) { return { t: 'mul', a: list }; }
  function pow(b, e) { return { t: 'pow', b, e }; }
  function fn(n, a, extra) { const f = { t: 'fn', n, a }; if (extra) Object.assign(f, extra); return f; }
  function eq(l, r, op) { return { t: 'rel', op: op || '=', l, r }; }
  const sub = (a, b) => add([a, mul([Q(-1), b])]);
  const div = (a, b) => mul([a, pow(b, Q(-1))]);
  const sqrt = (a) => pow(a, Q(1, 2));

  const FNS = ['sin', 'cos', 'tan', 'sec', 'csc', 'cot', 'asin', 'acos', 'atan', 'asec', 'acsc', 'acot',
    'sinh', 'cosh', 'tanh', 'ln', 'log', 'abs', 'floor', 'ceil', 'sign'];

  function equal(a, b) { return key(a) === key(b); }
  /** A string for a node: equal nodes, equal strings. */
  function key(a) {
    switch (a.t) {
      case 'num': return a.d === ONE ? a.n.toString() : a.n + '/' + a.d;
      case 'real': return 'r' + a.v;
      case 'sym': return a.n;
      case 'add': return '(+ ' + a.a.map(key).join(' ') + ')';
      case 'mul': return '(* ' + a.a.map(key).join(' ') + ')';
      case 'pow': return '(^ ' + key(a.b) + ' ' + key(a.e) + ')';
      case 'fn': return '(' + a.n + (a.base ? '_' + key(a.base) : '') + ' ' + key(a.a) + ')';
      case 'rel': return '(' + a.op + ' ' + key(a.l) + ' ' + key(a.r) + ')';
      default: return '?';
    }
  }
  function freeOf(a, x) {
    switch (a.t) {
      case 'sym': return a.n !== x;
      case 'num': case 'real': return true;
      case 'add': case 'mul': return a.a.every((c) => freeOf(c, x));
      case 'pow': return freeOf(a.b, x) && freeOf(a.e, x);
      case 'fn': return freeOf(a.a, x) && (!a.base || freeOf(a.base, x));
      case 'rel': return freeOf(a.l, x) && freeOf(a.r, x);
    }
    return true;
  }
  /** The letters in it that are not constants, in the order of first use. */
  function variables(a, out) {
    out = out || [];
    const walk = (n) => {
      if (n.t === 'sym') { if (!['pi', 'e', 'inf', 'i'].includes(n.n) && !out.includes(n.n)) out.push(n.n); }
      else if (n.t === 'add' || n.t === 'mul') n.a.forEach(walk);
      else if (n.t === 'pow') { walk(n.b); walk(n.e); }
      else if (n.t === 'fn') { walk(n.a); if (n.base) walk(n.base); }
      else if (n.t === 'rel') { walk(n.l); walk(n.r); }
      else if (n.t === 'list') n.a.forEach(walk);
      else if (n.t === 'chain') { walk(n.a); walk(n.b); }
    };
    walk(a);
    return out;
  }
  function substitute(a, x, v) {
    switch (a.t) {
      case 'sym': return a.n === x ? v : a;
      case 'add': return add(a.a.map((c) => substitute(c, x, v)));
      case 'mul': return mul(a.a.map((c) => substitute(c, x, v)));
      case 'pow': return pow(substitute(a.b, x, v), substitute(a.e, x, v));
      case 'fn': return fn(a.n, substitute(a.a, x, v), a.base ? { base: substitute(a.base, x, v) } : null);
      case 'rel': return eq(substitute(a.l, x, v), substitute(a.r, x, v), a.op);
    }
    return a;
  }
  /** Every node equal to `what` put back as `by`. */
  function replace(a, what, by) {
    if (equal(a, what)) return by;
    switch (a.t) {
      case 'add': return add(a.a.map((c) => replace(c, what, by)));
      case 'mul': return mul(a.a.map((c) => replace(c, what, by)));
      case 'pow': return pow(replace(a.b, what, by), replace(a.e, what, by));
      case 'fn': return fn(a.n, replace(a.a, what, by), a.base ? { base: a.base } : null);
    }
    return a;
  }

  // ---------------------------------------------------------------- reading what is typed

  const WORD_FNS = {
    sin: 'sin', cos: 'cos', tan: 'tan', sec: 'sec', csc: 'csc', cot: 'cot',
    arcsin: 'asin', arccos: 'acos', arctan: 'atan', asin: 'asin', acos: 'acos', atan: 'atan',
    arcsec: 'asec', arccsc: 'acsc', arccot: 'acot', asec: 'asec', acsc: 'acsc', acot: 'acot',
    sinh: 'sinh', cosh: 'cosh', tanh: 'tanh', ln: 'ln', log: 'log', lg: 'log', abs: 'abs', sqrt: 'sqrt', cbrt: 'cbrt',
    exp: 'exp', floor: 'floor', ceil: 'ceil', sign: 'sign', sgn: 'sign', root: 'root',
  };

  /** What is typed, made tidy: the maths keys' symbols as their letters. */
  function normalise(s) {
    return String(s)
      .replace(/[−–—]/g, '-').replace(/[×·∙⋅]/g, '*').replace(/÷/g, '/').replace(/π/g, 'pi').replace(/∞/g, 'inf')
      .replace(/≤|=</g, '<=').replace(/≥|=>/g, '>=').replace(/≠/g, '!=')
      .replace(/√/g, 'sqrt').replace(/∛/g, 'cbrt')
      .replace(/²/g, '^2').replace(/³/g, '^3').replace(/⁴/g, '^4')
      .replace(/\*\*/g, '^')
      .replace(/\btheta\b|θ/g, 't');
  }

  function tokenize(s) {
    const out = [];
    let i = 0;
    while (i < s.length) {
      const c = s[i];
      if (/\s/.test(c)) { i++; continue; }
      if (/[0-9.]/.test(c)) {
        let j = i; while (j < s.length && /[0-9.]/.test(s[j])) j++;
        // (1.5e3, 2E-4)
        if ((s[j] === 'e' || s[j] === 'E') && /[0-9+-]/.test(s[j + 1] || '') && /[0-9]/.test(s[j + 1] === '+' || s[j + 1] === '-' ? s[j + 2] || '' : s[j + 1])) {
          j += 2; while (j < s.length && /[0-9]/.test(s[j])) j++;
        }
        out.push({ k: 'num', v: s.slice(i, j) }); i = j; continue;
      }
      if (/[A-Za-z]/.test(c)) {
        let j = i; while (j < s.length && /[A-Za-z]/.test(s[j])) j++;
        out.push({ k: 'word', v: s.slice(i, j) }); i = j; continue;
      }
      const two = s.slice(i, i + 2);
      if (['<=', '>=', '!='].includes(two)) { out.push({ k: 'op', v: two }); i += 2; continue; }
      if ('+-*/^()[]{},=<>!|\'_;'.includes(c)) { out.push({ k: 'op', v: c }); i++; continue; }
      throw new MathError('“' + c + '” is not understood');
    }
    return out;
  }

  /** Words split into what they mean: known functions, pi, e, inf, else single letters (xy is x·y). */
  function splitWord(w) {
    const out = [];
    let i = 0;
    const names = Object.keys(WORD_FNS).sort((a, b) => b.length - a.length);
    while (i < w.length) {
      const rest = w.slice(i).toLowerCase();
      const f = names.find((n) => rest.startsWith(n));
      if (f) { out.push({ k: 'fn', v: WORD_FNS[f] }); i += f.length; continue; }
      if (rest.startsWith('pi')) { out.push({ k: 'sym', v: 'pi' }); i += 2; continue; }
      if (rest.startsWith('inf')) { out.push({ k: 'sym', v: 'inf' }); i += rest.startsWith('infinity') ? 8 : 3; continue; }
      out.push({ k: 'sym', v: w[i] }); i++;
    }
    return out;
  }

  function parse(text) {
    const toks = [];
    for (const t of tokenize(normalise(text))) {
      if (t.k === 'word') toks.push(...splitWord(t.v)); else toks.push(t);
    }
    let p = 0;
    const peek = () => toks[p];
    const isOp = (v) => toks[p] && toks[p].k === 'op' && toks[p].v === v;
    const take = (v) => { if (!isOp(v)) throw new MathError(v === ')' ? 'A bracket is not closed' : 'Expected “' + v + '”'); p++; };
    let absDepth = 0;

    function relation() {
      const l = expr();
      const t = peek();
      if (t && t.k === 'op' && ['=', '<', '>', '<=', '>=', '!='].includes(t.v)) {
        p++;
        const r = expr();
        // (1 < x < 3)
        const t2 = peek();
        if (t2 && t2.k === 'op' && ['<', '>', '<=', '>='].includes(t2.v)) { p++; const r2 = expr(); return { t: 'chain', a: eq(l, r, t.v), b: eq(r, r2, t2.v) }; }
        return eq(l, r, t.v);
      }
      return l;
    }
    function expr() {
      let terms = [term()];
      for (;;) {
        if (isOp('+')) { p++; terms.push(term()); }
        else if (isOp('-')) { p++; terms.push(mul([Q(-1), term()])); }
        else break;
      }
      return terms.length === 1 ? terms[0] : add(terms);
    }
    function startsFactor() {
      const t = peek();
      if (!t) return false;
      if (t.k === 'num' || t.k === 'sym' || t.k === 'fn') return true;
      if (t.k === 'op' && (t.v === '(' || t.v === '[' || t.v === '{')) return true;
      if (t.k === 'op' && t.v === '|' && absDepth === 0) return true;
      return false;
    }
    function term() {
      let f = [unary()];
      for (;;) {
        if (isOp('*')) { p++; f.push(unary()); }
        else if (isOp('/')) { p++; f.push(pow(unary(), Q(-1))); }
        else if (startsFactor()) f.push(power());
        else break;
      }
      return f.length === 1 ? f[0] : mul(f);
    }
    function unary() {
      if (isOp('-')) { p++; return mul([Q(-1), unary()]); }
      if (isOp('+')) { p++; return unary(); }
      return power();
    }
    function power() {
      let b = postfix();
      if (isOp('^')) {
        p++;
        const e = isOp('-') ? (p++, mul([Q(-1), power()])) : power();
        return pow(b, e);
      }
      return b;
    }
    function postfix() {
      let a = atom();
      for (;;) {
        if (isOp('!')) { p++; a = fn('fact', a); }
        else if (isOp('\'')) { p++; a = fn('prime', a); }
        else break;
      }
      return a;
    }
    function group() {
      const open = peek().v;
      const close = open === '(' ? ')' : open === '[' ? ']' : '}';
      p++;
      const first = relation();
      if (isOp(',')) {
        const list = [first];
        while (isOp(',')) { p++; list.push(relation()); }
        take(close);
        return { t: 'list', a: list };
      }
      take(close);
      return first;
    }
    function atom() {
      const t = peek();
      if (!t) throw new MathError('Something is missing at the end');
      if (t.k === 'num') {
        p++;
        return parseNumber(t.v);
      }
      if (t.k === 'sym') { p++; return sym(t.v); }
      if (t.k === 'fn') {
        p++;
        let name = t.v;
        // (sin^2 x, sin^-1 x)
        let power = null;
        if (isOp('^')) {
          p++;
          const e = isOp('-') ? (p++, mul([Q(-1), postfix()])) : postfix();
          power = e;
        }
        // (log_2(8), log_b x)
        let base = null;
        if ((name === 'log' || name === 'root') && isOp('_')) { p++; base = isOp('(') || isOp('{') ? group() : atom(); }
        if (isOp('^') && power === null) {
          p++;
          power = isOp('-') ? (p++, mul([Q(-1), postfix()])) : postfix();
        }
        let arg;
        if (isOp('(') || isOp('[') || isOp('{')) {
          const g = group();
          if (g.t === 'list') {
            if (name === 'log' || name === 'root') { arg = g.a[0]; base = g.a[1]; } else throw new MathError(name + ' takes one value');
          } else arg = g;
        } else arg = power === null ? powerNoImplicit() : powerNoImplicit();
        let node;
        if (name === 'sqrt') node = pow(arg, Q(1, 2));
        else if (name === 'cbrt') node = pow(arg, Q(1, 3));
        else if (name === 'root') node = pow(arg, pow(base || Q(2), Q(-1)));
        else if (name === 'exp') node = pow(E, arg);
        else node = fn(name, arg, base ? { base } : null);
        if (power !== null) {
          // (sin^-1 x is arcsin x)
          if (power.t === 'mul' && power.a.length === 2 && isNum(power.a[0]) && power.a[0].n === -1n && isOne(power.a[1]) && node.t === 'fn' && ['sin', 'cos', 'tan', 'sec', 'csc', 'cot'].includes(node.n)) node = fn('a' + node.n, node.a);
          else node = pow(node, power);
        }
        return node;
      }
      if (t.k === 'op' && (t.v === '(' || t.v === '[' || t.v === '{')) return group();
      if (t.k === 'op' && t.v === '|') {
        p++; absDepth++;
        const inner = expr();
        absDepth--;
        take('|');
        return fn('abs', inner);
      }
      throw new MathError('“' + t.v + '” is out of place');
    }
    /** A function's bare argument (sin 2x, ln x): a term without a + or −, sin x cos x read as sin(x)·cos(x). */
    function powerNoImplicit() {
      const f = [power()];
      while (peek() && (peek().k === 'num' || peek().k === 'sym') && !(f.length && f[f.length - 1].t === 'sym' && peek().k === 'num')) f.push(power());
      return f.length === 1 ? f[0] : mul(f);
    }

    if (!toks.length) throw new MathError('Type something to solve');
    const out = [relation()];
    while (isOp(',') || (peek() && peek().k === 'op' && peek().v === ';')) { p++; out.push(relation()); }
    if (p < toks.length) throw new MathError('“' + toks[p].v + '” is out of place');
    return out.length === 1 ? out[0] : { t: 'list', a: out };
  }

  function parseNumber(s) {
    if (!/^(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?$/.test(s)) throw new MathError('“' + s + '” is not a number');
    const m = s.match(/^(\d*)\.?(\d*)(?:[eE]([+-]?\d+))?$/);
    const whole = m[1] || '0', frac = m[2] || '', ex = parseInt(m[3] || '0', 10);
    let n = BigInt(whole + frac), d = 10n ** BigInt(frac.length);
    if (ex > 0) n *= 10n ** BigInt(ex); else if (ex < 0) d *= 10n ** BigInt(-ex);
    return Q(n, d);
  }

  // ---------------------------------------------------------------- simplifying

  /** The order terms are written in: higher powers first, then letters, numbers last. */
  function degreeOf(a) {
    switch (a.t) {
      case 'num': case 'real': return 0;
      case 'sym': return ['pi', 'e'].includes(a.n) ? 0 : 1;
      case 'pow': return isConst(a.e) ? degreeOf(a.b) * numVal(a.e) : 1;
      case 'mul': return a.a.reduce((s, c) => s + degreeOf(c), 0);
      case 'add': return Math.max(...a.a.map(degreeOf));
      default: return 0.5;
    }
  }
  /** Each letter's power in a term (3x²y: x 2, y 1). */
  function powers(a) {
    const out = {};
    for (const f of a.t === 'mul' ? a.a : [a]) {
      const { b, e } = basePow(f);
      if (b.t === 'sym' && !['pi', 'e'].includes(b.n) && isConst(e)) out[b.n] = (out[b.n] || 0) + numVal(e);
    }
    return out;
  }
  function termOrder(a, b) {
    const da = degreeOf(a), db = degreeOf(b);
    if (da !== db) return db - da;
    // (the same degree: the earlier letter's higher power first — a³ + 3a²b + 3ab² + b³)
    const pa = powers(a), pb = powers(b);
    const letters = [...new Set([...Object.keys(pa), ...Object.keys(pb)])].sort();
    for (const l of letters) { const d = (pb[l] || 0) - (pa[l] || 0); if (d) return d < 0 ? -1 : 1; }
    const ka = key(stripCoeff(a).rest), kb = key(stripCoeff(b).rest);
    return ka < kb ? -1 : ka > kb ? 1 : 0;
  }
  function factorOrder(a, b) {
    const rank = (n) => isConst(n) ? 0 : n.t === 'pow' && isConst(n.b) && isConst(n.e) ? 0.5 : n.t === 'pow' && (isConst(n.b) || (n.b.t === 'sym' && n.b.n === 'e')) && !isConst(n.e) ? 3 : n.t === 'sym' || (n.t === 'pow' && n.b.t === 'sym') ? (['pi', 'e'].includes(n.t === 'sym' ? n.n : n.b.n) ? 1 : 2) : n.t === 'pow' && n.b.t === 'add' ? 4 : n.t === 'add' ? 4 : 3;
    const ra = rank(a), rb = rank(b);
    if (ra !== rb) return ra - rb;
    const ba = a.t === 'pow' ? a.b : a, bb = b.t === 'pow' ? b.b : b;
    const ka = key(ba), kb = key(bb);
    return ka < kb ? -1 : ka > kb ? 1 : 0;
  }
  /** A term as its number times the rest: 3x² is 3 and x². */
  function stripCoeff(a) {
    if (isConst(a)) return { c: a, rest: Q(1) };
    if (a.t === 'mul' && isConst(a.a[0])) return { c: a.a[0], rest: a.a.length === 2 ? a.a[1] : mul(a.a.slice(1)) };
    return { c: Q(1), rest: a };
  }
  /** A factor as its base and its power: x³ is x and 3. */
  function basePow(a) { return a.t === 'pow' ? { b: a.b, e: a.e } : { b: a, e: Q(1) }; }

  const SPECIAL = {
    // (sin and cos at the angles every table has: k·π/12, k·π/6, k·π/4, k·π/3)
  };

  function S(a) {
    switch (a.t) {
      case 'num': case 'real': case 'sym': return a;
      case 'add': return simplifyAdd(a.a.map(S));
      case 'mul': return simplifyMul(a.a.map(S));
      case 'pow': return simplifyPow(S(a.b), S(a.e));
      case 'fn': return simplifyFn(a.n, S(a.a), a.base ? S(a.base) : null);
      case 'rel': return eq(S(a.l), S(a.r), a.op);
      case 'list': return { t: 'list', a: a.a.map(S) };
      case 'chain': return { t: 'chain', a: S(a.a), b: S(a.b) };
    }
    return a;
  }

  function simplifyAdd(list) {
    const flat = [];
    // (a number times a sum, inside a sum, spread over it: a − (b + c) is a − b − c)
    const spread = (c) => {
      if (c.t === 'add') { c.a.forEach(spread); return; }
      if (c.t === 'mul' && c.a.length === 2 && isConst(c.a[0]) && c.a[1].t === 'add') { c.a[1].a.forEach((t) => spread(simplifyMul([c.a[0], t]))); return; }
      flat.push(c);
    };
    list.forEach(spread);
    let c0 = Q(0);
    const terms = new Map();
    for (const t of flat) {
      if (isConst(t)) { c0 = addN(c0, t); continue; }
      const { c, rest } = stripCoeff(t);
      const k = key(rest);
      const had = terms.get(k);
      if (had) had.c = addN(had.c, c); else terms.set(k, { c, rest });
    }
    const out = [];
    for (const { c, rest } of terms.values()) {
      if (isZero(c)) continue;
      out.push(isOne(c) ? rest : simplifyMul([c, rest]));
    }
    out.sort(termOrder);
    // (a number with roots, no letters: the number first — 1 − √5, 3 + 2√2; with letters, last — x² + 1)
    if (!isZero(c0)) { if (out.every((t) => variables(t).length === 0)) out.unshift(c0); else out.push(c0); }
    if (!out.length) return Q(0);
    return out.length === 1 ? out[0] : add(out);
  }

  function simplifyMul(list) {
    const flat = [];
    for (const c of list) if (c.t === 'mul') flat.push(...c.a); else flat.push(c);
    let coeff = Q(1);
    const bases = new Map();
    for (const f of flat) {
      if (isConst(f)) { coeff = mulN(coeff, f); continue; }
      const { b, e } = basePow(f);
      const k = key(b);
      const had = bases.get(k);
      if (had) had.e = simplifyAdd([had.e, e]); else bases.set(k, { b, e });
    }
    if (isZero(coeff)) return Q(0);
    let out = [];
    for (const { b, e } of bases.values()) {
      const p = simplifyPow(b, e);
      if (isConst(p)) { coeff = mulN(coeff, p); continue; }
      if (isOne(p)) continue;
      if (p.t === 'mul') { for (const q of p.a) { if (isConst(q)) coeff = mulN(coeff, q); else out.push(q); } continue; }
      out.push(p);
    }
    // (√2·√3 = √6: the same root of numbers, as one)
    const roots = new Map();
    const kept = [];
    for (const f of out) {
      if (f.t === 'pow' && isInt(f.b) && isNum(f.e) && f.e.n === ONE && f.e.d !== ONE) {
        const k = f.e.d.toString();
        if (roots.has(k)) roots.set(k, mulN(roots.get(k), f.b)); else roots.set(k, f.b);
      } else kept.push(f);
    }
    out = kept;
    for (const [k, b] of roots) {
      const p = simplifyPow(b, Q(1, BigInt(k)));
      for (const q of p.t === 'mul' ? p.a : [p]) { if (isConst(q)) coeff = mulN(coeff, q); else out.push(q); }
    }
    if (isZero(coeff)) return Q(0);
    out.sort(factorOrder);
    // (a number times a sum, a sum times a number: kept as a product, distributed only when expanded)
    if (!isOne(coeff)) out.unshift(coeff);
    if (!out.length) return coeff;
    return out.length === 1 ? out[0] : mul(out);
  }

  function simplifyPow(b, e) {
    if (isZero(e)) return Q(1);
    if (isOne(e)) return b;
    if (isConst(b) && isConst(e)) {
      if (isReal(b) || isReal(e)) { const v = Math.pow(numVal(b), numVal(e)); return isNaN(v) ? R(NaN) : R(v); }
      if (isOne(b)) return Q(1);
      if (isZero(b)) { if (numVal(e) < 0) throw new MathError('Division by zero'); return Q(0); }
      if (e.d === ONE) return powInt(b, e.n);
      // (a root: whole powers taken out — √12 = 2√3, 12^(3/2) = 24√3, ∛(−8) = −2 — and none left under a line: 1/√2 =
      //  √2/2, √(1/3) = √3/3)
      const k = Number(e.d);
      if (k > 64) return R(Math.pow(numVal(b), numVal(e)));
      if (b.n < ZERO && k % 2 === 0) return pow(b, e);
      let sign = ONE, base = b;
      if (b.n < ZERO) { base = Q(-b.n, b.d); if (babs(e.n) % 2n === ONE) sign = -ONE; }
      const bp = powInt(base, babs(e.n));
      if (bp.t === 'real') return R(Math.pow(numVal(b), numVal(e)));
      const kk = BigInt(k);
      const top = extractRoot(bp.n * bp.d ** (kk - ONE), k);
      let coef, inside;
      if (e.n > ZERO) { coef = Q(sign * top.out, bp.d); inside = top.inside; }
      else if (top.inside === ONE) { coef = Q(sign * bp.d, top.out); inside = ONE; }
      else { const again = extractRoot(top.inside ** (kk - ONE), k); coef = Q(sign * bp.d * again.out, top.out * top.inside); inside = again.inside; }
      if (inside === ONE) return coef;
      const rootNode = pow(Q(inside), Q(1, k));
      return isOne(coef) ? rootNode : mul([coef, rootNode]);
    }
    if (isOne(b)) return Q(1);
    if (isZero(b) && isConst(e) && numVal(e) > 0) return Q(0);
    // ((x^a)^b = x^(ab) when b is whole)
    if (b.t === 'pow' && isInt(e)) return simplifyPow(b.b, simplifyMul([b.e, e]));
    if (b.t === 'pow' && isNum(b.e) && isNum(e) && b.e.d === ONE && b.e.n % 2n === 1n) return simplifyPow(b.b, simplifyMul([b.e, e]));
    // ((ab)^n = aⁿbⁿ)
    if (b.t === 'mul' && isInt(e)) return simplifyMul(b.a.map((f) => simplifyPow(f, e)));
    if (b.t === 'mul' && isNum(e) && isConst(b.a[0]) && numVal(b.a[0]) > 0) return simplifyMul([simplifyPow(b.a[0], e), simplifyPow(b.a.length === 2 ? b.a[1] : mul(b.a.slice(1)), e)]);
    // (e^(ln x) = x)
    if (b.t === 'sym' && b.n === 'e' && e.t === 'fn' && e.n === 'ln') return e.a;
    // (e^(k ln x) = x^k)
    if (b.t === 'sym' && b.n === 'e' && e.t === 'mul' && e.a.length === 2 && isConst(e.a[0]) && e.a[1].t === 'fn' && e.a[1].n === 'ln') return simplifyPow(e.a[1].a, e.a[0]);
    return pow(b, e);
  }

  /** π times what: sin(π/6) = 1/2 and the others every table has. */
  function piMultiple(a) {
    if (a.t === 'sym' && a.n === 'pi') return Q(1);
    if (isZero(a)) return Q(0);
    if (a.t === 'mul' && a.a.length === 2 && isNum(a.a[0]) && a.a[1].t === 'sym' && a.a[1].n === 'pi') return a.a[0];
    return null;
  }
  function trigExact(name, k) {
    // k: the angle as a multiple of π. sin and cos at multiples of π/12 that have short exact values.
    const twelfths = k.n * 12n / k.d;
    if ((k.n * 12n) % k.d !== ZERO) return null;
    const m = Number(((twelfths % 24n) + 24n) % 24n);
    const s3 = pow(Q(3), Q(1, 2)), s2 = pow(Q(2), Q(1, 2));
    const SIN = { 0: Q(0), 2: Q(1, 2), 3: mul([Q(1, 2), s2]), 4: mul([Q(1, 2), s3]), 6: Q(1) };
    const sinOf = (m) => {
      m = ((m % 24) + 24) % 24;
      if (m > 12) { const v = sinOf(m - 12); return v === null ? null : S(mul([Q(-1), v])); }
      if (m > 6) m = 12 - m;
      return SIN[m] === undefined ? null : SIN[m];
    };
    if (name === 'sin') return sinOf(m);
    if (name === 'cos') return sinOf(m + 6);
    if (name === 'tan') {
      const s = sinOf(m), c = sinOf(m + 6);
      if (s === null || c === null) return null;
      if (isZero(c)) throw new MathError('tan is undefined there');
      return S(div(s, c));
    }
    return null;
  }

  function simplifyFn(name, a, base) {
    if (name === 'log' && base && isConst(a) && isConst(base)) {
      // (log₂8 = 3)
      if (isInt(a) && isInt(base) && a.n > 0n && base.n > 1n) {
        let v = ONE, k = 0n;
        while (v < a.n) { v *= base.n; k++; }
        if (v === a.n) return Q(k);
      }
    }
    if (isConst(a) && (!base || isConst(base))) {
      if (name === 'abs') return a.t === 'num' ? Q(babs(a.n), a.d) : R(Math.abs(a.v));
      if (name === 'ln' && isOne(a)) return Q(0);
      if (name === 'log' && isOne(a)) return Q(0);
      if (name === 'log' && !base && isInt(a) && a.n > 0n) {
        const s = a.n.toString();
        if (/^10*$/.test(s)) return Q(s.length - 1);
      }
      if (['sin', 'tan', 'asin', 'atan', 'sinh', 'tanh'].includes(name) && isZero(a)) return Q(0);
      if (['cos', 'cosh'].includes(name) && isZero(a)) return Q(1);
      if (name === 'fact' && isInt(a) && a.n >= 0n && a.n <= 170n) { let f = ONE; for (let i = 2n; i <= a.n; i++) f *= i; return Q(f); }
      if (name === 'floor' && a.t === 'num') return Q(a.n >= 0n ? a.n / a.d : -((-a.n + a.d - 1n) / a.d));
      if (name === 'ceil' && a.t === 'num') return Q(a.n >= 0n ? (a.n + a.d - 1n) / a.d : -((-a.n) / a.d));
      if (name === 'sign') return Q(numVal(a) > 0 ? 1 : numVal(a) < 0 ? -1 : 0);
      if (isReal(a)) { const v = evalN(fn(name, a, base ? { base } : null), {}); return R(v); }
    }
    if (name === 'ln' && a.t === 'sym' && a.n === 'e') return Q(1);
    if (name === 'ln' && a.t === 'pow' && a.b.t === 'sym' && a.b.n === 'e') return a.e;
    if (name === 'abs' && a.t === 'fn' && a.n === 'abs') return a;
    // (|e| = e, |1 − π| = π − 1: a constant's sign known)
    if (name === 'abs' && variables(a).length === 0) { const v = evalN(a, {}); if (isFinite(v) && v !== 0) return v > 0 ? a : simplifyMul([Q(-1), a]); }
    if (name === 'abs' && a.t === 'mul' && isConst(a.a[0]) && numVal(a.a[0]) < 0) return simplifyFn('abs', simplifyMul([neg(a.a[0]), ...a.a.slice(1)]));
    if (['sin', 'cos', 'tan'].includes(name)) {
      const k = piMultiple(a);
      if (k && isNum(k)) { const v = trigExact(name, k); if (v) return v; }
      // (sin(−x) = −sin x, cos(−x) = cos x)
      if (a.t === 'mul' && isConst(a.a[0]) && numVal(a.a[0]) < 0) {
        const pos = simplifyMul([neg(a.a[0]), ...a.a.slice(1)]);
        return name === 'cos' ? fn('cos', pos) : simplifyMul([Q(-1), fn(name, pos)]);
      }
    }
    if (name === 'asin' && isNum(a)) { const t = { '1/2': Q(1, 6), '1': Q(1, 2), '-1/2': Q(-1, 6), '-1': Q(-1, 2) }[key(a)]; if (t) return S(mul([t, PI])); }
    if (name === 'acos' && isNum(a)) { const t = { '1/2': Q(1, 3), '1': Q(0), '-1/2': Q(2, 3), '-1': Q(1), '0': Q(1, 2) }[key(a)]; if (t) return S(mul([t, PI])); }
    if (name === 'atan' && isNum(a)) { const t = { '1': Q(1, 4), '-1': Q(-1, 4) }[key(a)]; if (t) return S(mul([t, PI])); }
    if (name === 'log' && base && equal(a, base)) return Q(1);
    return fn(name, a, base ? { base } : null);
  }

  // ---------------------------------------------------------------- numbers out of expressions

  function evalN(a, env) {
    switch (a.t) {
      case 'num': return Number(a.n) / Number(a.d);
      case 'real': return a.v;
      case 'sym':
        if (a.n === 'pi') return Math.PI;
        if (a.n === 'e') return Math.E;
        if (a.n === 'inf') return Infinity;
        if (env && a.n in env) return env[a.n];
        return NaN;
      case 'add': return a.a.reduce((s, c) => s + evalN(c, env), 0);
      case 'mul': return a.a.reduce((s, c) => s * evalN(c, env), 1);
      case 'pow': {
        const b = evalN(a.b, env), e = evalN(a.e, env);
        // (a real odd root of a negative number: ∛(−8) = −2)
        if (b < 0 && a.e.t === 'num' && a.e.d % 2n === 1n && a.e.d !== ONE) return (a.e.n % 2n === 0n ? 1 : -1) * Math.pow(-b, e);
        return Math.pow(b, e);
      }
      case 'fn': {
        const v = evalN(a.a, env);
        switch (a.n) {
          case 'sin': return Math.sin(v);
          case 'cos': return Math.cos(v);
          case 'tan': return Math.abs(Math.cos(v)) < 1e-15 ? NaN : Math.tan(v);
          case 'sec': return 1 / Math.cos(v);
          case 'csc': return 1 / Math.sin(v);
          case 'cot': return Math.cos(v) / Math.sin(v);
          case 'asin': return Math.asin(v);
          case 'acos': return Math.acos(v);
          case 'atan': return Math.atan(v);
          case 'asec': return Math.acos(1 / v);
          case 'acsc': return Math.asin(1 / v);
          case 'acot': return Math.atan(1 / v);
          case 'sinh': return Math.sinh(v);
          case 'cosh': return Math.cosh(v);
          case 'tanh': return Math.tanh(v);
          case 'ln': return v <= 0 ? NaN : Math.log(v);
          case 'log': { const b = a.base ? evalN(a.base, env) : 10; return v <= 0 ? NaN : Math.log(v) / Math.log(b); }
          case 'abs': return Math.abs(v);
          case 'floor': return Math.floor(v);
          case 'ceil': return Math.ceil(v);
          case 'sign': return Math.sign(v);
          case 'fact': return gamma(v + 1);
        }
        return NaN;
      }
    }
    return NaN;
  }
  function gamma(z) {
    if (Number.isInteger(z) && z > 0 && z < 171) { let f = 1; for (let i = 2; i < z; i++) f *= i; return f; }
    if (z < 0.5) return Math.PI / (Math.sin(Math.PI * z) * gamma(1 - z));
    z -= 1;
    const g = 7, c = [0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7];
    let x = c[0];
    for (let i = 1; i < g + 2; i++) x += c[i] / (z + i);
    const t = z + g + 0.5;
    return Math.sqrt(2 * Math.PI) * Math.pow(t, z + 0.5) * Math.exp(-t) * x;
  }
  /** A function of x to draw or search: what it is at a number. */
  function compile(a, x) { return (v) => { const env = {}; env[x] = v; return evalN(a, env); }; }

  // ---------------------------------------------------------------- expanding

  /** The terms of a sum (or the one term), multiplied out by another's. */
  function distribute(terms, f) {
    const fs = f.t === 'add' ? f.a : [f];
    const next = [];
    for (const x of terms) for (const y of fs) next.push(S(mul([x, y])));
    if (next.length > 4000) throw new MathError('That is too large to expand');
    return next;
  }
  function expand(a) {
    switch (a.t) {
      case 'add': return S(add(a.a.map(expand)));
      case 'mul': {
        // (the top multiplied out, the bottom kept whole: (x² + 1)/(x + 1) stays one fraction)
        let terms = [Q(1)];
        const below = [];
        for (const f of a.a) {
          if (f.t === 'pow' && isConst(f.e) && numVal(f.e) < 0 && f.b.t !== 'sym') { below.push(S(pow(expand(f.b), f.e))); continue; }
          terms = distribute(terms, expand(f));
        }
        const top = S(add(terms));
        return below.length ? S(mul([top, ...below])) : top;
      }
      case 'pow': {
        const b = expand(a.b), e = S(a.e);
        if (b.t === 'add' && isInt(e) && e.n > 1n && e.n <= 40n) {
          let terms = [Q(1)];
          for (let i = 0n; i < e.n; i++) terms = distribute(terms, b);
          return S(add(terms));
        }
        if (b.t === 'add' && isInt(e) && e.n < -1n && e.n >= -40n) return S(pow(expand(pow(b, Q(-e.n))), Q(-1)));
        const r = S(pow(b, e));
        // ((2x)^2 = 4x², (xy)^3 = x³y³: what simplifying makes of it, multiplied out if it became a product with a sum)
        return r.t === 'mul' && r.a.some((f) => f.t === 'add') ? expand(r) : r;
      }
      case 'fn': return S(fn(a.n, expand(a.a), a.base ? { base: a.base } : null));
      case 'rel': return eq(expand(a.l), expand(a.r), a.op);
      case 'list': return { t: 'list', a: a.a.map(expand) };
    }
    return S(a);
  }

  // ---------------------------------------------------------------- polynomials

  /** Its coefficients in x, lowest power first ([c0, c1, …]) — each free of x — or null when it is not a polynomial in x. */
  function polyCoeffs(a, x) {
    const e = expand(a);
    const terms = e.t === 'add' ? e.a : [e];
    const out = [];
    for (const t of terms) {
      let k = 0;
      const rest = [];
      const fs = t.t === 'mul' ? t.a : [t];
      for (const f of fs) {
        const { b, e: p } = basePow(f);
        if (b.t === 'sym' && b.n === x) {
          if (!isInt(p) || p.n < 0n || p.n > 200n) return null;
          k += Number(p.n);
        } else {
          if (!freeOf(f, x)) return null;
          rest.push(f);
        }
      }
      const c = rest.length ? S(mul(rest)) : Q(1);
      while (out.length <= k) out.push(Q(0));
      out[k] = S(add([out[k], c]));
    }
    while (out.length > 1 && isZero(out[out.length - 1])) out.pop();
    return out.length ? out : [Q(0)];
  }
  /** The coefficients, all numbers? */
  const numericPoly = (cs) => cs && cs.every(isConst);
  function polyFrom(cs, x) {
    const X = sym(x);
    return S(add(cs.map((c, k) => mul([c, pow(X, Q(k))]))));
  }
  function polyEval(cs, v) { let r = Q(0); for (let i = cs.length - 1; i >= 0; i--) r = addN(mulN(r, v), cs[i]); return r; }
  /** Long division of numeric polynomials: quotient and remainder. */
  function polyDiv(n, d) {
    n = n.slice(); const q = [];
    const dd = d.length - 1, lead = d[dd];
    for (let i = n.length - 1 - dd; i >= 0; i--) {
      const c = divN(n[i + dd], lead);
      q[i] = c;
      for (let j = 0; j <= dd; j++) n[i + j] = addN(n[i + j], neg(mulN(c, d[j])));
    }
    for (let i = 0; i < q.length; i++) if (!q[i]) q[i] = Q(0);
    const r = n.slice(0, Math.max(1, dd));
    while (r.length > 1 && isZero(r[r.length - 1])) r.pop();
    return { q: q.length ? q : [Q(0)], r };
  }
  function polyGcd(a, b) {
    const trim = (p) => { p = p.slice(); while (p.length > 1 && isZero(p[p.length - 1])) p.pop(); return p; };
    a = trim(a); b = trim(b);
    while (!(b.length === 1 && isZero(b[0]))) { const { r } = polyDiv(a, b); a = b; b = trim(r); }
    const lead = a[a.length - 1];
    return a.map((c) => divN(c, lead));
  }

  // ---------------------------------------------------------------- writing it as TeX

  const TEXFN = { asin: '\\arcsin', acos: '\\arccos', atan: '\\arctan', asec: '\\operatorname{arcsec}', acsc: '\\operatorname{arccsc}', acot: '\\operatorname{arccot}', sign: '\\operatorname{sgn}', floor: '', ceil: '' };
  function numTex(a) {
    if (a.t === 'real') return realText(a.v);
    if (a.d === ONE) return a.n.toString();
    const n = a.n < 0n ? '-' : '';
    return n + '\\frac{' + babs(a.n) + '}{' + a.d + '}';
  }
  function realText(v) {
    if (!isFinite(v)) return isNaN(v) ? '\\text{undefined}' : (v > 0 ? '\\infty' : '-\\infty');
    if (Math.abs(v) < 1e-12) return '0';
    let s = Math.abs(v) >= 1e10 || Math.abs(v) < 1e-5 ? v.toExponential(6).replace(/\.?0+e/, 'e') : String(+v.toPrecision(10));
    if (s.includes('e')) { const [m, e] = s.split('e'); return m + '\\times10^{' + parseInt(e, 10) + '}'; }
    return s;
  }
  function isNegative(a) {
    if (isConst(a)) return numVal(a) < 0;
    if (a.t === 'mul' && isConst(a.a[0])) return numVal(a.a[0]) < 0;
    return false;
  }
  /** Top over bottom, for writing: the factors with negative powers below the line. */
  function splitFraction(a) {
    const fs = a.t === 'mul' ? a.a : [a];
    const top = [], bottom = [];
    for (const f of fs) {
      if (f.t === 'num' && f.d !== ONE) { if (f.n !== ONE) top.push(Q(f.n)); bottom.push(Q(f.d)); continue; }
      if (f.t === 'pow' && isConst(f.e) && numVal(f.e) < 0) { const e = neg(f.e); bottom.push(isOne(e) ? f.b : pow(f.b, e)); continue; }
      top.push(f);
    }
    return { top, bottom };
  }
  function tex(a, ctx) {
    ctx = ctx || 0; // (0: anywhere, 1: a factor in a product, 2: a base of a power)
    switch (a.t) {
      case 'num': case 'real': {
        const s = numTex(a);
        if (ctx >= 1 && (isNegative(a) || (ctx === 2 && a.t === 'num' && a.d !== ONE))) return '\\left(' + s + '\\right)';
        return s;
      }
      case 'sym':
        if (a.n === 'pi') return '\\pi';
        if (a.n === 'inf') return '\\infty';
        if (a.n === 't') return 't';
        return a.n;
      case 'add': {
        let s = '';
        a.a.forEach((t, i) => {
          if (i === 0) { s += tex(t, 0); return; }
          if (isNegative(t)) { const p = S(mul([Q(-1), t])); s += ' - ' + tex(p, p.t === 'add' ? 1 : 0); }
          else s += ' + ' + tex(t, 0);
        });
        return ctx >= 1 ? '\\left(' + s + '\\right)' : s;
      }
      case 'mul': {
        if (isConst(a.a[0]) && numVal(a.a[0]) < 0) {
          const rest = S(mul([neg(a.a[0]), ...a.a.slice(1)]));
          const s = '-' + tex(rest, 1);
          return ctx >= 1 ? '\\left(' + s + '\\right)' : s;
        }
        const { top, bottom } = splitFraction(a);
        if (bottom.length) {
          const t = top.length ? texProduct(top) : '1';
          const s = '\\frac{' + t + '}{' + texProduct(bottom) + '}';
          return ctx === 2 ? '\\left(' + s + '\\right)' : s;
        }
        const s = texProduct(a.a);
        return ctx === 2 ? '\\left(' + s + '\\right)' : s;
      }
      case 'pow': {
        if (isNum(a.e) && a.e.n === ONE && a.e.d === 2n) return '\\sqrt{' + tex(a.b, 0) + '}';
        if (isNum(a.e) && a.e.n === ONE && a.e.d > 2n) return '\\sqrt[' + a.e.d + ']{' + tex(a.b, 0) + '}';
        if (isConst(a.e) && numVal(a.e) < 0) {
          const e = neg(a.e);
          return '\\frac{1}{' + tex(isOne(e) ? a.b : pow(a.b, e), 0) + '}';
        }
        if (a.b.t === 'fn' && !['abs', 'floor', 'ceil', 'fact'].includes(a.b.n) && isInt(a.e) && a.e.n > 0n) {
          // (sin²x)
          const f = texFnName(a.b);
          return f + '^{' + tex(a.e) + '}' + texArg(a.b.a);
        }
        if (a.b.t === 'sym' && a.b.n === 'e') return 'e^{' + tex(a.e, 0) + '}';
        return tex(a.b, 2) + '^{' + tex(a.e, 0) + '}';
      }
      case 'fn': {
        if (a.n === 'abs') return '\\left|' + tex(a.a, 0) + '\\right|';
        if (a.n === 'floor') return '\\lfloor ' + tex(a.a, 0) + '\\rfloor';
        if (a.n === 'ceil') return '\\lceil ' + tex(a.a, 0) + '\\rceil';
        if (a.n === 'fact') return tex(a.a, 2) + '!';
        if (a.n === 'prime') return tex(a.a, 2) + "'";
        return texFnName(a) + texArg(a.a);
      }
      case 'rel': {
        const op = { '=': '=', '<': '<', '>': '>', '<=': '\\le', '>=': '\\ge', '!=': '\\ne' }[a.op];
        return tex(a.l) + ' ' + op + ' ' + tex(a.r);
      }
      case 'list': return a.a.map((c) => tex(c)).join(',\\; ');
      case 'chain': return tex(a.a.l) + ' ' + relTex(a.a.op) + ' ' + tex(a.a.r) + ' ' + relTex(a.b.op) + ' ' + tex(a.b.r);
    }
    return '?';
  }
  const relTex = (op) => ({ '=': '=', '<': '<', '>': '>', '<=': '\\le', '>=': '\\ge', '!=': '\\ne' }[op]);
  function texFnName(f) {
    if (f.n === 'log' && f.base) return '\\log_{' + tex(f.base) + '}';
    if (f.n in TEXFN) return TEXFN[f.n];
    return '\\' + f.n;
  }
  function texArg(a) {
    if (a.t === 'sym' || (isConst(a) && !isNegative(a) && !(a.t === 'num' && a.d !== ONE))) return '\\left(' + tex(a) + '\\right)';
    return '\\left(' + tex(a, 0) + '\\right)';
  }
  function texProduct(fs) {
    if (fs.length === 1) return tex(fs[0], fs[0].t === 'add' ? 0 : 1);
    let s = '';
    fs.forEach((f, i) => {
      const t = tex(f, 1);
      if (i > 0) {
        const prev = fs[i - 1];
        // (a number after anything, or anything after a root or a fraction: a dot between them)
        const needDot = isConst(f) || /^[0-9]/.test(t) || (prev.t === 'pow' && isNum(prev.e) && prev.e.d !== ONE && f.t !== 'sym' && !(f.t === 'pow' && f.b.t === 'sym'));
        s += needDot ? ' \\cdot ' : (f.t === 'fn' || (prev.t === 'fn') ? '\\,' : '');
      }
      // (\\pi then n: a space, or TeX reads \\pin)
      if (/\\[a-zA-Z]+$/.test(s) && /^[a-zA-Z]/.test(t)) s += ' ';
      s += t;
    });
    return s;
  }

  /** Written for copying: plain text a calculator would read (x^2 + 3x - 4, (x + 1)/(2x), sqrt(3)). */
  function text(a, ctx) {
    ctx = ctx || 0;
    const wrap = (s, need) => need ? '(' + s + ')' : s;
    switch (a.t) {
      case 'num': {
        const s = a.d === ONE ? a.n.toString() : a.n + '/' + a.d;
        return wrap(s, ctx >= 1 && (a.n < 0n || a.d !== ONE));
      }
      case 'real': { const s = realText(a.v).replace('\\times10^{', 'e').replace('}', '').replace('\\infty', '∞'); return wrap(s, ctx >= 1 && a.v < 0); }
      case 'sym': return a.n === 'pi' ? 'π' : a.n === 'inf' ? '∞' : a.n;
      case 'add': {
        let s = '';
        a.a.forEach((t, i) => {
          if (i === 0) s += text(t);
          else if (isNegative(t)) s += ' - ' + text(S(mul([Q(-1), t])));
          else s += ' + ' + text(t);
        });
        return wrap(s, ctx >= 1);
      }
      case 'mul': {
        if (isConst(a.a[0]) && numVal(a.a[0]) < 0) return wrap('-' + text(S(mul([neg(a.a[0]), ...a.a.slice(1)])), 1), ctx >= 1);
        const { top, bottom } = splitFraction(a);
        const prod = (fs) => fs.length === 1 ? text(fs[0], fs[0].t === 'add' ? 0 : 1) : fs.map((f, i) => { const t = text(f, 1); return i && (isConst(f) || /^[0-9]/.test(t)) ? '*' + t : t; }).join('');
        if (bottom.length) {
          const t = top.length ? prod(top) : '1';
          const b = prod(bottom);
          return wrap((top.length > 1 || (top[0] && top[0].t === 'add') ? '(' + t + ')' : t) + '/' + (bottom.length > 1 || /[^0-9a-zπ]/.test(b) ? '(' + b + ')' : b), ctx >= 1);
        }
        return wrap(prod(a.a), ctx === 2);
      }
      case 'pow': {
        if (isNum(a.e) && a.e.n === ONE && a.e.d === 2n) return 'sqrt(' + text(a.b) + ')';
        if (isNum(a.e) && a.e.n === ONE && a.e.d === 3n) return 'cbrt(' + text(a.b) + ')';
        if (isConst(a.e) && numVal(a.e) < 0) { const e = neg(a.e); return '1/' + text(isOne(e) ? a.b : pow(a.b, e), 1); }
        const e = text(a.e, 1);
        return text(a.b, 2) + '^' + (/^([0-9]+|[a-zπ])$/.test(e) ? e : '(' + text(a.e) + ')');
      }
      case 'fn': {
        if (a.n === 'abs') return '|' + text(a.a) + '|';
        if (a.n === 'fact') return text(a.a, 2) + '!';
        if (a.n === 'log' && a.base) return 'log_' + text(a.base, 2) + '(' + text(a.a) + ')';
        const name = { asin: 'arcsin', acos: 'arccos', atan: 'arctan', asec: 'arcsec', acsc: 'arccsc', acot: 'arccot' }[a.n] || a.n;
        return name + '(' + text(a.a) + ')';
      }
      case 'rel': return text(a.l) + ' ' + ({ '<=': '≤', '>=': '≥', '!=': '≠' }[a.op] || a.op) + ' ' + text(a.r);
      case 'list': return a.a.map((c) => text(c)).join(', ');
    }
    return '?';
  }

  root.SimplMath = {
    MathError, Q, R, sym, add, mul, pow, fn, eq, sub, div, sqrt, PI, E, INF, I,
    isNum, isReal, isConst, isZero, isOne, isInt, numVal, neg, addN, mulN, divN, cmpN, powInt, intRoot, bgcd, babs,
    equal, key, freeOf, variables, substitute, replace, parse, normalise, S, expand, evalN, compile, gamma,
    polyCoeffs, numericPoly, polyFrom, polyEval, polyDiv, polyGcd, stripCoeff, basePow, degreeOf,
    tex, text, realText, splitFraction, isNegative, FNS, piMultiple,
  };
})(typeof self !== 'undefined' ? self : globalThis);
