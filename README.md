# Simpl Courses — the website

Five pages, no build step, no dependencies: `public/index.html` (the landing page, its pictures in
`public/img/`), `public/compare.html` (Simpl, BetterCampus and plain Canvas side by side, every fact about the other
two linked to its source, at `/compare`), `public/trust.html` (what Simpl can and can't see, in plain words, at
`/trust`), `public/privacy.html` (the privacy policy, served at `/privacy`) and `public/report/` (Report a bug,
served at `/report/`, where Simpl's purple button leads). Each is self-contained — the styles are inline and every
icon is SVG. One small piece of code, `src/worker.js`, answers the report form at `/api/report` and the landing
page's iPhone waitlist at `/api/waitlist` (`src/waitlist.js`: an email address handed to the same REPORT_WEBHOOK,
nothing kept); everything else is a file served as it is. The landing page's "Does it work with my school?" box
reads `/data/schools.json`, the extension's own school list (`extension/data/schools.json`), which
`scripts/publish-site.sh` copies in at every publish.

The landing page leads with "Make ___ Simpl." (the subject turning over), the three download buttons (Download for
Mac opens into the Mac app and the Safari extension) and Canvas's own Dashboard beside Simpl's (the slider), then the
features grouped by when a student needs them, the iPhone waitlist, the short comparison and the trust facts. Its claims are kept to what the code does: change the
comparison or the trust page when the apps change. There are no student quotes or user numbers on it yet: add them
only when they are real.

