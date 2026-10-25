// simplcourses.com/api/waitlist — the iPhone waitlist on the landing page: an email address, checked and handed on to
// the same place Report a bug's reports go (the REPORT_WEBHOOK secret, src/report.js `deliver`), as one short message.
// Nothing is kept here, as with a report. Every refusal carries a code of the site's own (SC-W-…).

import { deliver, reportId } from './report.js';

const json = (body, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' } });
const refuse = (status, code, message) => json({ ok: false, code, message }, status);
const EMAIL_RE = /^[^\s@]{1,64}@[^\s@]{1,190}\.[^\s@]{2,}$/;
const clip = (v, n = 200) => String(v ?? '').replace(/[\u0000-\u001f]/g, '').trim().slice(0, n);

// a few sign-ups per address per ten minutes, per isolate: best effort, nothing stored
const seen = new Map();
function tooMany(ip, now = Date.now()) {
  if (!ip) return false;
  const recent = (seen.get(ip) || []).filter((t) => now - t < 10 * 60 * 1000);
  recent.push(now);
  seen.set(ip, recent);
  if (seen.size > 5000) seen.clear();
  return recent.length > 4;
}

/** POST /api/waitlist (a form: email, and `website`, the field people never see): handed on, or refused with a code. */
export async function handleWaitlist(request, env = {}, fetchImpl = fetch) {
  if (request.method !== 'POST') return refuse(405, 'SC-W-405', 'Join with POST.');
  if (tooMany(request.headers.get('cf-connecting-ip'))) return refuse(429, 'SC-W-429', 'That is a lot at once. Try again in a few minutes.');
  let form;
  try { form = await request.formData(); } catch { return refuse(400, 'SC-W-400', 'That did not arrive as a form.'); }
  if (clip(form.get('website'))) return json({ ok: true }); // (filled in: not a person)
  const email = clip(form.get('email'));
  if (!EMAIL_RE.test(email)) return refuse(400, 'SC-W-EMAIL', 'That email address does not look right.');
  const hook = env.REPORT_WEBHOOK || '';
  if (!hook) return refuse(503, 'SC-W-503', 'The waitlist is not switched on yet.');
  const entry = {
    category: 'iPhone waitlist', text: 'Wants Simpl for iPhone when it is out.', email, phone: '', shots: [], codes: [],
    version: '', browser: '', page: 'simplcourses.com', agent: clip(request.headers.get('user-agent'), 300),
  };
  let ok = false;
  try { ok = await deliver(entry, reportId(), hook, fetchImpl); } catch { ok = false; }
  if (!ok) return refuse(502, 'SC-W-502', 'That could not be saved. Try again in a minute.');
  return json({ ok: true });
}
