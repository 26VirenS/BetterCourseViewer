# CI and tests

What GitHub Actions runs, when, about how long it takes, how to read a failure, and how to update the lists the checks
keep. The Mac app, the extension, the iPhone app and the site live on `claude/canvas-safari-ai-extension-5je8k4`;
Simpl for Windows on `claude/windows-liquid-glass` (which carries its own copy of the extension and of these scripts).

## What runs when

| Workflow | Runs on a push that changes | Runner | What it does | Time |
| --- | --- | --- | --- | --- |
| **Tests** (`tests.yml`) | `extension/**`, `scripts/dev/**`, `ios/SimplCourses/Resources/**`, itself; or by hand | ubuntu | every suite of `scripts/dev/test-all.mjs` (28 suites, ~2,350 checks) against the mock Canvas and Brightspace | ~5 min |
| **Mac app** (`mac.yml`) | `mac/**`, the iPhone models the Mac shares, `native-app.js`, `mac-shots.sh`, the checks' scripts and lists, itself | macOS 26 | Models (Swift checks, beside the build); the project as generated; the build (incremental); the pictures (`mac-shots.sh`); kept data sealed; Send from Phone; pictures, consoles and warnings checked; results to the `mac-shots` branch | ~8 min |
| **iOS build** (`ios.yml`) | `ios/**`, the mock (the test school is made from it), its warnings baseline, itself | macOS 26 + ubuntu | the test school as the mock makes it; the app built for the simulator (incremental); no new warnings | ~1 min |
| **iOS screenshots** (`ios-shots.yml`) | `ios/**`, `native-app.js`, `ios-shots.sh`, the checks' scripts and lists, itself | macOS 26 | the app built (incremental), every native screen pictured on the simulator, light and dark; pictures, consoles and warnings checked; results to the `ios-shots` branch | ~17 min |
| **Package** (`package.yml`) | `extension/**`, `scripts/package.sh`, `scripts/chrome-manifest.py`, itself; every `v*` tag | ubuntu | the extension's zips (a tag also publishes) | ~30 s |
| **Site** (`site.yml`) | `site/**`, a release, every hour | ubuntu | `site/` onto the site branch | ~30 s |
| **Windows app (Windows 11 look)** (`windows-fluent.yml`, Windows branch) | `windows/SimplFluent/**`, `windows/SimplFluent.Tests/**`, what the app carries (`extension/**`, the bridge, the sign-in reader), the checks' scripts and lists, itself | Windows + ubuntu | build once; beside it the unit tests and the compiler's warnings; then the pictures in eight parts, the installer tried, the performance walk; the pictures and logs checked; a `[preview]` or Run workflow publishes | ~8 min |

In Tests, Mac app, iOS build, iOS screenshots and the Windows app's push runs, a newer push cancels the run before it
(only the newest is worth waiting for: it holds every commit before it). A run that publishes is never cancelled: the
Windows app's Run workflow and a push asking for a `[preview]` keep groups of their own, and Package, Site and the
Mac's release have no cancelling at all. A push that changes only `docs/` runs nothing. The Windows branch runs its
own Tests workflow on its own copy of the extension and suites (every suite but `test-school`: the iPhone's test school
is not on that branch); its copies of Mac app and iOS screenshots never run there (a run of either would take the Mac
branch's place in its concurrency group and on `mac-shots` or `ios-shots`).

## The checks that fail a run

- **The suites** (Tests): a suite with a failed check fails the run. A suite that fails is run once more at the end,
  alone; one that passes then is reported as passing "only alone, on a retry", with what failed under load — and its
  first log is kept as `<suite>.first.log` in the run's `test-logs` artifact, to find the real cause (a check that
  reads something before it has settled, a port another suite uses…). Nothing is skipped or loosened to get green.
