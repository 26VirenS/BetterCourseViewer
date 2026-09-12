# Simpl Courses — the website

Two static pages, no build step, no dependencies: `public/index.html` (the landing page) and
`public/privacy.html` (the privacy policy, served at `/privacy`). Both are self-contained —
the styles are inline and every icon is SVG, so there is nothing else to fetch.

This branch carries the site and nothing else, so every push to it is a deploy. The
extension, the Mac app and the iOS app live on the development branches.

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

`wrangler.jsonc` does the rest: it declares a Worker with no code whose only job is to serve
`public/`. Clean addresses (`/privacy`) and the 404 page are configured there.

### Pages (Connect to Git)

Workers & Pages → Create → **Pages** → Connect to Git → `26VirenS/BetterCourseViewer`.

| Setting | Value |
| --- | --- |
| Production branch | `site` |
| Framework preset | None |
| Build command | *(leave empty)* |
| Build output directory | `public` |

Either way the privacy policy keeps one stable address for the Chrome Web Store and the App
Store to point at:

    https://<your-domain>/privacy

`public/_redirects` sends `/privacy.html` to `/privacy` so the short address is the only one
anyone sees, and `public/_headers` sets three safety headers.

## Editing

The pages are plain HTML with inline styles. The links that leave the site are the two
download buttons and the two support links: search for `github.com` to find them, and swap in
the Chrome Web Store URL once the listing is live. The canonical and Open Graph tags name
`simplcourses.com`; change those if the domain differs.

The pages were rendered from the design canvas (`Website.dc.html`, `Privacy.dc.html`) in the
mockup archive: the loops and `{{ }}` bindings were expanded once, so what is here is the
finished page rather than a template that needs a runtime.
