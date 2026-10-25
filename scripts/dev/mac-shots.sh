#!/usr/bin/env bash
# Simpl for Mac's screens against the mock Canvas: the Dashboard, each place in the sidebar, a course and its
# sections, an assignment, Hand In, a discussion, the Inbox, a quiz, a tool's window, the setup and Settings — in
# light and in dark; then against the mock Brightspace (2.99.22), its own: the Dashboard, a course, its work, an
# assignment, its grades, its content, a discussion. Run by .github/workflows/mac.yml once the app is built and the
# mocks are serving on :8800 (node scripts/dev/mock-canvas.mjs 8800 8801) and :8860 (mock-brightspace.mjs 8860 --open).
# Pictures in ./shots: <name>-app.png, taken from inside the app of its own windows (Shot.swift).
# (1.3.80) Every run takes the whole set — what a push took and what "full" took, every screen in light and dark
# (SHOTS=quick is the same set now) — shared out between the workflow's runners: SHARDS=4 SHARD=<0…3> takes one part
# of it (share_out, below), and SHOTS=check reads what the parts' apps said once they are put together (phone_check,
# live_check). With neither, one Mac takes every picture, as before. SHOTS=plan launches nothing: it shares the set
# out and writes shot-plan.txt (each picture, its part and the time it is counted at), on any machine.
set -u
OUT="${OUT:-shots}"
APP="${APP:-build/Build/Products/Debug/Simpl.app}"
BIN="$APP/Contents/MacOS/Simpl"
mkdir -p "$OUT"
OUT_ABS="$(cd "$OUT" && pwd)"
SHARDS="${SHARDS:-1}"
SHARD="${SHARD:-0}"
# (1.3.80) yes while the list (the_set) is walked only to share it out: nothing is launched or changed then
PLANNING=""

# (1.3.1) Several copies of the app at once (JOBS, 5 by default), each with a home of its own (CFFIXED_USER_HOME: its
# settings, its caches and its web storage apart from the others'), each picture taken from inside its app of its own
# windows only (Shot.swift) as soon as the app has settled — no answers asked for in a moment — the <wait> being now
# only the longest it waits. A picture of a web page loading (a school's sign-in, a tool's window, Quick Look) waits
# the whole <wait> (-SimplShotSettle NO).
JOBS="${JOBS:-5}"
HOMES=""

# a free worker's number (its lock taken), waiting for one while all are busy (a lock looked for before it is tried:
# a busy worker costs no process started every fifth of a second)
take_slot() {
  local i
  while true; do
    for ((i = 1; i <= JOBS; i++)); do
      if [ ! -d "$HOMES/lock$i" ] && mkdir "$HOMES/lock$i" 2>/dev/null; then echo "$i"; return; fi
    done
    sleep 0.2
  done
}

# (1.3.79) The pictures of a 3D model (HEAVY_SHOT=yes: a cell, a molecule, the double helix) at most HEAVY at once: five
# copies of the app each making and drawing a cell took the runner's every core, and a copy's picture, asked for at its
# <wait>, came 20 to 30 seconds late — two never came before the suite gave up on them. A heavy picture's turn is taken
# before its worker's.
HEAVY="${HEAVY:-3}"
take_heavy() {
  local i
  while true; do
    for ((i = 1; i <= HEAVY; i++)); do
      if [ ! -d "$HOMES/heavy$i" ] && mkdir "$HOMES/heavy$i" 2>/dev/null; then echo "$i"; return; fi
    done
    sleep 0.2
  done
}

# (1.3.80) share_out: which part takes each picture, the same in every part (each walks the whole list first, the same
# way, before it launches anything: the_set, PLANNING): the part with the least time on it so far, by what it took in a
# run (TIMES_FILE, a run's shot-times.txt — scripts/dev/baselines/mac-shot-times.txt; a new picture not in it counts its
# <wait>), so the runners end together rather than each taking an equal count of the pictures. A 3D model's picture
# (HEAVY_SHOT) goes to the part with the least time of those, at most HEAVY of which are taken at once. The groups
# taken alone at the end are placed first (alone), each charged its whole time for every worker it keeps idle. Every
# picture and its part go in shot-plan.txt — the same file from every part, which the workflow checks, and holds the
# gathered pictures to; this part's own pictures marked mine (M_<name>). The plan depends on nothing but the files
# checked out and SHARDS, so a part run again (a failed job re-run) takes the same pictures as the first time.
# (Each picture's time, each name planned and each of this part's is a variable of its own — T_, P_ and M_ with the
# name's every other character made "_" — looked up at once: searched for in one long string, the list took 12 seconds.)
TIMES_FILE="${TIMES_FILE:-scripts/dev/baselines/mac-shot-times.txt}"
if [ -f "$TIMES_FILE" ]; then
  eval "$(awk '!/^#/ && NF >= 2 { n = $1; gsub(/[^A-Za-z0-9]/, "_", n); t = $2; sub(/s$/, "", t); if (t ~ /^[0-9]+$/) printf "T_%s=%d\n", n, t }' "$TIMES_FILE")"
fi
LOADS=()
HEAVIES=()
COUNTS=()
SHOT_PID=""
for ((k = 0; k < SHARDS; k++)); do LOADS[k]=0; HEAVIES[k]=0; COUNTS[k]=0; done

# time_of <name> <wait>: what the picture took in the run TIMES_FILE is from, else its <wait>, in COST (seconds)
time_of() {
  local v="T_${1//[!A-Za-z0-9]/_}"
  COST="${!v:-$2}"
}

# alone <variable> <together|one-by-one> <name>…: a group taken alone at the end of its part — its pictures one after
# another, or at once (the longest of them) — placed on the part with the least time on it so far (from the last part
# down: part 0 also keeps the build and sends the app), charged for each of the workers it keeps idle (PLAN_JOBS, the
# parts' JOBS: the same in every part, so every part shares the pictures out alike); its part in <variable>
PLAN_JOBS="${PLAN_JOBS:-$JOBS}"
alone() {
  local var="$1" how="$2" n total=0 part=$((SHARDS - 1)) k
  shift 2
  for n in "$@"; do
    time_of "$n" 25
    if [ "$how" = together ]; then [ "$COST" -gt "$total" ] && total=$COST; else total=$((total + COST)); fi
  done
  for ((k = SHARDS - 2; k >= 0; k--)); do [ "${LOADS[k]}" -lt "${LOADS[part]}" ] && part=$k; done
  LOADS[part]=$(( ${LOADS[part]} + total * PLAN_JOBS ))
  printf -v "$var" '%s' "$part"
}

# share_out <name> <wait>: the picture given its part, in shot-plan.txt (PART=<k> shoot …: a picture of a group taken
# alone, on its group's part, already charged). A name in the list twice would be two launches writing one picture:
# said as an error, and the workflow fails on it.
share_out() {
  local name="$1" part k key="${1//[!A-Za-z0-9]/_}" seen
  case "$name" in warmup*) return 0 ;; esac
  seen="P_$key"
  if [ -n "${!seen:-}" ]; then echo "::error::mac-shots.sh: $name is in the list twice"; fi
  printf -v "$seen" '%s' 1
  time_of "$name" "$2"
  if [ -n "${PART:-}" ]; then
    part="$PART"
  elif [ -n "${HEAVY_SHOT:-}" ]; then
    part=0
    for ((k = 1; k < SHARDS; k++)); do
      if [ "${HEAVIES[k]}" -lt "${HEAVIES[part]}" ] || { [ "${HEAVIES[k]}" -eq "${HEAVIES[part]}" ] && [ "${LOADS[k]}" -lt "${LOADS[part]}" ]; }; then part=$k; fi
    done
    HEAVIES[part]=$(( ${HEAVIES[part]} + COST ))
    LOADS[part]=$(( ${LOADS[part]} + COST ))
  else
    part=0
    for ((k = 1; k < SHARDS; k++)); do [ "${LOADS[k]}" -lt "${LOADS[part]}" ] && part=$k; done
    LOADS[part]=$(( ${LOADS[part]} + COST ))
  fi
  COUNTS[part]=$(( ${COUNTS[part]} + 1 ))
  echo "$name part$part ${COST}s" >> "$OUT_ABS/shot-plan.txt"
  if [ "$part" = "$SHARD" ]; then printf -v "M_$key" '%s' 1; fi
}

# mine <name>: whether this part takes the picture <name> (warm-ups: every part)
mine() {
  case "$1" in warmup*) return 0 ;; esac
  local m="M_${1//[!A-Za-z0-9]/_}"
  [ -n "${!m:-}" ]
}

# shoot <name> <wait> [args…]: the app afresh with the mock as its Canvas (as a student who has used it: -SimplDemo),
# pictured once it has settled, <wait> seconds at most (SIZE=900x820 shoot …: a window of another size). It runs in the
# background on a free worker; `wait` at the end gathers them (SHOT_PID: this one's, when this part takes it). (1.3.79)
# A picture not taken 15 seconds after its <wait> has the app's threads sampled (sample-<name>.txt: what kept it), and
# is waited for 45 seconds past its <wait> in all. (1.3.80) By the clock now — the times in shot-times.txt are the
# clock's too — and the counted quarter-second naps those were ran nearly twice as long on a busy runner, so as long as
# they gave in fact: sampled 30 seconds past its <wait>, given up on 90 seconds past it (a picture under load came 20
# to 30 seconds late, 1.3.79; one later than 45 was taken then, and is now). While the list is walked to share it out
# (PLANNING), the picture is only given its part; then each part takes its own (mine).
shoot() {
  local name="$1" wait="$2"
  shift 2
  SHOT_PID=""
  if [ -n "$PLANNING" ]; then
    share_out "$name" "$wait"
    return 0
  fi
  mine "$name" || return 0
  local heavy="" slot
  [ -n "${HEAVY_SHOT:-}" ] && heavy="$(take_heavy)"
  slot="$(take_slot)"
  local settle=YES
  # (What's New comes up once the page is loaded and the app is quiet — 20 s on, with four copies of the app at work: a
  # settled picture caught it on its way in, faint, so its picture waits its whole time)
  # ((1.3.83) Desmos in a pin's panel, and a pin's Open Full Screen pressed a few seconds in: their whole time too)
  case " $* " in *PickerConfirm*|*" tool:"*|*" more:tool "*|*" alta:"*|*" macmillan:"*|*" file:"*|*" modulefile:"*|*SimplHandInPreview*|*"SimplOpen whatsnew"*|*SimplPinGraph*|*SimplPinFull*) settle=NO ;; esac
  (
    rm -f "$OUT_ABS/$name.rect" "$OUT_ABS/$name.png" "$OUT_ABS/$name-app.png"
    local start=$SECONDS took=0 sampled=""
    CFFIXED_USER_HOME="$HOMES/home$slot" "$BIN" -SimplBaseURL "${BASE:-http://localhost:8800}" -SimplDemo YES -SimplAppearance "$MODE" \
      -SimplShotFile "$OUT_ABS/$name.png" -SimplShotAfter "$wait" -SimplShotSettle "$settle" -SimplWindowSize "${SIZE:-1280x820}" \
      -NSQuitAlwaysKeepsWindows NO "$@" > "$OUT_ABS/console-$name.txt" 2>&1 &
    local pid=$!
    while [ ! -f "$OUT_ABS/$name.rect" ] && [ $took -lt $((wait + 90)) ] && kill -0 "$pid" 2>/dev/null; do
      if [ -z "$sampled" ] && [ $took -ge $((wait + 30)) ]; then
        sampled=yes
        sample "$pid" 3 -file "$OUT_ABS/sample-$name.txt" >/dev/null 2>&1 &
      fi
      sleep 0.25
      took=$((SECONDS - start))
    done
    if [ -f "$OUT_ABS/$name-app.png" ]; then echo "shot $name (${took}s)"; else echo "no picture $name (the app did not get there)"; fi
    echo "$name ${took}s worker$slot part$SHARD at ${start}s" >> "$OUT_ABS/shot-times.txt"
    # (its worker free once the app has gone — at the most 0.3 seconds after it is asked to, then it is stopped)
    kill "$pid" >/dev/null 2>&1 || true
    for _ in 1 2 3 4 5 6; do kill -0 "$pid" 2>/dev/null || break; sleep 0.05; done
    kill -9 "$pid" >/dev/null 2>&1 || true
    # (a console with nothing worth keeping is dropped)
    [ -s "$OUT_ABS/console-$name.txt" ] || rm -f "$OUT_ABS/console-$name.txt"
    rmdir "$HOMES/lock$slot"
    if [ -n "$heavy" ]; then rmdir "$HOMES/heavy$heavy"; fi
  ) &
  SHOT_PID=$!
}

