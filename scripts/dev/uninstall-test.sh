#!/usr/bin/env bash
# The Mac uninstaller (scripts/uninstall-mac.command), against a home folder built for the purpose.
# It never touches the real one: HOME points at a temporary tree and every path it removes is inside it.
#
#   ./scripts/dev/uninstall-test.sh
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SCRIPT="$ROOT/scripts/uninstall-mac.command"
PASS=0
FAIL=0

check() { # check <description> <condition…>
  local what="$1"; shift
  if "$@"; then printf '  ✓ %s\n' "$what"; PASS=$((PASS + 1)); else printf '  ✗ %s\n' "$what"; FAIL=$((FAIL + 1)); fi
}
gone() { [[ ! -e "$1" ]]; }
there() { [[ -e "$1" ]]; }
says() { grep -qF -- "$2" "$1"; } # -- so a needle like "--app" is not read as a flag

app_at() { # app_at <path> [bundle id]  — an app bundle; no id at all when the third argument is "none"
  mkdir -p "$1/Contents/Resources"
  case "${2:-}" in
    none) : ;;
    *) printf '<plist><dict><key>CFBundleIdentifier</key>\n<string>%s</string></dict></plist>' "$2" > "$1/Contents/Info.plist" ;;
  esac
}

make_home() { # a Mac with two copies of the app, someone else's lookalike, and the data of both
  HOME_DIR="$(mktemp -d)"
  export HOME="$HOME_DIR"
  APPS="$HOME_DIR/Applications"
  app_at "$APPS/Simpl Courses.app" com.simplcourses.app
  app_at "$APPS/Simpl Courses 2.app" com.simplcourses.app          # the Finder's name for a second copy
  app_at "$APPS/Simpl Courses Pro.app" com.someone.else            # not ours: only the name is alike
  app_at "$APPS/Other.app" com.other.app
  mkdir -p "$HOME_DIR/Library/Containers/com.simplcourses.app" \
           "$HOME_DIR/Library/Containers/com.simplcourses.app.Extension" \
           "$HOME_DIR/Library/Preferences" \
           "$HOME_DIR/Library/Containers/com.apple.Safari/Data/Library/WebKit/WebExtensions/1A2B-UUID" \
           "$HOME_DIR/Library/Containers/com.apple.Safari/Data/Library/WebKit/WebExtensions/OTHER-UUID"
  echo '{"id":"com.simplcourses.app.Extension"}' > "$HOME_DIR/Library/Containers/com.apple.Safari/Data/Library/WebKit/WebExtensions/1A2B-UUID/manifest.json"
  echo '{"id":"com.someone.else"}' > "$HOME_DIR/Library/Containers/com.apple.Safari/Data/Library/WebKit/WebExtensions/OTHER-UUID/manifest.json"
  touch "$HOME_DIR/Library/Preferences/com.simplcourses.app.plist"
  OUT="$HOME_DIR/out.txt"
  export SIMPL_APP_DIRS="$APPS" # the search stays inside this home: never the real Applications folder
}
# Every run sets SIMPL_APP_DIRS to the temporary home's Applications folder, so the search cannot
# reach the real one (this suite is meant to be runnable on a Mac that has the app installed).

echo "a Mac with two copies of the app"
make_home
bash "$SCRIPT" --yes > "$OUT" 2>&1
check "both copies go to the Trash" gone "$APPS/Simpl Courses.app"
check "the numbered second copy goes too" gone "$APPS/Simpl Courses 2.app"
check "they are in the Trash, not deleted" there "$HOME_DIR/.Trash/Simpl Courses.app"
check "an app that only shares the name is left alone" there "$APPS/Simpl Courses Pro.app"
check "and it is named as kept, with its own identifier" says "$OUT" "kept    $APPS/Simpl Courses Pro.app (com.someone.else)"
check "an unrelated app is never looked at" there "$APPS/Other.app"
check "the extension's storage in Safari goes" gone "$HOME_DIR/Library/Containers/com.apple.Safari/Data/Library/WebKit/WebExtensions/1A2B-UUID"
check "another extension's storage stays" there "$HOME_DIR/Library/Containers/com.apple.Safari/Data/Library/WebKit/WebExtensions/OTHER-UUID"
check "the app's own container goes" gone "$HOME_DIR/Library/Containers/com.simplcourses.app"
check "the preference file goes" gone "$HOME_DIR/Library/Preferences/com.simplcourses.app.plist"
check "nothing is reported as stuck" bash -c '! grep -q "could not be" "$1"' _ "$OUT"
rm -rf "$HOME_DIR"

