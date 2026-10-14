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
  # (What's New comes up once the page is loaded and the app is quiet — 20 s on, with four copies of the app at work: a
  # settled picture caught it on its way in, faint, so its picture waits its whole time)
  case " $* " in *PickerConfirm*|*" tool:"*|*" alta:"*|*" macmillan:"*|*" file:"*|*SimplHandInPreview*|*"SimplOpen whatsnew"*) settle=NO ;; esac
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
# time of each first visit and each visit again. The walks run last, by themselves (the copies picturing the rest slow
# them, and unevenly): the app as it is, and as it loaded before 1.3.78 (-SimplPlain: nothing held or read ahead, the
# Dashboard not kept), side by side, so the two are timed alike — against a Canvas a quarter of a second away, as a
# school's is (its assignment lists 0.6 s: the web's fluid suite's), not the mock's instant answers. Last, the
# assignment opened from the Dashboard — its course's place, made with the assignment on it — and Back: the console
# says "open Assignment" there, not the course's list.
WALK="todo,grades,calendar,notifications,inbox,course:101,externaltools,dashboard,todo,grades,calendar,notifications,inbox,course:101,push:/courses/101/assignments/1001,back,externaltools,dashboard,push:/courses/101/assignments/1001,back"
walks() {
  MODE=light
  curl -s -o /dev/null -X POST -H 'content-type: application/json' -d '{"apiDelay":{"/assignments$":600,"":250}}' http://localhost:8800/__mock/config || true
  shoot light-01w-walk 200 -SimplWalk "$WALK"
  shoot light-01v-walk-plain 200 -SimplWalk "$WALK" -SimplPlain YES
  wait
  curl -s -o /dev/null -X POST -H 'content-type: application/json' -d '{"apiDelay":null}' http://localhost:8800/__mock/config || true
}

# (1.3.78) bio <light|dark|both> <name> <wait> <tool> <state> [flags…]: a biology tool on the Tools page at 1440×920,
# opened as its launch's state asks (-SimplTool <tool> -SimplToolState <page>[:…], docs/mac-biology.md); "both" takes it
# in light and in dark (<mode>-<name>). A picture's launch keeps the tool's settings to itself (BioPrefs), so one
# picture's choices never show in the next.
bio() {
  local modes="$1" name="$2" wait="$3" tool="$4" state="$5" m
  shift 5
  [ "$modes" = both ] && modes="light dark"
  for m in $modes; do
    MODE="$m" SIZE=1440x920 shoot "$m-$name" "$wait" -SimplPlace tools -SimplTool "$tool" -SimplToolState "$state" "$@"
  done
}

# a push's biology pictures: each of the ten tools once, in light
bio_quick() {
  bio light 57-cell-animal 30 cell animal
  bio light 58a-dna-helix 24 dna helix:measure:pair=3
  bio light 59a-gene-transcribe 14 gene express:eukaryote:gene=teaching:step=22
  bio light 61b-biomol-peptide 16 biomol proteins:build:ribbon=over:colour=part
  bio light 62a-cross-dihybrid 14 cross cross -SimplBioCross "AaBb x AaBb"
  bio light 63a-divide-metaphase 14 divide mitosis:step=6
  bio light 64a-popgen-calculate 14 popgen calculate:q2=0.16
  bio light 65a-biolab-solutions 14 biolab solutions
  bio light 66a-enzyme-competitive 14 enzyme competitive
  bio light 67a-energy-map 14 energy map:stage=calvin
  bio_plain_all
}

# (1.3.79) bio_plain <name> <wait> <tool>: a biology tool opened as a student opens it — no launch state, so nothing
# selected and every setting its own — in dark, in a window 1,024 × 683 and in the smallest one (720 × 520), each with
# the sidebar out and folded away (dark-<name>-1024, -1024-folded, -720, -720-folded). The pictures of launch states
# never showed what the owner's 1.3.78 beta did on a plain open: DNA & RNA's helix filling the page with its controls
# and facts out of sight below it, the animal cell one gold tint with a part's name twice, the amino acid's labels off
# their parts.
bio_plain() {
  local name="$1" wait="$2" tool="$3" size
  for size in 1024x683 720x520; do
    MODE=dark SIZE="$size" shoot "dark-$name-${size%%x*}" "$wait" -SimplPlace tools -SimplTool "$tool"
    MODE=dark SIZE="$size" shoot "dark-$name-${size%%x*}-folded" "$wait" -SimplPlace tools -SimplTool "$tool" -SimplSidebar hidden
  done
}