# wait_shot: the last picture asked for waited for, when this part took it (`wait "$!"` would wait for another, or for
# nothing at all)
wait_shot() {
  if [ -n "$SHOT_PID" ]; then wait "$SHOT_PID"; fi
}

# console_says <console> <words> <tenths>: whether an app has said <words> on its console, looked for every tenth of a
# second, <tenths> times at most
console_says() {
  local i
  for ((i = 0; i < $3; i++)); do
    grep -qF -- "$2" "$1" 2>/dev/null && return 0
    sleep 0.1
  done
  return 1
}

# mock_config <json> [port]: the mock school (:8800, or <port>) told to answer otherwise, from here on — except while
# the list is only being shared out
mock_config() {
  [ -z "$PLANNING" ] || return 0
  curl -s -o /dev/null -X POST -H 'content-type: application/json' -d "$1" "http://localhost:${2:-8800}/__mock/config" || true
}
# (the mock schools name the website's made-up one — its courses, work and classmates: scripts/dev/site-names.mjs — so
#  any picture can go on simplcourses.com)
mock_config '{"siteNames":true}'
mock_config '{"siteNames":true}' 8802

# (1.3.78) Send from Phone, tried as a phone would: phone_send <name> <file>… waits for the picture <name>'s app to say
# its server's address on the console (-SimplPhoneTest), opens the page there, asks for a wrong key's, then posts each
# file as the phone's page does (X-File-Name, its type, its length); what it got back goes in phone-test.txt. The app
# holds its picture until a file has come, so the picture shows them arriving; phone_check reads the consoles after.
# (In the part that takes the picture <name>.) (1.3.80) Each next step as soon as the app has said it is done with
# the one before — the page seen opened, a file received — and never later than the fixed second (and 0.6 seconds)
# it was given before.
phone_send() {
  local name="$1"
  shift
  [ -z "$PLANNING" ] && mine "$name" || return 0
  (
    local console="$OUT_ABS/console-$name.txt" url="" start=$SECONDS codes="" code
    while [ -z "$url" ] && [ $((SECONDS - start)) -lt 90 ]; do
      url="$(grep -o 'SimplPhone ready http://[^ ]*' "$console" 2>/dev/null | head -1 | cut -d' ' -f3)"
      if [ -z "$url" ]; then sleep 0.25; fi
    done
    if [ -z "$url" ]; then echo "$name: no address on the console" >> "$OUT_ABS/phone-test.txt"; exit 0; fi
    local base="${url%/*/}"
    local page wrong
    page="$(curl -s -m 10 -o "$OUT_ABS/phone-page-$name.html" -w '%{http_code}' "$url")"
    wrong="$(curl -s -m 10 -o /dev/null -w '%{http_code}' "$base/AAAAAAAAAAAAAAAAAAAAAA/")"
    console_says "$console" "SimplPhone opened" 10 || true
    for f in "$@"; do
      local as="${f##*=}" path="${f%%=*}"
      code="$(curl -s -m 30 -o /dev/null -w '%{http_code}' -X POST -H 'Expect:' -H "X-File-Name: ${as// /%20}" \
        -H "Content-Type: $(file -b --mime-type "$path")" --data-binary @"$path" "${url}upload")"
      codes="$codes $code"
      # (a file refused says nothing more)
      if [ "$code" = 200 ]; then console_says "$console" "SimplPhone received $as " 6 || true; fi
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
  ok "the page has the assignment's name in it" "grep -q 'Composing functions' '$OUT_ABS/phone-page-light-12d-hand-in-phone-connected.html'"
  ok "the phone was seen to connect" "grep -q 'SimplPhone opened' '$OUT_ABS/console-light-12d-hand-in-phone-connected.txt'"
  ok "the photo is in the hand-in" "grep -q 'SimplHandIn added Photo from phone.jpg' '$OUT_ABS/console-light-12d-hand-in-phone-connected.txt'"
  ok "the notes are in the hand-in" "grep -q 'SimplHandIn added Notes.txt' '$OUT_ABS/console-light-12d-hand-in-phone-connected.txt'"
  ok "a PDF-only assignment's photos were taken and an mp3 refused (415): $(grep 12e "$t" 2>/dev/null)" "grep -q '12e-hand-in-phone-photos: page 200, wrong key 404, uploads 200 200 415' '$t'"
  ok "its two photos became one PDF in the hand-in" "[ \"\$(grep -c 'SimplHandIn added Group project.pdf (1 files)' '$OUT_ABS/console-dark-12e-hand-in-phone-photos.txt')\" -ge 2 ]"
  ok "(1.3.146) four files added one after another, each previewed: all four in, the app still up" "grep -q 'SimplHandIn added file 4 of 4: 4 in' '$OUT_ABS/console-light-12f-hand-in-one-by-one.txt'"
  cat "$out"
}

# a first launch in each worker's home to warm WebKit and the mock (the first start is slow), not pictured. (1.3.80)
# Not waited for together: a worker's first picture starts as soon as its own warm-up is done.
warm_up() {
  for i in $(seq 1 "$JOBS"); do MODE=light shoot "warmup$i" 20; done
}

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
# says "open Assignment" there, not the course's list. (1.3.80: in a part of their own, WALKS_PART, once its other
# pictures are done.)
WALK="todo,grades,calendar,notifications,inbox,course:101,externaltools,dashboard,todo,grades,calendar,notifications,inbox,course:101,push:/courses/101/assignments/1001,back,externaltools,dashboard,push:/courses/101/assignments/1001,back"
walks() {
  MODE=light
  local here=""
  [ "$WALKS_PART" = "$SHARD" ] && here=yes
  [ -n "$here" ] && mock_config '{"apiDelay":{"/assignments$":600,"":250}}'
  PART=$WALKS_PART shoot light-01w-walk 200 -SimplWalk "$WALK"
  PART=$WALKS_PART shoot light-01v-walk-plain 200 -SimplWalk "$WALK" -SimplPlain YES
  wait
  [ -n "$here" ] && mock_config '{"apiDelay":null}'
  return 0
}

# (1.3.78) bio <light|dark|both> <name> <wait> <tool> <state> [flags…]: a biology tool on the Tools page at 1440×920,
# opened as its launch's state asks (-SimplTool <tool> -SimplToolState <page>[:…], docs/mac-biology.md); "both" takes it
# in light and in dark (<mode>-<name>). A picture's launch keeps the tool's settings to itself (BioPrefs), so one
# picture's choices never show in the next. (1.3.79) A 3D model's picture — a cell, a molecule, the double helix, a
# nucleotide, the tRNA's L — is a heavy one (HEAVY_SHOT).
bio() {
  local modes="$1" name="$2" wait="$3" tool="$4" state="$5" m heavy=""
  shift 5
  [ "$modes" = both ] && modes="light dark"
  case "$tool:$state" in cell:*|biomol:*|dna:helix*|dna:nucleotides*|dna:rna:view=lshape*) heavy=yes ;; esac
  for m in $modes; do
    HEAVY_SHOT="$heavy" MODE="$m" SIZE=1440x920 shoot "$m-$name" "$wait" -SimplPlace tools -SimplTool "$tool" -SimplToolState "$state" "$@"
  done
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
    HEAVY_SHOT=yes MODE=dark SIZE="$size" shoot "dark-$name-${size%%x*}" "$wait" -SimplPlace tools -SimplTool "$tool"
    HEAVY_SHOT=yes MODE=dark SIZE="$size" shoot "dark-$name-${size%%x*}-folded" "$wait" -SimplPlace tools -SimplTool "$tool" -SimplSidebar hidden
  done
}

# the plain opens of the three tools the owner's report named: the Cell Explorer (its animal cell), DNA & RNA (its
# double helix) and the Biomolecule Builder (its amino acids, phenylalanine)
bio_plain_all() {
  bio_plain 57z-cell-plain 45 cell
  bio_plain 58z-dna-plain 24 dna
  bio_plain 61z-biomol-plain 40 biomol
}

# every biology tool's pages and what each is for: 57 Cell Explorer, 58 DNA & RNA, 59 Gene Expression,
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
# their pins at once took the front from each other (three of the four were pictured closed). (1.3.80: in a part of
# their own, PINS_PART.)
BIO_PIN_NAMES="light-54b-pin-gene light-54c-pin-cross light-54d-pin-popgen light-54e-pin-biolab"
bio_pins() {
  [ -n "${BIO_PINS:-}" ] || return 0
  PART=$PINS_PART MODE=light shoot light-54b-pin-gene 14 -SimplPinnedTools gene -SimplPinOpen gene -SimplPinCodon UGG
  wait_shot
  PART=$PINS_PART MODE=light shoot light-54c-pin-cross 14 -SimplPinnedTools "cross,popgen" -SimplPinOpen cross
  wait_shot
  PART=$PINS_PART MODE=light shoot light-54d-pin-popgen 14 -SimplPinnedTools "cross,popgen" -SimplPinOpen popgen
  wait_shot
  PART=$PINS_PART MODE=light shoot light-54e-pin-biolab 14 -SimplPinnedTools biolab -SimplPinOpen biolab
  wait_shot
}

