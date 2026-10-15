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
#
# (1.3.82) Built ahead: .github/workflows/mac.yml runs this on every push with MAC_RELEASE_STEP=build — the app built
# and signed exactly as a beta's release builds it, verified, and left zipped in prebuilt/ with a stamp of the commit,
# the version and the build (no notarizing, nothing published; only the certificate's two secrets and the team are
# needed). The release, run with MAC_PREBUILT=<that folder> for the same commit (mac-native-release.yml finds it by the
# commit), takes that app instead of compiling: the stamp must name this commit, this version and build and this
# kind of release, and the app must carry them and be signed with the team's Developer ID with the hardened runtime,
# for both kinds of Mac — else it is built here as before. Either way it is notarized, stapled and checked before
# anything is published.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
STEP="${MAC_RELEASE_STEP:-release}"
case "$STEP" in release|build) ;; *) echo "MAC_RELEASE_STEP is release or build, not $STEP" >&2; exit 1 ;; esac
NEEDED=(MAC_CERT_P12 MAC_CERT_PASSWORD APPLE_TEAM_ID)
if [[ "$STEP" == release ]]; then NEEDED+=(APPLE_ID APPLE_APP_PASSWORD); fi
for v in "${NEEDED[@]}"; do
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
KIND="${MAC_BETA:+beta}"
KIND="${KIND:-stable}"
COMMIT="${GITHUB_SHA:-$(git rev-parse HEAD)}"
echo "▶ Simpl for Mac $VERSION (build $BUILD), with the interface $INTERFACE ($KIND, $STEP, commit $COMMIT)"
if gh release view "$TAG" >/dev/null 2>&1; then
  if [[ "$STEP" == release ]]; then
    echo "$TAG is already released, and a release is never replaced: raise MARKETING_VERSION (and BUILD) in scripts/dev/make-mac-project.py." >&2
    exit 1
  fi
  # (built ahead all the same: the next build is compiled from this one's intermediates, so it has only one push's
  #  changes to compile)
  echo "▶ $TAG is already released: this app is built only to keep the intermediates of the next one fresh"
fi

# ---- the project is the one the generator writes ------------------------------------------------------
python3 scripts/dev/make-mac-project.py >/dev/null
git diff --exit-code -- mac/Simpl.xcodeproj >/dev/null || { echo "mac/Simpl.xcodeproj is not what make-mac-project.py writes; commit the regenerated project." >&2; exit 1; }

TMP="${RUNNER_TEMP:-$(mktemp -d)}"

# ---- the certificate, in a keychain of its own (only when the app is built here) --------------------------------
import_certificate() {
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
}

# ---- build, signed for distribution outside the App Store ------------------------------------------------
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
build_app() {
import_certificate
xcodebuild -version
release_build
# (1.3.1: the last release's intermediates are reused; one that will not build is set aside and built from clean)
if ! grep -q "BUILD SUCCEEDED" build-mac-native.log && [[ -f build/mac-native/.mtimes-ns.json ]]; then
  echo "The incremental build failed; building clean." >&2
  rm -rf build/mac-native
  release_build
fi
grep -q "BUILD SUCCEEDED" build-mac-native.log || { grep -E "error:" -B2 -A6 build-mac-native.log | head -200; exit 1; }
# (1.3.82: how much was compiled, from the log — one push's changes when the intermediates are a push old)
echo "▶ Compiled: $(grep -c '^SwiftCompile ' build-mac-native.log || true) Swift compile steps, $(grep -c '^SwiftDriver ' build-mac-native.log || true) driver runs"
# (1.3.31) A beta is compiled a file at a time (still optimized), so only what changed since the last beta is compiled
# again — a release proper is still compiled whole-module, all of it each time (minutes longer, a little quicker to run).
# (CODE_SIGN_INJECT_BASE_ENTITLEMENTS=NO: a plain build otherwise asks for the debugger's get-task-allow, which the
# notary service refuses; the hardened runtime is what notarization needs; both Apple silicon and Intel Macs; no index
# and no dSYM, which nothing here keeps — 1.3.1, quicker)
APP="build/mac-native/Build/Products/Release/Simpl.app"
[[ -d "$APP" ]] || { echo "The app was not built at $APP" >&2; exit 1; }
}

