# Simpl for Windows — Windows 11 look

The Mac app's screens, steps and layouts (`mac/Simpl`), drawn with Windows 11's own design: Mica, the Settings app's
navigation pane and cards, Segoe UI Variable, Fluent System Icons, WinUI's motion. WPF on .NET 8 with
[WPF-UI](https://github.com/lepoco/wpfui) 4.3. The first Windows app (`windows/Simpl`) is untouched, for rollback.

Build (Windows, or Linux with the targeting pack): `dotnet build windows/SimplFluent/SimplFluent.csproj -c Release -p:EnableWindowsTargeting=true`.
CI (`.github/workflows/windows-fluent.yml`) builds it once (the app, Simpl-Setup.exe and the zip, kept as `release`),
then at the same time pictures it against the mock Canvas in eight parts, tries its installer and walks it for its
timings (`windows-fluent-perf`); the parts come back together as `windows-fluent-shots`. A push takes about five
minutes, and a newer push cancels the run before it (never a run that publishes). A preview (`windows-fluent-v<run>`) is published
when a commit message carries `[preview]`, or by Run workflow, which publishes the green run already made for the
commit (or the run named by `from_run`) as it is, in about a minute. Keep changes to the workflow in commits of their
own: GitHub lets a workflow tag a commit only if the commit itself leaves the workflows alone.

Beside the build, the models' unit tests (`windows/SimplFluent.Tests`, xUnit: the chemistry, the calculator, the
vault's and the saved sign-in's seals, the bridge's JSON — `dotnet test windows/SimplFluent.Tests` on Windows; they
build anywhere with `-p:EnableWindowsTargeting=true`) and the compiler's warnings against the ones already known
(`scripts/dev/baselines/windows-warnings.txt`: a new one fails). After the pictures, every picture each part was to
take is checked to be there and to show something, and every Simpl's log (with the page's own errors, which a
picture's run writes in `page-errors.log`: `Support/PageErrors.cs`) to be free of exceptions and errors. A preview waits
for all of it. `docs/CI.md` says how to read a failure and how to update the lists.

## Installing

Each preview ships two ways: **Simpl-Setup.exe**, the installer, and **Simpl-Windows-x64.zip**, the same folder to
unzip anywhere (portable). Both are code-signed once the repository has a signing account (Signing, below); until
then SmartScreen asks ("More info → Run anyway"). The installer is
Inno Setup 6's (`Installer/Simpl.iss`; Inno Setup 7 compiles it too), compiled by CI right after `dotnet publish`, the
build's version passed in:

```
ISCC /DAppVersion=0.2.0.12 "/DAppDisplayVersion=0.2.0 (v012)" /DSourceDir=<dist\Simpl> /DOutputDir=<dist> windows\SimplFluent\Installer\Simpl.iss
```

What the installer does:

- installs for the current user only, with no administrator prompt (`PrivilegesRequired=lowest`), into
  `%LOCALAPPDATA%\Programs\Simpl` — never Simpl's own data folder, `%LOCALAPPDATA%\Simpl` (settings, grade history,
  the saved sign-in, the web view's data; Data and encryption, below);
- asks one thing — a desktop shortcut too? (unticked) — then installs; the last page has **Open Simpl** ticked;
- makes a Start menu shortcut (and the desktop one), both carrying Simpl's app id `SimplCourses.Simpl`, the one
  `Program.Main` sets first of all (`AppIdentity`), so the window, its taskbar button and the shortcuts are one app and
  pinning pins Simpl; it registers that id's name and picture for notifications (`HKCU\Software\Classes\AppUserModelId`);
- adds Simpl to Settings ▸ Apps (AppId `{B65F70B7-E13E-4CCC-9E1C-0B659660D130}`, per user); uninstalling removes the
  program, its shortcuts and that entry and **keeps** `%LOCALAPPDATA%\Simpl` (the saved sign-in in it, encrypted), and
  says where it is;
