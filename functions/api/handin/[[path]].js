// The site on Cloudflare Pages: Pages Functions answer /api/handin/* with the same handler the Worker uses
// (src/handin.js). Pages cannot define the HandInStore each hand-in is kept in, so there it answers that Hand in from
// your phone is not switched on (SC-H-503) — and the extension falls back to opening the assignment in Canvas on the
// phone — unless a HANDIN binding is given in the project's settings.
import { handleHandIn } from '../../../src/handin.js';

export const onRequest = ({ request, env }) => handleHandIn(request, env);
