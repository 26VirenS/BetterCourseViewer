// (2.99.106) Simpl Community, standing in for simplcourses.com's in the mock Canvas (the extension's background is pointed
// here by dev:communityBase): the site's own handler (site/src/community.js) over a store kept in memory, its review's
// passcode 'mock-passcode'; and, for the suites alone, /__mock/community/approve-all (what is waiting, approved) and
// /__mock/community/pending (what is, each with the places whose photos are kept). The iPhone app's test school, made
// from the mock, leaves it out (scripts/dev/make-test-school.mjs).
import { handleCommunity } from '../../site/src/community.js';

const store = new Map();
const kv = {
  async get(k, t) { const v = store.get(k); if (!v) return null; return t === 'arrayBuffer' || t?.type === 'arrayBuffer' ? v.value : typeof v.value === 'string' ? v.value : new TextDecoder().decode(v.value); },
  async getWithMetadata(k) { const v = store.get(k); return v ? { value: v.value instanceof Uint8Array ? v.value.buffer.slice(v.value.byteOffset, v.value.byteOffset + v.value.byteLength) : v.value, metadata: v.metadata } : { value: null }; },
  async put(k, value, o = {}) { store.set(k, { value: value instanceof ArrayBuffer ? new Uint8Array(value) : value, metadata: o.metadata }); },
  async delete(k) { store.delete(k); },
};
const list = async (k) => JSON.parse((await kv.get(k)) || '[]');

export async function communityMock(req, res, url) {
  const chunks = [];
  for await (const c of req) chunks.push(c);
  const send = (o) => { res.writeHead(200, { 'content-type': 'application/json; charset=utf-8' }); res.end(JSON.stringify(o)); };
  if (url.pathname === '/__mock/community/approve-all') {
    const pending = await list('list:pending'), approved = await list('list:approved');
    for (const id of pending) { const rec = JSON.parse((await kv.get(`theme:${id}`)) || 'null'); if (!rec) continue; const done = { ...rec, status: 'approved' }; await kv.put(`theme:${id}`, JSON.stringify(done)); approved.unshift(done); }
    await kv.put('list:pending', '[]');
    await kv.put('list:approved', JSON.stringify(approved));
    return send({ ok: true, approved: pending.length });
  }
  if (url.pathname === '/__mock/community/pending') {
    const recs = (await Promise.all((await list('list:pending')).map(async (id) => JSON.parse((await kv.get(`theme:${id}`)) || 'null')))).filter(Boolean);
    return send({ ok: true, pending: recs.map((r) => ({ ...r, photos: [...store.keys()].filter((k) => k.startsWith(`photo:${r.id}:`)).map((k) => k.split(':')[2]).sort() })) });
  }
  const r = await handleCommunity(new Request(url.href, { method: req.method, headers: req.headers, body: ['GET', 'HEAD'].includes(req.method) ? undefined : Buffer.concat(chunks), duplex: 'half' }), { COMMUNITY: kv, COMMUNITY_KEY: 'mock-passcode' });
  res.writeHead(r.status, Object.fromEntries(r.headers));
  res.end(Buffer.from(await r.arrayBuffer()));
}
