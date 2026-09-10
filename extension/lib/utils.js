/* Small DOM / date / text helpers shared by content features. */
(function () {
  const BCV = (self.BCV = self.BCV || {});

  const $ = (sel, root = document) => root.querySelector(sel);
  const $$ = (sel, root = document) => Array.from(root.querySelectorAll(sel));

  function h(tag, attrs = {}, children = []) {
    const el = document.createElement(tag);
    for (const [k, v] of Object.entries(attrs || {})) {
      if (v === null || v === undefined || v === false) continue;
      if (k === 'class') el.className = v;
      else if (k === 'text') el.textContent = v;
      else if (k === 'html') el.innerHTML = v;
      else if (k === 'style' && typeof v === 'object') Object.assign(el.style, v);
      else if (k.startsWith('on') && typeof v === 'function') el.addEventListener(k.slice(2).toLowerCase(), v);
      else if (k === 'dataset') Object.assign(el.dataset, v);
      else el.setAttribute(k, v === true ? '' : v);
    }
    for (const c of [].concat(children)) {
      if (c === null || c === undefined || c === false) continue;
      el.append(c instanceof Node ? c : document.createTextNode(String(c)));
    }
    return el;
  }

  function escapeHtml(s) {
    return String(s ?? '')
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }

  /** HTML -> readable plain text (keeps list/paragraph structure). */
  function htmlToText(html, maxLen = Infinity) {
    if (!html) return '';
    const doc = new DOMParser().parseFromString(String(html), 'text/html');
    doc.querySelectorAll('script, style, noscript, iframe, svg').forEach((n) => n.remove());
    doc.querySelectorAll('a[href]').forEach((a) => {
      const href = a.getAttribute('href') || '';
      const text = a.textContent.trim();
      if (href.startsWith('http') && text && text !== href) {
        a.textContent = `${text} (${href})`;
      }
    });
    doc.querySelectorAll('li').forEach((li) => li.prepend('- '));
    doc.querySelectorAll('br').forEach((br) => br.replaceWith('\n'));
    doc.querySelectorAll('p, div, li, h1, h2, h3, h4, h5, h6, tr, blockquote, pre, section, article, header, footer, table').forEach((el) => {
      el.prepend('\n');
      el.append('\n');
    });
    doc.querySelectorAll('td, th').forEach((el) => el.append(' | '));
    doc.querySelectorAll('h1, h2, h3, h4').forEach((el) => el.prepend('## '));
    let text = doc.body.textContent || '';
    text = text.replace(/[ \t]+\n/g, '\n').replace(/\n{3,}/g, '\n\n').replace(/[ \t]{2,}/g, ' ').trim();
    if (text.length > maxLen) text = text.slice(0, maxLen) + '\n...[truncated]';
    return text;
  }

  /** Visible text of a live element, ignoring our own UI. */
  function elementText(el, maxLen = Infinity) {
    if (!el) return '';
    const clone = el.cloneNode(true);
    clone.querySelectorAll('.bcv-ui, script, style, noscript').forEach((n) => n.remove());
    return htmlToText(clone.innerHTML, maxLen);
  }

  const HOUR = 3600e3;
  const DAY = 24 * HOUR;

  function parseDate(v) {
    if (!v) return null;
    const d = v instanceof Date ? v : new Date(v);
    return isNaN(d.getTime()) ? null : d;
  }

  function startOfDay(d) {
    const x = new Date(d);
    x.setHours(0, 0, 0, 0);
    return x;
  }

  /** "in 3h", "in 2d", "5h ago", "now" */
  function relative(date, now = new Date()) {
    const d = parseDate(date);
    if (!d) return '';
    const diff = d - now;
    const abs = Math.abs(diff);
    let s;
    if (abs < 60e3) return 'now';
    if (abs < HOUR) s = `${Math.round(abs / 60e3)}m`;
    else if (abs < DAY) s = `${Math.round(abs / HOUR)}h`;
    else if (abs < 14 * DAY) s = `${Math.round(abs / DAY)}d`;
    else s = `${Math.round(abs / (7 * DAY))}w`;
    return diff < 0 ? `${s} ago` : `in ${s}`;
  }

  function formatDue(date, now = new Date()) {
    const d = parseDate(date);
    if (!d) return '';
    const today = startOfDay(now);
    const day = startOfDay(d);
    const dayDiff = Math.round((day - today) / DAY);
    const time = d.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' });
    if (dayDiff === 0) return `Today ${time}`;
    if (dayDiff === 1) return `Tomorrow ${time}`;
    if (dayDiff === -1) return `Yesterday ${time}`;
    if (dayDiff > 1 && dayDiff < 7) return `${d.toLocaleDateString(undefined, { weekday: 'short' })} ${time}`;
    return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' }) + ' ' + time;
  }

  /** Urgency bucket for styling. */
  function urgency(date, now = new Date()) {
    const d = parseDate(date);
    if (!d) return 'none';
    const diff = d - now;
    if (diff < 0) return 'overdue';
    if (diff < DAY) return 'today';
    if (diff < 3 * DAY) return 'soon';
    if (diff < 7 * DAY) return 'week';
    return 'later';
  }

  function debounce(fn, ms) {
    let t;
    return (...args) => {
      clearTimeout(t);
      t = setTimeout(() => fn(...args), ms);
    };
  }

  function throttle(fn, ms) {
    let last = 0;
    let timer = null;
    return (...args) => {
      const now = Date.now();
      const run = () => {
        last = Date.now();
        timer = null;
        fn(...args);
      };
      if (now - last >= ms) run();
      else if (!timer) timer = setTimeout(run, ms - (now - last));
    };
  }

  function isEditable(el) {
    if (!el) return false;
    const tag = (el.tagName || '').toLowerCase();
    if (tag === 'input' || tag === 'textarea' || tag === 'select') return true;
    if (el.isContentEditable) return true;
    return false;
  }

  async function copyText(text) {
    try {
      await navigator.clipboard.writeText(text);
      return true;
    } catch {
      const ta = h('textarea', { style: { position: 'fixed', opacity: '0' } });
      ta.value = text;
      document.body.append(ta);
      ta.select();
      let ok = false;
      try {
        ok = document.execCommand('copy');
      } catch {
        ok = false;
      }
      ta.remove();
      return ok;
    }
  }

  /** Watch for DOM changes (debounced). */
  function observe(callback, { debounceMs = 250, root = document.body } = {}) {
    const run = debounce(callback, debounceMs);
    const mo = new MutationObserver(run);
    mo.observe(root || document.documentElement, { childList: true, subtree: true });
    return () => mo.disconnect();
  }

  function onUrlChange(callback) {
    let last = location.href;
    const check = () => {
      if (location.href !== last) {
        last = location.href;
        callback(last);
      }
    };
    window.addEventListener('popstate', check);
    window.addEventListener('hashchange', check);
    const timer = setInterval(check, 1000);
    return () => {
      window.removeEventListener('popstate', check);
      window.removeEventListener('hashchange', check);
      clearInterval(timer);
    };
  }

  function uid() {
    return Math.random().toString(36).slice(2, 10) + Date.now().toString(36);
  }

  const ICONS = {
    spark: '<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 3l1.8 5.2L19 10l-5.2 1.8L12 17l-1.8-5.2L5 10l5.2-1.8z"/><path d="M19 17l.7 2.3L22 20l-2.3.7L19 23l-.7-2.3L16 20l2.3-.7z"/></svg>',
    check: '<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="3" y="4" width="18" height="16" rx="3"/><path d="M8 12l3 3 5-6"/></svg>',
    close: '<svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18"/></svg>',
    external: '<svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M14 4h6v6M20 4l-9 9M19 14v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1h5"/></svg>',
    refresh: '<svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M20 12a8 8 0 1 1-2.3-5.7"/><path d="M20 4v5h-5"/></svg>',
    gear: '<svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z"/></svg>',
    plus: '<svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" aria-hidden="true"><path d="M12 5v14M5 12h14"/></svg>',
    copy: '<svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="9" y="9" width="11" height="11" rx="2"/><path d="M5 15V5a2 2 0 0 1 2-2h10"/></svg>',
    send: '<svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M22 2L11 13"/><path d="M22 2l-7 20-4-9-9-4z"/></svg>',
    stop: '<svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor" aria-hidden="true"><rect x="6" y="6" width="12" height="12" rx="2"/></svg>',
    trash: '<svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 6h18M8 6V4h8v2M6 6l1 14h10l1-14"/></svg>',
    insert: '<svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 5v10M8 11l4 4 4-4"/><path d="M4 19h16"/></svg>',
  };

  BCV.utils = {
    $, $$, h, escapeHtml, htmlToText, elementText, parseDate, startOfDay, relative, formatDue, urgency,
    debounce, throttle, isEditable, copyText, observe, onUrlChange, uid, ICONS, HOUR, DAY,
  };
})();
