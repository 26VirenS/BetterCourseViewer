// The site on Cloudflare Pages: Pages Functions answer /api/report with the same handler the
// Worker uses (src/report.js); the REPORT_WEBHOOK secret is set on the Pages project instead.
import { handleReport } from '../../src/report.js';

export const onRequest = ({ request, env }) => handleReport(request, env);
