#!/usr/bin/env bash
# Builds Simpl for Mac — the native app in mac/ (docs/mac-native.md), not the Safari extension's app in macos/ —
# signed with the team's Developer ID, notarized, stapled and zipped, and publishes it as a GitHub Release of its own
# (mac-v<version>, its zip Simpl-Mac-<version>.zip). A version already released is never published again: raise the
# version in scripts/dev/make-mac-project.py (MARKETING_VERSION, and BUILD for every upload) first. The site's Mac
# download and update feed stay the Safari app's; nothing here touches them. Run by
# .github/workflows/mac-native-release.yml (Actions → Simpl for Mac release → Run workflow).
#
# Needs, in the environment (the workflow takes them from the repository's secrets, the same five the Safari app's
# release uses — docs/mac-app.md):
#   MAC_CERT_P12        the Developer ID Application certificate with its key, as a .p12, base64
#   MAC_CERT_PASSWORD   the .p12's password
#   APPLE_ID            the Apple ID that owns the team
#   APPLE_APP_PASSWORD  an app-specific password for it
#   APPLE_TEAM_ID       the ten-character team identifier
#   GH_TOKEN            a token that can write releases (the workflow's)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
for v in MAC_CERT_P12 MAC_CERT_PASSWORD APPLE_ID APPLE_APP_PASSWORD APPLE_TEAM_ID; do
  if [[ -z "${!v:-}" ]]; then
    echo "$v is not set: Simpl for Mac cannot be signed for people to download (docs/mac-app.md explains the setup)." >&2
    exit 1
  fi
done
read -r VERSION BUILD < <(python3 - <<'PY'
import re
src = open('scripts/dev/make-mac-project.py').read()
v = re.search(r"^MARKETING_VERSION = '([^']+)'", src, re.M).group(1)
b = re.search(r"^BUILD = '([^']+)'", src, re.M).group(1)

print(v, b)
PY
)
INTERFACE="$(python3 -c "import json;print(json.load(open('extension/manifest.json'))['version'])")"
TAG="mac-v$VERSION"
ZIP_NAME="Simpl-Mac-$VERSION.zip"
echo "▶ Simpl for Mac $VERSION (build $BUILD), with the interface $INTERFACE"
if gh release view "$TAG" >/dev/null 2>&1; then
  echo "$TAG is already released, and a release is never replaced: raise MARKETING_VERSION (and BUILD) in scripts/dev/make-mac-project.py." >&2
  exit 1
fi

# ---- the project is the one the generator writes ------------------------------------------------------
python3 scripts/dev/make-mac-project.py >/dev/null
git diff --exit-code -- mac/Simpl.xcodeproj >/dev/null || { echo "mac/Simpl.xcodeproj is not what make-mac-project.py writes; commit the regenerated project." >&2; exit 1; }

# ---- the certificate, in a keychain of its own ----------------------------------------------------------
TMP="${RUNNER_TEMP:-$(mktemp -d)}"
KEYCHAIN="$TMP/simpl-native.keychain-db"
KEYPASS="$(uuidgen)"
security create-keychain -p "$KEYPASS" "$KEYCHAIN"
security set-keychain-settings -lut 21600 "$KEYCHAIN"
security unlock-keychain -p "$KEYPASS" "$KEYCHAIN"
echo "$MAC_CERT_P12" | base64 --decode > "$TMP/cert.p12"
security import "$TMP/cert.p12" -P "$MAC_CERT_PASSWORD" -A -t cert -f pkcs12 -k "$KEYCHAIN"
security set-key-partition-list -S apple-tool:,apple: -s -k "$KEYPASS" "$KEYCHAIN" >/dev/null
security list-keychains -d user -s "$KEYCHAIN" $(security list-keychains -d user | tr -d '"')
rm -f "$TMP/cert.p12"

# ---- build, signed for distribution outside the App Store ------------------------------------------------
xcodebuild -version
release_build() {
xcodebuild \
  -project mac/Simpl.xcodeproj \
  -scheme Simpl \
  -configuration Release \
  -destination 'generic/platform=macOS' \
  -derivedDataPath build/mac-native \
  CODE_SIGN_STYLE=Manual \
  CODE_SIGN_IDENTITY="Developer ID Application" \
  DEVELOPMENT_TEAM="$APPLE_TEAM_ID" \
  OTHER_CODE_SIGN_FLAGS="--timestamp" \
  CODE_SIGN_INJECT_BASE_ENTITLEMENTS=NO \
  ENABLE_HARDENED_RUNTIME=YES \
  ARCHS="arm64 x86_64" ONLY_ACTIVE_ARCH=NO \
  COMPILER_INDEX_STORE_ENABLE=NO DEBUG_INFORMATION_FORMAT=dwarf \
  ${MAC_BETA:+SWIFT_COMPILATION_MODE=singlefile} \
  build 2>&1 | tee build-mac-native.log | grep -E "error:|warning: .*mac/Simpl|BUILD (SUCCEEDED|FAILED)" || true
}
release_build
# (1.3.1: the last release's intermediates are reused; one that will not build is set aside and built from clean)
if ! grep -q "BUILD SUCCEEDED" build-mac-native.log && [[ -f build/mac-native/.mtimes-ns.json ]]; then
  echo "The incremental build failed; building clean." >&2
  rm -rf build/mac-native
  release_build
