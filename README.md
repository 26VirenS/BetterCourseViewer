# Simpl Courses — the website

Two static pages, no build step, no dependencies: `index.html` (the landing page) and
`privacy.html` (the privacy policy, served at `/privacy`). Both are self-contained — the
styles are inline and the icons are SVG, so there is nothing else to fetch.

This branch exists to be deployed. It carries only the site, so every push to it is a
deploy and nothing else. The extension, the Mac app and the iOS app live on the
development branches.

## Cloudflare Pages

Once, in the Cloudflare dashboard → Workers & Pages → Create → Pages → Connect to Git:

| Setting | Value |
| --- | --- |
| Repository | `26VirenS/BetterCourseViewer` |
| Production branch | `site` |
| Framework preset | None |
| Build command | *(leave empty)* |
| Build output directory | `/` |

Every push to this branch then deploys on its own, and the privacy policy keeps one
stable address to give the Chrome Web Store and the App Store:

    https://<your-domain>/privacy

`_redirects` sends `/privacy.html` to `/privacy`, so the shorter address is the only one
anyone needs to see. `_headers` sets the usual three safety headers. `404.html` is served
for anything else.

## Editing

The pages are plain HTML with inline styles. The three links that point outside the site
are the two download buttons and the support link; search for `github.com` to find them,
and swap in the Chrome Web Store URL once the listing is live.

The pages were rendered from the design canvas (`Website.dc.html`, `Privacy.dc.html`) in
the mockup archive: the loops and `{{ }}` bindings were expanded once, so what is here is
the finished page rather than a template that needs a runtime.
