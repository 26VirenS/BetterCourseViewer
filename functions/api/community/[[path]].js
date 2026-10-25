// The site on Cloudflare Pages: Pages Functions answer /api/community/* with the same handler the Worker uses
// (src/community.js).
import { handleCommunity } from '../../../src/community.js';

export const onRequest = ({ request, env }) => handleCommunity(request, env);
