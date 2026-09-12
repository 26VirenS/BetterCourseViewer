#!/usr/bin/env bash
# Packages the web extension. Two zips land in dist/:
#
#   simpl-courses-<version>.zip         the extension/ folder as-is (what the Safari
#                                            converter reads; also loads in Firefox)
#   simpl-courses-chrome-<version>.zip  the Chrome / Edge build: the same files with the
#                                            manifest trimmed to what Chrome's Manifest V3
#                                            accepts (a service worker only, no Firefox/Safari
#                                            keys). This is the file the Chrome Web Store
#                                            developer dashboard takes.
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
python3 - "$STAGE/manifest.json" <<'PY'
import json, sys
path = sys.argv[1]
m = json.load(open(path))
bg = m.get('background', {})
bg.pop('scripts', None)      # Firefox / older-Safari background page; Chrome MV3 runs the service worker
bg.pop('persistent', None)
m['background'] = bg
m.pop('author', None)        # not a Chrome key (it would only produce an "unrecognized key" warning)
# Safari draws the toolbar button from the icon's alpha channel, so the source manifest points it at the
# mark-only glyphs; Chrome shows the toolbar icon in colour, so the Chrome build uses the blue tile there.
m.setdefault('action', {})['default_icon'] = {s: f'icons/icon-{s}.png' for s in ('48', '96', '128')}
assert len(m['description']) <= 132, 'the Chrome Web Store uses the manifest description as the summary (132 characters max)'
with open(path, 'w') as f:
    json.dump(m, f, indent=2)
    f.write('\n')
PY
(cd "$STAGE" && zip -qr "$CHROME" . -x '.DS_Store' '*/.DS_Store')

# 3. the Mac uninstaller, for a Mac whose app is already in the Trash (the app itself carries a copy)
UNINSTALL="$ROOT/dist/uninstall-simpl-courses-mac.command"
cp "$ROOT/scripts/uninstall-mac.command" "$UNINSTALL"
chmod +x "$UNINSTALL"

echo "✅ $GENERIC"
echo "✅ $CHROME  ← upload this one to the Chrome Web Store (or Edge Add-ons)"
echo "✅ $UNINSTALL"
