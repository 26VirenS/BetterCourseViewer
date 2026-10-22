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
# beside the Safari app's /download/mac that scripts/release-mac-app.sh keeps; and its update feed, /app/mac-latest.json
# (the app's Updater.swift reads it): that release's version, zip, SHA-256 and size. Asked of GitHub when a token is at
# hand.
if command -v gh >/dev/null 2>&1 && [ -n "${GH_TOKEN:-}" ]; then
  REPO="${GITHUB_REPOSITORY:-26VirenS/BetterCourseViewer}"
  # (1.3.152) every update file's signature beside it (scripts/sign-releases.py, with the UPDATE_SIGNING_KEY secret)
  # before the feeds below name it — a release made since the last run, and any file replaced since it was signed. The
  # apps install only a signed file; Simpl for Windows reads GitHub's releases itself and takes one once it is signed.
  python3 scripts/sign-releases.py || echo "⚠ update signing failed: the feeds are written all the same, and the apps wait"
  # (the highest version, as numbers: GitHub's own order put mac-v1.2.10 after mac-v1.2.3)
  MAC_REL="$(gh api "repos/$REPO/releases?per_page=100" --jq '[.[] | select((.draft | not) and (.prerelease | not) and (.tag_name | startswith("mac-v")))] | sort_by(.tag_name | ltrimstr("mac-v") | split(".") | map(tonumber? // 0)) | last' 2>/dev/null || true)"
  MAC_TAG="$(printf '%s' "$MAC_REL" | python3 -c 'import json,sys; d=json.loads(sys.stdin.read() or "null"); print(d["tag_name"] if d else "")' 2>/dev/null || true)"
  # (1.3.72) the newest Critical Update (a mac-v* release whose notes carry the mark): both feeds name it, and a Mac on
  # an older version installs the feed's newest at its next check, whatever its settings (Updater.swift)
  CRITICAL="$(gh api "repos/$REPO/releases?per_page=100" --jq '[.[] | select((.draft | not) and (.prerelease | not) and (.tag_name | startswith("mac-v")) and ((.body // "") | contains("<!-- simpl-critical -->")))] | sort_by(.tag_name | ltrimstr("mac-v") | split(".") | map(tonumber? // 0)) | last | (.tag_name // "") | ltrimstr("mac-v")' 2>/dev/null || true)"
  [ -n "$CRITICAL" ] && echo "Critical Update: $CRITICAL"
  if [ -n "$MAC_TAG" ]; then
    MAC_ZIP="https://github.com/$REPO/releases/download/$MAC_TAG/Simpl-Mac-${MAC_TAG#mac-v}.zip"
    REDIRECTS=build/site-sync/public/_redirects
    { grep -v '^/download/mac-app ' "$REDIRECTS" 2>/dev/null || true; echo "/download/mac-app $MAC_ZIP 302"; } > "$REDIRECTS.new"
    mv "$REDIRECTS.new" "$REDIRECTS"
    echo "/download/mac-app → $MAC_TAG"
    mkdir -p build/site-sync/public/app
    printf '%s' "$MAC_REL" | python3 -c '
import json, sys
rel, repo = json.loads(sys.stdin.read()), sys.argv[1]
tag = rel["tag_name"]; version = tag[len("mac-v"):]
asset = next((a for a in rel.get("assets", []) if a.get("name") == f"Simpl-Mac-{version}.zip"), None)
if not asset: sys.exit("no zip on " + tag)
digest = asset.get("digest") or ""
feed = {
  "version": version,
  "url": asset["browser_download_url"],
  "sha256": digest[7:] if digest.startswith("sha256:") else "",
  "size": asset.get("size"),
  "minimumOS": "14.0",
  "notes": rel.get("html_url") or f"https://github.com/{repo}/releases/tag/{tag}",
  "published": rel.get("published_at"),
}
if len(sys.argv) > 2 and sys.argv[2]: feed["criticalVersion"] = sys.argv[2]
json.dump(feed, open("build/site-sync/public/app/mac-latest.json", "w"), indent=2)
print("/app/mac-latest.json → " + version)
' "$REPO" "$CRITICAL" || echo "no Simpl for Mac feed written"
  fi
  # The Safari app (Simpl Courses for Mac, the extension's own): /download/mac and its update feed /app/latest.json at
  # the newest v* release with its zip — kept as scripts/release-mac-app.sh writes them, here too so the site always
  # catches up (a release published, promoted, edited or deleted by hand; the hourly run). Left as they are when the
  # site already says that version, or when GitHub gives no checksum for the zip (the app checks it).
  SAFARI_REL="$(gh api "repos/$REPO/releases?per_page=100" --jq '[.[] | select((.draft | not) and (.prerelease | not) and (.tag_name | test("^v[0-9]+\\.[0-9]+\\.[0-9]+$")) and any(.assets[]; .name | startswith("Simpl-Courses-Mac-")))] | sort_by(.tag_name | ltrimstr("v") | split(".") | map(tonumber? // 0)) | last' 2>/dev/null || true)"
  if [ -n "$SAFARI_REL" ] && [ "$SAFARI_REL" != "null" ]; then
    mkdir -p build/site-sync/public/app
    printf '%s' "$SAFARI_REL" | python3 -c '
