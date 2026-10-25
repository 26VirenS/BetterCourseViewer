// (2.99.110) One Simpl calendar's sealed copy (src/feed.js), kept by a Durable Object of its own: the bytes, when they
// came, and the hash of the token that may replace or forget them. It cannot open the copy: the key is never sent here.
// A copy not sent again for LIMITS.keepDays is forgotten (an alarm). SQLite-backed (wrangler.jsonc migrations), the kind
// the Workers Free plan has.
import { LIMITS } from './feed.js';

const DAY = 86400e3;

export class FeedStore {
  constructor(state) {
    this.storage = state.storage;
  }

  async fetch(request) {
    const s = this.storage;
    if (request.method === 'GET') {
      const sealed = await s.get('sealed');
      return sealed ? new Response(sealed) : new Response(null, { status: 404 });
    }
    const hash = request.headers.get('x-token-hash') || '';
    const owner = await s.get('owner');
    if (owner && owner !== hash) return new Response(null, { status: 403 });
    if (request.method === 'DELETE') {
      if (owner) {
        await s.deleteAlarm();
        await s.deleteAll();
      }
      return new Response(null, { status: 204 });
    }
    if (request.method === 'PUT') {
      const now = Number(request.headers.get('x-now')) || Date.now();
      await s.put({ owner: hash, sealed: await request.arrayBuffer(), at: now });
      await s.setAlarm(now + LIMITS.keepDays * DAY);
      return new Response(null, { status: 204 });
    }
    return new Response(null, { status: 405 });
  }

  async alarm() {
    const at = (await this.storage.get('at')) || 0;
    if (Date.now() - at >= LIMITS.keepDays * DAY) await this.storage.deleteAll();
    else await this.storage.setAlarm(at + LIMITS.keepDays * DAY);
  }
}
