// simplcourses.com/api/community/* — Simpl Community: themes students make in Appearance, shared, checked by the owner,
// then offered to everyone (the website's /community, and Appearance's Community in the apps and the extensions).
//
// A theme is what Appearance keeps: an accent colour, light or dark, and a picture in each of seven places (the sidebar
// and the Dashboard's six counters) — one of the drawn scenes, or a photo — each with where it sits (x%, y%, zoom).
//
// Kept in the COMMUNITY namespace (Workers KV, in the owner's Cloudflare account):
//   theme:<id>          the theme (name, maker, colour, look, places), its status: pending or approved
//   photo:<id>:<place>  a photo's bytes
//   list:approved       every approved theme as the gallery shows it, newest first (one read for the whole gallery)
//   list:pending        the ids waiting for a decision
// Nothing about the people who send or add themes is kept: no accounts, no counts, no addresses.
//
// The review (/community/review): a passcode, the COMMUNITY_KEY secret, opens a signed cookie good for twelve hours;
// with it the pending themes can be seen, approved or turned down, and an approved one taken down. Each new theme is
// announced where reports go (REPORT_WEBHOOK, src/report.js `deliver`). Every refusal carries a code (SC-C-…).

import { deliver, reportId } from './report.js';

export const PLACES = ['side', 'today', 'tomorrow', 'next', 'overdue', 'unread', 'graded'];
export const SCENES = ['Dusk', 'Ocean', 'Forest', 'Sand', 'Peaks', 'City'];
export const LIMITS = { name: 40, maker: 30, photoBytes: 1536 * 1024, totalBytes: 8 * 1024 * 1024, pendingMax: 200, approvedMax: 1000, submitsPerHour: 3, loginTries: 8, sessionHours: 12 };
const COOKIE = 'simpl_review';

const json = (body, status = 200, headers = {}) => new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store', ...headers } });
const refuse = (status, code, message) => json({ ok: false, code, message }, status);
const clip = (v, n) => String(v ?? '').replace(/[\u0000-\u001f\u007f<>]/g, '').replace(/\s+/g, ' ').trim().slice(0, n);
const num = (v, lo, hi, d) => { const n = Number(v); return Number.isFinite(n) ? Math.min(hi, Math.max(lo, n)) : d; };
const DEFAULT_PLACE = (key) => (key === 'side' ? { x: 50, y: 100, z: 1 } : { x: 100, y: 100, z: 1 });

// ---- a little memory per isolate: best effort, nothing stored -------------------------------------------------
const windows = new Map();
function tooMany(kind, ip, limit, ms, now = Date.now()) {
  if (!ip) return false;
  const key = `${kind}:${ip}`;
  const recent = (windows.get(key) || []).filter((t) => now - t < ms);
  recent.push(now);
  windows.set(key, recent);
  if (windows.size > 5000) windows.clear();
  return recent.length > limit;
}

// ---- photos: known by their first bytes, never by what they say they are -------------------------------------
export function imageType(bytes) {
  const b = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
  if (b.length > 3 && b[0] === 0xff && b[1] === 0xd8 && b[2] === 0xff) return 'image/jpeg';
  if (b.length > 8 && b[0] === 0x89 && b[1] === 0x50 && b[2] === 0x4e && b[3] === 0x47) return 'image/png';
  if (b.length > 12 && b[0] === 0x52 && b[1] === 0x49 && b[2] === 0x46 && b[3] === 0x46 && b[8] === 0x57 && b[9] === 0x45 && b[10] === 0x42 && b[11] === 0x50) return 'image/webp';
  return null;
}