- **Every picture shows something** (`scripts/dev/check-shots.py`; Mac, iPhone, Windows): each picture the run was to
  take must be there (a missing one is an app that did not get there or quit), and must not be blank or nearly one
  colour. Each is measured at 320 pixels wide: the spread of its grey (under 5 fails), the share of its commonest colour
  (over 99% fails) and how many colours cover at least 0.2% of it (under 3 fails). The plainest real screens (a sign-in
  card on an empty window) measure a spread of 9 and 97–98% one colour. Every picture's numbers are in
  `shot-checks.txt` (on the `mac-shots` and `ios-shots` branches, in the Windows checks job's log).
- **The consoles are clean** (`scripts/dev/scan-logs.py`): the apps' consoles and logs (Mac `console-*.txt`, iPhone
  `console-*.txt`, Windows `profile-*.log` and the page's own errors, `*.page-errors.log`) must hold no exception,
  crash or crash report, `[Simpl] error` or a Simpl line that failed, answer not understood, unhandled promise
  rejection or a page's script that threw. Windows' picture runs write the page's own errors down (WebView2's DevTools
  protocol: `Support/PageErrors.cs`); the first ones it caught were the extension's document-start scripts failing on
  every start, since fixed (`Engine/ScriptBundle.cs`). The Mac's and the iPhone's consoles hold what the apps print.
- **No new compiler warnings** (`scripts/dev/check-warnings.py`): the build's warnings in the repository's own sources
  (C# for Windows, Swift and clang for the Mac and the iPhone), known by file and words rather than line, against the
  baseline in `scripts/dev/baselines/`. One that is not there fails, with an annotation on its line.
- **Unit tests**: Windows' models (`windows/SimplFluent.Tests`, xUnit: the chemistry, the calculator, the vault's and
  the saved sign-in's seals, the bridge's JSON, the biology toolkit's data and algorithms against its specification's
  test vectors — `BiologyTests`, its self-check all OK) and the Mac's (`scripts/dev/swift-checks/main.swift`: the
  elements' data, VSEPR's shapes). The Windows pictures' `tools-biology` runs the biology self-check in the app too
  (`--bio-check`), and its log must end with `bio-check summary n/n OK`.
- The checks that were already there stay as they were: the Mac's project as the generator writes it and its What's
  New entry, kept data sealed (Mac, Windows), Send from Phone, the installer tried, the test school as the mock makes it.

## Reading a failure

- The run's **summary and annotations** name each failed check (the Tests workflow lists every failed suite and its
  failed checks; the picture, log and warning checks print one line each, with the file and its numbers).
- **Logs and pictures**: the Tests workflow's `test-logs` artifact (every suite's log, `test-all.txt`); the Mac's and
  the iPhone's latest run on the `mac-shots` and `ios-shots` branches (`git fetch origin mac-shots`: `build-errors.txt`,
  `FAILED`, `console-*.txt`, `shot-times.txt`, `shot-checks.txt`, the pictures); Windows' `windows-fluent-shots`
  artifact (`gh run download <run id> -R 26VirenS/BetterCourseViewer -n windows-fluent-shots -D <empty dir>`).
- The same checks run on a machine of your own: `node scripts/dev/test-all.mjs` (`--only smoke:course,phone`,
  `BCV_NO_SHOTS=1`, `--jobs 4`); `python3 scripts/dev/check-shots.py <pictures> --app mac --report numbers.txt`
  (needs Pillow); `python3 scripts/dev/scan-logs.py --app windows '<dir>/profile-*.log'`;
  `python3 scripts/dev/check-warnings.py build.log --baseline scripts/dev/baselines/windows-warnings.txt`.

## Updating the lists

- **A picture meant to be plain** (an empty state, a spinner by design): add it to `scripts/dev/shot-checks.json` under
  its app, by name (a glob, without `.png`), with the floor it is held to instead (`min_spread`, `max_dominant`,
  `min_colours`) and its `reason`. A picture never taken on purpose: `"missing": true`. Never to make a broken screen
  pass.
- **A harmless log line**: add a regular expression for it to `scripts/dev/log-checks.json` under its app, with its
  `reason` (Windows' "the TPM did not take its key" is there: GitHub's runners have no TPM). An error in the app or
  its pages is fixed, never listed. `scan-logs.py` says which entries matched nothing, so stale ones can go.
