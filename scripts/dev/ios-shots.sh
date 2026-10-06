#!/usr/bin/env bash
# The iPhone app's screens on the iOS Simulator, against the mock Canvas: each tab, a counter's sheet,
# Notifications, a course and an assignment pushed from the native screens — in light and in dark.
# Run by .github/workflows/ios-shots.yml after the app is built and installed on a booted simulator and
# the mock is serving on :8800 (node scripts/dev/mock-canvas.mjs 8800 8801). Pictures in ./shots.
set -u
OUT="${OUT:-shots}"
mkdir -p "$OUT"
BID=com.simplcourses.app
VER=$(node -p "require('./extension/manifest.json').version")
FLOW=$(node -e "const s=require('fs').readFileSync('extension/background.js','utf8');console.log((s.match(/const SETUP_FLOW = (\d+)/)||[])[1]||3)")
SEED="{\"setup:offered\":true,\"setup:done\":true,\"welcome:search\":true,\"tools:welcomed\":true,\"setup:flow\":${FLOW},\"whatsnew:seen\":\"${VER}\"}"
xcrun simctl status_bar booted override --time 9:41 --batteryState charged --batteryLevel 100 --cellularBars 4 --wifiBars 3 >/dev/null 2>&1 || true

# launch <name> <wait> [args…]: the app afresh with the mock as its Canvas, then a picture after <wait> seconds
launch() {
  local name="$1" wait="$2"
  shift 2
  xcrun simctl terminate booted "$BID" >/dev/null 2>&1 || true
  sleep 1
  xcrun simctl launch --console-pty booted "$BID" -SimplBaseURL http://localhost:8800 -setupOpened YES -SimplSeed "$SEED" "$@" > "$OUT/console-$name.txt" 2>&1 &
  sleep "$wait"
  xcrun simctl io booted screenshot "$OUT/$name.png" >/dev/null 2>&1 && echo "shot $name" || echo "no shot $name"
}

for mode in light dark; do
  xcrun simctl ui booted appearance "$mode" || true
  launch "$mode-01-today" 22
  launch "$mode-02-courses" 14 -SimplTab courses
  launch "$mode-03-todo" 14 -SimplTab todo
  launch "$mode-04-grades" 16 -SimplTab grades
  launch "$mode-05-calendar" 14 -SimplTab calendar
  launch "$mode-06-search" 10 -SimplTab search
  launch "$mode-07-sheet-next" 16 -SimplSheet next
  launch "$mode-08-notifications" 16 -SimplPush notifications
  launch "$mode-09-course" 20 -SimplTab courses -SimplPush /courses/101
  launch "$mode-10-assignment" 20 -SimplTab todo -SimplPush /courses/101/assignments/1001
done
xcrun simctl terminate booted "$BID" >/dev/null 2>&1 || true

# smaller pictures (the contents API reads files up to 1 MB)
for f in "$OUT"/*.png; do sips -Z 1400 "$f" >/dev/null 2>&1 || true; done
ls -la "$OUT"
