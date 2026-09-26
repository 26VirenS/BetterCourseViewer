// simplcourses.com/api/report — where the Report a bug page sends a report (site/public/report/).
//
// Nothing is kept here: a report is checked, given an id, and handed on to the one place its
// owner named — the REPORT_WEBHOOK secret on the Cloudflare project (a secret outlives every
// deploy, where a variable set in the dashboard would not): a Discord channel's webhook (the
// report and its screenshots as one message), a Slack incoming webhook (the words alone), or any
// other address (the report as JSON, the screenshots in it as data URLs). With no secret set the
// answer says so (SC-B-503) and the page offers the report as a GitHub issue instead — without the
// email and the phone number, which would be public there.
//
// One module, two ways in: src/worker.js (a Worker serving public/, Cloudflare's "Import a
// repository") and functions/api/report.js (Pages Functions, Cloudflare Pages). No bindings are
// declared, so neither deploy can fail for want of one.
//
// Every refusal carries a code of the site's own (SC-B-…), the way Simpl's own errors carry theirs
// (SC-…): the page shows it, and a report can quote it.

export const CATEGORIES = ['Bug', 'Error', 'Crash', 'Reason to disable', 'Missing feature', 'Feature request'];
export const LIMITS = { text: 5000, shots: 5, shotBytes: 8 * 1024 * 1024, body: 32 * 1024 * 1024, codes: 20, field: 200, perWindow: 6, windowMs: 10 * 60 * 1000 };
const SHOT_TYPES = new Set(['image/png', 'image/jpeg', 'image/webp', 'image/gif']);
const CODE_RE = /^SC-[A-Z0-9]{1,4}(?:-[A-Z0-9]{1,6})?$/;

const json = (body, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' } });
const refuse = (status, code, message) => json({ ok: false, code, message }, status);

/** A report's id: short enough to read out, unlikely to repeat (SR- then the time and four random letters). */
export function reportId(now = Date.now(), rand = Math.random) {
  const tail = Array.from({ length: 4 }, () => 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789'[Math.floor(rand() * 32)]).join('');
  return `SR-${now.toString(36).toUpperCase()}-${tail}`;
}

/** The codes typed or carried in: SC-… alone, upper-cased, each once, twenty at most. */
export function cleanCodes(raw) {
  const out = [];
  for (const c of String(raw || '').toUpperCase().split(/[\s,;]+/)) if (CODE_RE.test(c) && !out.includes(c)) out.push(c);
  return out.slice(0, LIMITS.codes);
}

const clip = (v, n = LIMITS.field) => String(v ?? '').replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f]/g, '').trim().slice(0, n);
const EMAIL_RE = /^[^\s@]{1,64}@[^\s@]{1,190}\.[^\s@]{2,}$/;
const PHONE_RE = /^[+\d][\d\s().-]{4,28}$/;

// a few reports per address per ten minutes, per isolate: best effort, nothing stored
const seen = new Map();
function tooMany(ip, now = Date.now()) {
  if (!ip) return false;
  const recent = (seen.get(ip) || []).filter((t) => now - t < LIMITS.windowMs);
  recent.push(now);
  seen.set(ip, recent);
  if (seen.size > 5000) seen.clear();
  return recent.length > LIMITS.perWindow;
}

/** The form read and checked: the report, or a refusal. */
export async function readReport(request) {
  const len = Number(request.headers.get('content-length') || 0);
  if (len > LIMITS.body) return { refusal: refuse(413, 'SC-B-413', 'That report is too large. Send fewer or smaller screenshots.') };
  let form;
  try { form = await request.formData(); } catch { return { refusal: refuse(400, 'SC-B-400', 'That did not arrive as a form.') }; }
  if (clip(form.get('website'))) return { dropped: true }; // (the field people never see: filled in, it was not a person)
  const category = clip(form.get('category'), 40);
  if (!CATEGORIES.includes(category)) return { refusal: refuse(400, 'SC-B-KIND', 'Choose what kind of report this is.') };
  const text = String(form.get('text') ?? '').trim().slice(0, LIMITS.text);
  if (!text) return { refusal: refuse(400, 'SC-B-TEXT', 'Say what happened.') };
  const email = clip(form.get('email'));
  if (email && !EMAIL_RE.test(email)) return { refusal: refuse(400, 'SC-B-EMAIL', 'That email address does not look right.') };
  const phone = clip(form.get('phone'), 32);
  if (phone && !PHONE_RE.test(phone)) return { refusal: refuse(400, 'SC-B-PHONE', 'That phone number does not look right.') };
  const shots = [];
  for (const f of form.getAll('screenshot')) {
    if (!f || typeof f === 'string' || !f.size) continue;
    if (shots.length >= LIMITS.shots) return { refusal: refuse(413, 'SC-B-SHOTS', `Five screenshots at most.`) };
    if (!SHOT_TYPES.has(f.type)) return { refusal: refuse(415, 'SC-B-415', 'Screenshots have to be pictures (PNG, JPEG, WebP or GIF).') };
    if (f.size > LIMITS.shotBytes) return { refusal: refuse(413, 'SC-B-SIZE', 'A screenshot is over 8 MB.') };
    shots.push({ name: clip(f.name || `screenshot-${shots.length + 1}`, 80), type: f.type, bytes: new Uint8Array(await f.arrayBuffer()) });
  }
  return {
    report: {
      category, text, email, phone, shots,
      codes: cleanCodes(form.get('codes')),
      version: clip(form.get('version'), 24),
      browser: clip(form.get('browser'), 60),
      page: clip(form.get('page'), 300),
      agent: clip(request.headers.get('user-agent'), 300),
    },
  };
}