- closes a running Simpl first (it hides in the notification area when its window closes): every Simpl holds the mutex
  `SimplCourses.Simpl.App` (the installer's `AppMutex`), and the installer and uninstaller ask it to quit through the
  event `Local\SimplCourses.Simpl.Quit` (`Instance` in `Program.cs`) — an install someone opens asks first — then wait
  for it; Restart Manager (`CloseApplications`) closes anything else holding the files.

Silent install or update, as Simpl's own **Restart to Update** runs it:

```
Simpl-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /LAUNCH
```

`/LAUNCH` (the installer's own switch, read in its `[Code]`) waits for Simpl to have quit and opens it again when the
new version is in place; if the update cannot finish (another Simpl that will not quit — `/SUPPRESSMSGBOXES` answers
Setup's "still open" with Cancel — another Setup running, a file it cannot replace), `DeinitializeSetup` opens the
version that was there, so Simpl is never left closed. Without `/LAUNCH`, nothing is opened. `/DIR="…"` installs
elsewhere; `/LOG="…"` writes a log.
Uninstalling silently: `"%LOCALAPPDATA%\Programs\Simpl\unins000.exe" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART`.

Uninstalling from Simpl (`Support/Uninstall.cs`): Settings ▸ General ▸ This PC ▸ **Uninstall Simpl** asks first (with
"Also erase my settings, grade history and saved sign-in", off), then starts the uninstaller its Settings ▸ Apps entry
names (`UninstallString`; else `unins000.exe` beside Simpl.exe) with `/SILENT /NORESTART` — its progress shows, its own
question and its "removed" message do not, the latter's words about what is kept being wrong after an erase — erases
what Simpl keeps if asked (as Data ▸ Reset everything does) while the uninstaller waits for Simpl to go, and quits for
real. Not `/SUPPRESSMSGBOXES`: it would answer the uninstaller's one remaining box, "Simpl is still open", with Cancel.
A copy run from its zip says it is removed by deleting its folder (Open Folder); a development run has the button off.
`--uninstall yes` does the same as the sheet's Uninstall, keeping what Simpl keeps — for the installed copy only.

Updates (`Support/Updates.cs`, below): a copy the installer put here — its uninstall entry names the folder Simpl.exe
runs from — downloads the new release's Simpl-Setup.exe to `%TEMP%\Simpl-Update\<tag>` and runs it with the arguments
above as Simpl quits: by itself at a quiet moment, or when **Restart to Update** is pressed. A portable copy opens the
release's page, as before. Every Windows 11-look build is published as a GitHub pre-release, so a **beta** is marked
by "beta" in the release's title, not by that flag: with Get beta updates off (the default), the usual previews are
offered. CI tries the installer on every build: it installs silently into a folder of its own,
checks Simpl.exe, the Start menu shortcut (and its app id) and the Settings ▸ Apps entry, opens the installed Simpl
against the mock to picture the Dashboard (`installed-dashboard`; its log must say it knows it was installed); plays an
update that comes by itself through the installed Simpl (below: held while a quiz is open, put in once it closes,
Simpl opened again where it was); runs
the update exactly as Restart to Update does over a Simpl left open (it must take the quit event, Setup must see it
go, and `/LAUNCH` must open Simpl again); runs an update that cannot finish (a stand-in holding Simpl's mutex: Setup
must give up and open the old copy again); uninstalls silently with that Simpl open (the uninstaller must close it),
and checks the folder, the shortcut and the entry are gone and Simpl's data folder is not; then installs again and
uninstalls from Simpl itself (`--uninstall yes`: Simpl must start its uninstaller and quit, and the uninstaller — both
its phases — must finish), checking the same. The installer's and each Simpl's logs are kept with the pictures.

## Updates

Simpl keeps itself up to date as the Mac app does (`mac/Simpl/Support/Updater.swift`), in `Support/Updates.cs` and the
window's side of it, `Shell/UpdateWatch.cs`:

- **When it looks**: at launch when the last look is over an hour old, then every hour (looked at every ten minutes and
  whenever Simpl comes to the front, so a PC asleep does not count its hours) — one request for GitHub's releases of
  this repository, the `windows-fluent-v<run>` ones, compared by run number. **Check for updates automatically** (on)
  turns it off. A development run (the mock, a demo, a picture) never looks.
- **Install updates automatically** (on): a copy the installer put here downloads the newest version's Simpl-Setup.exe
  by itself — picked up where it stopped (an HTTP range), tried again 2, 10, 30 and then 60 minutes after a failure,
  and not on a metered connection (Windows' own word for a phone's hotspot or a capped plan; Download fetches it
  anyway) — and checks it: its size, that it is a program and not a web page, the SHA-256 GitHub publishes for it, and,
  when this copy is Authenticode-signed (Signing, above), that Windows trusts its signature (WinVerifyTrust; revocation
  not looked up, so nothing is asked of anyone) and that it is signed by this copy's own publisher (the certificate's
  subject). An installer kept from before (Simpl quit before it went in) is checked and used rather than fetched again.
- **When it goes in**: at once, unless something is open that an update must not cut off — then it waits, and goes in
  as soon as all of it is closed. What holds it: a quiz, a Knewton Alta or Macmillan assignment, a page of the school's
  in the window (each holds while it is open, the Mac's `UpdateHold`); any dialog over the window (Hand In, the setup,
  a message, a task, a tool opened whole, Sign out?… — all but What's New, which is only there to be read), a dialog in
  Settings, a window of a page of the school's or a tool's (`WebWindow`), Alta's graph; Windows' own Open and Save
  dialogs; the tour; the sign-in while it is on screen; Simpl opening; and, for a moment, a menu or a flyout, the quick
  look, a file on its way. Besides, with Simpl in front it waits for half a minute without a key or a click (the
  student may be reading); behind other windows, five seconds. Going in, the notice at the top of the window says so
  for a moment (a key or a click then, or something opened, and it waits again), where the window is is noted —
  its place and what is opened on it, and whether it was in front, behind (or minimized) or in the notification area —
  what Simpl keeps is written out, and the installer runs with `/LAUNCH` as Simpl quits. The new version opens where
  the student was: in front as it was; minimized, taking nothing from the app in front, when it was behind; in the
  notification area, out of sight, its engine running as ever, when it was there (`updates:resume`, used once, within
  half an hour).
- **What the student sees**: Settings ▸ Updates says where it stands — found, downloading, "Version v132 is ready —
  Simpl updates when you close the quiz, and opens again where you were", installing — with Restart to Update to put it
  in at once, whatever is open (the Mac's Update Now). The notice in the title bar's band (under it on a narrow window),
  over everything in the window, sheets too: "Simpl updates to v132 when you close the quiz." for a few seconds, once for
  each thing it waits for; "Updating Simpl to v132 — it opens again in a moment." as it goes in. Nothing interrupts:
  no question is asked, and the notice takes no clicks.
- **A Critical Update**: a release whose notes carry `<!-- simpl-critical -->` (the workflow's Run workflow ▸ critical
  adds it, with a line saying so, as `scripts/release-mac-native.sh` does for the Mac). Every installed copy older than
  it downloads the newest and puts it in whatever Install updates automatically, the pause or a metered connection say,
  and a failed download is tried again every ten minutes. Unlike the Mac's, it still waits for what is open — a quiz,
  a hand-in, a dialog — though only five seconds for the student's own typing; its notice stays while it waits
  ("Critical Update v132 installs when you close the quiz."). Turning Check for updates automatically off stops every
  look, a Critical Update's too: the student asked Simpl not to look.
- **Paused** (the Mac's Developer ▸ Pause updates): `Simpl.exe --pause-updates on` (with Simpl quit; `off` again) keeps
  the hourly look but installs nothing by itself, a Critical Update apart. Settings ▸ Updates says so.
- **A copy run from its zip** cannot put a version in its own place: it says one is available, and Download opens its
  page. Install updates automatically is off there, and says why.

`simpl.log`'s `update:` lines say each step: found, downloaded and checked (or why not), `held (quiz)`, `no longer held
(by quiz)`, `where it was: grades, the window hidden`, `installing …, and quitting`; and, opened again, `back after
updating to v132`, `opened again at grades`.

**Trying it** (a development run only): `--update-test <file>` plays an update through against a local file — the
next run number is found at once, its installer that file (its SHA-256 the file's; `--update-test-sha <hex>` to try a
mismatch), downloaded into the profile's `Update\` and checked, held and put in as above: the file is run as the
installer, so give it a Simpl-Setup.exe. `--update-test-installed` acts as a copy the installer put here,
`--update-test-critical` as a Critical Update, `--update-test-hold quiz` holds as an open quiz would (for Settings'
pictures), `--update-test-close <s>` closes what is open over the window after that long, and `--update-test-tray`
closes the window to the notification area a moment after it opens. With `--shot` nothing is ever run: the notice of
an update going in stays for the picture. CI pictures Settings ▸ Updates (`settings-updates-waiting`, `-dark`, `-zip`)
and the notice (`update-waiting-quiz`, `update-critical-quiz`, `update-restarting`) this way, with the build's own
Simpl.exe as the file, and plays one through for real in the installer job (2b): the installed Simpl with a quiz open
and its window in the notification area, the installer just made as the next version — every step logged in its
order, the installer run and Simpl opened again, then that profile opened again: at the Grades, out of sight, its
engine running.

## Signing

CI signs with **Azure Artifact Signing** (Microsoft's signing service, about $9.99 a month; an individual must be in
the US or Canada) as soon as the repository has its six Actions **variables** — none is a secret: GitHub signs in to
Azure with OIDC, so no password or key is kept anywhere. Without them, nothing is signed and nothing fails.

| Variable | What it is |
| --- | --- |
| `SIGNING_ENDPOINT` | the account's region endpoint, e.g. `https://eus.codesigning.azure.net` (East US) |
| `SIGNING_ACCOUNT` | the Artifact Signing account's name |
| `SIGNING_PROFILE` | its certificate profile's name (Public Trust) |
| `AZURE_CLIENT_ID` | the app registration's Application (client) ID |
| `AZURE_TENANT_ID` | the Directory (tenant) ID |
| `AZURE_SUBSCRIPTION_ID` | the subscription the account is in |

The app registration has a federated credential for GitHub Actions — repository `26VirenS/BetterCourseViewer`, entity
Branch, `claude/windows-liquid-glass` (pushes and Run workflow both run there) — and the **Artifact Signing
Certificate Profile Signer** role on the signing account.

What is signed: the build job signs every `.exe` and `.dll` in the published folder that nobody signed (Simpl's own,
WPF-UI's, PDFsharp's; .NET's and WebView2's are Microsoft's already), so the zip and the installer carry them signed;
the installer job has Inno Setup sign `Simpl-Setup.exe` and its uninstaller (`ISCC /DSign /Ssimpl=…`, `SignTool` in
`Installer/Simpl.iss`). Both check the signatures, and the log says who signed. Each signature counts against the
account's monthly 5,000 (about a dozen a run).

A signature names the publisher at once (Settings ▸ Apps, UAC, the download's properties) and lets Smart App Control
run Simpl; SmartScreen's warning on a new download fades as the signed releases build their reputation.

## Data and encryption

What Simpl keeps of the student's in `%LOCALAPPDATA%\Simpl` is encrypted with Windows' own protection — DPAPI, its
key in this PC's TPM — and opened with nothing to unlock as Simpl starts. The saved sign-in's password is locked with
**Windows Hello** besides: Windows asks for the student's face, fingerprint or PIN before Simpl can type it in.

- **Encrypted** (`Support/Vault.cs`): the preferences (`prefs.json`), the extension's storage (`storage.json`:
  settings, grade history, goals), the answers kept for a quick start (`Answers\`), the flashcards (`flashcards.json`)
  and the saved sign-in (`login.bin`; no longer Windows' Credential Manager). Each is AES-256-GCM, its file beginning
  `SIMPLENC`, bound to what it is (one file cannot be passed off as another); an answer's file and its school's folder
  are named by an HMAC under the key, so the names say nothing either.
- **The key**: 32 random bytes made the first time, kept in `vault.key` wrapped twice: by an RSA-2048 key in this PC's
  TPM that cannot be exported (the Platform Crypto Provider, this user's keys, `SimplCourses.Simpl.Vault`; a
  `--profile` has one of its own), RSA-OAEP SHA-256 (SHA-1 on a TPM that refuses it); then by Windows for this user
  (DPAPI). A PC or VM with no TPM keeps the DPAPI wrap alone. `Program.Main` starts unwrapping it first of all, on a
  worker (the TPM takes 50–300 ms). A new vault is kept by DPAPI alone for the seconds the TPM takes to make its key,
  then by both. Settings ▸ Data ▸ Encryption says which holds it; simpl.log's `vault:` lines say what happened.
- **The saved sign-in** (`Support/LoginVault.cs`, `Support/Hello.cs`, `Support/LoginRecord.cs`): `login.bin`, sealed
  by the vault, holds its facts — the sign-in page's host, the username, when it was saved, how its password is locked
  — so Settings and the sign-in know there is one, and whose, without a prompt. Where Windows Hello is set up (a PIN at
  least: `KeyCredentialManager.IsSupportedAsync`), **Stay Logged In** has Windows make a key for Simpl in this user's
  Windows Hello container (`KeyCredentialManager.RequestCreateAsync`, `SimplCourses.Simpl.SignIn`, a `--profile`'s
  its own; in the TPM where there is one, never exportable), which signs only after the face, fingerprint or PIN. It
  signs 32 random bytes kept in the record (the challenge) twice: the same signature both times (RSA PKCS#1 v1.5 is),
  its SHA-256 is the AES-256-GCM key the password is sealed with, bound to the host and username; only that sealed
  password is kept. When the school's sign-in page comes up, Simpl finds the key (`OpenAsync`), asks it to sign the
  challenge (`RequestSignAsync`: Windows asks, under "Logging you in", which says so), opens the password with the
  key, fills it in and sends it; the key, the signature and the bytes they passed through are zeroed after.
  (KeePassXC's Windows Hello quick unlock works the same way.) Windows' prompt can open behind the window of an app
  without a package: Simpl looks for it (`Credential Dialog Xaml Host`) three times 150 ms apart and once more 600 ms
  on, and brings it forward.
  - **No Windows Hello** (no PIN set up), Windows refusing, or two signatures that differ: the password stays in the
    vault, as 0.2.7 kept it. Settings ▸ General says so, with how to set Windows Hello up and a button to Windows'
    Sign-in options; Settings ▸ General's **Lock with Windows Hello** locks it once Windows Hello is there.
  - **Cancelled**: at Stay Logged In, kept in the vault, not locked (Lock with Windows Hello does it later); at the
    sign-in page, nothing is filled — the school's page shows, to sign in by hand.
  - **Windows Hello's key gone** (Windows Hello reset, the PIN removed): what it locked can never be opened, so the
    saved sign-in is forgotten — one line in simpl.log — Simpl's card comes up with the username in it, and the next
    Stay Logged In keeps it again.
  - **From 0.2.7** (the password in the vault only), on a PC with Windows Hello: locked at its next use, just after
    Simpl has signed in with it (one prompt, "Lock your saved sign-in with Windows Hello"); cancelled, not asked again.
  - **Forget, Sign out, Reset Everything and Uninstall's erase** delete Windows Hello's key too (`DeleteAsync`).
- **Not encrypted, on purpose**: simpl.log (for a bug report; no password or username is written to it, the
  screenshot suite's `--sign-in` included); what the student saves where they choose (exports, CSVs, converted files);
  files opened in another app or previewed (Windows' temporary folder, as they are, for the app that opens them); the
  photos chosen for the backgrounds and the themes' drawings; `--perf-walk` and `--shot` output; and `WebView2\`,
  Chromium's own profile (the school's site data and cache), whose cookies Chromium encrypts itself with DPAPI.
- **From an earlier Simpl**: a plain file is read once and written over sealed (the preferences and the storage within
  a second of starting, the flashcards as it starts); the answers kept in plain text are deleted (only a cache); a
  sign-in in Credential Manager (`Simpl Courses/sign-in`) is moved into `login.bin` and deleted there (then locked
  with Windows Hello at its next use, as 0.2.7's). The first Windows app (`windows/Simpl`) shares the folder and cannot
  read sealed files: going back to it starts it afresh.
- **A key that cannot be opened** (the TPM cleared, the folder copied to another PC or user): one line in simpl.log,
  `vault.key` and what it sealed moved to `Unreadable-<date>-<time>\` (not deleted: put back, they open again if the
  key ever does), the answers deleted, and a new vault made. Simpl never stops or asks.
- **Erasing**: Reset Everything and Uninstall's "Also erase" delete `vault.key`, the TPM's key, Windows Hello's key,
  `login.bin`, anything set aside and the rest, so what was sealed can never be opened again, wherever a copy went.
  Reset Everything keeps the flashcard sets (as the Mac does), sealed again under a new vault; Uninstall's erase
  deletes them too.

What the vault protects is data at rest: the files copied or backed up, read by another Windows account or an
administrator browsing the disk, or a lost disk. A program running as the same Windows user, while Simpl is
installed, can still ask the TPM and DPAPI to unwrap the vault's key just as Simpl does — and so read the preferences,
grade history and the sign-in's username without a sound. Windows Hello is what stops it from silently unlocking the
sign-in's password: that needs Windows Hello's key to sign, and Windows asks the student first, on screen. The limit,
honestly: what Simpl reads, something able to look inside Simpl while it runs can read too — an administrator (or
malware with an administrator's rights) debugging a running Simpl sees the password the moment Simpl has unlocked it
to type it in, as it sees anything else Simpl has open. Windows Hello guards the sign-in from other programs, not from
a PC that is already compromised. Nothing else is behind a prompt, by design: Simpl opens with nothing to unlock.

A development run (the mock, a demo, a picture) keeps the sign-in in memory only and never asks Windows Hello
anything — CI's runners have none. `--hello-state locked` or `--hello-state none` (a development run only) shows
Settings as a PC with Windows Hello would, the sign-in locked with it, or as one without: the pictures
`settings-signin-hello`, `settings-signin-nohello` and `settings-data-hello`.

CI checks it on every push: one part of the pictures (part 1, which signs in with the mock's login) fails if any
profile's `prefs.json`, `storage.json`, `flashcards.json`, `login.bin` or kept answer does not begin with `SIMPLENC`,
or if any file in a profile, WebView2's included, holds the mock's sign-in in plain text.

## Performance

Every simpl.log (`%LOCALAPPDATA%\Simpl`, a student's too) says how Simpl runs, in lines that begin `perf:`
(`Support/Perf.cs`). Once, when the first answers are in (or after a minute), how the start went, in ms from the
process's start — `main`, the `window` drawn, the `kept shell` (the app's screens on what was kept, before the page is
asked for), the `webview` started, the page asked for (`nav`) and come (`page`), the counts live (`engine ready`), the
`first screen` built and drawn, its `first data` in and shown — and what it runs on: the render tier (0 is software:
no graphics WPF can use), Windows' animation effects, Windows and the processor against Simpl's own (`emulated`: an x64
Simpl on an ARM PC), the processors, memory, the display's scale, the PC's maker and model (a VM says so) and WebView2.
Then at two minutes, every ten and at exit, four lines: the engine's calls by name (how many, the median, the 95th
percentile, the longest; most time first), the screens switched to (drawn, then their answers shown), the window's
stalls (input kept waiting over 200 ms, probed twice a second while the window shows) and memory
(`Support/PerfMemory.cs`: Simpl's process and every process it started — WebView2's browser, renderers, GPU process,
utilities — each one's private bytes, working set and private working set, read in one call to Windows; and .NET's
heap).

`--perf-walk <folder>` walks one fixed route once the first answers are in — To Do, the Calendar, Grades, the Inbox, a
course, an assignment, Notifications, the New task sheet, Settings, Tools, a search typed a letter at a time, the
window dragged and resized, the Dashboard again — writes `perf.json` and `perf.txt` there (and the table to
simpl.log), and quits. Each step: drawn (`ui`), its answers in and shown (`data`), the builds and calls it took, the
longest stall, the frames in its first 700 ms and the longest gap between them, the memory it allocated. Memory is
read four times — `settled` (the start done: nothing asked of the page for two seconds), `walked` (the same, after
the walk), `idle` (left alone sixteen seconds) and `minimized` (the window minimized for `--perf-minimized` seconds,
30; 0 leaves it out) — then the window is brought back and timed (`Restore`). `--perf-name warm` names the files `perf-warm.*`; `--perf-course` and
`--perf-assignment` choose what is opened (else the first course, and the first assignment To Do lists);
`--perf-timeout` (240 s) ends a walk that cannot finish, writing what it has; a first setup still to answer stops it,
with a note. The walk only looks: New task and Settings are opened and closed, nothing saved.

On a student's own PC: quit Simpl from the notification area (right-click its icon, Quit), then from Command Prompt
(a copy from the zip: its own Simpl.exe):

```
"%LOCALAPPDATA%\Programs\Simpl\Simpl.exe" --perf-walk "%USERPROFILE%\Desktop"
```

`perf.txt` lands on the desktop; its first two lines say what the PC is, which decides most of the rest.

A development run also says, as each screen comes in, how long it took and how: `perf: open Grades 12 ms (cached)` —
`kept` (its page was kept: nothing built), `cached` (built at once from the answers kept), `read` (nothing kept: it
waited for the page). The walk notes the same beside each step; its `Grades (again)` comes back to a screen whose page
was let go, so it is built from what was kept.

Memory given back (`Support/MemoryTidy.cs`): left alone ten seconds — nothing pressed or typed in the window, nothing
asked of the page, a good deal made and thrown away since — .NET collects and hands its free memory back to Windows
(once a minute at most). Minimized or in the notification area for twenty seconds: the same; the page (and the
graphing calculator's) asked to keep to little memory (`MemoryUsageTargetLevel` Low — WebView2 then hands its own
processes' memory back); and Windows takes what Simpl is not using out of RAM (its working set). Never sooner, so a
window brought straight back has everything where it was; back in view, the page has its pace again at once. One
browser (`Engine.WebEnvironment`) serves every web view, with its network service in its own process
(`NetworkServiceInProcess2`: one process fewer). `--no-tidy` (any run) leaves memory as .NET and WebView2 keep it.

CI's Performance walk, beside the pictures, does the same against the mock twice with one profile — `cold`, then
`warm` (the answers kept) — prints both tables in its log and the run's summary, keeps them as `windows-fluent-perf`
and with the pictures, and compares each with the last green run's (before, after, the change: the timings, all the
calls' time, and memory at each of its moments — all of it, then Simpl's and WebView2's). For now it also walks the
warm profile with `--no-prefetch`, against the warm walk, and once under dotnet-trace (`windows-fluent-trace`, kept a
week: the sampled stacks, as speedscope reads them). It only reports: a
slower number never fails a run, and nothing waits for it but the pictures' merge.

## Layout of the code

| Folder | What is there |
| --- | --- |
| `Engine/` | The school's site in WebView2 as the app's engine (carried over): `Engine.Call<T>(name, args)` asks the page (the extension's `BCVNative.call`), navigation (`Go`, `Push`, `Open`, `OpenWeb`, `GoBack`), the sign-in assist. |
| `Models/` | The page's answers, generated from the Swift models (`windows/tools/models.sh`). |
| `Kit/` | Windows 11 pieces in code: `K` (text ramp, icons, cards, settings rows, chips, rings, bars, avatars, load and empty states, frozen colour brushes), `B` (theme brush keys), `Motion` (WinUI curves and transitions, the pages' motion), `Ahead` (a screen ready before the click), `SelectorBar`, `CheckCircle`, `Heading`, `Sheet`/`SheetHost` (dialogs), `Glyph` (symbols for the school's things), `Styles.xaml` (card, subtle and row buttons). |
| `Shell/` | The window (`MainWindow`), the Settings-style shell (`AppShell`, its pages and their motion `AppShellPages`, reading ahead `Prefetch`, `NavPane`), Settings, Setup, What's New, the tour (`Tour`), keeping Simpl on the taskbar (`Taskbar`: asked once when the tour is followed to its end, and Settings ▸ General's Pin to taskbar…), dialogs, the tray, the web window, the window's side of updates (`UpdateWatch`: what holds one back, where the window is, the notice). `Shell/Search/`: the search box's suggestions and commands (`SearchPalette`, Ctrl+K). |
| `SignIn/` | Finding the school, confirming it, the sign-in card, "Logging you in", the handoff bar. |
| `Screens/` | One class per screen, built on `Screen`; `ScreenFactory` says which screen shows a place or a route. A folder per area: `Todo/`, `Calendar/` (with Notifications), `Grades/`, `Inbox/` (with Groups and New Message), `Course/` (All Courses, a course's home, its sections, pages, files), `Assignment/` (with Hand In and its Send from Phone, feedback, the rubric), `More/` (Search, External Tools, Tools, a discussion), `Quiz/`, `Alta/` (the Alta and Macmillan popups), `Preview/` (the quick look). |
| `Tools/` | `ToolHost.cs` (the registry, the title bar's pins and their flyouts, a tool opened whole over the window, the pieces tools are made of), then a folder per family: `Everyday/` (Focus timer, Calculator, Unit converter, Grade needed), `Study/` (Citations, Flashcards), `Files/` (File converter, Merge & split PDFs, PDF annotator, Image to text, Graphing calculator), `Elements/` (the Periodic Table), `Chemistry/` (its Lewis maker and molecule builder). |

## A screen

```csharp
public sealed class TodoScreen : Screen
{
    private readonly Loader<TodoData> data;
    public TodoScreen(Engine.Engine engine) : base(engine) { data = Watch(new Loader<TodoData>()); }
    public override string Title => "To Do";
    public override FrameworkElement? Commands => K.Button("New task", () => engine.AskNewTask(), icon: SymbolRegular.Add24);
    public override void Refresh() => data.Ensure(engine, "todo");          // read (kept answer first, then the page's)
    protected override FrameworkElement Build()                             // built again as answers come
    {
        if (data.Data is not { } d) return K.LoadState(data.Error, () => data.Reload(engine, "todo"));
        return K.V(28, ...);
    }
}
```

- The shell gives the page its title (a breadcrumb when something is pushed), the commands at the title's right, a
  scroll viewer and the padding; `Build` returns only the content. `Width` is the content's width; override
  `LayoutChanges` to be built again when a width threshold is crossed.
- Colours are theme resources (`B.Text`, `B.Text2`, `B.Card`, `B.Accent` …), set with `.Fg()`, `.Bg()`, `.Stroke()`, so
  light, dark and the accent follow Settings. A course's own colour comes from the page (`K.Hex(row.Color)`).
- Type follows Windows 11's ramp (`Ty.Caption` 12, `Ty.Body` 14, `Ty.BodyStrong`, `Ty.Subtitle` 20, `Ty.Title` 28).
  Section headings on a page are `Heading.Section(title, trailing, action)`; groups of settings-like rows use
  `K.SectionHeader` and `K.Row`.
- Surfaces: `K.Card` (8 radius; 4 for Settings rows), `K.CardButton`, `K.Subtle` for rows in a list. Lists sit in a card
  with `K.Divider` between rows. Dialogs are `Sheet`s shown with `Dialogs.Show(sheet, window)`.
- Motion: the shell brings the page in (below, Pages); a screen animates only what changes on it at the student's
  word — `Motion.Enter` / `Stagger` for a regrouped list or a filter's cards, `SlideIn` for steps, `DrillIn` for
  something opened over it — never its content as it first shows. Everything respects Windows' "Animation effects"
  and is off in the screenshot suite.
- Something that leads to another screen (a row, a card) gets it ready ahead of the click:
  `Ahead.On(element, build => engine.PrepareWeb(url, build))` (or `engine.Prepare(place, build)`).
- A screen whose `Refresh` does more than read overrides `ReadAhead` (what the prefetch asks of it); one that listens to
  something outside itself undoes it in `Release`; one with something of the student's in it (a reply being written)
  says `Busy`.

## Pages

`Shell/AppShellPages.cs`: the page area is a deck of pages, each kept screen's page with its own scroller (so each
keeps where it was scrolled), the one showing on top and the rest folded away — not laid out, not drawn — until shown
again. A screen is built at once as it is shown (`Show(now: true)`), from the answers kept when there are some, so the
page comes in with what it shows; one with nothing read yet keeps the page before it up while its first answer comes
(300 ms at most), never coming in empty to fill in after.

One motion, here as on the Mac and in the extension: going deeper (a piece of work opened) slides the page in from
32 px to the right as it fades in, the one before drifting 16 px left as it fades out; Back is the reverse; another
place in the pane crossfades, the page lifting 8 px; the title fades in with it and the pane's mark slides to its row
in the same time and curve — 220 ms, decelerating hard (`Motion.Out`, CSS's `cubic-bezier(0.23, 1, 0.32, 1)`). Only
the pages' transforms and opacity move (never their layout), each drawn once into a picture of itself while it moves
(`BitmapCache`, at the display's scale, where the graphics card draws), so every frame is drawn; a move interrupted
by another carries on from where the pages are, nothing queued. A page holding a window of its own (a web view) cannot
move or fade, so it changes at once. Windows' "Animation effects" off: pages simply change.

What is kept, against memory: the Dashboard's page always (its cards stay loaded; coming back to it is instant, and
its answers read again in the background — a while after they came — update its cards in place); of the rest, the
last three screens shown keep their pages; any screen with something of the student's in it is kept. Every other is
let go — page, tree and screen — and made again when next shown, at once, from the answers kept in memory (the last
few megabytes of them, `AnswerCache`) or sealed on disk. Answers are read again (stale while they are) once they are
two minutes old, or when something changes them (a tick, a hand-in: `DataVersion`); one the page gave less than 90
seconds ago, nothing changed since, is taken as it is.

Reading ahead (`Shell/Prefetch.cs`): once the start has settled — the counts live, nothing asked of the page for a
moment — the root places (To Do, the Calendar, Grades, Notifications, the Inbox, All Courses) and each course's home
(the first twelve) are read, one screen at a time (two or three reads at once at most), a breath between, waiting
whenever the student's own screens are asking: the school is asked at the pace of a student clicking through, not all
at once. A pointer resting 90 ms on a pane's row, a course's card or a row of work has its screen read; pressed, the
screen is built and laid out too, unseen (`Kit/Ahead.cs`), so the click only has to bring it in. `--no-prefetch` (any
run) reads nothing ahead.
- Never `using` both `Wpf.Ui.Controls` and `System.Windows.Controls` without aliasing the clashes (`Button`,
  `TextBlock`, `TextBox`, `Image`, `MenuItem`). Inside a `Screen`, the engine is the field `engine`.

## The quick look

A row of work (the Dashboard's, To Do's, the Calendar's, a counter's panel) grows a card out of itself with
`WorkPreview.Press(item, row)`: build the `PreviewItem` with `WorkPreview.Dash`, `Work` or `Sheet`, or by hand (the
Calendar's `CalendarPeek`). The card reads the work's details as it opens, Mark done ticks it, a double-click or a
Ctrl-click opens the work whole, and a click round it, Escape or Close folds it back into its row.

## Send from Phone

Hand In's *Send from Phone…* (beside Choose Files…, for an assignment that takes files) opens a panel over the form
with a QR code (`Screens/Assignment/PhoneHandIn.cs`). The phone's own camera opens it: a page this PC serves itself
(`Assets/PhoneHandIn.html`, an embedded resource — the same file as the Mac app's), with the assignment, its course and
the kinds of file it takes, *Take Photo* and *Choose Files*, each file's progress and Retry, then "Sent to your PC —
finish handing in there." Each file comes into the hand-in as if chosen with Choose Files… (its type checked, converted
where it must be); the student looks it over and presses Submit on the PC — nothing is handed in by itself.

- **No server anywhere.** `Support/PhoneServer.cs` is a small web server on plain sockets (`TcpListener`, not
  `HttpListener`, which needs an address an administrator reserves) on this PC's private Wi-Fi or Ethernet address
  (`PhoneLan.Best()`: never a VPN's, a virtual switch's or Bluetooth's) and a port Windows picks. It is up only while the
  panel is open: it stops when the panel closes, when the hand-in is sent or cancelled, and after 15 minutes with nothing
  from the phone. The address holds a 128-bit one-time key, compared in constant time; every other path is 404; only
  private addresses may connect; a file is at most 50 MB (Hand In's own limit) and a session 500 MB; names are made safe
  (no folders, nothing Windows forbids, not CON or NUL); what came waits in `%TEMP%\SimplPhone-…`, deleted with the
  hand-in. The page is served with a strict Content-Security-Policy and asks for nothing but its uploads.
- **Reachability.** The panel says where this PC is ("This PC is on Wi-Fi at 192.168.1.23") and that the phone must be
  on the same Wi-Fi — some school networks keep devices apart; a phone's hotspot works. With no such address: "Connect
  this computer to Wi-Fi to use your phone." The first time, before it listens, the panel says Windows will ask whether
  Simpl may use the network (choose Allow) — `phoneAsked` in prefs.json remembers it was said.
- **Photos into one PDF.** For an assignment that takes PDFs and not pictures, *Combine photos into one PDF*: each photo
  turned upright (its EXIF orientation), no larger than 2200 pixels, a JPEG page each in the order they came (WPF's own
  codecs, off the window's thread), on Letter (or A4 where the region is metric), landscape for a wide photo (`PhotoPdf`,
  PDFsharp), as one PDF named for the assignment, replaced in the hand-in as each photo comes.
- **The code** is QRCoder's (MIT, correction M) module matrix drawn by `PhoneQrView` in one `OnRender` on one clock
  (`CompositionTarget.Rendering`, only while it arrives): softly rounded modules, dark on white even in dark mode, four
  modules of quiet space. The panel grows from the button (96% to full, fading in, 240 ms, cubic-bezier(0.23, 1, 0.32,
  1)); the finder squares settle first (from 85%), then the data ripples out from the centre (each module from 60% over
  120 ms, all in place within 420 ms, never from nothing); with Windows' animation effects off, a 150 ms fade. "Waiting for
  your phone…" has a breathing dot, never the code. When the phone opens the page the code flies into the corner as
  "Phone connected" and the files arrive in a list with their pictures and progress.
- **CI**: `windows-fluent.yml` pictures the panel (`hand-in-phone`, `-dark`, `-first`), and two parts run
  `--phone-test` (the server on 127.0.0.1 too, its address in simpl.log): PowerShell opens the page, tries a wrong key,
  posts files with `Invoke-WebRequest` as the phone's page does, and the log must say each is in the hand-in
  (`hand-in-phone-connected`; `hand-in-phone-photos`: a PDF-only assignment's two photos made one PDF, an mp3 refused).

The Chrome and Safari extensions and the iPhone app do not have it: an extension cannot take a connection.

## A tool

A tool registers itself from its own file, so a tool added is a file added:

```csharp
internal static class CalculatorRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => ToolRegistry.Register("calc", c => new CalculatorTool(c), c => new CalculatorCompact(c));
}
```

The whole tool opens over the window (`ToolPopup`), from its tile on the Tools page, a pin's flyout or a command
alike; the quick version is the pin's flyout. `ToolContext` hands it the engine, what
was handed over (`TakeFiles`, `TakeInfo`), and `OpenFull` / `Close` for the flyout. `ToolKit` has the page, two
columns, fields, notes and the drop zone; `Files` has Windows' file dialogs.

## Icons

WPF-UI 4.3 draws only the Fluent icons whose code is at most U+FFFF; the rest show as a stray character. Check a new
`SymbolRegular` against that before using it.
