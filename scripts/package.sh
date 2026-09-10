#!/usr/bin/env bash
# Zips the web extension for other browsers (Chrome / Edge / Firefox) or for
# sharing. Output: dist/bettercourseviewer-<version>.zip
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="$(python3 -c "import json;print(json.load(open('$ROOT/extension/manifest.json'))['version'])")"
mkdir -p "$ROOT/dist"
OUT="$ROOT/dist/bettercourseviewer-$VERSION.zip"
rm -f "$OUT"
(cd "$ROOT/extension" && zip -qr "$OUT" . -x '.DS_Store' '*/.DS_Store')
echo "✅ $OUT"