/** The report in words, for a message: what kind, what happened, the codes and where it came from. */
export function summary(r, id) {
  return [
    `${r.category} · ${id}`,
    '',
    r.text,
    '',
    r.codes.length ? `Codes: ${r.codes.join(', ')}` : null,
    [r.version && `Simpl ${r.version}`, r.browser, r.page && `on ${r.page}`].filter(Boolean).join(' · ') || null,
    r.email ? `Email: ${r.email}` : null,
    r.phone ? `Phone: ${r.phone}` : null,
    r.shots.length ? `${r.shots.length} screenshot${r.shots.length === 1 ? '' : 's'}` : null,
  ].filter((l) => l !== null).join('\n');
}

const b64 = (bytes) => { let s = ''; for (let i = 0; i < bytes.length; i += 0x8000) s += String.fromCharCode(...bytes.subarray(i, i + 0x8000)); return btoa(s); };

/** The report handed to the webhook the owner named, in the shape that address takes. */
export async function deliver(r, id, hook, fetchImpl = fetch) {
  let host = '';
  try { host = new URL(hook).hostname; } catch { return false; }
  const text = summary(r, id);
  let res;
  if (/(^|\.)discord(app)?\.com$/.test(host)) {
    // Discord: the words (2000 characters at most in a message; the rest in an attached text file) and the screenshots as files, mentions switched off
    const body = new FormData();
    const head = text.length > 1900 ? `${text.slice(0, 1900)}\n… (the whole report is attached)` : text;
    body.set('payload_json', JSON.stringify({ content: head, allowed_mentions: { parse: [] }, username: 'Simpl reports' }));
    let n = 0;
    if (text.length > 1900) body.set(`files[${n++}]`, new Blob([text], { type: 'text/plain' }), `${id}.txt`);
    for (const s of r.shots) body.set(`files[${n++}]`, new Blob([s.bytes], { type: s.type }), s.name);
    res = await fetchImpl(hook, { method: 'POST', body });
  } else if (/(^|\.)slack\.com$/.test(host)) {
    res = await fetchImpl(hook, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ text: r.shots.length ? `${text}\n(screenshots are not carried to Slack)` : text }) });
  } else {
    const { shots, ...rest } = r;
    res = await fetchImpl(hook, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ id, ...rest, summary: text, screenshots: shots.map((s) => ({ name: s.name, type: s.type, dataUrl: `data:${s.type};base64,${b64(s.bytes)}` })) }) });
  }
  return !!res && res.ok;
}

/** POST /api/report: checked, handed on, answered with the report's id — or refused with a code. */
export async function handleReport(request, env = {}, fetchImpl = fetch) {
  if (request.method !== 'POST') return refuse(405, 'SC-B-405', 'Send a report with POST.');
  if (tooMany(request.headers.get('cf-connecting-ip'))) return refuse(429, 'SC-B-429', 'That is a lot of reports at once. Try again in a few minutes.');
  const got = await readReport(request);
  if (got.refusal) return got.refusal;
  const id = reportId();
  if (got.dropped) return json({ ok: true, id });
  const hook = env.REPORT_WEBHOOK || '';
  if (!hook) return refuse(503, 'SC-B-503', 'Reports are not switched on yet.');
  let ok = false;
  try { ok = await deliver(got.report, id, hook, fetchImpl); } catch { ok = false; }
  if (!ok) return refuse(502, 'SC-B-502', 'The report could not be passed on. Try again in a minute.');
  return json({ ok: true, id });
}
