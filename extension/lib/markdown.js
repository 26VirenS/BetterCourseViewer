/* Minimal, safe Markdown -> HTML renderer for assistant replies.
 * Supports headings, paragraphs, bold/italic/code, fenced code, lists,
 * blockquotes, links (http/https only) and simple tables. Everything is
 * HTML-escaped first, so untrusted text cannot inject markup. */
(function () {
  const BCV = (self.BCV = self.BCV || {});

  const esc = (s) => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
  // Placeholder marker for protected code spans (never appears in normal text).
  const PH = '';
  const PH_RE = new RegExp(PH + '(\\d+)' + PH, 'g');

  function inline(text) {
    let s = esc(text);
    // code spans first so their contents are not further transformed
    const codes = [];
    s = s.replace(/`([^`]+)`/g, (_, c) => {
      codes.push(`<code>${c}</code>`);
      return `${PH}${codes.length - 1}${PH}`;
    });
    s = s.replace(/\[([^\]]+)\]\((https?:\/\/[^\s)]+)\)/g, '<a href="$2" target="_blank" rel="noopener noreferrer">$1</a>');
    s = s.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');
    s = s.replace(/__([^_]+)__/g, '<strong>$1</strong>');
    s = s.replace(/(^|[^*\w])\*([^*\n]+)\*(?!\w)/g, '$1<em>$2</em>');
    s = s.replace(/(^|[^_\w])_([^_\n]+)_(?!\w)/g, '$1<em>$2</em>');
    s = s.replace(/~~([^~]+)~~/g, '<del>$1</del>');
    s = s.replace(PH_RE, (_, i) => codes[+i]);
    return s;
  }

  function render(md) {
    const lines = String(md || '').replace(/\r\n?/g, '\n').split('\n');
    const out = [];
    let i = 0;
    const listStack = [];
    const closeLists = (toDepth = 0) => {
      while (listStack.length > toDepth) out.push(`</${listStack.pop()}>`);
    };
    let para = [];
    const flushPara = () => {
      if (para.length) {
        out.push(`<p>${inline(para.join(' '))}</p>`);
        para = [];
      }
    };

    while (i < lines.length) {
      const line = lines[i];

      // fenced code
      const fence = line.match(/^\s*```(\w*)/);
      if (fence) {
        flushPara();
        closeLists();
        const buf = [];
        i++;
        while (i < lines.length && !/^\s*```/.test(lines[i])) buf.push(lines[i++]);
        i++;
        const lang = fence[1] ? ` data-lang="${esc(fence[1])}"` : '';
        out.push(`<pre${lang}><code>${esc(buf.join('\n'))}</code></pre>`);
        continue;
      }

      if (!line.trim()) {
        flushPara();
        closeLists();
        i++;
        continue;
      }

      const heading = line.match(/^(#{1,6})\s+(.*)$/);
      if (heading) {
        flushPara();
        closeLists();
        const level = Math.min(4, heading[1].length + 1); // h2..h4 inside the panel
        out.push(`<h${level}>${inline(heading[2].trim())}</h${level}>`);
        i++;
        continue;
      }

      if (/^\s*(-{3,}|\*{3,})\s*$/.test(line)) {
        flushPara();
        closeLists();
        out.push('<hr>');
        i++;
        continue;
      }

      const quote = line.match(/^\s*>\s?(.*)$/);
      if (quote) {
        flushPara();
        closeLists();
        const buf = [quote[1]];
        i++;
        while (i < lines.length && /^\s*>/.test(lines[i])) buf.push(lines[i++].replace(/^\s*>\s?/, ''));
        out.push(`<blockquote>${render(buf.join('\n'))}</blockquote>`);
        continue;
      }

      // tables: header | header / --- | --- / cells
      if (line.includes('|') && i + 1 < lines.length && /^\s*\|?\s*:?-{2,}/.test(lines[i + 1])) {
        flushPara();
        closeLists();
        const cells = (l) => l.trim().replace(/^\|/, '').replace(/\|$/, '').split('|').map((c) => inline(c.trim()));
        const head = cells(line);
        i += 2;
        const rows = [];
        while (i < lines.length && lines[i].includes('|')) rows.push(cells(lines[i++]));
        out.push('<table><thead><tr>' + head.map((c) => `<th>${c}</th>`).join('') + '</tr></thead><tbody>' +
          rows.map((r) => '<tr>' + r.map((c) => `<td>${c}</td>`).join('') + '</tr>').join('') + '</tbody></table>');
        continue;
      }

      const item = line.match(/^(\s*)([-*+]|\d+[.)])\s+(.*)$/);
      if (item) {
        flushPara();
        const depth = Math.floor(item[1].replace(/\t/g, '  ').length / 2) + 1;
        const type = /\d/.test(item[2]) ? 'ol' : 'ul';
        while (listStack.length > depth) out.push(`</${listStack.pop()}>`);
        while (listStack.length < depth) {
          listStack.push(type);
          out.push(`<${type}>`);
        }
        const content = item[3];
        const task = content.match(/^\[( |x|X)\]\s+(.*)$/);
        if (task) {
          const done = task[1] !== ' ';
          out.push(`<li><span class="bcv-md-task${done ? ' is-done' : ''}">${done ? '&#9745;' : '&#9744;'}</span> ${inline(task[2])}</li>`);
        } else {
          out.push(`<li>${inline(content)}</li>`);
        }
        i++;
        continue;
      }

      // continuation of a list item (indented text)
      if (listStack.length && /^\s{2,}\S/.test(line)) {
        const lastIdx = out.length - 1;
        if (out[lastIdx]?.endsWith('</li>')) {
          out[lastIdx] = out[lastIdx].slice(0, -5) + ' ' + inline(line.trim()) + '</li>';
          i++;
          continue;
        }
      }

      closeLists();
      para.push(line.trim());
      i++;
    }
    flushPara();
    closeLists();
    return out.join('\n');
  }

  BCV.markdown = { render, inline, escape: esc };
})();
