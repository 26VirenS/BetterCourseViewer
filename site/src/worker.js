// The site as a Worker (wrangler.jsonc): public/ is served as it is, and /api/* — the one path
// sent here first (assets.run_worker_first) — is the Report a bug endpoint (src/report.js), the iPhone waitlist
// (src/waitlist.js), Simpl Community's themes (src/community.js) and (2.99.110) Simpl's own calendar feeds
// (src/feed.js, each kept by a FeedStore: src/feed-store.js).
import { handleReport } from './report.js';
import { handleWaitlist } from './waitlist.js';
import { handleCommunity } from './community.js';
import { handleFeed } from './feed.js';

export { FeedStore } from './feed-store.js';

export default {
  async fetch(request, env) {
    const { pathname } = new URL(request.url);
    if (pathname === '/api/report') return handleReport(request, env);
    if (pathname === '/api/waitlist') return handleWaitlist(request, env);
    if (pathname.startsWith('/api/community/')) return handleCommunity(request, env);
    if (pathname === '/api/feed' || pathname.startsWith('/api/feed/')) return handleFeed(request, env);
    if (pathname.startsWith('/api/')) return new Response(JSON.stringify({ ok: false, code: 'SC-B-404', message: 'Nothing here.' }), { status: 404, headers: { 'content-type': 'application/json; charset=utf-8' } });
    return env.ASSETS.fetch(request);
  },
};
