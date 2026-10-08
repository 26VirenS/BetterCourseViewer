#!/bin/bash
# The website's sources that live on the development branch (site/: the landing page and its pictures, the Report a
# bug page, the Worker that answers it, the site's wrangler.jsonc and README) laid over the site branch, which is
# what Cloudflare deploys, with Simpl for Mac's download address (/download/mac-app) pointed at its newest release.
# Everything else on the site branch — the privacy policy, the Safari app's update feed and download address
# (scripts/release-mac-app.sh) — is left as it is. Nothing is pushed when nothing changed. Run by the Site workflow
# (site/ changed on the default branch, or a Simpl for Mac release) and by the Package workflow after the Mac app's
# job; it can be run by hand from the repository root.
#
#   GH_TOKEN / the checkout's credentials   push access to the site branch (the workflow's token)
set -euo pipefail
cd "$(dirname "$0")/.."
[ -d site ] || { echo "no site/ here: nothing to publish"; exit 0; }

git fetch origin site:refs/remotes/origin/site
rm -rf build/site-sync
git worktree prune
git worktree add build/site-sync origin/site
cp -R site/. build/site-sync/
# Simpl for Mac's download address (the landing page's Download for Mac → Mac App): the newest mac-v* release's zip,
# beside the Safari app's /download/mac that scripts/release-mac-app.sh keeps. Asked of GitHub when a token is at hand.
if command -v gh >/dev/null 2>&1 && [ -n "${GH_TOKEN:-}" ]; then
  REPO="${GITHUB_REPOSITORY:-26VirenS/BetterCourseViewer}"
  MAC_TAG="$(gh api "repos/$REPO/releases?per_page=50" --jq '[.[] | select((.draft | not) and (.tag_name | startswith("mac-v")))][0].tag_name' 2>/dev/null || true)"
  if [ -n "$MAC_TAG" ] && [ "$MAC_TAG" != "null" ]; then
    REDIRECTS=build/site-sync/public/_redirects
    { grep -v '^/download/mac-app ' "$REDIRECTS" 2>/dev/null || true; echo "/download/mac-app https://github.com/$REPO/releases/download/$MAC_TAG/Simpl-Mac-${MAC_TAG#mac-v}.zip 302"; } > "$REDIRECTS.new"
    mv "$REDIRECTS.new" "$REDIRECTS"
    echo "/download/mac-app → $MAC_TAG"
  fi
fi
(
  cd build/site-sync
  git config user.name "github-actions[bot]"
  git config user.email "41898282+github-actions[bot]@users.noreply.github.com"
  git add -A
  if git diff --cached --quiet; then
    echo "the site branch already has site/ as it is here"
    exit 0
  fi
  git diff --cached --stat
  git commit -q -m "Site: the sources from the development branch (${GITHUB_SHA:-$(git -C .. rev-parse --short HEAD)})"
  pushed=0
  for d in 0 2 4 8; do
    if [ "$d" -gt 0 ]; then sleep "$d"; git pull -q --rebase origin site; fi
    if git push origin HEAD:site; then pushed=1; break; fi
  done
  [ "$pushed" = 1 ] || { echo "could not push the site branch"; exit 1; }
)
git worktree remove --force build/site-sync
echo "✅ the site branch carries site/"
