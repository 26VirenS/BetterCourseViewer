// The site on Cloudflare Pages: Pages Functions answer /api/waitlist with the same handler the Worker uses
// (src/waitlist.js).
import { handleWaitlist } from '../../src/waitlist.js';

export const onRequest = ({ request, env }) => handleWaitlist(request, env);
