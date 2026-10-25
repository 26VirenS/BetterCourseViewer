// (2.99.110) Simpl's own calendar feed, standing in for simplcourses.com's in the mock Canvas (the extension's background
// is pointed here by dev:feedBase): the site's own handler (site/src/feed.js) over its FeedStore (site/src/feed-store.js)
// with Durable Object storage kept in memory; and, for the suites alone, /__mock/feed/kept (the ids with a copy kept,
// and the size of each). The iPhone app's test school, made from the mock, leaves it out (scripts/dev/make-test-school.mjs).
import { handleFeed } from '../../site/src/feed.js';
import { FeedStore } from '../../site/src/feed-store.js';

const stores = new Map();
function memStorage() {
  const m = new Map();
  return {
    m,
    async get(k) { return m.get(k); },
    async put(k, v) { if (typeof k === 'object') { for (const [a, b] of Object.entries(k)) m.set(a, b); } else m.set(k, v); },
    async deleteAll() { m.clear(); },
    async setAlarm() {},
    async deleteAlarm() {},
  };
}
const FEEDS = {
  idFromName: (n) => n,
  get: (id) => {
    if (!stores.has(id)) { const storage = memStorage(); stores.set(id, { storage, obj: new FeedStore({ storage }) }); }
    const { obj } = stores.get(id);
    return { fetch: (u, init) => obj.fetch(new Request(u, init)) };
  },
};

export async function feedMock(req, res, url) {
  const chunks = [];
  for await (const c of req) chunks.push(c);
  if (url.pathname === '/__mock/feed/kept') {
    const kept = [...stores.entries()].filter(([, s]) => s.storage.m.has('sealed')).map(([id, s]) => ({ id, bytes: s.storage.m.get('sealed').byteLength }));
    res.writeHead(200, { 'content-type': 'application/json; charset=utf-8' });
    return void res.end(JSON.stringify({ ok: true, kept }));
  }
  const r = await handleFeed(new Request(url.href, { method: req.method, headers: req.headers, body: ['GET', 'HEAD', 'OPTIONS'].includes(req.method) ? undefined : Buffer.concat(chunks), duplex: 'half' }), { FEEDS });
  res.writeHead(r.status, Object.fromEntries(r.headers));
  res.end(Buffer.from(await r.arrayBuffer()));
}
