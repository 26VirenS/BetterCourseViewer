// (Mac 1.3.214, Windows 0.3.108) Export questions, the whole question: in every frame of the publisher popups' page
// (Macmillan Learning, ALEKS, Knewton Alta), from its start. A question often runs on past the frame it is shown in — a
// Macmillan module of a fixed height that scrolls inside itself, ALEKS's or Alta's page that scrolls — and a picture of the
// popup alone showed only what was in sight. The app asks the page, from its top frame, for the place that scrolls; then,
// one view at a time, has it scrolled and pictures what is in sight, and lays the pictures one under the other, so the
// question's picture is as long as the question. Nothing is answered, opened or changed: the place scrolls, what floats
// over it (Macmillan's ↓ to say there is more under it, a bar fixed to the frame) is hidden while the pictures are taken,
// and it is all put back as it was.
//
// The app calls, in the top frame: `__simplShot({ id, op: 'find' })`, then `{ id, op: 'begin', frame }`, `{ id, op:
// 'step', frame, y }` for each view, and `{ id, op: 'end', frame }`; and reads what the frames said of each with
// `__simplShotSaid(id)` (JSON: a list, each with the frame it came from, where its place is in the top frame — x, y, w, h —
// how far it scrolls — sh, ch, st — and the top frame's own width, vw). A frame tells its parent, which adds where the
// frame sits in it and tells its own, up to the top.
(function () {
  'use strict';
  if (window.__simplShotFrame) return;
  window.__simplShotFrame = true;
  var me = Math.random().toString(36).slice(2) + Date.now().toString(36);
  var isTop = window.parent === window;
  var held = null; // { el, st, hidden } while pictures are taken here
  var said = {}; // (the top frame) what the frames said, by the ask's id

  var docScroller = function () { return document.scrollingElement || document.documentElement; };
  var scrolls = function (el) {
    if (el.clientHeight < 80 || el.clientWidth < 120 || el.scrollHeight <= el.clientHeight + 8) return false;
    if (el === docScroller()) {
      var b = document.body ? getComputedStyle(document.body).overflowY : '';
      return getComputedStyle(el).overflowY !== 'hidden' && b !== 'hidden';
    }
    return /(auto|scroll|overlay)/.test(getComputedStyle(el).overflowY);
  };
  var box = function (el) {
    if (el === docScroller()) return { x: 0, y: 0, w: el.clientWidth || innerWidth, h: el.clientHeight || innerHeight };
    var r = el.getBoundingClientRect();
    return { x: r.left + el.clientLeft, y: r.top + el.clientTop, w: el.clientWidth, h: el.clientHeight };
  };
  var inSight = function (b) { return b.w > 0 && b.h > 0 && b.x < innerWidth && b.y < innerHeight && b.x + b.w > 0 && b.y + b.h > 0; };

  // The place here the question scrolls in: of the places that scroll (and are at least a quarter as large as the
  // largest), the one with the most out of sight — a Macmillan module of a fixed height, not the frame round it that
  // scrolls a few pixels of margin.
  var places = function () {
    var out = [], all = document.querySelectorAll('*');
    var look = function (el) {
      if (!scrolls(el)) return;
      var b = box(el);
      if (!inSight(b)) return;
      out.push({ el: el, area: Math.min(b.w, innerWidth) * Math.min(b.h, innerHeight), hidden: el.scrollHeight - el.clientHeight });
    };
    look(docScroller());
    for (var i = 0; i < all.length; i++) if (all[i] !== docScroller() && all[i] !== document.body) look(all[i]);
    return out;
  };
  var place = function () { return pick(places()); };
  // (the same choice the app makes among the frames' places)
  var pick = function (list) {
    var most = 0, best = null;
    for (var i = 0; i < list.length; i++) most = Math.max(most, list[i].area);
    for (var j = 0; j < list.length; j++) {
      var c = list[j];
      if (c.area < most / 4) continue;
      if (!best || c.hidden > best.hidden || (c.hidden === best.hidden && c.area > best.area)) best = c;
    }
    return best ? best.el : null;
  };

  var report = function (id, op, el) {
    var b = box(el);
    return { id: id, op: op, frame: me, x: b.x, y: b.y, w: b.w, h: b.h, sh: el.scrollHeight, ch: el.clientHeight, st: el.scrollTop };
  };
  // (said to the top frame: here added to as it goes up, by where each frame sits in its parent)
  var up = function (o) {
    if (isTop) { o.vw = innerWidth; o.vh = innerHeight; (said[o.id] = said[o.id] || []).push(o); return; }
    try { window.parent.postMessage({ simplShotUp: o }, '*'); } catch (e) { /* (no parent to tell) */ }
  };
  var after = function (fn) { requestAnimationFrame(function () { requestAnimationFrame(fn); }); };

  // What floats over the place without scrolling with it: hidden while the pictures are taken, else it is in each one.
  var floating = function (el) {
    var b = box(el), out = [], all = document.querySelectorAll('body *');
    for (var i = 0; i < all.length; i++) {
      var e = all[i];
      if (e === el || e.contains(el) || el.contains(e)) continue;
      var c = getComputedStyle(e);
      if (c.visibility === 'hidden' || c.display === 'none') continue;
      var p = c.position;
      if (p !== 'fixed' && p !== 'sticky' && p !== 'absolute') continue;
      var r = e.getBoundingClientRect();
      if (r.width === 0 || r.height === 0 || r.right <= b.x || r.left >= b.x + b.w || r.bottom <= b.y || r.top >= b.y + b.h) continue;
      // (a small thing set over it — a ↓, a badge — or a bar fixed to the frame; not a part of the page that holds it)
      if (p === 'absolute' && (r.width > 160 || r.height > 160)) continue;
      if (r.width * r.height > 0.5 * b.w * b.h) continue;
      out.push(e);
    }
    return out;
  };

  var act = function (c) {
    if (!c || typeof c !== 'object' || !c.id) return;
    if (c.op === 'find') {
      var el = place();
      if (el) up(report(c.id, 'find', el));
      return;
    }
    if (c.frame !== me) return;
    if (c.op === 'begin') {
      if (held) put();
      var p = place();
      if (!p) return;
      var hidden = floating(p);
      for (var i = 0; i < hidden.length; i++) { hidden[i].setAttribute('data-simpl-shot-was', hidden[i].style.visibility || ''); hidden[i].style.setProperty('visibility', 'hidden', 'important'); }
      held = { el: p, st: p.scrollTop, hidden: hidden, behavior: p.style.scrollBehavior };
      p.style.scrollBehavior = 'auto';
      p.scrollTop = 0;
      after(function () { up(report(c.id, 'begin', p)); });
    } else if (c.op === 'step' && held) {
      held.el.scrollTop = Math.max(0, +c.y || 0);
      after(function () { up(report(c.id, 'step', held.el)); });
    } else if (c.op === 'end' && held) {
      put();
      up({ id: c.id, op: 'end', frame: me });
    }
  };
  // Back as it was: where it was scrolled to, what was hidden shown again.
  var put = function () {
    if (!held) return;
    for (var i = 0; i < held.hidden.length; i++) {
      var e = held.hidden[i], was = e.getAttribute('data-simpl-shot-was');
      e.style.removeProperty('visibility');
      if (was) e.style.visibility = was;
      e.removeAttribute('data-simpl-shot-was');
    }
    held.el.scrollTop = held.st;
    held.el.style.scrollBehavior = held.behavior || '';
    held = null;
  };

  var down = function (c) {
    for (var i = 0; i < window.frames.length; i++) { try { window.frames[i].postMessage({ simplShotDo: c }, '*'); } catch (e) { /* (gone) */ } }
  };
  addEventListener('message', function (e) {
    var d = e.data;
    if (!d || typeof d !== 'object') return;
    if (d.simplShotDo && e.source === window.parent && !isTop) { act(d.simplShotDo); down(d.simplShotDo); return; }
    if (d.simplShotUp && typeof d.simplShotUp === 'object') {
      // (from a frame of this one: where that frame's page sits in this one's)
      var frames = document.querySelectorAll('iframe,frame'), o = d.simplShotUp;
      for (var i = 0; i < frames.length; i++) {
        if (frames[i].contentWindow !== e.source) continue;
        var r = frames[i].getBoundingClientRect(), s = getComputedStyle(frames[i]);
        if (typeof o.x === 'number') {
          o.x += r.left + frames[i].clientLeft + (parseFloat(s.paddingLeft) || 0);
          o.y += r.top + frames[i].clientTop + (parseFloat(s.paddingTop) || 0);
        }
        up(o);
        return;
      }
    }
  });
  if (isTop) {
    window.__simplShot = function (c) { act(c); down(c); return true; };
    window.__simplShotSaid = function (id) { return JSON.stringify(said[id] || []); };
  }
})();
