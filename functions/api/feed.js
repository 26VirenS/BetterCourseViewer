// The site on Cloudflare Pages: /api/feed itself (whether the calendar store is there), as src/feed.js answers it.
import { handleFeed } from '../../src/feed.js';

export const onRequest = ({ request, env }) => handleFeed(request, env);
