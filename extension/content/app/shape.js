/* Shape: the continuous corner. A circular arc meets a straight edge with a jump in curvature; the
 * corner iOS draws (and this interface wears) blends a superellipse into the edge, so the curvature
 * builds up and eases out — the "squircle". Two ways to get it:
 *
 *  - Where the browser draws it: `corner-shape: superellipse(k)` beside `border-radius` (Chromium
 *    139+). The stylesheet says so on every pane; nothing to do here.
 *  - Elsewhere (Safari): the exact path for the element's size and its own border-radius (each
 *    corner, from the computed style) is written into --bcv-sq on the element, and the stylesheet
 *    uses it as a clip-path — on a pane's ::before (the shape under the content, the shadow on the
 *    pane), or on a tile or picture directly. One ResizeObserver serves every element; a
 *    MutationObserver finds the panes as screens draw them.
 *
 * BCV.shape: path(w, h, radii, smoothing) → the SVG path; apply(el) / release(el); watch(); force(on)
 * (tests: the path way even where corner-shape exists). See docs/MOTION.md §2.4. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const SMOOTHING = 0.6; // how far the curve runs into the edge past the radius (0: a plain arc; 1: the whole run); iOS sits near .6
  const SEL = '.bcv-card, .bcv-sheet, .bcv-menu, .bcv-toast, .bcv-quicknav, .bcv-picker__list, .bcv-omni__panel, .bcv-omni__box, .bcv-ph-card, .bcv-tool-card, .bcv-ccard, .bcv-ccard__hero, .bcv-gpa__card, .bcv-stat, .bcv-tile, .bcv-sheet__tile, .bcv-quick__face, .bcv-pin__btn, .bcv-skel__row, .bcv-skel__card, .bcv-todo__composer, .bcv-ph-composer, .bcv-pv, .bcv-work, .bcv-nf__group .bcv-card';

  const native = (() => {
    try { return typeof CSS !== 'undefined' && CSS.supports('corner-shape', 'squircle'); } catch { return false; }
  })();
  let forced = false; // the path way even with corner-shape (tests, or a look at the fallback)
  const usePath = () => forced || !native;

  const rad = (deg) => (deg * Math.PI) / 180;
  const fix = (n) => (Math.round(n * 100) / 100).toString();
  /** The four numbers of one corner's curve for a radius r under smoothing s, within the room the
   *  side has (budget: half the shorter side): the run p the corner takes along the edge, the arc's
   *  length, and the bezier handles a, b, c, d that carry the edge into the arc. */
  function corner(r, s, budget) {
    if (r <= 0) return null;
    r = Math.min(r, budget);
    let p = (1 + s) * r;
    if (p > budget) { s = Math.max(0, budget / r - 1); p = Math.min(p, budget); }
    const arcMeasure = 90 * (1 - s);
    const arc = Math.sin(rad(arcMeasure / 2)) * r * Math.SQRT2;
    const alpha = (90 - arcMeasure) / 2;
    const p3p4 = r * Math.tan(rad(alpha / 2));
    const beta = 45 * s;
    const c = p3p4 * Math.cos(rad(beta));
    const d = c * Math.tan(rad(beta));
    const b = (p - arc - c - d) / 3;
    const a = 2 * b;
    return { r, p, arc, a, b, c, d };
  }
  /** The path, clockwise from the top edge. radii: one number, or [tl, tr, br, bl]. */
  function path(w, h, radii, smoothing = SMOOTHING) {
    const rs = Array.isArray(radii) ? radii : [radii, radii, radii, radii];
    const budget = Math.min(w, h) / 2;
    const [tl, tr, br, bl] = rs.map((r) => corner(Number(r) || 0, smoothing, budget));
    const parts = [];
    parts.push(`M ${fix(tr ? w - tr.p : w)} 0`);
    if (tr) parts.push(`c ${fix(tr.a)} 0 ${fix(tr.a + tr.b)} 0 ${fix(tr.a + tr.b + tr.c)} ${fix(tr.d)} a ${fix(tr.r)} ${fix(tr.r)} 0 0 1 ${fix(tr.arc)} ${fix(tr.arc)} c ${fix(tr.d)} ${fix(tr.c)} ${fix(tr.d)} ${fix(tr.b + tr.c)} ${fix(tr.d)} ${fix(tr.a + tr.b + tr.c)}`);
    parts.push(`L ${fix(w)} ${fix(br ? h - br.p : h)}`);
    if (br) parts.push(`c 0 ${fix(br.a)} 0 ${fix(br.a + br.b)} ${fix(-br.d)} ${fix(br.a + br.b + br.c)} a ${fix(br.r)} ${fix(br.r)} 0 0 1 ${fix(-br.arc)} ${fix(br.arc)} c ${fix(-br.c)} ${fix(br.d)} ${fix(-(br.b + br.c))} ${fix(br.d)} ${fix(-(br.a + br.b + br.c))} ${fix(br.d)}`);
    parts.push(`L ${fix(bl ? bl.p : 0)} ${fix(h)}`);
    if (bl) parts.push(`c ${fix(-bl.a)} 0 ${fix(-(bl.a + bl.b))} 0 ${fix(-(bl.a + bl.b + bl.c))} ${fix(-bl.d)} a ${fix(bl.r)} ${fix(bl.r)} 0 0 1 ${fix(-bl.arc)} ${fix(-bl.arc)} c ${fix(-bl.d)} ${fix(-bl.c)} ${fix(-bl.d)} ${fix(-(bl.b + bl.c))} ${fix(-bl.d)} ${fix(-(bl.a + bl.b + bl.c))}`);
    parts.push(`L 0 ${fix(tl ? tl.p : 0)}`);
    if (tl) parts.push(`c 0 ${fix(-tl.a)} 0 ${fix(-(tl.a + tl.b))} ${fix(tl.d)} ${fix(-(tl.a + tl.b + tl.c))} a ${fix(tl.r)} ${fix(tl.r)} 0 0 1 ${fix(tl.arc)} ${fix(-tl.arc)} c ${fix(tl.c)} ${fix(-tl.d)} ${fix(tl.b + tl.c)} ${fix(-tl.d)} ${fix(tl.a + tl.b + tl.c)} ${fix(-tl.d)}`);
    parts.push('Z');
    return parts.join(' ');
  }

  // ---- the elements, followed -------------------------------------------------------------------
  const seen = new WeakSet();
  let ro = null;
  let mo = null;
  const radiiOf = (cs) => {
    const px = (v) => { const n = parseFloat(v); return Number.isFinite(n) && !/%/.test(v) ? n : 0; }; // (a percentage radius is a disc or a pill: left to border-radius)
    return [px(cs.borderTopLeftRadius), px(cs.borderTopRightRadius), px(cs.borderBottomRightRadius), px(cs.borderBottomLeftRadius)];
  };
  function paint(el, w, h) {
    if (!el.isConnected) return;
    const cs = getComputedStyle(el);
    const radii = radiiOf(cs);
    if (!w || !h || !radii.some((r) => r > 0) || /%/.test(cs.borderTopLeftRadius)) { el.style.removeProperty('--bcv-sq'); return; }
    el.style.setProperty('--bcv-sq', `path("${path(w, h, radii)}")`);
  }
  function apply(el) {
    if (!el || seen.has(el) || !usePath()) return;
    seen.add(el);
    if (!ro) ro = new ResizeObserver((entries) => { for (const e of entries) { const b = e.borderBoxSize?.[0]; paint(e.target, b ? b.inlineSize : e.target.offsetWidth, b ? b.blockSize : e.target.offsetHeight); } });
    ro.observe(el);
  }
  function release(el) { if (el && seen.has(el)) { seen.delete(el); ro?.unobserve(el); el.style.removeProperty('--bcv-sq'); } }
  function scan(root) {
    if (!root || root.nodeType !== 1) return;
    if (root.matches?.(SEL)) apply(root);
    for (const el of root.querySelectorAll?.(SEL) || []) apply(el);
  }
  /** Follows the document: every pane drawn from now on gets its path (and the ones already there). */
  function watch() {
    if (mo || !usePath() || typeof MutationObserver === 'undefined') return;
    document.documentElement.classList.add('bcv-sq-path');
    scan(document.body);
    mo = new MutationObserver((recs) => { for (const m of recs) for (const n of m.addedNodes) scan(n); });
    mo.observe(document.documentElement, { childList: true, subtree: true });
  }
  function force(on = true) {
    forced = !!on;
    if (forced) watch();
  }

  if (typeof document !== 'undefined') {
    document.documentElement.classList.add(native ? 'bcv-sq-native' : 'bcv-sq-path');
    if (document.body) watch(); else document.addEventListener('DOMContentLoaded', watch, { once: true });
  }
  BCV.shape = { path, apply, release, watch, force, native: () => native, usePath, SMOOTHING };
})();
