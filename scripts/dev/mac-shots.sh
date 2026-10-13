#!/usr/bin/env bash
# Simpl for Mac's screens against the mock Canvas: the Dashboard, each place in the sidebar, a course and its
# sections, an assignment, Hand In, a discussion, the Inbox, a quiz, a tool's window, the setup and Settings — in
# light and in dark; then against the mock Brightspace (2.99.22), its own: the Dashboard, a course, its work, an
# assignment, its grades, its content, a discussion. Run by .github/workflows/mac.yml once the app is built and the
# mocks are serving on :8800 (node scripts/dev/mock-canvas.mjs 8800 8801) and :8860 (mock-brightspace.mjs 8860 --open).
# Pictures in ./shots: <name>-app.png, taken from inside the app of its own windows (Shot.swift).
set -u
OUT="${OUT:-shots}"
APP="${APP:-build/Build/Products/Debug/Simpl.app}"
BIN="$APP/Contents/MacOS/Simpl"
mkdir -p "$OUT"
OUT_ABS="$(cd "$OUT" && pwd)"
# every kind of quiz question in the mock's quizzes (matching, blanks, essay, formula, file upload), for the quiz pictures
curl -s -o /dev/null -X POST -H 'content-type: application/json' -d '{"richQuestions":true,"moreTypes":true}' http://localhost:8800/__mock/config || true
defaults write com.simplcourses.mac ApplePersistenceIgnoreState -bool YES >/dev/null 2>&1 || true

# (1.3.1) Several copies of the app at once (JOBS, 5 by default), each with a home of its own (CFFIXED_USER_HOME: its
# settings, its caches and its web storage apart from the others'), each picture taken from inside its app of its own
# windows only (Shot.swift) as soon as the app has settled — no answers asked for in a moment — the <wait> being now
# only the longest it waits. A picture of a web page loading (a school's sign-in, a tool's window, Quick Look) waits
# the whole <wait> (-SimplShotSettle NO).
JOBS="${JOBS:-5}"
HOMES="$(mktemp -d /tmp/simpl-shots.XXXXXX)"
for i in $(seq 1 "$JOBS"); do mkdir -p "$HOMES/home$i/Library/Preferences"; done

# a free worker's number (its lock taken), waiting for one while all are busy
take_slot() {
  while true; do
    for i in $(seq 1 "$JOBS"); do
      if mkdir "$HOMES/lock$i" 2>/dev/null; then echo "$i"; return; fi
    done
    sleep 0.2
  done
}

# shoot <name> <wait> [args…]: the app afresh with the mock as its Canvas (as a student who has used it: -SimplDemo),
# pictured once it has settled, <wait> seconds at most (SIZE=900x820 shoot …: a window of another size). It runs in the
# background on a free worker; `wait` at the end gathers them.
shoot() {
  local name="$1" wait="$2"
  shift 2
  local slot
  slot="$(take_slot)"
  local settle=YES
  case " $* " in *PickerConfirm*|*" tool:"*|*" alta:"*|*" macmillan:"*|*" file:"*|*SimplHandInPreview*) settle=NO ;; esac
  (
    rm -f "$OUT_ABS/$name.rect" "$OUT_ABS/$name.png" "$OUT_ABS/$name-app.png"
    CFFIXED_USER_HOME="$HOMES/home$slot" "$BIN" -SimplBaseURL "${BASE:-http://localhost:8800}" -SimplDemo YES -SimplAppearance "$MODE" \
      -SimplShotFile "$OUT_ABS/$name.png" -SimplShotAfter "$wait" -SimplShotSettle "$settle" -SimplWindowSize "${SIZE:-1280x820}" \
      -NSQuitAlwaysKeepsWindows NO "$@" > "$OUT_ABS/console-$name.txt" 2>&1 &
    local pid=$!
    local i=0
    while [ ! -f "$OUT_ABS/$name.rect" ] && [ $i -lt $(( (wait + 25) * 4 )) ] && kill -0 "$pid" 2>/dev/null; do
      sleep 0.25
      i=$((i + 1))
    done
    if [ -f "$OUT_ABS/$name-app.png" ]; then echo "shot $name ($((i / 4))s)"; else echo "no picture $name (the app did not get there)"; fi
    echo "$name $((i / 4))s worker$slot" >> "$OUT_ABS/shot-times.txt"
    kill "$pid" >/dev/null 2>&1 || true
    sleep 0.3
    kill -9 "$pid" >/dev/null 2>&1 || true
    # (a console with nothing worth keeping is dropped)
    [ -s "$OUT_ABS/console-$name.txt" ] || rm -f "$OUT_ABS/console-$name.txt"
    rmdir "$HOMES/lock$slot"
  ) &
}

