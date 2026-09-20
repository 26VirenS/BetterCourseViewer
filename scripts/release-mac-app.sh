#!/usr/bin/env bash
# Builds the Mac app for people to download from simplcourses.com — signed with a Developer ID,
# notarized, stapled and zipped — puts the zip on the GitHub Release for the manifest's version,
# and points the site at it: its update feed (public/app/latest.json on the site branch, which the
# app reads every hour) and its /download/mac address. Run by .github/workflows/package.yml on a
# version tag and on Run workflow; docs/mac-app.md walks through the one-time setup.
#
# Needs, in the environment (the workflow takes them from the repository's secrets):
#   MAC_CERT_P12        the Developer ID Application certificate with its key, as a .p12, base64
#   MAC_CERT_PASSWORD   the .p12's password
#   APPLE_ID            the Apple ID that owns the team
#   APPLE_APP_PASSWORD  an app-specific password for it (appleid.apple.com → Sign-In and Security)
#   APPLE_TEAM_ID       the ten-character team identifier
#   GH_TOKEN            a token that can write releases and push the site branch (the workflow's)
# Without the five Apple ones it says so and exits 0: the rest of the release is not held up.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
for v in MAC_CERT_P12 MAC_CERT_PASSWORD APPLE_ID APPLE_APP_PASSWORD APPLE_TEAM_ID; do
  if [[ -z "${!v:-}" ]]; then
    echo "$v is not set; skipping the Mac app (docs/mac-app.md explains the setup)."
    exit 0
  fi
done
VERSION="$(python3 -c "import json;print(json.load(open('extension/manifest.json'))['version'])")"
APP_NAME="Simpl Courses"
PROJECT="macos/$APP_NAME/$APP_NAME.xcodeproj"
TAG="v$VERSION"
ZIP_NAME="Simpl-Courses-Mac-$VERSION.zip"
# (the Release already carries the extension zips, simpl-courses-<version>.zip among them, and
# GitHub matches asset names without regard to case — so the app's zip says Mac in its name)
SITE="https://simplcourses.com"

# ---- the certificate, in a keychain of its own ----------------------------------------------
TMP="${RUNNER_TEMP:-$(mktemp -d)}"
KEYCHAIN="$TMP/build.keychain-db"
KEYPASS="$(uuidgen)"
security create-keychain -p "$KEYPASS" "$KEYCHAIN"
security set-keychain-settings -lut 21600 "$KEYCHAIN"
security unlock-keychain -p "$KEYPASS" "$KEYCHAIN"
echo "$MAC_CERT_P12" | base64 --decode > "$TMP/cert.p12"
security import "$TMP/cert.p12" -P "$MAC_CERT_PASSWORD" -A -t cert -f pkcs12 -k "$KEYCHAIN"
security set-key-partition-list -S apple-tool:,apple: -s -k "$KEYPASS" "$KEYCHAIN" >/dev/null
security list-keychains -d user -s "$KEYCHAIN" $(security list-keychains -d user | tr -d '"')
rm -f "$TMP/cert.p12"

# ---- build, signed for distribution outside the App Store ------------------------------------------
echo "▶ Building $APP_NAME $VERSION"
xcodebuild \
  -project "$PROJECT" \
  -scheme "$APP_NAME" \
  -configuration Release \
  -derivedDataPath build/mac \
  CODE_SIGN_STYLE=Manual \
  CODE_SIGN_IDENTITY="Developer ID Application" \
  DEVELOPMENT_TEAM="$APPLE_TEAM_ID" \
  OTHER_CODE_SIGN_FLAGS="--timestamp" \
  CODE_SIGN_INJECT_BASE_ENTITLEMENTS=NO \
  MARKETING_VERSION="$VERSION" \
  build | tail -n 20
# (CODE_SIGN_INJECT_BASE_ENTITLEMENTS=NO: a plain build otherwise carries the debugger's
# get-task-allow entitlement, and the notary service refuses an app that asks for it)
APP="build/mac/Build/Products/Release/$APP_NAME.app"
[[ -d "$APP" ]] || { echo "The app was not built at $APP" >&2; exit 1; }
codesign --verify --deep --strict --verbose=2 "$APP"
echo "▶ Signed as:"
codesign -dvv "$APP" 2>&1 | grep -E "^(Authority|TeamIdentifier|Timestamp|Runtime Version)" | sed 's/^/    /'
echo "▶ The app's entitlements:"
codesign -d --entitlements :- "$APP" 2>/dev/null | grep -E "<key>|<string>|<true/>|<false/>" | sed 's/^[[:space:]]*/    /'
echo "▶ The extension's entitlements:"
codesign -d --entitlements :- "$APP/Contents/PlugIns/$APP_NAME Extension.appex" 2>/dev/null | grep -E "<key>|<string>|<true/>|<false/>" | sed 's/^[[:space:]]*/    /'

