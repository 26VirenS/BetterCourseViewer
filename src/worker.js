// The site as a Worker (wrangler.jsonc): public/ is served as it is, and /api/* — the one path
// sent here first (assets.run_worker_first) — is the Report a bug endpoint (src/report.js).
import { handleReport } from './report.js';

export default {
  async fetch(request, env) {
    const { pathname } = new URL(request.url);
    if (pathname === '/api/report') return handleReport(request, env);
    if (pathname.startsWith('/api/')) return new Response(JSON.stringify({ ok: false, code: 'SC-B-404', message: 'Nothing here.' }), { status: 404, headers: { 'content-type': 'application/json; charset=utf-8' } });
    return env.ASSETS.fetch(request);
  },
};