fi
grep -q "BUILD SUCCEEDED" build-mac-native.log || { grep -E "error:" -B2 -A6 build-mac-native.log | head -200; exit 1; }
# (1.3.31) A beta is compiled a file at a time (still optimized), so only what changed since the last beta is compiled
# again — a release proper is still compiled whole-module, all of it each time (minutes longer, a little quicker to run).
# (CODE_SIGN_INJECT_BASE_ENTITLEMENTS=NO: a plain build otherwise asks for the debugger's get-task-allow, which the
# notary service refuses; the hardened runtime is what notarization needs; both Apple silicon and Intel Macs; no index
# and no dSYM, which nothing here keeps — 1.3.1, quicker)
APP="build/mac-native/Build/Products/Release/Simpl.app"
[[ -d "$APP" ]] || { echo "The app was not built at $APP" >&2; exit 1; }
codesign --verify --deep --strict --verbose=2 "$APP"
echo "▶ Signed as:"
codesign -dvv "$APP" 2>&1 | grep -E "^(Identifier|Authority|TeamIdentifier|Timestamp|Runtime Version|Format)" | sed 's/^/    /'
echo "▶ Architectures: $(lipo -archs "$APP/Contents/MacOS/Simpl")"
echo "▶ Version: $(defaults read "$PWD/$APP/Contents/Info" CFBundleShortVersionString) ($(defaults read "$PWD/$APP/Contents/Info" CFBundleVersion))"

# ---- notarize, staple, zip ----------------------------------------------------------------------------------
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
  echo "Simpl for Mac was not notarized, so it was not published." >&2
  exit 1
fi
xcrun stapler staple "$APP"
xcrun stapler validate "$APP"
spctl --assess --type execute --verbose=2 "$APP" || true
rm -f "$ZIP"
ditto -c -k --keepParent "$APP" "$ZIP" # (the zip people download carries the stapled ticket)
SHA="$(shasum -a 256 "$ZIP" | cut -d' ' -f1)"
SIZE="$(stat -f%z "$ZIP")"
echo "✅ $ZIP ($SIZE bytes, sha256 $SHA)"

# ---- the release: a new one, never one replaced ---------------------------------------------------------------
NOTES="$TMP/notes.md"
# (1.3.17) a beta (MAC_BETA=1, the workflow's beta switch): a pre-release, which only the beta feed (/app/mac-beta.json)
# offers — to Macs with Settings ▸ Updates ▸ Get beta updates on; the usual feed passes it over
BETA_TITLE=""; BETA_FLAG=(); BETA_LINE=""
if [ -n "${MAC_BETA:-}" ]; then
  BETA_TITLE=" Beta"; BETA_FLAG=(--prerelease)
  BETA_LINE=$'\n**This is a beta.** Only Macs with *Settings ▸ Updates ▸ Get beta updates* on are offered it.\n'
fi
cat > "$NOTES" <<EOF
**Simpl for Mac $VERSION$BETA_TITLE** (build $BUILD) — the native Mac app, with Simpl's interface $INTERFACE inside.
$BETA_LINE
Download **$ZIP_NAME**, open it, and move **Simpl** to Applications. It is signed with the team's Developer ID and notarized by Apple. It needs macOS 14 or later, and runs on Apple silicon and Intel Macs.

This is not the Safari extension: that stays **Simpl Courses** (the \`Simpl-Courses-Mac-…\` download on the web releases).

SHA-256: \`$SHA\`
EOF
gh release create "$TAG" "$ZIP" --target "${GITHUB_SHA:-HEAD}" --title "Simpl for Mac $VERSION$BETA_TITLE" --notes-file "$NOTES" ${BETA_FLAG[@]+"${BETA_FLAG[@]}"}
echo "✅ https://github.com/${GITHUB_REPOSITORY:-26VirenS/BetterCourseViewer}/releases/tag/$TAG"
