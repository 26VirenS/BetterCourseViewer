/* Simpl's solver, the calculus: derivatives (each rule said), integrals (the table, a linear inside, u-substitution, by
 * parts, partial fractions; a definite one exactly or as a number), and limits (substitution, L'Hôpital, at infinity). */
(function (root) {
  'use strict';
  const M = root.SimplMath;
  const { Q, R, sym, add, mul, pow, fn, eq, S, expand, tex, MathError, isConst, isNum, isZero, isOne, isInt, numVal, neg,
    freeOf, evalN, polyCoeffs, numericPoly, polyDiv, polyFrom, substitute, PI, E } = M;
  const step = (text, t) => ({ text, tex: t });
  const negate = (a) => S(mul([Q(-1), a]));

  // ---------------------------------------------------------------- derivatives

  function d(a, x) {
    switch (a.t) {
      case 'num': case 'real': return Q(0);
      case 'sym': return a.n === x ? Q(1) : Q(0);
      case 'add': return S(add(a.a.map((t) => d(t, x))));
      case 'mul': {
        const terms = [];
        a.a.forEach((f, i) => {
          if (freeOf(f, x)) return;
          terms.push(mul([d(f, x), ...a.a.filter((_, j) => j !== i)]));
        });
        return S(add(terms));
      }
      case 'pow': {
        const b = a.b, e = a.e;
        if (freeOf(e, x)) return S(mul([e, pow(b, add([e, Q(-1)])), d(b, x)]));
        if (freeOf(b, x)) return S(mul([a, fn('ln', b), d(e, x)]));
        return S(mul([a, add([mul([d(e, x), fn('ln', b)]), mul([e, d(b, x), pow(b, Q(-1))])])]));
      }
      case 'fn': {
        const u = a.a, du = d(u, x);
        if (isZero(du) && a.n !== 'log') return Q(0);
        return S(mul([outer(a), du]));
      }
    }
    throw new MathError('This cannot be differentiated');
  }
  /** f′(u) for f(u): the outer function's derivative, at its inside. */
  function outer(a) {
    const u = a.a;
    switch (a.n) {
      case 'sin': return fn('cos', u);
      case 'cos': return mul([Q(-1), fn('sin', u)]);
      case 'tan': return pow(fn('sec', u), Q(2));
      case 'sec': return mul([fn('sec', u), fn('tan', u)]);
      case 'csc': return mul([Q(-1), fn('csc', u), fn('cot', u)]);
      case 'cot': return mul([Q(-1), pow(fn('csc', u), Q(2))]);
      case 'asin': return pow(add([Q(1), mul([Q(-1), pow(u, Q(2))])]), Q(-1, 2));
      case 'acos': return mul([Q(-1), pow(add([Q(1), mul([Q(-1), pow(u, Q(2))])]), Q(-1, 2))]);
      case 'atan': return pow(add([Q(1), pow(u, Q(2))]), Q(-1));
      case 'acot': return mul([Q(-1), pow(add([Q(1), pow(u, Q(2))]), Q(-1))]);
      case 'sinh': return fn('cosh', u);
      case 'cosh': return fn('sinh', u);
      case 'tanh': return pow(fn('cosh', u), Q(-2));
      case 'ln': return pow(u, Q(-1));
      case 'log': return pow(mul([u, fn('ln', a.base || Q(10))]), Q(-1));
      case 'abs': return mul([u, pow(fn('abs', u), Q(-1))]);
    }
    throw new MathError('The derivative of ' + a.n + ' is not known here');
  }

  const DX = (x) => '\\frac{d}{d' + x + '}';
  const RULE = {
    sin: '\\frac{d}{du}\\sin u = \\cos u', cos: '\\frac{d}{du}\\cos u = -\\sin u', tan: '\\frac{d}{du}\\tan u = \\sec^2 u',
    sec: '\\frac{d}{du}\\sec u = \\sec u \\tan u', csc: '\\frac{d}{du}\\csc u = -\\csc u \\cot u', cot: '\\frac{d}{du}\\cot u = -\\csc^2 u',
    asin: '\\frac{d}{du}\\arcsin u = \\frac{1}{\\sqrt{1-u^2}}', acos: '\\frac{d}{du}\\arccos u = -\\frac{1}{\\sqrt{1-u^2}}', atan: '\\frac{d}{du}\\arctan u = \\frac{1}{1+u^2}',
    ln: '\\frac{d}{du}\\ln u = \\frac{1}{u}', log: '\\frac{d}{du}\\log_b u = \\frac{1}{u \\ln b}', sinh: '\\frac{d}{du}\\sinh u = \\cosh u', cosh: '\\frac{d}{du}\\cosh u = \\sinh u',
    abs: '\\frac{d}{du}|u| = \\frac{u}{|u|}',
  };

  /** The derivative with its steps: the rule for the whole, then for each part that needs its own. */
  function derivative(a, x, order) {
    order = order || 1;
    const steps = [];
    let cur = S(a);
    for (let k = 1; k <= order; k++) {
      if (order > 1) steps.push(step('Derivative number ' + k, ''));
      explain(cur, x, steps, 0);
      const raw = d(cur, x);
      const simple = tidy(raw, x);
      steps.push(step('Simplify', tex(simple)));
      cur = simple;
    }
    return { result: cur, steps };
  }
  /** The derivative written its simplest way: expanded or as it is, whichever is shorter; a fraction cancelled. */
  function tidy(a, x) {
    const opts = [S(a)];
    try { opts.push(expand(a)); } catch (e) { /* too large */ }
    try { if (M.cancel) opts.push(M.cancel(a, x)); } catch (e) { /* not a fraction of polynomials */ }
    return opts.sort((p, q) => M.text(p).length - M.text(q).length)[0];
  }
  function explain(a, x, steps, depth) {
    if (depth > 4 || freeOf(a, x)) return;
    const head = DX(x) + '\\left[' + tex(a) + '\\right]';
    switch (a.t) {
      case 'sym': return;
      case 'add':
        steps.push(step('Sum rule: the derivative of a sum is the sum of the derivatives', head + ' = ' + a.a.map((t) => DX(x) + '\\left[' + tex(t) + '\\right]').join(' + ')));
        a.a.forEach((t) => explain(t, x, steps, depth + 1));
        return;
      case 'mul': {
        const consts = a.a.filter((f) => freeOf(f, x)), vars = a.a.filter((f) => !freeOf(f, x));
        if (consts.length) {
          const c = S(mul(consts)), rest = S(mul(vars));
          steps.push(step('Constant multiple rule: take out $' + tex(c) + '$', head + ' = ' + tex(c, 1) + ' \\cdot ' + DX(x) + '\\left[' + tex(rest) + '\\right]'));
          explain(rest, x, steps, depth + 1);
          return;
        }
        const { top, bottom } = M.splitFraction(a);
        if (bottom.length && top.length && !top.every((f) => freeOf(f, x))) {
          const f = S(mul(top)), g = S(mul(bottom));
          steps.push(step('Quotient rule: $\\left(\\frac{f}{g}\\right)\' = \\frac{f\'g - fg\'}{g^2}$ with $f = ' + tex(f) + '$, $g = ' + tex(g) + '$',
            '\\frac{(' + tex(tidy(d(f, x), x)) + ')(' + tex(g) + ') - (' + tex(f) + ')(' + tex(tidy(d(g, x), x)) + ')}{(' + tex(g) + ')^2}'));
          return;
        }
        const f = vars[0], g = S(mul(vars.slice(1)));
        steps.push(step('Product rule: $(fg)\' = f\'g + fg\'$ with $f = ' + tex(f) + '$, $g = ' + tex(g) + '$',
          '(' + tex(tidy(d(f, x), x)) + ')(' + tex(g) + ') + (' + tex(f) + ')(' + tex(tidy(d(g, x), x)) + ')'));
        return;
      }
      case 'pow': {
        if (freeOf(a.e, x)) {
          if (a.b.t === 'sym') { steps.push(step('Power rule: $\\frac{d}{dx}x^n = nx^{n-1}$', head + ' = ' + tex(tidy(d(a, x), x)))); return; }
          steps.push(step('Chain rule with the power rule: the outside is $u^{' + tex(a.e) + '}$, the inside $u = ' + tex(a.b) + '$', head + ' = ' + tex(a.e, 1) + '\\left(' + tex(a.b) + '\\right)^{' + tex(S(add([a.e, Q(-1)]))) + '} \\cdot ' + DX(x) + '\\left[' + tex(a.b) + '\\right]'));
          explain(a.b, x, steps, depth + 1);
          return;
        }
        if (freeOf(a.b, x)) {
          const isE = a.b.t === 'sym' && a.b.n === 'e';
          const rule = isE ? 'Exponential rule: $\\frac{d}{du}e^u = e^u$' : 'Exponential rule: $\\frac{d}{du}b^u = b^u \\ln b$';
          if (a.e.t === 'sym') { steps.push(step(rule, head + ' = ' + tex(tidy(d(a, x), x)))); return; }
          steps.push(step(rule + ', with the chain rule for $u = ' + tex(a.e) + '$', head + ' = ' + tex(isE ? a : mul([a, fn('ln', a.b)])) + ' \\cdot ' + DX(x) + '\\left[' + tex(a.e) + '\\right]'));
          explain(a.e, x, steps, depth + 1);
          return;
        }
        steps.push(step('Write it as $e^{' + tex(a.e) + '\\ln ' + tex(a.b, 2) + '}$ and use the chain rule', head + ' = ' + tex(tidy(d(a, x), x))));
        return;
      }
      case 'fn': {
        const rule = RULE[a.n] ? '$' + RULE[a.n] + '$' : 'the derivative of ' + a.n;
        if (a.a.t === 'sym') { steps.push(step('Use ' + rule, head + ' = ' + tex(tidy(d(a, x), x)))); return; }
        steps.push(step('Chain rule: use ' + rule + ' with $u = ' + tex(a.a) + '$', head + ' = ' + tex(S(outer(a))) + ' \\cdot ' + DX(x) + '\\left[' + tex(a.a) + '\\right]'));
        explain(a.a, x, steps, depth + 1);
        return;
      }
    }
  }

  // ---------------------------------------------------------------- integrals

  /** ∫a dx, or null where none is found: tried in turn — a constant, a sum, a constant factor, the table, a linear
   * inside, expanding, partial fractions, u-substitution, by parts. Each way that works says so in `steps`. */
  function integrate(a, x, steps, depth) {
    depth = depth || 0;
    if (depth > 6) return null;
    a = S(a);
    const X = sym(x);
    if (freeOf(a, x)) return S(mul([a, X]));
    if (a.t === 'add') {
      const parts = a.a.map((t) => integrate(t, x, [], depth + 1));
      if (parts.every((p) => p)) {
        steps.push(step('Integrate term by term', a.a.map((t, i) => '\\int ' + tex(t, 1) + '\\,d' + x + ' = ' + tex(parts[i])).join(',\\quad ')));
        return S(add(parts));
      }
    }
    if (a.t === 'mul') {
      const c = a.a.filter((f) => freeOf(f, x));
      if (c.length) {
        const rest = S(mul(a.a.filter((f) => !freeOf(f, x))));
        const r = integrate(rest, x, steps, depth + 1);
        if (r) { steps.unshift(step('Take the constant $' + tex(S(mul(c))) + '$ out of the integral', '')); return S(mul([...c, r])); }
        return null;
      }
    }
    const table = basic(a, x);
    if (table) { steps.push(step(table.rule, '\\int ' + tex(a) + '\\,d' + x + ' = ' + tex(table.result))); return table.result; }
    // (trig powers: sin² and cos² halved; tan² is sec² − 1)
    const trig = trigPower(a, x);
    if (trig) {
      steps.push(step(trig.rule, tex(a) + ' = ' + tex(trig.as)));
      const r = integrate(trig.as, x, steps, depth + 1);
      if (r) return r;
    }
    // (a polynomial times a power of a sum: multiplied out)
    const ex = expand(a);
    if (!M.equal(ex, a) && ex.t === 'add' && ex.a.length <= 40) {
      const r = integrate(ex, x, [], depth + 1);
      if (r) { steps.push(step('Expand, then integrate term by term', '\\int ' + tex(ex) + '\\,d' + x + ' = ' + tex(r))); return r; }
    }
    const pf = partialFractions(a, x, steps, depth);
    if (pf) return pf;
    const us = uSub(a, x, steps, depth);
    if (us) return us;
    const bp = byParts(a, x, steps, depth);
    if (bp) return bp;
    return null;
  }

  /** The table, with a linear inside (f(kx + c) integrates to F(kx + c)/k). */
  function basic(a, x) {
    const X = sym(x);
    const linear = (u) => { const cs = polyCoeffs(u, x); return cs && cs.length === 2 && freeOf(cs[1], x) ? cs[1] : null; };
    const over = (r, k) => S(mul([r, pow(k, Q(-1))]));
    if (a.t === 'sym' && a.n === x) return { result: S(mul([Q(1, 2), pow(X, Q(2))])), rule: 'Power rule: $\\int x^n\\,dx = \\frac{x^{n+1}}{n+1}$' };
    if (a.t === 'pow' && freeOf(a.e, x)) {
      const k = linear(a.b);
      if (k) {
        if (isNum(a.e) && a.e.n === -1n && a.e.d === 1n) return { result: over(fn('ln', fn('abs', a.b)), k), rule: '$\\int \\frac{1}{u}\\,du = \\ln|u|$' + (isOne(k) ? '' : ', divided by $' + tex(k) + '$ for the inside') };
        const e1 = S(add([a.e, Q(1)]));
        return { result: over(mul([pow(a.b, e1), pow(e1, Q(-1))]), k), rule: 'Power rule: $\\int u^n\\,du = \\frac{u^{n+1}}{n+1}$' + (isOne(k) ? '' : ', divided by $' + tex(k) + '$ for the inside') };
      }
    }
    if (a.t === 'pow' && freeOf(a.b, x)) {
      const k = linear(a.e);
      if (k) {
        const isE = a.b.t === 'sym' && a.b.n === 'e';
        return { result: over(isE ? a : mul([a, pow(fn('ln', a.b), Q(-1))]), k), rule: isE ? '$\\int e^u\\,du = e^u$' : '$\\int b^u\\,du = \\frac{b^u}{\\ln b}$' };
      }
    }
    if (a.t === 'fn') {
      const u = a.a, k = linear(u);
      if (k) {
        const T = {
          sin: [mul([Q(-1), fn('cos', u)]), '\\int \\sin u\\,du = -\\cos u'],
          cos: [fn('sin', u), '\\int \\cos u\\,du = \\sin u'],
          tan: [mul([Q(-1), fn('ln', fn('abs', fn('cos', u)))]), '\\int \\tan u\\,du = -\\ln|\\cos u|'],
          cot: [fn('ln', fn('abs', fn('sin', u))), '\\int \\cot u\\,du = \\ln|\\sin u|'],
          sec: [fn('ln', fn('abs', add([fn('sec', u), fn('tan', u)]))), '\\int \\sec u\\,du = \\ln|\\sec u + \\tan u|'],
          csc: [mul([Q(-1), fn('ln', fn('abs', add([fn('csc', u), fn('cot', u)])))]), '\\int \\csc u\\,du = -\\ln|\\csc u + \\cot u|'],
          ln: [add([mul([u, fn('ln', u)]), mul([Q(-1), u])]), '\\int \\ln u\\,du = u\\ln u - u'],
          sinh: [fn('cosh', u), '\\int \\sinh u\\,du = \\cosh u'],
          cosh: [fn('sinh', u), '\\int \\cosh u\\,du = \\sinh u'],
          atan: [add([mul([u, fn('atan', u)]), mul([Q(-1, 2), fn('ln', add([Q(1), pow(u, Q(2))]))])]), '\\int \\arctan u\\,du = u\\arctan u - \\tfrac12\\ln(1+u^2)'],
          asin: [add([mul([u, fn('asin', u)]), pow(add([Q(1), mul([Q(-1), pow(u, Q(2))])]), Q(1, 2))]), '\\int \\arcsin u\\,du = u\\arcsin u + \\sqrt{1-u^2}'],
        }[a.n];
        if (T) return { result: over(T[0], k), rule: '$' + T[1] + '$' + (isOne(k) ? '' : ', divided by $' + tex(k) + '$ for the inside') };
      }
    }
    // (sec², csc², sec·tan, 1/(1 + u²), 1/√(1 − u²))
    if (a.t === 'pow' && isInt(a.e) && a.e.n === 2n && a.b.t === 'fn') {
      const k = linear(a.b.a);
      if (k && a.b.n === 'sec') return { result: over(fn('tan', a.b.a), k), rule: '$\\int \\sec^2 u\\,du = \\tan u$' };
      if (k && a.b.n === 'csc') return { result: over(mul([Q(-1), fn('cot', a.b.a)]), k), rule: '$\\int \\csc^2 u\\,du = -\\cot u$' };
    }
    if (a.t === 'mul' && a.a.length === 2) {
      const [p, q] = a.a;
      if (p.t === 'fn' && q.t === 'fn' && M.equal(p.a, q.a) && linear(p.a)) {
        const pair = p.n + q.n, k = linear(p.a);
        if (pair === 'sectan' || pair === 'tansec') return { result: over(fn('sec', p.a), k), rule: '$\\int \\sec u \\tan u\\,du = \\sec u$' };
        if (pair === 'cotcsc' || pair === 'csccot') return { result: over(mul([Q(-1), fn('csc', p.a)]), k), rule: '$\\int \\csc u \\cot u\\,du = -\\csc u$' };
      }
    }
    if (a.t === 'pow' && a.b.t === 'add') {
      // (1/(a² + x²) → atan, 1/√(a² − x²) → asin)
      const cs = polyCoeffs(a.b, x);
      if (cs && cs.length === 3 && isZero(cs[1]) && numericPoly(cs)) {
        const c0 = numVal(cs[0]), c2 = numVal(cs[2]);
        if (isNum(a.e) && a.e.n === -1n && a.e.d === 1n && c0 > 0 && c2 > 0) {
          const k = S(pow(mul([cs[2], pow(cs[0], Q(-1))]), Q(1, 2)));
          return { result: S(mul([pow(mul([cs[0], cs[2]]), Q(-1, 2)), fn('atan', mul([k, sym(x)]))])), rule: '$\\int \\frac{du}{a^2 + u^2} = \\frac{1}{a}\\arctan\\frac{u}{a}$' };
        }
        if (isNum(a.e) && a.e.n === -1n && a.e.d === 2n && c0 > 0 && c2 < 0) {
          const k = S(pow(mul([neg(cs[2]), pow(cs[0], Q(-1))]), Q(1, 2)));
          return { result: S(mul([pow(neg(cs[2]), Q(-1, 2)), fn('asin', mul([k, sym(x)]))])), rule: '$\\int \\frac{du}{\\sqrt{a^2 - u^2}} = \\arcsin\\frac{u}{a}$' };
        }
      }
    }
    return null;
  }

  function trigPower(a, x) {
    if (a.t !== 'pow' || !isInt(a.e) || a.e.n !== 2n || a.b.t !== 'fn') return null;
    const u = a.b.a;
    const two = S(mul([Q(2), u]));
    if (a.b.n === 'sin') return { as: S(add([Q(1, 2), mul([Q(-1, 2), fn('cos', two)])])), rule: 'Use $\\sin^2 u = \\frac{1 - \\cos 2u}{2}$' };
    if (a.b.n === 'cos') return { as: S(add([Q(1, 2), mul([Q(1, 2), fn('cos', two)])])), rule: 'Use $\\cos^2 u = \\frac{1 + \\cos 2u}{2}$' };
    if (a.b.n === 'tan') return { as: S(add([pow(fn('sec', u), Q(2)), Q(-1)])), rule: 'Use $\\tan^2 u = \\sec^2 u - 1$' };
    return null;
  }

  /** u-substitution: u some part of it whose derivative is (a constant times) what is left. */
  function uSub(a, x, steps, depth) {
    const cands = [];
    const walk = (n) => {
      if (n.t === 'fn' && !freeOf(n.a, x) && n.a.t !== 'sym') cands.push(n.a);
      if (n.t === 'fn' && !freeOf(n, x)) cands.push(n);
      if (n.t === 'pow' && !freeOf(n.b, x) && n.b.t !== 'sym') cands.push(n.b);
      if (n.t === 'pow' && freeOf(n.b, x) && !freeOf(n.e, x) && n.e.t !== 'sym') cands.push(n.e);
      if (n.t === 'add' || n.t === 'mul') n.a.forEach(walk);
      if (n.t === 'pow') { walk(n.b); walk(n.e); }
      if (n.t === 'fn') walk(n.a);
    };
    walk(a);
    const U = sym('u');
    for (const u of cands) {
      const du = d(u, x);
      if (isZero(du)) continue;
      // (what is left once du is taken out, in terms of u alone)
      const ratio = S(mul([a, pow(du, Q(-1))]));
      let inU = M.replace(ratio, S(u), U);
      inU = S(inU);
      if (!freeOf(inU, x)) {
        // (u = x² + 1 and the rest x²: x² written as u − 1 when u is linear in x²)
        continue;
      }
      if (equalNode(inU, ratio)) continue;
      const inner = [];
      const r = integrate(inU, 'u', inner, depth + 1);
      if (!r) continue;
      steps.push(step('Substitute $u = ' + tex(u) + '$, so $du = ' + tex(du) + '\\,d' + x + '$', '\\int ' + tex(inU) + '\\,du'));
      steps.push(...inner);
      const back = S(substitute(r, 'u', u));
      steps.push(step('Put $u = ' + tex(u) + '$ back', tex(back)));
      return back;
    }
    return null;
  }
  const equalNode = (a, b) => M.key(a) === M.key(b);

  /** By parts, ∫u dv = uv − ∫v du, u chosen by LIATE (logs, inverse trig, algebra, trig, exponentials). */
  function byParts(a, x, steps, depth) {
    if (depth > 4) return null;
    const fs = a.t === 'mul' ? a.a : [a];
    const rank = (f) => {
      if (f.t === 'fn' && (f.n === 'ln' || f.n === 'log')) return 0;
      if (f.t === 'fn' && f.n.startsWith('a') && ['asin', 'acos', 'atan'].includes(f.n)) return 1;
      if ((f.t === 'sym' && f.n === x) || (f.t === 'pow' && f.b.t === 'sym' && f.b.n === x && isInt(f.e) && f.e.n > 0n)) return 2;
      if (f.t === 'fn' && ['sin', 'cos'].includes(f.n)) return 3;
      if (f.t === 'pow' && freeOf(f.b, x)) return 4;
      return 9;
    };
    let order = fs.map((f, i) => ({ f, i, r: rank(f) })).sort((p, q) => p.r - q.r);
    // (ln x alone: u = ln x, dv = dx)
    if (fs.length === 1 && rank(fs[0]) <= 1) order = [{ f: fs[0], i: 0, r: 0 }];
    else if (order.length < 2 || order[0].r === 9) return null;
    const u = order[0].f;
    const dv = fs.length === 1 ? Q(1) : S(mul(fs.filter((_, i) => i !== order[0].i)));
    const v = integrate(dv, x, [], depth + 1);
    if (!v) return null;
    const du = d(u, x);
    const rest = S(mul([v, du]));
    const inner = [];
    const w = integrate(rest, x, inner, depth + 1);
    if (!w) return null;
    steps.push(step('Integrate by parts, $\\int u\\,dv = uv - \\int v\\,du$, with $u = ' + tex(u) + '$, $dv = ' + tex(dv) + '\\,d' + x + '$', 'du = ' + tex(du) + '\\,d' + x + ',\\quad v = ' + tex(v)));
    steps.push(step('So', tex(S(mul([u, v]))) + ' - \\int ' + tex(rest, 1) + '\\,d' + x));
    steps.push(...inner);
    return S(add([mul([u, v]), mul([Q(-1), w])]));
  }

  /** A fraction of polynomials: divided if the top is not smaller, the rest split over the bottom's factors. */
  function partialFractions(a, x, steps, depth) {
    const { num, den, lcd } = M.together(a, x);
    if (!lcd) return null;
    const n = polyCoeffs(num, x), dd = polyCoeffs(expand(den), x);
    if (!n || !dd || !numericPoly(n) || !numericPoly(dd) || dd.length < 2) return null;
    let quotient = null, top = n;
    if (n.length >= dd.length) {
      const { q, r } = polyDiv(n, dd);
      quotient = polyFrom(q, x);
      top = r;
      steps.push(step('Divide: the top is not of lower degree than the bottom', tex(quotient) + ' + \\frac{' + tex(polyFrom(r, x)) + '}{' + tex(den) + '}'));
    }
    const fsteps = [];
    const fac = M.factor(polyFrom(dd, x), x);
    const factors = (fac.result.t === 'mul' ? fac.result.a : [fac.result]);
    let lead = Q(1);
    const lin = [];
    for (const f of factors) {
      if (isConst(f)) { lead = M.mulN(lead, f); continue; }
      const base = f.t === 'pow' ? f.b : f, k = f.t === 'pow' ? Number(f.e.n) : 1;
      const cs = polyCoeffs(base, x);
      if (cs.length !== 2) return quotient ? null : null;
      lin.push({ base, k, root: M.divN(neg(cs[0]), cs[1]), slope: cs[1] });
    }
    if (!lin.length) return null;
    // (distinct linear factors, each once: A = top(r)/den′(r); repeated ones: the coefficients solved for)
    const pieces = [];
    const denPoly = dd;
    const ddPrime = denPoly.slice(1).map((c, i) => M.mulN(c, Q(i + 1)));
    if (lin.every((l) => l.k === 1)) {
      for (const l of lin) {
        const A = M.divN(M.polyEval(top, l.root), M.polyEval(ddPrime, l.root));
        pieces.push(S(mul([A, M.divN(Q(1), Q(1)), pow(l.base, Q(-1))])));
      }
    } else return null;
    const split = S(add(pieces));
    steps.push(step('Split into partial fractions', tex(split)));
    const parts = [quotient ? integrate(quotient, x, [], depth + 1) : Q(0), integrate(split, x, [], depth + 1)];
    if (parts.some((p) => !p)) return null;
    const r = S(add(parts));
    steps.push(step('Integrate each part', tex(r)));
    return r;
  }

  /** ∫ with its steps: indefinite (+ C), or from a to b — exactly where an antiderivative is found, else as a number. */
  function integral(a, x, lo, hi) {
    const steps = [];
    const F = integrate(a, x, steps, 0);
    if (lo === undefined) {
      if (!F) throw new MathError('No antiderivative in elementary functions was found for this');
      const Fs = tidyAnti(F, x);
      return { result: Fs, steps, plusC: true };
    }
    if (F) {
      const Fs = tidyAnti(F, x);
      // (an infinite end: F's limit there)
      const at = (v) => { if (!isFinite(evalN(v, {}))) { const l = limit(Fs, x, v); if (!l.result) throw new MathError('This integral does not converge'); return l.result; } return S(substitute(Fs, x, v)); };
      const Fb = at(hi), Fa = at(lo);
      const val = S(add([Fb, mul([Q(-1), Fa])]));
      const num = evalN(val, {});
      if (isFinite(num) && hasNoUndefined(Fb) && hasNoUndefined(Fa)) {
        steps.push(step('Evaluate from $' + tex(lo) + '$ to $' + tex(hi) + '$: $F(' + tex(hi) + ') - F(' + tex(lo) + ')$', tex(Fb) + ' - \\left(' + tex(Fa) + '\\right) = ' + tex(val)));
        return { result: val, steps, value: num };
      }
    }
    const v = simpson(M.compile(a, x), evalN(lo, {}), evalN(hi, {}));
    if (!isFinite(v)) throw new MathError('This integral does not converge');
    steps.push(step(F ? 'The antiderivative is undefined somewhere between the limits; the area, found numerically' : 'No antiderivative in elementary functions was found; the area, found numerically', '\\approx ' + M.realText(v)));
    return { result: R(v), steps, value: v, numeric: true };
  }
  function hasNoUndefined(a) { return isFinite(evalN(a, {})); }
  function tidyAnti(F, x) {
    const opts = [S(F)];
    try { const e = expand(F); opts.push(e); } catch (e) { /* too large */ }
    return opts.sort((p, q) => M.text(p).length - M.text(q).length)[0];
  }
  /** Adaptive Simpson's rule (an infinite end mapped onto a finite one). */
  function simpson(f, a, b) {
    if (a === b) return 0;
    if (a > b) return -simpson(f, b, a);
    if (!isFinite(a) || !isFinite(b)) {
      // (x = t/(1 − t²) maps (−1, 1) onto (−∞, ∞))
      const g = (t) => { const s = 1 - t * t; return f(t / s) * (1 + t * t) / (s * s); };
      const ta = isFinite(a) ? (Math.abs(a) < 1e-12 ? 0 : (-1 + Math.sqrt(1 + 4 * a * a)) / (2 * a)) : -1 + 1e-9;
      const tb = isFinite(b) ? (Math.abs(b) < 1e-12 ? 0 : (-1 + Math.sqrt(1 + 4 * b * b)) / (2 * b)) : 1 - 1e-9;
      return simpson(g, ta, tb);
    }
    const safe = (t) => { const v = f(t); return isFinite(v) ? v : f(t + 1e-9 * (b - a)); };
    const s = (l, r, fl, fm, fr) => (r - l) / 6 * (fl + 4 * fm + fr);
    const rec = (l, r, fl, fm, fr, whole, eps, depth) => {
      const m = (l + r) / 2, lm = (l + m) / 2, rm = (m + r) / 2;
      const flm = safe(lm), frm = safe(rm);
      const left = s(l, m, fl, flm, fm), right = s(m, r, fm, frm, fr);
      if (depth > 40 || Math.abs(left + right - whole) <= 15 * eps) return left + right + (left + right - whole) / 15;
      return rec(l, m, fl, flm, fm, left, eps / 2, depth + 1) + rec(m, r, fm, frm, fr, right, eps / 2, depth + 1);
    };
    const fa = safe(a), fb = safe(b), fm = safe((a + b) / 2);
    return rec(a, b, fa, fm, fb, s(a, b, fa, fm, fb), 1e-10, 0);
  }

  // ---------------------------------------------------------------- limits

  /** The limit of a as x → to (a node: a number, or ±∞), with its steps; side −1, 0 or 1. */
  function limit(a, x, to, side) {
    side = side || 0;
    const steps = [];
    a = S(a);
    const toV = evalN(to, {});
    const atInf = !isFinite(toV);
    if (!atInf) {
      let direct = null;
      try { direct = S(substitute(a, x, to)); } catch (e) { /* a division by zero there */ }
      const v = direct ? evalN(direct, {}) : NaN;
      if (isFinite(v) && !hasZeroOverZero(a, x, to)) {
        steps.push(step('Substitute $' + x + ' = ' + tex(to) + '$ directly', tex(direct)));
        return { result: direct, steps, value: v };
      }
    } else {
      // (a fraction of polynomials at ±∞: by the leading terms)
      const { top, bottom } = M.splitFraction(a);
      const n = polyCoeffs(S(mul(top.length ? top : [Q(1)])), x), dd = polyCoeffs(S(mul(bottom.length ? bottom : [Q(1)])), x);
      if (n && dd && numericPoly(n) && numericPoly(dd)) {
        const dn = n.length - 1, ddeg = dd.length - 1;
        steps.push(step('Compare the highest powers of the top and the bottom', '\\text{degree } ' + dn + '\\text{ over degree } ' + ddeg));
        if (dn < ddeg) { steps.push(step('The bottom grows faster: the limit is 0', '0')); return { result: Q(0), steps, value: 0 }; }
        if (dn === ddeg) { const r = M.divN(n[dn], dd[ddeg]); steps.push(step('Same degree: the ratio of the leading coefficients', tex(r))); return { result: r, steps, value: numVal(r) }; }
        const sign = numVal(n[dn]) / numVal(dd[ddeg]) * (toV < 0 && (dn - ddeg) % 2 === 1 ? -1 : 1);
        steps.push(step('The top grows faster', sign > 0 ? '\\infty' : '-\\infty'));
        return { result: sign > 0 ? M.INF : S(mul([Q(-1), M.INF])), steps, value: sign > 0 ? Infinity : -Infinity };
      }
    }
    // (0/0 or ∞/∞: L'Hôpital's rule, up to five times)
    let { top, bottom } = M.splitFraction(a);
    if (bottom.length) {
      let f = S(mul(top.length ? top : [Q(1)])), g = S(mul(bottom));
      for (let k = 0; k < 5; k++) {
        const fv = valueAt(f, x, to, side), gv = valueAt(g, x, to, side);
        const zz = Math.abs(fv) < 1e-12 && Math.abs(gv) < 1e-12, ii = !isFinite(fv) && !isFinite(gv);
        if (!(zz || ii)) {
          if (Math.abs(gv) < 1e-12) break;
          let r = null;
          try { r = S(mul([substituteLimit(f, x, to), pow(substituteLimit(g, x, to), Q(-1))])); } catch (e) { break; }
          const rv = evalN(r, {});
          if (isFinite(rv)) { steps.push(step('Substitute $' + x + ' = ' + tex(to) + '$', tex(r))); return { result: r, steps, value: rv }; }
          break;
        }
        steps.push(step('This is the form $' + (zz ? '\\frac{0}{0}' : '\\frac{\\infty}{\\infty}') + '$: use L\'Hôpital\'s rule, the derivatives of the top and the bottom', '\\lim \\frac{' + tex(tidy(d(f, x), x)) + '}{' + tex(tidy(d(g, x), x)) + '}'));
        f = tidy(d(f, x), x); g = tidy(d(g, x), x);
      }
    }
    // (else: the values close to it, from the side asked for)
    const fx = M.compile(a, x);
    const approach = (s) => {
      let last = NaN;
      const vals = [];
      for (let k = 2; k <= 9; k++) {
        const h = Math.pow(10, -k);
        const t = atInf ? toV * Math.pow(10, k) * Math.sign(toV) * Math.sign(toV) : toV + s * h;
        const v = fx(atInf ? (toV > 0 ? Math.pow(10, k) : -Math.pow(10, k)) : t);
        vals.push(v);
        last = v;
      }
      const tail = vals.slice(-3);
      if (tail.every((v) => v > 1e8)) return Infinity;
      if (tail.every((v) => v < -1e8)) return -Infinity;
      if (tail.every(isFinite) && Math.abs(tail[2] - tail[1]) < 1e-6 * Math.max(1, Math.abs(tail[2]))) return Math.abs(tail[2]) < 1e-6 && Math.abs(tail[2]) < Math.abs(tail[0]) ? 0 : tail[2];
      return NaN;
    };
    const l = atInf ? approach(1) : approach(-1), r = atInf ? l : approach(1);
    let v;
    if (side < 0) v = l; else if (side > 0) v = r;
    else if ((isNaN(l) && isNaN(r)) || (isFinite(l) && isFinite(r) && Math.abs(l - r) > 1e-5 * Math.max(1, Math.abs(l))) || (!isFinite(l) && !isNaN(l) && l !== r)) {
      steps.push(step('From the left it goes to ' + side_(l) + ', from the right to ' + side_(r) + ': the limit does not exist', ''));
      return { result: null, steps, none: 'The limit does not exist', left: l, right: r };
    } else v = isNaN(l) ? r : l;
    if (isNaN(v)) return { result: null, steps, none: 'The limit does not exist' };
    steps.push(step('The values close to $' + x + ' = ' + tex(to) + '$ approach', M.realText(v)));
    const exact = guessExact(v);
    return { result: exact || R(v), steps, value: v, numeric: !exact };
  }
  const side_ = (v) => '$' + M.realText(v) + '$';
  function hasZeroOverZero(a, x, to) {
    const { top, bottom } = M.splitFraction(a);
    if (!bottom.length) return false;
    const g = evalN(S(substitute(S(mul(bottom)), x, to)), {});
    return Math.abs(g) < 1e-12;
  }
  /** Its value at `to` — at ±∞, ±∞ itself where it keeps growing (ln x does, slowly). */
  function valueAt(a, x, to, side) {
    const t = evalN(to, {});
    const f = M.compile(a, x);
    if (!isFinite(t)) {
      const s = t > 0 ? 1 : -1, v6 = f(s * 1e6), v12 = f(s * 1e12);
      if (Math.abs(v12) > 1e9 || (Math.abs(v12) > 1.5 * Math.abs(v6) && Math.abs(v12) > 10)) return v12 > 0 ? Infinity : -Infinity;
      return Math.abs(v12) < 1e-9 ? 0 : v12;
    }
    try { return evalN(S(substitute(a, x, to)), {}); } catch (e) { const near = f(t + (side || 1) * 1e-12); return Math.abs(near) > 1e9 ? (near > 0 ? Infinity : -Infinity) : near; }
  }
  function substituteLimit(a, x, to) {
    if (!isFinite(evalN(to, {}))) { const v = valueAt(a, x, to); return isFinite(v) ? R(v) : (v > 0 ? M.INF : S(mul([Q(-1), M.INF]))); }
    return S(substitute(a, x, to));
  }
  /** A decimal recognised as a simple exact number (a fraction with a small bottom). */
  function guessExact(v) {
    if (!isFinite(v)) return v > 0 ? M.INF : S(mul([Q(-1), M.INF]));
    for (let den = 1; den <= 24; den++) { const n = Math.round(v * den); if (Math.abs(n / den - v) < 1e-9) return Q(n, den); }
    for (const [c, node] of [[Math.PI, PI], [Math.E, E]]) for (let den = 1; den <= 12; den++) { const n = Math.round(v / c * den); if (n && Math.abs(n * c / den - v) < 1e-9) return S(mul([Q(n, den), node])); }
    return null;
  }

  Object.assign(M, { d, derivative, integrate, integral, limit, simpson, tidy });
})(typeof self !== 'undefined' ? self : globalThis);
