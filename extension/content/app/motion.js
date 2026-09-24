/* Motion: every entrance, exit and move in the interface is a damped spring — a mass on a spring
 * with friction, solved exactly, never an eased guess. The same springs drive CSS (as linear()
 * easings and durations in custom properties, so a rule's `animation` or `transition` follows the
 * physics with no script per element) and script (element.animate with the spring sampled into
 * keyframes, so an exit can start from wherever an entrance has got to, at the speed it is going).
 *
 * BCV.motion
 *   PRESETS                       snappy · gentle · settle · scrim · phone (stiffness, damping, mass)
 *   spring(preset, { from, to, v0 })  → { at(t), velocity(t), settle }   t in seconds
 *   easing(preset)                → 'linear(0, 0.13, …)' for CSS; duration(preset) → seconds
 *   run(el, spec, preset, opts)   → handle { finished, cancel(), now(), progress }
 *   exit(el, kind, opts)          → handle: the element leaves from its current state
 *   reduced()                     → prefers-reduced-motion
 *
 * A spec names what moves between progress 0 and 1: { x: [px, px], y: [px, px], scale: [n, n],
 * opacity: [n, n] } (any subset; x and y may be strings for percentages). Progress can overshoot
 * 1 on a springy preset: a scale of [.9, 1] then swells a touch past 1 and settles, which is the
 * point. See docs/MOTION.md. */
