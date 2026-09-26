/* simplcourses.com/report/ — Report a bug. Simpl's purple button opens this page with what it
 * knows in the address: c (the kind), codes (the error codes it saw on the page, SC-…), v (its
 * version), b (the browser) and p (the Canvas page's path). All of it is shown before anything is
 * sent. The form goes to /api/report (site/src/report.js), which hands it to the one place its
 * owner named; when that is not set up, the page offers the report as a GitHub issue instead —
 * the words, the kind and the codes, never the email or the phone number, which would be public
 * there. Screenshots are picked, dropped or pasted; big ones are made smaller here first. */
(function () {
  const $ = (id) => document.getElementById(id);
  const form = $('report');
  const q = new URLSearchParams(location.search);
  const CATEGORIES = ['Bug', 'Error', 'Crash', 'Reason to disable', 'Missing feature', 'Feature request'];
  const MAX_SHOTS = 5, MAX_SIDE = 2000, MAX_TEXT = 5000;
  const ISSUES = 'https://github.com/26VirenS/BetterCourseViewer/issues/new';

  // ---- what Simpl sent along -------------------------------------------------------------------
  const clip = (v, n) => String(v || '').trim().slice(0, n);
  const meta = { version: clip(q.get('v'), 24), browser: clip(q.get('b'), 60), page: clip(q.get('p'), 300) };
  const cat = CATEGORIES.find((c) => c.toLowerCase() === String(q.get('c') || '').toLowerCase());
  if (cat) form.querySelector(`input[name="category"][value="${cat}"]`).checked = true;
  const codesIn = String(q.get('codes') || '').toUpperCase().split(/[\s,;]+/).filter((c) => /^SC-[A-Z0-9]{1,4}(-[A-Z0-9]{1,6})?$/.test(c));
  if (codesIn.length) $('codes').value = [...new Set(codesIn)].slice(0, 20).join(', ');
  const shown = [['Simpl version', meta.version], ['Browser', meta.browser], ['Canvas page', meta.page]].filter(([, v]) => v);
  if (shown.length) {
    $('sent').hidden = false;
    for (const [k, v] of shown) { const dt = document.createElement('dt'); dt.textContent = k; const dd = document.createElement('dd'); dd.textContent = v; $('meta').append(dt, dd); }
  }

  // ---- the words and their count ---------------------------------------------------------------
  const count = () => { $('count').textContent = `${$('text').value.length} / ${MAX_TEXT}`; };
  $('text').addEventListener('input', () => { count(); if ($('text').value.trim()) mark('textField', 'textErr', ''); });
  count();

  // ---- screenshots: picked, dropped or pasted; made smaller when big ---------------------------
  const shots = []; // { blob, name, url }
  /** A picture at most MAX_SIDE on its long side (a JPEG then, unless it was a small PNG). */
  async function shrink(file) {
    if (file.type === 'image/gif' || (file.size < 1.5 * 1024 * 1024 && file.type !== 'image/webp')) return file;
    try {
      const bmp = await createImageBitmap(file);
      const k = Math.min(1, MAX_SIDE / Math.max(bmp.width, bmp.height));
      const c = document.createElement('canvas');
      c.width = Math.round(bmp.width * k); c.height = Math.round(bmp.height * k);
      c.getContext('2d').drawImage(bmp, 0, 0, c.width, c.height);
      bmp.close?.();
      const blob = await new Promise((r) => c.toBlob(r, 'image/jpeg', 0.86));
      return blob ? new File([blob], (file.name || 'screenshot').replace(/\.\w+$/, '') + '.jpg', { type: 'image/jpeg' }) : file;
    } catch { return file; }
  }
  async function add(files) {
    $('shotErr').textContent = '';
    for (const f of files) {
      if (!f || !/^image\/(png|jpeg|webp|gif)$/.test(f.type)) { $('shotErr').textContent = 'Only pictures: PNG, JPEG, WebP or GIF.'; continue; }
      if (shots.length >= MAX_SHOTS) { $('shotErr').textContent = 'Five screenshots at most.'; break; }
      const blob = await shrink(f);
      if (blob.size > 8 * 1024 * 1024) { $('shotErr').textContent = 'That picture is over 8 MB, even made smaller.'; continue; }
      shots.push({ blob, name: blob.name || f.name || `screenshot-${shots.length + 1}.png`, url: URL.createObjectURL(blob) });
    }
    drawShots();
  }
  function drawShots() {
    $('thumbs').replaceChildren(...shots.map((s, i) => {
      const li = document.createElement('li');
      const img = document.createElement('img'); img.src = s.url; img.alt = `Screenshot ${i + 1}`;
      const x = document.createElement('button'); x.type = 'button'; x.textContent = '×'; x.setAttribute('aria-label', `Remove screenshot ${i + 1}`);
      x.addEventListener('click', () => { URL.revokeObjectURL(s.url); shots.splice(i, 1); drawShots(); });
      li.append(img, x);
      return li;
    }));
  }
  $('pick').addEventListener('click', () => $('files').click());
  $('drop').addEventListener('keydown', (e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); $('files').click(); } });
  $('files').addEventListener('change', (e) => { add([...e.target.files]); e.target.value = ''; });
  for (const ev of ['dragenter', 'dragover']) $('drop').addEventListener(ev, (e) => { e.preventDefault(); $('drop').classList.add('is-over'); });
  for (const ev of ['dragleave', 'drop']) $('drop').addEventListener(ev, () => $('drop').classList.remove('is-over'));
  $('drop').addEventListener('drop', (e) => { e.preventDefault(); add([...(e.dataTransfer?.files || [])]); });
  document.addEventListener('paste', (e) => { const files = [...(e.clipboardData?.files || [])].filter((f) => f.type.startsWith('image/')); if (files.length) { e.preventDefault(); add(files); } });

  // ---- checks before sending -------------------------------------------------------------------
  function mark(fieldId, errId, msg) { $(errId).textContent = msg; $(fieldId).classList.toggle('bad', !!msg); }
  function check() {
    let ok = true, first = null;
    const kind = form.querySelector('input[name="category"]:checked');
    $('kindErr').textContent = kind ? '' : 'Choose one.';
    if (!kind) { ok = false; first ||= form.querySelector('input[name="category"]'); }
    const text = $('text').value.trim();
    mark('textField', 'textErr', text ? '' : 'Say what happened.');
    if (!text) { ok = false; first ||= $('text'); }
    const email = $('email').value.trim();
    const emailBad = email && !/^[^\s@]{1,64}@[^\s@]{1,190}\.[^\s@]{2,}$/.test(email);
    mark('emailField', 'emailErr', emailBad ? 'That does not look like an email address.' : '');
    if (emailBad) { ok = false; first ||= $('email'); }
    const phone = $('phone').value.trim();
    const phoneBad = phone && !/^[+\d][\d\s().-]{4,28}$/.test(phone);
    mark('phoneField', 'phoneErr', phoneBad ? 'That does not look like a phone number.' : '');
    if (phoneBad) { ok = false; first ||= $('phone'); }
    if (first) first.focus();
    return ok;
  }
  form.addEventListener('change', (e) => { if (e.target.name === 'category') $('kindErr').textContent = ''; });
  // a field put right loses its warning as it is typed in, not only at the next Send
  $('email').addEventListener('input', () => { const v = $('email').value.trim(); if (!v || /^[^\s@]{1,64}@[^\s@]{1,190}\.[^\s@]{2,}$/.test(v)) mark('emailField', 'emailErr', ''); });
  $('phone').addEventListener('input', () => { const v = $('phone').value.trim(); if (!v || /^[+\d][\d\s().-]{4,28}$/.test(v)) mark('phoneField', 'phoneErr', ''); });

  // ---- sending ---------------------------------------------------------------------------------
  const status = (msg, bad = false, code = '') => {
    $('status').classList.toggle('is-bad', bad);
    $('status').replaceChildren(msg, ...(code ? [' ', Object.assign(document.createElement('code'), { textContent: code })] : []));
  };
  /** The report as a GitHub issue: the kind, the words, the codes and where it came from — not the email or the phone. */
  function issueLink() {
    const kind = form.querySelector('input[name="category"]:checked')?.value || 'Bug';
    const body = [
      $('text').value.trim(),
      '',
      $('codes').value.trim() ? `Error codes: ${$('codes').value.trim()}` : null,
      [meta.version && `Simpl ${meta.version}`, meta.browser, meta.page && `on ${meta.page}`].filter(Boolean).join(' · ') || null,
      shots.length ? `(${shots.length} screenshot${shots.length === 1 ? '' : 's'} to attach here)` : null,
    ].filter((l) => l !== null).join('\n');
    const title = `${kind}: ${$('text').value.trim().split('\n')[0].slice(0, 80)}`;
    return `${ISSUES}?${new URLSearchParams({ title, body: body.slice(0, 6000) })}`;
  }
  function offerIssue(reason) {
    const a = Object.assign(document.createElement('a'), { href: issueLink(), target: '_blank', rel: 'noopener', textContent: 'open it as a GitHub issue' });
    $('fallback').replaceChildren(`${reason} You can `, a, ' instead — your email and phone number are left out, since issues are public', shots.length ? ', and the screenshots are attached there by hand.' : '.');
    $('fallback').hidden = false;
  }
  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    $('fallback').hidden = true;
    if (!check()) { status('Some of it needs a look first.', true); return; }
    const body = new FormData(form);
    body.delete('screenshot');
    for (const s of shots) body.append('screenshot', s.blob, s.name);
    for (const [k, v] of Object.entries(meta)) if (v) body.set(k, v);
    $('send').disabled = true;
    status('Sending…');
    let res = null, data = null;
    try {
      res = await fetch('/api/report', { method: 'POST', body });
      data = await res.json().catch(() => null);
    } catch { /* no answer: below */ }
    $('send').disabled = false;
    if (res?.ok && data?.ok) {
      done(data.id);
      return;
    }
    if (!res) { status('The report could not be sent: no connection.', true, 'SC-B-NET'); offerIssue('Nothing came back from simplcourses.com.'); return; }
    const code = data?.code || `SC-B-${res.status}`;
    status(data?.message || 'The report could not be sent.', true, code);
    if (res.status === 503 || res.status === 404 || res.status === 405 || res.status >= 500) offerIssue(res.status === 503 ? 'Reports are not switched on here yet.' : 'The report could not be passed on.');
  });
  function done(id) {
    for (const s of shots) URL.revokeObjectURL(s.url);
    const card = $('card');
    card.replaceChildren();
    const box = document.createElement('div');
    box.className = 'done';
    box.innerHTML = '<span class="tick" aria-hidden="true"><svg viewBox="0 0 24 24" width="30" height="30" fill="none" stroke="#fff" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6L9 17l-5-5"/></svg></span><h2>Thanks — it is on its way.</h2>';
    const p = document.createElement('p');
    p.append('Your report is ', Object.assign(document.createElement('span'), { className: 'code', textContent: id }), '. Quote it if you write to us about it.');
    const back = Object.assign(document.createElement('a'), { href: '/', textContent: 'Back to Simpl Courses' });
    box.append(p, back);
    card.append(box);
    box.setAttribute('tabindex', '-1');
    box.focus();
    document.title = 'Report sent — Simpl Courses';
  }
})();
