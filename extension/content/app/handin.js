/* (2.99.135) Hand in from your phone, as the Mac and Windows apps have it (Send from Phone): the phone sends its files
 * here, they go into the hand-in as if chosen with Choose Files, and the student looks them over and presses Submit on
 * this computer — nothing is ever handed in by itself. The apps take the files over a server of their own on the Wi-Fi
 * (mac/Simpl/Support/PhoneServer.swift); an extension cannot run one, so they pass through a relay on simplcourses.com
 * (site/src/handin.js), sealed with a key only this page and the phone hold.
 *
 * This page makes one secret, s, and shows it in the QR code: https://simplcourses.com/handin/#<s> — the phone's page
 * (site/public/handin/index.html), the secret in the part of the address no server is sent. From s both work out the
 * hand-in's id, the AES-256-GCM key and the phone's token (HKDF-SHA-256); this page's own token is made apart and never
 * leaves it but to the relay. The assignment's facts go up sealed (the offer: its name, its course, the types it takes,
 * whether photos become one PDF); the phone sends each file as its sealed description, then its sealed 512 KiB parts;
 * this page asks every second or two what has come, fetches each part, opens it, checks the file against its
 * description, takes it (the relay forgets it at once) and adds it to the hand-in. A photo, where the assignment takes a
 * PDF and no pictures, comes as a page (the phone made it upright and small enough) and goes into one PDF named after
 * the assignment, made again with each page.
 *
 * Every request goes through the background (background.js handin: a school's Canvas need not let its page reach
 * another site), which never sees the key. The sheet closed, the relay forgets the files and the offer; Submit gone
 * through, the phone is told ("Handed in ✓"); 15 minutes with nothing from the phone, the code stops. Where the relay
 * cannot be reached at all, the hand-in falls back to the old code: the assignment, opened in Canvas on the phone
 * (screens/submit.js canvasSheet). Loaded on demand with the QR code's library (content/app/lazy-modules.js). */