# ---- notarize, staple, zip ---------------------------------------------------------------------------
mkdir -p dist
ZIP="dist/$ZIP_NAME"
rm -f "$ZIP"
ditto -c -k --keepParent "$APP" "$ZIP"
echo "▶ Notarizing"
SUBMIT="$(xcrun notarytool submit "$ZIP" --apple-id "$APPLE_ID" --password "$APPLE_APP_PASSWORD" --team-id "$APPLE_TEAM_ID" --wait --output-format json 2>&1 || true)"
echo "$SUBMIT"
SUBMISSION_ID="$(printf '%s' "$SUBMIT" | python3 -c "import json,sys; print(json.loads(sys.stdin.read().strip().splitlines()[-1]).get('id',''))" 2>/dev/null || true)"
STATUS="$(printf '%s' "$SUBMIT" | python3 -c "import json,sys; print(json.loads(sys.stdin.read().strip().splitlines()[-1]).get('status',''))" 2>/dev/null || true)"
if [[ "$STATUS" != "Accepted" ]]; then
  echo "▶ The notary service said: ${STATUS:-nothing}. Its log:"
  [[ -n "$SUBMISSION_ID" ]] && xcrun notarytool log "$SUBMISSION_ID" --apple-id "$APPLE_ID" --password "$APPLE_APP_PASSWORD" --team-id "$APPLE_TEAM_ID" || true
  echo "The app was not notarized, so it was not published." >&2
  exit 1
fi
xcrun stapler staple "$APP"
rm -f "$ZIP"
ditto -c -k --keepParent "$APP" "$ZIP" # the zip people download carries the stapled ticket
SHA="$(shasum -a 256 "$ZIP" | cut -d' ' -f1)"
SIZE="$(stat -f%z "$ZIP")"
echo "✅ $ZIP ($SIZE bytes, sha256 $SHA)"

# ---- the release --------------------------------------------------------------------------------------
if gh release view "$TAG" >/dev/null 2>&1; then
  gh release upload "$TAG" "$ZIP" --clobber
else
  gh release create "$TAG" "$ZIP" --target "${GITHUB_SHA:-HEAD}" --title "$APP_NAME $VERSION" --generate-notes
fi
ASSET="https://github.com/${GITHUB_REPOSITORY:-26VirenS/BetterCourseViewer}/releases/download/$TAG/$ZIP_NAME"

# ---- the site: the feed the app reads every hour, and the download address -------------------------
git fetch origin site:refs/remotes/origin/site
rm -rf build/site
git worktree add build/site origin/site
mkdir -p build/site/public/app
python3 - "$VERSION" "$ASSET" "$SHA" "$SIZE" "$TAG" > build/site/public/app/latest.json <<'PY'
import json, sys, datetime
version, asset, sha, size, tag = sys.argv[1:6]
print(json.dumps({
    "version": version,
    "url": "https://simplcourses.com/download/mac",
    "download": asset,
    "sha256": sha,
    "size": int(size),
    "minimumOS": "13.0",
    "notes": f"https://github.com/26VirenS/BetterCourseViewer/releases/tag/{tag}",
    "published": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
}, indent=2))
PY
REDIRECTS=build/site/public/_redirects
grep -v '^/download/mac ' "$REDIRECTS" > "$REDIRECTS.new" || true
echo "/download/mac $ASSET 302" >> "$REDIRECTS.new"
mv "$REDIRECTS.new" "$REDIRECTS"
(
  cd build/site
  git config user.name "github-actions[bot]"
  git config user.email "41898282+github-actions[bot]@users.noreply.github.com"
  git add -A
  git commit -q -m "Mac app $VERSION: the update feed and the download address" || echo "the site already points at $VERSION"
  git push origin HEAD:site
)
echo "✅ $SITE/download/mac → $ASSET; $SITE/app/latest.json says $VERSION"
