// The site on Cloudflare Pages: Pages Functions answer /api/feed/* with the same handler the Worker uses
// (src/feed.js). Pages cannot define the FeedStore it keeps calendars in, so there it answers that Simpl calendars are
// not switched on (SC-F-503) unless a FEEDS binding is given in the project's settings.
import { handleFeed } from '../../../src/feed.js';

export const onRequest = ({ request, env }) => handleFeed(request, env);