(function (root) {
  const BCV = (root.BCV = root.BCV || {});
  const enc = new TextEncoder();
  const dec = new TextDecoder();
  const subtle = () => root.crypto.subtle;

  // ---- the protocol (site/src/handin.js holds the same numbers) -----------------------------------------------
  const LIMITS = { chunk: 512 * 1024, files: 10, total: 50 * 1024 * 1024 };
  const IMAGES = ['jpg', 'jpeg', 'png', 'heic', 'heif', 'gif', 'webp'];
  const IDLE = 15 * 60e3; // (nothing from the phone this long: the code stops, as the apps' does)

  const rand = (n) => { const b = new Uint8Array(n); root.crypto.getRandomValues(b); return b; };
  const b64 = (u8) => { let s = ''; for (let i = 0; i < u8.length; i += 0x8000) s += String.fromCharCode(...u8.subarray(i, i + 0x8000)); return btoa(s); };
  const unb64 = (s) => { const t = atob(String(s || '')); const out = new Uint8Array(t.length); for (let i = 0; i < t.length; i++) out[i] = t.charCodeAt(i); return out; };
  const b64u = (u8) => b64(u8).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  const unb64u = (s) => { const t = String(s); return unb64(t.replace(/-/g, '+').replace(/_/g, '/') + '='.repeat((4 - (t.length % 4)) % 4)); };
  /** What a sealed piece is bound to: the hand-in and its place in it ("offer", "meta|<f>", "chunk|<f>|<i>"). */
  const aad = (id, ...where) => enc.encode(['simpl-handin/1', id, ...where].join('|'));

  /** The hand-in's id, key and the phone's token, from the secret in the QR code (HKDF-SHA-256). */
  async function derive(secret) {
    const ikm = await subtle().importKey('raw', unb64u(secret), 'HKDF', false, ['deriveBits']);
    const bits = async (info, n) => new Uint8Array(await subtle().deriveBits({ name: 'HKDF', hash: 'SHA-256', salt: enc.encode('simpl-handin-v1'), info: enc.encode(info) }, ikm, n * 8));
    const id = b64u(await bits('id', 16));
    const send = b64u(await bits('send', 32));
    const key = await subtle().importKey('raw', await bits('key', 32), { name: 'AES-GCM' }, false, ['encrypt', 'decrypt']);
    return { id, key, send };
  }
  /** Sealed: a fresh 12-byte nonce, then the bytes under AES-256-GCM, bound to `ad`. */
  async function seal(key, ad, plain) {
    const iv = rand(12);
    const body = new Uint8Array(await subtle().encrypt({ name: 'AES-GCM', iv, additionalData: ad }, key, plain));
    const out = new Uint8Array(12 + body.length);
    out.set(iv, 0);
    out.set(body, 12);
    return out;
  }
  /** Opened again (throws when the key, the place or a byte is wrong). */
  async function open(key, ad, sealed) {
    const u = new Uint8Array(sealed);
    if (u.length < 12 + 16) throw new Error('short');
    return new Uint8Array(await subtle().decrypt({ name: 'AES-GCM', iv: u.subarray(0, 12), additionalData: ad }, key, u.subarray(12)));
  }

  /** A JPEG's size and colours, from its frame header (null: not a JPEG this can put in a PDF). */
  function jpegInfo(u) {
    if (!u || u.length < 4 || u[0] !== 0xff || u[1] !== 0xd8) return null;
    let i = 2;
    while (i + 9 < u.length) {
      if (u[i] !== 0xff) return null;
      const m = u[i + 1];
      if (m === 0xff) { i += 1; continue; }
      if (m === 0x01 || (m >= 0xd0 && m <= 0xd8)) { i += 2; continue; }
      if (m === 0xd9 || m === 0xda) return null; // (the picture began, or ended, before its size was said)
      const len = (u[i + 2] << 8) | u[i + 3];
      if (m >= 0xc0 && m <= 0xcf && m !== 0xc4 && m !== 0xc8 && m !== 0xcc) {
        const h = (u[i + 5] << 8) | u[i + 6], w = (u[i + 7] << 8) | u[i + 8], comps = u[i + 9];
        return w > 0 && h > 0 && [1, 3, 4].includes(comps) ? { w, h, comps } : null;
      }
      if (len < 2) return null;
      i += 2 + len;
    }
    return null;
  }
  /** Text as PDF bytes (one byte a character); a title as UTF-16, as a PDF reads a string that is not plain ASCII. */
  const latin = (s) => { const out = new Uint8Array(s.length); for (let i = 0; i < s.length; i++) out[i] = s.charCodeAt(i) & 0xff; return out; };
  const pdfText = (s) => `<FEFF${[...String(s)].map((c) => { const p = c.codePointAt(0); const u = p > 0xffff ? [0xd800 + ((p - 0x10000) >> 10), 0xdc00 + ((p - 0x10000) & 0x3ff)] : [p]; return u.map((x) => x.toString(16).padStart(4, '0')).join(''); }).join('').toUpperCase()}>`;
  /** (as the apps' PhotoPDF) The photos as one PDF, a page each in the order they came: each JPEG put in as it is
   *  (nothing decoded here), laid on a Letter page — turned to landscape for a wide photo — with 18 points around it. */
  function pdfOf(pages, { title = '' } = {}) {
    const parts = [], offsets = [];
    let at = 0;
    const put = (x) => { const b = typeof x === 'string' ? latin(x) : x; parts.push(b); at += b.length; };
    const obj = (n, ...body) => { offsets[n] = at; put(`${n} 0 obj\n`); for (const b of body) put(b); put('\nendobj\n'); };
    const n = pages.length, info = 3 + 3 * n;
    put('%PDF-1.4\n%\xe2\xe3\xcf\xd3\n');
    obj(1, '<< /Type /Catalog /Pages 2 0 R >>');
    obj(2, `<< /Type /Pages /Count ${n} /Kids [${pages.map((_, k) => `${3 + 3 * k} 0 R`).join(' ')}] >>`);
    pages.forEach((p, k) => {
      const wide = p.w > p.h, W = wide ? 792 : 612, H = wide ? 612 : 792, margin = 18;
      const fit = Math.min((W - margin * 2) / p.w, (H - margin * 2) / p.h);
      const w = p.w * fit, h = p.h * fit;
      const draw = `q ${w.toFixed(2)} 0 0 ${h.toFixed(2)} ${((W - w) / 2).toFixed(2)} ${((H - h) / 2).toFixed(2)} cm /P Do Q`;
      const cs = p.comps === 1 ? '/DeviceGray' : p.comps === 4 ? '/DeviceCMYK /Decode [1 0 1 0 1 0 1 0]' : '/DeviceRGB';
      obj(3 + 3 * k, `<< /Type /Page /Parent 2 0 R /MediaBox [0 0 ${W} ${H}] /Resources << /XObject << /P ${5 + 3 * k} 0 R >> >> /Contents ${4 + 3 * k} 0 R >>`);
      obj(4 + 3 * k, `<< /Length ${draw.length} >>\nstream\n`, draw, '\nendstream');
      obj(5 + 3 * k, `<< /Type /XObject /Subtype /Image /Width ${p.w} /Height ${p.h} /ColorSpace ${cs} /BitsPerComponent 8 /Filter /DCTDecode /Length ${p.jpeg.length} >>\nstream\n`, p.jpeg, '\nendstream');
    });
    obj(info, `<< /Producer (Simpl Courses)${title ? ` /Title ${pdfText(title)}` : ''} >>`);
    const xref = at;
    put(`xref\n0 ${info + 1}\n0000000000 65535 f \n`);
    for (let k = 1; k <= info; k++) put(`${String(offsets[k]).padStart(10, '0')} 00000 n \n`);
    put(`trailer\n<< /Size ${info + 1} /Root 1 0 R /Info ${info} 0 R >>\nstartxref\n${xref}\n%%EOF\n`);
    const out = new Uint8Array(at);
    let o = 0;
    for (const b of parts) { out.set(b, o); o += b.length; }
    return out;
  }
  /** A file's name as the phone gave it, made safe for a hand-in: no folders, nothing unprintable, not too long. */
  function cleanName(name) {
    let s = String(name || '').replace(/[\u0000-\u001f\u007f]+/g, '').split(/[\\/]/).pop().trim().replace(/^\.+/, '');
    if (s.length > 120) { const dot = s.lastIndexOf('.'); const ext = dot > 0 && s.length - dot <= 12 ? s.slice(dot) : ''; s = s.slice(0, 120 - ext.length).trim() + ext; }
    return s || 'From your phone';
  }
  /** What the phone's camera may send (as the apps decide it): "yes" (photos as they are), "pdf" (pages of one PDF), "no". */
  function photosRule(allowed, convertible) {
    if (!allowed.length || allowed.some((x) => IMAGES.includes(x))) return 'yes';
    if (allowed.includes('pdf')) return 'pdf';
    return convertible.some((x) => IMAGES.includes(x)) ? 'yes' : 'no';
  }
  /** The sealed offer's facts: what the phone's page says it is handing in to, and what it lets through. */
  function offerOf({ title = '', course = '', color = '', allowed = [], convertible = [], zipOK = false, pdfName = '' }) {
    const a = allowed.map((x) => String(x).toLowerCase().replace(/^\./, '')), c = convertible.map((x) => String(x).toLowerCase().replace(/^\./, ''));
    const photos = photosRule(a, c);
    // (no types named, or a .zip taken — anything else goes zipped: the phone may send anything)
    const accept = !a.length || zipOK ? [] : [...new Set([...a, ...c, ...(photos === 'no' ? [] : IMAGES)])];
    return { v: 1, title: String(title).slice(0, 300), course: String(course).slice(0, 200), color: /^#[0-9a-f]{6}$/i.test(color || '') ? color : '', allowed: a, accept, photos, pdfName: photos === 'pdf' ? pdfName : '', files: LIMITS.files, total: LIMITS.total, chunk: LIMITS.chunk, device: 'computer' };
  }

  const core = { LIMITS, IMAGES, derive, seal, open, aad, jpegInfo, pdfOf, cleanName, photosRule, offerOf, b64u, unb64u, rand };
  BCV.handinCore = core;
  if (!root.document) return; // (a test in Node: the protocol alone)

  // ---- the relay, through the background ----------------------------------------------------------------------
  /** A request to the relay: { status, json, bytes } — status 0 when it could not be reached at all. */
  async function relay(method, path, token, { json, bytes, chunks } = {}) {
    let r = null;
    try { r = await BCV.api.runtime.sendMessage({ type: 'handin', host: location.host, method, path, token, json, bytes: bytes ? b64(bytes) : undefined, chunks }); } catch { r = null; }
    if (!r || !r.status) return { status: 0, json: null, bytes: null };
    return { status: r.status, json: r.json || null, bytes: typeof r.bytes === 'string' && r.bytes ? unb64(r.bytes) : null };
  }
  const why = (r, words) => Object.assign(new Error(r.json?.message || words), { code: r.json?.code || (r.status ? `SC-H-${r.status}` : 'SC-H-NET'), status: r.status });

  /** A hand-in started on the relay: its secret, id, key and two tokens, and the link the QR code holds. */
  async function begin(offer) {
    const secret = b64u(rand(32)), read = b64u(rand(32));
    const k = await derive(secret);
    const sealed = await seal(k.key, aad(k.id, 'offer'), enc.encode(JSON.stringify(offer)));
    let base = '';
    try { base = String((await BCV.api.runtime.sendMessage({ type: 'handin', op: 'base', host: location.host }))?.base || ''); } catch { base = ''; }
    if (!/^https?:\/\//.test(base)) throw Object.assign(new Error('Simpl could not reach simplcourses.com.'), { code: 'SC-H-NET' });
    const r = await relay('PUT', `/api/handin/${k.id}`, read, { json: { send: k.send, offer: b64u(sealed) } });
    if (r.status !== 200 && r.status !== 201) throw why(r, 'Simpl could not reach simplcourses.com.');
    return { ...k, secret, read, link: `${base}/handin/#${secret}` };
  }

  // ---- the sheet ----------------------------------------------------------------------------------------------
  const fmtSize = (n) => (n >= 1048576 ? `${(n / 1048576).toFixed(n >= 10485760 ? 0 : 1)} MB` : n >= 1024 ? `${Math.round(n / 1024)} KB` : `${n} B`);
  const extOf = (name) => (/\.([a-z0-9]+)$/i.exec(String(name || ''))?.[1] || '').toLowerCase();
  const TYPES = { pdf: 'application/pdf', jpg: 'image/jpeg', jpeg: 'image/jpeg', png: 'image/png', heic: 'image/heic', heif: 'image/heif', gif: 'image/gif', webp: 'image/webp', txt: 'text/plain', doc: 'application/msword', docx: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document', zip: 'application/zip' };

  /** The sheet: started on the relay first (a rejection, with its code, when it cannot be: the caller falls back), then
   *  shown. `o`: { from, title, course, color, allowed, convertible, zipOK, pdfName, alive(), add(file) → how many went
   *  into the hand-in, page({ jpeg, w, h, comps }) → its page number }. Resolves to { handedIn(), close() }. */
  async function sheet(o) {
    const { h, overlayRoot } = BCV.utils;
    const U = BCV.ui;
    const IC = BCV.IC;
    const offer = offerOf(o);
    let s = await begin(offer); // (throws: the caller shows the old code instead)

    let phase = 'waiting', closed = false, timer = 0, misses = 0, lastSeen = Date.now(), pulling = false;
    const handed = [];
    const items = new Map(); // f → { f, seq, chunks, got, meta, parts, state, n, el }
    const list = U.el('bcv-phone__list');
    const ov = U.el('bcv-sheet-ov bcv-phone-ov', null, { role: 'dialog', 'aria-label': 'Hand in from your phone' });
    const body = U.el('bcv-phone__body');
    const dot = h('span', { class: 'bcv-phone__dot' });
    const status = U.text('bcv-phone__status', 'Waiting for your phone…', 'span');
    const wait = U.el('bcv-phone__wait', [dot, status], { role: 'status', 'aria-live': 'polite' });
    const copy = h('button', { type: 'button', class: 'bcv-sb__folder bcv-phone__copy', text: 'Copy link', title: 'The code’s link, to open on your phone another way', onclick: async () => {
      try { await navigator.clipboard.writeText(s.link); copy.textContent = 'Copied'; setTimeout(() => { copy.textContent = 'Copy link'; }, 1600); } catch { U.toast('The link could not be copied.', { error: true }); }
    } });
    const done = U.btn('Done', { cls: 'bcv-phone__done', title: 'Close — then look over your hand-in and press Submit', onClick: () => close() });
    const btns = U.el('bcv-prompt__btns', [wait, h('span', { style: { flex: '1' } }), copy, done]);
    ov.append(U.el('bcv-sheet bcv-sheet--prompt bcv-sheet--phone', [
      U.el('bcv-sheet__head', [
        U.el('bcv-sheet__titles', [U.text('bcv-sheet__title bcv-pretty', 'Hand in from your phone')]),
        h('button', { type: 'button', class: 'bcv-sheet__close', 'aria-label': 'Close', onclick: () => close() }, U.svg(IC.close, { size: 13, stroke: 'var(--bcv-ink2)', width: 2.3 })),
      ]),
      U.el('bcv-prompt bcv-phone', [body, btns]),
    ]));
    ov.addEventListener('click', (e) => { if (e.target === ov) close(); });
    ov.addEventListener('keydown', (e) => { if (e.key === 'Escape') { e.stopPropagation(); close(); } });

    const added = () => [...items.values()].filter((it) => it.state === 'added').length + ([...items.values()].some((it) => it.state === 'page') ? 1 : 0);
    function say(words, kind = '') {
      status.textContent = words;
      wait.className = `bcv-phone__wait ${kind}`;
    }
    function sayDone() {
      const n = added();
      const coming = [...items.values()].find((it) => it.state === 'receiving');
      if (coming) say(`Receiving${coming.meta ? ` ${coming.meta.name}` : ''} · ${Math.round(((coming.parts?.length || 0) / coming.chunks) * 100)}%`, 'is-busy');
      else if (n) say(n === 1 ? '1 file in your hand-in' : `${n} files in your hand-in`, 'is-done');
      else say(phase === 'waiting' ? 'Waiting for your phone…' : 'Phone connected', phase === 'waiting' ? '' : 'is-on');
      done.classList.toggle('bcv-btn--primary', n > 0);
    }

    // ---- what the sheet shows, by phase ----
    function draw() {
      copy.hidden = phase === 'idle' || phase === 'gone';
      done.hidden = phase === 'waiting';
      if (phase === 'idle' || phase === 'gone') {
        const idle = phase === 'idle';
        body.replaceChildren(U.el('bcv-phone__problem', [
          U.text('bcv-phone__lead', idle ? 'Stopped after 15 minutes without use.' : 'This code has expired.'),
          U.text('bcv-phone__note bcv-pretty', added() ? 'What your phone sent is in your hand-in. Start again for a new code.' : 'Start again to get a new code.'),
          h('div', {}, U.btn('Start Again', { kind: 'primary', cls: 'bcv-phone__again', onClick: () => again() })),
        ]));
        say(idle ? 'Stopped' : 'Expired', 'is-idle');
        return;
      }
      const connected = phase === 'connected';
      const qr = qrCode(s.link);
      if (connected) qr.classList.add('is-small');
      const side = connected
        ? U.el('bcv-phone__side', [U.text('bcv-phone__lead', 'Phone connected'), U.text('bcv-phone__note bcv-pretty', 'Take photos or choose files on your phone. Each one comes here, into this hand-in.')])
        : U.el('bcv-phone__steps', [
          U.text('bcv-phone__step', '1. Point your phone’s camera at the code.'),
          U.text('bcv-phone__step', '2. Open the link it shows: it opens this assignment.'),
          U.text('bcv-phone__step', '3. Take a photo or choose files. They come here, into this hand-in.'),
          U.text('bcv-phone__note bcv-pretty', 'Nothing is handed in until you press Submit here. On the way your files are sealed with a key only this computer and your phone hold.'),
        ]);
      const parts = [U.el(`bcv-phone__row ${connected ? 'is-connected' : ''}`, [qr, side])];
      if (offer.photos === 'pdf') parts.push(U.text('bcv-phone__combine bcv-pretty', `Photos you take become the pages of one PDF, “${offer.pdfName}” — you can turn that off on your phone.`));
      if (connected) {
        if (!items.size && !list.childNodes.length) list.append(U.text('bcv-phone__empty bcv-pretty', 'Nothing yet — what you send shows here, and goes into your hand-in.'));
        parts.push(list);
      }
      body.replaceChildren(...parts);
      sayDone();
    }
    /** A file the phone has begun to send, its row put at the end of the list (the rest left as they are). */
    function addRow(it) {
      list.querySelector('.bcv-phone__empty')?.remove();
      list.append(it.el = rowOf(it));
      list.scrollTop = list.scrollHeight;
    }
    function rowOf(it) {
      const name = U.text('bcv-phone__iname bcv-ellip', '');
      const sub = U.text('bcv-phone__isub', '');
      const fill = h('div', { class: 'bcv-sb__barfill' });
      const bar = h('div', { class: 'bcv-sb__bar' }, fill);
      const kind = h('span', { class: 'bcv-sb__kind' });
      const row = U.el('bcv-phone__item', [kind, U.el('bcv-sb__fbody', [name, sub, bar])]);
      it.paint = () => {
        const nm = it.meta?.name || 'A file';
        name.textContent = nm;
        kind.textContent = (extOf(nm) || 'file').toUpperCase().slice(0, 4);
        const pct = Math.round(((it.parts?.length || 0) / it.chunks) * 100);
        const words = {
          receiving: `Receiving… ${pct}%${it.meta ? ` of ${fmtSize(it.meta.size)}` : ''}`,
          added: 'Added to your hand-in',
          page: `Page ${it.n} of ${offer.pdfName}`,
          skipped: 'Not added',
          lost: 'Didn’t arrive — send it again from your phone',
          unreadable: 'This photo couldn’t be read',
        }[it.state];
        sub.textContent = words;
        sub.className = `bcv-phone__isub ${it.state === 'added' || it.state === 'page' ? 'is-ok' : it.state === 'lost' || it.state === 'unreadable' ? 'is-warn' : ''}`;
        bar.hidden = it.state !== 'receiving';
        fill.style.width = `${pct}%`;
        row.dataset.state = it.state;
      };
      it.paint();
      return row;
    }

    // ---- the relay asked every second or two (every five out of sight) ----
    const path = () => `/api/handin/${s.id}`;
    const schedule = (ms) => { clearTimeout(timer); if (!closed) timer = setTimeout(tick, document.hidden ? Math.max(ms, 5000) : ms); };
    async function tick() {
      if (closed) return;
      if (!o.alive() || !ov.isConnected) { close(); return; }
      if (phase === 'idle' || phase === 'gone') return;
      const r = await relay('GET', path(), s.read);
      if (closed) return;
      if (r.status === 404) { phase = 'gone'; draw(); return; }
      if (r.status !== 200 || !r.json) {
        misses += 1;
        say('Can’t reach simplcourses.com — trying again…', 'is-idle');
        schedule(Math.min(10000, 1000 * 2 ** Math.min(misses, 4)));
        return;
      }
      misses = 0;
      const look = r.json;
      let moved = false;
      if (look.opened && phase === 'waiting') { phase = 'connected'; moved = true; lastSeen = Date.now(); draw(); }
      for (const x of look.files || []) {
        let it = items.get(x.f);
        if (!it) {
          it = { f: x.f, seq: x.seq, chunks: x.chunks, got: 0, meta: null, parts: [], state: 'receiving' };
          items.set(x.f, it);
          moved = true;
          if (phase === 'waiting') { phase = 'connected'; draw(); } // (a file before the offer was seen to be read: the phone has it)
          addRow(it);
        }
        if (x.got !== it.got) { it.got = x.got; moved = true; }
      }
      if (moved) lastSeen = Date.now();
      await pull();
      if (closed) return;
      sayDone();
      if (Date.now() - lastSeen > IDLE) { phase = 'idle'; await forget(); draw(); return; }
      schedule(phase === 'connected' ? 1000 : 2000);
    }
    /** What has come, fetched, opened and checked, a file at a time in the order sent; each one whole given to the
     *  hand-in (the photos' pages kept in order: a file waits for the one before it). */
    async function pull() {
      if (pulling) return;
      pulling = true;
      try {
        for (const it of [...items.values()].sort((a, b) => a.seq - b.seq)) {
          if (closed) return;
          if (it.state !== 'receiving') continue;
          if (!it.meta) {
            const r = await relay('GET', `${path()}/file/${it.f}`, s.read);
            if (r.status !== 200 || !r.bytes) return;
            try {
              const m = JSON.parse(dec.decode(await open(s.key, aad(s.id, 'meta', it.f), r.bytes)));
              const size = Number(m.size);
              if (!Number.isInteger(size) || size < 1 || size > LIMITS.total || Math.ceil(size / LIMITS.chunk) !== it.chunks || Number(m.chunks) !== it.chunks) throw new Error('meta');
              it.meta = { name: cleanName(m.name), type: String(m.type || '').slice(0, 120), size, page: !!m.page && offer.photos === 'pdf' };
            } catch { await lost(it); continue; }
            it.paint?.();
          }
          while (it.parts.length < it.got) {
            const i = it.parts.length;
            const r = await relay('GET', `${path()}/file/${it.f}/${i}`, s.read);
            if (closed) return;
            if (r.status !== 200 || !r.bytes) return;
            let part;
            try { part = await open(s.key, aad(s.id, 'chunk', it.f, String(i)), r.bytes); } catch { await lost(it); break; }
            it.parts.push(part);
            it.paint?.();
            sayDone();
          }
          if (it.state !== 'receiving') continue;
          if (it.parts.length < it.chunks) return; // (still coming: the files after it wait their turn)
          const size = it.parts.reduce((n, p) => n + p.length, 0);
          if (size !== it.meta.size) { await lost(it); continue; }
          await relay('DELETE', `${path()}/file/${it.f}`, s.read); // (taken: the relay forgets it now)
          await deliver(it);
        }
      } finally { pulling = false; }
    }
    async function lost(it) {
      it.state = 'lost';
      it.parts = [];
      it.paint?.();
      await relay('DELETE', `${path()}/file/${it.f}`, s.read);
    }
    async function deliver(it) {
      const parts = it.parts;
      it.parts = Array(it.chunks); // (the bytes go to the hand-in; the row keeps its 100%)
      if (it.meta.page) {
        const jpeg = parts.length === 1 ? parts[0] : new Uint8Array(await new Blob(parts).arrayBuffer());
        const info = jpegInfo(jpeg);
        if (!info) { it.state = 'unreadable'; it.paint?.(); return; }
        try { it.n = await o.page({ jpeg, ...info }); it.state = 'page'; } catch { it.state = 'unreadable'; }
        it.paint?.();
        return;
      }
      const type = it.meta.type && /^[\w.+-]+\/[\w.+-]+$/.test(it.meta.type) ? it.meta.type : TYPES[extOf(it.meta.name)] || 'application/octet-stream';
      const file = new File(parts, it.meta.name, { type });
      let n = 0;
      try { n = Number(await o.add(file)) || 0; } catch { n = 0; }
      it.state = n > 0 ? 'added' : 'skipped';
      it.paint?.();
    }

    /** The relay told to forget the hand-in: the phone can send no more, and what it sent is gone from there. */
    async function forget() {
      clearTimeout(timer);
      const was = s;
      if (!was || was.forgot) return;
      was.forgot = true;
      handed.push(was);
      await relay('DELETE', `/api/handin/${was.id}`, was.read);
    }
    async function again() {
      try { s = await begin(offer); } catch (e) { U.toast(`Could not start again: ${e.message}`, { error: true, code: e.code }); return; }
      if (closed) { forget(); return; }
      items.clear();
      list.replaceChildren();
      phase = 'waiting';
      lastSeen = Date.now();
      draw();
      schedule(2000);
    }
    function close() {
      if (closed) return;
      closed = true;
      clearTimeout(timer);
      removeEventListener('pagehide', onHide);
      forget();
      U.dismiss(ov);
    }
    const onHide = () => { if (!closed) forget(); };
    addEventListener('pagehide', onHide);

    draw();
    overlayRoot().append(ov);
    if (o.from) U.morphFrom(ov.firstElementChild, o.from);
    ov.tabIndex = -1;
    setTimeout(() => ov.focus({ preventScroll: true }), 30);
    schedule(1500);
    return {
      close,
      get link() { return s.link; },
      /** Submit went through: each phone that sent here is told it is handed in. */
      handedIn() {
        close();
        for (const x of handed.splice(0)) relay('POST', `/api/handin/${x.id}/status`, x.read, { json: { state: 'handed-in' } });
      },
    };
  }

  /** The link as a QR code (qrcode-generator, lib/vendor/qrcode.js), drawn as one path: dark on white in either theme
   *  — a camera reads it so — with the quiet margin a reader needs around it. */
  function qrCode(text, label = 'QR code: scan it with your phone’s camera') {
    const q = root.qrcode(0, 'M');
    q.addData(text);
    q.make();
    const n = q.getModuleCount(), pad = 4, size = n + pad * 2;
    let d = '';
    for (let r = 0; r < n; r++) for (let c = 0; c < n; c++) if (q.isDark(r, c)) d += `M${c + pad} ${r + pad}h1v1h-1z`;
    const NS = 'http://www.w3.org/2000/svg';
    const svg = document.createElementNS(NS, 'svg');
    svg.setAttribute('viewBox', `0 0 ${size} ${size}`);
    svg.setAttribute('class', 'bcv-phone__qr');
    svg.setAttribute('role', 'img');
    svg.setAttribute('aria-label', label);
    svg.setAttribute('shape-rendering', 'crispEdges');
    const bg = document.createElementNS(NS, 'rect');
    bg.setAttribute('width', size);
    bg.setAttribute('height', size);
    bg.setAttribute('fill', '#fff');
    const path = document.createElementNS(NS, 'path');
    path.setAttribute('d', d);
    path.setAttribute('fill', '#000');
    svg.append(bg, path);
    return svg;
  }

  BCV.handin = { core, sheet, qrCode, begin, relay };
})(typeof self !== 'undefined' ? self : globalThis);
