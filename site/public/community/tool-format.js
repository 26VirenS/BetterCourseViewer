// Simpl Community tools, format 1 — the reference implementation (docs/community-tools.md is the spec; every app's
// own copy — the Mac's, the Windows app's — must give the same answers: docs/community-tools-vectors.json).
//
// A community tool is data, never code: its inputs (numbers, choices, switches), its results (formulas in the small
// language below, over the inputs and the results before them), an optional table and a note. Each app draws it with
// its own controls; nothing in a tool can reach the network, the files, Canvas or another tool.
//
// One file, two places: the site's Worker imports it (site/src/community.js, bundled by wrangler), and the website's
// pages load it as it is, an ES module at /community/tool-format.js (the gallery, the maker, the review). It must keep
// to what both have: no imports, no Node, no DOM.

export const FORMAT = 1;
export const GROUPS = ['study', 'math', 'science', 'money', 'everyday'];
export const SYMBOLS = ['bolt', 'percent', 'function', 'sum', 'divide', 'ruler', 'scale', 'thermometer', 'clock', 'calendar', 'money', 'chart', 'atom', 'flask', 'leaf', 'heart', 'book', 'pencil', 'globe', 'car', 'drop', 'flame', 'gauge', 'star', 'lightbulb', 'music', 'dice', 'triangle'];
/** Each function's arity: [fewest, most]. */
export const FUNCTIONS = {
  abs: [1, 1], sqrt: [1, 1], cbrt: [1, 1], exp: [1, 1], ln: [1, 1], log: [1, 1], log2: [1, 1],
  sin: [1, 1], cos: [1, 1], tan: [1, 1], asin: [1, 1], acos: [1, 1], atan: [1, 1], atan2: [2, 2],
  floor: [1, 1], ceil: [1, 1], round: [1, 2], trunc: [1, 1], sign: [1, 1], min: [1, 16], max: [1, 16],
  pow: [2, 2], hypot: [2, 2], rad: [1, 1], deg: [1, 1], clamp: [3, 3], fact: [1, 1],
};
export const CONSTANTS = { pi: Math.PI, e: Math.E, true: 1, false: 0 };
export const LIMITS = {
  json: 32768, name: 40, maker: 30, summary: 60, label: 32, unit: 12, note: 400, inputs: 16, outputs: 12, options: 12,
  expr: 300, args: 16, tableCols: 6, tableRows: 100, colName: 24, cell: 48, tableTitle: 40, decimals: 10,
};
/** 10^0 … 10^10, exact: every app uses this table rather than pow(10, n). */
export const POW10 = [1, 10, 100, 1000, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10];
const ID = /^[a-z][a-z0-9_]{0,15}$/;
const COLOR = /^#[0-9a-f]{6}$/;

// ---- the language: tokens -----------------------------------------------------------------------------------------
const NUM = /^(?:\d+(?:\.\d+)?|\.\d+)(?:[eE][+-]?\d+)?/;
const NAME = /^[a-z][a-z0-9_]*/;
const OPS = ['<=', '>=', '==', '!=', '&&', '||', '+', '-', '*', '/', '%', '^', '(', ')', ',', '?', ':', '<', '>', '!'];

class SyntaxErr extends Error {}

export function tokenize(src) {
  const out = [];
  let i = 0;
  while (i < src.length) {
    const c = src[i];
    if (c === ' ' || c === '\t') { i++; continue; }
    const rest = src.slice(i);
    let m = NUM.exec(rest);
    if (m) { out.push({ t: 'num', v: Number(m[0]), at: i }); i += m[0].length; continue; }
    m = NAME.exec(rest);
    if (m) { out.push({ t: 'name', v: m[0], at: i }); i += m[0].length; continue; }
    const op = OPS.find((o) => rest.startsWith(o));
    if (op) { out.push({ t: 'op', v: op, at: i }); i += op.length; continue; }
    throw new SyntaxErr(`“${c}” can’t be used in a formula`);
  }
  out.push({ t: 'end', at: src.length });
  return out;
}

