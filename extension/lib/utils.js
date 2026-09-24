/* Small DOM / text helpers shared by content features. */
(function () {
  const BCV = (self.BCV = self.BCV || {});

  function h(tag, attrs = {}, children = []) {
    const el = document.createElement(tag);
    for (const [k, v] of Object.entries(attrs || {})) {
      if (v === null || v === undefined || v === false) continue;
      if (k === 'class') el.className = v;
      else if (k === 'text') el.textContent = v;
      else if (k === 'html') el.innerHTML = v;
      else if (k === 'style' && typeof v === 'object') {
        // custom properties (--x) only take through setProperty
        for (const [sk, sv] of Object.entries(v)) {
          if (sk.startsWith('--')) el.style.setProperty(sk, sv);
          else el.style[sk] = sv;
        }
      }
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
    // Canvas keeps a formula as an image and the LaTeX on the tag itself, so an answer or a heading
    // that is nothing but a formula would come back blank. Read the formula (any image's alt text,
    // in fact) rather than dropping it.
    doc.querySelectorAll('img').forEach((img) => {
      const eq = img.getAttribute('data-equation-content') || (img.classList.contains('equation_image') ? img.getAttribute('title') : '') || '';
      const src = eq || (img.getAttribute('alt') || '').replace(/^LaTeX:\s*/i, '');
      img.replaceWith(doc.createTextNode(src.trim()));
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

  /* Where the interface's own floating parts go — the tray of pinned tools, a tool's popup, a menu,
   * a toast. On Canvas that is <body>, and always has been. On a tool's own tab it is <html>: the
   * look switch turns the tool's page over with a filter on <body>, and anything inside <body> is
   * turned over with it, so ours would come out inverted. Outside it, they keep their own colours. */
  const overlayRoot = () => BCV.overlayRoot || document.body;

  BCV.utils = { h, escapeHtml, htmlToText, overlayRoot };
})();