import json, os, sys
rel, repo = json.loads(sys.stdin.read()), sys.argv[1]
tag = rel["tag_name"]; version = tag[1:]
asset = next((a for a in rel.get("assets", []) if a.get("name") == f"Simpl-Courses-Mac-{version}.zip"), None)
if not asset: sys.exit("no Safari app zip on " + tag)
feed_path = "build/site-sync/public/app/latest.json"
try: had = json.load(open(feed_path)).get("version")
except Exception: had = None
digest = asset.get("digest") or ""
if had != version and digest.startswith("sha256:"):
    json.dump({
        "version": version,
        "url": "https://simplcourses.com/download/mac",
        "download": asset["browser_download_url"],
        "sha256": digest[7:],
        "size": asset.get("size"),
        "minimumOS": "13.0",
        "notes": rel.get("html_url") or f"https://github.com/{repo}/releases/tag/{tag}",
        "published": rel.get("published_at"),
    }, open(feed_path, "w"), indent=2)
    print("/app/latest.json → " + version)
elif had != version:
    print("no checksum for " + asset["name"] + ": /app/latest.json left at " + str(had))
open("build/site-sync/safari-zip", "w").write(asset["browser_download_url"])
' "$REPO" || echo "no Safari app feed written"
    if [ -s build/site-sync/safari-zip ]; then
      REDIRECTS=build/site-sync/public/_redirects
      { grep -v '^/download/mac ' "$REDIRECTS" 2>/dev/null || true; echo "/download/mac $(cat build/site-sync/safari-zip) 302"; } > "$REDIRECTS.new"
      mv "$REDIRECTS.new" "$REDIRECTS"
      echo "/download/mac → $(cat build/site-sync/safari-zip)"
      rm -f build/site-sync/safari-zip
    fi
  fi
  # (2.99.38) the Safari app's beta feed, /app/latest-beta.json, for a Simpl Courses with Get beta updates on: the newest
  # v* release with the app's zip of all, a beta (a pre-release) or not — so a beta Mac also moves on to a release newer
  # than its beta. /download/mac and /app/latest.json never name a beta (above).
  SAFARI_BETA="$(gh api "repos/$REPO/releases?per_page=100" --jq '[.[] | select((.draft | not) and (.tag_name | test("^v[0-9]+\\.[0-9]+\\.[0-9]+$")) and any(.assets[]; .name | startswith("Simpl-Courses-Mac-")))] | sort_by(.tag_name | ltrimstr("v") | split(".") | map(tonumber? // 0)) | last' 2>/dev/null || true)"
  if [ -n "$SAFARI_BETA" ] && [ "$SAFARI_BETA" != "null" ]; then
    mkdir -p build/site-sync/public/app
    printf '%s' "$SAFARI_BETA" | python3 -c '
import json, sys
rel, repo = json.loads(sys.stdin.read()), sys.argv[1]
tag = rel["tag_name"]; version = tag[1:]
asset = next((a for a in rel.get("assets", []) if a.get("name") == f"Simpl-Courses-Mac-{version}.zip"), None)
if not asset: sys.exit("no Safari app zip on " + tag)
digest = asset.get("digest") or ""
if not digest.startswith("sha256:"): sys.exit("no checksum for " + asset["name"] + ": /app/latest-beta.json left as it is")
json.dump({
    "version": version,
    "url": asset["browser_download_url"],
    "sha256": digest[7:],
    "size": asset.get("size"),
    "minimumOS": "13.0",
    "notes": rel.get("html_url") or f"https://github.com/{repo}/releases/tag/{tag}",
    "published": rel.get("published_at"),
    "beta": bool(rel.get("prerelease")),
}, open("build/site-sync/public/app/latest-beta.json", "w"), indent=2)
print("/app/latest-beta.json → " + version + (" (beta)" if rel.get("prerelease") else ""))
' "$REPO" || echo "no Safari app beta feed written"
  fi
  # (1.3.17) the beta feed, /app/mac-beta.json, for Macs with Get beta updates on: the newest mac-v* release of all, a
  # beta (a pre-release) or not — so a beta Mac also moves on to a release newer than its beta
  BETA_REL="$(gh api "repos/$REPO/releases?per_page=100" --jq '[.[] | select((.draft | not) and (.tag_name | startswith("mac-v")))] | sort_by(.tag_name | ltrimstr("mac-v") | split(".") | map(tonumber? // 0)) | last' 2>/dev/null || true)"
  if [ -n "$BETA_REL" ] && [ "$BETA_REL" != "null" ]; then
    mkdir -p build/site-sync/public/app
    printf '%s' "$BETA_REL" | python3 -c '