(function () {
  const BCV = (self.BCV = self.BCV || {});

  // stiffness k (N/m), damping c (N·s/m), mass m (kg): the ratio c / 2√(km) says how it settles —
  // under 1 it overshoots a little, at 1 it lands as fast as it can without, over 1 it creeps in
  const PRESETS = {
    snappy: { stiffness: 750, damping: 40, mass: 1 }, // pins, ticks, toggles, menus: ~290 ms, a small overshoot (ζ ≈ .73)
    gentle: { stiffness: 200, damping: 27, mass: 1 }, // sheets, popups, screens: ~460 ms, no visible overshoot (ζ ≈ .95)
    settle: { stiffness: 280, damping: 30, mass: 1 }, // rows and blocks arriving, staggered: ~350 ms (ζ ≈ .9)
    scrim: { stiffness: 300, damping: 34, mass: 1 }, // opacity only: ~400 ms (ζ ≈ .98)
    phone: { stiffness: 260, damping: 30, mass: 1 }, // push and pop, bottom sheets: ~390 ms (ζ ≈ .93)
    island: { stiffness: 150, damping: 22.8, mass: 1 }, // the timer's island and every pin capsule or panel swelling under the pointer: ~490 ms, no overshoot (ζ ≈ .93)
  }; // (2.98.4: each a fifth slower than before and softer at the turn — smoother over speed)
  const REST_DIST = 0.01; // of the travel: at rest once within this and slower than REST_SPEED (a pixel of a hundred: the eye's threshold)
  const REST_SPEED = 0.1; // travel per second
  const MAX_T = 1.2; // s: nothing rings for longer than this, whatever the numbers

  const reduced = () => {
    try { return window.matchMedia('(prefers-reduced-motion: reduce)').matches; } catch { return false; }
  };
  const presetOf = (p) => (typeof p === 'string' ? PRESETS[p] || PRESETS.gentle : p && p.stiffness ? p : PRESETS.gentle);

  /** The spring: position and velocity as functions of time, from `from` at rest (or moving at v0,
   *  in travel units per second) to `to`. The three cases of the damped oscillator, solved exactly. */
  function spring(preset, { from = 0, to = 1, v0 = 0 } = {}) {
    const { stiffness: k, damping: c, mass: m } = presetOf(preset);
    const w0 = Math.sqrt(k / m); // undamped angular frequency
    const zeta = c / (2 * Math.sqrt(k * m)); // damping ratio
    const d0 = from - to; // displacement from rest at t = 0
    const travel = Math.abs(to - from) || 1;
    let at, velocity;
    if (zeta < 1) {
      const wd = w0 * Math.sqrt(1 - zeta * zeta);
      const A = d0;
      const B = (v0 + zeta * w0 * d0) / wd;
      at = (t) => to + Math.exp(-zeta * w0 * t) * (A * Math.cos(wd * t) + B * Math.sin(wd * t));
      velocity = (t) => {
        const e = Math.exp(-zeta * w0 * t);
        const cs = Math.cos(wd * t), sn = Math.sin(wd * t);
        return e * ((-zeta * w0) * (A * cs + B * sn) + (-A * wd * sn + B * wd * cs));
      };
    } else if (zeta === 1) {
      const A = d0;
      const B = v0 + w0 * d0;
      at = (t) => to + Math.exp(-w0 * t) * (A + B * t);
      velocity = (t) => Math.exp(-w0 * t) * (B - w0 * (A + B * t));
    } else {
      const s = Math.sqrt(zeta * zeta - 1);
      const r1 = -w0 * (zeta - s), r2 = -w0 * (zeta + s);
      const A = (v0 - r2 * d0) / (r1 - r2);
      const B = d0 - A;
      at = (t) => to + A * Math.exp(r1 * t) + B * Math.exp(r2 * t);
      velocity = (t) => A * r1 * Math.exp(r1 * t) + B * r2 * Math.exp(r2 * t);
    }
    // when it is at rest: within a hundredth of the travel and hardly moving, checked every 4 ms;
    // the first such moment that stays so for 16 ms (an overshoot passes through rest on its way back)
    let settle = MAX_T;
    for (let t = 0, quiet = 0; t <= MAX_T; t += 0.004) {
      const still = Math.abs(at(t) - to) <= REST_DIST * travel && Math.abs(velocity(t)) <= REST_SPEED * travel;
      quiet = still ? quiet + 0.004 : 0;
      if (quiet >= 0.016) { settle = t - quiet + 0.004; break; }
    }
    return { at, velocity, settle: Math.max(0.05, settle), zeta, w0 };
  }

  const caches = { easing: new Map(), duration: new Map() };
  /** The preset from rest to rest, sampled into a CSS linear() easing — the same curve a rule can
   *  play with no script. Steps every ~5 ms of its settle time, at most 192 stops: finer than any
   *  display's frame, so the curve between stops is never seen as straight. */
  function easing(preset) {
    const key = typeof preset === 'string' ? preset : JSON.stringify(preset);
    if (caches.easing.has(key)) return caches.easing.get(key);
    const sp = spring(preset);
    const n = Math.min(192, Math.max(24, Math.round(sp.settle / 0.005)));
    const stops = [];
    for (let i = 0; i <= n; i++) {
      const t = (sp.settle * i) / n;
      const v = i === n ? 1 : sp.at(t);
      stops.push(`${Math.round(v * 10000) / 10000}${i === 0 || i === n ? '' : ` ${Math.round((i / n) * 1000) / 10}%`}`);
    }
    const out = `linear(${stops.join(', ')})`;
    caches.easing.set(key, out);
    return out;
  }
  /** How long the preset takes from rest to rest, in seconds. */
  function duration(preset) {
    const key = typeof preset === 'string' ? preset : JSON.stringify(preset);
    if (!caches.duration.has(key)) caches.duration.set(key, spring(preset).settle);
    return caches.duration.get(key);
  }

  const supportsLinear = (() => {
    try { return typeof CSS !== 'undefined' && CSS.supports('animation-timing-function', 'linear(0, 1)'); } catch { return false; }
  })();
  /** The springs handed to the stylesheet: --bcv-spring-<preset> (the easing) and --bcv-t-<preset>
   *  (the duration) on the root, so `animation: name var(--bcv-t-gentle) var(--bcv-spring-gentle)`
   *  is that spring. Where linear() is unknown the vars block's bezier and durations stand. */
  function installTokens(root) {
    if (!supportsLinear) return false;
    root = root || (typeof document !== 'undefined' ? document.documentElement : null);
    if (!root) return false;
    for (const name of Object.keys(PRESETS)) {
      root.style.setProperty(`--bcv-spring-${name}`, easing(name));
      root.style.setProperty(`--bcv-t-${name}`, `${Math.round(duration(name) * 1000)}ms`);
    }
    root.classList.add('bcv-springs');
    return true;
  }

  // ---- script-driven motion --------------------------------------------------------------------
  const num = (v) => (typeof v === 'number' ? `${v}px` : String(v));
  const lerpLen = (a, b, p) => {
    // pixels or a percentage; two units never mix within one axis
    const pa = String(a), pb = String(b);
    const ua = /%$/.test(pa) ? '%' : 'px', ub = /%$/.test(pb) ? '%' : 'px';
    const na = parseFloat(pa) || 0, nb = parseFloat(pb) || 0;
    return `${(na + (nb - na) * p).toFixed(3)}${ua === ub ? ua : 'px'}`;
  };
  function frameOf(spec, p) {
    const f = {};
    const parts = [];
    if (spec.x || spec.y) parts.push(`translate(${spec.x ? lerpLen(spec.x[0], spec.x[1], p) : '0px'}, ${spec.y ? lerpLen(spec.y[0], spec.y[1], p) : '0px'})`);
    if (spec.scale) parts.push(`scale(${(spec.scale[0] + (spec.scale[1] - spec.scale[0]) * p).toFixed(4)})`);
    if (spec.rotate) parts.push(`rotate(${(spec.rotate[0] + (spec.rotate[1] - spec.rotate[0]) * p).toFixed(3)}deg)`);
    if (parts.length) f.transform = parts.join(' ');
    if (spec.opacity) f.opacity = Math.max(0, Math.min(1, spec.opacity[0] + (spec.opacity[1] - spec.opacity[0]) * p));
    return f;
  }
  /** Plays a spec on an element along a spring, from progress `from` (0) to `to` (1), with an initial
   *  velocity in progress per second; every ~4 ms a keyframe. The handle's now() reads the spring at
   *  the animation's current time, so a motion cut short can hand its position and speed on. */
  function run(el, spec, preset = 'gentle', { from = 0, to = 1, v0 = 0, fill = 'both', delay = 0, composite = 'replace' } = {}) {
    if (!el || typeof el.animate !== 'function') return { finished: Promise.resolve(), cancel() {}, now: () => ({ p: to, v: 0 }), done: true };
    const sp = spring(preset, { from, to, v0 });
    if (reduced()) {
      // no travel: a short fade to the end state, nothing left invisible
      const end = frameOf(spec, to);
      const anim = el.animate([{ opacity: spec.opacity ? Math.max(0, Math.min(1, spec.opacity[0] + (spec.opacity[1] - spec.opacity[0]) * from)) : 1 }, end], { duration: 120, easing: 'linear', fill, delay });
      return { anim, finished: anim.finished.catch(() => {}), cancel: () => anim.cancel(), now: () => ({ p: to, v: 0 }), get done() { return anim.playState === 'finished'; } };
    }
    const dur = sp.settle;
    const n = Math.max(12, Math.round(dur / 0.004)); // (a keyframe every 4 ms: past a 120 Hz frame, so the browser draws the spring, not a line between two of its points)
    const frames = [];
    for (let i = 0; i <= n; i++) {
      const t = (dur * i) / n;
      frames.push({ ...frameOf(spec, i === n ? to : sp.at(t)), offset: i / n, easing: 'linear' });
    }
    const anim = el.animate(frames, { duration: Math.round(dur * 1000), fill, delay, composite });
    const handle = {
      anim,
      finished: anim.finished.catch(() => {}),
      cancel: () => { try { anim.cancel(); } catch { /* already gone */ } },
      /** Where the spring is right now: progress and velocity (progress per second). */
      now() {
        let ms = 0;
        try { ms = Number(anim.currentTime) || 0; } catch { ms = 0; }
        const t = Math.max(0, Math.min(dur, (ms - delay) / 1000));
        if (anim.playState === 'finished') return { p: to, v: 0 };
        return { p: sp.at(t), v: sp.velocity(t) };
      },
      get done() { return anim.playState === 'finished'; },
    };
    return handle;
  }

  /** How things leave: the kinds ui.dismiss knows, each from the element's current state (an
   *  entrance still under way is read for its progress and speed; the exit spring continues from
   *  there, so a sheet closed while still growing shrinks back from where it got to). */
  const EXITS = {
    scrim: { spec: { opacity: [1, 0] }, preset: 'scrim' },
    shrink: { spec: { scale: [1, 0.96], opacity: [1, 0] }, preset: 'gentle' },
    pop: { spec: { scale: [1, 0.94], opacity: [1, 0] }, preset: 'snappy' },
    down: { spec: { y: [0, 40], opacity: [1, 0] }, preset: 'phone' },
    fade: { spec: { opacity: [1, 0] }, preset: 'scrim' },
    toast: { spec: { y: [0, 10], opacity: [1, 0] }, preset: 'snappy' },
    left: { spec: { x: [0, -8], opacity: [1, 0] }, preset: 'snappy' },
  };
  /** The progress an element's running entrance has reached (1 when none is running): read from its
   *  animations' current time against their duration, so an exit can pick up mid-flight. */
  function progressOf(el) {
    let p = 1, v = 0;
    try {
      for (const a of el.getAnimations()) {
        if (a.playState === 'finished' || a.playState === 'idle') continue;
        const tm = a.effect?.getTiming?.() || {};
        const dur = Number(tm.duration) || 0;
        if (!dur || tm.iterations === Infinity) continue;
        const cur = Math.max(0, (Number(a.currentTime) || 0) - (Number(tm.delay) || 0));
        p = Math.min(p, Math.max(0, Math.min(1, cur / dur)));
        v = Math.max(v, 1 / (dur / 1000)); // roughly the pace of the entrance, progress per second
      }
    } catch { /* an element without animations */ }
    return { p, v };
  }
  /** What the element is showing right now: its transform's scale and offset, its opacity. */
  function current(el) {
    let m = null;
    let opacity = 1;
    try {
      const cs = getComputedStyle(el);
      m = new DOMMatrix(cs.transform === 'none' ? '' : cs.transform);
      opacity = Number.isFinite(parseFloat(cs.opacity)) ? parseFloat(cs.opacity) : 1;
    } catch { m = null; }
    return { scale: m && Number.isFinite(m.a) && m.a > 0 ? m.a : 1, x: m ? m.e || 0 : 0, y: m ? m.f || 0 : 0, opacity };
  }
  function exit(el, kind = 'fade', { child = null } = {}) {
    const def = EXITS[kind] || EXITS.fade;
    const target = child || el;
    // the exit starts from what is on the screen this instant — the size, place and opacity the
    // entrance has reached — and carries a little of the entrance's speed on before turning, so a
    // sheet cut short at a third of the way in keeps growing a moment and shrinks back from there
    const now = current(target);
    const { p, v } = progressOf(target);
    const spec = {};
    for (const [k, [, gone]] of Object.entries(def.spec)) spec[k] = [k === 'scale' ? now.scale : k === 'opacity' ? now.opacity : k === 'x' ? now.x : k === 'y' ? now.y : 0, gone];
    for (const a of target.getAnimations?.() || []) { try { if (a.playState !== 'finished') a.cancel(); } catch { /* fine */ } }
    return run(target, spec, def.preset, { from: 0, to: 1, v0: p < 1 ? -Math.abs(v) * 0.5 : 0 });
  }

  installTokens();
  BCV.motion = { PRESETS, spring, easing, duration, run, exit, progressOf, reduced, supportsLinear, installTokens };
})();
