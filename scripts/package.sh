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
#                                            This is the file the Chrome Web Store developer
#                                            dashboard takes.
#
# Needs: zip, python3 (both ship with macOS's command-line tools and with GitHub's runners).
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="$(python3 -c "import json;print(json.load(open('$ROOT/extension/manifest.json'))['version'])")"
mkdir -p "$ROOT/dist"
GENERIC="$ROOT/dist/simpl-courses-$VERSION.zip"
CHROME="$ROOT/dist/simpl-courses-chrome-$VERSION.zip"
rm -f "$GENERIC" "$CHROME"

# 1. as-is
(cd "$ROOT/extension" && zip -qr "$GENERIC" . -x '.DS_Store' '*/.DS_Store')

# 2. Chrome: stage a copy, rewrite the manifest, zip it with manifest.json at the root
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
cp -R "$ROOT/extension/." "$STAGE/"
python3 "$ROOT/scripts/chrome-manifest.py" "$STAGE/manifest.json"
(cd "$STAGE" && zip -qr "$CHROME" . -x '.DS_Store' '*/.DS_Store')

echo "✅ $GENERIC"
echo "✅ $CHROME  ← upload this one to the Chrome Web Store (or Edge Add-ons)"