# the plain opens of the three tools the owner's report named: the Cell Explorer (its animal cell), DNA & RNA (its
# double helix) and the Biomolecule Builder (its amino acids, phenylalanine)
bio_plain_all() {
  bio_plain 57z-cell-plain 45 cell
  bio_plain 58z-dna-plain 24 dna
  bio_plain 61z-biomol-plain 40 biomol
}

# every biology tool's pages and what each is for (the full set): 57 Cell Explorer, 58 DNA & RNA, 59 Gene Expression,
# 61 Biomolecule Builder, 62 Genetics Crosses, 63 Cell Division, 64 Hardy–Weinberg, 65 Bio Lab Calculator, 66 Enzyme
# Lab, 67 Photosynthesis & Respiration; then the four pins' quick versions
bio_full() {
  # the three cells, a part's card, inside an organelle, pulled apart, at true scale, the membrane, the three compared
  # (each picture waits for its cell to be built — seconds in a debug build, the plant and the three compared longest —
  # and is taken as soon as it is: the wait is the longest it may take; the plant's and the three compared's were 40
  # and 45, and with four copies of the app at work the plant took 39 s after the app had been up 8, and was pictured
  # before it came)
  bio both 57-cell-animal 30 cell animal
  bio light 57b-cell-mito-card 30 cell animal:select=mito
  bio both 57c-cell-plant 60 cell plant:select=chloroplast
  bio light 57d-cell-bacterium 30 cell bacterium
  bio light 57e-cell-explode 30 cell animal:explode=0.7
  bio light 57f-cell-true-scale 40 cell animal:true
  bio light 57g-cell-mito-inside 30 cell animal:inside=mito
  bio light 57h-cell-nucleus-inside 30 cell animal:inside=nucleus
  bio both 57i-cell-membrane 30 cell membrane:select=integral:channel:osmosis
  bio light 57j-cell-compare 60 cell compare
  bio light 57k-cell-quiz 30 cell animal:quiz=name
  bio light 57l-cell-gram-positive 30 cell bacterium:gram+:select=wall
  # the double helix (measured, its grooves, in atoms, unzipped), the nucleotides, a strand's facts, replication, RNA
  bio both 58a-dna-helix 30 dna helix:measure:pair=3
  bio both 58b-dna-grooves 16 dna helix:grooves
  bio both 58c-dna-atoms 16 dna helix:style=atoms:pair=5
  bio both 58d-dna-unzip 16 dna helix:unzip=0.5
  bio both 58e-dna-nucleotide 16 dna nucleotides:mol=DT:numbers:compare
  bio both 58f-dna-bases 16 dna nucleotides:pairs
  bio both 58g-dna-sequence 16 dna sequence
  bio both 58h-dna-replication 16 dna replication:step=4
  bio both 58i-dna-okazaki 16 dna replication:step=6
  bio both 58j-dna-trna 16 dna rna:view=cloverleaf:modified:base=35
  bio both 58k-dna-trna3d 16 dna rna:view=lshape
  bio both 58l-dna-mrna 16 dna rna:view=mrna
  # a gene made into a protein (transcription, splicing, initiation, elongation, the protein; a prokaryote's), the
  # codon table and wheel, the six reading frames, the sickle-cell mutation and a frameshift
  bio both 59a-gene-transcribe 14 gene express:eukaryote:gene=teaching:step=22
  bio both 59b-gene-splice 14 gene express:eukaryote:gene=teaching:step=87
  bio both 59c-gene-initiation 14 gene express:eukaryote:gene=teaching:step=89
  bio both 59d-gene-translate 14 gene express:eukaryote:gene=teaching:step=93
  bio both 59e-gene-result 14 gene express:eukaryote:gene=teaching:step=99
  bio both 59f-gene-prokaryote 14 gene express:prokaryote:gene=teaching:step=30
  bio both 59g-gene-codons 14 gene codons:find=Leu
  bio both 59h-gene-wheel 14 gene codons:wheel:find=Leu
  bio both 59i-gene-frames 14 gene frames:example=2
  bio both 59j-gene-sickle 14 gene mutations:sickle
  bio both 59k-gene-frameshift 14 gene mutations:mutgene=teaching:mut=c.6_7insA
  # an amino acid and its D form, the peptide builder (atoms with the ribbon, the ribbon alone, a sheet), the four
  # levels of structure and a denatured protein, a DNA strand and ATP, sugars, lipids
  bio light 61a-biomol-amino 16 biomol amino:aa=W:colour=part
  bio light 61b-biomol-peptide 16 biomol proteins:build:ribbon=over:colour=part
  bio light 61c-biomol-ribbon 16 biomol proteins:build:ribbon=only
  bio light 61d-biomol-sheet 16 biomol proteins:sheet
  bio light 61e-biomol-primary 16 biomol proteins:level=primary
  bio light 61f-biomol-secondary 16 biomol proteins:level=secondary
  bio light 61g-biomol-tertiary 16 biomol proteins:level=tertiary:core
  bio dark 61h-biomol-quaternary 16 biomol proteins:level=quaternary
  bio dark 61i-biomol-denatured 18 biomol proteins:level=tertiary:denatured
  bio light 61j-biomol-strand 16 biomol nucleic:strand:seq=ATGCGT:complement:colour=part
  bio light 61k-biomol-atp 16 biomol nucleic:nt=ATP:colour=part
  bio light 61l-biomol-anomers 16 biomol carbs:anomers:colour=part
  bio light 61m-biomol-sucrose 16 biomol carbs:di=SUC
  bio light 61n-biomol-maltose-split 16 biomol carbs:di=MAL:split
  bio light 61o-biomol-cellulose 16 biomol carbs:poly=cellulose
  bio light 61p-biomol-popc 30 biomol lipids:lipid=POV:colour=part
  bio dark 61q-biomol-oleic 16 biomol lipids:lipid=compare
  bio dark 61r-biomol-dform 16 biomol amino:aa=A:dform
  # crosses (a dihybrid, X-linked, the forked line, a lethal, why), probability, chi-square and a pedigree
  bio light 62a-cross-dihybrid 14 cross cross -SimplBioCross "AaBb x AaBb"
  bio dark 62b-cross-xlinked 14 cross "cross:cross=X^A X^a x X^a Y"
  bio light 62c-cross-forked 14 cross cross:forked -SimplBioCross "AaBbCc x AaBbCc"
  bio light 62d-cross-lethal 14 cross cross:example=yellow
  bio light 62e-cross-why 14 cross cross:why -SimplBioCross "AaBb x AaBb"
  bio both 62f-cross-probability 14 cross probability
  bio both 62g-cross-chi 14 cross chi:obs=315,108,101,32 -SimplBioCross "AaBb x AaBb"
  bio light 62h-cross-pedigree 14 cross pedigree:example=xr:mode=xd
  # mitosis and meiosis at their telling steps (a recombinant chromatid's card, the gametes, nondisjunction), a plant
  # cell, 2n = 6, the two compared, the cell cycle's checkpoints and cancer
  bio both 63a-divide-metaphase 14 divide mitosis:step=6
  bio both 63b-divide-tetrads 14 divide meiosis:step=4
  bio both 63c-divide-recombinant 14 divide meiosis:step=6:pick=0
  bio both 63d-divide-gametes 14 divide meiosis:step=11
  bio both 63e-divide-nondisjunction 14 divide meiosis:nd=1:step=11
  bio both 63f-divide-plant 14 divide mitosis:plant:step=8
  bio both 63g-divide-six 14 divide mitosis:2n=6:step=7
  bio both 63h-divide-compare 14 divide compare:step=3
  bio both 63i-divide-cycle 14 divide cycle:gate=g1s
  bio both 63j-divide-cancer 14 divide cycle:gate=spindle:cancer
  # Hardy–Weinberg worked out, tested and simulated (drift, and selection against a recessive lethal)
  bio both 64a-popgen-calculate 14 popgen calculate:q2=0.16
  bio light 64b-popgen-test 14 popgen test:counts=50,30,20
  bio both 64c-popgen-drift 16 popgen simulate:n=10:p0=0.5:seed=7:reps=10:gens=100
  bio dark 64d-popgen-selection 16 popgen simulate:infinite:p0=0.5:preset=recessiveLethal:gens=50
  # the lab calculator's pages (statistics with its steps open), the enzyme's graphs, the energy pathways
  bio both 65a-biolab-solutions 14 biolab solutions
  bio both 65b-biolab-stats 14 biolab stats:steps
  bio both 65c-biolab-cells 14 biolab cells
  bio both 65d-biolab-molecules 14 biolab molecules
  bio both 66a-enzyme-competitive 14 enzyme competitive
  bio both 66b-enzyme-denatured 14 enzyme denatured
  bio both 67a-energy-map 14 energy map:stage=calvin
  bio both 67b-energy-respire 14 energy map:respire
  bio both 67c-energy-respiration 14 energy respiration:step=7
  bio both 67d-energy-photosynthesis 14 energy photosynthesis:step=6
  bio both 67e-energy-atoms 14 energy atoms:trace=carbon:step=4
  # (the pins' quick versions are taken at the end, each alone: bio_pins)
  BIO_PINS=yes
}