// ---- the language: grammar (lowest to highest) ---------------------------------------------------------------------
//   cond  := or ('?' cond ':' cond)?
//   or    := and ('||' and)*            and := eq ('&&' eq)*
//   eq    := rel (('=='|'!=') rel)*     rel := add (('<'|'<='|'>'|'>=') add)*
//   add   := mul (('+'|'-') mul)*       mul := unary (('*'|'/'|'%') unary)*
//   unary := ('-'|'+'|'!') unary | pow  pow := primary ('^' unary)?
//   primary := number | name | name '(' cond (',' cond)* ')' | '(' cond ')'
export function parse(src) {
  if (typeof src !== 'string' || !src.trim()) throw new SyntaxErr('The formula is empty');
  if (src.length > LIMITS.expr) throw new SyntaxErr(`A formula can be ${LIMITS.expr} characters at most`);
  const toks = tokenize(src);
  let p = 0;
  const peek = () => toks[p];
  const isOp = (...ops) => peek().t === 'op' && ops.includes(peek().v);
  const take = () => toks[p++];
  const expect = (op) => { if (!isOp(op)) throw new SyntaxErr(`Expected “${op}”${peek().t === 'end' ? ' at the end' : ''}`); take(); };
  const binary = (next, ops) => () => {
    let a = next();
    while (isOp(...ops)) { const op = take().v; a = { t: 'bin', op, a, b: next() }; }
    return a;
  };
  const primary = () => {
    const tk = peek();
    if (tk.t === 'num') { take(); return { t: 'num', v: tk.v }; }
    if (tk.t === 'name') {
      take();
      if (isOp('(')) {
        take();
        const args = [];
        if (!isOp(')')) { args.push(cond()); while (isOp(',')) { take(); args.push(cond()); } }
        expect(')');
        return { t: 'call', f: tk.v, args };
      }
      return { t: 'name', n: tk.v };
    }
    if (isOp('(')) { take(); const e = cond(); expect(')'); return e; }
    throw new SyntaxErr(tk.t === 'end' ? 'The formula stops too soon' : `“${tk.v}” is out of place`);
  };
  const pow = () => { const a = primary(); if (isOp('^')) { take(); return { t: 'bin', op: '^', a, b: unary() }; } return a; };
  const unary = () => { if (isOp('-', '+', '!')) { const op = take().v; return { t: 'un', op, a: unary() }; } return pow(); };
  const mul = binary(unary, ['*', '/', '%']);
  const add = binary(mul, ['+', '-']);
  const rel = binary(add, ['<', '<=', '>', '>=']);
  const eq = binary(rel, ['==', '!=']);
  const and = binary(eq, ['&&']);
  const or = binary(and, ['||']);
  function cond() {
    const c = or();
    if (isOp('?')) { take(); const a = cond(); expect(':'); const b = cond(); return { t: 'cond', c, a, b }; }
    return c;
  }
  const tree = cond();
  if (peek().t !== 'end') throw new SyntaxErr(`“${peek().v}” is out of place`);
  return tree;
}

// (a function is one of FUNCTIONS' own: never a name every object has, such as “constructor”)
const own = (obj, key) => Object.prototype.hasOwnProperty.call(obj, key);

/** Every name a formula reads, and a check of every function it calls (its name and how many it is given). */
function namesIn(node, out = new Set()) {
  switch (node.t) {
    case 'name': out.add(node.n); break;
    case 'un': namesIn(node.a, out); break;
    case 'bin': namesIn(node.a, out); namesIn(node.b, out); break;
    case 'cond': namesIn(node.c, out); namesIn(node.a, out); namesIn(node.b, out); break;
    case 'call': {
      const ar = own(FUNCTIONS, node.f) ? FUNCTIONS[node.f] : null;
      if (!ar) throw new SyntaxErr(`There is no function “${node.f}”`);
      if (node.args.length < ar[0] || node.args.length > ar[1]) throw new SyntaxErr(`${node.f}() takes ${ar[0] === ar[1] ? ar[0] : `${ar[0]} to ${ar[1]}`} value${ar[1] === 1 ? '' : 's'}`);
      node.args.forEach((a) => namesIn(a, out));
      break;
    }
    default: break;
  }
  return out;
}

