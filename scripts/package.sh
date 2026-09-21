#!/usr/bin/env bash
# Packages the web extension. Two zips land in dist/:
#
#   simpl-courses-<version>.zip         the extension/ folder as-is (what the Safari
#                                            converter reads; also loads in Firefox)
#   simpl-courses-chrome-<version>.zip  the Chrome / Edge build: the same files with the
#                                            manifest rewritten by scripts/chrome-manifest.py —
#                                            trimmed to what Chrome's Manifest V3 accepts (a
#                                            service worker only, no Firefox/Safari keys), and
#                                            given the run of every site plus the small script
#                                            that finds Canvas on its own (content/sniff.js).
#   simpl-courses-chrome-quiet-<version>.zip  the same, without any of that: Canvas's own domain
#                                            and nothing else, no script that looks at other
#                                            sites, so the listing need not ask for the broad
#                                            permission. A school's own address is added by hand.
#
# Either is a file the Chrome Web Store developer dashboard takes; which one to send depends on
# whether the listing is asking for every site.
#
# Needs: zip, python3 (both ship with macOS's command-line tools and with GitHub's runners).
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="$(python3 -c "import json;print(json.load(open('$ROOT/extension/manifest.json'))['version'])")"
mkdir -p "$ROOT/dist"
GENERIC="$ROOT/dist/simpl-courses-$VERSION.zip"
CHROME="$ROOT/dist/simpl-courses-chrome-$VERSION.zip"
QUIET="$ROOT/dist/simpl-courses-chrome-quiet-$VERSION.zip"
rm -f "$GENERIC" "$CHROME" "$QUIET"

# 1. as-is
(cd "$ROOT/extension" && zip -qr "$GENERIC" . -x '.DS_Store' '*/.DS_Store')

# 2. Chrome: stage a copy, rewrite the manifest, zip it with manifest.json at the root
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
cp -R "$ROOT/extension/." "$STAGE/"
python3 "$ROOT/scripts/chrome-manifest.py" "$STAGE/manifest.json"
(cd "$STAGE" && zip -qr "$CHROME" . -x '.DS_Store' '*/.DS_Store')

# 3. the quiet Chrome build: Canvas's own domain alone, and no script that looks at other sites
QSTAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE" "$QSTAGE"' EXIT
cp -R "$ROOT/extension/." "$QSTAGE/"
rm -f "$QSTAGE/content/sniff.js"
python3 "$ROOT/scripts/chrome-manifest.py" "$QSTAGE/manifest.json" --no-sniffer
(cd "$QSTAGE" && zip -qr "$QUIET" . -x '.DS_Store' '*/.DS_Store')

echo "✅ $GENERIC"
echo "✅ $CHROME  ← the Chrome Web Store build that finds Canvas on its own"
echo "✅ $QUIET  ← the same without that: Canvas's own domain alone, sites added by hand"