# the biology pins' quick versions: a codon looked up, a cross, Hardy–Weinberg, the lab's dilution and molarity — each
# alone, once the rest are done: a pin's popover is there only while its app is in front, and four copies picturing
# their pins at once took the front from each other (three of the four were pictured closed)
bio_pins() {
  [ -n "${BIO_PINS:-}" ] || return 0
  MODE=light shoot light-54b-pin-gene 14 -SimplPinnedTools gene -SimplPinOpen gene -SimplPinCodon UGG
  wait
  MODE=light shoot light-54c-pin-cross 14 -SimplPinnedTools "cross,popgen" -SimplPinOpen cross
  wait
  MODE=light shoot light-54d-pin-popgen 14 -SimplPinnedTools "cross,popgen" -SimplPinOpen popgen
  wait
  MODE=light shoot light-54e-pin-biolab 14 -SimplPinnedTools biolab -SimplPinOpen biolab
  wait
}

# (1.3.78) the UI review's new states, each once (in both sets: what the review's findings changed, seen on every
# push): the Dashboard's cards and the Announcements panel, Search's answer to a sum, the tour's search step and its
# offer; a quiz's access code and its multiple answers, an assignment's rubric wide and narrow; the periodic table's
# tour offered and an element compared, the citation, unit and Grade needed tools, the PDF tools with a file; the
# Calendar's Week and List, What-If, a message with a file, Grades, To Do and Notifications wide; a locked module,
# People and the Future courses; the first-run setup, the sign-in still going, a school's preview that failed, and the
# Settings tabs
ui_review() {
  MODE=light shoot light-01g-dashboard-cards 14 -SimplDashView cards
  MODE=light SIZE=1440x900 shoot light-01h-dashboard-cards-wide 14 -SimplDashView cards
  MODE=light shoot light-01u-counter-unread 14 -SimplSheet unread
  MODE=light shoot light-30b-palette-sum 14 -SimplOpen "palette:12*4"
  MODE=light shoot light-29b-tour-search 16 -SimplOpen tour:5
  MODE=light shoot light-27b-tour-offer 8 -SimplOpen touroffer
  MODE=light shoot light-18b-quiz-code 22 -SimplOpen quiz:102:10019
  MODE=light shoot light-19g-quiz-multiple 24 -SimplOpen quiz:101:9011:take:2
  MODE=light SIZE=1440x900 shoot light-10g-assignment-rubric-wide 20 -SimplPlace course:104 -SimplPush /courses/104/assignments/4001
  MODE=light shoot light-10h-assignment-rubric 20 -SimplPlace course:104 -SimplPush /courses/104/assignments/4001
  MODE=light shoot light-55m-ptable-tour-offer 14 -SimplPlace tools -SimplTool ptable -SimplPTTourOffer YES
  MODE=light SIZE=1440x1200 shoot light-55n-ptable-element-compare 16 -SimplPlace tools -SimplTool ptable -SimplPTElement 26
  MODE=light shoot light-56-tool-cite 12 -SimplPlace tools -SimplTool cite
  MODE=light shoot light-57-tool-units 12 -SimplPlace tools -SimplTool units
  MODE=light shoot light-54e-pin-need 14 -SimplPinnedTools need -SimplPinOpen need
  # (a two-page reading, for the annotator and Merge & Split: one PDF opens Split)
  MODE=light shoot light-58-tool-annotate 14 -SimplPlace tools -SimplTool mark -SimplToolFile "$PWD/scripts/dev/fixtures/reading.pdf"
  MODE=light shoot light-58b-tool-split 14 -SimplPlace tools -SimplTool pdfx -SimplToolFile "$PWD/scripts/dev/fixtures/reading.pdf"
  MODE=light shoot light-05w-calendar-week 12 -SimplPlace calendar -SimplCalView week
  MODE=light shoot light-05l-calendar-list 12 -SimplPlace calendar -SimplCalView list
  MODE=light shoot light-11w-course-grades-whatif 16 -SimplPlace section:courses/101:grades -SimplWhatIf YES
  MODE=light shoot light-25b-compose-attached 16 -SimplPlace inbox -SimplOpen compose -SimplAttach "$PWD/docs/screenshots/calendar.png"
  MODE=light SIZE=1720x1040 shoot light-45-wide-grades 14 -SimplPlace grades
  MODE=light SIZE=1440x900 shoot light-46-wide-todo 12 -SimplPlace todo
  MODE=light SIZE=1440x900 shoot light-47-wide-notifications 12 -SimplPlace notifications
  MODE=light shoot light-14b-modules-locked 12 -SimplPlace section:courses/102:modules
  MODE=light shoot light-14c-people 12 -SimplPlace section:courses/101:people
  MODE=light shoot light-02c-courses-future 12 -SimplPlace courses -SimplCoursesFilter future
  MODE=light shoot light-22b-setup-first 20 -SimplOpen setup -SimplSetupFirstRun YES
  MODE=light BASE=http://localhost:8802 shoot light-60b-logging-in 6 -SimplShowLoggingIn YES
  MODE=dark shoot dark-26c-school-confirm-failed 10 -SimplPicker catcourses -SimplPickerConfirm YES -SimplPreviewFail YES
  MODE=light shoot light-23c-settings-grades 12 -SimplOpen settings -SimplSettingsTab grades
  MODE=light shoot light-23d-settings-data 12 -SimplOpen settings -SimplSettingsTab data
  MODE=light shoot light-23e-settings-about 12 -SimplOpen settings -SimplSettingsTab about
  MODE=light shoot light-23f-settings-notifications 12 -SimplOpen settings -SimplSettingsTab notifications
}