# (1.3.78) Send from Phone, tried as a phone would: phone_send <name> <file>… waits for the picture <name>'s app to say
# its server's address on the console (-SimplPhoneTest), opens the page there, asks for a wrong key's, then posts each
# file as the phone's page does (X-File-Name, its type, its length); what it got back goes in phone-test.txt. The app
# holds its picture until a file has come, so the picture shows them arriving; phone_check reads the consoles after.
phone_send() {
  local name="$1"
  shift
  (
    local console="$OUT_ABS/console-$name.txt" url="" i=0 codes=""
    while [ -z "$url" ] && [ $i -lt 360 ]; do
      url="$(grep -o 'SimplPhone ready http://[^ ]*' "$console" 2>/dev/null | head -1 | cut -d' ' -f3)"
      if [ -z "$url" ]; then sleep 0.25; i=$((i + 1)); fi
    done
    if [ -z "$url" ]; then echo "$name: no address on the console" >> "$OUT_ABS/phone-test.txt"; exit 0; fi
    local base="${url%/*/}"
    local page wrong
    page="$(curl -s -m 10 -o "$OUT_ABS/phone-page-$name.html" -w '%{http_code}' "$url")"
    wrong="$(curl -s -m 10 -o /dev/null -w '%{http_code}' "$base/AAAAAAAAAAAAAAAAAAAAAA/")"
    sleep 1
    for f in "$@"; do
      local as="${f##*=}" path="${f%%=*}"
      codes="$codes $(curl -s -m 30 -o /dev/null -w '%{http_code}' -X POST -H 'Expect:' -H "X-File-Name: ${as// /%20}" \
        -H "Content-Type: $(file -b --mime-type "$path")" --data-binary @"$path" "${url}upload")"
      sleep 0.6
    done
    echo "$name: page $page, wrong key $wrong, uploads$codes" >> "$OUT_ABS/phone-test.txt"
  ) &
}

# phone_check: what the server answered and what the app's consoles say came into the hand-in, as phone-check.txt
phone_check() {
  local out="$OUT_ABS/phone-check.txt" t="$OUT_ABS/phone-test.txt"
  : > "$out"
  ok() { if eval "$2"; then echo "ok   $1" >> "$out"; else echo "FAIL $1" >> "$out"; fi; }
  ok "the page opened (200) and a wrong key did not (404): $(grep 12d "$t" 2>/dev/null)" "grep -q '12d-hand-in-phone-connected: page 200, wrong key 404, uploads 200 200' '$t'"
  ok "the page has the assignment's name in it" "grep -q 'Composition of Functions' '$OUT_ABS/phone-page-light-12d-hand-in-phone-connected.html'"
  ok "the phone was seen to connect" "grep -q 'SimplPhone opened' '$OUT_ABS/console-light-12d-hand-in-phone-connected.txt'"
  ok "the photo is in the hand-in" "grep -q 'SimplHandIn added Photo from phone.jpg' '$OUT_ABS/console-light-12d-hand-in-phone-connected.txt'"
  ok "the notes are in the hand-in" "grep -q 'SimplHandIn added Notes.txt' '$OUT_ABS/console-light-12d-hand-in-phone-connected.txt'"
  ok "a PDF-only assignment's photos were taken and an mp3 refused (415): $(grep 12e "$t" 2>/dev/null)" "grep -q '12e-hand-in-phone-photos: page 200, wrong key 404, uploads 200 200 415' '$t'"
  ok "its two photos became one PDF in the hand-in" "[ \"\$(grep -c 'SimplHandIn added project01.pdf (1 files)' '$OUT_ABS/console-dark-12e-hand-in-phone-photos.txt')\" -ge 2 ]"
  cat "$out"
}