/**
 * Why a formula cannot be used, or null: `{ code, message }` — it does not parse (SC-T-FORMULA), or it reads a name
 * that is not among `names` nor pi, e, true, false (SC-T-NAME-USE; one among `later` is "used before it is worked
 * out"). validateTool's own check of every formula; the maker shows it as it is typed.
 */
export function formulaProblem(src, names = [], later = []) {
  try {
    const tree = parse(src);
    const known = new Set([...names, ...Object.keys(CONSTANTS)]);
    const soon = new Set(later);
    for (const n of namesIn(tree)) {
      if (!known.has(n)) return { code: 'SC-T-NAME-USE', message: soon.has(n) ? `“${n}” is used before it is worked out.` : `There is nothing called “${n}”.` };
    }
    return null;
  } catch (e) {
    if (e instanceof SyntaxErr) return { code: 'SC-T-FORMULA', message: `${e.message}.` };
    throw e;
  }
}

// ---- the language: values ------------------------------------------------------------------------------------------
const truthy = (x) => x !== 0 && !Number.isNaN(x);
const anyNaN = (xs) => xs.some((x) => Number.isNaN(x));

/** x^y as every app works it out (JavaScript's Math.pow, made explicit where C's pow differs). */
export function power(x, y) {
  if (Number.isNaN(y)) return NaN;
  if (y === 0) return 1;
  if (Number.isNaN(x)) return NaN;
  if (Math.abs(x) === 1 && !Number.isFinite(y)) return NaN;
  return Math.pow(x, y);
}

/** x rounded to n decimals, halves away from zero: sign(x) · floor(|x| · 10ⁿ + 0.5) / 10ⁿ. */
export function roundTo(x, n = 0) {
  if (!Number.isFinite(x) || Number.isNaN(n)) return Number.isNaN(n) ? NaN : x;
  const d = Math.min(LIMITS.decimals, Math.max(0, Math.trunc(n)));
  const f = POW10[d];
  const a = Math.abs(x) * f;
  if (a >= 4503599627370496) return x; // (2^52: already whole at this many decimals)
  const r = Math.floor(a + 0.5) / f;
  return x < 0 ? -r : r;
}

function factorial(n) {
  if (!Number.isFinite(n) || n < 0 || n > 170 || Math.trunc(n) !== n) return NaN;
  let r = 1;
  for (let i = 2; i <= n; i++) r *= i;
  return r;
}

const FN = {
  abs: (x) => Math.abs(x), sqrt: (x) => Math.sqrt(x), cbrt: (x) => Math.cbrt(x), exp: (x) => Math.exp(x),
  ln: (x) => Math.log(x), log: (x) => Math.log10(x), log2: (x) => Math.log2(x),
  sin: (x) => Math.sin(x), cos: (x) => Math.cos(x), tan: (x) => Math.tan(x),
  asin: (x) => Math.asin(x), acos: (x) => Math.acos(x), atan: (x) => Math.atan(x), atan2: (y, x) => Math.atan2(y, x),
  floor: (x) => Math.floor(x), ceil: (x) => Math.ceil(x), trunc: (x) => Math.trunc(x),
  round: (x, n = 0) => roundTo(x, n),
  sign: (x) => (x > 0 ? 1 : x < 0 ? -1 : x), // (0, −0 and NaN as they are)
  min: (...xs) => (anyNaN(xs) ? NaN : xs.reduce((a, b) => (b < a ? b : a))),
  max: (...xs) => (anyNaN(xs) ? NaN : xs.reduce((a, b) => (b > a ? b : a))),
  pow: (x, y) => power(x, y), hypot: (x, y) => Math.hypot(x, y),
  rad: (d) => (d * Math.PI) / 180, deg: (r) => (r * 180) / Math.PI,
  clamp: (x, lo, hi) => (anyNaN([x, lo, hi]) ? NaN : Math.min(Math.max(x, lo), hi)),
  fact: (n) => factorial(n),
};

