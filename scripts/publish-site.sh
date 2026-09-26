#!/bin/bash
# The website's sources that live on the development branch (site/: the Report a bug page, the
# Worker that answers it, the site's wrangler.jsonc and README) laid over the site branch, which is
# what Cloudflare deploys. Everything else on the site branch — the landing page, the privacy
# policy, the Mac app's update feed and download address (scripts/release-mac-app.sh) — is left as
# it is. Nothing is pushed when nothing changed. Run by the Package workflow after the Mac app's
# job (the two never push the site branch at once); it can be run by hand from the repository root.
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
