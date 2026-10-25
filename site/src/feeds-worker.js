// (2.99.110) The Worker behind feeds.jsonc: nothing of its own to answer (it has no address); it is there to keep
// FeedStore, the Durable Object the site's Pages project reaches through its FEEDS binding.
export { FeedStore } from './feed-store.js';

export default {
  fetch: () => new Response(null, { status: 404 }),
};