- **Fewer warnings**: after fixing some, build from clean and write the baseline again:
  `python3 scripts/dev/check-warnings.py build.log --baseline scripts/dev/baselines/<app>-warnings.txt --write`, and
  commit it. The Mac's builds are incremental (only what changed is compiled, so a known warning in a file not
  compiled this time is not seen): Actions → Mac app → Run workflow → "Build from clean" gives a full build's log
  (`build-tail.log` on `mac-shots`). Windows' build is full every time. A new warning is fixed rather than added; when
  one truly must stay, `--write` after a full build adds it.

## Where the time goes, and what was done about it

Measured from the runs' own step timings (`gh api repos/26VirenS/BetterCourseViewer/actions/runs/<id>/jobs`), on
13 October 2026: before, the average of the last green push runs; after, the runs named.

| Workflow | Before (wall · runner minutes) | After (wall · runner minutes) |
| --- | --- | --- |
| Tests | did not run in CI | 4.8–5.0 · 5 (runs 38383359114, 38386097341: 28 suites, 2,357 checks) |
| iOS screenshots | 30.3 · 29.5 (5 runs; the pictures alone 24.3) | 17.4 · 17.3 (run 38386097591; the pictures 12.8, the build 0.4 instead of 1.8) |
| iOS build | 1.9 · 1.7 (5 runs) | 0.7 · 0.8 (run 38386097329: the build kept) |
| Mac app | 6.8 · 6.1 (6 runs, 76 pictures) | 8.0 · 6.9 (run 38383359174: 83 pictures now, in both looks, and a 160-second walk; the checks add about 12 seconds, Models runs beside the build in under half a minute) |
| Windows app | 4.6 · 17.8 (runs 55–58), then 8.0 · 23.9 with the walk's experiments (runs 60–61) | 8.8 · 26.1 (run 38387662877, every check green: what a release waits for — build, unit tests, pictures, installer, checks — done in 5.3 minutes; the performance walk, which nothing waits for, is the rest) |
| Package | every push to every branch | only a push that changes the extension or its packaging |

- **The extension's suites** ran on no CI at all; now about 5 minutes a push (Playwright's browsers come from the
  cache; one suite a core; each suite's last time orders the next run's longest first).
- **iOS screenshots** spent 30 minutes a run: 24 of them in fixed waits of 10–28 seconds a picture. The app now says
  when it has settled (`-SimplShotSettle YES`: "[Simpl Courses] shot: settled", `ios/SimplCourses/Native/Engine.swift`)
  and each picture is taken then, its wait only the longest (a wait timed by the clock: counted naps ran twice as long
  on a busy runner); the build's intermediates are kept between runs (`scripts/dev/mtime-cache.py`, shared with the
  iOS build workflow). The pictures taken as the app settled match those taken after the old fixed waits (the largest
  mean difference of any picture, 0.3 of 255). The simulator boots after the build, not beside it: its first boot
  takes the runner's three cores, and a build beside it took four times as long. What is left is mostly the simulator's
  boot (about three minutes) and the eleven pictures that wait their whole time (a tool's page, the school's sign-in,
  the launches that settle the simulator after its appearance changes).
- **The Mac app** already took each picture as the app settled (`Shot.swift`, several copies at once) and kept its
  build; its run is mostly the pictures. The Models job runs beside the build on a runner of its own.
- **Windows** already built once, pictured in eight parts at once, settled before each picture (`Support/Shot.cs`)
  and kept NuGet's packages. The unit tests and warnings run beside the build (about as long as it: nothing added);
  the checks read the parts in 14 seconds once the pictures and the installer are done. The installer's try waits for
  the Simpl it updates over to say it is up rather than for fifteen seconds. The longest job is the performance walk
  (about 6 minutes, with its experiments); a release does not wait for it.
- **Package** no longer runs on every push to every branch, only when the extension or its packaging changes.
