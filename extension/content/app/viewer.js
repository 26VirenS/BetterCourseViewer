/* File viewer: a file opened from Files, a module or a link opens in a sheet over the page
 * rather than in a new tab — its name and details up top, a preview where one can be drawn
 * (images, video, audio and text here; PDFs and documents through Canvas's own preview, which
 * is made to be framed), and Download and Open in Canvas beside it. Escape, the close button
 * or a click outside puts it away and hands focus back to the row that opened it. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const store = () => BCV.store; // read when used: the store's script loads after this one

  const fmtSize = (n) => {
    if (!Number.isFinite(n)) return '';
    if (n < 1024) return `${n} B`;
    if (n < 1048576) return `${Math.round(n / 1024)} KB`;
    if (n < 1073741824) return `${(n / 1048576).toFixed(1)} MB`;
    return `${(n / 1073741824).toFixed(1)} GB`;
  };

  /** What a file is, by its content type (its name as a fallback), and how it is best shown. */
  function kindOf(f) {
    const t = String(f['content-type'] || f.mime_class || '').toLowerCase();
    const name = String(f.display_name || f.filename || '').toLowerCase();
    if (t.startsWith('image/')) return { kind: 'image', label: 'Image', icon: IC.image || IC.page, color: '#34c759' };
    if (t.startsWith('video/')) return { kind: 'video', label: 'Video', icon: IC.video || IC.doc, color: '#ff375f' };
    if (t.startsWith('audio/')) return { kind: 'audio', label: 'Audio', icon: IC.video || IC.doc, color: '#ff9500' };
    if (t === 'application/pdf' || name.endsWith('.pdf')) return { kind: 'pdf', label: 'PDF', icon: IC.doc, color: '#ff453a' };
    if (t.startsWith('text/') || /\.(md|txt|csv|json|log)$/.test(name)) return { kind: 'text', label: 'Text', icon: IC.page, color: '#8e8e93' };
    if (/word|officedocument|presentation|spreadsheet|ms-excel|ms-powerpoint|msword|rtf|opendocument/.test(t) || /\.(docx?|pptx?|xlsx?|odt|odp|ods|rtf)$/.test(name)) return { kind: 'doc', label: 'Document', icon: IC.doc, color: '#0a84ff' };
    return { kind: 'other', label: 'File', icon: IC.doc, color: '#8e8e93' };
  }
  const canvasPage = (f, ctx) => (ctx && ctx.url ? `${ctx.url}/files/${f.id}` : `/files/${f.id}`);
  const canvasPreview = (f, ctx) => (ctx && ctx.url ? `${ctx.url}/files/${f.id}/file_preview` : `/files/${f.id}/file_preview`);
  /** The file's own address without the "download it" flag: what a new tab shows as itself. */
  const inlineUrl = (f) => {
    try {
      const u = new URL(f.url, location.origin);
      u.searchParams.delete('download_frd');
      return u.pathname + (u.search || '') + (u.hash || '');
    } catch {
      return f.url;
    }
  };
  /** The kinds a browser shows as they are in a tab of their own: an image, a PDF, text, video, audio. */
  const SHOWABLE = ['image', 'pdf', 'text', 'video', 'audio'];
  /** "Open in new tab" for a file a browser can show. Canvas serves its download address as an
   *  attachment whatever is asked (a tab given it downloads the file), so the bytes are fetched
   *  here and the tab is handed a copy of its own to show. The tab is opened on the press itself
   *  — a tab opened later would be blocked as a pop-up — and says what it is doing until the
   *  file lands; if the bytes cannot be read it is sent to the file's own address instead. */
  async function openInTab(f, name) {
    const w = window.open('', '_blank');
    if (!w) { U.toast('The browser blocked the new tab.', { error: true }); return; }
    try {
      w.document.title = name;
      w.document.body.style.cssText = 'font: 15px/1.5 -apple-system, system-ui, sans-serif; color: #555; padding: 40px;';
      w.document.body.textContent = `Opening ${name}…`;
    } catch { /* the tab is not ours to write in: it will still be sent the file */ }
    try {
      const r = await fetch(f.url, { credentials: 'same-origin' });
      if (!r.ok) throw new Error(`HTTP ${r.status}`);
      const raw = await r.blob();
      const type = f['content-type'] || raw.type || 'application/octet-stream';
      const blob = raw.type === type ? raw : new Blob([raw], { type });
      const url = URL.createObjectURL(blob);
      w.location.href = url;
      setTimeout(() => URL.revokeObjectURL(url), 5 * 60 * 1000); // the tab has read it long before
    } catch {
      try { w.location.href = inlineUrl(f); } catch { /* nothing more to try */ }
    }
  }

  let current = null; // { ov, restore }
  function close() {
    if (!current) return;
    const { ov, restore } = current;
    current = null;
    ov.remove();
    try { restore?.focus?.(); } catch { /* it may be gone */ }
  }
  const isOpen = () => !!current;

  /** Opens `file` — an API File object, or just `{ id }`, fetched here — over the page. `context`
   *  is the course or group it belongs to (its Canvas addresses hang off that); `from` is the
   *  control that was pressed, which the sheet grows out of and hands focus back to. */
  async function open(file, { context = null, from = null } = {}) {
    close();
    const dark = !!BCV.app?.isDark?.();
    const ov = U.el('bcv-sheet-ov bcv-viewer-ov', null, { role: 'dialog', 'aria-label': 'File', tabindex: '-1' });
    const sheet = U.el('bcv-sheet bcv-viewer');
    const head = U.el('bcv-sheet__head');
    const body = U.el('bcv-viewer__body', U.loading('inset', 2));
    sheet.append(head, body);
    ov.append(sheet);
    ov.addEventListener('click', (e) => { if (e.target === ov) close(); });
    ov.addEventListener('keydown', (e) => { if (e.key === 'Escape') { e.stopPropagation(); close(); } });
    current = { ov, restore: from && from.focus ? from : document.activeElement };
    document.body.append(ov);
    if (from) U.morphFrom(sheet, from);
    const closeBtn = h('button', { type: 'button', class: 'bcv-sheet__close', 'aria-label': 'Close', onclick: close }, U.svg(IC.close, { size: 13, stroke: 'var(--bcv-ink2)', width: 2.3 }));
    head.append(U.el('bcv-sheet__titles', [U.text('bcv-sheet__title', file?.display_name || file?.filename || 'File')]), closeBtn);
    ov.focus();

    // the details, when only an id came (a module item, a link into the page)
    let f = file && (file.display_name || file.url) ? file : null;
    if (!f) {
      try { f = await store().file(file.id); } catch (e) {
        if (current?.ov !== ov) return;
        body.replaceChildren(U.el('bcv-viewer__none', [U.errorBox(`This file could not be read: ${e?.message || e}`)]));
        return;
      }
    }
    if (current?.ov !== ov) return; // closed, or another file opened, while the details were on their way
    const k = kindOf(f);
    const pal = U.palette(k.color, dark);
    const name = f.display_name || f.filename || 'File';
    const when = f.updated_at || f.modified_at || f.created_at;
    const note = [k.label, fmtSize(f.size), when ? `modified ${U.fmtRecent(when)}` : null].filter(Boolean).join(' · ');
    const download = h('a', { class: 'bcv-btn bcv-btn--primary bcv-viewer__dl', href: f.url, download: f.filename || name, text: 'Download' });
    download.prepend(U.svg(IC.download, { size: 14, stroke: 'currentColor', width: 1.9 }));
    const inCanvas = U.btn('Open in Canvas', { cls: 'bcv-viewer__canvas', onClick: () => { close(); BCV.app.go(`${canvasPage(f, context)}?bcv=native`); } });
    // Open in new tab: the file itself where a browser can show it, Canvas's own page for it otherwise
    const newTab = SHOWABLE.includes(k.kind)
      ? U.btn('Open in new tab', { cls: 'bcv-viewer__tab', icon: IC.external || IC.link || IC.doc, iconSize: 13, title: 'Open in a new tab', onClick: () => openInTab(f, name) })
      : h('a', { class: 'bcv-btn bcv-viewer__tab', href: `${canvasPage(f, context)}?bcv=native`, target: '_blank', rel: 'noopener', title: 'Open in a new tab', text: 'Open in new tab' });
    if (newTab.tagName === 'A') newTab.prepend(U.svg(IC.external || IC.link || IC.doc, { size: 13, stroke: 'currentColor', width: 1.9 }));
    head.replaceChildren(
      U.tile(k.icon, { color: pal.text, tint: pal.tint, size: 32, iconSize: 16 }),
      U.el('bcv-sheet__titles', [U.text('bcv-sheet__title', name), U.text('bcv-sheet__note', note)]),
      U.el('bcv-viewer__acts', [inCanvas, newTab, download]),
      closeBtn,
    );

    const none = (why) => U.el('bcv-viewer__none', [
      U.tile(k.icon, { color: pal.text, tint: pal.tint, size: 32, iconSize: 16 }),
      h('div', { text: why }),
      h('a', { class: 'bcv-btn bcv-btn--primary', href: f.url, download: f.filename || name, text: 'Download' }),
    ]);
    const frame = (src) => h('iframe', { class: 'bcv-viewer__frame', src, title: name, allow: 'fullscreen' });
    let view;
    if (k.kind === 'image') view = h('img', { class: 'bcv-viewer__img', src: f.url, alt: name });
    else if (k.kind === 'video') view = h('video', { class: 'bcv-viewer__media', src: f.url, controls: 'controls', preload: 'metadata' });
    else if (k.kind === 'audio') view = h('audio', { class: 'bcv-viewer__media bcv-viewer__media--audio', src: f.url, controls: 'controls', preload: 'metadata' });
    else if (k.kind === 'text') {
      view = h('pre', { class: 'bcv-viewer__text', text: '' });
      fetch(f.url, { credentials: 'same-origin' }).then((r) => (r.ok ? r.text() : Promise.reject(new Error(`${r.status}`)))).then((t) => { view.textContent = t.slice(0, 200000); }).catch(() => { if (current?.ov === ov) body.replaceChildren(none('The text could not be read.')); });
    } else if (f.preview_url) view = frame(f.preview_url); // Canvas's document preview, when its service made one
    else if (k.kind === 'pdf' || k.kind === 'doc') view = frame(canvasPreview(f, context)); // Canvas's own preview of the file
    else view = none('No preview for this kind of file.');
    body.replaceChildren(view);
    if (view.tagName === 'IMG') view.addEventListener('error', () => { if (current?.ov === ov) body.replaceChildren(none('The image could not be shown.')); });
  }

  // A link to a file anywhere in the interface — in an assignment's text, a page, a discussion,
  // an announcement, a module — opens the viewer too, rather than a new tab or Canvas's file page.
  // A download link (Canvas's ".../download?download_frd=1") is left to download; so is a click
  // with a modifier, and anything outside the app or inside the viewer itself.
  const FILE_LINK = /^\/(?:(courses|groups)\/(\w+)\/)?files\/(\w+)(?:\/preview)?\/?$/;
  function linkToFile(a) {
    let url;
    try { url = new URL(a.href, location.origin); } catch { return null; }
    if (url.origin !== location.origin) return null;
    const m = url.pathname.match(FILE_LINK);
    if (!m) return null;
    return { id: m[3], context: m[1] ? { url: `/${m[1]}/${m[2]}` } : null };
  }
  document.addEventListener('click', (e) => {
    if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
    const a = e.target.closest?.('a[href]');
    if (!a || !a.closest('#bcv-app') || a.closest('.bcv-viewer-ov') || a.hasAttribute('download')) return;
    const hit = linkToFile(a);
    if (!hit) return;
    e.preventDefault();
    open({ id: hit.id }, { context: hit.context, from: a });
  }, true);

  BCV.viewer = { open, close, isOpen, kindOf, fmtSize, linkToFile };
})();