# a first launch in each worker's home to warm WebKit and the mock (the first start is slow), not pictured
for i in $(seq 1 "$JOBS"); do MODE=light shoot "warmup$i" 20; done
wait
rm -f "$OUT_ABS"/warmup* "$OUT_ABS"/console-warmup*

# the app's crash reports, kept with the pictures (a missing picture is an app that did not stay up: these say why)
keep_crashes() {
  pkill -x Simpl >/dev/null 2>&1 || true
  for f in "$HOME"/Library/Logs/DiagnosticReports/*Simpl*; do
    [ -f "$f" ] && head -c 600000 "$f" > "$OUT_ABS/crash-$(basename "$f" | tr ' ' '-').txt"
  done
}

# (1.3.78) a walk through the app as a student's clicks would make it (Support/OpenClock.swift): each root place, a
# course and an assignment, then each again — the console's "[Simpl] open <screen>: <ms> (<where from>)" lines are the
# time of each first visit and each visit again
WALK="todo,grades,calendar,notifications,inbox,course:101,externaltools,dashboard,todo,grades,calendar,notifications,inbox,course:101,push:/courses/101/assignments/1001,back,externaltools,dashboard"

# (1.2.1) SHOTS=quick, a push's set: the main screens once each, a few minutes — every screen in light and dark is
# SHOTS=full (the workflow's own choice, or a commit's message saying [full shots])
if [ "${SHOTS:-full}" = quick ]; then
  MODE=light
  shoot light-01-dashboard 12
  shoot light-01w-walk 60 -SimplWalk "$WALK"
  shoot light-01d-dashboard-counter 14 -SimplSheet next
  shoot light-01r-counter-middle 14 -SimplSheet tomorrow # (1.3.16: a middle tile's panel centred on it)
  shoot light-21a-alta 20 -SimplOpen alta:104:4003 # (1.3.17: a Knewton Alta assignment in the quiz-style popup)
  shoot light-21c-alta-start 20 -SimplOpen alta:104:4004 # (1.3.59: an assignment's cover as the popup's start screen)
  shoot light-21f-alta-pins 20 -SimplOpen alta:104:4003 -SimplPinnedTools "calc,need,ptable" # (1.3.73: the toolbar's pins on the popup's own bar)
  shoot light-21g-alta-float 22 -SimplOpen alta:104:4003 -SimplPinnedTools "calc,need" -SimplPinFloat calc # (1.3.73: a quick panel pulled out, floating over the popup)
  shoot light-21d-macmillan 20 -SimplOpen macmillan:104:4005 # (1.3.66: a Macmillan Learning assignment's popup)
  shoot light-21e-second-window 18 -SimplOpen window:section:courses/101:grades # (1.3.69: a window more, on a course's grades: its breakdown bar)
  shoot light-11-course-grades 14 -SimplPlace section:courses/101:grades # (1.3.69: the breakdown bar in the main window)
  shoot light-36-external-tools 16 -SimplPlace externaltools # (1.3.67: every external tool in the courses)
  shoot light-36b-external-tools-list 7 -SimplPlace externaltools -SimplExternalToolList knewton # (1.3.77: every place Knewton Alta is used, to choose from)
  shoot light-37-developer 16 -SimplOpen developer # (1.3.72: the Developer window)
  shoot light-01p-dashboard-preview 16 -SimplOpen preview
  shoot light-01q-counter-preview 18 -SimplSheet today -SimplOpen preview
  shoot light-05p-calendar-preview 16 -SimplPlace calendar -SimplOpen preview
  shoot light-02-courses 10 -SimplPlace courses
  shoot light-04-grades 12 -SimplPlace grades
  shoot light-07-inbox 10 -SimplPlace inbox
  shoot light-07b-inbox-thread 18 -SimplPlace inbox -SimplOpen conversation:first
  shoot light-10-assignment 12 -SimplPlace course:101 -SimplPush /courses/101/assignments/1001
  shoot light-10b-grades-assignment 12 -SimplPlace grades -SimplPush /courses/101/assignments/1001
  shoot light-10c-feedback 18 -SimplPlace course:101 -SimplPush /courses/101/assignments/1008 -SimplOpen feedback
  shoot light-10d-assignment-submitted 12 -SimplPlace course:101 -SimplPush /courses/101/assignments/1008
  SIZE=1440x900 shoot light-10e-assignment-wide 12 -SimplPlace course:101 -SimplPush /courses/101/assignments/1008
  SIZE=1440x900 shoot light-10f-assignment-wide-quiz 12 -SimplPlace course:101 -SimplPush /courses/101/assignments/1001
  shoot light-32-rubric-ring 16 -SimplPlace course:104 -SimplPush /courses/104/assignments/4001 -SimplOpen rubric
  shoot light-32b-rubric-open 16 -SimplPlace course:104 -SimplPush /courses/104/assignments/4001 -SimplOpen rubric:1
  shoot light-19-quiz-question 14 -SimplOpen quiz:101:9011:take:5
  shoot light-19d-quiz-click 18 -SimplOpen quiz:101:9011:take:1 -SimplQuizClick 4
  shoot light-19f-quiz-feedback 18 -SimplOpen quiz:101:9001:feedback
  shoot light-19e-quiz-click-next 18 -SimplOpen quiz:101:9011:take:1 -SimplQuizClick next
  shoot light-35-tools 10 -SimplPlace tools
  shoot light-26-school 8 -SimplPicker merced
  shoot light-26b-school-confirm 18 -SimplPicker catcourses -SimplPickerConfirm YES
  # (1.3.73) the sign-in: Simpl's own card over the school's sign-in page (the mock run with --login, on :8802)
  BASE=http://localhost:8802 shoot light-60-sign-in 10
  shoot light-27-tour 12 -SimplOpen tour
  # (1.3) themes, To Do's fixed side column, a narrow window, the setup's nicknames
  shoot light-50-theme-dusk 12 -SimplTheme Dusk
  shoot light-50b-theme-ocean-todo 12 -SimplTheme Ocean -SimplPlace todo
  shoot light-50d-theme-photo 12 -SimplThemePhoto "$PWD/scripts/dev/fixtures/photo.jpg"
  shoot light-51-settings-appearance 12 -SimplOpen settings -SimplSettingsTab appearance -SimplTheme Ocean -SimplPhotoSlot today
  shoot light-51b-settings-picker 14 -SimplOpen settings -SimplSettingsTab appearance -SimplPickerOpen YES
  shoot light-03-todo 12 -SimplPlace todo
  shoot light-12-hand-in 16 -SimplPlace course:101 -SimplPush /courses/101/assignments/1012 -SimplOpen handin
  shoot light-12b-hand-in-preview 18 -SimplPlace course:101 -SimplPush /courses/101/assignments/1012 -SimplOpen handin -SimplHandInPreview "$PWD/docs/screenshots/calendar.png"
  # (1.3.78) Send from Phone: its panel with the code; the phone connected and two files in (posted by phone_send); a
  # PDF-only assignment's photos made into one PDF, and a kind it does not take refused
  shoot light-12c-hand-in-phone 18 -SimplPlace course:101 -SimplPush /courses/101/assignments/1012 -SimplOpen handin -SimplPhone YES
  printf 'Notes from class, sent from a phone.\n' > "$OUT_ABS/phone-notes.txt"
  printf 'ID3 not really music\n' > "$OUT_ABS/phone-song.mp3"
  shoot light-12d-hand-in-phone-connected 40 -SimplPlace course:101 -SimplPush /courses/101/assignments/1012 -SimplOpen handin -SimplPhone YES -SimplPhoneTest YES
  phone_send light-12d-hand-in-phone-connected "$PWD/scripts/dev/fixtures/photo.jpg=Photo from phone.jpg" "$OUT_ABS/phone-notes.txt=Notes.txt"
  curl -s -o /dev/null -X POST -H 'content-type: application/json' -d '{"ext":{"1013":["pdf"]}}' http://localhost:8800/__mock/config || true
  shoot light-22-setup 14 -SimplOpen setup
  shoot light-53-whats-new 12 -SimplOpen whatsnew
  shoot light-54-pin-element 14 -SimplPinnedTools "ptable" -SimplPinOpen ptable -SimplPinElement 25 -SimplPTColour category
  # (1.3.52) the full Periodic Table: coloured by electronegativity with its trend arrows; an element's page; the Lewis
  # maker; the 3D molecule builder
  SIZE=1440x920 shoot light-55-ptable-trends 14 -SimplPlace tools -SimplTool ptable -SimplPTColour electronegativity
  SIZE=1440x920 shoot light-55b-ptable-element 16 -SimplPlace tools -SimplTool ptable -SimplPTElement 6
  # (1.3.76) the element page's Cloud view: carbon's whole atom, and the cloud model's test vectors on its console
  SIZE=1440x920 shoot light-55k-ptable-cloud 16 -SimplPlace tools -SimplTool ptable -SimplPTElement 6 -SimplPTModel cloud -SimplCloudCheck YES
  SIZE=1440x920 shoot light-55c-ptable-lewis 14 -SimplPlace tools -SimplTool ptable -SimplPTPage lewis -SimplLewisPreset "NO₃⁻"
  SIZE=1440x920 shoot light-55f-lewis-benzene 14 -SimplPlace tools -SimplTool ptable -SimplPTPage lewis -SimplLewisPreset Benzene
  SIZE=1440x920 shoot light-55g-lewis-ionic 14 -SimplPlace tools -SimplTool ptable -SimplPTPage lewis -SimplLewisPreset "NaNO₃"
  SIZE=1440x920 shoot light-55d-ptable-molecule 16 -SimplPlace tools -SimplTool ptable -SimplPTPage molecules -SimplPTMolecule Ethanol
  # (1.3.74) shapes laid out by VSEPR: carbon dioxide straight, water bent
  SIZE=1440x920 shoot light-55k-molecule-co2 16 -SimplPlace tools -SimplTool ptable -SimplPTPage molecules -SimplPTMolecule "Carbon dioxide"
  SIZE=1440x920 shoot light-55l-molecule-water 16 -SimplPlace tools -SimplTool ptable -SimplPTPage molecules -SimplPTMolecule Water
  # (1.3.59) the tour, at its Draw your own step; (1.3.61) and at its first, the table's carbon lit
  SIZE=1440x920 shoot light-55h-ptable-tour 16 -SimplPlace tools -SimplTool ptable -SimplPTTour 4
  SIZE=1440x920 shoot light-55i-ptable-tour-start 16 -SimplPlace tools -SimplTool ptable -SimplPTTour 0
  # (1.3.64) the header's search open, with what it finds
  SIZE=1440x920 shoot light-55j-ptable-search 14 -SimplPlace tools -SimplTool ptable -SimplPTColour category -SimplPTSearch ne
  SIZE=780x700 shoot light-52-narrow-dashboard 14
  MODE=dark
  SIZE=1440x920 shoot dark-55e-ptable-element 16 -SimplPlace tools -SimplTool ptable -SimplPTElement 26
  # (1.3.76) iron's cloud in dark, its 3d chosen: the rest of the atom grey
  SIZE=1440x920 shoot dark-55l-ptable-cloud 16 -SimplPlace tools -SimplTool ptable -SimplPTElement 26 -SimplPTModel cloud -SimplPTSubshell 3d
  shoot dark-50-theme-forest 12 -SimplTheme Forest
  shoot dark-50c-theme-dusk 12 -SimplTheme Dusk
  shoot dark-50d-theme-photo 12 -SimplThemePhoto "$PWD/scripts/dev/fixtures/photo.jpg"
  shoot dark-51b-settings-picker 14 -SimplOpen settings -SimplSettingsTab appearance -SimplPickerOpen YES
  shoot dark-01-dashboard 12
  shoot dark-36b-external-tools-list 7 -SimplPlace externaltools -SimplExternalToolList knewton # (1.3.77)
  shoot dark-32b-rubric-open 16 -SimplPlace course:104 -SimplPush /courses/104/assignments/4001 -SimplOpen rubric:1
  BASE=http://localhost:8802 shoot dark-60-sign-in 10
  shoot dark-12c-hand-in-phone 18 -SimplPlace course:101 -SimplPush /courses/101/assignments/1012 -SimplOpen handin -SimplPhone YES
  shoot dark-12e-hand-in-phone-photos 40 -SimplPlace course:101 -SimplPush /courses/101/assignments/1013 -SimplOpen handin -SimplPhone YES -SimplPhoneTest YES
  phone_send dark-12e-hand-in-phone-photos "$PWD/scripts/dev/fixtures/photo.jpg=IMG_0001.jpg" "$PWD/scripts/dev/fixtures/photo.jpg=IMG_0002.jpg" "$OUT_ABS/phone-song.mp3=Song.mp3"
  wait
  phone_check
  keep_crashes
  exit 0
fi

for MODE in light dark; do
  shoot "$MODE-01-dashboard" 14 -SimplActivityProbe YES
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
  shoot "$MODE-12c-hand-in-phone" 18 -SimplPlace course:101 -SimplPush /courses/101/assignments/1012 -SimplOpen handin -SimplPhone YES
  shoot "$MODE-13-discussion" 14 -SimplPlace course:101 -SimplPush /courses/101/discussion_topics/7003
  shoot "$MODE-14-modules" 12 -SimplPlace section:courses/101:modules
  shoot "$MODE-15-files" 12 -SimplPlace section:courses/101:files
  shoot "$MODE-16-groups" 12 -SimplPlace groups
  shoot "$MODE-17-search" 12 -SimplPlace search:dis
  shoot "$MODE-18-quiz" 14 -SimplOpen quiz:101:9011
  shoot "$MODE-19-quiz-question" 18 -SimplOpen quiz:101:9011:take:5
  shoot "$MODE-20-quiz-review" 18 -SimplOpen quiz:101:9011:review
  shoot "$MODE-21-tool" 16 -SimplOpen tool:101:9
  shoot "$MODE-21a-alta" 20 -SimplOpen alta:104:4003
  shoot "$MODE-21c-alta-start" 20 -SimplOpen alta:104:4004
  shoot "$MODE-21f-alta-pins" 20 -SimplOpen alta:104:4003 -SimplPinnedTools "calc,need,ptable"
  shoot "$MODE-21g-alta-float" 22 -SimplOpen alta:104:4003 -SimplPinnedTools "calc,need" -SimplPinFloat calc
  shoot "$MODE-22-setup" 14 -SimplOpen setup
  shoot "$MODE-23-settings" 12 -SimplOpen settings
  shoot "$MODE-24-new-task" 12 -SimplPlace todo -SimplOpen newtask
  shoot "$MODE-25-compose" 12 -SimplPlace inbox -SimplOpen compose
  shoot "$MODE-26-school" 8 -SimplPicker merced
  shoot "$MODE-26b-school-confirm" 18 -SimplPicker catcourses -SimplPickerConfirm YES
  BASE=http://localhost:8802 shoot "$MODE-60-sign-in" 10 # (1.3.73) Simpl's own sign-in card over the school's page
  # (1.2) the Dashboard's other views and a counter grown into its list, a thread beside the Inbox, the tour, the
  # search field's commands
  shoot "$MODE-01b-dashboard-list" 14 -SimplDashView list
  shoot "$MODE-01d-dashboard-counter" 16 -SimplSheet next
  shoot "$MODE-07b-inbox-thread" 18 -SimplPlace inbox -SimplOpen conversation:first
  shoot "$MODE-27-tour" 14 -SimplOpen tour
  shoot "$MODE-30-commands" 14 -SimplOpen palette:/
done
# (1.2) in light only: more of the redesign, and the narrow window's layouts
MODE=light
shoot light-01w-walk 60 -SimplWalk "$WALK"
shoot light-01c-dashboard-activity 14 -SimplDashView activity
shoot light-02b-courses-past 12 -SimplPlace courses -SimplCoursesFilter past
shoot light-19b-quiz-matching 18 -SimplOpen quiz:101:9011:take:4
shoot light-23b-settings-updates 12 -SimplOpen settings -SimplSettingsTab updates
shoot light-28-tour-grades 16 -SimplOpen tour:2
shoot light-29-tour-cards 16 -SimplOpen tour:12
shoot light-31-command-grades 16 -SimplOpen "palette:/grades "
shoot light-32-rubric-ring 16 -SimplPlace course:104 -SimplPush /courses/104/assignments/4001 -SimplOpen rubric:1
shoot light-33-rubric-grid 16 -SimplPlace course:104 -SimplPush /courses/104/assignments/4001 -SimplOpen rubric:grid
shoot light-34-files-preview 14 -SimplPlace section:courses/101:files -SimplOpen file:f1
shoot light-35-tools 12 -SimplPlace tools -SimplPinnedTools "pomo,calc,need,ptable"
shoot light-37-tool-calc 12 -SimplPlace tools -SimplTool calc
shoot light-38-tool-ptable 12 -SimplPlace tools -SimplTool ptable -SimplPTColour category
shoot light-39-tool-need 14 -SimplPlace tools -SimplTool need
SIZE=940x820 shoot light-40-narrow-dashboard 14
SIZE=940x820 shoot light-41-narrow-grades 14 -SimplPlace grades
SIZE=940x820 shoot light-42-narrow-inbox 12 -SimplPlace inbox
SIZE=1720x1040 shoot light-43-wide-dashboard 14
SIZE=1720x1040 shoot light-44-wide-inbox 14 -SimplPlace inbox -SimplOpen conversation:first
# Brightspace: a made-up student's (scripts/dev/mock-brightspace.mjs), in light — no Inbox or Groups in the sidebar
export BASE=http://localhost:8860
MODE=light
shoot d2l-01-dashboard 16 -SimplActivityProbe YES # (and the new-activity check's read of Brightspace, on the console)
shoot d2l-02-course 14 -SimplPlace course:31001
shoot d2l-03-assignments 12 -SimplPlace section:courses/31001:assignments
shoot d2l-04-assignment 14 -SimplPlace course:31001 -SimplPush /courses/31001/assignments/702
shoot d2l-05-grades 14 -SimplPlace section:courses/31001:grades
shoot d2l-06-content 12 -SimplPlace section:courses/31001:modules
shoot d2l-07-discussion 14 -SimplPlace course:31001 -SimplPush /courses/31001/discussion_topics/2000000902
shoot d2l-08-todo 12 -SimplPlace todo
unset BASE
wait # (the rest alone: a running focus timer is kept)
# (1.2) last, since a running focus timer is kept: the timer, and the pinned tools in the toolbar with it live
shoot light-36-tool-timer 12 -SimplPlace tools -SimplTool pomo -SimplFocusDemo YES -SimplPinnedTools "calc,need"
shoot light-45-dashboard-pins 14 -SimplFocusDemo YES -SimplPinnedTools "calc,need,cite"
wait
keep_crashes
exit 0