# ---- the app checked: what is notarized and published must be all of this ----------------------------------------
# check_app <app>: signed (deep, strict) with the team's Developer ID Application certificate, a secure timestamp and
# the hardened runtime, for Apple silicon and Intel Macs, and this version and build — said, and false (with why)
# when it is not. (1.3.82: the app built ahead is held to it before it is taken; the app built here too.)
check_app() {
  local app="$1" abs info archs v b
  codesign --verify --deep --strict --verbose=2 "$app" || { echo "$app is not validly signed" >&2; return 1; }
  info="$(codesign -dvv "$app" 2>&1)"
  echo "▶ Signed as:"
  echo "$info" | grep -E "^(Identifier|Authority|TeamIdentifier|Timestamp|Runtime Version|Format)" | sed 's/^/    /'
  grep -q '^Authority=Developer ID Application: ' <<< "$info" || { echo "$app is not signed with a Developer ID Application certificate" >&2; return 1; }
  grep -qxF "TeamIdentifier=$APPLE_TEAM_ID" <<< "$info" || { echo "$app is not signed by the team" >&2; return 1; }
  grep -q '^Timestamp=' <<< "$info" || { echo "$app has no secure timestamp" >&2; return 1; }
  grep -q '^Runtime Version=' <<< "$info" || { echo "$app is not built with the hardened runtime" >&2; return 1; }
  archs="$(lipo -archs "$app/Contents/MacOS/Simpl")"
  echo "▶ Architectures: $archs"
  [[ " $archs " == *" arm64 "* && " $archs " == *" x86_64 "* ]] || { echo "$app is not for both Apple silicon and Intel" >&2; return 1; }
  abs="$(cd "$app" && pwd)"
  v="$(defaults read "$abs/Contents/Info" CFBundleShortVersionString)"
  b="$(defaults read "$abs/Contents/Info" CFBundleVersion)"
  echo "▶ Version: $v ($b)"
  [[ "$v" == "$VERSION" && "$b" == "$BUILD" ]] || { echo "$app is $v ($b), and this commit's project $VERSION ($BUILD)" >&2; return 1; }
}

# ---- (1.3.82) the app built ahead, taken when it is this release's ----------------------------------------------
# use_prebuilt <folder>: the app CI built ahead (Simpl.app.zip and stamp.txt in <folder>, MAC_RELEASE_STEP=build)
# unzipped and made APP — only when its stamp names this commit, version, build and kind of release and the app
# passes check_app; false (and why) otherwise, and the release builds the app itself
use_prebuilt() {
  local dir="$1" stamp="$1/stamp.txt" field
  [[ -f "$stamp" && -f "$dir/Simpl.app.zip" ]] || { echo "▶ No app built ahead in $dir: building it here"; return 1; }
  echo "▶ Built ahead: $(tr '\n' ' ' < "$stamp")"
  for field in "commit=$COMMIT" "version=$VERSION" "build=$BUILD" "kind=$KIND"; do
    grep -qxF "$field" "$stamp" || { echo "::warning::the app built ahead is not this release's ($field is not in its stamp): building it here"; return 1; }
  done
  rm -rf "$TMP/prebuilt"
  mkdir -p "$TMP/prebuilt"
  ditto -x -k "$dir/Simpl.app.zip" "$TMP/prebuilt" || { echo "::warning::the app built ahead would not unzip: building it here"; return 1; }
  [[ -d "$TMP/prebuilt/Simpl.app" ]] || { echo "::warning::the app built ahead has no Simpl.app: building it here"; return 1; }
  check_app "$TMP/prebuilt/Simpl.app" || { echo "::warning::the app built ahead did not pass its checks: building it here"; return 1; }
  APP="$TMP/prebuilt/Simpl.app"
}

if [[ "$STEP" == release && -n "${MAC_PREBUILT:-}" ]] && use_prebuilt "$MAC_PREBUILT"; then
  echo "▶ Taking the app built ahead for $COMMIT: nothing compiled here"
else
  build_app
  check_app "$APP" || exit 1
fi

# ---- (1.3.82) built ahead: the app and its stamp left for the release, nothing notarized or published -------------
if [[ "$STEP" == build ]]; then
  OUT="${MAC_PREBUILT_OUT:-prebuilt}"
  rm -rf "$OUT"
  if gh release view "$TAG" >/dev/null 2>&1; then echo "▶ $TAG is already released: no app kept for a release"; exit 0; fi
  mkdir -p "$OUT"
  ditto -c -k --keepParent "$APP" "$OUT/Simpl.app.zip"
  printf 'commit=%s\nversion=%s\nbuild=%s\nkind=%s\nxcode=%s\nrun=%s\n' "$COMMIT" "$VERSION" "$BUILD" "$KIND" \
    "$(xcodebuild -version | head -1)" "${GITHUB_RUN_ID:-}" > "$OUT/stamp.txt"
  echo "✅ Built ahead for a release: $OUT/Simpl.app.zip ($(stat -f%z "$OUT/Simpl.app.zip") bytes), $(tr '\n' ' ' < "$OUT/stamp.txt")"
  exit 0
fi

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
# (1.3.72) a Critical Update (MAC_CRITICAL=1, the workflow's critical switch): a release proper (never a beta), marked
# in its notes; the feeds name it (criticalVersion, scripts/publish-site.sh), and every Mac on an older version installs
# the newest release at its next check, whatever its update settings (Updater.swift)
if [ -n "${MAC_CRITICAL:-}" ]; then
  BETA_TITLE=" Critical Update"; BETA_FLAG=()
  BETA_LINE=$'\n**This is a Critical Update.** Every Mac running an older Simpl installs it at its next update check, whatever its update settings.\n<!-- simpl-critical -->\n'
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
