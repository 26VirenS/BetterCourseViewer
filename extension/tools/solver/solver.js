/* Simpl's solver, what the page asks: what was typed (a number, an expression, an equation, an inequality, a system),
 * the ways it can be worked (Mathway's and Symbolab's "solving options"), and each worked, its answer and steps. */
(function (root) {
  'use strict';
  const M = root.SimplMath;
  const { Q, R, sym, add, mul, pow, fn, eq, S, expand, tex, MathError, isConst, isZero, numVal, evalN } = M;
  const step = (text, t) => ({ text, tex: t });

  /** The words a question can start with, and the option each one asks for. */
  const COMMANDS = [
    [/^(solve|find)\s+(for\s+[a-z]\s*:?\s*)?/i, 'solve'],
    [/^factor(ise|ize)?\s+/i, 'factor'],
    [/^expand\s+/i, 'expand'],
    [/^simplify\s+/i, 'simplify'],
    [/^(evaluate|calculate|compute)\s+/i, 'evaluate'],
    [/^(differentiate|derivative\s+of|derive|find\s+the\s+derivative\s+of)\s+/i, 'derivative'],
    [/^d\/d([a-z])\s*/i, 'derivative'],
    [/^(integrate|integral\s+of|antiderivative\s+of|∫)\s*/i, 'integral'],
    [/^(lim(it)?)\s*/i, 'limit'],
    [/^graph\s+/i, 'graph'],
  ];

  /** What was typed, read: the command word (if any) taken off, a limit's or an integral's own words read. */
  function read(text) {
    let s = String(text || '').trim();
    let command = null, params = {};
    for (const [re, c] of COMMANDS) {
      const m = s.match(re);
      if (m) {
        command = c;
        if (c === 'derivative' && /^d\/d([a-z])/i.test(m[0])) params.variable = m[0].match(/^d\/d([a-z])/i)[1];
        if (c === 'solve' && m[2]) params.variable = m[2].match(/for\s+([a-z])/i)[1];
        s = s.slice(m[0].length).trim();
        break;
      }
    }
    if (command === 'limit') {
      // ("lim x->0 sin(x)/x", "limit as x approaches 0 of …", "lim_(x→∞) …")
      const m = s.match(/^[_(]*\s*(?:as\s+)?([a-z])\s*(?:->|→|approaches|to)\s*([^\s)]+?)\s*(?:\)|of\b)?\s+(.*)$/i);
      if (m) { params.variable = m[1]; params.to = m[2].replace(/\+$|-$/, ''); params.side = /\+$/.test(m[2]) ? 1 : /-$/.test(m[2]) && m[2].length > 1 && !/^-/.test(m[2]) ? -1 : 0; s = m[3]; }
    }
    if (command === 'integral' || /∫/.test(s)) {
      if (!command) command = 'integral';
      s = s.replace(/^∫\s*/, '');
      // ("from 0 to 1", "_0^1")
      const m = s.match(/^_\{?([^}\s^]+)\}?\^\{?([^}\s]+)\}?\s*(.*)$/) || null;
      if (m) { params.lo = m[1]; params.hi = m[2]; s = m[3]; }
      const f = s.match(/^(.*?)\s+from\s+(\S+)\s+to\s+(\S+)\s*$/i);
      if (f) { s = f[1]; params.lo = f[2]; params.hi = f[3]; }
      const dx = s.match(/^(.*?)\s*d([a-z])\s*$/);
      if (dx && dx[1].trim()) { s = dx[1]; params.variable = dx[2]; }
    }
    return { body: s, command, params };
  }

  /** Its kind, its letters, the ways it can be worked, and how it reads (TeX), for the page to show as it is typed. */
  function analyze(text) {
    const { body, command, params } = read(text);
    const node = M.parse(body);
    let kind;
    if (node.t === 'list') kind = node.a.every((r) => r.t === 'rel' && r.op === '=') ? 'system' : 'list';
    else if (node.t === 'rel') kind = node.op === '=' ? 'equation' : 'inequality';
    else if (node.t === 'chain') kind = 'inequality';
    else kind = M.variables(node).length ? 'expression' : 'number';
    const vars = M.variables(node);
    const main = pickVar(vars, params.variable);
    const options = [];
    if (kind === 'number') options.push(['evaluate', 'Evaluate']);
    if (kind === 'equation') { for (const v of (vars.length ? vars : ['x']).slice(0, 3)) options.push(['solve:' + v, 'Solve for ' + v]); options.push(['graph', 'Graph']); }
    if (kind === 'inequality') options.push(['solve:' + main, 'Solve for ' + main], ['graph', 'Graph']);
    if (kind === 'system') options.push(['system', 'Solve the system']);
    if (kind === 'expression') {
      options.push(['simplify', 'Simplify'], ['factor', 'Factor'], ['expand', 'Expand'], ['derivative', 'Derivative'], ['integral', 'Integral'], ['limit', 'Limit'], ['graph', 'Graph']);
      if (vars.length) options.push(['evaluate', 'Evaluate at ' + main + ' = …']);
    }
    let chosen = null;
    if (command === 'solve') chosen = kind === 'system' ? 'system' : 'solve:' + main;
    else if (command) chosen = command;
    if (!chosen) chosen = kind === 'number' ? 'evaluate' : kind === 'equation' || kind === 'inequality' ? 'solve:' + main : kind === 'system' ? 'system' : 'simplify';
    if (!options.some((o) => o[0] === chosen) && chosen.indexOf(':') < 0 && kind !== 'number') options.unshift([chosen, chosen[0].toUpperCase() + chosen.slice(1)]);
    return { kind, vars, variable: main, options: options.map(([id, label]) => ({ id, label })), chosen, params, tex: shown(node, command, params, main), body };
  }
  /** How the question reads, its command written as maths: d/dx [ … ], ∫ … dx, lim, "Factor". */
  function shown(node, command, params, x) {
    const t = tex(node);
    try {
      if (command === 'derivative') return '\\frac{d}{d' + x + '}\\left[' + t + '\\right]';
      if (command === 'integral') return '\\int' + (params.lo !== undefined ? '_{' + tex(M.S(M.parse(String(params.lo)))) + '}^{' + tex(M.S(M.parse(String(params.hi)))) + '}' : '') + ' ' + t + '\\,d' + x;
      if (command === 'limit' && params.to !== undefined) return '\\lim_{' + x + ' \\to ' + tex(M.S(M.parse(String(params.to)))) + '} ' + t;
      if (['factor', 'expand', 'simplify'].includes(command)) return '\\text{' + command[0].toUpperCase() + command.slice(1) + '}\\;' + t;
    } catch (e) { /* a limit's own words not read: as it is */ }
    return t;
  }
  function pickVar(vars, asked) {
    if (asked) return asked;
    for (const v of ['x', 'y', 't', 'z', 'n']) if (vars.includes(v)) return v;
    return vars[0] || 'x';
  }

  /** One option worked: {title, answers [{tex, text, approx}], steps [{text, tex}], note, graph, error}. */
  function run(text, option, extra) {
    try {
      return work(text, option, extra || {});
    } catch (e) {
      return { error: e instanceof MathError || e instanceof RangeError ? (e instanceof RangeError ? 'That is too large to work out' : e.message) : 'That could not be worked out', detail: String(e && e.stack || e) };
    }
  }

  function approx(a) {
    const v = evalN(a, {});
    if (!isFinite(v) || isConst(a) && a.t === 'num' && a.d === 1n) return null;
    return M.realText(v);
  }
  function answer(a, label) {
    const ap = M.variables(a).length ? null : approx(a);
    return { tex: (label ? label + ' = ' : '') + tex(a), text: (label ? label + ' = ' : '') + M.text(a), approx: ap && ap !== tex(a) ? ap : null };
  }

  function work(text, option, extra) {
    const info = analyze(text);
    const node = M.parse(info.body);
    option = option || info.chosen;
    const [op, opVar] = option.split(':');
    const x = opVar || extra.variable || info.variable;
    const params = Object.assign({}, info.params, extra);
    const out = { title: '', answers: [], steps: [], input: tex(node), option, kind: info.kind, variable: x };
    const graphOf = (fs) => { out.graph = { fns: fs.filter(Boolean).map((f) => ({ tex: tex(f), js: f })), x }; };

    switch (op) {
      case 'evaluate': {
        let a = node;
        if (info.kind !== 'number') {
          const at = params.at !== undefined ? M.S(M.parse(String(params.at))) : null;
          if (!at) { out.ask = { what: 'at', label: x + ' =' }; out.title = 'Evaluate'; return out; }
          a = M.substitute(node, x, at);
          out.steps.push(step('Substitute $' + x + ' = ' + tex(at) + '$', tex(a)));
        }
        const v = S(a);
        out.title = 'Value';
        out.answers.push(answer(v));
        const n = evalN(v, {});
        if (v.t === 'num' && v.d !== 1n) {
          const whole = v.n / v.d;
          if (whole !== 0n) out.alternates = [{ label: 'Mixed number', tex: (v.n < 0n ? '-' : '') + M.babs(whole) + '\\tfrac{' + M.babs(v.n % v.d) + '}{' + v.d + '}' }];
        }
        if (!isFinite(n) && !M.variables(v).length) throw new MathError('That is undefined');
        if (info.kind === 'number' && !M.equal(v, node)) out.steps.push(step('Work it out', tex(v)));
        return out;
      }
      case 'simplify': {
        const opts = [{ a: S(node), why: null }];
        try { opts.push({ a: expand(node), why: 'Multiply out and combine like terms' }); } catch (e) { /* too large */ }
        if (M.variables(node).length === 1) {
          try { opts.push({ a: M.cancel(node, x), why: 'Write it as one fraction and cancel the common factors' }); } catch (e) { /* not a fraction of polynomials */ }
        }
        const best = opts.sort((p, q) => M.text(p.a).length - M.text(q.a).length || (p.why ? 1 : -1))[0];
        if (!M.equal(S(node), S(best.a)) || best.why) out.steps.push(step(best.why || 'Combine like terms', tex(best.a)));
        else out.steps.push(step('Combine like terms and simplify', tex(best.a)));
        out.title = 'Simplified';
        out.answers.push(answer(best.a));
        if (info.kind === 'expression' && M.variables(node).length === 1) graphOf([best.a]);
        return out;
      }
      case 'factor': {
        const f = M.factor(node, x);
        out.steps.push(...f.steps);
        out.title = 'Factored';
        out.answers.push(answer(f.result));
        if (!f.changed) out.note = 'It does not factor any further over the rational numbers';
        return out;
      }
      case 'expand': {
        const e = expand(node);
        out.steps.push(step('Multiply out each product, then combine like terms', tex(e)));
        out.title = 'Expanded';
        out.answers.push(answer(e));
        return out;
      }
      case 'derivative': {
        const order = Math.max(1, Math.min(6, parseInt(params.order || 1, 10) || 1));
        const r = M.derivative(node, x, order);
        out.steps.push(...r.steps);
        out.title = order === 1 ? 'Derivative' : 'Derivative ' + order;
        out.answers.push({ tex: '\\frac{d' + (order > 1 ? '^' + order : '') + '}{d' + x + (order > 1 ? '^' + order : '') + '}\\left[' + tex(S(node)) + '\\right] = ' + tex(r.result), text: M.text(r.result) });
        graphOf([S(node), r.result]);
        return out;
      }
      case 'integral': {
        const lo = params.lo !== undefined && params.lo !== '' ? S(M.parse(String(params.lo))) : undefined;
        const hi = params.hi !== undefined && params.hi !== '' ? S(M.parse(String(params.hi))) : undefined;
        if ((lo === undefined) !== (hi === undefined)) throw new MathError('Give both limits of the integral, or neither');
        const r = M.integral(node, x, lo, hi);
        out.steps.push(...r.steps);
        if (lo === undefined) {
          out.title = 'Integral';
          out.answers.push({ tex: '\\int ' + tex(S(node)) + '\\,d' + x + ' = ' + tex(r.result) + ' + C', text: M.text(r.result) + ' + C' });
          graphOf([S(node)]);
        } else {
          out.title = 'Definite integral';
          const a = answer(r.result);
          out.answers.push({ tex: '\\int_{' + tex(lo) + '}^{' + tex(hi) + '} ' + tex(S(node)) + '\\,d' + x + ' = ' + (r.numeric ? '\\approx ' : '') + tex(r.result), text: a.text, approx: r.numeric ? null : a.approx });
          graphOf([S(node)]);
          out.graph.shade = [evalN(lo, {}), evalN(hi, {})];
        }
        return out;
      }
      case 'limit': {
        if (params.to === undefined || params.to === '') { out.ask = { what: 'to', label: x + ' \\to' }; out.title = 'Limit'; return out; }
        const to = S(M.parse(String(params.to)));
        const r = M.limit(node, x, to, params.side || 0);
        out.steps.push(...r.steps);
        out.title = 'Limit';
        const head = '\\lim_{' + x + ' \\to ' + tex(to) + (params.side > 0 ? '^+' : params.side < 0 ? '^-' : '') + '} ' + tex(S(node));
        if (!r.result) { out.answers.push({ tex: head + '\\;\\text{does not exist}', text: 'does not exist' }); }
        else out.answers.push({ tex: head + ' = ' + (r.numeric ? '\\approx ' : '') + tex(r.result), text: M.text(r.result), approx: r.numeric ? null : approx(r.result) });
        graphOf([S(node)]);
        return out;
      }
      case 'solve': {
        if (info.kind === 'inequality') {
          if (node.t === 'chain') {
            const a = M.solveInequality(node.a, x), b = M.solveInequality(node.b, x);
            out.steps.push(step('Split it into two inequalities', tex(node.a) + '\\quad\\text{and}\\quad ' + tex(node.b)), ...a.steps, ...b.steps);
            const both = M.intersectIntervals(a.intervals, b.intervals);
            const t = M.intervalsTex(both, x);
            out.steps.push(step('Keep where both hold', t.inequalityTex));
            out.title = 'Solution';
            out.answers.push({ tex: t.inequalityTex, text: '' });
            out.alternates = [{ label: 'Interval notation', tex: t.intervalTex }];
            return out;
          }
          const r = M.solveInequality(node, x);
          out.steps.push(...r.steps);
          out.title = 'Solution';
          out.answers.push({ tex: r.inequalityTex, text: '' });
          out.alternates = [{ label: 'Interval notation', tex: r.intervalTex }];
          graphOf([S(node.l), S(node.r)]);
          return out;
        }
        if (node.t !== 'rel') throw new MathError('There is nothing to solve: type an equation, with =');
        const r = M.solve(node, x);
        out.steps.push(...r.steps);
        out.title = r.solutions.length > 1 ? 'Solutions' : 'Solution';
        if (r.all) out.answers.push({ tex: '\\text{Every number: it is true for all } ' + x, text: 'all ' + x });
        for (const s of r.solutions) {
          const a = answer(s, x);
          if (r.numeric || M.isReal(s)) { a.tex = x + ' \\approx ' + tex(s); a.approx = null; }
          out.answers.push(a);
        }
        if (r.general) out.alternates = [{ label: 'Every solution', tex: r.general }];
        if (r.general && r.solutions.length) out.note = 'The solutions from 0 to 2π are shown; they repeat';
        if (r.complex && r.complex.length && !r.solutions.length) {
          out.alternates = (out.alternates || []).concat(r.complex.map((c) => ({ label: 'Complex', tex: x + ' = ' + complexTex(c) })));
        }
        if (!r.solutions.length && !r.all) out.answers.push({ tex: '\\text{' + (r.none || 'No solution') + '}', text: r.none || 'No solution', none: true });
        if (r.truncated) out.note = 'Only the first 40 solutions are shown';
        if (r.numeric) out.note = 'Found numerically; the decimals are rounded';
        graphOf([S(node.l), S(node.r)]);
        out.graph.points = r.solutions.map((s) => evalN(s, {})).filter(isFinite).map((v) => [v, evalN(S(M.substitute(node.l, x, R(v))), {})]);
        return out;
      }
      case 'system': {
        const rels = node.t === 'list' ? node.a : [node];
        const r = M.solveSystem(rels);
        out.steps.push(...r.steps);
        out.title = 'Solution';
        if (r.none) out.answers.push({ tex: '\\text{' + r.none + '}', text: r.none, none: true });
        else if (r.pairs) {
          for (const p of r.pairs) out.answers.push({ tex: r.vars.slice().sort().map((v) => v + ' = ' + tex(p[v])).join(',\\; '), text: r.vars.slice().sort().map((v) => v + ' = ' + M.text(p[v])).join(', ') });
          if (!r.pairs.length) out.answers.push({ tex: '\\text{No real solution}', text: 'No real solution', none: true });
        } else {
          const vs = Object.keys(r.solutions).sort();
          out.answers.push({ tex: vs.map((v) => v + ' = ' + tex(r.solutions[v])).join(',\\; '), text: vs.map((v) => v + ' = ' + M.text(r.solutions[v])).join(', ') });
          if (r.infinite) out.note = 'Infinitely many solutions: ' + r.free.join(', ') + ' can be any number';
        }
        // (two letters, each equation solvable for y: drawn)
        const vs = M.variables(node);
        if (vs.length === 2) {
          const [u, w] = vs.includes('x') && vs.includes('y') ? ['x', 'y'] : vs;
          const fs = rels.map((rel) => { try { const s = M.solve(rel, w); return s.solutions.length === 1 && !s.numeric ? s.solutions[0] : null; } catch (e) { return null; } });
          if (fs.every(Boolean)) { graphOf(fs); out.graph.x = u; }
        }
        return out;
      }
      case 'graph': {
        out.title = 'Graph';
        if (node.t === 'rel') {
          // (y = f(x): f drawn; else both sides)
          const vs = M.variables(node);
          if (vs.includes('y') && vs.length <= 2) {
            const s = M.solve(node, 'y');
            if (s.solutions.length) { graphOf(s.solutions); out.graph.x = vs.find((v) => v !== 'y') || 'x'; out.answers.push({ tex: s.solutions.map((f) => 'y = ' + tex(f)).join(',\\; '), text: '' }); return out; }
          }
          graphOf([S(node.l), S(node.r)]);
        } else graphOf([S(node)]);
        out.answers.push({ tex: out.graph.fns.map((f) => 'y = ' + f.tex).join(',\\; '), text: '' });
        return out;
      }
    }
    throw new MathError('That is not one of the ways to work it');
  }
  function complexTex(c) {
    const re = S(c.re), im = S(c.im);
    const neg = M.isNegative(im);
    const mag = neg ? S(mul([Q(-1), im])) : im;
    const imTex = M.isOne(mag) ? 'i' : tex(mag, 1) + 'i';
    return (isZero(re) ? (neg ? '-' : '') : tex(re) + (neg ? ' - ' : ' + ')) + imTex;
  }

  /** A function to draw: its value at a number (NaN where it has none). */
  function plotter(node, x) { return M.compile(node, x || 'x'); }

  root.SimplSolver = { read, analyze, run, plotter, version: '1' };
})(typeof self !== 'undefined' ? self : globalThis);