export function evaluate(node, env) {
  switch (node.t) {
    case 'num': return node.v;
    case 'name': return own(env, node.n) ? env[node.n] : NaN;
    case 'un': {
      const a = evaluate(node.a, env);
      return node.op === '-' ? -a : node.op === '+' ? a : truthy(a) ? 0 : 1;
    }
    case 'cond': return truthy(evaluate(node.c, env)) ? evaluate(node.a, env) : evaluate(node.b, env);
    case 'call': return own(FN, node.f) ? FN[node.f](...node.args.map((a) => evaluate(a, env))) : NaN;
    case 'bin': {
      const a = evaluate(node.a, env);
      const b = evaluate(node.b, env);
      switch (node.op) {
        case '+': return a + b;
        case '-': return a - b;
        case '*': return a * b;
        case '/': return a / b;
        case '%': return a % b; // (the sign of the dividend, as C's fmod)
        case '^': return power(a, b);
        case '<': return a < b ? 1 : 0;
        case '<=': return a <= b ? 1 : 0;
        case '>': return a > b ? 1 : 0;
        case '>=': return a >= b ? 1 : 0;
        case '==': return a === b ? 1 : 0;
        case '!=': return a !== b ? 1 : 0;
        case '&&': return truthy(a) && truthy(b) ? 1 : 0;
        case '||': return truthy(a) || truthy(b) ? 1 : 0;
        default: return NaN;
      }
    }
    default: return NaN;
  }
}

// ---- numbers as a tool shows them ----------------------------------------------------------------------------------
const SUP = { 0: '⁰', 1: '¹', 2: '²', 3: '³', 4: '⁴', 5: '⁵', 6: '⁶', 7: '⁷', 8: '⁸', 9: '⁹', '-': '⁻' };
const trimZeros = (s) => (s.includes('.') ? s.replace(/0+$/, '').replace(/\.$/, '') : s);
const group = (int) => (int.length >= 5 ? int.replace(/\B(?=(\d{3})+(?!\d))/g, ',') : int);

/**
 * A result as text: "—" when it is not a number; from 1e12 up, or under 1e-4 and rounding to 0 at `decimals`, as
 * "m × 10ⁿ" (m to 3 decimals); otherwise rounded to `decimals` (default 4) halves away from zero, trailing zeros
 * dropped, the whole part grouped by threes with commas from 10,000.
 */
export function formatNumber(v, decimals = 4) {
  if (typeof v !== 'number' || !Number.isFinite(v)) return '—';
  const d = Math.min(LIMITS.decimals, Math.max(0, Math.trunc(decimals ?? 4)));
  if (v === 0) return '0';
  const a = Math.abs(v);
  const r = roundTo(v, d);
  if (a >= 1e12 || (r === 0 && a < 1e-4)) {
    let ex = Math.floor(Math.log10(a));
    let m = v / Math.pow(10, ex);
    if (Math.abs(m) >= 10) { m /= 10; ex += 1; }
    if (Math.abs(m) < 1) { m *= 10; ex -= 1; }
    m = roundTo(m, 3);
    if (Math.abs(m) >= 10) { m = roundTo(m / 10, 3); ex += 1; }
    return `${trimZeros(m.toFixed(3))} × 10${String(ex).split('').map((c) => SUP[c]).join('')}`;
  }
  if (r === 0) return '0';
  const s = trimZeros(Math.abs(r).toFixed(d));
  const [int, frac] = s.split('.');
  return `${r < 0 ? '-' : ''}${group(int)}${frac ? `.${frac}` : ''}`;
}