echo "a dry run"
make_home
bash "$SCRIPT" --dry-run > "$OUT" 2>&1
check "it lists both copies" bash -c 'grep -c "^  app     " "$1" | grep -qx 2' _ "$OUT"
check "and removes nothing" there "$APPS/Simpl Courses 2.app"
check "saying so" says "$OUT" "Dry run: nothing was removed."
rm -rf "$HOME_DIR"

echo "the copy inside the app bundle"
make_home
cp "$SCRIPT" "$APPS/Simpl Courses.app/Contents/Resources/uninstall-mac.command"
TMPDIR="$HOME_DIR/tmp"; mkdir -p "$TMPDIR"; export TMPDIR
bash "$APPS/Simpl Courses.app/Contents/Resources/uninstall-mac.command" --yes > "$OUT" 2>&1
check "it trashes the bundle it was running from" gone "$APPS/Simpl Courses.app"
check "and the other copy with it" gone "$APPS/Simpl Courses 2.app"
check "its working copy cleans itself up" bash -c '[[ -z "$(ls -A "$1" 2>/dev/null)" ]]' _ "$TMPDIR"
rm -rf "$HOME_DIR"
unset TMPDIR # it pointed inside the home folder just removed

echo "an app somewhere else"
make_home
ELSEWHERE="$HOME_DIR/Projects/build"
mkdir -p "$ELSEWHERE"
app_at "$ELSEWHERE/Simpl Courses.app" com.simplcourses.app
rm -rf "$APPS/Simpl Courses.app" "$APPS/Simpl Courses 2.app"
bash "$SCRIPT" --yes > "$OUT" 2>&1
check "the usual folders come up empty, and it says where it looked" says "$OUT" "app     none found. Looked in:"
check "with the way to point at it" says "$OUT" "--app "
bash "$SCRIPT" --yes --app "$ELSEWHERE/Simpl Courses.app" > "$OUT" 2>&1
check "--app removes that one" gone "$ELSEWHERE/Simpl Courses.app"
rm -rf "$HOME_DIR"

echo "a copy whose Info.plist cannot be read"
make_home
rm -rf "$APPS/Simpl Courses 2.app"
app_at "$APPS/Simpl Courses 3.app" none
bash "$SCRIPT" --yes > "$OUT" 2>&1
check "the name is enough when there is no identifier to read" gone "$APPS/Simpl Courses 3.app"
rm -rf "$HOME_DIR"

echo "an app that cannot be moved"
if [[ "$(id -u)" -eq 0 ]]; then
  echo "  – skipped: running as root, which no folder refuses (this one is for a real Mac)"
else
make_home
rm -rf "$APPS/Simpl Courses 2.app" "$APPS/Simpl Courses Pro.app"
chmod 500 "$APPS" # the folder refuses both the move and the delete
bash "$SCRIPT" --yes > "$OUT" 2>&1
chmod 700 "$APPS"
check "it says what is stuck instead of claiming success" says "$OUT" "could not be"
check "and where it is" says "$OUT" "$APPS/Simpl Courses.app"
check "the data still went" gone "$HOME_DIR/Library/Containers/com.simplcourses.app"
rm -rf "$HOME_DIR"
fi

echo "a Mac with nothing on it"
HOME_DIR="$(mktemp -d)"; export HOME="$HOME_DIR"; OUT="$HOME_DIR/out.txt"; export SIMPL_APP_DIRS="$HOME_DIR/Applications"
bash "$SCRIPT" --yes > "$OUT" 2>&1
check "it says there is nothing to do" says "$OUT" "Nothing of Simpl Courses was found"
rm -rf "$HOME_DIR"

echo
if [[ "$FAIL" -eq 0 ]]; then echo "All $PASS checks passed."; else echo "$FAIL of $((PASS + FAIL)) checks failed."; fi
exit $(( FAIL > 0 ? 1 : 0 ))