The landing page's pictures of the interface are the real thing: `scripts/dev/site-shots.mjs` (on the development
branch) renders the extension at 2× on the mock Canvas and the mock Brightspace, as a made-up school, in light and
dark; the iPhone and Mac app pictures come from their screenshot suites. **Download for Mac** opens into two
choices: the Safari extension (`/download/mac`, the Safari app's newest zip) and Simpl for Mac
(`/download/mac-app`, the newest `mac-v*` release's zip, written into `_redirects` by `scripts/publish-site.sh`).

This branch carries the site and nothing else, so every push to it is a deploy. The
extension, the Mac app and the iOS app live on the development branches.

**Where to edit what.** The landing page and its pictures, the Report a bug page, the code behind it,
`wrangler.jsonc` and this README are kept on the development branch under `site/`, and the Site workflow lays
them over this branch whenever they change there (`scripts/publish-site.sh`; the Package workflow does too, at
every release): edit them there, or the next run puts them back. The privacy policy, `_headers` and `robots.txt`
are edited here. The Mac apps' update feed and download addresses are written by their releases (below).

## Deploying it

Cloudflare offers two ways in, and this branch is set up for either. **Workers** is the one
the dashboard steers you to now; **Pages** is the older, slightly simpler path. Pick one.

### Workers (Import a repository)

Workers & Pages → Create → Workers → Import a repository → `26VirenS/BetterCourseViewer`.

| Setting | Value |
| --- | --- |
| Branch | `site` |
| Build command | *(leave empty)* |
| Deploy command | `npx wrangler deploy` |

`wrangler.jsonc` does the rest: it declares a Worker that serves `public/` and hands `/api/*` —
and nothing else — to `src/worker.js`. Clean addresses (`/privacy`, `/report/`) and the 404 page
are configured there. It declares no bindings (no KV, no D1, no R2), so a deploy never waits on a
resource being made first.

### Pages (Connect to Git)

Workers & Pages → Create → **Pages** → Connect to Git → `26VirenS/BetterCourseViewer`.

| Setting | Value |
| --- | --- |
| Production branch | `site` |
| Framework preset | None |
| Build command | *(leave empty)* |
| Build output directory | `public` |

On Pages the form's endpoint is `functions/api/report.js`, which Pages finds by itself; it runs
the same handler (`src/report.js`).

Either way the privacy policy keeps one stable address for the Chrome Web Store and the App
Store to point at:

    https://<your-domain>/privacy

`public/_redirects` sends `/privacy.html` to `/privacy` so the short address is the only one
anyone sees, and `public/_headers` sets three safety headers.

Two more addresses are the Mac app's. `/download/mac` is a redirect to the signed zip on the
latest GitHub Release, so the download button on the page never changes; `/app/latest.json` is the
update feed the app reads every hour (its version, the download's address and SHA-256). The
release workflow on the development branch (`scripts/release-mac-app.sh`) rewrites both and pushes
here after every release, so neither needs editing by hand.

`/app/windows-latest.json` is Simpl for Windows' Critical Update feed: the newest Windows run, a
beta's too, and the newest Critical Update's of each, which the app reads every five minutes so
that those looks never count against GitHub's API limit for a network. `scripts/publish-site.sh`
writes it at every run of the Site workflow (a release published, edited or deleted, and every
hour).

## Simpl calendars

(2.99.110) Simpl's own calendar — a student's work, tasks and plans, sealed on their device — is kept at
`/api/feed/<id>` and opened by a calendar app at `/api/feed/<id>/<key>.ics` (`src/feed.js`). Each one is a Durable
Object, `FeedStore` (`src/feed-store.js`). `https://<your-domain>/api/feed` says whether the store is attached:
`{"ok":true,"store":true}`.

**On Workers** nothing is needed: `wrangler.jsonc` defines `FeedStore` and its migration, and the deploy makes it.

**On Pages** the store lives in a small Worker of its own, which the Pages project binds:

1. Workers & Pages → Create → Workers → Import a repository → `26VirenS/BetterCourseViewer`. Name it `simpl-feeds`;
   branch `site`; build command empty; deploy command `npx wrangler deploy -c feeds.jsonc`. Deploy.
2. The Pages project → Settings → Bindings → Add → **Durable Object namespace**: variable name `FEEDS`, the
   namespace `simpl-feeds` → `FeedStore`. Save.
3. The Pages project → Deployments → the newest → ⋯ → Retry deployment (a binding takes effect from the next one).

## Report a bug

The page at `/report/` takes a kind (Bug, Error, Crash, Reason to disable, Missing feature,
Feature request), the words, up to five screenshots, the error codes Simpl showed (filled in by the
button; see `docs/ERROR-CODES.md` on the development branch), and an email address and phone number
if the sender wants an answer. What Simpl's button carried along — its version, the browser and the
Canvas page's path — is shown on the page before anything is sent.

Reports are handed to one webhook, named by a secret. Until it is set the form says *Reports are not
switched on yet* (`SC-B-503`) and offers to open the report as a GitHub issue instead — the words,
the kind and the codes only, never the email or the phone number, since issues are public.

To switch reports on:

1. Make a webhook. A **Discord** channel's is simplest: Channel settings → Integrations → Webhooks →
   New Webhook → Copy Webhook URL. Each report arrives as a message with the whole report attached
   as a text file and the screenshots as pictures. A **Slack** incoming webhook works too (the words
   only; screenshots stay behind), and so does any other address that takes a JSON POST (the whole
   report, screenshots included as data URLs).
2. Cloudflare dashboard → Workers & Pages → the site → Settings → Variables and Secrets → Add →
   type **Secret**, name `REPORT_WEBHOOK`, value the webhook's address → Deploy.

A secret outlives every deploy, so this is done once. Reports are limited to six from one address in
ten minutes (`SC-B-429`), a hidden field catches the simplest bots, and the webhook's address never
reaches the page.

## Editing

The pages are plain HTML with inline styles. The links that leave the site are the two
download buttons, which point at the Chrome Web Store listing (search for
`chromewebstore.google.com`), and the two support links, which point at the repository's
issues (search for `github.com`). The canonical and Open Graph tags name `simplcourses.com`;
change those if the domain differs.

The pages were rendered from the design canvas (`Website.dc.html`, `Privacy.dc.html`) in the
mockup archive: the loops and `{{ }}` bindings were expanded once, so what is here is the
finished page rather than a template that needs a runtime.