import json, sys
rel, repo = json.loads(sys.stdin.read()), sys.argv[1]
tag = rel["tag_name"]; version = tag[len("mac-v"):]
asset = next((a for a in rel.get("assets", []) if a.get("name") == f"Simpl-Mac-{version}.zip"), None)
if not asset: sys.exit("no zip on " + tag)
digest = asset.get("digest") or ""
feed = {
  "version": version,
  "url": asset["browser_download_url"],
  "sha256": digest[7:] if digest.startswith("sha256:") else "",
  "size": asset.get("size"),
  "minimumOS": "14.0",
  "notes": rel.get("html_url") or f"https://github.com/{repo}/releases/tag/{tag}",
  "published": rel.get("published_at"),
  "beta": bool(rel.get("prerelease")),
}
if len(sys.argv) > 2 and sys.argv[2]: feed["criticalVersion"] = sys.argv[2]
json.dump(feed, open("build/site-sync/public/app/mac-beta.json", "w"), indent=2)
print("/app/mac-beta.json → " + version + (" (beta)" if feed["beta"] else ""))
' "$REPO" "$CRITICAL" || echo "no Simpl for Mac beta feed written"
  fi
  # Simpl for Windows (a beta: the landing page's Windows button), /download/windows: the newest windows-fluent-v*
  # release's installer, Simpl-Setup.exe (every one of them is a pre-release, the app's previews)
  WIN_EXE="$(gh api "repos/$REPO/releases?per_page=100" --jq '[.[] | select((.draft | not) and (.tag_name | test("^windows-fluent-v[0-9]+$")) and any(.assets[]; .name == "Simpl-Setup.exe"))] | sort_by(.tag_name | ltrimstr("windows-fluent-v") | tonumber) | last | (.assets // [] | map(select(.name == "Simpl-Setup.exe")) | first | .browser_download_url) // ""' 2>/dev/null || true)"
  if [ -n "$WIN_EXE" ]; then
    REDIRECTS=build/site-sync/public/_redirects
    { grep -v '^/download/windows ' "$REDIRECTS" 2>/dev/null || true; echo "/download/windows $WIN_EXE 302"; } > "$REDIRECTS.new"
    mv "$REDIRECTS.new" "$REDIRECTS"
    echo "/download/windows → $WIN_EXE"
  fi
  # Simpl for Windows' Critical Update feed, /app/windows-latest.json, which the app reads every five minutes between
  # its hourly looks at GitHub's releases (windows/SimplFluent/Support/Updates.cs, LookForCritical) — a file here, so
  # those looks never count against the sixty requests an hour GitHub's API allows a network without signing in: the
  # newest windows-fluent run, a beta's too, and the newest Critical Update's (a release whose notes carry the mark) of
  # the usual releases and of all. The releases read as the app reads them (UpdatesChoice.cs): a beta by its tag or its
  # title, a run published both as a beta and as a usual release counted once, as the usual one.
  WIN_RELS="$(gh api "repos/$REPO/releases?per_page=100" 2>/dev/null || true)"
  if [ -n "$WIN_RELS" ]; then
    mkdir -p build/site-sync/public/app
    printf '%s' "$WIN_RELS" | python3 -c '
import json, re, sys
tags = re.compile(r"^windows-fluent-(beta-)?v(\d+)$")
runs = {}
for r in json.loads(sys.stdin.read() or "[]"):
    m = tags.match(r.get("tag_name") or "")
    if r.get("draft") or not m: continue
    run = int(m.group(2))
    beta = bool(m.group(1)) or re.search(r"\bbeta\b", r.get("name") or "", re.I) is not None
    if run in runs and not runs[run][0]: continue
    runs[run] = (beta, "<!-- simpl-critical -->" in (r.get("body") or ""))
if not runs: sys.exit("no Simpl for Windows release")
def newest(usual, critical):
    found = [run for run, (beta, crit) in runs.items() if (not usual or not beta) and (not critical or crit)]
    return max(found) if found else None
feed = {"newest": newest(True, False), "newestBeta": newest(False, False), "critical": newest(True, True), "criticalBeta": newest(False, True)}
json.dump(feed, open("build/site-sync/public/app/windows-latest.json", "w"), indent=2)
print("/app/windows-latest.json → " + json.dumps(feed))
' || echo "no Simpl for Windows feed written"
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
