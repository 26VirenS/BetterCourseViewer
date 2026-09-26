# Simpl Courses — the website

Three pages, no build step, no dependencies: `public/index.html` (the landing page),
`public/privacy.html` (the privacy policy, served at `/privacy`) and `public/report/` (Report a
bug, served at `/report/`, where Simpl's purple button leads). Each is self-contained — the
styles are inline and every icon is SVG. One small piece of code, `src/worker.js`, answers the
report form at `/api/report`; everything else is a file served as it is.

This branch carries the site and nothing else, so every push to it is a deploy. The
extension, the Mac app and the iOS app live on the development branches.

**Where to edit what.** The Report a bug page, the code behind it, `wrangler.jsonc` and this
README are kept on the development branch under `site/`, and the Package workflow lays them over
this branch at every release (`scripts/publish-site.sh`): edit them there, or the next release puts
them back. The landing page, the privacy policy, `_headers` and `robots.txt` are edited here. The
Mac app's update feed and download address are written by the release (below).

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