/** A theme as sent (the form's `theme` field), checked: its name, maker, colour, look and places, or a refusal. */
export function readTheme(raw, photoPlaces) {
  let t;
  try { t = typeof raw === 'string' ? JSON.parse(raw) : raw; } catch { return { refusal: ['SC-C-THEME', 'That theme could not be read.'] }; }
  if (!t || typeof t !== 'object') return { refusal: ['SC-C-THEME', 'That theme could not be read.'] };
  const name = clip(t.name, LIMITS.name);
  if (!name) return { refusal: ['SC-C-NAME', 'Give the theme a name.'] };
  const accent = t.accent == null || t.accent === '' ? null : String(t.accent).toLowerCase();
  if (accent !== null && !/^#[0-9a-f]{6}$/.test(accent)) return { refusal: ['SC-C-COLOUR', 'That colour is not one Simpl can use.'] };
  const look = ['light', 'dark'].includes(t.look) ? t.look : null;
  const places = {};
  for (const key of PLACES) {
    const p = t.places?.[key];
    if (!p) continue;
    const where = { x: num(p.x, 0, 100, DEFAULT_PLACE(key).x), y: num(p.y, 0, 100, DEFAULT_PLACE(key).y), z: num(p.z, 0.5, 3, 1) };
    if (p.scene != null) {
      if (!SCENES.includes(p.scene)) return { refusal: ['SC-C-SCENE', 'That drawing is not one of Simpl’s.'] };
      places[key] = { scene: p.scene, ...where };
    } else if (p.photo) {
      if (!photoPlaces.has(key)) return { refusal: ['SC-C-PHOTO', `The ${key} photo did not arrive.`] };
      places[key] = { photo: true, ...where, ...(p.ink === false ? { ink: false } : {}) };
    }
  }
  if (!accent && !Object.keys(places).length) return { refusal: ['SC-C-EMPTY', 'A theme needs a colour or a picture.'] };
  return { theme: { name, maker: clip(t.maker, LIMITS.maker), accent, look, places } };
}

const newId = () => { const b = crypto.getRandomValues(new Uint8Array(8)); return Array.from(b, (x) => 'abcdefghjkmnpqrstuvwxyz23456789'[x % 31]).join(''); };

/** A theme as the gallery and the apps take it: its photos as addresses on this site. */
export function publicTheme(rec, origin) {
  const places = {};
  for (const [key, p] of Object.entries(rec.places || {})) {
    const where = { x: p.x, y: p.y, z: p.z };
    places[key] = p.photo ? { photo: `${origin}/api/community/photo/${rec.id}/${key}`, ...where, ...(p.ink === false ? { ink: false } : {}) } : { scene: p.scene, ...where };
  }
  return { id: rec.id, name: rec.name, maker: rec.maker || '', accent: rec.accent, look: rec.look, places, created: rec.created };
}

const readList = async (kv, key) => { try { return JSON.parse((await kv.get(key)) || '[]'); } catch { return []; } };

// ---- the review's passcode and its cookie --------------------------------------------------------------------
const enc = new TextEncoder();
const b64url = (buf) => btoa(String.fromCharCode(...new Uint8Array(buf))).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
const hmacKey = (secret) => crypto.subtle.importKey('raw', enc.encode(secret), { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);
const sign = async (secret, text) => b64url(await crypto.subtle.sign('HMAC', await hmacKey(secret), enc.encode(text)));
/** Equal, compared in a time that does not depend on where they differ (each side signed first: equal lengths). */
async function sameSecret(secret, given) {
  const [a, b] = await Promise.all([sign(secret, `pass:${secret}`), sign(secret, `pass:${given}`)]);
  let diff = a.length ^ b.length;
  for (let i = 0; i < a.length; i++) diff |= a.charCodeAt(i) ^ b.charCodeAt(i % b.length);
  return diff === 0;
}
async function sessionCookie(secret, now = Date.now()) {
  const until = now + LIMITS.sessionHours * 3600 * 1000;
  return `${COOKIE}=${until}.${await sign(secret, `session:${until}`)}; Path=/api/community; HttpOnly; Secure; SameSite=Strict; Max-Age=${LIMITS.sessionHours * 3600}`;
}
export async function isReviewer(request, env, now = Date.now()) {
  const secret = env.COMMUNITY_KEY || '';
  if (!secret) return false;
  const m = new RegExp(`(?:^|;\\s*)${COOKIE}=(\\d+)\\.([\\w-]+)`).exec(request.headers.get('cookie') || '');
  if (!m || Number(m[1]) < now) return false;
  const want = await sign(secret, `session:${m[1]}`);
  let diff = want.length ^ m[2].length;
  for (let i = 0; i < want.length; i++) diff |= want.charCodeAt(i) ^ m[2].charCodeAt(i % Math.max(1, m[2].length));
  return diff === 0;
}
// (a decision comes from the review page itself: the same site, and a header no other page can send without asking)
const sameSite = (request) => { const o = request.headers.get('origin'); return (!o || o === new URL(request.url).origin) && request.headers.get('x-simpl-review') === '1'; };

// ---- the endpoints ----------------------------------------------------------------------------------------------
/** /api/community/…: the gallery, a photo, a theme sent in, and the review. */
export async function handleCommunity(request, env = {}, fetchImpl = fetch, now = Date.now()) {
  const res = await answer(request, env, fetchImpl, now);
  // (the gallery, a photo and a theme sent in are open to the apps and the extensions, from wherever they ask)
  if (/^\/api\/community\/(themes|photo\/|submit)/.test(new URL(request.url).pathname)) res.headers.set('access-control-allow-origin', '*');
  return res;
}

async function answer(request, env, fetchImpl, now) {
  const url = new URL(request.url);
  const path = url.pathname.replace(/^\/api\/community\/?/, '');
  const kv = env.COMMUNITY;
  const ip = request.headers.get('cf-connecting-ip');
  if (!kv) return refuse(503, 'SC-C-503', 'Simpl Community is not switched on yet.');

  // the gallery: every approved theme, newest first
  if (path === 'themes' && request.method === 'GET') {
    const list = await readList(kv, 'list:approved');
    return json({ ok: true, themes: list.map((r) => publicTheme(r, url.origin)) }, 200, { 'cache-control': 'public, max-age=60' });
  }

  // a photo: an approved theme's for anyone; a pending one's for the review alone
  let m = /^photo\/([a-z0-9]{6,16})\/(\w+)$/.exec(path);
  if (m && request.method === 'GET') {
    const [, id, key] = m;
    if (!PLACES.includes(key)) return refuse(404, 'SC-C-404', 'No such photo.');
    const rec = JSON.parse((await kv.get(`theme:${id}`)) || 'null');
    const open = rec?.status === 'approved';
    if (!rec || (!open && !(await isReviewer(request, env, now)))) return refuse(404, 'SC-C-404', 'No such photo.');
    const got = await kv.getWithMetadata(`photo:${id}:${key}`, { type: 'arrayBuffer' });
    if (!got?.value) return refuse(404, 'SC-C-404', 'No such photo.');
    return new Response(got.value, { headers: {
      'content-type': got.metadata?.type || imageType(got.value) || 'application/octet-stream',
      'cache-control': open ? 'public, max-age=31536000, immutable' : 'private, no-store',
      'x-content-type-options': 'nosniff', 'content-security-policy': "default-src 'none'",
    } });
  }

  // a theme sent in from Appearance: kept as pending, and the owner told
  if (path === 'submit') {
    if (request.method !== 'POST') return refuse(405, 'SC-C-405', 'Send a theme with POST.');
    if (tooMany('submit', ip, LIMITS.submitsPerHour, 3600 * 1000, now)) return refuse(429, 'SC-C-429', 'That is a lot of themes at once. Try again in an hour.');
    let form;
    try { form = await request.formData(); } catch { return refuse(400, 'SC-C-400', 'That did not arrive as a form.'); }
    if (clip(form.get('website'), 200)) return json({ ok: true, id: 'thanks' }); // (filled in: not a person)
    const photos = new Map();
    let total = 0;
    for (const key of PLACES) {
      const f = form.get(`photo-${key}`);
      if (!f || typeof f === 'string') continue;
      const bytes = new Uint8Array(await f.arrayBuffer());
      if (bytes.length > LIMITS.photoBytes) return refuse(413, 'SC-C-BIG', 'A photo is too big. Each must be under 1.5 MB.');
      const type = imageType(bytes);
      if (!type) return refuse(415, 'SC-C-TYPE', 'Photos must be JPEG, PNG or WebP.');
      total += bytes.length;
      photos.set(key, { bytes, type });
    }
    if (total > LIMITS.totalBytes) return refuse(413, 'SC-C-BIG', 'The photos together are too big.');
    const got = readTheme(form.get('theme'), photos);
    if (got.refusal) return refuse(400, ...got.refusal);
    const pending = await readList(kv, 'list:pending');
    if (pending.length >= LIMITS.pendingMax) return refuse(503, 'SC-C-FULL', 'So many themes are waiting to be looked at that no more can come in for now.');
    const id = newId();
    const rec = { id, status: 'pending', created: new Date(now).toISOString().slice(0, 10), ...got.theme };
    for (const [key, p] of photos) if (rec.places[key]?.photo) await kv.put(`photo:${id}:${key}`, p.bytes, { metadata: { type: p.type } });
    await kv.put(`theme:${id}`, JSON.stringify(rec));
    await kv.put('list:pending', JSON.stringify([id, ...pending]));
    if (env.REPORT_WEBHOOK) {
      const entry = { category: 'Community theme', text: `A theme to look at: “${rec.name}”${rec.maker ? ` by ${rec.maker}` : ''}. Approve or turn it down at ${url.origin}/community/review`, email: '', phone: '', shots: [], codes: [], version: '', browser: '', page: 'Simpl Community', agent: '' };
      try { await deliver(entry, reportId(now), env.REPORT_WEBHOOK, fetchImpl); } catch { /* the theme is kept all the same */ }
    }
    return json({ ok: true, id });
  }

  // the review: in with the passcode, out again
  if (path === 'login') {
    if (request.method !== 'POST') return refuse(405, 'SC-C-405', 'Sign in with POST.');
    if (!env.COMMUNITY_KEY) return refuse(503, 'SC-C-KEY', 'The review has no passcode set yet.');
    if (tooMany('login', ip, LIMITS.loginTries, 15 * 60 * 1000, now)) return refuse(429, 'SC-C-429', 'Too many tries. Wait a quarter of an hour.');
    let body = {};
    try { body = await request.json(); } catch { /* none */ }
    if (!(await sameSecret(env.COMMUNITY_KEY, String(body.key || '')))) return refuse(401, 'SC-C-401', 'That passcode is not right.');
    return json({ ok: true }, 200, { 'set-cookie': await sessionCookie(env.COMMUNITY_KEY, now) });
  }
  if (path === 'logout' && request.method === 'POST') return json({ ok: true }, 200, { 'set-cookie': `${COOKIE}=; Path=/api/community; HttpOnly; Secure; SameSite=Strict; Max-Age=0` });

  if (path === 'review' || path === 'decide' || path === 'remove') {
    if (!(await isReviewer(request, env, now))) return refuse(401, 'SC-C-401', 'Sign in to the review first.');
    if (path === 'review' && request.method === 'GET') {
      const ids = await readList(kv, 'list:pending');
      const pending = (await Promise.all(ids.map(async (id) => JSON.parse((await kv.get(`theme:${id}`)) || 'null')))).filter(Boolean);
      const approved = await readList(kv, 'list:approved');
      return json({ ok: true, pending: pending.map((r) => publicTheme(r, url.origin)), approved: approved.map((r) => publicTheme(r, url.origin)) });
    }
    if (request.method !== 'POST') return refuse(405, 'SC-C-405', 'Decide with POST.');
    if (!sameSite(request)) return refuse(403, 'SC-C-403', 'Decide from the review page.');
    let body = {};
    try { body = await request.json(); } catch { /* none */ }
    const id = /^[a-z0-9]{6,16}$/.test(body.id || '') ? body.id : '';
    const rec = id ? JSON.parse((await kv.get(`theme:${id}`)) || 'null') : null;
    if (!rec) return refuse(404, 'SC-C-404', 'No such theme.');
    const drop = async () => {
      await Promise.all(Object.entries(rec.places || {}).filter(([, p]) => p.photo).map(([key]) => kv.delete(`photo:${id}:${key}`)));
      await kv.delete(`theme:${id}`);
    };
    if (path === 'decide') {
      if (rec.status !== 'pending') return refuse(409, 'SC-C-409', 'That theme has been decided already.');
      await kv.put('list:pending', JSON.stringify((await readList(kv, 'list:pending')).filter((x) => x !== id)));
      if (body.decision === 'approve') {
        const done = { ...rec, status: 'approved' };
        await kv.put(`theme:${id}`, JSON.stringify(done));
        await kv.put('list:approved', JSON.stringify([done, ...(await readList(kv, 'list:approved')).filter((r) => r.id !== id)].slice(0, LIMITS.approvedMax)));
        return json({ ok: true, status: 'approved' });
      }
      if (body.decision === 'deny') { await drop(); return json({ ok: true, status: 'denied' }); }
      return refuse(400, 'SC-C-400', 'Approve or deny.');
    }
    // remove: an approved theme taken down, its photos with it
    await kv.put('list:approved', JSON.stringify((await readList(kv, 'list:approved')).filter((r) => r.id !== id)));
    await drop();
    return json({ ok: true, status: 'removed' });
  }

  return refuse(404, 'SC-C-404', 'Nothing here.');
}