# (1.3.79) in both sets: five pins in the toolbar at 1024 points and narrow (every pin that fits shown, the rest in the
# Tools pull-down: ToolbarFit) and on a popup's bar; the focus timer's panel pulled out, over the window in dark; and an
# external tool's page light in Dark Mode — a tool window, an Alta and a Macmillan assignment
pins_pages() {
  MODE=light SIZE=1024x700 shoot light-45c-toolbar-five-pins 14 -SimplPinnedTools "calc,need,cite,graph,units"
  MODE=light SIZE=780x700 shoot light-52b-narrow-five-pins 14 -SimplPinnedTools "calc,need,cite,graph,units"
  MODE=light shoot light-21k-alta-five-pins 20 -SimplOpen alta:104:4003 -SimplPinnedTools "calc,need,ptable,cite,units"
  MODE=dark shoot dark-54g-pin-float-timer 16 -SimplPinnedTools "pomo,calc" -SimplPinFloat pomo
  MODE=dark shoot dark-21h-tool-light-page 24 -SimplOpen tool:101:9
  MODE=dark shoot dark-21i-alta-light-page 20 -SimplOpen alta:104:4003
  MODE=dark shoot dark-21j-macmillan-light-page 26 -SimplOpen macmillan:104:4005
}

# (1.3.79) a tool opened whole is a popup over the window, never in its place (Shell/FloatingPopup.swift): the Periodic
# Table, Cell Explorer and Flashcards, each over the Tools page, in light and in dark, at 1024 points wide and at the
# narrow size (the sidebar folded away)
tool_popups() {
  local m
  for m in light dark; do
    MODE="$m" SIZE=1024x768 shoot "$m-35p-popup-ptable" 14 -SimplPlace tools -SimplTool ptable -SimplPTColour category
    MODE="$m" SIZE=780x700 shoot "$m-35p-popup-ptable-narrow" 14 -SimplPlace tools -SimplTool ptable -SimplPTColour category
    MODE="$m" SIZE=1024x768 shoot "$m-35q-popup-cell" 45 -SimplPlace tools -SimplTool cell -SimplToolState animal
    MODE="$m" SIZE=780x700 shoot "$m-35q-popup-cell-narrow" 45 -SimplPlace tools -SimplTool cell -SimplToolState animal
    MODE="$m" SIZE=1024x768 shoot "$m-35r-popup-flashcards" 12 -SimplPlace tools -SimplTool fc
    MODE="$m" SIZE=780x700 shoot "$m-35r-popup-flashcards-narrow" 12 -SimplPlace tools -SimplTool fc
  done
}

