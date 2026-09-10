#!/usr/bin/env bash
# Builds the BetterCourseViewer macOS app (a Safari Web Extension host app).
#
# Requires Xcode (with the Command Line Tools selected) on macOS. Uses
# Apple's safari-web-extension-converter to generate an Xcode project from
# the extension/ folder, then optionally builds it with xcodebuild.
#
#   ./scripts/build-mac-app.sh            # generate macos/BetterCourseViewer.xcodeproj
#   ./scripts/build-mac-app.sh --build    # …and build the .app (Release)
#   ./scripts/build-mac-app.sh --open     # …and open the project in Xcode
#
# Environment overrides:
#   BUNDLE_ID   default: com.bettercourseviewer.app
#   APP_NAME    default: BetterCourseViewer
#   OUT_DIR     default: macos
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
EXT_DIR="$ROOT/extension"
OUT_DIR="${OUT_DIR:-$ROOT/macos}"
APP_NAME="${APP_NAME:-BetterCourseViewer}"
BUNDLE_ID="${BUNDLE_ID:-com.bettercourseviewer.app}"

BUILD=0
OPEN=0
for arg in "$@"; do
  case "$arg" in
    --build) BUILD=1 ;;
    --open) OPEN=1 ;;
    -h|--help) sed -n '2,16p' "$0"; exit 0 ;;
    *) echo "Unknown option: $arg" >&2; exit 1 ;;
  esac
done

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "This script must run on macOS (it needs Xcode's safari-web-extension-converter)." >&2
  exit 1
fi
if ! xcode-select -p >/dev/null 2>&1; then
  echo "Xcode is not installed or not selected. Install Xcode from the App Store, open it once, then run:" >&2
  echo "  sudo xcode-select -s /Applications/Xcode.app" >&2
  exit 1
fi
if ! xcrun --find safari-web-extension-converter >/dev/null 2>&1; then
  echo "safari-web-extension-converter not found. Make sure full Xcode (not just the Command Line Tools) is selected." >&2
  exit 1
fi

PROJECT="$OUT_DIR/$APP_NAME/$APP_NAME.xcodeproj"

if [[ -d "$PROJECT" ]]; then
  echo "▶ Xcode project already exists at $PROJECT"
  echo "  The extension files are referenced in place, so edits under extension/ are picked up on the next build."
  echo "  Delete $OUT_DIR to regenerate from scratch."
else
  echo "▶ Generating Xcode project in $OUT_DIR …"
  mkdir -p "$OUT_DIR"
  xcrun safari-web-extension-converter "$EXT_DIR" \
    --project-location "$OUT_DIR" \
    --app-name "$APP_NAME" \
    --bundle-identifier "$BUNDLE_ID" \
    --macos-only \
    --no-prompt \
    --no-open \
    --force
fi

if [[ "$BUILD" == "1" ]]; then
  echo "▶ Building $APP_NAME.app (Release, ad-hoc signed) …"
  DERIVED="$OUT_DIR/DerivedData"
  xcodebuild \
    -project "$PROJECT" \
    -scheme "$APP_NAME" \
    -configuration Release \
    -derivedDataPath "$DERIVED" \
    CODE_SIGN_IDENTITY="-" \
    CODE_SIGNING_REQUIRED=NO \
    CODE_SIGNING_ALLOWED=NO \
    build | tail -n 5
  APP_PATH="$DERIVED/Build/Products/Release/$APP_NAME.app"
  if [[ -d "$APP_PATH" ]]; then
    mkdir -p "$OUT_DIR/build"
    rm -rf "$OUT_DIR/build/$APP_NAME.app"
    cp -R "$APP_PATH" "$OUT_DIR/build/"
    echo
    echo "✅ Built: $OUT_DIR/build/$APP_NAME.app"
    echo "   Next: open the app once, then enable BetterCourseViewer in Safari → Settings → Extensions."
    echo "   Unsigned builds also need: Safari → Settings → Developer → Allow unsigned extensions."
  else
    echo "Build finished but the app was not found at $APP_PATH" >&2
    exit 1
  fi
fi

if [[ "$OPEN" == "1" ]]; then
  open "$PROJECT"
fi