// ---- a tool: checked, then worked out ------------------------------------------------------------------------------
const text = (v) => (typeof v === 'string' ? v : '');
// (text is kept well-formed: a lone half of a surrogate pair — JSON's \u escapes can carry one in — becomes U+FFFD,
// and a cut at a limit never falls between a pair's two halves: a character that would straddle it is left out)
const wellFormed = (s) => s.replace(/[\ud800-\udbff][\udc00-\udfff]|[\ud800-\udfff]/g, (m) => (m.length === 2 ? m : '\ufffd'));
const clean = (v) => wellFormed(text(v)).replace(/[\u0000-\u001f\u007f]/g, ' ').replace(/\s+/g, ' ').trim();
const cut = (s, n) => (s.length > n && /[\ud800-\udbff]/.test(s[n - 1]) ? s.slice(0, n - 1) : s.slice(0, n));
const fail = (code, message, path) => ({ error: { code, message, path } });
const isNum = (v) => typeof v === 'number' && Number.isFinite(v);
const RESERVED = new Set([...Object.keys(CONSTANTS), ...Object.keys(FUNCTIONS)]);
const TOP_KEYS = new Set(['format', 'id', 'version', 'name', 'maker', 'summary', 'group', 'symbol', 'color', 'inputs', 'outputs', 'table', 'note', 'created']);
const INPUT_KEYS = new Set(['id', 'kind', 'label', 'unit', 'default', 'min', 'max', 'step', 'options']);
const OUTPUT_KEYS = new Set(['id', 'label', 'value', 'unit', 'decimals', 'when']);

/**
 * A tool checked against format 1: `{ tool }` (cleaned: text trimmed, defaults filled in) or `{ error: { code,
 * message, path } }`. `strict` (the site, on a tool sent in) refuses keys format 1 does not have; the apps read
 * leniently and pass over them, so a later format's extra keys do not stop an older app.
 */
