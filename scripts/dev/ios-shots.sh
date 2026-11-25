#!/usr/bin/env bash
# The iPhone app's screens on the iOS Simulator, against the mock Canvas: each tab, a counter's sheet,
# Notifications, a course and an assignment pushed from the native screens, a course's grades sheet,
# Hand In, a discussion, modules, files, Groups and the Inbox, a quiz and a tool — in light and in dark; then against
# the mock Brightspace (2.99.22): Today, a course, an assignment, its grades, a discussion, its content, in light; then
# the app's own test school (test.simpl.com): its sign-in page, and Today signed in.
# Run by .github/workflows/ios-shots.yml after the app is built and installed on a booted simulator and
# the mocks are serving on :8800 (node scripts/dev/mock-canvas.mjs 8800 8801) and :8860
# (node scripts/dev/mock-brightspace.mjs 8860 --open). Pictures in ./shots.
set -u
OUT="${OUT:-shots}"
mkdir -p "$OUT"
BID=com.simplcourses.app
xcrun simctl status_bar booted override --time 9:41 --batteryState charged --batteryLevel 100 --cellularBars 4 --wifiBars 3 >/dev/null 2>&1 || true
# every kind of quiz question in the mock's quizzes (matching, blanks, essay, formula, file upload), for the quiz pictures;
# and the website's made-up school's names (scripts/dev/site-names.mjs), so any picture can go on simplcourses.com
curl -s -o /dev/null -X POST -H 'content-type: application/json' -d '{"richQuestions":true,"moreTypes":true,"siteNames":true}' http://localhost:8800/__mock/config || true

# launch <name> <wait> [args…]: the app afresh with the mock as its Canvas (as a student who has used it:
# -SimplDemo), then a picture once the app says it has settled — "[Simpl Courses] shot: settled" on its console
# (Native/Engine.swift, watchForSettled: its screens up, the page's answers in, nothing asked for a moment) — <wait>
# seconds at most. A picture of what the app's answers do not say (a tool's page loading in its sheet, the school's
# sign-in page, a launch only to settle the simulator) waits the whole <wait> (SETTLE=no). The mock is the one at BASE
# (http://localhost:8800 unless set) — or, with BASE=test-school, the app's own test school: test.simpl.com picked as a
# student picks it, no address given, its Canvas answered inside the app (ios/SimplCourses/Web/TestSchool.swift), no
# server
launch() {
  local name="$1" wait="$2"
  shift 2
  local school=(-SimplBaseURL "${BASE:-http://localhost:8800}")
  [ "${BASE:-}" = "test-school" ] && school=(-canvasHost test.simpl.com)
  local settle="${SETTLE:-yes}"
  case "$name $*" in *-settle*|*" tool:"*|test-01-sign-in*) settle=no ;; esac
  xcrun simctl terminate booted "$BID" >/dev/null 2>&1 || true
  sleep 0.5
  xcrun simctl launch --console-pty booted "$BID" "${school[@]}" -setupOpened YES -SimplDemo YES -SimplShotSettle YES "$@" > "$OUT/console-$name.txt" 2>&1 &
  # (timed by the clock, not by counting naps: with the simulator busy a nap of a quarter second took twice that, and
  #  a count of them stretched a 40-second wait to 83)
  local t0=$SECONDS how="after ${wait}s"
  if [ "$settle" = yes ]; then
    while [ $((SECONDS - t0)) -lt "$wait" ]; do
      if grep -q "shot: settled" "$OUT/console-$name.txt" 2>/dev/null; then how="settled"; break; fi
      sleep 0.3
    done
    sleep 0.5 # (the frame after the last change drawn)
  else
    sleep "$wait"
  fi
  xcrun simctl io booted screenshot "$OUT/$name.png" >/dev/null 2>&1 && echo "shot $name ($((SECONDS - t0))s, $how)" || echo "no shot $name"
  echo "$name $((SECONDS - t0))s $how" >> "$OUT/shot-times.txt"
}