# (1.3.78) a discussion's post that never measured in its web view says so on the console (Components/RichText.swift,
# in a picture's run only: its words are shown natively meanwhile); said here as a warning of the run
richtext_check() {
  local hits
  hits="$(grep -l '\[richtext\] not measured' "$OUT_ABS"/console-*.txt 2>/dev/null | xargs -n1 basename 2>/dev/null | tr '\n' ' ')"
  if [ -n "$hits" ]; then echo "::warning::a post's web view never measured (its words shown as plain text): $hits"; fi
}

# (1.2.1) SHOTS=quick, a push's set: the main screens once each, a few minutes — every screen in light and dark is
# SHOTS=full (the workflow's own choice, or a commit's message saying [full shots])
if [ "${SHOTS:-full}" = quick ]; then
  MODE=light
  shoot light-01-dashboard 12
  shoot light-01d-dashboard-counter 14 -SimplSheet next
  shoot light-01r-counter-middle 14 -SimplSheet tomorrow # (1.3.16: a middle tile's panel centred on it)
  # (1.3.78) the row of six: the first tile's panel from its left edge, the last one's from its right; and the row with
  # the sidebar folded away, each symbol beside its name
  shoot light-01e-counter-first 14 -SimplSheet today
  shoot light-01f-counter-last 14 -SimplSheet graded
  shoot light-01s-dashboard-no-sidebar 12 -SimplSidebar hidden
  # (1.3.79) a window under 1024 points with the sidebar out: 1.3.77's tiles in two rows of three
  SIZE=940x820 shoot light-40-narrow-dashboard 14
  shoot light-21a-alta 20 -SimplOpen alta:104:4003 # (1.3.17: a Knewton Alta assignment in the quiz-style popup)
  shoot light-21c-alta-start 20 -SimplOpen alta:104:4004 # (1.3.59: an assignment's cover as the popup's start screen)
  shoot light-21f-alta-pins 20 -SimplOpen alta:104:4003 -SimplPinnedTools "calc,need,ptable" # (1.3.73: the toolbar's pins on the popup's own bar)
  shoot light-21g-alta-float 22 -SimplOpen alta:104:4003 -SimplPinnedTools "calc,need" -SimplPinFloat calc # (1.3.73: a quick panel pulled out, floating over the popup)
  shoot light-21d-macmillan 26 -SimplOpen macmillan:104:4005 # (1.3.66: a Macmillan Learning assignment's popup)
  shoot light-21e-second-window 18 -SimplOpen window:section:courses/101:grades # (1.3.69: a window more, on a course's grades: its breakdown bar)
  shoot light-11-course-grades 14 -SimplPlace section:courses/101:grades # (1.3.69: the breakdown bar in the main window)
  shoot light-36-external-tools 16 -SimplPlace externaltools # (1.3.67: every external tool in the courses)
  shoot light-36b-external-tools-list 12 -SimplPlace externaltools -SimplExternalToolList knewton # (1.3.77: every place Knewton Alta is used, to choose from)
  shoot light-36c-external-tools-macmillan 12 -SimplPlace externaltools -SimplExternalToolList macmillan # (1.3.79: CHEM's week-titled Macmillan assignments and module item, one tool)
  shoot light-37-developer 16 -SimplOpen developer # (1.3.72: the Developer window)
  shoot light-01p-dashboard-preview 16 -SimplOpen preview
  shoot light-01q-counter-preview 30 -SimplSheet today -SimplOpen preview:counter # (1.3.79: a row of the panel's own)
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
  shoot light-19f-quiz-feedback 24 -SimplOpen quiz:101:9001:feedback
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
  # (1.3.78) the main screens in both looks, so a change to how screens load and move is seen to leave them as they were
  shoot light-05-calendar 12 -SimplPlace calendar
  shoot light-08-course 14 -SimplPlace course:101
  shoot light-12-hand-in 16 -SimplPlace course:101 -SimplPush /courses/101/assignments/1012 -SimplOpen handin
  shoot light-12b-hand-in-preview 30 -SimplPlace course:101 -SimplPush /courses/101/assignments/1012 -SimplOpen handin -SimplHandInPreview "$PWD/docs/screenshots/calendar.png"
  # (1.3.78) Send from Phone: its panel with the code; the phone connected and two files in (posted by phone_send); a
  # PDF-only assignment's photos made into one PDF, and a kind it does not take refused
  shoot light-12c-hand-in-phone 30 -SimplPlace course:101 -SimplPush /courses/101/assignments/1012 -SimplOpen handin -SimplPhone YES
  printf 'Notes from class, sent from a phone.\n' > "$HOMES/phone-notes.txt"
  printf 'ID3 not really music\n' > "$HOMES/phone-song.mp3"
  shoot light-12d-hand-in-phone-connected 40 -SimplPlace course:101 -SimplPush /courses/101/assignments/1012 -SimplOpen handin -SimplPhone YES -SimplPhoneTest YES
  phone_send light-12d-hand-in-phone-connected "$PWD/scripts/dev/fixtures/photo.jpg=Photo from phone.jpg" "$HOMES/phone-notes.txt=Notes.txt"
  curl -s -o /dev/null -X POST -H 'content-type: application/json' -d '{"ext":{"1013":["pdf"]}}' http://localhost:8800/__mock/config || true
  shoot light-22-setup 20 -SimplOpen setup
  shoot light-53-whats-new 32 -SimplOpen whatsnew
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
  # (1.3.79) a counter's panel, and the card of one of its rows over the window, in dark
  shoot dark-01d-dashboard-counter 16 -SimplSheet next
  shoot dark-01q-counter-preview 30 -SimplSheet today -SimplOpen preview:counter
  shoot dark-03-todo 12 -SimplPlace todo
  shoot dark-04-grades 12 -SimplPlace grades
  shoot dark-05-calendar 12 -SimplPlace calendar
  shoot dark-08-course 14 -SimplPlace course:101
  shoot dark-36-external-tools 16 -SimplPlace externaltools
  shoot dark-36b-external-tools-list 12 -SimplPlace externaltools -SimplExternalToolList knewton # (1.3.77)
  shoot dark-32b-rubric-open 16 -SimplPlace course:104 -SimplPush /courses/104/assignments/4001 -SimplOpen rubric:1
  BASE=http://localhost:8802 shoot dark-60-sign-in 10
  shoot dark-12c-hand-in-phone 30 -SimplPlace course:101 -SimplPush /courses/101/assignments/1012 -SimplOpen handin -SimplPhone YES
  shoot dark-12e-hand-in-phone-photos 40 -SimplPlace course:101 -SimplPush /courses/101/assignments/1013 -SimplOpen handin -SimplPhone YES -SimplPhoneTest YES
  # (1.3.78) the Tools page's Biology section, with the toolkit's self-check on its console; each biology tool once, on
  # the page its launch asked for (-SimplTool <key> -SimplToolState <page>[:…]) — every page of each is in the full set
  MODE=light SIZE=1280x900 shoot light-35b-tools-biology 12 -SimplPlace tools -SimplToolsScroll biology -SimplBioCheck YES
  phone_send dark-12e-hand-in-phone-photos "$PWD/scripts/dev/fixtures/photo.jpg=IMG_0001.jpg" "$PWD/scripts/dev/fixtures/photo.jpg=IMG_0002.jpg" "$HOMES/phone-song.mp3=Song.mp3"
  # (1.3.78) the PDF-only assignment waiting for the phone: Combine photos under the code before anything is sent
  shoot dark-12f-hand-in-phone-photos-waiting 30 -SimplPlace course:101 -SimplPush /courses/101/assignments/1013 -SimplOpen handin -SimplPhone YES
  ui_review
  pins_pages # (1.3.79)
  tool_popups # (1.3.79)
  bio_quick # (after the phone's files are on their way: a shot waits for a free worker, and the phone's app must not)
  wait
  walks
  phone_check
  richtext_check
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
  shoot "$MODE-12c-hand-in-phone" 30 -SimplPlace course:101 -SimplPush /courses/101/assignments/1012 -SimplOpen handin -SimplPhone YES
  shoot "$MODE-13-discussion" 14 -SimplPlace course:101 -SimplPush /courses/101/discussion_topics/7003
  shoot "$MODE-14-modules" 12 -SimplPlace section:courses/101:modules
  shoot "$MODE-15-files" 12 -SimplPlace section:courses/101:files
  shoot "$MODE-16-groups" 12 -SimplPlace groups
  shoot "$MODE-17-search" 12 -SimplPlace search:dis
  shoot "$MODE-18-quiz" 14 -SimplOpen quiz:101:9011
  shoot "$MODE-19-quiz-question" 18 -SimplOpen quiz:101:9011:take:5
  shoot "$MODE-20-quiz-review" 18 -SimplOpen quiz:101:9011:review
  shoot "$MODE-21-tool" 24 -SimplOpen tool:101:9
  shoot "$MODE-21a-alta" 20 -SimplOpen alta:104:4003
  shoot "$MODE-21c-alta-start" 20 -SimplOpen alta:104:4004
  shoot "$MODE-21f-alta-pins" 20 -SimplOpen alta:104:4003 -SimplPinnedTools "calc,need,ptable"
  shoot "$MODE-21g-alta-float" 22 -SimplOpen alta:104:4003 -SimplPinnedTools "calc,need" -SimplPinFloat calc
  shoot "$MODE-22-setup" 20 -SimplOpen setup
  shoot "$MODE-23-settings" 18 -SimplOpen settings
  shoot "$MODE-24-new-task" 12 -SimplPlace todo -SimplOpen newtask
  shoot "$MODE-25-compose" 12 -SimplPlace inbox -SimplOpen compose
  shoot "$MODE-26-school" 8 -SimplPicker merced
  shoot "$MODE-26b-school-confirm" 18 -SimplPicker catcourses -SimplPickerConfirm YES
  BASE=http://localhost:8802 shoot "$MODE-60-sign-in" 10 # (1.3.73) Simpl's own sign-in card over the school's page
  # (1.2) the Dashboard's other views and a counter grown into its list, a thread beside the Inbox, the tour, the
  # search field's commands
  shoot "$MODE-01b-dashboard-list" 14 -SimplDashView list
  shoot "$MODE-01d-dashboard-counter" 16 -SimplSheet next
  shoot "$MODE-01q-counter-preview" 30 -SimplSheet today -SimplOpen preview:counter # (1.3.79: a panel row's card)
  shoot "$MODE-07b-inbox-thread" 18 -SimplPlace inbox -SimplOpen conversation:first
  shoot "$MODE-27-tour" 14 -SimplOpen tour
  shoot "$MODE-30-commands" 14 -SimplOpen palette:/