# (1.3.78) the UI review's new states, each once (what the review's findings changed, seen on every run): the
# Dashboard's cards and the Announcements panel, Search's answer to a sum, the tour's search step and its
# offer; a quiz's access code and its multiple answers, an assignment's rubric wide and narrow; the periodic table's
# tour offered and an element compared, the citation, unit and Grade needed tools, the PDF tools with a file; the
# Calendar's Week and List, What-If, a message with a file, Grades, To Do and Notifications wide; a locked module,
# People and the Future courses; the first-run setup, the sign-in still going, a school's preview that failed, and the
# Settings tabs
ui_review() {
  MODE=light shoot light-01g-dashboard-cards 14 -SimplDashView cards
  MODE=light SIZE=1440x900 shoot light-01h-dashboard-cards-wide 14 -SimplDashView cards
  MODE=light shoot light-01u-counter-unread 30 -SimplSheet unread # (1.3.80: up to 30 seconds for its list, as the others)
  MODE=light shoot light-30b-palette-sum 14 -SimplOpen "palette:12*4"
  MODE=light shoot light-30d-palette-instant 20 -SimplOpen "palette:quiz" # (1.3.136: the school's results under the field before Return)
  # (1.3.152) the floating search: the four kinds before anything is typed; /open's tool asking which assignment
  MODE=light shoot light-30e-search-kinds 14 -SimplOpen "palette:"
  MODE=light shoot light-30f-open-which 24 -SimplOpen "palette:/open Knewton Alta › "
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
  # (1.3.112) the Calculator, GeoGebra's: its suite with a function graphed, and its pin's scientific calculator
  MODE=light SIZE=1280x900 shoot light-57b-tool-geogebra 20 -SimplPlace tools -SimplTool calc -SimplSolver "f(x) = x^2 - 2x - 3"
  MODE=light shoot light-54f-pin-calc 14 -SimplPinnedTools calc -SimplPinOpen calc
  # (1.3.127) the timer's pin: the web's mini timer — the strip of minutes under its marker, and Start
  MODE=light shoot light-54z-pin-pomo 14 -SimplPinnedTools pomo -SimplPinOpen pomo
  MODE=dark shoot dark-54z-pin-pomo 14 -SimplPinnedTools pomo -SimplPinOpen pomo
  MODE=light shoot light-54e-pin-need 14 -SimplPinnedTools need -SimplPinOpen need
  # (a two-page reading, for the annotator and Merge & Split: one PDF opens Split)
  MODE=light shoot light-58-tool-annotate 14 -SimplPlace tools -SimplTool mark -SimplToolFile "$PWD/scripts/dev/fixtures/reading.pdf"
  MODE=light shoot light-58b-tool-split 14 -SimplPlace tools -SimplTool pdfx -SimplToolFile "$PWD/scripts/dev/fixtures/reading.pdf"
  MODE=light shoot light-05w-calendar-week 12 -SimplPlace calendar -SimplCalView week
  MODE=light shoot light-05l-calendar-list 12 -SimplPlace calendar -SimplCalView list
  # (1.3.151) Find Appointment's popup over the calendar: the class, the group's card, a day open to its times
  MODE=light shoot light-05f-find-appointment 16 -SimplPlace calendar -SimplFindAppointment YES
  MODE=dark shoot dark-05f-find-appointment 16 -SimplPlace calendar -SimplFindAppointment YES
  # (1.3.165) Sync's popup over the calendar: Canvas's feed for a calendar app, the calendars linked, Send plans; and the
  # plan rail under a piece of work on To Do: its free times, the one chosen lit
  MODE=light shoot light-05s-calendar-sync 16 -SimplPlace calendar -SimplSync YES
  MODE=dark shoot dark-05s-calendar-sync 16 -SimplPlace calendar -SimplSync YES
  MODE=light shoot light-03p-todo-plan-rail 16 -SimplPlace todo -SimplPlanFirst YES
  MODE=dark shoot dark-03p-todo-plan-rail 16 -SimplPlace todo -SimplPlanFirst YES
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

# (1.3.79) five pins in the toolbar at 1024 points and narrow (every pin that fits shown, the rest in the
# Tools pull-down: ToolbarFit) and on a popup's bar; the focus timer's panel pulled out, over the window in dark (1.3.83:
# Open Full Screen at its head's right end); and an external tool's page light in Dark Mode — a tool window, an Alta and
# a Macmillan assignment. (1.3.83) A pin's panel with Open Full Screen at its top right, where Pop Out was — the graphing
# calculator's, as the owner pictured it, a graph drawn in it (-SimplPinGraph) — and a tool opened full screen from its
# pin (-SimplPinFull presses the button), what was begun in the panel carried over: Grade needed with the panel's three
# numbers (-SimplPinNeed), and the graph with the panel's graph (its console says what was carried)
pins_pages() {
  MODE=light SIZE=1024x700 shoot light-45c-toolbar-five-pins 14 -SimplPinnedTools "calc,need,cite,graph,units"
  MODE=light SIZE=780x700 shoot light-52b-narrow-five-pins 14 -SimplPinnedTools "calc,need,cite,graph,units"
  MODE=light shoot light-21k-alta-five-pins 20 -SimplOpen alta:104:4003 -SimplPinnedTools "calc,need,ptable,cite,units"
  MODE=dark shoot dark-54g-pin-float-timer 16 -SimplPinnedTools "pomo,calc" -SimplPinFloat pomo
  MODE=light shoot light-54i-pin-graph 24 -SimplPinnedTools graph -SimplPinOpen graph -SimplPinGraph "y=x^2"
  MODE=light shoot light-54j-pin-full-need 16 -SimplPinnedTools need -SimplPinFull need -SimplPinNeed 85,30,90
  MODE=light shoot light-54k-pin-full-graph 40 -SimplPinnedTools graph -SimplPinFull graph -SimplPinGraph "y=x^2"
  MODE=dark shoot dark-21h-tool-light-page 24 -SimplOpen tool:101:9
  MODE=dark shoot dark-21i-alta-light-page 20 -SimplOpen alta:104:4003
  MODE=dark shoot dark-21j-macmillan-light-page 26 -SimplOpen macmillan:104:4005
}

# (1.3.83) Alta's pop-ups read whole — the mock's "Knewton Alta" pop-up over its learn page, its body
# coming 1.2 s after its box (a spinner first) with a list, a link, two formulas, a picture and a drawing, as the Mac's
# own sheet in light and in dark; and one holding a video, given back to Alta's page, shown as it is
# (1.3.83) and Light or Dark above the objectives (-SimplAltaPageDark YES: Dark picked): the cover's start screen in Dark
# Mode with Dark picked and with Light (the default, 1.3.79's), a question in Dark in both modes, and Alta's page as it
# is (the overview) drawn dark
# (1.3.85: the mock's pop-ups asked for with ?sample=, as the Windows app's pictures ask for theirs — one mock page, one
# way to ask; ?dialog=rich and ?dialog=video before)
alta_pages() {
  local page="alta:104:4003:/mock-alta/learn/course/c1/assignment/a3"
  MODE=light shoot light-21m-alta-dialog 24 -SimplOpen "$page?sample=welcome"
  MODE=dark shoot dark-21m-alta-dialog 24 -SimplOpen "$page?sample=welcome"
  MODE=light shoot light-21n-alta-dialog-in-page 22 -SimplOpen "$page?sample=video"
  MODE=dark shoot dark-21o-alta-dark 20 -SimplOpen alta:104:4004 -SimplAltaPageDark YES
  MODE=dark shoot dark-21p-alta-light 20 -SimplOpen alta:104:4004
  MODE=dark shoot dark-21q-alta-question-dark 22 -SimplOpen "$page" -SimplAltaPageDark YES
  MODE=light shoot light-21q-alta-question-dark 22 -SimplOpen "$page" -SimplAltaPageDark YES
  MODE=dark shoot dark-21r-alta-page-dark 20 -SimplOpen alta:104:4003 -SimplAltaPageDark YES
  # (1.3.83) Show Browser (-SimplAltaShowBrowser YES): Knewton Alta's own page on the right as Alta draws it — the
  # cover, light, in light mode; the learn page in Dark Mode with Dark picked, drawn dark; and a 1024-point window, the
  # rail narrow, its row of controls in short words
  MODE=light shoot light-21s-alta-browser 20 -SimplOpen alta:104:4004 -SimplAltaShowBrowser YES
  MODE=dark shoot dark-21s-alta-browser-dark 22 -SimplOpen "$page" -SimplAltaShowBrowser YES -SimplAltaPageDark YES
  MODE=light SIZE=1024x768 shoot light-21t-alta-browser-narrow 22 -SimplOpen "$page" -SimplAltaShowBrowser YES
  # (1.3.99) the question answered (?sample=right / ?sample=wrong: the mock's verdict strip and Next Question): Alta's
  # verdict as the popup's own pill at the left of its buttons — Correct in green, light; Incorrect in red, dark
  MODE=light shoot light-21u-alta-right 22 -SimplOpen "$page?sample=right"
  MODE=dark shoot dark-21v-alta-wrong 22 -SimplOpen "$page?sample=wrong"
  # (1.3.106) the popup opened in the web view made ahead for an account that uses Alta (-SimplAltaSpare YES): the same
  # overview as 21a, its console's line saying `alta: view spare`
  MODE=light shoot light-21w-alta-spare 20 -SimplOpen alta:104:4003 -SimplAltaSpare YES
}

# (1.3.79) a tool opened whole is a popup over the window, never in its place (Shell/FloatingPopup.swift): the Periodic
# Table, Cell Explorer, (1.3.80) DNA & RNA and Flashcards, each over the Tools page, in light and in dark, at 1024 points
# wide and at the narrow size — (1.3.80) spanning the window over the sidebar, each 3D model filling its view; and DNA &
# RNA reopened at the size it was left at (-SimplPopupSize: 760 × 520, centred over the window)
tool_popups() {
  local m
  for m in light dark; do
    MODE="$m" SIZE=1024x768 shoot "$m-35p-popup-ptable" 14 -SimplPlace tools -SimplTool ptable -SimplPTColour category
    MODE="$m" SIZE=780x700 shoot "$m-35p-popup-ptable-narrow" 14 -SimplPlace tools -SimplTool ptable -SimplPTColour category
    HEAVY_SHOT=yes MODE="$m" SIZE=1024x768 shoot "$m-35q-popup-cell" 45 -SimplPlace tools -SimplTool cell -SimplToolState animal
    HEAVY_SHOT=yes MODE="$m" SIZE=780x700 shoot "$m-35q-popup-cell-narrow" 45 -SimplPlace tools -SimplTool cell -SimplToolState animal
    HEAVY_SHOT=yes MODE="$m" SIZE=1024x768 shoot "$m-35s-popup-dna" 30 -SimplPlace tools -SimplTool dna
    HEAVY_SHOT=yes MODE="$m" SIZE=780x700 shoot "$m-35s-popup-dna-narrow" 30 -SimplPlace tools -SimplTool dna
    MODE="$m" SIZE=1024x768 shoot "$m-35r-popup-flashcards" 12 -SimplPlace tools -SimplTool fc
    MODE="$m" SIZE=780x700 shoot "$m-35r-popup-flashcards-narrow" 12 -SimplPlace tools -SimplTool fc
  done
  HEAVY_SHOT=yes MODE=light SIZE=1280x820 shoot light-35t-popup-dna-kept-size 30 -SimplPlace tools -SimplTool dna -SimplPopupSize tool.dna=760x520
}

# (1.3.80) the Tools page's Chemistry section — the Periodic Table split into four tools — in light and in dark; Atoms
# & Orbitals on iron's atom, and narrow on oxygen's 2p orbitals (its controls and facts in sight under the model);
# Atoms & Orbitals as a student comes to it from carbon's facts page (-SimplPTHandOff atoms presses Open in Atoms &
# Orbitals there); Lewis Structures in dark (each tool's light picture is in push_set: light-55-ptable-trends,
# light-55c-lewis, light-55d-molecule; Atoms & Orbitals' dark one dark-55l-atoms-cloud); Atoms & Orbitals' pin
# opened; and "orbitals" typed in the search field, its tool among the suggestions
chem_tools() {
  local m
  for m in light dark; do
    MODE="$m" SIZE=1280x900 shoot "$m-35c-tools-chemistry" 12 -SimplPlace tools -SimplToolsScroll chemistry
  done
  MODE=light SIZE=1440x920 shoot light-55o-atoms 16 -SimplPlace tools -SimplTool atoms -SimplPTElement 26
  MODE=light SIZE=780x700 shoot light-55p-atoms-narrow 16 -SimplPlace tools -SimplTool atoms -SimplPTElement 8 -SimplPTModel orbitals
  # (1.3.135: carbon's page with its atom in 3D on it — part of the table now; and its Orbitals)
  MODE=light SIZE=1440x920 shoot light-55q-atoms-from-facts 18 -SimplPlace tools -SimplTool ptable -SimplPTElement 6
  MODE=dark SIZE=1440x920 shoot dark-55r-ptable-page-3d 18 -SimplPlace tools -SimplTool ptable -SimplPTElement 26
  MODE=dark SIZE=1440x920 shoot dark-55r-lewis 14 -SimplPlace tools -SimplTool lewis -SimplLewisPreset "NO₃⁻"
  MODE=light shoot light-54h-pin-atoms 14 -SimplPinnedTools atoms -SimplPinOpen atoms
  MODE=light shoot light-30c-palette-chemistry 14 -SimplOpen "palette:orbitals"
}

# (tools) the chemistry calculators. The Equation Balancer on a redox equation in light and dark, narrow on
# propane's combustion, an equation that can't be balanced (an element on one side only, named), and its pin opened.
chem_calc_tools() {
  local m
  for m in light dark; do
    MODE="$m" SIZE=1440x920 shoot "$m-71-tool-balance" 14 -SimplPlace tools -SimplTool balance -SimplBalance "KMnO4 + HCl → KCl + MnCl2 + H2O + Cl2"
  done
  MODE=light SIZE=780x700 shoot light-71b-tool-balance-narrow 14 -SimplPlace tools -SimplTool balance -SimplBalance "C3H8 + O2 → CO2 + H2O"
  MODE=light SIZE=1440x920 shoot light-71a-tool-balance-cant 14 -SimplPlace tools -SimplTool balance -SimplBalance "Na + Cl2 -> NaBr"
  MODE=light shoot light-54l-pin-balance 14 -SimplPinnedTools balance -SimplPinOpen balance -SimplBalance "Fe + O2 -> Fe2O3"
  # Molar Mass & Stoichiometry: glucose's molar mass and breakdown in light and dark, the Reaction tab on water from
  # 4 g of hydrogen and 16 g of oxygen (oxygen limits), the Empirical tab narrow, and its pin on slaked lime
  for m in light dark; do
    MODE="$m" SIZE=1440x920 shoot "$m-71c-tool-moles" 14 -SimplPlace tools -SimplTool moles -SimplMoles molar -SimplMolesFormula "C6H12O6"
  done
  MODE=light SIZE=1440x920 shoot light-71d-tool-moles-reaction 14 -SimplPlace tools -SimplTool moles -SimplMoles reaction -SimplMolesEquation "2H2 + O2 → 2H2O" -SimplMolesGiven "H2=4g,O2=16g"
  MODE=light SIZE=780x700 shoot light-71f-tool-moles-narrow 14 -SimplPlace tools -SimplTool moles -SimplMoles empirical -SimplMolesEmpirical "C=40,H=6.71,O=53.3" -SimplMolesMass 180
  MODE=light shoot light-54v-pin-moles 14 -SimplPinnedTools moles -SimplPinOpen moles -SimplMolesFormula "Ca(OH)2"
  # Gas Laws: a mole at STP solved for n in light and dark, the Combined tab (V2 solved) and Boyle's chip, narrow
  # with V solved from mixed units, and its pin
  for m in light dark; do
    MODE="$m" SIZE=1440x920 shoot "$m-72-tool-gas" 14 -SimplPlace tools -SimplTool gas -SimplGas ideal -SimplGasValues "P=1 atm,V=22.4 L,T=273.15 K"
  done
  MODE=light SIZE=1440x920 shoot light-72b-tool-gas-combined 14 -SimplPlace tools -SimplTool gas -SimplGas combined -SimplGasValues "P1=1 atm,V1=2 L,T1=300 K,P2=2 atm,T2=600 K"
  MODE=light SIZE=1440x920 shoot light-72e-tool-gas-boyle 14 -SimplPlace tools -SimplTool gas -SimplGas combined -SimplGasConstant T -SimplGasValues "P1=1 atm,V1=2 L,V2=4 L"
  MODE=light SIZE=780x700 shoot light-72a-tool-gas-narrow 14 -SimplPlace tools -SimplTool gas -SimplGas ideal -SimplGasValues "P=2 atm,n=0.5 mol,T=25 C"
  MODE=light shoot light-54w-pin-gas 14 -SimplPinnedTools gas -SimplPinOpen gas -SimplGasValues "P=1 atm,V=22.4 L,T=273.15 K"
  # Solutions & pH: the pH tab on [H+] = 1e-3 with acetic acid from its concentration in light and dark, a weak
  # acid's titration curve, a dilution ("how much stock?"), the Molarity tab narrow, and its pin on pH 3
  for m in light dark; do
    MODE="$m" SIZE=1440x920 shoot "$m-72c-tool-solution" 14 -SimplPlace tools -SimplTool solution -SimplSolution ph -SimplSolutionValues "H=1e-3,kind=weak acid,c=0.10,K=1.8e-5"
  done
  MODE=light SIZE=1440x920 shoot light-72d-tool-solution-titration 14 -SimplPlace tools -SimplTool solution -SimplSolution titration -SimplSolutionValues "kind=wa-sb,Ca=0.1,Va=25,Ct=0.1,K=1.8e-5"
  MODE=light SIZE=1440x920 shoot light-72f-tool-solution-dilution 14 -SimplPlace tools -SimplTool solution -SimplSolution dilution -SimplSolutionValues "M1=2,V1=50 mL,M2=0.5"
  MODE=light SIZE=780x700 shoot light-72g-tool-solution-narrow 14 -SimplPlace tools -SimplTool solution -SimplSolution molarity -SimplSolutionValues "formula=NaCl,amount=5.844 g,volume=100 mL"
  MODE=light shoot light-54x-pin-solution 14 -SimplPinnedTools solution -SimplPinOpen solution -SimplSolutionValues "pH=3"
}

# (tools) the Tools page's Physics section in light and in dark; Kinematics & Projectiles' 1D page (a fall of 20 m:
# Δx, v₀ and a given, t and v found, the graphs beside) in light, its Projectile page (20 m/s at 45°, the path flown,
# a target 30 m off) in light and in dark; and its pin opened (20 m/s at 45° to its range and height). Vectors on
# (3, 4) and (1, 2) tip to tail in light and in dark, and its pin opened on the same two. The Circuit Builder on its
# series preset (9 V, 100 Ω and 200 Ω: 0.03 A, the arrows running) in light and in dark, on the voltage divider (a
# voltmeter reading 8 V across the 2 kΩ) in light, and its pin opened (Ohm's law 9 V over 300 Ω, 100 and 200 in
# series and in parallel)
phys_tools() {
  local m
  for m in light dark; do
    MODE="$m" SIZE=1280x900 shoot "$m-35d-tools-physics" 12 -SimplPlace tools -SimplToolsScroll physics
    MODE="$m" SIZE=1440x920 shoot "$m-77b-motion-projectile" 14 -SimplPlace tools -SimplTool motion -SimplMotion projectile -SimplMotionValues "v0=20,angle=45,h=0,target=30"
  done
  MODE=light SIZE=1440x920 shoot light-77-motion-1d 14 -SimplPlace tools -SimplTool motion -SimplMotion 1d -SimplMotionValues "x=-20,v0=0,a=-9.81"
  MODE=light shoot light-54s-pin-motion 14 -SimplPinnedTools motion -SimplPinOpen motion
  for m in light dark; do
    MODE="$m" SIZE=1440x920 shoot "$m-77c-vectors" 14 -SimplPlace tools -SimplTool vectors -SimplVectors "3,4;1,2" -SimplVectorsMode 2d
  done
  MODE=light shoot light-54t-pin-vectors 14 -SimplPinnedTools vectors -SimplPinOpen vectors
  for m in light dark; do
    MODE="$m" SIZE=1440x920 shoot "$m-78-circuit-series" 14 -SimplPlace tools -SimplTool circuit -SimplCircuit series
  done
  MODE=light SIZE=1440x920 shoot light-78b-circuit-divider 14 -SimplPlace tools -SimplTool circuit -SimplCircuit divider
  MODE=light shoot light-54u-pin-circuit 14 -SimplPinnedTools circuit -SimplPinOpen circuit
}

# (tools) the study and maths tools, each its popup over the Tools page in light and in dark, narrow, and its pin
# opened: the Word Counter on an essay's opening (-SimplWordsText) with a target of 150 words; Statistics on each of
# its pages (-SimplStats <page> -SimplStatsData "…": a class's exam scores described, hours of revision against marks,
# the normal curve and Student's t, a one-sample t-test), the Describe page in dark and narrow too; the Matrix
# Calculator on a 3 × 3's inverse (-SimplMatrixA, -SimplMatrixOp), its RREF with the steps open (-SimplMatrixSteps YES),
# and A·x = b (-SimplMatrixB the column)
study_tools() {
  local m
  local essay="The printing press changed Europe faster than any invention before it. Within fifty years of Gutenberg's first Bible, presses in two hundred towns had printed some twenty million books — more than every scribe of the previous thousand years. Ideas that once travelled at the pace of a copyist's hand now crossed the continent in a season.

This essay argues that the press did not simply spread ideas; it changed which ideas could survive. A pamphlet printed in Wittenberg could be read in Zurich before its author's opponents had drafted a reply, and the reply itself would be printed."
  for m in light dark; do
    MODE="$m" SIZE=1440x920 shoot "$m-73-tool-words" 14 -SimplPlace tools -SimplTool words -SimplWordsText "$essay" -SimplWordsTarget 150
  done
  MODE=light SIZE=780x700 shoot light-73b-tool-words-narrow 14 -SimplPlace tools -SimplTool words -SimplWordsText "$essay" -SimplWordsTarget 150
  MODE=light shoot light-54m-pin-words 14 -SimplPinnedTools words -SimplPinOpen words -SimplWordsText "Hello, world! This is Simpl."
  local scores="72 85 90 66 78 95 88 70 81 77 84 69 91 73 86 79 83 58 88 76"
  for m in light dark; do
    MODE="$m" SIZE=1440x920 shoot "$m-73c-stats-describe" 14 -SimplPlace tools -SimplTool stats -SimplStats describe -SimplStatsData "$scores"
  done
  MODE=light SIZE=1440x920 shoot light-73d-stats-regression 14 -SimplPlace tools -SimplTool stats -SimplStats regression -SimplStatsData "1 2 3 4 5 6 7 8; 52 55 61 64 70 73 78 85"
  MODE=light SIZE=1440x920 shoot light-73e-stats-dist 14 -SimplPlace tools -SimplTool stats -SimplStats dist
  MODE=light SIZE=1440x920 shoot light-73f-stats-tests 14 -SimplPlace tools -SimplTool stats -SimplStats tests -SimplStatsData "$scores"
  MODE=light SIZE=780x700 shoot light-73g-stats-describe-narrow 14 -SimplPlace tools -SimplTool stats -SimplStats describe -SimplStatsData "$scores"
  MODE=light shoot light-54n-pin-stats 14 -SimplPinnedTools stats -SimplPinOpen stats -SimplStatsData "2, 4, 4, 4, 5, 5, 7, 9"
  local three="2 1 -1;-3 -1 2;-2 1 2"
  for m in light dark; do
    MODE="$m" SIZE=1440x920 shoot "$m-74-tool-matrix" 14 -SimplPlace tools -SimplTool matrix -SimplMatrixA "$three" -SimplMatrixB "8;-11;-3" -SimplMatrixOp inverse
  done
  MODE=light SIZE=1440x920 shoot light-74b-tool-matrix-rref 14 -SimplPlace tools -SimplTool matrix -SimplMatrixA "1 2 3;4 5 6;7 8 9" -SimplMatrixOp rref -SimplMatrixSteps YES
  MODE=light SIZE=1440x920 shoot light-74c-tool-matrix-solve 14 -SimplPlace tools -SimplTool matrix -SimplMatrixA "$three" -SimplMatrixB "8;-11;-3" -SimplMatrixOp solve
  MODE=light SIZE=780x700 shoot light-74d-tool-matrix-narrow 14 -SimplPlace tools -SimplTool matrix -SimplMatrixA "$three" -SimplMatrixB "8;-11;-3" -SimplMatrixOp eigen
  MODE=light shoot light-54o-pin-matrix 14 -SimplPinnedTools matrix -SimplPinOpen matrix -SimplMatrixA "1 2;3 4"
}

# (tools) the Files group's Compress Files: its before/after table over the converter's sample files (the hand-in's
# photo, the annotator's reading and a screenshot of the calendar) made smaller at Balanced as the tool opens
# (-SimplShrinkFiles, -SimplShrinkPreset; the console says what each came to: "[Simpl] shrink photo.jpg: 104 KB → …"),
# in light and in dark; narrow, at Smallest; and its pin
files_tools() {
  local m samples="$PWD/scripts/dev/fixtures/photo.jpg,$PWD/scripts/dev/fixtures/reading.pdf,$PWD/docs/screenshots/calendar.png"
  for m in light dark; do
    MODE="$m" SIZE=1440x920 shoot "$m-75-tool-shrink" 16 -SimplPlace tools -SimplTool shrink -SimplShrinkFiles "$samples" -SimplShrinkPreset balanced
  done
  MODE=light SIZE=780x700 shoot light-75b-tool-shrink-narrow 16 -SimplPlace tools -SimplTool shrink -SimplShrinkFiles "$samples" -SimplShrinkPreset smallest
  MODE=light shoot light-54p-pin-shrink 14 -SimplPinnedTools shrink -SimplPinOpen shrink
}

# (tools) the Study group's Study Planner over the mock's work: the week view in light and in dark (two hours a day,
# 45-minute blocks, two of the lab's blocks done: -SimplPlanHours, -SimplPlanBlock, -SimplPlanDone), the list view
# (-SimplPlanView list), the week narrow, and its pin (today's blocks as a checklist)
planner_tool() {
  local m
  for m in light dark; do
    MODE="$m" SIZE=1440x920 shoot "$m-75c-tool-plan" 18 -SimplPlace tools -SimplTool plan -SimplPlanHours 2 -SimplPlanBlock 45 -SimplPlanDone "2003:1"
  done
  MODE=light SIZE=1440x920 shoot light-75d-tool-plan-list 18 -SimplPlace tools -SimplTool plan -SimplPlanHours 2 -SimplPlanBlock 45 -SimplPlanView list
  MODE=light SIZE=780x700 shoot light-75e-tool-plan-narrow 18 -SimplPlace tools -SimplTool plan -SimplPlanHours 2 -SimplPlanBlock 45
  MODE=light shoot light-54q-pin-plan 16 -SimplPinnedTools plan -SimplPinOpen plan
}

# (tools) the Study group's Verb Conjugations: a Spanish table in light (tener, its irregular forms tinted), a French one
# in dark (aller), a German separable verb narrow (anfangen), and its pin (-SimplVerbs, -SimplVerb)
verbs_tool() {
  MODE=light SIZE=1440x920 shoot light-76-tool-verbs 14 -SimplPlace tools -SimplTool verbs -SimplVerbs es -SimplVerb tener
  MODE=dark SIZE=1440x920 shoot dark-76-tool-verbs 14 -SimplPlace tools -SimplTool verbs -SimplVerbs fr -SimplVerb aller
  MODE=light SIZE=780x700 shoot light-76b-tool-verbs-narrow 14 -SimplPlace tools -SimplTool verbs -SimplVerbs de -SimplVerb anfangen
  MODE=light shoot light-54r-pin-verbs 14 -SimplPinnedTools verbs -SimplPinOpen verbs
}

# (1.3.80) how each 3D model was framed, from the consoles ("[Simpl] bio frame <scene>: view W × H, model X% across,
# Y% up", and the atom's and the molecule's frames): each picture's last framing and its model's share of its view's
# limiting side (the larger of the two) in frame-check.txt — 75% to 85% is as framed (SceneFill); under 70% is said
# as a warning of the run
frame_check() {
  local out="$OUT_ABS/frame-check.txt" f name line small=""
  : > "$out"
  for f in "$OUT_ABS"/console-*.txt; do
    [ -f "$f" ] || continue
    line="$(grep -E '\[Simpl\] (bio|atom|molecule) frame' "$f" | tail -1)"
    [ -n "$line" ] || continue
    name="$(basename "$f" .txt)"
    name="${name#console-}"
    echo "$name: ${line#*\] }" >> "$out"
    if echo "$line" | awk '{ for (i = 1; i <= NF; i++) if ($i ~ /%,?$/) { v = $i + 0; if (v > m) m = v } } END { exit !(m < 70) }'; then
      small="$small $name"
    fi
  done
  if [ -n "$small" ]; then echo "::warning::a 3D model framed under 70% of its view:$small (frame-check.txt)"; fi
}

# (1.3.78) a discussion's post that never measured in its web view says so on the console (Components/RichText.swift,
# in a picture's run only: its words are shown natively meanwhile); said here as a warning of the run
richtext_check() {
  local hits
  hits="$(grep -l '\[richtext\] not measured' "$OUT_ABS"/console-*.txt 2>/dev/null | xargs -n1 basename 2>/dev/null | tr '\n' ' ')"
  if [ -n "$hits" ]; then echo "::warning::a post's web view never measured (its words shown as plain text): $hits"; fi
}

# (1.3.80) Live checks (mac/Simpl/Engine/LiveRefresh.swift): the screen on show asked again, every 4 seconds here
# (-SimplLiveEvery 4; a minute in the app), the mock school changed behind the engine's back 6 seconds in
# (-SimplLiveChange: a piece of work marked done, a course's total moved, a task added — the engine's liveDevChange), and
# the picture taken once 5 checks are done (-SimplLiveShotAfter 5). Against a mock of their own (:8810, so their changes
# never show in another picture) and one at a time, after every other picture: a check runs only while its app is in
# front, and with the other copies of the app at work it would not be. Each check says one line on the console —
# "live: dashboard unchanged · 12 reads · 0 redraws", "live: dashboard updated counters,day,list · 9 reads · 1 redraw" —
# and live_check reads them. (1.3.80: in a part of their own, LIVE_PART; the mock is up once it answers, not after a
# fixed two seconds.)
LIVE_NAMES="light-48a-live-dashboard light-48b-live-cards light-48c-live-todo light-48d-live-course"
live_shots() {
  local mock="" live=(-SimplLiveEvery 4 -SimplLiveShotAfter 5) i
  if [ -z "$PLANNING" ] && [ "$LIVE_PART" = "$SHARD" ]; then
    node scripts/dev/mock-canvas.mjs 8810 8811 > "$HOMES/mock-live.log" 2>&1 &
    mock=$!
    for i in $(seq 1 50); do curl -s -o /dev/null http://localhost:8810/ && break; sleep 0.1; done
    mock_config '{"siteNames":true}' 8810
  fi
  # (each picture waited for by its own worker's pid: a bare `wait` would wait for the mock above too, which never ends)
  PART=$LIVE_PART MODE=light BASE=http://localhost:8810 shoot light-48a-live-dashboard 50 "${live[@]}" -SimplLiveChange done:6
  wait_shot
  PART=$LIVE_PART MODE=light BASE=http://localhost:8810 shoot light-48b-live-cards 50 "${live[@]}" -SimplDashView cards -SimplLiveChange grade:6
  wait_shot
  PART=$LIVE_PART MODE=light BASE=http://localhost:8810 shoot light-48c-live-todo 50 "${live[@]}" -SimplPlace todo -SimplLiveChange task:6
  wait_shot
  # (a course's page at its own pace: every 4 seconds here too, -SimplLiveCourseEvery, ten minutes in the app)
  PART=$LIVE_PART MODE=light BASE=http://localhost:8810 shoot light-48d-live-course 40 -SimplLiveEvery 4 -SimplLiveCourseEvery 4 -SimplLiveShotAfter 3 \
    -SimplPlace section:courses/101:assignments
  wait_shot
  if [ -n "$mock" ]; then kill "$mock" >/dev/null 2>&1 || true; fi
}

# (1.3.80) live_check: what the live pictures' consoles say, as live-check.txt (the workflow fails on a FAIL line). A
# check that found nothing new drew nothing; the mock school's change was found by a check that put in place only what
# it changed — the counters and the List's row, one course's card, To Do's new row — and the next check found nothing
# new again; a course's page was checked at its own pace; no check went without an answer.
live_check() {
  local out="$OUT_ABS/live-check.txt"
  local d="$OUT_ABS/console-light-48a-live-dashboard.txt" k="$OUT_ABS/console-light-48b-live-cards.txt"
  local t="$OUT_ABS/console-light-48c-live-todo.txt" a="$OUT_ABS/console-light-48d-live-course.txt"
  : > "$out"
  say() { if [ "$1" = 0 ]; then echo "ok   $2" >> "$out"; else echo "FAIL $2" >> "$out"; fi; }
  after() { awk -v a="$2" -v b="$3" 'index($0, a) { seen = 1 } seen && index($0, b) { found = 1 } END { exit !found }' "$1" 2>/dev/null; }
  grep -Eq 'live: dashboard unchanged · [0-9]+ reads? · 0 redraws' "$d" 2>/dev/null && ! grep -Eq 'live: dashboard unchanged .* · [1-9][0-9]* redraws?' "$d" 2>/dev/null
  say $? "the Dashboard checked with nothing new drew nothing: $(grep -Ec 'live: dashboard unchanged' "$d" 2>/dev/null) unchanged, each 0 redraws ($(grep -Eo 'live: dashboard unchanged · [0-9]+ reads?' "$d" 2>/dev/null | head -1 | grep -Eo '[0-9]+ reads?'))"
  grep -q 'live: dev change done made' "$d" 2>/dev/null
  say $? "the mock school's soonest work marked done behind the engine's back"
  grep -Eq 'live: dashboard updated ([a-z-]+,)*counters[, ].* · [1-9][0-9]* redraws?' "$d" 2>/dev/null
  say $? "a check found it and put in place the counters: $(grep -Eo 'live: dashboard updated [a-z,-]+' "$d" 2>/dev/null | head -1)"
  grep -Eq 'live: dashboard updated ([a-z-]+,)*list[, ]' "$d" 2>/dev/null
  say $? "and the List's row (Coming up)"
  after "$d" 'live: dashboard updated' 'live: dashboard unchanged'
  say $? "the check after it found nothing new again"
  grep -q 'live: dev change grade made' "$k" 2>/dev/null && grep -Eq 'live: dashboard updated ([a-z-]+,)*cards[, ]' "$k" 2>/dev/null
  say $? "a course's total moved: the Cards view's card changed ($(grep -Eo 'live: dashboard updated [a-z,-]+' "$k" 2>/dev/null | head -1))"
  grep -q 'live: dev change task made' "$t" 2>/dev/null && grep -q 'live: todo updated todo' "$t" 2>/dev/null
  say $? "a task added: To Do's list changed"
  after "$t" 'live: todo updated' 'live: todo unchanged'
  say $? "and To Do's next check found nothing new again"
  grep -Eq 'live: assignments unchanged' "$a" 2>/dev/null
  say $? "a course's assignments checked at their own pace: $(grep -Ec 'live: assignments ' "$a" 2>/dev/null) checks"
  ! grep -q 'live: .* not checked' "$d" "$k" "$t" "$a" 2>/dev/null
  say $? "no check went without an answer"
  cat "$out"
}

# (1.3.92: the lists' pictures wait 16 s like 36d's — at 12 s, with the app grown by the tools of 1.3.89–1.3.91, a list
# had not always reckoned its room before its picture on GitHub's Macs, and this check failed three runs out of four)
# (1.3.102) again_check: what light-01x-sidebar-again's walk said (Support/OpenClock.swift), as again-check.txt — the
# assignment pushed on the Dashboard ("walk: at dashboard, 1 pushed"), then the Dashboard's row pressed again and the
# Dashboard at its root with nothing pushed ("walk: at dashboard, 0 pushed"). A FAIL fails the check (SHOTS=check: the
# workflow's "What the apps said").
again_check() {
  local out="$OUT_ABS/again-check.txt" f="$OUT_ABS/console-light-01x-sidebar-again.txt"
  : > "$out"
  if awk 'index($0, "walk: at dashboard, 1 pushed") { seen = 1 } seen && index($0, "walk: at dashboard, 0 pushed") { found = 1 } END { exit !found }' "$f" 2>/dev/null; then
    echo "ok   the Dashboard's row pressed again with an assignment on it: back to the Dashboard, nothing pushed" >> "$out"
  else
    echo "FAIL the Dashboard's row pressed again with an assignment on it did not go back to the Dashboard: $(grep -o 'walk: at .*' "$f" 2>/dev/null | tr '\n' ';')" >> "$out"
  fi
  cat "$out"
  if grep -q '^FAIL' "$out"; then grep '^FAIL' "$out" | sed 's/^/::error::sidebar: /'; return 1; fi
}

# (1.3.104) newwin_check: what light-21k-tool-new-window's tool window said (Shell/ToolWindow.swift), as newwin-check.txt
# — Canvas's page for a tool it opens in a new window, its launch form sent on in the window. A FAIL fails the check.
newwin_check() {
  local out="$OUT_ABS/newwin-check.txt" f="$OUT_ABS/console-light-21k-tool-new-window.txt"
  : > "$out"
  if grep -q "tool window: the school's page for the tool, its launch sent on in this window" "$f" 2>/dev/null; then
    echo "ok   Canvas's page for a tool it opens in a new window: its launch sent on in the tool window" >> "$out"
  else
    echo "FAIL Canvas's page for a tool it opens in a new window: its launch was not sent on ($(grep -o 'tool window: .*' "$f" 2>/dev/null | tr '\n' ';'))" >> "$out"
  fi
  cat "$out"
  if grep -q '^FAIL' "$out"; then grep '^FAIL' "$out" | sed 's/^/::error::tool window: /'; return 1; fi
}

# (1.3.103) quicklook_check: what light-14e-instructions-file-open's walk and its Quick Look said (Support/OpenClock.swift,
# Engine/FilePreview.swift), as quicklook-check.txt — the file link in the assignment's instructions found and clicked,
# and the panel up with the file in it ("[files] Quick Look: 1 item, …"), not empty; and no picture's app saying its
# panel was taken by no window ("Quick Look: 0 items"). The module's file (light-14d) is said, not failed: opened at
# launch, its app may still be behind another copy when its picture is taken. A FAIL fails the check (SHOTS=check: the
# workflow's "What the apps said").
quicklook_check() {
  local out="$OUT_ABS/quicklook-check.txt" f="$OUT_ABS/console-light-14e-instructions-file-open.txt" m="$OUT_ABS/console-light-14d-module-file-open.txt" empty
  : > "$out"
  if grep -q 'walk: file link .*/files/' "$f" 2>/dev/null; then
    echo "ok   the instructions' file link clicked: $(grep -o 'walk: file link .*' "$f" | head -1)" >> "$out"
  else
    echo "FAIL the instructions' file link was not clicked: $(grep -o 'walk: file link .*' "$f" 2>/dev/null | head -1)" >> "$out"
  fi
  if grep -q '\[files\] Quick Look: 1 item' "$f" 2>/dev/null; then
    echo "ok   its file in Quick Look: $(grep -o 'Quick Look: 1 item.*' "$f" | head -1)" >> "$out"
  else
    echo "FAIL its file was not seen in Quick Look: $(grep -o '\[files\] Quick Look: .*' "$f" 2>/dev/null | head -1)" >> "$out"
  fi
  empty="$(grep -l 'Quick Look: 0 items' "$OUT_ABS"/console-*.txt 2>/dev/null | sed 's|.*/console-||; s|\.txt$||' | tr '\n' ' ')"
  if [ -z "$empty" ]; then
    echo "ok   no app's Quick Look was left with nothing in it" >> "$out"
  else
    echo "FAIL Quick Look taken by no window (0 items) in: $empty" >> "$out"
  fi
  if grep -q '\[files\] Quick Look: 1 item' "$m" 2>/dev/null; then
    echo "ok   the module's file in Quick Look: $(grep -o 'Quick Look: 1 item.*' "$m" | head -1)" >> "$out"
  else
    echo "note the module's file was not seen in Quick Look before its picture (its app behind another copy?)" >> "$out"
  fi
  cat "$out"
  if grep -q '^FAIL' "$out"; then grep '^FAIL' "$out" | sed 's/^/::error::quick look: /'; return 1; fi
}

# (1.3.82) tool_list_check: what the External Tools lists' consoles say, as tool-list-check.txt (a FAIL line is said as
# a warning of the run): each list reckoned its room once as it opened ("[Simpl] tool list room: 303 (…)"), the list
# read again under it was still open after the read, and no copy of the app crashed (a crash report is kept beside the
# pictures by keep_crashes: 1.3.81's was a popover sized again and again until AppKit stopped the app). (1.3.80: the
# crash reports looked for on this Mac and among the parts' files put together, where SHOTS=check finds them.)
tool_list_check() {
  local out="$OUT_ABS/tool-list-check.txt" r="$OUT_ABS/console-light-36d-external-tools-list-reread.txt" f crashes=0
  : > "$out"
  say() { if [ "$1" = 0 ]; then echo "ok   $2" >> "$out"; else echo "FAIL $2" >> "$out"; fi; }
  for f in "$OUT_ABS"/console-*-36[bcd]-external-tools-*.txt; do
    [ -f "$f" ] || continue
    grep -q '\[Simpl\] tool list room: ' "$f"
    say $? "$(basename "$f" .txt | sed 's/^console-//'): $(grep -o 'tool list room: .*' "$f" | head -1)"
  done
  grep -q "read again; Knewton Alta's list still open" "$r" 2>/dev/null
  say $? "the list was still open after the tools were read again under it: $(grep -o 'read again; .*' "$r" 2>/dev/null | head -1)"
  for f in "$HOME"/Library/Logs/DiagnosticReports/*Simpl* "$OUT_ABS"/crash-*.txt; do
    if [ -f "$f" ]; then crashes=$((crashes + 1)); fi
  done
  [ "$crashes" = 0 ]
  say $? "no copy of the app crashed: $crashes crash reports"
  if grep -q '^FAIL' "$out"; then grep '^FAIL' "$out" | sed 's/^/::warning::tool list: /'; fi
  cat "$out"
}

# (1.3.80) push_set: the pictures only a push took before every run took them all (SHOTS=quick was a set of its own,
# 1.2.1–1.3.80) — in the order a push took them. Where the two sets gave one name two launches, the full set's keeps
# the name and the push's is here under its own: light-01m-counter-morph-next and dark-01m-counter-morph-graded (Next
# 7 days' and Graded's tiles half-way through growing; the full set's is Tomorrow's), light-35-tools-plain (the Tools
# page with nothing pinned; the full set's light-35-tools has four pins). light-32-rubric-ring is the push's (the ring
# alone): the full set's opened the first criterion, as light-32b-rubric-open does.
push_set() {
  MODE=light
  # (1.3.80: a counter's panel waits up to 30 seconds — taken as soon as it has settled — for its list: at 14, under load,
  # the first tile's said "Loading…")
  shoot light-01r-counter-middle 30 -SimplSheet tomorrow # (1.3.16: a middle tile's panel centred on it)
  # (1.3.78) the row of six: the first tile's panel from its left edge, the last one's from its right; and the row with
  # the sidebar folded away
  shoot light-01e-counter-first 30 -SimplSheet today
  shoot light-01f-counter-last 30 -SimplSheet graded
  shoot light-01s-dashboard-no-sidebar 12 -SimplSidebar hidden
  # (1.3.80) Next 7 days' tile half-way through growing into its panel over the page (held there: -SimplMorphHold)
  shoot light-01m-counter-morph-next 30 -SimplSheet next -SimplMorphHold 0.5 # (its wait the longest: under load the panel came 15 seconds in)
  shoot light-21d-macmillan 26 -SimplOpen macmillan:104:4005 # (1.3.66: a Macmillan Learning assignment — 1.3.163: in the tool window, as Macmillan draws it)
  shoot light-21e-second-window 18 -SimplOpen window:section:courses/101:grades # (1.3.69: a window more, on a course's grades: its breakdown bar)
  shoot light-36-external-tools 16 -SimplPlace externaltools # (1.3.67: every external tool in the courses)
  shoot light-36b-external-tools-list 16 -SimplPlace externaltools -SimplExternalToolList knewton # (1.3.77: every place Knewton Alta is used, to choose from)
  shoot light-36c-external-tools-macmillan 16 -SimplPlace externaltools -SimplExternalToolList macmillan # (1.3.79: CHEM's week-titled Macmillan assignments and module item, one tool)
  # (1.3.82) Knewton Alta's list open while the tools are read again and put in place under it (-SimplExternalToolReread):
  # in 1.3.81 the tools' answer coming in a second time under the open list had its popover sized and sized again
  # until AppKit stopped the app; its console says the list is still open after the read (tool_list_check)
  shoot light-36d-external-tools-list-reread 16 -SimplPlace externaltools -SimplExternalToolList knewton -SimplExternalToolReread YES
  shoot light-37-developer 16 -SimplOpen developer # (1.3.72: the Developer window)
  shoot light-37b-developer-logs 20 -SimplOpen developer -SimplDevPage logs # (1.3.134: Simpl's own lines in Logs)
  shoot light-37c-developer-crashes 20 -SimplOpen developer -SimplDevPage crashes # (1.3.146: the crash log — runs of the suite's that were stopped, kept)
  shoot light-01p-dashboard-preview 16 -SimplOpen preview
  # (1.3.102) an assignment pushed on the Dashboard (as Hand In or See Feedback pushes one), then the Dashboard's row in
  # the sidebar pressed again: the Dashboard at its root, nothing pushed (again_check) — (1.3.133) 45 s: on a slow GitHub
  # Mac the Dashboard took 14.5 s to open and 30 s cut the walk off before it said where it ended
  shoot light-01x-sidebar-again 45 -SimplWalk "on:/courses/101/assignments/1001,again" -SimplWalkWait 2
  # (1.3.104) a tool Canvas opens in a new window, its one-time launch refused by the mock (as Smartwork's is at the
  # owner's school): Canvas's page for it, its launch sent on in the tool window (newwin_check)
  shoot light-21k-tool-new-window 24 -SimplOpen tool:101:20
  shoot light-05p-calendar-preview 16 -SimplPlace calendar -SimplOpen preview
  shoot light-10b-grades-assignment 12 -SimplPlace grades -SimplPush /courses/101/assignments/1001
  shoot light-10c-feedback 18 -SimplPlace course:101 -SimplPush /courses/101/assignments/1008 -SimplOpen feedback
  shoot light-10d-assignment-submitted 12 -SimplPlace course:101 -SimplPush /courses/101/assignments/1008
  SIZE=1440x900 shoot light-10e-assignment-wide 12 -SimplPlace course:101 -SimplPush /courses/101/assignments/1008
  SIZE=1440x900 shoot light-10f-assignment-wide-quiz 12 -SimplPlace course:101 -SimplPush /courses/101/assignments/1001
  shoot light-32b-rubric-open 16 -SimplPlace course:104 -SimplPush /courses/104/assignments/4001 -SimplOpen rubric:1
  shoot light-19d-quiz-click 18 -SimplOpen quiz:101:9011:take:1 -SimplQuizClick 4
  shoot light-19f-quiz-feedback 24 -SimplOpen quiz:101:9001:feedback
  shoot light-19e-quiz-click-next 18 -SimplOpen quiz:101:9011:take:1 -SimplQuizClick next
  shoot light-35-tools-plain 10 -SimplPlace tools
  # (1.3) themes, To Do's fixed side column, a narrow window, the setup's nicknames
  shoot light-50-theme-dusk 12 -SimplTheme Dusk
  shoot light-50b-theme-ocean-todo 12 -SimplTheme Ocean -SimplPlace todo
  shoot light-50d-theme-photo 12 -SimplThemePhoto "$PWD/scripts/dev/fixtures/photo.jpg"
  # (1.3.164) the same photo with its Ink off: shown as it is, the words on it dark with a white border
  shoot light-50e-theme-photo-plain 12 -SimplThemePhoto "$PWD/scripts/dev/fixtures/photo.jpg" -SimplThemePlain YES
  # (1.3.113) Appearance, from the ⋯ menu: the window darkened but the sidebar and the cards, their photo buttons, the bar
  shoot light-51-appearance-mode 16 -SimplAppearanceMode YES -SimplTheme Ocean
  shoot light-51c-appearance-courses 16 -SimplAppearanceMode YES -SimplAppearanceTab courses
  shoot light-12b-hand-in-preview 30 -SimplPlace course:101 -SimplPush /courses/101/assignments/1012 -SimplOpen handin -SimplHandInPreview "$PWD/docs/screenshots/calendar.png"
  # (1.3.145) a preview scrolled by a posted wheel: the console says whether it reached the preview (SimplScrollProbe)
  shoot light-12e-hand-in-preview-scroll 30 -SimplPlace course:101 -SimplPush /courses/101/assignments/1012 -SimplOpen handin -SimplScrollProbe YES -SimplHandInPreview "$PWD/scripts/dev/fixtures/long-reading.pdf"
  # (1.3.145) several files added at once, as the file chooser hands them back: the hand-in stays up, all of them in
  shoot light-12c-hand-in-several 30 -SimplPlace course:101 -SimplPush /courses/101/assignments/1012 -SimplOpen handin -SimplHandInFiles "$PWD/docs/screenshots/calendar.png|$PWD/docs/screenshots/grades.png|$PWD/docs/screenshots/dashboard.png"
  # (1.3.146) files added one after another, each previewed as it comes (PDF, picture, PDF, picture): the hand-in stays up
  shoot light-12f-hand-in-one-by-one 30 -SimplPlace course:101 -SimplPush /courses/101/assignments/1012 -SimplOpen handin -SimplHandInOneByOne YES -SimplHandInFiles "$PWD/scripts/dev/fixtures/long-reading.pdf|$PWD/docs/screenshots/calendar.png|$PWD/scripts/dev/fixtures/reading.pdf|$PWD/docs/screenshots/grades.png"
  # (1.3.78) Send from Phone: the phone connected and two files in (posted by phone_send); a PDF-only assignment's
  # photos made into one PDF, and a kind it does not take refused (1013 made PDF-only above)
  if [ -z "$PLANNING" ]; then
    printf 'Notes from class, sent from a phone.\n' > "$HOMES/phone-notes.txt"
    printf 'ID3 not really music\n' > "$HOMES/phone-song.mp3"
  fi
  shoot light-12d-hand-in-phone-connected 40 -SimplPlace course:101 -SimplPush /courses/101/assignments/1012 -SimplOpen handin -SimplPhone YES -SimplPhoneTest YES
  phone_send light-12d-hand-in-phone-connected "$PWD/scripts/dev/fixtures/photo.jpg=Photo from phone.jpg" "$HOMES/phone-notes.txt=Notes.txt"
  shoot light-53-whats-new 32 -SimplOpen whatsnew
  shoot light-54-pin-element 14 -SimplPinnedTools "ptable" -SimplPinOpen ptable -SimplPinElement 25 -SimplPTColour category
  # (1.3.52) the Periodic Table: coloured by electronegativity with its trend arrows; an element's page (1.3.80: its
  # shells drawn flat, and Open in Atoms & Orbitals where its 3D view was); (1.3.80) the chemistry tools that were its
  # pages and its element page's 3D view — Lewis Structures, the Molecule Builder and Atoms & Orbitals — each opened as
  # a tool of its own (-SimplTool lewis, molecule, atoms; the old -SimplTool ptable -SimplPTPage … flags open them too)
  SIZE=1440x920 shoot light-55-ptable-trends 14 -SimplPlace tools -SimplTool ptable -SimplPTColour electronegativity
  SIZE=1440x920 shoot light-55b-ptable-element 16 -SimplPlace tools -SimplTool ptable -SimplPTElement 6
  # (1.3.76) the Cloud view — (1.3.80) Atoms & Orbitals': carbon's whole atom, and the cloud model's test vectors on its
  # console
  SIZE=1440x920 shoot light-55k-atoms-cloud 16 -SimplPlace tools -SimplTool atoms -SimplPTElement 6 -SimplPTModel cloud -SimplCloudCheck YES
  # (1.3.80) iron's five 3d orbitals in a row in Atoms & Orbitals, framed to fill their view's width
  SIZE=1440x920 shoot light-55s-atoms-orbitals 16 -SimplPlace tools -SimplTool atoms -SimplPTElement 26 -SimplPTModel orbitals -SimplPTSubshell 3d
  SIZE=1440x920 shoot light-55c-lewis 14 -SimplPlace tools -SimplTool lewis -SimplLewisPreset "NO₃⁻"
  SIZE=1440x920 shoot light-55f-lewis-benzene 14 -SimplPlace tools -SimplTool lewis -SimplLewisPreset Benzene
  SIZE=1440x920 shoot light-55g-lewis-ionic 14 -SimplPlace tools -SimplTool lewis -SimplLewisPreset "NaNO₃"
  SIZE=1440x920 shoot light-55d-molecule 16 -SimplPlace tools -SimplTool molecule -SimplPTMolecule Ethanol
  # (1.3.74) shapes laid out by VSEPR: carbon dioxide straight, water bent
  SIZE=1440x920 shoot light-55k-molecule-co2 16 -SimplPlace tools -SimplTool molecule -SimplPTMolecule "Carbon dioxide"
  SIZE=1440x920 shoot light-55l-molecule-water 16 -SimplPlace tools -SimplTool molecule -SimplPTMolecule Water
  # (1.3.59) the tour — (1.3.80) at its last step, carbon's page with Open in Atoms & Orbitals lit; (1.3.61) and at its
  # first, the table's top rows lit
  SIZE=1440x920 shoot light-55h-ptable-tour 16 -SimplPlace tools -SimplTool ptable -SimplPTTour 4
  SIZE=1440x920 shoot light-55i-ptable-tour-start 16 -SimplPlace tools -SimplTool ptable -SimplPTTour 0
  # (1.3.64) the header's search open, with what it finds
  SIZE=1440x920 shoot light-55j-ptable-search 14 -SimplPlace tools -SimplTool ptable -SimplPTColour category -SimplPTSearch ne
  SIZE=780x700 shoot light-52-narrow-dashboard 14
  MODE=dark
  SIZE=1440x920 shoot dark-55e-ptable-element 16 -SimplPlace tools -SimplTool ptable -SimplPTElement 26
  # (1.3.76) iron's cloud in dark, its 3d chosen: the rest of the atom grey — (1.3.80) in Atoms & Orbitals
  SIZE=1440x920 shoot dark-55l-atoms-cloud 16 -SimplPlace tools -SimplTool atoms -SimplPTElement 26 -SimplPTModel cloud -SimplPTSubshell 3d
  shoot dark-50-theme-forest 12 -SimplTheme Forest
  shoot dark-50c-theme-dusk 12 -SimplTheme Dusk
  shoot dark-50d-theme-photo 12 -SimplThemePhoto "$PWD/scripts/dev/fixtures/photo.jpg"
  shoot dark-50e-theme-photo-plain 12 -SimplThemePhoto "$PWD/scripts/dev/fixtures/photo.jpg" -SimplThemePlain YES
  shoot dark-51-appearance-mode 16 -SimplAppearanceMode YES
  # (1.3.80) Overdue's panel at 1024 points, the first tile of the second row grown over the page under it; Graded's,
  # the last tile, half-way through growing
  SIZE=1024x820 shoot dark-01n-counter-1024-overdue 30 -SimplSheet overdue
  shoot dark-01m-counter-morph-graded 30 -SimplSheet graded -SimplMorphHold 0.4
  SIZE=940x820 shoot dark-40-narrow-dashboard 14
  shoot dark-36-external-tools 16 -SimplPlace externaltools
  shoot dark-36b-external-tools-list 16 -SimplPlace externaltools -SimplExternalToolList knewton # (1.3.77)
  shoot dark-32b-rubric-open 16 -SimplPlace course:104 -SimplPush /courses/104/assignments/4001 -SimplOpen rubric:1
  shoot dark-12e-hand-in-phone-photos 40 -SimplPlace course:101 -SimplPush /courses/101/assignments/1013 -SimplOpen handin -SimplPhone YES -SimplPhoneTest YES
  phone_send dark-12e-hand-in-phone-photos "$PWD/scripts/dev/fixtures/photo.jpg=IMG_0001.jpg" "$PWD/scripts/dev/fixtures/photo.jpg=IMG_0002.jpg" "$HOMES/phone-song.mp3=Song.mp3"
}

# (1.3.80) SHOTS=check: the parts' pictures put together in $OUT (the workflow's last job), what their apps said read
# as one — Send from Phone's files, a post's web view that never measured, how each 3D model was framed, the live checks
if [ "${SHOTS:-full}" = check ]; then
  phone_check
  richtext_check
  frame_check
  live_check
  tool_list_check # (1.3.82)
  again_check || exit 1 # (1.3.102)
  quicklook_check || exit 1 # (1.3.103)
  newwin_check || exit 1 # (1.3.104)
  exit 0
fi

# (1.3.80) the_set: every picture, in the order a part takes its own — walked twice in each part: first to share the
# whole list out (PLANNING: nothing launched, shot-plan.txt written), then to take this part's pictures
the_set() {
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
    # (1.3.134: 24 s, not 16 — on a slow GitHub Mac the hand-in page was still loading at 16)
    shoot "$MODE-12-hand-in" 24 -SimplPlace course:101 -SimplPush /courses/101/assignments/1012 -SimplOpen handin
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
    # (1.3.79: under the full set's load the Dashboard's counts came 24 seconds in, and the panel after them: each taken
    # as soon as it has settled, its wait only the longest)
    shoot "$MODE-01d-dashboard-counter" 30 -SimplSheet next
    shoot "$MODE-01q-counter-preview" 45 -SimplSheet today -SimplOpen preview:counter # (1.3.79: a panel row's card)
    # (1.3.80) the tiles at 1024 points, the sidebar out (three across) and folded away (six); a tile half-way through
    # growing into its panel
    SIZE=1024x820 shoot "$MODE-01k-dashboard-1024" 14
    SIZE=1024x820 shoot "$MODE-01l-dashboard-1024-no-sidebar" 14 -SimplSidebar hidden
    shoot "$MODE-01m-counter-morph" 30 -SimplSheet tomorrow -SimplMorphHold 0.5
    shoot "$MODE-07b-inbox-thread" 18 -SimplPlace inbox -SimplOpen conversation:first
    shoot "$MODE-27-tour" 14 -SimplOpen tour
    shoot "$MODE-30-commands" 14 -SimplOpen palette:/
    # More from Canvas: the sidebar's section, and its History and Help popups
    shoot "$MODE-70-more-sidebar" 20 -SimplSidebarScroll more
    shoot "$MODE-70b-more-history" 24 -SimplOpen more:history -SimplSidebarScroll more
    shoot "$MODE-70c-more-help" 24 -SimplOpen more:help -SimplSidebarScroll more
  done
  bio_full # (1.3.78) the biology toolkit, every tool's pages
  bio_plain_all # (1.3.79) three of them opened plainly, in dark, at two window sizes, the sidebar out and folded
  ui_review # (1.3.78) the UI review's new states
  pins_pages # (1.3.79) the pins' room, a panel pulled out, external tools' pages light
  alta_pages # (1.3.83) Alta's popup: its pop-ups read whole, Light or Dark, Show Browser
  tool_popups # (1.3.79) a tool's popup over the Tools page
  chem_tools # (1.3.80) the Chemistry section and its tools
  chem_calc_tools # (tools) the chemistry calculators
  phys_tools # (tools) the Physics section and its tools
  study_tools # (tools) the study and maths tools
  files_tools # (tools) Compress Files
  planner_tool # (tools) the Study Planner
  verbs_tool # (tools) Verb Conjugations
  # (1.3.78) a PDF-only assignment (1013, made so here) waiting for the phone: Combine photos under the code
  mock_config '{"ext":{"1013":["pdf"]}}'
  MODE=dark shoot dark-12f-hand-in-phone-photos-waiting 30 -SimplPlace course:101 -SimplPush /courses/101/assignments/1013 -SimplOpen handin -SimplPhone YES
  push_set # (1.3.80) what only a push took before
  # (1.2) in light only: more of the redesign, and the narrow window's layouts
  MODE=light
  shoot light-01c-dashboard-activity 14 -SimplDashView activity
  shoot light-02b-courses-past 12 -SimplPlace courses -SimplCoursesFilter past
  shoot light-19b-quiz-matching 18 -SimplOpen quiz:101:9011:take:4
  shoot light-23b-settings-updates 12 -SimplOpen settings -SimplSettingsTab updates
  # (1.3.86) a version put back: Simpl stays on it, said where Install updates automatically was (the hold's key given
  # for this run alone; a development run never installs anything)
  shoot light-23g-settings-updates-held 12 -SimplOpen settings -SimplSettingsTab updates -SimplRolledBackFrom 9.9.9
  shoot light-28-tour-grades 16 -SimplOpen tour:2
  shoot light-29-tour-last 16 -SimplOpen tour:6 # (1.3.78: the tour's last step, Report a Bug at the account menu)
  shoot light-31-command-grades 16 -SimplOpen "palette:/grades "
  shoot light-32-rubric-ring 16 -SimplPlace course:104 -SimplPush /courses/104/assignments/4001 -SimplOpen rubric
  shoot light-33-rubric-grid 16 -SimplPlace course:104 -SimplPush /courses/104/assignments/4001 -SimplOpen rubric:grid
  shoot light-34-files-preview 14 -SimplPlace section:courses/101:files -SimplOpen file:f1
  shoot light-35-tools 12 -SimplPlace tools -SimplPinnedTools "pomo,calc,need,ptable"
  shoot light-37-tool-calc 12 -SimplPlace tools -SimplTool calc
  shoot light-38-tool-ptable 12 -SimplPlace tools -SimplTool ptable -SimplPTColour category
  shoot light-39-tool-need 14 -SimplPlace tools -SimplTool need
  shoot light-70d-more-tool 45 -SimplOpen more:tool # (More from Canvas: My Materials in the tool window — 1.3.134: 45 s, a slow GitHub Mac had it still loading at 30)
  MODE=dark shoot dark-70d-more-tool 45 -SimplOpen more:tool # (1.3.80: the app dark, the school's tool page light)
  SIZE=1280x900 shoot light-35b-tools-biology 12 -SimplPlace tools -SimplToolsScroll biology -SimplBioCheck YES # (1.3.78)
  SIZE=940x820 shoot light-40-narrow-dashboard 14
  SIZE=940x820 shoot light-41-narrow-grades 14 -SimplPlace grades
  SIZE=940x820 shoot light-42-narrow-inbox 12 -SimplPlace inbox
  SIZE=1720x1040 shoot light-43-wide-dashboard 14
  SIZE=1720x1040 shoot light-44-wide-inbox 14 -SimplPlace inbox -SimplOpen conversation:first
  # (modules-file) a module's worksheet opened from its row: the file by its id, in Quick Look over the Modules screen
  shoot light-14d-module-file-open 24 -SimplPlace section:courses/101:modules -SimplOpen modulefile:i16
  # (1.3.103) the file link in an assignment's instructions (Canvas's …/courses/101/files/f1?wrap=1) clicked as a student
  # clicks it, its web view given the keys: the file in Quick Look over the assignment, the panel with it pictured, and
  # what the panel held said on the console (quicklook_check) — in 1.3.102 it came up saying "No items selected"
  shoot light-14e-instructions-file-open 40 -SimplPlace course:104 -SimplPush /courses/104/assignments/4002 -SimplWalk filelink -SimplWalkWait 2
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
  PART=$TIMER_PART shoot light-36-tool-timer 12 -SimplPlace tools -SimplTool pomo -SimplFocusDemo YES -SimplPinnedTools "calc,need"
  PART=$TIMER_PART shoot light-45-dashboard-pins 14 -SimplFocusDemo YES -SimplPinnedTools "calc,need,cite"
  PART=$TIMER_PART shoot light-54f-pin-timer 14 -SimplFocusDemo YES -SimplPinnedTools pomo -SimplPinOpen pomo # (1.3.78: the timer's quick version)
  wait
  bio_pins
  live_shots # (1.3.80: alone, last)
}

# the groups taken alone at the end, each placed on a part before the rest are shared out (the longest first)
TIMER_NAMES="light-36-tool-timer light-45-dashboard-pins light-54f-pin-timer"
alone LIVE_PART one-by-one $LIVE_NAMES
alone WALKS_PART together light-01w-walk light-01v-walk-plain
alone PINS_PART one-by-one $BIO_PIN_NAMES
alone TIMER_PART together $TIMER_NAMES
: > "$OUT_ABS/shot-plan.txt"
PLANNING=yes
the_set
PLANNING=""
for ((k = 0; k < SHARDS; k++)); do
  echo "part $k of $SHARDS: ${COUNTS[k]} pictures, about $(( ${LOADS[k]} / PLAN_JOBS ))s with $PLAN_JOBS copies at once"
done
if [ "${SHOTS:-full}" = plan ]; then exit 0; fi

# every kind of quiz question in the mock's quizzes (matching, blanks, essay, formula, file upload), for the quiz pictures
mock_config '{"richQuestions":true,"moreTypes":true}'
defaults write com.simplcourses.mac ApplePersistenceIgnoreState -bool YES >/dev/null 2>&1 || true
HOMES="$(mktemp -d /tmp/simpl-shots.XXXXXX)"
for i in $(seq 1 "$JOBS"); do mkdir -p "$HOMES/home$i/Library/Preferences"; done

warm_up
the_set
rm -f "$OUT_ABS"/warmup* "$OUT_ABS"/console-warmup*
# (one Mac taking every picture reads its consoles itself; the workflow's parts are read once they are put together)
if [ "$SHARDS" = 1 ]; then
  phone_check
  richtext_check
  frame_check
  live_check
  tool_list_check # (1.3.82)
  again_check || true # (1.3.102)
  quicklook_check || true # (1.3.103)
  newwin_check || true # (1.3.104)
fi
keep_crashes
exit 0
