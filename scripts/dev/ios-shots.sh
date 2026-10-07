#!/usr/bin/env bash
# The iPhone app's screens on the iOS Simulator, against the mock Canvas: each tab, a counter's sheet,
# Notifications, a course and an assignment pushed from the native screens, a course's grades sheet,
# Hand In, a discussion, modules, files, Groups and the Inbox, a quiz and a tool — in light and in dark.
# Run by .github/workflows/ios-shots.yml after the app is built and installed on a booted simulator and
# the mock is serving on :8800 (node scripts/dev/mock-canvas.mjs 8800 8801). Pictures in ./shots.
set -u
OUT="${OUT:-shots}"
mkdir -p "$OUT"
BID=com.simplcourses.app
xcrun simctl status_bar booted override --time 9:41 --batteryState charged --batteryLevel 100 --cellularBars 4 --wifiBars 3 >/dev/null 2>&1 || true
# every kind of quiz question in the mock's quizzes (matching, blanks, essay, formula, file upload), for the quiz pictures
curl -s -o /dev/null -X POST -H 'content-type: application/json' -d '{"richQuestions":true,"moreTypes":true}' http://localhost:8800/__mock/config || true

# launch <name> <wait> [args…]: the app afresh with the mock as its Canvas (as a student who has used it:
# -SimplDemo), then a picture after <wait> seconds
launch() {
  local name="$1" wait="$2"
  shift 2
  xcrun simctl terminate booted "$BID" >/dev/null 2>&1 || true
  sleep 1
  xcrun simctl launch --console-pty booted "$BID" -SimplBaseURL http://localhost:8800 -setupOpened YES -SimplDemo YES "$@" > "$OUT/console-$name.txt" 2>&1 &
  sleep "$wait"
  xcrun simctl io booted screenshot "$OUT/$name.png" >/dev/null 2>&1 && echo "shot $name" || echo "no shot $name"
}

# the app's crash reports and its own log lines, kept with the pictures (a picture of the home screen is an app
# that did not stay up: these say why)
collect() {
  local name="$1"
  xcrun simctl spawn booted log show --last 2m --style compact --predicate 'process CONTAINS "Simpl" OR eventMessage CONTAINS "Simpl Courses"' 2>&1 | tail -c 400000 > "$OUT/log-$name.txt" || true
  for f in "$HOME"/Library/Logs/DiagnosticReports/*Simpl*; do
    [ -f "$f" ] && head -c 600000 "$f" > "$OUT/crash-$(basename "$f" | tr ' ' '-').txt"
  done
}

# a first launch to warm the simulator (the first start of a fresh install is slow), not pictured
launch warmup 40
rm -f "$OUT/warmup.png"
collect warmup

for mode in light dark; do
  xcrun simctl ui booted appearance "$mode" || true
  # (the first launch after the appearance changes can come up behind the home screen: one launch to settle it, not pictured)
  launch "$mode-settle" 16
  rm -f "$OUT/$mode-settle.png" "$OUT/console-$mode-settle.txt"
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
  # (1.2) the course's own screens, Groups and the Inbox, drawn by the app; the grades and Hand In sheets
  launch "$mode-11-grades-sheet" 18 -SimplTab grades -SimplOpen grades:101
  launch "$mode-12-hand-in" 20 -SimplTab todo -SimplPush /courses/101/assignments/1012 -SimplOpen handin
  launch "$mode-13-discussion" 20 -SimplTab courses -SimplPush /courses/101/discussion_topics/7003
  launch "$mode-14-modules" 18 -SimplTab courses -SimplPush /courses/101/modules
  launch "$mode-15-files" 18 -SimplTab courses -SimplPush /courses/101/files
  launch "$mode-16-groups" 16 -SimplPush /groups
  launch "$mode-17-inbox" 16 -SimplPush /conversations
  # (1.3) a quiz in the app's own screen: its intro, a question being answered; a course's tool in its sheet
  launch "$mode-18-quiz" 18 -SimplOpen quiz:101:9011
  launch "$mode-19-quiz-question" 24 -SimplOpen quiz:101:9011:take:5
  launch "$mode-20-tool" 20 -SimplOpen tool:101:9
  launch "$mode-21-setup" 18 -SimplOpen setup
done
xcrun simctl terminate booted "$BID" >/dev/null 2>&1 || true
collect end

# smaller pictures (the contents API reads files up to 1 MB)
for f in "$OUT"/*.png; do sips -Z 1400 "$f" >/dev/null 2>&1 || true; done
ls -la "$OUT"