done
bio_full # (1.3.78) the biology toolkit, every tool's pages
bio_plain_all # (1.3.79) three of them opened plainly, in dark, at two window sizes, the sidebar out and folded
ui_review # (1.3.78) the UI review's new states
pins_pages # (1.3.79) the pins' room, a panel pulled out, external tools' pages light
tool_popups # (1.3.79) a tool's popup over the Tools page
# (1.3.78) a PDF-only assignment (as the push's set makes 1013) waiting for the phone: Combine photos under the code
curl -s -o /dev/null -X POST -H 'content-type: application/json' -d '{"ext":{"1013":["pdf"]}}' http://localhost:8800/__mock/config || true
MODE=dark shoot dark-12f-hand-in-phone-photos-waiting 30 -SimplPlace course:101 -SimplPush /courses/101/assignments/1013 -SimplOpen handin -SimplPhone YES
# (1.2) in light only: more of the redesign, and the narrow window's layouts
MODE=light
shoot light-01c-dashboard-activity 14 -SimplDashView activity
shoot light-02b-courses-past 12 -SimplPlace courses -SimplCoursesFilter past
shoot light-19b-quiz-matching 18 -SimplOpen quiz:101:9011:take:4
shoot light-23b-settings-updates 12 -SimplOpen settings -SimplSettingsTab updates
shoot light-28-tour-grades 16 -SimplOpen tour:2
shoot light-29-tour-last 16 -SimplOpen tour:6 # (1.3.78: the tour's last step, Report a Bug at the account menu)
shoot light-31-command-grades 16 -SimplOpen "palette:/grades "
shoot light-32-rubric-ring 16 -SimplPlace course:104 -SimplPush /courses/104/assignments/4001 -SimplOpen rubric:1
shoot light-33-rubric-grid 16 -SimplPlace course:104 -SimplPush /courses/104/assignments/4001 -SimplOpen rubric:grid
shoot light-34-files-preview 14 -SimplPlace section:courses/101:files -SimplOpen file:f1
shoot light-35-tools 12 -SimplPlace tools -SimplPinnedTools "pomo,calc,need,ptable"
shoot light-37-tool-calc 12 -SimplPlace tools -SimplTool calc
shoot light-38-tool-ptable 12 -SimplPlace tools -SimplTool ptable -SimplPTColour category
shoot light-39-tool-need 14 -SimplPlace tools -SimplTool need
SIZE=1280x900 shoot light-35b-tools-biology 12 -SimplPlace tools -SimplToolsScroll biology -SimplBioCheck YES # (1.3.78)
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
walks
# (1.2) last, since a running focus timer is kept: the timer, and the pinned tools in the toolbar with it live
shoot light-36-tool-timer 12 -SimplPlace tools -SimplTool pomo -SimplFocusDemo YES -SimplPinnedTools "calc,need"
shoot light-45-dashboard-pins 14 -SimplFocusDemo YES -SimplPinnedTools "calc,need,cite"
shoot light-54f-pin-timer 14 -SimplFocusDemo YES -SimplPinnedTools pomo -SimplPinOpen pomo # (1.3.78: the timer's quick version)
wait
bio_pins
richtext_check
keep_crashes
exit 0