# tour <prefix> [args…]: (iPhone 1.9, build 72) the tour after the first setup, gone through by the app itself
# (-SimplTour drive: Native/Tour.swift) — each step pictured as the app says it is showing, with its ring placed
# ("tour: shot <name>", then four seconds before the step is done for it), until "tour: done" (three minutes at most).
# A tour that does not finish is said in tour.txt with its last lines.
tour() {
  local prefix="$1"
  shift
  local log="$OUT/console-$prefix-tour.txt" seen=0 t0=$SECONDS
  xcrun simctl terminate booted "$BID" >/dev/null 2>&1 || true
  sleep 0.5
  xcrun simctl launch --console-pty booted "$BID" -SimplBaseURL "${BASE:-http://localhost:8800}" -setupOpened YES -SimplDemo YES -SimplTour drive "$@" > "$log" 2>&1 &
  while [ $((SECONDS - t0)) -lt 180 ]; do
    # (done looked for first: the pictures said before it are all taken before the loop ends)
    local finished=no
    grep -q "tour: done" "$log" 2>/dev/null && finished=yes
    local names
    names=$(grep -o "tour: shot [a-z0-9-]*" "$log" 2>/dev/null | awk '{print $3}')
    local n
    n=$(printf '%s\n' "$names" | grep -c .)
    while [ "$seen" -lt "$n" ]; do
      seen=$((seen + 1))
      local name
      name="$prefix-tour-$(printf '%02d' "$seen")-$(printf '%s\n' "$names" | sed -n "${seen}p")"
      xcrun simctl io booted screenshot "$OUT/$name.png" >/dev/null 2>&1 && echo "shot $name" || echo "no shot $name"
      echo "$name $((SECONDS - t0))s tour" >> "$OUT/shot-times.txt"
    done
    [ "$finished" = yes ] && break
    sleep 0.25
  done
  if ! grep -q "tour: done" "$log"; then
    { echo "tour $prefix: DID NOT FINISH — last lines:"; tail -30 "$log"; } | tee -a "$OUT/tour.txt"
  fi
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
  launch "$mode-05b-appointments" 18 -SimplTab calendar -SimplOpen appointments # (1.9 build 68: Find Appointment)
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
  # (1.4) a finished quiz with an attempt left (Take Quiz and See Feedback side by side); the review's lines, formulas read as maths
  launch "$mode-18b-quiz-done" 18 -SimplOpen quiz:101:9001
  launch "$mode-19b-quiz-review" 26 -SimplOpen quiz:101:9011:review
  launch "$mode-20-tool" 20 -SimplOpen tool:101:9
  launch "$mode-21-setup" 18 -SimplOpen setup
  # (1.4.3) Settings, all of it the phone's own rows: the look, grades, the record, data
  launch "$mode-22-settings" 18 -SimplOpen settings
  # (1.9, build 72) the tour after the first setup, step by step
  tour "$mode"
  # (1.6.1) the first run: the school searched for by name
  launch "$mode-23-school" 12 -SimplPicker merced
done
# (2.99.22) Brightspace: a made-up student's (scripts/dev/mock-brightspace.mjs), in light — no Inbox or Groups in the menu
xcrun simctl ui booted appearance light || true
export BASE=http://localhost:8860
launch d2l-settle 16
rm -f "$OUT/d2l-settle.png" "$OUT/console-d2l-settle.txt"
launch d2l-01-today 22
launch d2l-02-course 20 -SimplTab courses -SimplPush /courses/31001
launch d2l-03-assignment 20 -SimplTab todo -SimplPush /courses/31001/assignments/702
launch d2l-04-grades-sheet 18 -SimplTab grades -SimplOpen grades:31001
launch d2l-05-discussion 20 -SimplTab courses -SimplPush /courses/31001/discussion_topics/2000000902
launch d2l-06-modules 18 -SimplTab courses -SimplPush /courses/31001/modules
# the test school (test.simpl.com), in light: its sign-in page as App Review first sees it (the app's own sign-in form over
# Canvas's page), then Today once signed in — by -SimplTestSchoolSignedIn, the suite's own sign-in (a debug build with
# -SimplDemo only), so CI never needs the password
export BASE=test-school
launch test-01-sign-in 20
launch test-02-today 28 -SimplTestSchoolSignedIn YES
unset BASE
xcrun simctl terminate booted "$BID" >/dev/null 2>&1 || true
collect end

# (iPhone 1.8) the wide screen, in light: an iPhone Duo unfolded — or, where this Xcode has none, an iPad mini, its
# sides both 600 points or more as the Duo's inner display's are — Today, Courses, To Do and Grades in two columns or
# cards, a course's grades and an assignment
WIDE=$(xcrun simctl list devices available | grep -E "iPhone Duo \(" | head -1 | grep -oE "[0-9A-F-]{36}")
WIDE_NAME="iPhone Duo"
# (on an iPad, -SimplWideWidth holds the app to the Duo's inner display: 626 points across upright — one column there —
# and 890 on its side, two; the largest iPad there is, so 890 fits across it upright)
WARGS=""
LARGS=""
if [ -z "$WIDE" ]; then
  WIDE=$(xcrun simctl list devices available | grep -E "iPad Pro 13|iPad Air 13|iPad Pro \(12\.9" | head -1 | grep -oE "[0-9A-F-]{36}")
  [ -z "$WIDE" ] && WIDE=$(xcrun simctl list devices available | grep -E "iPad" | head -1 | grep -oE "[0-9A-F-]{36}")
  WIDE_NAME="$(xcrun simctl list devices available | grep "$WIDE" | sed -E 's/ \(.*//; s/^ +//') at the Duo's widths"
  WARGS="-SimplWideWidth 626"
  LARGS="-SimplWideWidth 890"
fi
if [ -n "$WIDE" ]; then
  echo "wide: $WIDE_NAME $WIDE" | tee "$OUT/wide-device.txt"
  xcrun simctl shutdown booted >/dev/null 2>&1 || true
  xcrun simctl boot "$WIDE" && xcrun simctl bootstatus "$WIDE" -b
  xcrun simctl install booted "build/Build/Products/Debug-iphonesimulator/Simpl Courses.app"
  xcrun simctl status_bar booted override --time 9:41 --batteryState charged --batteryLevel 100 --wifiBars 3 >/dev/null 2>&1 || true
  xcrun simctl ui booted appearance light || true
  launch wide-settle 40 $WARGS
  rm -f "$OUT/wide-settle.png" "$OUT/console-wide-settle.txt"
  launch wide-01-today 24 $WARGS
  launch wide-02-courses 16 -SimplTab courses $WARGS
  launch wide-03-todo 16 -SimplTab todo $WARGS
  launch wide-04-grades 16 -SimplTab grades $WARGS
  launch wide-10-assignment 22 -SimplTab todo -SimplPush /courses/101/assignments/1001 $WARGS
  launch wide-11-grades-sheet 20 -SimplTab grades -SimplOpen grades:101 $WARGS
  tour wide $WARGS # (build 72: one column — the courses as cards)
  # the Duo on its side: two columns
  launch wide-l-01-today 18 $LARGS
  launch wide-l-03-todo 16 -SimplTab todo $LARGS
  launch wide-l-04-grades 16 -SimplTab grades $LARGS
  launch wide-l-10-assignment 22 -SimplTab todo -SimplPush /courses/101/assignments/1001 $LARGS
  # (1.8.1) two pages, meeting at the fold: the Courses cards a column each side of it; a course opened beside them;
  # the Inbox beside Today
  launch wide-l-02-courses 16 -SimplTab courses $LARGS
  launch wide-l-09-course 20 -SimplTab courses -SimplPush /courses/101 $LARGS
  launch wide-l-17-inbox 18 -SimplPush /conversations $LARGS
  tour wide-l $LARGS # (build 72: two columns — Switch Course)
  # (iPhone 1.8.1, build 43) folded and opened again and again on each tab, something opened on the way: the app must
  # stay up through it and say "fold cycle: done" (NativeShell.foldCycle)
  # (the cycle takes about 45 seconds once the app's screens are up, which can itself take 40 on a slow simulator: up to
  # 120 seconds, done as soon as it says so)
  SETTLE=no launch wide-fold-cycle 1 -SimplFoldCycle YES $LARGS
  t0=$SECONDS
  while [ $((SECONDS - t0)) -lt 120 ] && ! grep -q "fold cycle: done" "$OUT/console-wide-fold-cycle.txt"; do sleep 1; done
  sleep 1
  xcrun simctl io booted screenshot "$OUT/wide-fold-cycle.png" >/dev/null 2>&1 || true # (the app as the cycle left it)
  if grep -q "fold cycle: done" "$OUT/console-wide-fold-cycle.txt"; then
    echo "fold cycle: stayed up" | tee "$OUT/fold-cycle.txt"
  else
    { echo "fold cycle: DID NOT FINISH — last steps:"; grep "fold cycle\|rail:\|split:" "$OUT/console-wide-fold-cycle.txt" | tail -8; tail -40 "$OUT/console-wide-fold-cycle.txt"; } | tee "$OUT/fold-cycle.txt"
  fi
  xcrun simctl terminate booted "$BID" >/dev/null 2>&1 || true
  collect wide
else
  echo "wide: no iPhone Duo or iPad mini simulator on this Mac" | tee "$OUT/wide-device.txt"
fi

# smaller pictures (the contents API reads files up to 1 MB)
for f in "$OUT"/*.png; do sips -Z 1400 "$f" >/dev/null 2>&1 || true; done
ls -la "$OUT"
