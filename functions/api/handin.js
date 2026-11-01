// The site on Cloudflare Pages: /api/handin itself (whether Hand in from your phone's store is there), as src/handin.js answers it.
import { handleHandIn } from '../../src/handin.js';

export const onRequest = ({ request, env }) => handleHandIn(request, env);
