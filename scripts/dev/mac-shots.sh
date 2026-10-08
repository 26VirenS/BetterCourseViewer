#!/usr/bin/env bash
# Simpl for Mac's screens against the mock Canvas: the Dashboard, each place in the sidebar, a course and its
# sections, an assignment, Hand In, a discussion, the Inbox, a quiz, a tool's window, the setup and Settings — in
# light and in dark. Run by .github/workflows/mac.yml once the app is built and the mock is serving on :8800
# (node scripts/dev/mock-canvas.mjs 8800 8801). Pictures in ./shots: <name>.png from screencapture (the screen as
# shown) and <name>-app.png from inside the app (Shot.swift), in case the runner may not record the screen.
set -u
OUT="${OUT:-shots}"
APP="${APP:-build/Build/Products/Debug/Simpl.app}"
BIN="$APP/Contents/MacOS/Simpl"
mkdir -p "$OUT"
OUT_ABS="$(cd "$OUT" && pwd)"
# every kind of quiz question in the mock's quizzes (matching, blanks, essay, formula, file upload), for the quiz pictures
curl -s -o /dev/null -X POST -H 'content-type: application/json' -d '{"richQuestions":true,"moreTypes":true}' http://localhost:8800/__mock/config || true
defaults write com.simplcourses.mac ApplePersistenceIgnoreState -bool YES >/dev/null 2>&1 || true

# shoot <name> <wait> [args…]: the app afresh with the mock as its Canvas (as a student who has used it: -SimplDemo),
# pictured <wait> seconds after it starts
shoot() {
  local name="$1" wait="$2"
  shift 2
  pkill -x Simpl >/dev/null 2>&1 || true
  sleep 0.6
  rm -f "$OUT_ABS/$name.rect" "$OUT_ABS/$name.png" "$OUT_ABS/$name-app.png"
  "$BIN" -SimplBaseURL http://localhost:8800 -SimplDemo YES -SimplAppearance "$MODE" \
    -SimplShotFile "$OUT_ABS/$name.png" -SimplShotAfter "$wait" -SimplWindowSize 1280x820 \
    -NSQuitAlwaysKeepsWindows NO "$@" > "$OUT_ABS/console-$name.txt" 2>&1 &
  local pid=$!
  local i=0
  while [ ! -f "$OUT_ABS/$name.rect" ] && [ $i -lt $(( (wait + 25) * 4 )) ]; do
    sleep 0.25
    i=$((i + 1))
  done
  if [ -f "$OUT_ABS/$name.rect" ]; then
    screencapture -x -R"$(cat "$OUT_ABS/$name.rect")" "$OUT_ABS/$name.png" >/dev/null 2>&1 && echo "shot $name" || echo "no screencapture $name"
  else
    echo "no picture $name (the app did not get there)"
  fi
  kill "$pid" >/dev/null 2>&1 || true
  sleep 0.4
  kill -9 "$pid" >/dev/null 2>&1 || true
  # (a console with nothing worth keeping is dropped)
  [ -s "$OUT_ABS/console-$name.txt" ] || rm -f "$OUT_ABS/console-$name.txt"
}

# a first launch to warm WebKit and the mock (the first start is slow), not pictured
MODE=light shoot warmup 25
rm -f "$OUT_ABS"/warmup*

for MODE in light dark; do
  shoot "$MODE-01-dashboard" 14
  shoot "$MODE-02-courses" 12 -SimplPlace courses
  shoot "$MODE-03-todo" 12 -SimplPlace todo
  shoot "$MODE-04-grades" 14 -SimplPlace grades
  shoot "$MODE-05-calendar" 12 -SimplPlace calendar
  shoot "$MODE-06-notifications" 12 -SimplPlace notifications
  shoot "$MODE-07-inbox" 12 -SimplPlace inbox
  shoot "$MODE-08-course" 14 -SimplPlace course:101
  shoot "$MODE-09-assignments" 12 -SimplPlace section:courses/101:assignments
  shoot "$MODE-10-assignment" 14 -SimplPlace course:101 -SimplPush /courses/101/assignments/1001
  shoot "$MODE-11-course-grades" 14 -SimplPlace section:courses/101:grades
  shoot "$MODE-12-hand-in" 16 -SimplPlace course:101 -SimplPush /courses/101/assignments/1012 -SimplOpen handin
  shoot "$MODE-13-discussion" 14 -SimplPlace course:101 -SimplPush /courses/101/discussion_topics/7003
  shoot "$MODE-14-modules" 12 -SimplPlace section:courses/101:modules
  shoot "$MODE-15-files" 12 -SimplPlace section:courses/101:files
  shoot "$MODE-16-groups" 12 -SimplPlace groups
  shoot "$MODE-17-search" 12 -SimplPlace search:essay
  shoot "$MODE-18-quiz" 14 -SimplOpen quiz:101:9011
  shoot "$MODE-19-quiz-question" 18 -SimplOpen quiz:101:9011:take:5
  shoot "$MODE-20-quiz-review" 18 -SimplOpen quiz:101:9011:review
  shoot "$MODE-21-tool" 16 -SimplOpen tool:101:9
  shoot "$MODE-22-setup" 14 -SimplOpen setup
  shoot "$MODE-23-settings" 12 -SimplOpen settings
  shoot "$MODE-24-new-task" 12 -SimplPlace todo -SimplOpen newtask
  shoot "$MODE-25-compose" 12 -SimplPlace inbox -SimplOpen compose
  shoot "$MODE-26-school" 8 -SimplPicker merced
done
pkill -x Simpl >/dev/null 2>&1 || true
# the app's crash reports, kept with the pictures (a missing picture is an app that did not stay up: these say why)
for f in "$HOME"/Library/Logs/DiagnosticReports/*Simpl*; do
  [ -f "$f" ] && head -c 600000 "$f" > "$OUT_ABS/crash-$(basename "$f" | tr ' ' '-').txt"
done
exit 0
