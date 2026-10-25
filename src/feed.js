// simplcourses.com/api/feed/* — (2.99.110) Simpl's own calendar feed: a student's work, their own tasks and their
// plans, as one calendar they subscribe to in Apple Calendar, Google Calendar or Outlook.
//
// What is kept is a sealed copy only. Simpl (the extension, or the Mac or Windows app) writes the calendar, seals it
// with AES-256-GCM under a key it made and keeps, and sends the sealed bytes here under the feed's id. The key is in
// the private link the student subscribes to — /api/feed/<id>/<key>.ics — and nowhere else: when a calendar app asks
// for that link, the copy is opened with the key it carries, the calendar is handed back, and the key is let go. It is
// never written down, and without it the copy kept here cannot be read, by anyone, us included.
//
//   PUT    /api/feed/<id>             the sealed copy (12 bytes of nonce, then the sealed text, bound to the id),
//                                    with "Authorization: Bearer <token>": the first one sent claims the id, every
//                                    later one must carry the same token (only its hash is kept)
//   DELETE /api/feed/<id>             the copy forgotten (the same token): its link stops working
//   GET    /api/feed/<id>/<key>.ics   the calendar, opened with the key in the link
//   GET    /api/feed                  whether the store is there (the site's own check)
//
// A copy not sent again for 120 days is forgotten. Each feed is kept by a Durable Object of its own (FeedStore,
// src/feed-store.js), which the deploy makes itself: nothing to set up first. Every refusal carries a code (SC-F-…).

export const LIMITS = { bytes: 1024 * 1024, putsPerHour: 120, keepDays: 120 };
const ID = /^[A-Za-z0-9_-]{22}$/; // 16 random bytes, base64url
const SECRET = /^[A-Za-z0-9_-]{43}$/; // 32 random bytes, base64url (the key, the token)

const cors = {
  'access-control-allow-origin': '*', // (no cookie is ever read: the token and the key are the only proofs)
  'access-control-allow-methods': 'GET, PUT, DELETE, OPTIONS',
  'access-control-allow-headers': 'authorization, content-type',
  'access-control-max-age': '86400',
};
const json = (body, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store', ...cors } });
const refuse = (status, code, message) => json({ ok: false, code, message }, status);

export const fromB64url = (s) => {
  const b = atob(String(s).replace(/-/g, '+').replace(/_/g, '/') + '='.repeat((4 - (String(s).length % 4)) % 4));
  const out = new Uint8Array(b.length);
  for (let i = 0; i < b.length; i++) out[i] = b.charCodeAt(i);
  return out;
};
const hex = (buf) => [...new Uint8Array(buf)].map((x) => x.toString(16).padStart(2, '0')).join('');
async function tokenHash(token) {
  return hex(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(`simpl-feed:${token}`)));
}

/** The calendar in a sealed copy: nonce (12 bytes), then the AES-256-GCM text, its id as the associated data. */
export async function openSealed(sealed, keyB64, id) {
  const bytes = new Uint8Array(sealed);
  if (bytes.length < 13 + 16) throw new Error('short');
  const key = await crypto.subtle.importKey('raw', fromB64url(keyB64), { name: 'AES-GCM' }, false, ['decrypt']);
  const plain = await crypto.subtle.decrypt({ name: 'AES-GCM', iv: bytes.slice(0, 12), additionalData: new TextEncoder().encode(id) }, key, bytes.slice(12));
  return new TextDecoder().decode(plain);
}

// ---- a little memory per isolate: best effort, nothing stored -------------------------------------------------
const windows = new Map();
function tooMany(ip, now = Date.now()) {
  if (!ip) return false;
  const recent = (windows.get(ip) || []).filter((t) => now - t < 3600e3);
  recent.push(now);
  windows.set(ip, recent);
  if (windows.size > 5000) windows.clear();
  return recent.length > LIMITS.putsPerHour;
}

/** env.FEEDS: the FeedStore namespace. A stand-in for it (the tests') answers the same three questions. */
function store(env, id) {
  const ns = env.FEEDS;
  return ns.get(ns.idFromName(id));
}

export async function handleFeed(request, env, now = Date.now()) {
  if (request.method === 'OPTIONS') return new Response(null, { status: 204, headers: cors });
  const { pathname } = new URL(request.url);
  const parts = pathname.replace(/^\/api\/feed\/?/, '').split('/').filter(Boolean);
  if (!parts.length) return json({ ok: true, store: !!env.FEEDS });
  if (!env.FEEDS) return refuse(503, 'SC-F-503', 'Simpl calendars are not switched on yet.');
  const id = parts[0];
  if (!ID.test(id)) return refuse(404, 'SC-F-404', 'Nothing here.');

  // the calendar itself, for a calendar app
  if (request.method === 'GET' || request.method === 'HEAD') {
    const m = parts.length === 2 ? /^([A-Za-z0-9_-]{43})\.ics$/.exec(parts[1]) : null;
    const gone = () => new Response('Not found', { status: 404, headers: { 'content-type': 'text/plain; charset=utf-8', 'cache-control': 'no-store', ...cors } });
    if (!m) return gone();
    const res = await store(env, id).fetch('https://feed/sealed', { method: 'GET' });
    if (!res.ok) return gone();
    let text;
    try { text = await openSealed(await res.arrayBuffer(), m[1], id); } catch { return gone(); } // (a wrong key reads as nothing there)
    return new Response(request.method === 'HEAD' ? null : text, { status: 200, headers: {
      'content-type': 'text/calendar; charset=utf-8',
      'content-disposition': 'inline; filename="simpl.ics"',
      'cache-control': 'private, max-age=300',
      'x-robots-tag': 'noindex, nofollow',
      ...cors,
    } });
  }

  // the sealed copy, from Simpl
  if (parts.length !== 1) return refuse(404, 'SC-F-404', 'Nothing here.');
  const token = /^Bearer\s+(\S+)$/i.exec(request.headers.get('authorization') || '')?.[1] || '';
  if (!SECRET.test(token)) return refuse(401, 'SC-F-401', 'This calendar needs its token.');
  const hash = await tokenHash(token);
  if (request.method === 'DELETE') {
    const res = await store(env, id).fetch('https://feed/sealed', { method: 'DELETE', headers: { 'x-token-hash': hash } });
    if (res.status === 403) return refuse(403, 'SC-F-403', 'That is not this calendar\'s token.');
    return json({ ok: true });
  }
  if (request.method !== 'PUT') return refuse(405, 'SC-F-405', 'Not like that.');
  if (tooMany(request.headers.get('cf-connecting-ip'), now)) return refuse(429, 'SC-F-429', 'Too many updates for now. Try again in a while.');
  const declared = Number(request.headers.get('content-length') || 0);
  if (declared > LIMITS.bytes) return refuse(413, 'SC-F-413', 'That calendar is too big.');
  const body = await request.arrayBuffer();
  if (body.byteLength > LIMITS.bytes) return refuse(413, 'SC-F-413', 'That calendar is too big.');
  if (body.byteLength < 13 + 16) return refuse(400, 'SC-F-400', 'That is not a sealed calendar.');
  const res = await store(env, id).fetch('https://feed/sealed', { method: 'PUT', headers: { 'x-token-hash': hash, 'x-now': String(now) }, body });
  if (res.status === 403) return refuse(403, 'SC-F-403', 'That is not this calendar\'s token.');
  if (!res.ok) return refuse(500, 'SC-F-500', 'The calendar could not be kept.');
  return json({ ok: true, bytes: body.byteLength });
}
