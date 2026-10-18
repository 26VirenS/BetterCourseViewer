/* Simpl's solver, the algebra: factoring, solving equations (linear, quadratic, polynomial, with fractions, roots,
 * powers, logarithms, absolute values, trig, else numerically), inequalities and systems — each with its steps. */
(function (root) {
  'use strict';
  const M = root.SimplMath;
  const { Q, R, sym, add, mul, pow, fn, eq, S, expand, tex, MathError, isConst, isNum, isZero, isOne, isInt, numVal, neg,
    addN, mulN, divN, polyCoeffs, numericPoly, polyFrom, polyEval, polyDiv, polyGcd, freeOf, evalN, bgcd, babs, intRoot } = M;

  const step = (text, t) => ({ text, tex: t });
  const sub = (a, b) => S(add([a, mul([Q(-1), b])]));

  // ---------------------------------------------------------------- polynomials of one letter

  function lcmBig(a, b) { return a / bgcd(a, b) * b; }
  /** Rational coefficients made whole (and their common factor taken out): [2/3, 1] → [2, 3]. */
  function integerCoeffs(cs) {
    let l = 1n;
    for (const c of cs) if (isNum(c)) l = lcmBig(l, c.d);
    const ints = cs.map((c) => c.n * (l / c.d));
    let g = 0n;
    for (const n of ints) g = bgcd(g, n);
    return ints.map((n) => g ? n / g : n);
  }
  function divisors(n) {
    n = babs(n);
    if (n === 0n) return [];
    if (n > 10000000n) return null;
    const out = [];
    for (let i = 1n; i * i <= n; i++) if (n % i === 0n) { out.push(i); if (i * i !== n) out.push(n / i); }
    return out.sort((a, b) => (a < b ? -1 : 1));
  }
  /** The rational roots of a numeric polynomial (each once), by the rational root theorem. */
  function rationalRoots(cs) {
    if (!cs.every(isNum)) return [];
    let ints = integerCoeffs(cs);
    const roots = [];
    // (x itself a factor: 0 a root)
    while (ints.length > 1 && ints[0] === 0n) { if (!roots.some((r) => isZero(r))) roots.push(Q(0)); ints = ints.slice(1); }
    if (ints.length < 2) return roots;
    const ps = divisors(ints[0]), qs = divisors(ints[ints.length - 1]);
    if (!ps || !qs) return roots;
    const poly = ints.map((n) => Q(n));
    for (const p of ps) for (const q of qs) for (const s of [1n, -1n]) {
      const r = Q(s * p, q);
      if (roots.some((x) => M.equal(x, r))) continue;
      if (isZero(polyEval(poly, r))) roots.push(r);
    }
    return roots.sort(M.cmpN);
  }
  /** All the roots, as numbers (Durand–Kerner, then each polished): [{re, im}]. */
  function numericRoots(cs) {
    const a = cs.map(numVal);
    while (a.length > 1 && Math.abs(a[a.length - 1]) < 1e-300) a.pop();
    const n = a.length - 1;
    if (n < 1) return [];
    const lead = a[n];
    const c = a.map((v) => v / lead);
    let z = [];
    for (let i = 0; i < n; i++) { const ang = 2 * Math.PI * i / n + 0.4; const r = 1 + Math.max(...c.slice(0, n).map(Math.abs)); z.push([r * Math.cos(ang), r * Math.sin(ang)]); }
    const cmul = (p, q) => [p[0] * q[0] - p[1] * q[1], p[0] * q[1] + p[1] * q[0]];
    const cdiv = (p, q) => { const d = q[0] * q[0] + q[1] * q[1]; return [(p[0] * q[0] + p[1] * q[1]) / d, (p[1] * q[0] - p[0] * q[1]) / d]; };
    const ev = (x) => { let r = [1, 0]; for (let k = n - 1; k >= 0; k--) r = [r[0] * x[0] - r[1] * x[1] + c[k], r[0] * x[1] + r[1] * x[0]]; return r; };
    for (let it = 0; it < 500; it++) {
      let moved = 0;
      z = z.map((zi, i) => {
        let d = [1, 0];
        z.forEach((zj, j) => { if (j !== i) d = cmul(d, [zi[0] - zj[0], zi[1] - zj[1]]); });
        const step = cdiv(ev(zi), d);
        moved = Math.max(moved, Math.hypot(step[0], step[1]));
        return [zi[0] - step[0], zi[1] - step[1]];
      });
      if (moved < 1e-14) break;
    }
    return z.map(([re, im]) => ({ re: Math.abs(re) < 1e-12 ? 0 : re, im: Math.abs(im) < 1e-9 * Math.max(1, Math.abs(re)) ? 0 : im }));
  }

  // ---------------------------------------------------------------- factoring

  /** The greatest common factor of a sum's terms: the numbers' gcd (its sign the first term's) and each letter's
   * lowest power. */
  function commonFactor(terms) {
    let g = null, sign = 1;
    const lows = new Map();
    terms.forEach((t, i) => {
      const { c, rest } = M.stripCoeff(t);
      if (isNum(c)) g = g === null ? Q(babs(c.n), c.d) : Q(bgcd(g.n * c.d, c.n * g.d), g.d * c.d);
      else g = Q(1);
      if (i === 0 && numVal(c) < 0) sign = -1;
      const fs = isOne(rest) ? [] : rest.t === 'mul' ? rest.a : [rest];
      const here = new Map();
      for (const f of fs) { const { b, e } = M.basePow(f); if (isConst(e) && numVal(e) > 0) here.set(M.key(b), { b, e }); }
      if (i === 0) for (const [k, v] of here) lows.set(k, v);
      else for (const k of [...lows.keys()]) {
        if (!here.has(k)) lows.delete(k);
        else if (M.cmpN(here.get(k).e, lows.get(k).e) < 0) lows.set(k, here.get(k));
      }
    });
    // (a gcd of fractions: of their tops over the lcm of their bottoms)
    if (g && g.d !== 1n) {
      let num = 0n, den = 1n;
      for (const t of terms) { const { c } = M.stripCoeff(t); if (isNum(c)) { num = bgcd(num, c.n); den = lcmBig(den, c.d); } }
      g = Q(num, den);
    }
    const parts = [];
    if (g && !(isOne(g) && sign > 0)) parts.push(sign < 0 ? neg(g) : g);
    for (const { b, e } of lows.values()) parts.push(pow(b, e));
    return parts.length ? S(mul(parts)) : Q(1);
  }

  /** Factored, with its steps: a common factor, then (in one letter) its rational roots, a quadratic's own pair, a
   * quadratic in x², and two letters of the same degree. */
  function factor(a, x) {
    const steps = [];
    a = expand(a);
    if (a.t !== 'add') return { result: S(a), steps, changed: false };
    const gcf = commonFactor(a.a);
    let rest = a;
    const outside = [];
    if (!isOne(gcf)) {
      rest = expand(S(mul([a, pow(gcf, Q(-1))])));
      steps.push(step('Factor out the greatest common factor, ' + '$' + tex(gcf) + '$', tex(mul([gcf, rest]))));
      outside.push(gcf);
    }
    const vars = M.variables(rest);
    let inner = null;
    if (vars.length === 2) inner = factorHomogeneous(rest, vars, steps);
    if (!inner && vars.length >= 1) {
      const v = x && vars.includes(x) ? x : vars[0];
      const cs = polyCoeffs(rest, v);
      if (cs && numericPoly(cs) && cs.length > 2) inner = factorPoly(cs, v, steps);
    }
    const parts = [...outside, ...(inner || [rest])];
    const result = parts.length === 1 ? parts[0] : mul(parts);
    return { result, steps, changed: parts.length > 1 || !M.equal(S(result), S(a)) };
  }

  /** A numeric polynomial in v as its factors (each a node), the steps said as they are found. */
  function factorPoly(cs, v, steps) {
    const X = sym(v);
    const out = [];
    let poly = cs.map((c) => c);
    // (x^k)
    let k = 0;
    while (poly.length > 1 && isZero(poly[0])) { poly = poly.slice(1); k++; }
    if (k) out.push(k === 1 ? X : pow(X, Q(k)));
    if (poly.length === 3) {
      const quad = factorQuadratic(poly, v, steps);
      if (quad) return [...out, ...quad];
      return [...out, polyFrom(poly, v)];
    }
    const roots = rationalRoots(poly);
    if (roots.length && poly.length > 3) {
      steps.push(step('By the rational root theorem, try the factors of the constant over the factors of the leading coefficient. ' +
        roots.map((r) => '$' + v + ' = ' + tex(r) + '$').join(', ') + ' make' + (roots.length === 1 ? 's' : '') + ' it zero', polyFrom(poly, v) ? tex(polyFrom(poly, v)) : ''));
    }
    for (const r of roots) {
      // (each root as many times as it divides)
      for (;;) {
        const lin = isInt(r) ? [neg(r), Q(1)] : [Q(-r.n), Q(r.d)];
        const { q, r: rem } = polyDiv(poly, lin);
        if (!rem.every(isZero)) break;
        out.push(polyFrom(lin, v));
        poly = q;
        if (poly.length > 1) steps.push(step('Divide by $' + tex(polyFrom(lin, v)) + '$', tex(S(mul([...out.slice(-1), polyFrom(poly, v)])))));
        if (poly.length <= 1) break;
      }
    }
    if (poly.length === 3) {
      const quad = factorQuadratic(poly, v, steps);
      if (quad) out.push(...quad); else out.push(polyFrom(poly, v));
    } else if (poly.length === 5 && isZero(poly[1]) && isZero(poly[3])) {
      // (a quadratic in v²: x⁴ + 5x² + 6 = (x² + 2)(x² + 3))
      const inner = factorQuadratic([poly[0], poly[2], poly[4]], v + '²', []);
      if (inner) {
        const back = inner.map((f) => S(M.substitute(f, v + '²', pow(X, Q(2)))));
        steps.push(step('Treat it as a quadratic in $' + v + '^2$', tex(mul(back))));
        // (and each part again: x⁴ − 1 = (x² + 1)(x − 1)(x + 1))
        for (const f of back) { const cs2 = polyCoeffs(f, v); const again = cs2 && cs2.length === 3 ? factorQuadratic(cs2, v, []) : null; out.push(...(again || [f])); }
      } else out.push(polyFrom(poly, v));
    } else if (poly.length > 1) out.push(polyFrom(poly, v));
    else if (!isOne(poly[0])) out.unshift(poly[0]);
    return out;
  }

  /** ax² + bx + c as two factors over the rationals (the a·c method, said), or null. */
  function factorQuadratic(cs, v, steps) {
    const [c, b, a] = cs;
    if (!isNum(a) || !isNum(b) || !isNum(c)) return null;
    const disc = addN(mulN(b, b), neg(mulN(Q(4), mulN(a, c))));
    if (numVal(disc) < 0) return null;
    const rt = isNum(disc) ? [intRoot(disc.n, 2), intRoot(disc.d, 2)] : [null, null];
    if (rt[0] === null || rt[1] === null) return null;
    const X = sym(v);
    const sq = Q(rt[0], rt[1]);
    const r1 = divN(addN(neg(b), sq), mulN(Q(2), a)), r2 = divN(addN(neg(b), neg(sq)), mulN(Q(2), a));
    // (each root p/q as the factor (qx − p); what is left over, a number in front)
    const f1 = [Q(-r1.n), Q(r1.d)], f2 = [Q(-r2.n), Q(r2.d)];
    const lead = divN(a, Q(r1.d * r2.d));
    const ints = isInt(a) && isInt(b) && isInt(c);
    if (ints && steps && !isZero(c)) {
      const ac = mulN(a, c);
      // (two numbers that multiply to a·c and add to b)
      const m = Q(-r1.n * (a.n / r1.d)), n = Q(-r2.n * (a.n / r2.d));
      if (isOne(a)) steps.push(step('Find two numbers that multiply to $' + tex(c) + '$ and add to $' + tex(b) + '$: $' + tex(neg(r1)) + '$ and $' + tex(neg(r2)) + '$', tex(mul([polyFrom(f1, v), polyFrom(f2, v)]))));
      else steps.push(step('Find two numbers that multiply to $a \\cdot c = ' + tex(ac) + '$ and add to $' + tex(b) + '$: $' + tex(m) + '$ and $' + tex(n) + '$. Split the middle term and group', tex(mul([polyFrom(f1, v), polyFrom(f2, v)]))));
    }
    const out = [];
    if (!isOne(lead)) out.push(lead);
    if (M.equal(r1, r2)) out.push(pow(polyFrom(f1, v), Q(2)));
    else out.push(polyFrom(f1, v), polyFrom(f2, v));
    return out;
  }

  /** Two letters, every term of the same degree (x² − y², x³ + y³): factored as one letter with the other set to 1, and
   * each factor made whole again. */
  function factorHomogeneous(a, vars, steps) {
    const terms = a.t === 'add' ? a.a : [a];
    const deg = M.degreeOf(terms[0]);
    if (!Number.isInteger(deg) || terms.some((t) => M.degreeOf(t) !== deg)) return null;
    const [u, w] = vars;
    const one = S(M.substitute(a, w, Q(1)));
    const cs = polyCoeffs(one, u);
    if (!cs || !numericPoly(cs)) return null;
    const fs = factorPoly(cs, u, []);
    if (fs.length < 2 && !(fs[0] && fs[0].t === 'pow')) return null;
    const back = fs.map((f) => {
      if (isConst(f)) return f;
      const base = f.t === 'pow' ? f.b : f;
      const c2 = polyCoeffs(base, u);
      const d = c2.length - 1;
      const h = S(add(c2.map((c, k) => mul([c, pow(sym(u), Q(k)), pow(sym(w), Q(d - k))]))));
      return f.t === 'pow' ? pow(h, f.e) : h;
    });
    const out = mul(back);
    steps.push(step(deg === 2 ? 'Factor it as a product of two binomials' : 'Factor it by its pattern', tex(out)));
    return back;
  }

  // ---------------------------------------------------------------- fractions

  /** One fraction: its top and bottom as polynomials in x (the bottom the least common denominator of its parts). */
  function together(a, x) {
    a = S(a);
    const dens = new Map();
    const walk = (t) => {
      const fs = t.t === 'mul' ? t.a : [t];
      for (const f of fs) {
        if (f.t === 'pow' && isInt(f.e) && f.e.n < 0n && !freeOf(f.b, x)) {
          const k = M.key(f.b), e = -f.e.n;
          if (!dens.has(k) || dens.get(k).e < e) dens.set(k, { b: f.b, e });
        }
      }
    };
    for (const t of a.t === 'add' ? a.a : [a]) walk(t);
    if (!dens.size) return { num: a, den: Q(1), lcd: null };
    const lcdParts = [...dens.values()].map(({ b, e }) => pow(b, Q(e)));
    const lcd = S(mul(lcdParts));
    // (each term times the denominator, cancelled as it is, then multiplied out)
    const num = expand(add((a.t === 'add' ? a.a : [a]).map((t) => S(mul([t, ...lcdParts])))));
    return { num, den: lcd, lcd };
  }

  /** A fraction of polynomials in x, its common factors cancelled ((x² − 1)/(x − 1) = x + 1). */
  function cancel(a, x) {
    const { num, den } = together(a, x);
    if (isOne(den)) return a;
    const n = polyCoeffs(num, x), d = polyCoeffs(expand(den), x);
    if (!n || !d || !numericPoly(n) || !numericPoly(d)) return S(mul([num, pow(den, Q(-1))]));
    const g = polyGcd(n, d);
    if (g.length <= 1) return S(mul([polyFrom(n, x), pow(den, Q(-1))]));
    const nq = polyDiv(n, g).q, dq = polyDiv(d, g).q;
    const top = polyFrom(nq, x), bottom = polyFrom(dq, x);
    if (dq.length === 1) return S(mul([top, pow(dq[0], Q(-1))]));
    const bf = factor(bottom, x).result;
    return S(mul([top, pow(bf, Q(-1))]));
  }

  // ---------------------------------------------------------------- solving equations

  /** Solved for x: {solutions (nodes), steps, numeric (true where only decimals were found), general (words for every
   * solution, trig's), none (why there are none)}. */
  function solve(rel, x) {
    const steps = [];
    let l = S(rel.l), r = S(rel.r);
    const start = tex(eq(l, r));
    const outcome = { solutions: [], steps, numeric: false, general: null, none: null, complex: [] };
    if (freeOf(l, x) && freeOf(r, x)) {
      const d = S(sub(l, r));
      if (isZero(d)) { outcome.none = null; outcome.all = true; steps.push(step('Both sides are always equal', tex(eq(l, r)))); return outcome; }
      if (isConst(d)) { outcome.none = 'The two sides are never equal'; steps.push(step('The two sides are never equal', tex(eq(l, r)) + '\\;\\text{is false}')); return outcome; }
    }
    // (special shapes first, while the equation is as it was typed: |u| = c, √u = c, b^u = c, log u = c, sin u = c — their
    //  steps kept only if one of them is the way)
    const tried = [];
    const special = solveShape(l, r, x, tried);
    if (special) { steps.push(...tried); finish(outcome, special, rel, x); return outcome; }

    let f = S(sub(l, r));
    if (!isZero(r)) steps.push(step('Move everything to the left side', tex(eq(f, Q(0)))));
    // (fractions: both sides times the least common denominator)
    const { num, lcd } = together(f, x);
    let excluded = [];
    if (lcd) {
      steps.push(step('Multiply both sides by the least common denominator, $' + tex(lcd) + '$', tex(eq(num, Q(0)))));
      excluded = lcd;
      f = num;
    } else f = expand(f);

    const cs = polyCoeffs(f, x);
    if (cs) {
      const deg = cs.length - 1;
      if (deg === 0) {
        if (isZero(cs[0])) { outcome.all = true; steps.push(step('Every number is a solution', '0 = 0')); }
        else { outcome.none = 'No number makes the two sides equal'; steps.push(step('This is never true', tex(eq(cs[0], Q(0))))); }
        return outcome;
      }
      if (deg === 1) { outcome.solutions = solveLinear(rel, cs, x, steps, lcd); }
      else if (numericPoly(cs)) solvePolynomial(cs, x, steps, outcome);
      else if (deg === 2) { outcome.solutions = quadraticFormula(cs, x, steps, outcome); }
      else throw new MathError('This equation has letters other than ' + x + ' in a polynomial of degree ' + deg);
    } else {
      numericSolve(f, x, steps, outcome);
    }
    if (lcd) {
      const bad = outcome.solutions.filter((s) => { const v = evalN(S(M.substitute(lcd, x, s)), {}); return Math.abs(v) < 1e-12; });
      if (bad.length) {
        steps.push(step('Exclude ' + bad.map((b) => '$' + x + ' = ' + tex(b) + '$').join(' and ') + ': ' + (bad.length > 1 ? 'they make' : 'it makes') + ' a denominator zero', ''));
        outcome.solutions = outcome.solutions.filter((s) => !bad.includes(s));
      }
    }
    finish(outcome, null, rel, x);
    return outcome;
  }

  /** The solutions checked in the equation as it was typed (a squared root's, a log's can bring in ones that are not). */
  function finish(outcome, special, rel, x) {
    if (special) Object.assign(outcome, special);
    const keep = [];
    // (other letters given any values for the check: y = −x³ checked at x = 0.37, 1.13 …)
    const others = M.variables(rel).filter((v) => v !== x);
    const env = {};
    others.forEach((v, i) => { env[v] = 0.37 + 0.76 * i; });
    for (const s of outcome.solutions) {
      const lv = evalN(M.substitute(rel.l, x, s), env), rv = evalN(M.substitute(rel.r, x, s), env);
      if (others.length && (isNaN(lv) || isNaN(rv))) { keep.push(s); continue; }
      const ok = isFinite(lv) && isFinite(rv) && Math.abs(lv - rv) <= 1e-7 * Math.max(1, Math.abs(lv), Math.abs(rv));
      if (ok || (!isFinite(lv) && !isFinite(rv))) keep.push(s);
      else if (!outcome.numeric) outcome.steps.push(step('Check $' + x + ' = ' + tex(s) + '$ in the original equation: it does not work, so it is not a solution', ''));
    }
    const seen = new Set();
    outcome.solutions = keep.filter((s) => { const k = outcome.numeric ? numVal(s).toFixed(9) : M.key(S(s)); if (seen.has(k)) return false; seen.add(k); return true; })
      .map((s) => (M.variables(s).length ? expand(s) : S(s))).sort((a, b) => evalN(a, {}) - evalN(b, {}));
    if (!outcome.solutions.length && !outcome.none && !outcome.all && !outcome.general) outcome.none = outcome.complex && outcome.complex.length ? 'There are no real solutions' : 'There are no solutions';
  }

  function solveLinear(rel, cs, x, steps, lcd) {
    const X = sym(x);
    const [b, a] = cs;
    if (!lcd) {
      // (as a student writes it: the x terms to the left, the numbers to the right, then divide)
      const L = polyCoeffs(rel.l, x), Rt = polyCoeffs(rel.r, x);
      if (L && Rt && L.length <= 2 && Rt.length <= 2) {
        const a1 = L[1] || Q(0), b1 = L[0], a2 = Rt[1] || Q(0), b2 = Rt[0];
        let la = a1, lb = b1, ra = a2, rb = b2;
        const side = (ca, cb) => S(add([mul([ca, X]), cb]));
        if (!isZero(ra)) {
          const term = S(mul([ra, X]));
          la = S(sub(la, ra)); ra = Q(0);
          steps.push(step((M.isNegative(term) ? 'Add $' + tex(S(mul([Q(-1), term]))) : 'Subtract $' + tex(term)) + '$ ' + (M.isNegative(term) ? 'to' : 'from') + ' both sides', tex(eq(side(la, lb), rb))));
        }
        if (!isZero(lb)) {
          const was = lb;
          rb = S(sub(rb, lb)); lb = Q(0);
          steps.push(step((M.isNegative(was) ? 'Add $' + tex(S(mul([Q(-1), was]))) : 'Subtract $' + tex(was)) + '$ ' + (M.isNegative(was) ? 'to' : 'from') + ' both sides', tex(eq(S(mul([la, X])), rb))));
        }
        if (isZero(la)) return [];
        const sol = S(mul([rb, pow(la, Q(-1))]));
        if (!isOne(la)) steps.push(step('Divide both sides by $' + tex(la) + '$', tex(eq(X, sol))));
        return [sol];
      }
    }
    // (b + a·x = 0)
    steps.push(step('Subtract $' + tex(b) + '$ from both sides', tex(eq(S(mul([a, X])), S(neg2(b))))));
    const sol = S(mul([neg2(b), pow(a, Q(-1))]));
    if (!isOne(a)) steps.push(step('Divide both sides by $' + tex(a) + '$', tex(eq(X, sol))));
    return [sol];
  }
  const neg2 = (a) => S(mul([Q(-1), a]));

  function solvePolynomial(cs, x, steps, outcome) {
    const X = sym(x);
    const deg = cs.length - 1;
    // (xⁿ = c: a root of both sides)
    if (deg >= 2 && cs.slice(1, -1).every(isZero)) {
      const c = S(divN(neg(cs[0]), cs[deg]));
      steps.push(step('Isolate $' + tex(pow(X, Q(deg))) + '$', tex(eq(pow(X, Q(deg)), c))));
      const root1 = S(pow(c, Q(1, deg)));
      if (deg % 2 === 0) {
        if (numVal(c) < 0) {
          steps.push(step('An even power is never negative', ''));
          if (deg === 2) { const im = S(pow(neg(c), Q(1, 2))); outcome.complex = [{ re: Q(0), im }, { re: Q(0), im: neg2(im) }]; }
          return;
        }
        steps.push(step('Take the ' + (deg === 2 ? 'square' : deg + 'th') + ' root of both sides (both signs)', tex(X) + ' = \\pm ' + tex(root1)));
        outcome.solutions = [root1, neg2(root1)];
      } else {
        steps.push(step('Take the ' + (deg === 3 ? 'cube' : deg + 'th') + ' root of both sides', tex(eq(X, root1))));
        outcome.solutions = [root1];
      }
      return;
    }
    if (deg === 2) {
      const quad = factorQuadratic(cs, x, null);
      if (quad && !isZero(cs[0])) {
        const shown = S(mul(quad));
        steps.push(step('Factor the left side', tex(eq(shown, Q(0)))));
        const fs = quad.filter((f) => !isConst(f)).map((f) => (f.t === 'pow' ? f.b : f));
        steps.push(step('Set each factor equal to zero', fs.map((f) => tex(eq(f, Q(0)))).join('\\quad\\text{or}\\quad ')));
        outcome.solutions = fs.map((f) => { const c = polyCoeffs(f, x); return S(divN(neg(c[0]), c[1])); });
        return;
      }
      if (isZero(cs[0])) {
        const shown = S(mul([X, polyFrom([cs[1], cs[2]], x)]));
        steps.push(step('Factor out $' + x + '$', tex(eq(shown, Q(0)))));
        outcome.solutions = [Q(0), S(divN(neg(cs[1]), cs[2]))];
        return;
      }
      outcome.solutions = quadraticFormula(cs, x, steps, outcome);
      return;
    }
    // (higher: the factors found, then each solved)
    const fsteps = [];
    const factors = factorPoly(cs, x, fsteps);
    steps.push(...fsteps);
    const parts = factors.filter((f) => !isConst(f)).map((f) => (f.t === 'pow' ? f.b : f));
    if (parts.length > 1) steps.push(step('Set each factor equal to zero', parts.map((f) => tex(eq(f, Q(0)))).join('\\quad\\text{or}\\quad ')));
    for (const p of parts) {
      const pc = polyCoeffs(p, x);
      if (pc.length === 2) outcome.solutions.push(S(divN(neg(pc[0]), pc[1])));
      else if (pc.length === 3) {
        const sub = { solutions: [], complex: [] };
        const sols = quadraticFormula(pc, x, parts.length > 1 ? steps : steps, sub);
        outcome.solutions.push(...sols);
        outcome.complex.push(...(sub.complex || []));
      } else {
        // (no rational roots left: found as numbers)
        const roots = numericRoots(pc);
        const real = roots.filter((z) => z.im === 0).map((z) => R(polish(pc, z.re)));
        steps.push(step('$' + tex(p) + ' = 0$ has no rational roots; its real roots, found numerically', real.map((v) => x + ' \\approx ' + M.realText(v.v)).join(',\\; ') || '\\text{none}'));
        outcome.solutions.push(...real);
        outcome.complex.push(...roots.filter((z) => z.im !== 0).map((z) => ({ re: R(z.re), im: R(z.im) })));
        outcome.numericPart = true;
      }
    }
  }
  function polish(cs, v) {
    const f = (t) => cs.reduceRight((s, c) => s * t + numVal(c), 0);
    const df = (t) => cs.slice(1).reduceRight((s, c, i) => s * t + numVal(c) * (i + 1), 0);
    for (let i = 0; i < 30; i++) { const d = df(v); if (!d) break; const nv = v - f(v) / d; if (Math.abs(nv - v) < 1e-15) return nv; v = nv; }
    return v;
  }

  /** The quadratic formula, worked: a, b and c, the discriminant, and the two answers. */
  function quadraticFormula(cs, x, steps, outcome) {
    const [c, b, a] = cs;
    steps.push(step('Use the quadratic formula with $a = ' + tex(a) + '$, $b = ' + tex(b) + '$, $c = ' + tex(c) + '$', x + ' = \\frac{-b \\pm \\sqrt{b^2 - 4ac}}{2a}'));
    const disc = S(add([pow(b, Q(2)), mul([Q(-4), a, c])]));
    steps.push(step('Work out the discriminant', 'b^2 - 4ac = ' + tex(disc)));
    const two_a = S(mul([Q(2), a]));
    const dv = evalN(disc, {});
    if (isConst(disc) && dv < 0) {
      const im = S(mul([pow(neg(disc), Q(1, 2)), pow(two_a, Q(-1))]));
      const re = S(mul([neg2(b), pow(two_a, Q(-1))]));
      steps.push(step('The discriminant is negative, so there are no real solutions. The complex ones:', x + ' = ' + tex(re) + ' \\pm ' + tex(im) + 'i'));
      if (outcome) outcome.complex = [{ re, im }, { re, im: neg2(im) }];
      return [];
    }
    const root = S(pow(disc, Q(1, 2)));
    const s1 = S(mul([add([neg2(b), root]), pow(two_a, Q(-1))]));
    const s2 = S(mul([add([neg2(b), neg2(root)]), pow(two_a, Q(-1))]));
    const shown = '\\frac{' + tex(neg2(b)) + ' \\pm ' + tex(root) + '}{' + tex(two_a) + '}';
    steps.push(step('Put them in', x + ' = ' + shown));
    if (isZero(disc)) { steps.push(step('The discriminant is zero: one repeated solution', tex(eq(sym(x), s1)))); return [s1]; }
    const nice = [splitRootFraction(b, root, two_a, 1), splitRootFraction(b, root, two_a, -1)];
    steps.push(step('Simplify', x + ' = ' + tex(nice[0]) + ',\\; ' + x + ' = ' + tex(nice[1])));
    return nice;
  }
  /** (−b ± √d)/(2a) as one fraction, its common factor cancelled ((1 + √5)/2, −1 + √2), when the numbers are rational;
   * else −b/(2a) ± √d/(2a). */
  function splitRootFraction(b, root, twoA, sign) {
    if (isConst(root)) return S(mul([add([neg2(b), mul([Q(sign), root])]), pow(twoA, Q(-1))]));
    const { c, rest } = M.stripCoeff(root);
    if (isNum(b) && isNum(c) && isNum(twoA)) {
      const L = lcmBig(lcmBig(b.d, c.d), twoA.d);
      let nb = -b.n * (L / b.d), nc = c.n * (L / c.d), nd = twoA.n * (L / twoA.d);
      const g = bgcd(bgcd(nb, nc), nd) || 1n;
      nb /= g; nc /= g; nd /= g;
      if (nd < 0n) { nb = -nb; nc = -nc; nd = -nd; }
      const top = S(add([Q(nb), mul([Q(BigInt(sign) * nc), rest])]));
      return nd === 1n ? top : mul([Q(1, nd), top]);
    }
    return S(add([mul([neg2(b), pow(twoA, Q(-1))]), mul([Q(sign), root, pow(twoA, Q(-1))])]));
  }

  /** The shapes solved by undoing them: the one x-term with what is around it taken off, its inverse taken. */
  function solveShape(l, r, x, steps) {
    // (x on one side only)
    if (freeOf(l, x) && !freeOf(r, x)) { const t = l; l = r; r = t; }
    if (!freeOf(r, x)) return solveRadical(l, r, x, steps);
    // (peel off what is added or multiplied around the one part holding x)
    let lhs = l, rhs = r;
    for (let guard = 0; guard < 8; guard++) {
      if (lhs.t === 'add') {
        const withX = lhs.a.filter((t) => !freeOf(t, x));
        if (withX.length !== 1) return null;
        const others = lhs.a.filter((t) => freeOf(t, x));
        const o = S(add(others));
        lhs = withX[0];
        rhs = S(sub(rhs, o));
        steps.push(step((M.isNegative(o) ? 'Add $' + tex(neg2(o)) + '$ to' : 'Subtract $' + tex(o) + '$ from') + ' both sides', tex(eq(lhs, rhs))));
        continue;
      }
      if (lhs.t === 'mul') {
        const withX = lhs.a.filter((t) => !freeOf(t, x));
        if (withX.length !== 1) return null;
        const c = S(mul(lhs.a.filter((t) => freeOf(t, x))));
        lhs = withX[0];
        rhs = S(mul([rhs, pow(c, Q(-1))]));
        steps.push(step('Divide both sides by $' + tex(c) + '$', tex(eq(lhs, rhs))));
        continue;
      }
      break;
    }
    if (lhs.t === 'pow' && freeOf(lhs.e, x) && isConst(lhs.e) && !isInt(lhs.e)) {
      // (√u = c: square both sides — c below 0, none)
      if (numVal(lhs.e) > 0 && isNum(lhs.e) && lhs.e.n === 1n) {
        const k = Number(lhs.e.d);
        if (k % 2 === 0 && isConst(rhs) && numVal(rhs) < 0) return { solutions: [], none: 'A ' + (k === 2 ? 'square' : 'even') + ' root is never negative', steps };
        const v = S(pow(rhs, Q(k)));
        steps.push(step((k === 2 ? 'Square' : 'Raise to the power ' + k + ' on') + ' both sides', tex(eq(lhs.b, v))));
        return solveInner(lhs.b, v, x, steps);
      }
      return null;
    }
    if (lhs.t === 'pow' && freeOf(lhs.b, x) && !freeOf(lhs.e, x)) {
      // (b^u = c: a logarithm of both sides)
      if (isConst(rhs) && numVal(rhs) <= 0) return { solutions: [], none: 'A power of a positive number is never zero or negative', steps };
      const base = lhs.b;
      const v = base.t === 'sym' && base.n === 'e' ? S(fn('ln', rhs)) : S(fn('log', rhs, { base }));
      const shown = base.t === 'sym' && base.n === 'e' ? 'Take the natural log of both sides' : 'Take the logarithm base $' + tex(base) + '$ of both sides';
      // (log_b c, written as ln c / ln b where it is not a whole number)
      const exact = isConst(v) ? v : (base.t === 'sym' && base.n === 'e' ? v : S(mul([fn('ln', rhs), pow(fn('ln', base), Q(-1))])));
      steps.push(step(shown, tex(eq(lhs.e, exact))));
      return solveInner(lhs.e, exact, x, steps);
    }
    if (lhs.t === 'fn' && ['ln', 'log'].includes(lhs.n) && !freeOf(lhs.a, x)) {
      const base = lhs.n === 'ln' ? M.E : (lhs.base || Q(10));
      const v = S(pow(base, rhs));
      steps.push(step('Rewrite in exponential form', tex(eq(lhs.a, v))));
      return solveInner(lhs.a, v, x, steps);
    }
    if (lhs.t === 'fn' && lhs.n === 'abs') {
      if (isConst(rhs) && numVal(rhs) < 0) return { solutions: [], none: 'An absolute value is never negative', steps };
      steps.push(step('An absolute value equation splits in two', tex(eq(lhs.a, rhs)) + '\\quad\\text{or}\\quad ' + tex(eq(lhs.a, neg2(rhs)))));
      const a = solveInner(lhs.a, rhs, x, steps), b = isZero(rhs) ? { solutions: [] } : solveInner(lhs.a, neg2(rhs), x, steps);
      return { solutions: [...(a ? a.solutions : []), ...(b ? b.solutions : [])] };
    }
    if (lhs.t === 'fn' && ['sin', 'cos', 'tan'].includes(lhs.n) && isConst(rhs)) return solveTrig(lhs, rhs, x, steps);
    return null;
  }
  /** One square root with x under it (√(x + 3) = x − 3): it alone on one side, both sides squared. */
  function solveRadical(l, r, x, steps) {
    const f = S(sub(l, r));
    const terms = f.t === 'add' ? f.a : [f];
    const isRoot = (t) => { const fs = t.t === 'mul' ? t.a : [t]; return fs.some((g) => g.t === 'pow' && isNum(g.e) && g.e.n === 1n && g.e.d === 2n && !freeOf(g.b, x)); };
    const rad = terms.filter(isRoot);
    if (rad.length !== 1) return null;
    const rest = S(add(terms.filter((t) => !isRoot(t))));
    const lhs = rad[0], rhs = S(mul([Q(-1), rest]));
    steps.push(step('Get the square root alone on one side', tex(eq(lhs, rhs))));
    const sq = eq(expand(pow(lhs, Q(2))), expand(pow(rhs, Q(2))));
    steps.push(step('Square both sides', tex(sq)));
    const inner = solve(sq, x);
    steps.push(...inner.steps);
    return { solutions: inner.solutions, numeric: inner.numeric };
  }
  function solveInner(l, r, x, steps) {
    const sub = solve(eq(l, r), x);
    steps.push(...sub.steps);
    return { solutions: sub.solutions, complex: sub.complex, numeric: sub.numeric, none: sub.none };
  }

  /** sin u = k (and cos, tan): the solutions in one turn and every one there is. */
  function solveTrig(lhs, k, x, steps) {
    const v = numVal(k);
    const u = lhs.a;
    if ((lhs.n === 'sin' || lhs.n === 'cos') && Math.abs(v) > 1) return { solutions: [], none: lhs.n + ' is always between −1 and 1', steps };
    const inv = S(fn('a' + lhs.n, k));
    const n = sym('n');
    let principal, general;
    if (lhs.n === 'sin') {
      const second = S(add([M.PI, mul([Q(-1), inv])]));
      principal = M.equal(S(inv), second) ? [inv] : [inv, second];
      general = principal.map((p) => S(add([p, mul([Q(2), M.PI, n])])));
    } else if (lhs.n === 'cos') {
      const second = S(add([mul([Q(2), M.PI]), mul([Q(-1), inv])]));
      principal = isZero(inv) || M.equal(S(inv), M.PI) ? [inv] : [inv, second];
      general = principal.map((p) => S(add([p, mul([Q(2), M.PI, n])])));
    } else {
      principal = [inv];
      general = [S(add([inv, mul([M.PI, n])]))];
    }
    const period = lhs.n === 'tan' ? M.PI : S(mul([Q(2), M.PI]));
    steps.push(step('Use the inverse: ' + ({ sin: '$\\sin$ repeats every $2\\pi$', cos: '$\\cos$ repeats every $2\\pi$', tan: '$\\tan$ repeats every $\\pi$' })[lhs.n],
      principal.map((p) => tex(u) + ' = ' + tex(p) + ' + ' + tex(S(mul([period, n])))).join(',\\; ')));
    // (u is x itself, or linear in x: solved for x — each solution and how often it comes again)
    const uc = polyCoeffs(u, x);
    if (!uc || uc.length !== 2) return null;
    const every = S(mul([period, pow(uc[1], Q(-1))]));
    const base = principal.map((p) => expand(S(mul([add([p, mul([Q(-1), uc[0]])]), pow(uc[1], Q(-1))]))));
    const generalTex = base.map((b) => x + ' = ' + tex(b) + ' + ' + tex(S(mul([every, n])))).join(',\\; ') + ',\\; n \\in \\mathbb{Z}';
    if (!(isOne(uc[1]) && isZero(uc[0]))) steps.push(step('Solve for $' + x + '$', generalTex));
    // (the ones in [0, 2π))
    const inTurn = [];
    for (const b of base) for (let m = -12; m <= 12; m++) {
      const s = S(add([b, mul([Q(m), every])]));
      const val = evalN(s, {});
      if (val >= -1e-12 && val < 2 * Math.PI - 1e-12) inTurn.push(s);
    }
    return { solutions: inTurn, general: generalTex, inTurnOnly: true };
  }

  /** No shape it could be undone by: its real solutions found as numbers, where it changes sign or touches zero. */
  function numericSolve(f, x, steps, outcome) {
    const g = M.compile(f, x);
    const found = [];
    const scan = (lo, hi, n) => {
      let px = lo, py = g(lo);
      for (let i = 1; i <= n; i++) {
        const cx = lo + (hi - lo) * i / n, cy = g(cx);
        if (isFinite(py) && isFinite(cy)) {
          if (py === 0) found.push(px);
          else if (py * cy < 0) {
            // (a sign change: bisected; one into a pole is not a root)
            let a = px, b = cx, fa = py;
            for (let k = 0; k < 200; k++) { const m = (a + b) / 2, fm = g(m); if (!isFinite(fm)) break; if (fa * fm <= 0) b = m; else { a = m; fa = fm; } }
            const m = (a + b) / 2;
            if (Math.abs(g(m)) < 1e-6 * Math.max(1, Math.abs(py), Math.abs(cy))) found.push(m);
          }
        }
        px = cx; py = cy;
      }
    };
    scan(-100, 100, 40000);
    scan(-1e4, 1e4, 20000);
    // (a root that only touches zero: the smallest |f| in a fine scan, polished)
    const uniq = [];
    for (const v of found.sort((a, b) => a - b)) if (!uniq.some((u) => Math.abs(u - v) < 1e-7 * Math.max(1, Math.abs(v)))) uniq.push(v);
    outcome.numeric = true;
    outcome.solutions = uniq.slice(0, 40).map((v) => R(Math.abs(v) < 1e-13 ? 0 : v));
    steps.push(step('This equation has no exact method here; its real solutions are found numerically, where the left side crosses zero', tex(eq(f, Q(0)))));
    if (uniq.length > 40) outcome.truncated = true;
  }

  // ---------------------------------------------------------------- inequalities

  /** l op r for x: the intervals where it holds, from where it is zero or undefined and a test in each part. */
  function solveInequality(rel, x) {
    const steps = [];
    const f = S(sub(rel.l, rel.r));
    const op = rel.op;
    steps.push(step('Move everything to one side', tex(eq(f, Q(0), op))));
    // (linear: as an equation, flipping the sign when dividing by a negative)
    const cs = polyCoeffs(f, x);
    const { num, lcd } = together(f, x);
    const crit = [];
    const zeros = solve(eq(lcd ? num : f, Q(0)), x);
    for (const s of zeros.solutions) crit.push({ v: evalN(s, {}), node: s, kind: 'zero' });
    if (lcd) {
      const poles = solve(eq(lcd, Q(0)), x);
      for (const s of poles.solutions) crit.push({ v: evalN(s, {}), node: s, kind: 'pole' });
      steps.push(step('Find where the left side is zero or undefined', crit.map((c) => x + ' = ' + tex(c.node)).join(',\\; ') || '\\text{nowhere}'));
    } else if (cs && cs.length === 2) {
      const a = cs[1];
      const sol = zeros.solutions[0];
      const flip = numVal(a) < 0;
      const newOp = flip ? { '<': '>', '>': '<', '<=': '>=', '>=': '<=' }[op] : op;
      steps.push(step('Solve as an equation' + (flip ? ', flipping the sign: you divide by a negative number' : ''), tex(eq(sym(x), sol, newOp))));
    } else steps.push(step('Find where the left side is zero', crit.map((c) => x + ' = ' + tex(c.node)).join(',\\; ') || '\\text{nowhere}'));
    crit.sort((a, b) => a.v - b.v);
    const g = M.compile(f, x);
    const holds = (v) => { const y = g(v); if (!isFinite(y)) return false; return op === '<' ? y < 0 : op === '>' ? y > 0 : op === '<=' ? y <= 1e-12 : y >= -1e-12; };
    // (test a point in each interval, and each critical point itself)
    const pts = [-Infinity, ...crit.map((c) => c.v), Infinity];
    const pieces = [];
    for (let i = 0; i < pts.length - 1; i++) {
      const a = pts[i], b = pts[i + 1];
      const mid = !isFinite(a) && !isFinite(b) ? 0 : !isFinite(a) ? b - 1 - Math.abs(b) : !isFinite(b) ? a + 1 + Math.abs(a) : (a + b) / 2;
      pieces.push({ lo: i === 0 ? null : crit[i - 1], hi: i === pts.length - 2 ? null : crit[i], ok: holds(mid), test: mid });
    }
    if (crit.length) steps.push(step('Test a number in each interval', pieces.map((p) => x + ' = ' + M.realText(+p.test.toPrecision(4)) + ': ' + (p.ok ? '\\text{true}' : '\\text{false}')).join(',\\; ')));
    // (join the intervals, the end points in where they count)
    const intervals = [];
    for (const p of pieces) {
      if (!p.ok) continue;
      const loIn = p.lo && p.lo.kind === 'zero' && (op === '<=' || op === '>=');
      const hiIn = p.hi && p.hi.kind === 'zero' && (op === '<=' || op === '>=');
      const last = intervals[intervals.length - 1];
      if (last && last.hi && p.lo && last.hi === p.lo && last.hiIn === false && p.lo.kind === 'zero' && (op === '<=' || op === '>=')) { last.hi = p.hi; last.hiIn = hiIn; continue; }
      if (last && last.hi && p.lo && last.hi === p.lo && p.lo.kind === 'zero' && holdsAt(p.lo)) { last.hi = p.hi; last.hiIn = hiIn; continue; }
      intervals.push({ lo: p.lo, loIn, hi: p.hi, hiIn });
    }
    function holdsAt(c) { return c.kind === 'zero' && (op === '<=' || op === '>='); }
    // (a lone point: x² ≤ 0 holds only at 0)
    if (op === '<=' || op === '>=') for (const c of crit) {
      if (c.kind !== 'zero') continue;
      const inside = intervals.some((iv) => (iv.lo === c && iv.loIn) || (iv.hi === c && iv.hiIn) || ((!iv.lo || iv.lo.v < c.v) && (!iv.hi || iv.hi.v > c.v)));
      if (!inside) intervals.push({ lo: c, loIn: true, hi: c, hiIn: true, point: true });
    }
    intervals.sort((a, b) => (a.lo ? a.lo.v : -Infinity) - (b.lo ? b.lo.v : -Infinity));
    return Object.assign({ steps, intervals, none: !intervals.length }, intervalsTex(intervals, x));
  }

  /** Intervals written both ways: (−∞, −2] ∪ [2, ∞) and x ≤ −2 or x ≥ 2. */
  function intervalsTex(intervals, x) {
    const ivTex = intervals.map((iv) => iv.point ? '\\{' + tex(iv.lo.node) + '\\}' : (iv.loIn ? '[' : '(') + (iv.lo ? tex(iv.lo.node) : '-\\infty') + ', ' + (iv.hi ? tex(iv.hi.node) : '\\infty') + (iv.hiIn ? ']' : ')')).join(' \\cup ');
    const ineqTex = intervals.map((iv) => {
      if (iv.point) return x + ' = ' + tex(iv.lo.node);
      if (!iv.lo && !iv.hi) return '\\text{all real } ' + x;
      if (!iv.lo) return x + (iv.hiIn ? ' \\le ' : ' < ') + tex(iv.hi.node);
      if (!iv.hi) return x + (iv.loIn ? ' \\ge ' : ' > ') + tex(iv.lo.node);
      return tex(iv.lo.node) + (iv.loIn ? ' \\le ' : ' < ') + x + (iv.hiIn ? ' \\le ' : ' < ') + tex(iv.hi.node);
    }).join('\\;\\text{or}\\; ');
    return { intervalTex: ivTex || '\\varnothing', inequalityTex: ineqTex || '\\text{no solution}' };
  }

  /** Where both hold: each pair of intervals overlapped. */
  function intersectIntervals(a, b) {
    const out = [];
    const lo = (iv) => (iv.lo ? iv.lo.v : -Infinity), hi = (iv) => (iv.hi ? iv.hi.v : Infinity);
    for (const p of a) for (const q of b) {
      const L = lo(p) > lo(q) || (lo(p) === lo(q) && !p.loIn) ? p : q;
      const H = hi(p) < hi(q) || (hi(p) === hi(q) && !p.hiIn) ? p : q;
      if (lo(L) > hi(H) || (lo(L) === hi(H) && !(L.loIn && H.hiIn))) continue;
      out.push({ lo: L.lo, loIn: L.loIn, hi: H.hi, hiIn: H.hiIn, point: lo(L) === hi(H) });
    }
    return out;
  }

  // ---------------------------------------------------------------- systems

  /** Several equations at once: linear ones by elimination (the augmented matrix, row by row); two with one of them
   * linear in a letter, by substitution. */
  function solveSystem(rels, vars) {
    const steps = [];
    vars = vars || M.variables({ t: 'list', a: rels });
    vars = vars.slice().sort();
    const rows = [];
    let linear = true;
    for (const r of rels) {
      const f = expand(S(sub(r.l, r.r)));
      const row = [];
      let rest = f;
      for (const v of vars) {
        const cs = polyCoeffs(rest, v);
        if (!cs || cs.length > 2 || (cs[1] && !isConst(cs[1]))) { linear = false; break; }
        row.push(cs[1] || Q(0));
        rest = cs[0];
      }
      if (!linear) break;
      if (!isConst(rest)) { linear = false; break; }
      row.push(neg(rest));
      rows.push(row);
    }
    if (linear) return gaussJordan(rows, vars, steps);
    if (rels.length === 2 && vars.length === 2) return substitution(rels, vars, steps);
    throw new MathError('Only linear systems, or two equations in two letters, are solved');
  }

  function matrixTex(rows) {
    return '\\left[\\begin{array}{' + 'r'.repeat(rows[0].length - 1) + '|r}' + rows.map((r) => r.map((c) => tex(c)).join(' & ')).join(' \\\\ ') + '\\end{array}\\right]';
  }
  function gaussJordan(rows, vars, steps) {
    const m = rows.map((r) => r.slice());
    const nv = vars.length;
    steps.push(step('Write the system as an augmented matrix', matrixTex(m)));
    let lead = 0;
    const pivots = [];
    for (let r = 0; r < m.length && lead < nv; r++) {
      let i = r;
      while (i < m.length && isZero(m[i][lead])) i++;
      if (i === m.length) { lead++; r--; continue; }
      if (i !== r) { [m[i], m[r]] = [m[r], m[i]]; steps.push(step('Swap rows ' + (r + 1) + ' and ' + (i + 1), matrixTex(m))); }
      const p = m[r][lead];
      if (!isOne(p)) { m[r] = m[r].map((c) => divN(c, p)); steps.push(step('Divide row ' + (r + 1) + ' by $' + tex(p) + '$', matrixTex(m))); }
      for (let j = 0; j < m.length; j++) {
        if (j === r || isZero(m[j][lead])) continue;
        const k = m[j][lead];
        m[j] = m[j].map((c, col) => addN(c, neg(mulN(k, m[r][col]))));
        steps.push(step('Row ' + (j + 1) + ' − (' + '$' + tex(k) + '$) × row ' + (r + 1), matrixTex(m)));
      }
      pivots.push(lead);
      lead++;
    }
    const inconsistent = m.some((row) => row.slice(0, nv).every(isZero) && !isZero(row[nv]));
    if (inconsistent) { steps.push(step('A row says 0 equals a number that is not 0: the equations contradict each other', '')); return { steps, none: 'The system has no solution', solutions: null }; }
    if (pivots.length < nv) {
      const free = vars.filter((_, i) => !pivots.includes(i));
      steps.push(step('There are fewer independent equations than unknowns: infinitely many solutions', ''));
      const sol = {};
      pivots.forEach((col, r) => {
        const expr = S(add([m[r][nv], ...free.map((v) => mul([neg(m[r][vars.indexOf(v)]), sym(v)]))]));
        sol[vars[col]] = expr;
      });
      free.forEach((v) => { sol[v] = sym(v); });
      return { steps, infinite: true, solutions: sol, free };
    }
    const sol = {};
    pivots.forEach((col, r) => { sol[vars[col]] = m[r][nv]; });
    steps.push(step('Read off the answer', vars.map((v) => v + ' = ' + tex(sol[v])).join(',\\; ')));
    return { steps, solutions: sol };
  }

  function substitution(rels, vars, steps) {
    // (a letter one equation is linear in: solved for, put in the other)
    for (let i = 0; i < 2; i++) for (const v of vars) {
      const f = expand(S(sub(rels[i].l, rels[i].r)));
      const cs = polyCoeffs(f, v);
      if (!cs || cs.length !== 2 || !freeOf(cs[1], vars.find((w) => w !== v)) || !isConst(cs[1])) continue;
      const expr = S(mul([neg2(cs[0]), pow(cs[1], Q(-1))]));
      const other = vars.find((w) => w !== v);
      steps.push(step('Solve equation ' + (i + 1) + ' for $' + v + '$', tex(eq(sym(v), expr))));
      const o = rels[1 - i];
      const put = eq(S(M.substitute(o.l, v, expr)), S(M.substitute(o.r, v, expr)));
      steps.push(step('Substitute into equation ' + (2 - i), tex(put)));
      const inner = solve(put, other);
      steps.push(...inner.steps);
      const pairs = inner.solutions.map((s) => { const sol = {}; sol[other] = s; sol[v] = S(M.substitute(expr, other, s)); return sol; });
      if (pairs.length) steps.push(step('Put each back in to find $' + v + '$', pairs.map((p) => '(' + vars.map((w) => tex(p[w])).join(', ') + ')').join(',\\; ')));
      return { steps, pairs, vars, numeric: inner.numeric, none: pairs.length ? null : 'The system has no real solution' };
    }
    throw new MathError('This system is not one that can be solved by substitution here');
  }

  Object.assign(M, { intervalsTex, intersectIntervals, factor, factorPoly, factorQuadratic, rationalRoots, numericRoots, commonFactor, together, cancel, solve, solveInequality, solveSystem });
})(typeof self !== 'undefined' ? self : globalThis);