export function validateTool(raw, { strict = false } = {}) {
  let t = raw;
  if (typeof raw === 'string') {
    if (raw.length > LIMITS.json) return fail('SC-T-BIG', 'That tool is too big.', '');
    try { t = JSON.parse(raw); } catch { return fail('SC-T-JSON', 'That tool could not be read.', ''); }
  } else if (JSON.stringify(raw ?? null).length > LIMITS.json) return fail('SC-T-BIG', 'That tool is too big.', '');
  if (!t || typeof t !== 'object' || Array.isArray(t)) return fail('SC-T-JSON', 'That tool could not be read.', '');
  if (t.format !== FORMAT) return fail('SC-T-FORMAT', t.format > FORMAT ? 'This tool needs a newer Simpl.' : 'That is not a Simpl tool.', 'format');
  if (strict) for (const k of Object.keys(t)) if (!TOP_KEYS.has(k)) return fail('SC-T-KEY', `“${k}” is not part of a tool.`, k);

  const name = cut(clean(t.name), LIMITS.name);
  if (!name) return fail('SC-T-NAME', 'Give the tool a name.', 'name');
  const summary = cut(clean(t.summary), LIMITS.summary);
  if (!summary) return fail('SC-T-SUMMARY', 'Say in a line what the tool does.', 'summary');
  const group = GROUPS.includes(t.group) ? t.group : null;
  if (!group) return fail('SC-T-GROUP', `The group must be one of: ${GROUPS.join(', ')}.`, 'group');
  const symbol = SYMBOLS.includes(t.symbol) ? t.symbol : null;
  if (!symbol) return fail('SC-T-SYMBOL', 'That symbol is not one of Simpl’s.', 'symbol');
  const color = typeof t.color === 'string' ? t.color.toLowerCase() : '';
  if (!COLOR.test(color)) return fail('SC-T-COLOUR', 'The colour must be like #3478f6.', 'color');

  const seen = new Set();
  const takeId = (id, path, optional) => {
    if (id == null && optional) return { ok: null };
    if (typeof id !== 'string' || !ID.test(id)) return { err: fail('SC-T-ID', 'A name in formulas is a lower-case letter, then letters, digits or _ (16 at most).', path) };
    if (RESERVED.has(id)) return { err: fail('SC-T-ID', `“${id}” is a name Simpl keeps for itself.`, path) };
    if (seen.has(id)) return { err: fail('SC-T-ID', `“${id}” is used twice.`, path) };
    seen.add(id);
    return { ok: id };
  };

  if (!Array.isArray(t.inputs) || t.inputs.length < 1 || t.inputs.length > LIMITS.inputs) return fail('SC-T-INPUTS', `A tool has 1 to ${LIMITS.inputs} inputs.`, 'inputs');
  const inputs = [];
  for (const [i, raw] of t.inputs.entries()) {
    const path = `inputs.${i}`;
    if (!raw || typeof raw !== 'object') return fail('SC-T-INPUT', 'That input could not be read.', path);
    if (strict) for (const k of Object.keys(raw)) if (!INPUT_KEYS.has(k)) return fail('SC-T-KEY', `“${k}” is not part of an input.`, `${path}.${k}`);
    const id = takeId(raw.id, `${path}.id`);
    if (id.err) return id.err;
    const label = cut(clean(raw.label), LIMITS.label);
    if (!label) return fail('SC-T-LABEL', 'Every input needs a label.', `${path}.label`);
    if (raw.kind === 'number') {
      for (const k of ['default', 'min', 'max', 'step']) if (raw[k] != null && !isNum(raw[k])) return fail('SC-T-NUMBER', `The input’s ${k} must be a number.`, `${path}.${k}`);
      if (isNum(raw.min) && isNum(raw.max) && raw.min > raw.max) return fail('SC-T-RANGE', 'The input’s min is above its max.', path);
      if (raw.step != null && raw.step <= 0) return fail('SC-T-NUMBER', 'The input’s step must be above 0.', `${path}.step`);
      const lo = isNum(raw.min) ? raw.min : -Infinity;
      const hi = isNum(raw.max) ? raw.max : Infinity;
      const def = Math.min(hi, Math.max(lo, isNum(raw.default) ? raw.default : 0));
      inputs.push({ id: id.ok, kind: 'number', label, unit: cut(clean(raw.unit), LIMITS.unit), default: def, ...(isNum(raw.min) ? { min: raw.min } : {}), ...(isNum(raw.max) ? { max: raw.max } : {}), step: isNum(raw.step) ? raw.step : 1 });
    } else if (raw.kind === 'choice') {
      if (!Array.isArray(raw.options) || raw.options.length < 2 || raw.options.length > LIMITS.options) return fail('SC-T-OPTIONS', `A choice has 2 to ${LIMITS.options} options.`, `${path}.options`);
      const options = [];
      for (const [j, o] of raw.options.entries()) {
        const ol = cut(clean(o?.label), LIMITS.label);
        if (!ol || !isNum(o?.value)) return fail('SC-T-OPTIONS', 'Every option needs a label and a number for its value.', `${path}.options.${j}`);
        if (options.some((x) => x.value === o.value)) return fail('SC-T-OPTIONS', 'Two options have the same value.', `${path}.options.${j}`);
        options.push({ label: ol, value: o.value });
      }
      const def = options.some((o) => o.value === raw.default) ? raw.default : options[0].value;
      inputs.push({ id: id.ok, kind: 'choice', label, options, default: def });
    } else if (raw.kind === 'toggle') {
      inputs.push({ id: id.ok, kind: 'toggle', label, default: raw.default === true });
    } else return fail('SC-T-KIND', 'An input is a number, a choice or a toggle.', `${path}.kind`);
  }

  if (!Array.isArray(t.outputs) || t.outputs.length < 1 || t.outputs.length > LIMITS.outputs) return fail('SC-T-OUTPUTS', `A tool has 1 to ${LIMITS.outputs} results.`, 'outputs');
  const known = new Set([...inputs.map((x) => x.id), ...Object.keys(CONSTANTS)]);
  const laterIds = new Set(t.outputs.map((o) => o?.id).filter((x) => typeof x === 'string'));
  const outputs = [];
  for (const [i, raw] of t.outputs.entries()) {
    const path = `outputs.${i}`;
    if (!raw || typeof raw !== 'object') return fail('SC-T-OUTPUT', 'That result could not be read.', path);
    if (strict) for (const k of Object.keys(raw)) if (!OUTPUT_KEYS.has(k)) return fail('SC-T-KEY', `“${k}” is not part of a result.`, `${path}.${k}`);
    const label = cut(clean(raw.label), LIMITS.label);
    if (!label) return fail('SC-T-LABEL', 'Every result needs a label.', `${path}.label`);
    if (raw.decimals != null && !(Number.isInteger(raw.decimals) && raw.decimals >= 0 && raw.decimals <= LIMITS.decimals)) return fail('SC-T-DECIMALS', `Decimals are a whole number from 0 to ${LIMITS.decimals}.`, `${path}.decimals`);
    // the value reads the inputs and the results before this one; `when` may read this one's value too
    const formula = (k) => {
      const src = typeof raw[k] === 'string' ? raw[k] : '';
      const p = formulaProblem(src, known, laterIds);
      return p ? { err: fail(p.code, p.message, `${path}.${k}`) } : { ok: src.trim() };
    };
    const value = formula('value');
    if (value.err) return value.err;
    const id = takeId(raw.id, `${path}.id`, true);
    if (id.err) return id.err;
    if (id.ok) known.add(id.ok);
    const when = raw.when == null || raw.when === '' ? { ok: null } : formula('when');
    if (when.err) return when.err;
    outputs.push({ ...(id.ok ? { id: id.ok } : {}), label, value: value.ok, unit: cut(clean(raw.unit), LIMITS.unit), decimals: raw.decimals ?? 4, ...(when.ok ? { when: when.ok } : {}) });
  }

  let table = null;
  if (t.table != null) {
    const tb = t.table;
    if (typeof tb !== 'object' || !Array.isArray(tb.columns) || !Array.isArray(tb.rows)) return fail('SC-T-TABLE', 'That table could not be read.', 'table');
    const columns = tb.columns.map((c) => cut(clean(c), LIMITS.colName));
    if (columns.length < 1 || columns.length > LIMITS.tableCols || columns.some((c) => !c)) return fail('SC-T-TABLE', `A table has 1 to ${LIMITS.tableCols} named columns.`, 'table.columns');
    if (tb.rows.length < 1 || tb.rows.length > LIMITS.tableRows) return fail('SC-T-TABLE', `A table has 1 to ${LIMITS.tableRows} rows.`, 'table.rows');
    const rows = [];
    for (const [i, r] of tb.rows.entries()) {
      if (!Array.isArray(r) || r.length !== columns.length) return fail('SC-T-TABLE', 'Every row has one cell per column.', `table.rows.${i}`);
      rows.push(r.map((c) => cut(clean(typeof c === 'number' ? String(c) : c), LIMITS.cell)));
    }
    table = { title: cut(clean(tb.title), LIMITS.tableTitle), columns, rows };
  }

  const tool = {
    format: FORMAT,
    ...(typeof t.id === 'string' && /^[a-z0-9-]{3,24}$/.test(t.id) ? { id: t.id } : {}),
    ...(Number.isInteger(t.version) && t.version > 0 ? { version: t.version } : {}),
    name, maker: cut(clean(t.maker), LIMITS.maker), summary, group, symbol, color, inputs, outputs,
    ...(table ? { table } : {}),
    ...(clean(t.note) ? { note: cut(clean(t.note), LIMITS.note) } : {}),
    ...(typeof t.created === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(t.created) ? { created: t.created } : {}),
  };
  return { tool };
}

/** What an input is worth in a formula: a number clamped to its range (not a number: its default), a choice's value
 *  (one not offered: its default), a toggle's 1 or 0. */
export function inputValue(input, given) {
  if (input.kind === 'toggle') return (given === undefined ? input.default : given === true || given === 1) ? 1 : 0;
  if (input.kind === 'choice') return input.options.some((o) => o.value === given) ? given : input.default;
  const v = isNum(given) ? given : input.default;
  return Math.min(input.max ?? Infinity, Math.max(input.min ?? -Infinity, v));
}

/** A checked tool's results for these inputs (by id): each its label, its text, its unit and whether it is shown. */
export function runTool(tool, given = {}) {
  const env = { ...CONSTANTS };
  for (const input of tool.inputs) env[input.id] = inputValue(input, given[input.id]);
  return tool.outputs.map((o) => {
    const value = evaluate(parse(o.value), env);
    if (o.id) env[o.id] = value;
    const shown = o.when ? truthy(evaluate(parse(o.when), env)) : true;
    return { label: o.label, value, text: formatNumber(value, o.decimals), unit: o.unit, shown };
  });
}
