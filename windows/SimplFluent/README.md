# Simpl for Windows — Windows 11 look

The Mac app's screens, steps and layouts (`mac/Simpl`), drawn with Windows 11's own design: Mica, the Settings app's
navigation pane and cards, Segoe UI Variable, Fluent System Icons, WinUI's motion. WPF on .NET 8 with
[WPF-UI](https://github.com/lepoco/wpfui) 4.3. The first Windows app (`windows/Simpl`) is untouched, for rollback.

Build (Windows, or Linux with the targeting pack): `dotnet build windows/SimplFluent/SimplFluent.csproj -c Release -p:EnableWindowsTargeting=true`.
CI (`.github/workflows/windows-fluent.yml`) builds it once (the app, Simpl-Setup.exe and the zip, kept as `release`),
then at the same time pictures it against the mock Canvas in eight parts, tries its installer and walks it for its
timings (`windows-fluent-perf`); the parts come back together as `windows-fluent-shots`. A push takes about five
minutes, and a newer push cancels the run before it (never a run that publishes). A preview (`windows-fluent-v<run>`),
which every installed Simpl takes, is published only by Run workflow with beta off: it publishes the green run already
made for the commit (or the run named by `from_run`, its id) as it is, in about a minute; Run workflow ▸ beta
publishes it as a beta instead (`windows-fluent-beta-v<run>`: Updates ▸ Betas, below), and Run workflow ▸ promote
makes a beta the usual release. A commit whose subject line carries `[preview]` is published by its push, always as a
beta. Keep changes to the workflow in commits of their own: GitHub lets a workflow tag a commit only if the commit
itself leaves the workflows alone.

Beside the build, the models' unit tests (`windows/SimplFluent.Tests`, xUnit: the chemistry, the calculator, the
vault's and the saved sign-in's seals, when Windows Hello is asked for the saved password and how the run holds it,
the bridge's JSON, which published version an update takes — `dotnet test windows/SimplFluent.Tests` on Windows; they
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
by its tag, `windows-fluent-beta-v<run>` (or "beta" in the release's title), not by that flag: with Get beta updates
off (the default), the usual previews are offered. CI tries the installer on every build: it installs silently into a folder of its own,
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
  this repository (the first 100 it lists, compressed: the Mac's share the list; 0.3.0 and earlier read 40), the
  usual releases' `windows-fluent-v<run>` and the betas' `windows-fluent-beta-v<run>`, compared
  by run number (`Support/UpdatesChoice.cs`: the newest newer than this copy that it may take — a beta only with Get
  beta updates on, Betas below). **Check for updates automatically** (on) turns it off. A development run (the mock,
  a demo, a picture) never looks.
- **Installing by itself** (always, 0.3.7: Install updates automatically is no longer a setting, and one turned off
  in 0.3.6 or earlier is let go of at the first start, `updates:install` removed): a copy the installer put here
  downloads the newest version's Simpl-Setup.exe by itself — picked up where it stopped (an HTTP range), tried again
  2, 10, 30 and then 60 minutes after a failure, and not on a metered connection (Windows' own word for a phone's hotspot or a capped plan; Download fetches it
  anyway) — and checks it: its size, that it is a program and not a web page, the SHA-256 GitHub publishes for it, and,
  when this copy is Authenticode-signed (Signing, above), that Windows trusts its signature (WinVerifyTrust; revocation
  not looked up, so nothing is asked of anyone) and that it is signed by this copy's own publisher (the certificate's
  subject). An installer kept from before (Simpl quit before it went in) is checked and used rather than fetched again.
- **When it goes in**: at once, unless something is open that an update must not cut off — then it waits, and goes in
  as soon as all of it is closed. What holds it: a quiz, a Knewton Alta or Macmillan assignment, a page of the school's
  in the window (each holds while it is open, the Mac's `UpdateHold`); any dialog over the window (the setup, Sign out?…)
  or popup floating over it (Hand In, a message, a task, a tool opened whole… — all but What's New, History and Help,
  which are only there to be read), a dialog in Settings, a window of a page of the school's or a tool's (`WebWindow`), Alta's graph; Windows' own Open and Save
  dialogs; the tour; the sign-in while it is on screen; Simpl opening; and, for a moment, a menu or a flyout, the quick
  look, a file on its way. Besides, with Simpl in front it waits for half a minute without a key or a click (the
  student may be reading); behind other windows, five seconds. Going in, the notice at the top of the window says so
  for a moment (a key or a click then, or something opened, and it waits again), where the window is is noted —
  its place and what is opened on it, and whether it was in front, behind (or minimized) or in the notification area —
  what Simpl keeps is written out, and the installer runs with `/LAUNCH` as Simpl quits. The new version opens where
  the student was: in front as it was; minimized, taking nothing from the app in front, when it was behind; in the
  notification area, out of sight, its engine running as ever, when it was there (`updates:resume`, used once, within
  half an hour).
- **What the student sees**: Settings ▸ Updates says where it stands — found ("Version v133 (beta) is available" for a
  beta), downloading, "Version v132 is ready —
  Simpl updates when you close the quiz, and opens again where you were", installing — with Restart to update to put it
  in at once, whatever is open (the Mac's Update Now). The notice in the title bar's band (under it on a narrow window),
  over everything in the window, sheets too: "Simpl updates to v132 when you close the quiz." for a few seconds, once for
  each thing it waits for; "Updating Simpl to v132 — it opens again in a moment." as it goes in. Nothing interrupts:
  no question is asked, and the notice takes no clicks.
- **A Critical Update**: a release whose notes carry `<!-- simpl-critical -->` (the workflow's Run workflow ▸ critical
  adds it, with a line saying so, as `scripts/release-mac-native.sh` does for the Mac). Every installed copy older than
  it downloads the newest and puts it in whatever the pause, an earlier version put back or a metered connection say,
  and a failed download is tried again every ten minutes. Unlike the Mac's, it still waits for what is open — a quiz,
  a hand-in, a dialog — though only five seconds for the student's own typing; its notice stays while it waits
  ("Critical Update v132 installs when you close the quiz."). Turning Check for updates automatically off stops every
  look, a Critical Update's too: the student asked Simpl not to look. A usual release's Critical Update reaches every
  copy; a beta's (Run workflow ▸ beta and critical) only the copies with Get beta updates on.
- **Betas** (**Get beta updates**, off; the Mac's beta feed): a beta is published under a tag of its own,
  `windows-fluent-beta-v<run>` (Run workflow ▸ beta; titled "Simpl for Windows 0.3.1 beta (v105)"), and offered only
  to a copy with Get beta updates on; a release under the usual tag whose title says "beta" counts as one too. A copy
  taking betas takes the newest of all, so a usual release newer than a beta takes over from it. Turned off, a beta
  found and not yet in is let go, and a copy on a beta stays on it — never back to an older version — until a usual
  release newer than it comes. Simpl 0.3.0 and earlier list `windows-fluent-v<run>` alone (and took any 0.x version
  there, a beta too): a beta's own tag keeps it from them. The same run published both ways (a beta promoted) is
  listed once, as the usual release.
- **Paused** (the Mac's Developer ▸ Pause updates): `Simpl.exe --pause-updates on` (with Simpl quit; `off` again) keeps
  the hourly look but installs nothing by itself, a Critical Update apart. Settings ▸ Updates says so.
- **A copy run from its zip** cannot put a version in its own place: it says one is available, and Download opens its
  page. Settings ▸ Updates says why (Installing updates, where the toggle was in 0.3.6).
- **Earlier versions** (Settings ▸ Updates, folded away as the Mac's are; `--settings-earlier yes` opens it for a
  picture): the last twelve, each with its date ("Oct 11"; the year only when it is not this one) and size. For the copy
  the installer put here each has Install… (the Mac's), which asks first, then downloads that version's
  Simpl-Setup.exe, checks it as an update's is and runs it the same way (`Updates.InstallEarlier`). A copy run from its
  zip has Open…, the version's page.
- **Staying on an earlier version** (0.3.7, in place of Install… turning Install updates automatically off): before
  the earlier version goes in, Simpl notes the run it goes back from (`updates:staying`, with the other settings,
  encrypted as they are; the higher of it and one noted before, when it goes back twice). That version and those before
  it are not fetched or put in by themselves — or the newest would go straight back in at the next quiet moment — and
  a version newer than it is, as every update is; a Critical Update goes in all the same. Settings ▸ Updates says so
  where the toggle was ("Staying on 0.3.5 (v130)": v132 doesn't install by itself, a version newer than v132 does), and
  the card's button is **Update now** in place of Download: the version downloaded, the stay let go of (even if the
  download fails, which is then tried again as any update's), and put in at once. The stay is let go of too once
  Simpl is that version or newer (Simpl-Setup.exe run by hand). `update: v132 found, kept back: staying back from
  v132` in `simpl.log`; `--update-test-staying` plays an update through as a copy put back from the version it finds
  (`settings-updates-staying`).

`simpl.log`'s `update:` lines say each step: found, downloaded and checked (or why not), `held (quiz)`, `no longer held
(by quiz)`, `where it was: grades, the window hidden`, `installing …, and quitting`; and, opened again, `back after
updating to v132`, `opened again at grades`.

**Trying it** (a development run only): `--update-test <file>` plays an update through against a local file — the
next run number is found at once, its installer that file (its SHA-256 the file's; `--update-test-sha <hex>` to try a
mismatch), downloaded into the profile's `Update\` and checked, held and put in as above: the file is run as the
installer, so give it a Simpl-Setup.exe. `--update-test-installed` acts as a copy the installer put here,
`--update-test-critical` as a Critical Update, `--update-test-hold quiz` holds as an open quiz would (for Settings'
pictures), `--update-test-staying` as a copy staying on an earlier version put back from the one it finds,
`--update-test-earlier yes` lists this version and the one before it under Earlier versions (their dates,
sizes and Install…), `--update-test-close <s>` closes what is open over the window after that long, and `--update-test-tray`
closes the window to the notification area a moment after it opens. With `--shot` nothing is ever run: the notice of
an update going in stays for the picture. CI pictures Settings ▸ Updates (`settings-updates-waiting`, `-dark`, `-zip`,
`-staying`) and the notice (`update-waiting-quiz`, `update-critical-quiz`, `update-restarting`) this way, with the build's own
Simpl.exe as the file, and plays one through for real in the installer job (2b): the installed Simpl with a quiz open
and its window in the notification area, the installer just made as the next version — every step logged in its
order, the installer run and Simpl opened again, then that profile opened again: at the Grades, out of sight, its
engine running.

**Publishing a beta, and promoting it.** A version for testers only goes out as a beta, from the commit's green run:
`gh workflow run windows-fluent.yml --ref claude/windows-liquid-glass -f preview=true -f reuse=true -f beta=true`
(`-f from_run=<id>` for another green run — its id, the long number in its link, not the v-number; `-f critical=true`
makes it a Critical Update for beta testers alone). It is a GitHub pre-release, `windows-fluent-beta-v<run>`, titled
with the version the commit's `SimplFluent.csproj` gives (bump it first), its notes RELEASE_NOTES.md's with a line
first saying how to get it; the website's /download/windows (`scripts/publish-site.sh`, newest `windows-fluent-v<run>`
with a Simpl-Setup.exe) stays on the newest usual release. A push whose commit's subject line carries `[preview]`
publishes a beta too, never the usual release. **A tester gets the first beta** by downloading Simpl-Setup.exe from
the newest `windows-fluent-beta-v…` release on GitHub and opening it (it installs over the Simpl there, settings and
sign-in kept), then turning on **Settings ▸ Updates ▸ Get beta updates**, so each next beta comes by itself.
**To promote a beta** to everyone, give its tag:
`gh workflow run windows-fluent.yml --ref claude/windows-liquid-glass -f preview=true -f promote=windows-fluent-beta-v<run>`
(beta off, no from_run). It downloads the Simpl-Setup.exe and zip that beta's release carries and publishes them as
`windows-fluent-v<run>` at the commit the beta was made from — byte for byte what the testers had, however long ago its
run was: every copy older than it takes it, 0.3.0 too, and the testers already on that run stay as they are. A tag
that is not a beta's, or a beta missing either file, stops the run, as a `from_run` that is not this workflow's run
does: neither ever builds the branch in its place. RELEASE_NOTES.md is the notes either way: write it for the version
being published before Run workflow. Simpl 0.3.0 and earlier read only the first 40 releases GitHub lists (the Mac's
and the extension's among them, in GitHub's own order, not quite that of publishing; 0.3.1 and later read 100): the
workflow warns when a usual release it has published is not among them, and a newer commit's run published as the
usual release then reaches those copies. **A bad beta**: deleting its release does not move the testers who have it —
Simpl never goes back by itself. Publish a newer run with the fix (or the change reverted), as a beta or the usual
release, and they take it; a tester can also go back by hand under Settings ▸ Updates ▸ Earlier versions ▸ Install….

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
WPF-UI's, PDFsharp's, QRCoder's, WPF-Math's; .NET's and WebView2's are Microsoft's already), so the zip and the installer carry them signed;
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
  its own; in the TPM where there is one, never exportable; one already there from an earlier save is used instead,
  `OpenAsync`), which signs only after the face, fingerprint or PIN. It signs 32 random bytes kept in the record (the
  challenge) once — RSA PKCS#1 v1.5 gives the same signature every time, and were a PC's ever to differ, the first
  unlock could not open the password and the sign-in would be forgotten (below) — and that signature's SHA-256 is the
  AES-256-GCM key the password is sealed with, bound to the host and username; only that sealed password is kept. So
  saving asks at most twice — making the key, then signing (no request of KeyCredentialManager's does both; Windows may
  let the second stand) — and once where Simpl's key is already there. When the school's sign-in page comes up, Simpl
  finds the key (`OpenAsync`), asks it to sign the challenge (`RequestSignAsync`: Windows asks, under "Logging you in",
  which says so), opens the password with the key, fills it in and sends it; the key, the signature and the bytes they
  passed through are zeroed after. (KeePassXC's Windows Hello quick unlock works the same way.) Windows' prompt can
  open behind the window of an app without a package: Simpl looks for it (`Credential Dialog Xaml Host`) three times
  150 ms apart and once more 600 ms on, and brings it forward.
  - **Once a run** (`Support/UnlockGate.cs`, `Support/RunSecret.cs`): Windows Hello is asked for the password at most
    once per run of Simpl, however many ask at once — one request for the whole app, every sign-in waiting on it given
    its answer. What it gives (or the password just typed at Stay Logged In, or just locked at its use) is held for the
    rest of the run: its bytes in an array the garbage collector never moves, encrypted there for Simpl's process alone
    (`CryptProtectMemory`, `SAME_PROCESS`), opened only for the moment it is typed in; and zeroed when the sign-in is
    kept anew or forgotten, on Sign out and Reset Everything, when the school refuses it (a changed password), and
    when Settings locks it. simpl.log says when a sign-in used it (`sign-in: opened without asking Windows Hello
    again`), never the password.
  - **No Windows Hello** (no PIN set up), or Windows refusing: the password stays in the vault, as 0.2.7 kept it.
    Settings ▸ General says so, with how to set Windows Hello up and a button to Windows' Sign-in options; Settings ▸
    General's **Lock with Windows Hello** locks it once Windows Hello is there.
  - **Cancelled**: at Stay Logged In, kept in the vault, not locked (Lock with Windows Hello does it later); at the
    sign-in page, nothing is filled — the school's page shows, to sign in by hand — and Windows Hello is not asked
    again by itself that run, until Start Over, a sign-in by hand (Simpl's card or the school's page) or Settings.
  - **Windows Hello's key gone** (Windows Hello reset, the PIN removed), or giving a key the password does not open
    with: what it locked can never be opened, so the saved sign-in is forgotten — one line in simpl.log — Simpl's card
    comes up with the username in it, and the next Stay Logged In keeps it again.
  - **From 0.2.7** (the password in the vault only), on a PC with Windows Hello: locked at its next use, just after
    Simpl has signed in with it ("Lock your saved sign-in with Windows Hello": as Stay Logged In asks, and nothing more
    that run); cancelled, not asked again.
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
malware with an administrator's rights) debugging a running Simpl can read the password once Simpl has unlocked it,
as it can anything else Simpl has open: held for the rest of the run, it is encrypted for Simpl's process alone, which
keeps it out of a memory dump and the page file but not from code running inside Simpl. Windows Hello guards the
sign-in from other programs, not from a PC that is already compromised. Nothing else is behind a prompt, by design:
Simpl opens with nothing to unlock.

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

Resources looked up (`Kit/FlatResources.cs`): WPF-UI keeps its control styles in 91 merged dictionaries, and every
style or theme colour WPF looked up for an element searched them one after another — again for each element each time
the part of a page it is in was added to another (pages are built from the bottom up). A trace of the walk put a fifth
of the window's busy time there (half the walk's busy time went once they were one). They are copied into one
dictionary, in WPF-UI's order, a few milliseconds at a time while the window is idle at start (making every style
took 170 ms in one go, and the window waited for it), and the copy put in WPF-UI's place once whole — or, on a first
start, finished at once just before the app's pages are made on the page's answers; the theme's own
dictionary stays apart (WPF-UI swaps it), and the accent is written over all of it as before. `--no-flatten` leaves
them as they were. A screen let go is taken out of the window once nothing else is happening, not while the next page
comes in.

Memory given back (`Support/MemoryTidy.cs`): left alone ten seconds — nothing pressed or typed in the window, nothing
asked of the page, a good deal made and thrown away since — .NET collects and hands its free memory back to Windows
(once a minute at most). Minimized or in the notification area for twenty seconds: the same; the page (and the
graphing calculator's) asked to keep to little memory (`MemoryUsageTargetLevel` Low — WebView2 then hands its own
processes' memory back); and Windows takes what Simpl is not using out of RAM (its working set). Never sooner, so a
window brought straight back has everything where it was; back in view, the page has its pace again at once. One
browser (`Engine.WebEnvironment`) serves every web view, with its network service in its own process
(`NetworkServiceInProcess2`: one process fewer). `--no-tidy` (any run) leaves memory as .NET and WebView2 keep it.

Pictures and files (`Kit/Decoding.cs`): a picture in a page or a course's files is decoded no wider than 2,400 pixels
(a phone's photo, decoded whole, is 48 MB), off the window's thread, and drawn at the size it would have had. A PDF in
a course's files is drawn a page at a time as it comes near what the pane shows and let go again once four heights of
the pane away, its place kept the same size meanwhile, rather than all sixty of its first pages at once.

CI's Performance walk, beside the pictures, does the same against the mock twice with one profile — `cold`, then
`warm` (the answers kept) — prints both tables in its log and the run's summary, keeps them as `windows-fluent-perf`
and with the pictures, and compares each with the last green run's (before, after, the change: the timings, all the
calls' time, and memory at each of its moments — all of it, then Simpl's and WebView2's). Runners differ more than
most changes do (their processors, their caches: .NET keeps far more memory on one with a very large cache), so that
run's build is walked here too, on the same runner, cold then warm, each just before this build's walk — the first
comparison, then the one against that run's own numbers. A commit message with `[perf-base <run id>]` names the run to
compare with instead (its build is kept 30 days). For now it also walks once under dotnet-trace
(`windows-fluent-trace`, kept a week: the sampled stacks, as speedscope reads them). It only reports: a slower number
never fails a run, and nothing waits for it but the pictures' merge.

## Layout of the code

| Folder | What is there |
| --- | --- |
| `Engine/` | The school's site in WebView2 as the app's engine (carried over): `Engine.Call<T>(name, args)` asks the page (the extension's `BCVNative.call`), navigation (`Go`, `Push`, `Open`, `OpenWeb`, `GoBack`), the sign-in assist. |
| `Models/` | The page's answers, generated from the Swift models (`windows/tools/models.sh`). Since 2.99.33 the page also writes the figures, dates and course names it sends (`pctText`, `gpaText`, `goalLine`, `dueLine`, `altName`, `gradedText`…): show those strings as they come rather than formatting the numbers here, so Windows reads every grade, date and course exactly as the Mac does. |
| `Kit/` | Windows 11 pieces in code: `K` (text ramp, icons, cards, settings rows, chips, rings, bars, avatars, load and empty states, frozen colour brushes), `B` (theme brush keys), `Motion` (WinUI curves and transitions, the pages' motion), `Ahead` (a screen ready before the click), `Decoding` (pictures decoded at the size shown), `FlatResources` (WPF-UI's styles in one dictionary), `SelectorBar`, `CheckCircle`, `Heading`, `Sheet`/`SheetHost` (dialogs and popups), `Drag` (a popup moved by its heading), `Glyph` (symbols for the school's things), `RichText` (Canvas's own HTML as Windows text), `Formula` (Canvas's formulas typeset from their TeX with WPF-Math, MIT, drawn in the text's colour; written out as plain maths where they cannot be), `Styles.xaml` (card, subtle and row buttons). |
| `Shell/` | The window (`MainWindow`), the Settings-style shell (`AppShell`, its pages and their motion `AppShellPages`, reading ahead `Prefetch`, `NavPane`, what the school added to Canvas's own navigation `MoreFromCanvas`, the screen on
show checked against the school while the window shows `LiveCheck`), the popups floating over the window (`FloatPopup`, Popups below), Settings, Setup, What's New, the tour (`Tour`), keeping Simpl on the taskbar (`Taskbar`: asked once, the third time Simpl is opened, at a calm moment, and from Settings ▸ General's Pin to taskbar…), dialogs, the tray, the web window, a place or a page in a window of its own (`PlaceWindow`: a pane row's or a list row's Open in new window, Shift+click on a pane row, a page's ⋯ ▸ Open in new window, or Ctrl+Shift+O for what shows), the window's side of updates (`UpdateWatch`: what holds one back, where the window is, the notice). `Shell/Search/`: the search box's suggestions and commands (`SearchPalette`, Ctrl+K). |
| `SignIn/` | Finding the school, confirming it, the sign-in card, "Logging you in", the handoff bar. |
| `Screens/` | One class per screen, built on `Screen`; `ScreenFactory` says which screen shows a place or a route. A folder per area: `Todo/`, `Calendar/` (with Notifications), `Grades/`, `Inbox/` (with Groups and New Message), `Course/` (All Courses, a course's home, its sections, pages, files), `Assignment/` (with Hand In and its Send from Phone, feedback, the rubric), `More/` (Search, External Tools, Tools, a discussion), `Quiz/`, `Alta/` (the Alta and Macmillan popups), `Preview/` (the quick look). |
| `Tools/` | `ToolHost.cs` (the registry, the title bar's pins, their flyouts and pulled-out windows, a tool opened whole in its popup, the pieces tools are made of), then a folder per family: `Everyday/` (Focus timer, Calculator, Unit converter, Grade needed, Word Counter, Statistics, Matrix Calculator; `Model/` holds their counting and sums with no window in them, tested in `SimplFluent.Tests`), `Study/` (Citations, Flashcards), `Files/` (File converter, Merge & split PDFs, PDF annotator, Image to text, Compress files, Graphing calculator), `Elements/` (the Periodic Table, Atoms & Orbitals), `Chemistry/` (the Chemistry section's tools: `ChemTools.cs`, Lewis Structures, the Molecule Builder, and the calculators — the Equation Balancer, Molar Mass & Stoichiometry, Gas Laws — with their sums in `Chemistry/Model/`, plain .NET), `Physics/` (the Physics section's tools: `PhysTools.cs`, Kinematics & Projectiles, Vectors; their maths in `Model/`, free of WPF), `Biology/` (the biology toolkit's ten tools on their shared foundation: data, 3D, labels, step-through; its own README). |

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

- The shell gives the page its title (a breadcrumb when something is pushed, and the screen's own `Before` parts ahead
  of its title: a course's Grades reads "F26-MATH 021 20 › Grades", as Windows 11's Settings names where a page is — on
  one line, as WinUI's BreadcrumbBar keeps it: steps that do not fit fold into "…" from the first, `Shell/CrumbLine.cs`),
  the
  commands at the title's right, a scroll viewer and the padding; `Build` returns only the content. `Width` is the content's width; override
  `LayoutChanges` to be built again when a width threshold is crossed. `WindowWidth` is the whole window's (the pane
  and the page together), for a layout told by the window rather than by the page left beside the pane, with
  `WindowLayoutChanges` for its thresholds. The Dashboard's six counters are cards as tall as their words (0.2.9's, the
  Mac's 1.3.77), never square: the symbol and name at the top, the number, and the line about it whole on up to two
  lines — its parts on lines of their own — or left out, the tooltip saying it; the cards of a row as tall as its
  tallest. Six to a row where six cards each have room for their symbol beside their name (a page about 870 wide, a
  1280 window's; their symbol 24 and their padding 12 there, 28 and 14 otherwise), else three to a row, two on a page
  under 520 (`DashboardScreen.CounterLayout`); the full names where every card has room for them, else the short ones.
  Each card wears its counter's colour — the one its symbol wears: Due today Windows' blue, Tomorrow orange, Next 7
  days green, Overdue red, Unread orange, Graded purple — washed across the whole card inside it, with a faint edge of
  the colour (a little stronger in dark, lighter in light, so the number and its words stay easy to read; over a
  theme's picture too, which shows through). Overdue wears its red wash with nothing late too (`--overdue-none` draws
  it so, for the `dashboard-overdue-none` pictures: the mock always has something late); with something late, the
  alert instead — a stronger red wash ringed in red (in dark a deeper red, so the card is redder without lifting it
  towards the red number, which reads on it at about 4:1) — and its number in red.
- Colours are theme resources (`B.Text`, `B.Text2`, `B.Card`, `B.Accent` …), set with `.Fg()`, `.Bg()`, `.Stroke()`, so
  light, dark and the accent follow Settings. A course's own colour comes from the page (`K.Hex(row.Color)`). In the
  light theme `B.Text2` and `B.Text3` are solid greys (#5F5F5F, #707070; `Appearance.ReadableText`): Fluent's
  see-through blacks drew paler than they ask (about 3.2 to 1 on the window) once WPF had drawn them.
- Type follows Windows 11's ramp (`Ty.Caption` 12, `Ty.Body` 14, `Ty.BodyStrong`, `Ty.Subtitle` 20, `Ty.Title` 28).
  Section headings on a page are `Heading.Section(title, trailing, action)`; groups of settings-like rows use
  `K.SectionHeader` and `K.Row`.
- Surfaces: `K.Card` (8 radius; 4 for Settings rows), `K.CardButton`, `K.Subtle` for rows in a list. Lists sit in a card
  with `K.Divider` between rows. Dialogs are `Sheet`s shown with `Dialogs.Show(sheet, window)`; a popup is a `Sheet`
  with `Floats` (Popups, below).
- Motion: the shell brings the page in (below, Pages); a screen animates only what changes on it at the student's
  word — `Motion.Enter` / `Stagger` for a regrouped list or a filter's cards, `SlideIn` for steps, `DrillIn` for
  something opened over it — never its content as it first shows. Everything respects Windows' "Animation effects"
  and is off in the screenshot suite.
- Something that leads to another screen (a row, a card) gets it ready ahead of the click:
  `Ahead.On(element, build => engine.PrepareWeb(url, build))` (or `engine.Prepare(place, build)`).
- A screen whose `Refresh` does more than read overrides `ReadAhead` (what the prefetch asks of it); one that listens to
  something outside itself undoes it in `Release`; one with something of the student's in it (a reply being written)
  says `Busy`.
- A screen of a course or a group says its colour (`TitleColor`): the shell draws a square in it before the title, as
  the Mac's headings have (a section's comes from `CoursePart`, the course's colour in the pane). A screen whose title
  is often long (a discussion, a page, an assignment) lets it wrap (`TitleLines`), the commands staying at the top
  right; cut, its whole words are its tooltip. The line under the title never repeats a part the breadcrumb already
  says (`AppShell.WithoutCrumbs`: "Discussion", not "Discussion · F26-MATH 021 20" under "F26-MATH 021 20 ›").
- Words on a course's own colour (a card's band, the pane's letter squares) are `K.InkOn(color)`: white where white
  reads 3:1 on it, else near-black — students pick their colours, and many are light. A course's second name is shown
  only when it says something new (the engine's `altName`, or `CourseKit.SaysMore`, its comparison); a list of work
  draws a chip only for news (Missing, Late, Submitted, a score — never "Not submitted"); what opens in a Simpl window
  of its own ends in `CourseKit.OwnWindowMark()` (the window's symbol), never ↗. All Courses' filter reads Current ·
  Past · Future, the line under its title the engine's `subs` for the one chosen (`--courses-filter past` opens on
  Past, for the pictures).
- In the school's rich text (`Kit/RichText.cs`), a video, a sound or another page set in it (an iframe — Canvas's media
  player —, a video, an object) is drawn as what it is — a video as a 16:9 frame with its name ("Video player for
  jump.mp4" → "jump.mp4") and a play button, a sound or a page as a row — and played or opened in a window of its own;
  a web view in the text would float over the page as it scrolls, so none is played in place.
- A tool, or a page of the school's or another site's, opens in a window of its own (`Shell/WebWindow.cs`, the Mac's
  ToolWindow): a course's tool, a tool assignment or a module's tool by the school's one-time launch (the engine's
  `toolLaunch`) — the tool alone, its school page only as the fallback —; Back, Forward and Refresh at the title bar's
  left (Alt+←, Alt+→, F5), More at its right (Open in browser, Copy link), a thin line under the title bar while a page
  loads, and why it could not open in the window with Try again. The page is in light, whatever Simpl's appearance or
  Windows' (`LightPages`: the page and its frames are told the student prefers light, through the browser's DevTools
  protocol, per web view — the profile's preference is shared with the school's pages in the app, which follow the
  app; a frame in a process of its own is held at its start until it has been told, so a script that reads the
  colour scheme as it starts reads light); Macmillan Learning's popup the same. A window the page opens (window.open,
  a link to a new window, a sign-in's popup) opens as another of these windows, in light, the page that opened it still
  its opener — left to the web view, it was a bare browser window that took Windows' dark. `--open tool:101:9` opens one
  at launch (the screenshot suite pictures the window in front; `--web-scheme dark` in a picture run gives its profile
  Windows' dark preference, and the window writes "web window: the page prefers light" in simpl.log as each page comes
  in, and "web window: a frame prefers light" for each of its frames); `--open tool:101:19` opens the mock school's
  tool whose page, and the other site's page it frames, each have a dark look of their own.

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
few megabytes of them, `AnswerCache`) or sealed on disk. Answers are read again when something changes them (a tick, a
hand-in: `DataVersion`); a screen shown again with what it shows a minute old is checked at once (Live checks, below:
the main window's screens and a place in a window of its own — a screen made only to read ahead reads again once its
answers are a minute old, as before); one the page gave less than 60 seconds ago, nothing changed since, is taken as it
is.

Reading ahead (`Shell/Prefetch.cs`): once the start has settled — the counts live, nothing asked of the page for a
moment — the root places (To Do, the Calendar, Grades, Notifications, the Inbox, All Courses) and each course's home
(the first twelve) are read, one screen at a time (two or three reads at once at most), a breath between, waiting
whenever the student's own screens are asking: the school is asked at the pace of a student clicking through, not all
at once. A pointer resting 90 ms on a pane's row, a course's card or a row of work has its screen read; pressed, the
screen is built and laid out too, unseen (`Kit/Ahead.cs`), so the click only has to bring it in. `--no-prefetch` (any
run) reads nothing ahead.

These, and the kept answers above, are the app's own. The extension in the engine's page does none of its own: since
2.99.33 it reads ahead (at idle, under a resting pointer, Chrome's speculation rules), keeps the answers a page opens
with between page loads, and moves between its screens with view transitions only where no app is around it — which it
knows by `BCVBridge.native`. `bridge.js` sets that because `Engine/ScriptBundle.cs` hands it a stand-in for WebKit's
message handler (the local `webkit`), exactly as the Mac's and the iPhone's web views have the real one; so nothing is
read twice over (the app's reading ahead and the page's), the page keeps no more of the school's answers in the
extension's storage than it did before (the courses' assignments, for the Dashboard's counts), and a web screen takes
no picture of the one before it to move from: it arrives by its own entrance (the app shows each one in place, so a
220 ms fade on the house ease-out, where it rose before), and the app's pages make the move between them.

The engine's side of live checks (Simpl Courses 2.99.35, `native-app.js`): a call asked with `maxAge` (seconds) reads
again every Canvas answer it is made from that is older than that, and nothing younger — the age is the call's own, so
two calls under way at once keep theirs. Who is signed in, the course colours and the help links are not read again for
it. With `known` (the `hash` of the answer on screen) a call answers `{ unchanged: true, hash }` when nothing changed,
else `{ unchanged: false, hash, value }`; the same data always makes the same answer. `BCVNative.stats().reads` counts
the reads the page has made, for a check's log line. What a course holds (its record, tabs, sections, people, groups,
folders) is kept 10 minutes at most, as its modules, pages and files already were. Against the mock school only,
`liveDevChange` (`done`, `grade`, `task`) changes the school behind the engine's back, for CI to see a check find it.

## Live checks

A screen left open keeps up with the school by itself (`Shell/LiveCheck.cs`, its rules in `Support/LiveRules.cs` with
their tests; the Mac's are the same):

- **When.** While the window shows (visible, not minimized, not hidden in the notification area) and someone can be
  at it (the PC not locked, its display on: `Support/Presence.cs`), the screen on show is checked once what it shows is
  a minute old — a course's pages (its home, a section, a page, an assignment, a discussion, its grades:
  `Screen.CoursePage`) once it is ten minutes old, as the engine keeps their data ten minutes at most. Nothing while
  the window is out of sight or nobody is at it (the memory given back then stays given back: the timer stops too, and
  `MemoryTidy` gives memory back on a locked PC as on a minimized window), offline, or while the page is not ready;
  back after more than a minute away, at once. A screen shown again with what it shows a minute old is checked at once
  (the shell pokes the check as it shows one). One check at a time for the whole app: one due while another still runs
  is skipped. A failure waits a minute, then two, four, five at most; a success starts over. A place in a window of its
  own (`PlaceWindow`) is checked the same way, by its own window; the pane's counts only by the main window's.
- **Read only.** A check writes nothing to the school unless the student is at the window (one of Simpl's windows in
  front, the PC unlocked: `Engine.LiveSeen`, sent as `seen`): only then does a check of an open conversation mark a
  new message read, or of an open discussion a new reply (the engine reads a conversation for a check with
  `auto_mark_as_read=false`). A message that comes while nobody is looking stays unread, and the Inbox's badge says so.
- **What.** Each of the screen's loaders (`Screen.LiveReads`: what `Watch` and `Checked` registered, with something on
  screen and nothing on its way) asks its call again with the engine's `maxAge` — the screen's interval, a course
  page's for what is made of every course's own data (`coursesProgress`, `externalTools`) wherever it shows — and
  `known`, the hash of the answer on screen (none yet after a
  read: the answer's text is compared with the one shown instead); the pane's counts (`snapshot`) go with it. Never the
  arguments that write a preference (`dashList`'s `hideDone`, `todo`'s `group` and `showDone`), never the Dashboard's
  copies kept from its last visit (`kept: true`), and of the Dashboard's views only the one on show.
- **Nothing new: nothing.** An answer the same as the one on screen changes nothing at all — no build, no flicker, the
  scroll and the keyboard where they were; it only says when the screen's data was last the school's. An answer the
  screen asked for itself (To Do regrouped, a module's item ticked) is taken with its text (`Loader.Take`), so the next
  check finds it the same.
- **Something new: only that.** The loader takes the answer (`Loader.LastLive` while it tells its watchers) and the
  screen shows it (`Screen.LiveApplied`): built again once, as for any answer, the scroll kept and the keyboard given
  back to the same row or button in the new build (found by its kind and its name) — or, on the Dashboard,
  part by part and never whole (`Screens/DashboardLive.cs`): a counter's number rolls to its new value (a short
  ease-out, 8 px; none under Animation effects off), its line and its tooltip with it, its face drawn again only when
  late work comes or goes; a course's card drawn again in its place; a row of a list (Today, a day of Coming up, the
  activity) drawn again where it was, a new one unfolding into its place, a gone one folding away before the rows under
  it close up (200 ms each), the rows that did not change untouched (by their ids: planner ids, course ids, stream ids);
  the rail's grades and week load drawn again on their own. A part whose shape changed (a counter more, a list emptied)
  is built again alone, in its place. A row or a card drawn again that had the keyboard gives it to its new self; a row
  in or out above where the page is scrolled to leaves what the student is looking at where it was (the page scrolled
  by as much). The kept Dashboard does not check while another place shows; shown again a minute later, it checks at
  once. Grades checks the course beside its list only while there is room for it.
- **Waits.** No check, and nothing it found shown, while: a quiz, an Alta or a Macmillan assignment is open; the student
  is typing (the keyboard in a text box — the pane's search box only while keys go into it, and a click on the page
  takes the keyboard out of it — or in a web view with a key or a click in the last five seconds, or a key in the last
  five seconds anywhere); What-If is on; Hand In (or Send from Phone) is open; a dialog, a popup over this window or
  the main one (not a tool's, `Sheet.Beside`: it is used beside the page; nor one that holds no update — What's New,
  History, Help — only there to be read), a menu or the tour is up; a drag (the mouse held) is under way; a counter's
  panel or a quick look is open; the screen has something of the student's in it (`Busy`: a reply). It runs once they
  end.
- **The log.** One line a check in simpl.log, never anything of the student's: `live: dashboard unchanged · 0 reads`,
  `live: dashboard updated counter:today,list(~1) · 7 reads` (the reads the page asked of the school meanwhile,
  `BCVNative.stats`), `live: todo waits (typing)`, `live: next try in 120 s`.
- **The screenshot suite.** `--live-every 5` checks every five seconds (a course's page every fifty) with `maxAge` 5,
  says each screen's build in simpl.log (`live: dashboard built`), and turns the check on in a picture run (off
  otherwise: a picture is of what the screen showed); `--live-change done:25` (or `task`, `grade`; several with commas)
  has the engine change the mock school behind its back 25 s after the screens show. `--no-live` (a development run)
  turns the check off. CI's `live-*` pictures check that a check before the change found nothing and built nothing
  (no `built` line after the first check), that one after it showed only what changed (never a whole part: `counters`,
  `main`, `rail`, `today`, `page`) and built nothing, and that the next found nothing again
  (`.github/workflows/windows-fluent.yml`).

- Never `using` both `Wpf.Ui.Controls` and `System.Windows.Controls` without aliasing the clashes (`Button`,
  `TextBlock`, `TextBox`, `Image`, `MenuItem`). Inside a `Screen`, the engine is the field `engine`.

## The pane, the title bar and the tour

- **Title bar** (`MainWindow`): Back and Refresh (F5; its arrow turns while Simpl reads, it dims instead with
  animations off) at its left. Below 880 wide the pane folds away and the pane's button and Search join them; Search
  lends the pane's own search box to the title bar (`AppShell.OpenSearchBar`, also Ctrl+F and Ctrl+K), and it goes back
  to the pane once let go of.
- **Pane** (`AppShell.BuildPane`, `NavPane`): the course open folds out its sections (as the Mac's sidebar does; the
  chevron folds out any other); a course's unread announcements are a badge like Notifications', its tooltip and screen
  reader saying what it counts ("2 unread announcements"); one Groups row opens the Groups page and its chevron lists
  the groups. Under them, *More from Canvas* (`Shell/MoreFromCanvas.cs`): what the school added to Canvas's own
  navigation that no list of Canvas's gives — History (a clock), its tools for every course such as My Materials or
  Studio, Help (a question mark), sometimes a link — as the extension's sidebar lists it, read by the engine from the
  school's page (`moreFromCanvas`; the last menu seen when a page has none, kept with the answers so it shows at once).
  A tool wears the school's own icon as the page drew it, on a square of Canvas's nav colour (the icon is made for that
  dark nav; Simpl never fetches another site's picture itself — the puzzle piece instead), and a tool or a link the
  mark of a window of its own: a tool opens Canvas's launch page for it in the window for tools (light, as every
  outside page is), a link the school's page or another site's in Simpl's browser window. History and Help open
  popups listing the extension's sheets' rows (`moreHistory`, read afresh each time with `maxAge` 1, and `moreHelp`, the
  school's help links as the engine keeps them for an hour; the popup shown whole once they are in, as the extension's
  sheet is), one at a time — the other pressed takes its place: History's with what each was in its course's colour,
  Help's with Report a problem's word at the foot; a row opens where it leads (the school's own as Simpl opens any,
  another site in Simpl's browser window) and the popup goes, its menu's Open in new window and Open in Canvas too
  (Copy link on every row, another site's too). The rows' list is held to the window as it is sized. Both close when
  the student signs out or changes school (`MoreFromCanvas.CloseAll`), a list still being read let go. A tool or a
  link in the pane has a menu of its own (Open, Copy link); a name too long for its row says itself whole in its
  tooltip. The section's heading and each popup's title are headings for Narrator. No section when the school adds
  nothing; on Brightspace, *More from Brightspace* with the navbar's links Simpl does not show already.
  `--open more` brings it into sight for a picture; `--open more:history`, `more:help` and `more:tool` press that item.
  At the foot, Settings (Windows 11's place for it), then the account tile: name, email and the school,
  each said once. In a pane under 760 tall (a 1024×681 or 1366×768 window) the foot keeps compact so the courses keep
  their room: Settings is a gear beside the account tile, and the email and the school share one line. A theme's
  picture stays faint (15%) behind the rows and shows whole only in the room under them.
- **Search box** (`Shell/Search/`): typing only suggests; the full search runs on Enter or *Search for*, so the page
  never changes under the student. Enter on a sum copies its answer (only that row says Enter, "Enter to copy"). A
  result that opens in Simpl's own window has no ↗, which stays the browser's mark. A command's arguments show as grey placeholders
  (`/course ‹course› ‹section›`). Everything is in sentence case.
- **Tour** (`Shell/Tour.cs`): offered, not begun — once the app first shows with nothing over it, *Take a 1-minute
  tour?* rises in at the window's foot, dimming nothing, with *Not now* (counts as seen) and *Take the tour*
  (`--open touroffer` shows it for a picture). Six steps from the Dashboard's counters (Grades, a course, Tools, Search)
  to *Something wrong? Tell us here*, which lights the account tile's ⋯ alone. Each card has a tail toward what it
  lights (Windows 11's TeachingTip), ×, *Skip tour*, Back and a Next that always works (a suggested click moves it on —
  only one taken while its step shows, so Back to a step already done stays there — but no step waits for one); Escape
  ends it; the app's own shortcuts wait while it is up; leaving counts as seen; the account menu's *Take the tour* runs
  it again. Its dim covers the title bar too, all but Windows' caption buttons.

## First run and Settings

- **The school** (`SignIn/`): the search lists a site once — listings at one address are one row under the name that
  matched best (a tie: the shorter), the others under it ("scccd.instructure.com · also: State Center SCCCD";
  `SchoolSearch.BySite` and `Join`, the Mac's rule) — and, while it lists, the welcome steps back (a smaller mark, no
  line under the name) and the list is as tall as its rows, five at most, so the page never scrolls at the usual size;
  the field glides there and then stays put. A preview that cannot load is a small card in the theme's colours ("You can
  still sign in."). The name picked is kept for its address (`SchoolName`), so the card says "Sign in to State Center
  Community College District", the host under it ("at login.school.edu"); `--school-name` gives a development run one
  for its pictures. Start over is in the title bar only over the school's own page with nothing of Simpl's on it;
  "Logging you in" becomes "Still signing in…" with Open sign-in page and its own Start over.
- **The setup** (`Shell/SetupSheet.cs`): opens on "Which courses should Simpl show?" (every current course ticked on a
  first run; one line a course: the name the pane will show, then the term and teacher), then nicknames, grade history
  and the term GPA goal (`GoalFigure`: half up to the hundredth, always two decimals), and a read-back. A first run has
  *Set up later*: nothing saved, and the setup not put up again at launch (`setup:later`); Settings ▸ General ▸ Courses
  shown and goals runs it.
- **Settings** (`Shell/SettingsWindow.cs`): *Find a setting* lists what matches as Windows' Settings does ("Get beta
  updates — Updates"; ↑ ↓ Enter), opens the page and lights the row for a moment; "No matching settings" when nothing
  does. A sign-in not saved has *Save sign-in…* (it signs out, so the card can save one). Appearance has *Use system
  setting*, a ninth swatch for a colour of one's own (*Custom colour*: a spectrum to drag, and a hex code), the Default
  theme drawn as a small page, and the photos editor: the seven places side by side, the one picked larger, its picture
  dragged to move it (`Appearance.Pan`), Reset, and any theme's drawing for any place (`Appearance.SceneFor`). Headers
  never repeat the page's or a row's name; the words are plain ("can only be opened from your Windows account", "Import
  a grade-history CSV you exported earlier"), and no folder's path is printed (Open folder instead).

## The quick look

A row of work (the Dashboard's, To Do's, the Calendar's, a counter's panel) grows a card out of itself with
`WorkPreview.Press(item, row)`: build the `PreviewItem` with `WorkPreview.Dash`, `Work` or `Sheet`, or by hand (the
Calendar's `CalendarPeek`). The card reads the work's details as it opens — the first words of what the work says, as
plain lines, up to five — Mark done ticks it, a double-click or a Ctrl-click opens the work whole, and Close, Escape or
its row pressed again folds it back into its row. It takes nothing from the window: nothing behind it is dimmed, a click
elsewhere does what it does and leaves it up, and it stays over whatever place is opened under it until it is closed;
its heading moves it (`Kit/Drag.cs`: only once the pointer has gone Windows' drag distance — a click there is not a
move). A counter's panel on the Dashboard is 0.2.9's, as the Mac's 1.3.77: pressed, the counter's card grows in place
into its panel over the page (its place and size to the panel's, 400 ms on Windows' decelerate curve; the surface is
the card's own face at first — its wash, or the alert's, its edge, its picture — giving way to the panel's over 215 ms
as the panel's words fade in from 90 ms) and folds back into the card as it closes (250 ms, the words fading first and
the card's face coming back, then the surface lifts off the card, whose words show through it) — with ×, a click on its
heading, Escape, the card again, or a press anywhere else, which still does what it does (another card opens its own),
so it never holds the window. A panel folding lies under any panel opened meanwhile and takes no press: its own card
pressed again as it folds opens it again. It is drawn in the shell's overlay over the page (`WorkPreview.Layer`; a
Dashboard in a window of its own, the page's own overlay), under any quick look (a press there leaves it up), is not
moved by its heading, and goes with the page when another place is opened. It stays on its card: as the page scrolls
or is laid out again it goes with the card, cut under the page's header where the card has scrolled up there (`DashboardScreen.Follow`; `--sheet next
--open sheetscroll` scrolls the page under it, for the `dashboard-counter-scrolled` picture). With Windows' Animation
effects off nothing grows: the panel fades in where it opens, over its card, and fades away as it closes
(`Motion.Fade`). `--sheet <key> --open sheetmorph` draws it as it is a quarter of the way through growing, for the
`dashboard-counter-morph` pictures.

The card is 0.2.9's: as wide as the row it grows out of and a little more, from 300 (a month's narrow chip) to 420 (a
list's row), never wider than the page. It says where the work stands once: a graded piece's score and letter as one
chip, the engine's `grade.chip` ("13/16 · B−"), with no "Graded" beside it; Late, Missing and Submitted stay. From a
counter's panel (`WorkPreview.Sheet`) it reads the row's parts — its course, its detail, its day — and says the due
once, on its clock line ("Due today at 11:59 PM", "Was due Oct 13"), never again as a line of detail
(`--sheet today --open sheetpreview` opens the panel's first row's card, for the `dashboard-counter-preview` picture).

A counter's panel (`DashboardScreen.PanelContent`) is 0.2.9's too: the counter's symbol, name and number with the
engine's line beside it, and its list — each row a bar of its course's colour, its title and the engine's one line about
it (`sub`) with the kind of work after its course as 0.2.9's read ("F26-MATH 021 20 · Quiz · 10 pts · due 11:59 PM":
the engine gives the kind on its own now, `kind`), a chevron to its quick look (or Dismiss, in Overdue) — scrolling past
about five rows, nothing under it.

## Files with a message

New message (its foot's Attach, or files dropped on it) and a conversation's reply bar (the paperclip, or files dropped
on it) send files with what is written (`MessageFiles`, `Screens/Inbox/InboxShared.cs`; the Mac's MessageAttachments).
Each file is uploaded as it is picked to the student's own "conversation attachments" folder in Canvas — the engine's
`messageUpload { name, type, data }` (base64), Canvas's own Inbox's way — and shows as a chip: its name and size, a ring
while it goes up, a red mark and why if it could not (over 50 MB, unreadable, Canvas said no), × to take it out. Send
waits until every file is up and sends the ids of those that are (`sendMessage` / `sendReply`'s `attachments`). A
development run's `--attach sample` attaches a small text file of its own making to the first new message or reply
opened (`--attach <path>`: that file), for the pictures.

## Send from Phone

Hand in's *Send from phone…* (beside Choose files…, for an assignment that takes files) opens a panel over the form
with a QR code (`Screens/Assignment/PhoneHandIn.cs`). The phone's own camera opens it: a page this PC serves itself
(`Assets/PhoneHandIn.html`, an embedded resource — the same file as the Mac app's), with the assignment, its course and
the kinds of file it takes, *Take Photo* and *Choose Files*, each file's progress and Retry, then "Sent to your PC —
finish handing in there." Each file comes into the hand-in as if chosen with Choose files… (its type checked, converted
where it must be); the student looks it over and presses Submit on the PC — nothing is handed in by itself.

- **No server anywhere.** `Support/PhoneServer.cs` is a small web server on plain sockets (`TcpListener`, not
  `HttpListener`, which needs an address an administrator reserves) on this PC's private Wi-Fi or Ethernet address
  (`PhoneLan.Best()`: never a VPN's, a virtual switch's or Bluetooth's) and a port Windows picks. It is up only while the
  panel is open: it stops when the panel closes, when the hand-in is sent or cancelled, and after 15 minutes with nothing
  from the phone. The address holds a 128-bit one-time key, compared in constant time; every other path is 404; only
  private addresses may connect; a file is at most 50 MB (Hand In's own limit) and a session 500 MB; names are made safe
  (no folders, nothing Windows forbids, not CON or NUL); what came waits in `%TEMP%\SimplPhone-…`, deleted with the
  hand-in. The page is served with a strict Content-Security-Policy and asks for nothing but its uploads.
- **Reachability.** While the code waits, the panel says what the phone needs in the Mac's words — "Your phone needs to
  be on the same network as this PC (your phone's hotspot works)." — with where this PC is ("This PC is on Wi-Fi at
  192.168.1.23") and that some school networks keep devices apart folded under *Trouble connecting?*; once a phone has
  connected, all of that goes. With no such address: "Connect this PC to a network to use your phone." The first time,
  before it listens, the panel says Windows will ask whether Simpl may use the network (choose Allow) — `phoneAsked` in
  prefs.json remembers it was said. Done (or Escape) is its one way to close.
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

A tool opens whole in a popup over where you are (`ToolPopup`, a popup floating over the window: Popups, below) —
from its tile on the Tools page (or the tile's menu), a pin's menu, a pin's flyout's or pulled-out window's Open full
screen (0.3.5), a command, a link, or another tool handing it something (`ToolsCenter.Show`) — as large as the window under its title
bar less a margin, over the pane too, and resized with the window; sized by its edges and corners, the size kept for
that tool the next time (Popups, below); the Tools page (or whatever shows) behind it and the title bar and its search
still at hand. Its heading, which moves it: the tool's tile and name, its line, its own controls (a biology
tool's pages, the Periodic Table's search and Tour — on a line of their own under the name where the popup is too
narrow for them beside it), its pin and ×; × or Escape closes it. One popup a tool: opened again, the one there comes
forward, taking what was handed to it. A tool that shows pages of its own can name the one shown after its name
(`ToolContext.ShowPage`: "Tool › Page", the name going back to its first page), and may hide its line while it needs
the room (`ShowNote`); a tool handing something to another from its popup (`ToolsCenter.HandTo`: `BioTools.Open`, an
element's See it in 3D, Lewis Structures' Open in Molecule Builder, the Biomolecule Builder's molecule for the Molecule
Builder) turns that popup into the other tool in place (`ToolsCenter.PopUp`), the other taking what it was handed once
(`TakeInfo`). `--tool calc` opens one over the Tools page for a picture.

A pin's flyout is the tool's quick version, under its whole name and (0.3.5) *Open full screen* at its top right (the
Fluent FullScreenMaximize icon, its tooltip and screen-reader name "Open full screen"; `PinBar.QuickHead`), where Pop
out was — the owner, of the Mac's: "it should be full screen". It closes the flyout (a pulled-out window, whose heading
has it too, closes: kept on top, it would sit over the tool) and opens the tool whole, as its tile does
(`ToolsCenter.FullScreen` → `Show`), with what was begun in the quick version: each quick version says what it carries
(`ToolContext.Carry`) — Grade needed's three numbers, the element searched for in the Periodic Table's flyout or typed
in Atoms & Orbitals', the link pasted to cite, a codon looked up, a cross, a Hardy–Weinberg value, the lab calculator
shown, and `"carry": "pin"` for the calculator (its line, sums, memory and Rad/Deg: `CalcEngine.Take`) and the unit
converter (its kind, units and value: `UnitsState.Take`), whose whole tool then goes on from the pin's; the graph's
whole drawing goes by Desmos's own `Calc.getState`/`Calc.setState` from the pin's web view to the whole one's
(`GraphWeb.CarryOver`; the whole graph before it is a Ctrl+Z away). The focus timer, Flashcards' sets and the lab's
numbers are the whole tool's already. A quick version as it opens carries nothing, and the tool opens as from its tile;
with something carried, the tool's popup, if it is open already, is made again to take it. The name is the heading's
title alone — its ›, which opened the whole tool too, is gone — so Open full screen is the one way from a flyout to its
tool. Pop out, which pulls the quick version into a small window of its own kept above Simpl and its popups
(`ToolFloat`; pictured over a quiz with `--pin-popout calc`), is in the pin's menu; unpinning is in the pin's menu, not
in the flyout. For pictures, `--pin-full need` opens that pin's flyout and presses Open full screen (`--need-numbers
85,30,90` types Grade needed's numbers; `--pin-graph "y=x^2"` draws in the pin's Desmos), and the log says what was
carried ("pin full screen: need (carried: goal, now, worth)", "graph: the pin's graph put in the whole one"). The flyout folds back on a
press elsewhere in the window, the window left (not for a part of the flyout that is a window of its own), moved or
resized, Escape, or its pin pressed again (its opening and closing go to the log in picture runs; CI's `--pin-try
graph|fc` opens one as a press does, clicks and types in it, and checks it is still open five seconds on). The same
pins sit in the heading of a quiz (which covers the title bar), Knewton Alta and Macmillan (`PinBar(engine, inPopup:
true)`); the whole tool opens in its popup from them too, above them. A popup focuses its first field only when it
shows without scrolling, and never read-only words, so it opens at its top; a tool opens at its top too
(`ToolKit.Edged`), whatever takes the focus as it comes up. The work and its side panel (`ToolKit.Columns`) sit side by
side from about 860 of the popup's width — the popup is the window's width less 48 (1318 in a 1366 window, 976 in a
1024 one, 748 in a 780 one) — and one over the other below that, a side that holds the tool's answer (Grade needed,
Citations) first. The chemistry builders' and the biology tools' layout (`ChemLayout`) puts the controls, the drawing
and the facts side by side from 1180 of it (a 1366 window), the drawing beside one column of the controls over the
facts from 840 (a 1024 window), and the drawing over the two below that (a 780 window); it is held to 2200 wide (the
DNA tool's two columns too), so in a 1920 window the drawing takes the popup's width (held to 1480, 180 of it went
empty at each side).

Every 3D view frames its model the same way (`Tools/SceneFrame.cs`): the model fills about 85% of the view where it
meets the view's edges first — the element's atom, orbitals and electron cloud (`Atom3DView`: the atom by the ball round
it, the orbitals at their widest turn as they revolve, as they stand with Windows' animations off, the cloud by the ball
95% of its dots lie in, at 80%), the molecule builder's (`MoleculeSceneView`: as it stands, or at its widest turn about
the view's up while it turns by itself — as it stands, looked at in its middle: `SceneFrame.PointsCentred`) and the
biology tools' (`BioSceneView`) — on first show, on Reset view, and
whenever the view's size changes (a popup sized by its edges, or with the window), a zoom of the student's own kept as
the same share of it. The lens is measured across the view's smaller side, so a tall narrow view keeps the model's
sides.

The biology tools' view measures its model by its outline, not its box: the scene's outermost points
(`BioSceneContent.OuterPoints`, the vertex furthest out in each of 256 directions) as the camera sees them, their hull
(`BioOutline`). The camera looks at the outline's middle (an amino acid lies off its ball's centre) and stands where the
model fills 85% of the view, then back a step at a time while its labels do not fit round it — but never past the model
filling 75% (`SceneFrame.Least`). The labels are kept off the model itself (`BioLabelLayout` given the outline): each
column's labels, in order round the model's middle, are spread down the column to the heights nearest their anchors
where each fits beside the outline, so a round cell has its labels in the corners its outline leaves (beside its box,
sixteen labels in columns held the cell to half its view: 46% of a 1280 window's view); one with no room there goes to a
row above or below it, and one still in the way — on the model, over another's anchor, or led through another's capsule
— is folded into "+N more", the smallest object's first, the note itself put where it lies off the model and every
label. A note over the view's top (the membrane's on osmosis) keeps the labels under it (`BioSceneView.LabelTop`). The fit is worked out again once the view's size settles, not
at every step of a popup sized by its edge. A development run's log says each view's size and framing (`3d view …: the
model W×H, N% of the view; with its L labels …, M% (+F more)`, `element view …`, `molecule view …`); CI holds the
biology views' own share to 70% or more.

`ToolContext` hands a tool the engine, where it is (`Quick`; `Owner`, the window it is in), what was handed over
(`TakeFiles`, `TakeInfo`), `OpenFull` / `Close`, and `ShowPage` / `ShowNote`. `ToolKit` has the page, two columns,
fields, notes, the drop zone and the scroll edge; `Files` has Windows' file dialogs.

The Tools page lists the tools under what they are for — Study, Math & science, Files, then Chemistry, Physics and
Biology — a one-line note on each tile, three across from about 900 wide (a section's four in full rows); a tile opens
its tool's popup over the page. Windows' sentence case on every button and menu ("Pin to title bar", "Copy result",
"Skip tour"). `--tools-scroll chemistry` (or `physics`, `biology`) opens the page scrolled to that section.

The chemistry tools (`Tools/Chemistry/ChemTools.cs`, a "Chemistry" section of their own just before Biology) were one
tool until 0.3.2, the Periodic Table with its Lewis structures and Molecule builder pages and its element pages' atom in
3D. Both apps the same: the Mac's keys, names, lines, short names, search words and colours (red, cyan, green, violet);
the Periodic Table's symbol a frame of element tiles (`SlideGrid24`: a table's grid read as the Biomolecule Builder's
cube). What each keeps for next time is the Mac's too (`ChemPrefs`, `SimplChem.<tool>.<field>` in the preferences):

- **Periodic Table** (`ptable`, its key kept, so a pin of it stays the Periodic Table; `Tools/Elements/PeriodicTable.cs`):
  the table, coloured by any property with its trends, the element under the pointer in its top, an element's own page
  (its electrons and every fact) and the table's tour (five steps, offered the first time; Tour beside the search
  takes it again). An element's page has **See it in 3D**, which hands it to Atoms & Orbitals in the same popup
  (`--ptable-element Fe --ptable-atoms` presses it for a picture). It keeps its colouring and the last element opened
  (`ptable.colour`, `ptable.element`).
- **Lewis Structures** (`lewis`, `Tools/Chemistry/LewisView.cs`): the Lewis maker — quick or drawn, presets, ions,
  resonance, formal charges and checks; **Open in Molecule Builder** hands the structure shown to the Molecule Builder.
  It keeps its molecule, its way and its ranking (`lewis.draft`).
- **Molecule Builder** (`molecule`, `Tools/Chemistry/MoleculeView.cs`): the 3D builder with VSEPR shapes. It keeps its
  molecule and how it is shown (`molecule.saved`, `.style`, `.lonePairs`, `.labels`).
- **Atoms & Orbitals** (`atoms`, `Tools/Elements/AtomsTool.cs`): an element typed by its symbol, name or number (← and →
  the one before or after — from the field while it is empty, or once the model is pressed; the tool takes the keyboard
  as it opens and after a hand-off) as its atom (shells), a subshell's orbitals or its electron cloud, with its
  electrons and **Open in Periodic Table**. It remembers its element and view (`atoms.element`, `atoms.view`) and opens,
  until it has shown one, on the element last opened in the Periodic Table. The line under the field is read by
  Narrator as it changes; a subshell's chip keeps the keyboard when it is chosen. Its pin's flyout takes an element and
  Enter, opening the whole tool on it.
- **Equation Balancer** (`balance`, `Tools/Chemistry/BalancerTool.cs`; the sums in `Tools/Chemistry/Model/`: the
  formula reader `ChemFormula`, the solver `ChemBalancer`, molar masses `ChemMolarMass`, numbers `ChemNum`): one field
  for the whole equation (`->`, `→` or `=`; phases, charges like `Fe3+` or `SO4^2-`, brackets, hydrates `CuSO4·5H2O`),
  balanced as it is typed (150 ms after the last key; Return at once) by Gaussian elimination over exact fractions —
  the balanced equation typeset with its coefficients in bold, each element's (and the charge's) count on each side
  with a tick, the mass check; or why not, in plain words: more than one way (two reactions written as one), an
  element on one side only (named), a formula it cannot read (named). Copy, Clear and four example chips; Ctrl+C with
  no field focused copies the plain-text equation. It keeps its equation (`balance.equation`). Its pin's flyout is the
  field and the balanced line, and Open full screen carries the equation. `--tool balance --tool-state "Fe + O2 ->
  Fe2O3"` (or `--balance "…"`, which the flyout reads too) opens it on an equation; a picture run logs `balance: shows
  balanced 4,3,2`. The search box opens it with `/balance`, `equation`, `reaction` or `coefficients`.
- **Molar Mass & Stoichiometry** (`moles`, `Tools/Chemistry/MolesTool.cs`; the sums in `Model/ChemStoich.cs` on
  `ChemMolarMass` and the balancer): four tabs, the one chosen remembered (`moles.tab`). **Molar mass**: a formula, its
  mass large, and each element's count, atomic mass, subtotal and mass % (percent composition); chips for Ca(OH)2,
  C6H12O6 and CuSO4·5H2O. **Convert**: a formula and one amount in grams, moles, molecules (or formula units, or atoms)
  or litres of gas at STP (22.4 L/mol; Avogadro 6.02214076e23) → the other three, to the input's figures. **Reaction**:
  an equation (balanced for you, and said so when the coefficients change), a row per reactant with what you have (g
  or mol) → the limiting reagent, every product's theoretical yield in g and mol, what is left of the others, and the
  percent yield of the product weighed. **Empirical**: up to six elements with their mass % (or grams) → the empirical
  formula (ratios to the smallest, ×1–6 until whole within 0.08; said when it had to be rounded) and, with a molar
  mass, the molecular one. Every tab keeps its fields (`moles.formula`, `.convert.*`, `.equation`, `.given`, `.actual`,
  `.empirical`, `.empirical.mass`); Ctrl+C copies the tab's result. Its pin's flyout is the Molar mass tab compact, and
  Open full screen carries the formula. `--tool moles --tool-state reaction` (or `--moles reaction`) opens a tab, with
  `--moles-formula`, `--moles-amount "10 g"`, `--moles-equation`, `--moles-given "H2=4g,O2=16g"`, `--moles-actual
  "H2O=15g"`, `--moles-empirical "C=40,H=6.71,O=53.3"` and `--moles-mass 180` for what it holds; a picture run logs
  `moles: shows reaction` and each tab's `moles reaction: shows …`. The search box opens it with `/moles`, `molar mass`,
  `stoichiometry`, `limiting reagent`, `percent yield` or `empirical`.
- **Gas Laws** (`gas`, `Tools/Chemistry/GasTool.cs`; the sums in `Model/ChemGas.cs`): two tabs. **Ideal gas**: P, V,
  n and T, each with its unit (atm, kPa, mmHg, bar, psi; L, mL, m³; mol or g of a formula; K, °C, °F); leave exactly
  one blank and it is solved (R = 0.082057 L·atm/(mol·K), every sum in atm, L, mol, K), the answer shown large and
  faintly in the blank field, the formula with the solved letter in the accent; two blanks say "Fill in all but one."
  **Combined**: P1V1/T1 = P2V2/T2 with six fields, one blank solved; the chips Boyle (T constant), Charles (P constant)
  and Gay-Lussac (V constant) hide the constant pair. A piston drawing (`PistonView`) follows the answer: its height
  the volume, its dots the density n/V, its colour the temperature's warmth, each change over 200 ms easing out —
  at once where Windows' animation effects are off (`Motion.On`). Fields and units are remembered (`gas.P`,
  `gas.P.unit` … `gas.T2.unit`, `gas.formula`, `gas.constant`, `gas.tab`). Its pin's flyout is the Ideal gas tab compact
  (the same remembered fields, so Open full screen goes on from them). `--tool gas --tool-state ideal|combined` (or
  `--gas …`) with `--gas-values "P=1 atm,V=22.4 L,T=273.15 K"` (a letter left out is the blank; `P1`…`T2` for the
  combined law) and `--gas-constant T|P|V` open it for pictures; a picture run logs `gas: shows ideal` and `gas ideal:
  shows n=0.9994 mol`. The search box opens it with `/gas`, `ideal gas`, `pv=nrt`, `boyle`, `charles`, `pressure` or
  `volume`.

The builders and Atoms & Orbitals share one layout (`ChemLayout`): the controls and the facts in sight at every size,
the drawing's own heading holding what to show when the drawing is over the rest, and the drawing held to the page less
100 for the columns under it (on the smallest window too). A launch's `--tool-state` names what each opens on:
`--tool lewis --tool-state NO3-`, `--tool molecule --tool-state Ethanol`, `--tool atoms --tool-state Fe:cloud:3d` (the
element, then atom, orbitals or cloud, then the subshell; `all` for the whole cloud). The search box opens each by the
Mac's words (`/ptable` or `periodic`, `/lewis` or `resonance`, `/molecule` or `vsepr`, `/atom` or `orbitals`, `electron
cloud`, `bohr`). A tool handing over to another in its popup gives the other the keyboard (`ToolPopup.Change`).

The physics tools (`Tools/Physics/PhysTools.cs`, a "Physics" section of their own between Chemistry and Biology) are
the Mac's too: keys, names, lines, short names, search words and colours (a family of blues). Their maths lives in
`Tools/Physics/Model/` with no WPF in it, so the unit tests (`MotionTests`, `VectorsTests`) check every worked example on
any machine;
numbers are written to four significant figures with no float noise (`PhysMath.Text`: "40.77 m", "2.883 s"), the minus
the typeset "−", and a result's menu (or Ctrl+C while it has the keyboard) copies it in plain ASCII. What each keeps for
next time is in the preferences as `SimplPhys.<tool>.<field>` (`PhysPrefs`):

- **Kinematics & Projectiles** (`motion`, `Tools/Physics/MotionTool.cs`): two tabs. *1D motion* — Δx, v₀, v, a and t,
  any three filled in, the other two solved and shown with the equation each came from (v = v₀ + at, Δx = v₀t + ½at²,
  v² = v₀² + 2aΔx, Δx = ½(v₀ + v)t; a root is picked so the time is not negative, the earlier of two), a g preset
  (a = −9.81), and the motion's position–time and velocity–time graphs (`MotionGraph`). *Projectile* — launch speed,
  angle, height and g → time of flight, range, highest point (and when), impact speed and angle; the path drawn
  (`TrajectoryView`) with the apex and landing marked and a dot flying along it over a second and a half (still, at the
  landing, with Windows' animations off or in a picture; Launch again flies it once more); and "Hit a target at" a
  distance → the two angles that land on it, or that it is out of reach. SI in; the "ft and mph" switch shows the
  answers in feet and miles per hour beside the metres. It keeps its tab, numbers and switch (`motion.tab`,
  `motion.oneD`, `motion.projectile`, `motion.imperial`). Its pin's flyout takes a speed and an angle and gives the
  range and the highest point from the ground; Open full screen hands those to the whole tool. A launch's
  `--tool motion --tool-state 1d:v0=0,a=2,t=5` or `--tool-state projectile:v0=20,angle=45,h=0` (the values alone in
  `--motion-values`) opens it in that state, and in a picture run the log says what it shows ("motion: shows projectile
  range=40.77 height=10.19 time=2.883"). The search box opens it by `/motion`, `kinematics`, `projectile`, `velocity`,
  `acceleration` or `free fall`.
- **Vectors** (`vectors`, `Tools/Physics/VectorsTool.cs`): up to six vectors, A to F, each given as components or as a
  magnitude and an angle from +x, counter-clockwise (a row switched from one to the other keeps its vector), in 2D or
  3D, degrees or radians. Each one's magnitude and direction (a unit vector in 3D), the resultant with its magnitude
  and angle, and for a chosen pair the dot product, the angle between, the cross product (in 2D its z-component, out of
  or into the page), the projection of one onto the other and their unit vectors; Copy takes it all as plain text. The
  drawing (`VectorDiagram`): arrows from the origin, or each from the tip of the one before ("Tip to tail"), the
  resultant dashed, over axes scaled to fit; 3D as an isometric view with its three axes. It keeps its vectors and
  settings (`vectors.list`, `.polar`, `.threeD`, `.radians`, `.tipToTail`). Its pin's flyout takes two 2D vectors and
  gives their sum, its magnitude and angle, and their dot product; Open full screen hands the two over. A launch's
  `--tool vectors --tool-state 3,4;1,2` (`3d:1,2,3;4,5,6` for 3D; `--vectors-mode 2d|3d`) opens it on those, and in a
  picture run the log says what it shows ("vectors: shows 2 vectors, resultant (4, 6) |R|=7.211"). The search box opens
  it by `/vectors`, `vector`, `components`, `dot product`, `cross product`, `resultant` or `magnitude`.

The tools program's everyday tools (`Tools/Everyday/`, each at the end of its group on the Tools page; their sums in
`Tools/Everyday/Model/`, plain C# with no window in it, tested in `SimplFluent.Tests` on the specs' vectors; the search
box opens each by its name and words, `PaletteCommand.EverydayTools`; `--tool <key> --tool-state …` for the pictures,
`Everyday.LaunchState`):

- **Word Counter** (`words`, Study; `Tools/Everyday/WordCounter.cs`, `Model/WordCount.cs`): an essay pasted or typed,
  counted as it changes — words (a run of non-spaces with a letter or a figure in it), characters with and without
  spaces, sentences (ending at . ! or ? before a space or the end), paragraphs (blocks between blank lines), reading
  time at 238 words a minute and speaking time at 150, the average and the longest word — a target of so many words
  with a bar and "120 to go" / "15 over", and the ten words used most as chips, the stop words left out. The text and
  the target are kept (`SimplWords.text`, `.target`) and are the pin's too: its flyout is a few lines of the same text
  with the counts under them. `--words-text "…"` (`\n` for a line break) and `--words-target 500` fill it for a
  picture; the log says `words: shows N words`. The search box: `/words`, `word count`, `characters`, `reading time`,
  `essay`.
- **Statistics** (`stats`, Math & science; `Tools/Everyday/Statistics.cs`, `StatisticsPages.cs`, `Model/Statistics.cs`):
  four pages under a selector bar, kept with every field (`SimplStats.<field>`). **Describe**: numbers (spaces, commas
  or line breaks; a spreadsheet column pastes) → n, mean, median, mode(s), min, max, range, Q1, Q3 and IQR (the
  medians of the lower and upper halves, the median left out when n is odd — said on the page), sample and population
  SD and variance, the SE of the mean, sum and sum of squares, outliers past 1.5 IQR as red chips, and a histogram
  (Sturges' bins) with the box plot under it. **Regression**: x and y columns → slope, intercept, r, r² and the
  equation, a scatter with the line, and y for an x. **Distributions**: the normal's P(X < x), P(X > x), P(a < X < b),
  the z-score and the x for a p (erf to full precision — its series near zero, the continued fraction for erfc beyond
  — and Acklam's inverse with a Halley step), the curve with the area below x shaded; the t's two- and one-tailed p and
  the critical t for a p (the regularized incomplete beta by Numerical Recipes' continued fraction). **Tests**:
  one-sample (from numbers or mean, SD and n), two-sample (Welch) and paired t-tests → t, df, p, the 95% interval, in
  a plain sentence. The pin shows a sample's mean, median and sample SD (its numbers are Describe's; Enter opens the
  whole tool). `--tool-state describe|regression|dist|tests` picks the page, `--stats-data`, `--stats-x`, `--stats-y`,
  `--stats-b`, `--stats-mu0`, `--stats-test one|two|paired`, `--stats-normal "μ,σ,x,a,b,p"` and `--stats-t "df,t,p"` fill
  the fields for a picture; the log says `stats: shows <page>`. The search box: `/stats`, `statistics`, `mean`,
  `standard deviation`, `regression`, `normal`, `z-score`, `t-test`.
- **Matrix Calculator** (`matrix`, Math & science; `Tools/Everyday/MatrixCalculator.cs`, `Model/Matrix.cs`): A and B
  in editors of cells (1 to 6 rows and columns by − and +, Tab from cell to cell, a block pasted into any cell —
  rows on lines or after semicolons, cells apart by spaces, commas or tabs — fills and sizes the matrix), k and n
  beside them, the operations as a row of buttons (A + B, A − B, A × B, kA, Aᵀ, det A, A⁻¹, rank A, RREF A, trace A,
  Solve A·x = B with B's first column as b, the eigenvalues of A, Aⁿ for n from −64 to 64), and the answer at the right:
  a matrix between brackets, a number large, or the eigenvalues listed (real and complex, "1 + 2i"), with a
  reduction's row operations under it ("R2 ← R2 − 4R1"). Cells are exact fractions (big integers over big integers,
  `Rational`), so answers come as fractions when every cell is an integer or a fraction, with a Fractions / Decimals
  switch; a decimal cell or the eigenvalues (the shifted QR iteration on the Hessenberg form, `Eigen`) make the answer
  decimals. Why an operation cannot be done is said plainly (sizes that do not match, a singular A, a system with no
  or many solutions). The matrices, operation, k, n and the switch are kept (`SimplMatrix.a`, `.b`, `.op`, `.k`, `.n`,
  `.fractions`); the pin holds a 2×2 or 3×3 A of its own with its determinant and inverse, and Open full screen hands
  it over. `--matrix-a "1 2; 3 4"`, `--matrix-b "3; 5"` and `--matrix-op det` (or the operation as `--tool-state`)
  fill it for a picture; the log says `matrix: shows <op>`. Its tile wears `TableSimple24`, not the `Grid24` the
  biology Crosses tile has. The search box: `/matrix`, `matrices`, `determinant`, `inverse`, `linear algebra`,
  `system of equations`, `eigenvalues`.

The biology toolkit's ten (`Tools/Biology/`, a "Biology" section of their own on the Tools page) register through
`BioTools.Register`, which adds their pages in the header and what a launch asks of them (`--tool cell --tool-state
plant:cutaway`); their shared data, 3D view, labels with leader lines, step-through and self-check (`--bio-check`) are
described in `Tools/Biology/README.md`.

The tools added since (each the Mac's too: the same key, name, line, colour and words) sit at the end of their section's
list in `ToolsScreen.Everyday` and `Groups`, and the search box opens each by its command (`PaletteCommand.NewTools`):

- **Compress files** (`shrink`, Files; `Tools/Files/CompressFiles.cs`, its sums in `Tools/Files/Shrink.cs`; `/compress`,
  `shrink`, `smaller`, `file size`, `upload limit`): pictures and PDFs dropped in and made small enough to upload, at Max
  quality, Balanced (up to 2048 px), Smallest (up to 1280 px, see-through parts flattened) or Fit under a size in MB
  (the quality searched, then the size). A picture is read with Windows' own codecs (WPF's imaging: JPEG, PNG, BMP,
  TIFF, GIF, and HEIC or WebP where the extension is installed), turned the way the camera held it, and written as a
  JPEG — or a PNG again while it has see-through parts. A PDF's embedded pictures (JPEGs and plain 8-bit RGB or grey
  ones in its pages' and forms' resources) are written again smaller inside it with PDFsharp, its pages, words and links
  untouched — never drawn again as pictures; pictures of other kinds (masks, indexed, CMYK, 16-bit) are left as they are
  and the row says so. Each smaller copy goes to Downloads as `name-smaller.ext`, never over the original; the row says
  "1.9 MB → 540 KB · −72%", or that it was left as it was, or the smallest Fit under could reach; the first picture made
  smaller is shown before and after. The preset and the limit are remembered (`SimplShrink.preset`, `SimplShrink.fit`).
  Its pin is a small drop zone: files dropped open the whole tool on them at Balanced. For pictures: `--shrink-files
  sample` (a photo, a diagram with see-through parts and a PDF of both, made on the spot in the profile) or paths joined
  by `|`, `--shrink-preset balanced|smallest|max|fit`, `--shrink-fit 0.5`, and `--shrink-go` to compress at once; the log
  says `shrink: shows N files at <preset>` and `shrink: done at <preset>, … (N% saved)`. Tested in
  `SimplFluent.Tests/ShrinkTests.cs`.
- **Study planner** (`plan`, Study; `Tools/Planner/StudyPlanner.cs`, its sums in `Tools/Planner/Model/StudyPlan.cs`,
  free of WPF; `/plan`, `study plan`, `study time`, `exam prep`): the work due in the next six weeks — the assignments,
  quizzes and discussions the Calendar shows, read with the engine's `calendar` call — shared out as study blocks over
  the days before each is due. A piece's weight is its points (equal shares when none has any) times its kind: an exam,
  midterm, final or test by name three, a quiz one and a half, an assignment one, a discussion half; its need is its
  share of the hours between now and the last deadline, in whole blocks. Each day is filled earliest deadline first,
  then heaviest — first what keeps every piece on pace for its deadline, then the room left — and a block never ends
  past its work's due moment, nor sits before now. A week view (today first, the days flowing on a narrow page) and a
  list view; beside them how every piece stands (done of needed, and what won't fit before it's due), the settings
  (hours a day by weekday, 2 on weekdays and 3 at the weekend; the block, 25/45/60/90 min; morning, afternoon or
  evening; days to finish early; the courses included) — all remembered (`SimplPlan.hours`, `.block`, `.time`,
  `.finish`, `.courses`, `.view`) — Regenerate and Add to Calendar (an `.ics` of the blocks saved to Downloads). A
  block ticked is done (`SimplPlan.done`): it stays where it was and comes off what its work needs, and the rest is
  planned round it. Nothing is written to Canvas. Its pin is today's blocks as a checklist. For pictures:
  `--plan-hours 2`, `--plan-block 45`, `--plan-time evening`, `--plan-view list`, `--plan-done assignment_1015:2`; the
  log says `plan: shows N blocks for M items over D days (week|list)`. Tested in `SimplFluent.Tests/PlannerTests.cs`
  (the sums) and `PlannerToolTests.cs`.

## Popups

Every popup floats over the window without holding it (`Shell/FloatPopup.cs`, a `Sheet` with `Floats`): a window of
its own owned by Simpl's window — kept above it, minimized and brought back with it, hidden with it in the notification
area and shown again with it — so nothing takes the window's clicks: the pane, the title bar, the search and the page
behind it go on working, and another place opened under a popup leaves it where it is. Nothing is dimmed; Windows' own
shadow and rounded corners set it apart (pictures draw them, `Support/Shot.cs`). Its window is as large as its card,
however small (WPF-UI's least window size, 460 × 320, let go). It opens centred over the page beside
the pane (`MainWindow.PopupRoom`); it moves with the window, keeping where it was put, and its heading moves it (a
press there that is not on a control, `Kit/Drag.cs`; `Handle` names the heading, else its top 60).
× (or its own Close: `OwnClose`), Escape while it has the keyboard (not while what it shows must be answered first:
`Modal`, the popup's own keys then) and Alt+F4 close it — through its Cancel, or `Dismiss` (New message asks before a
message is thrown away). Enter presses its default button. Its own questions show over it (its own `SheetHost`), and
Windows' Open dialog over it. Windows' own window motion brings it in and takes it away, as Windows' Animation effects
say. Each popup with something of the student's in it holds an update while it is open (`Shell/UpdateWatch.cs`).

A large one (`Large`: a tool, Knewton Alta, Macmillan, a submission's feedback, the rubric) spans the window under its
title bar — over the pane too (`MainWindow.LargePopupRoom`; the owner's choice, Oct 2026: in 0.3.1 it kept to the page
beside the pane, 688 wide in a 1024 window) — less a margin all round (24, 16 under 900 wide), never larger than the
screen's work area nor, the feedback and the rubric, than their `Most` (1500 × 1000, 1180 × 880); it is sized again with
the window, and one over another opens a step down and along (28, the fifth back where the first is), that much
smaller so its edges stay inside the window. It is sized by its edges and corners too: Windows'
own sizing (`ResizeMode.CanResize`, WPF-UI's 4 of edge), at least 480 × 340 and at most its room, what it shows laid
out afresh as it goes; never maximized, minimized or snapped (its boxes taken off, `NoBoxes`, and not sizable while its
heading moves it). A sizing taken back (Escape or a right-click while sizing) keeps nothing. Let go, the size is the
student's: kept in the preferences under its `SizeKey` (`popup:tool:dna`,
`popup:alta`, `popup:macmillan`, `popup:feedback`, `popup:rubric` — width and height) and taken the next time it opens,
held to the room it then has; a window made smaller takes it in with it, and larger again gives back that size. A tool
handing over to another in its popup takes that one's size. In a development run `--popup-size 900x560` opens large
popups as if the student had sized them so, `--popup-drag 300` sizes one by its right edge as the student does (the
pointer pressed there, moved 300 left and let go; CI's check, alone on the runner), and `--popup-move -120,40` first
moves one by its heading, then sizes it by its edge 200 narrower, the log saying its size and the size kept (`large
popup … opens at`, `sized by its edges to`, `popup move: moved by …`, `popup drag: …`). A web page shown edge to edge in
one (Knewton Alta's, Macmillan's, a submission's file) keeps 4 in from its sides and foot, which size it (a web view
there takes the pointer); round a submission's file that room is painted in the page's own ground.

What each is now: a tool opened whole (its `ToolPopup`; a tool handing something to another turns into it there); Knewton Alta
and Macmillan (homework, not quizzes); Hand In (with its Send from Phone panel and its files' preview); a submission's
review; the rubric; New message (the Inbox's and a course's People's); New task; What's New; What-if assignment and
Grading scale; the Calendar's Calendars; a course's Nickname; the pane's History and Help — popups. The quick look and
a counter's panel stay cards on the page that grow out of their rows and cards, taking nothing from the window either
(The quick look, above). Pinned tools' flyouts and their pulled-out windows, menus and the search's suggestions are as they were.
Staying sheets over the window, dimming it: a quiz being taken (`QuizPopup`: its integrity and its clock), the first
setup, the sign-in's "Stay logged in?", the tour (its own veil), and the questions that must be answered first (Sign
out?, Delete this task?, Uninstall, Keep Simpl on your taskbar?) — and a development run's biology foundation demo.

## Knewton Alta

`Screens/Alta/`: a Knewton Alta assignment in a large popup (`AltaPopup`, the Mac's AltaQuizScreen) — its objectives and
their mastery down the left, Alta's own page in the rest, in a web view of the popup's own (`AltaSession`) signed in as
the app is, which a script in each of its frames reads (`AltaHook`, the Mac's copy): its question laid out alone on
panels, its buttons as the popup's own, its cover as the popup's start screen, its pop-ups as the popup's dialogs.
`--open alta:104:4003` opens one at launch, by the assignment's launch through Canvas; `--open
alta:104:4003:/mock-alta/learn/course/c1/assignment/a3?sample=1` opens it on a page of the mock's own instead — Alta's
learn page with a question, the rail filled from the player's own answer — and `?sample=dialog` there also opens a pop-up
built as Alta's are, its rich body coming in a second and a half after it (`window.mockRichDialog`), for the pictures.

A pop-up of Alta's (0.3.5) is hidden in the page as soon as the page script sees it and read once it has settled —
nothing it says changed for a moment, nothing in it loading, its fade done; three seconds at most, five while its body
says nothing — and again at each change (a next step): its title, its body as plain HTML (its formulas as their TeX,
each picture, drawing or formula without TeX made into a picture by the page, on a canvas at twice its size) and every
button, Alta's main one told. The popup draws it as its own dialog (`AltaDialogCard`: the body as `Kit/RichText` draws
the school's rich text, its links followed in Alta's page, its buttons pressing Alta's). Before, the pop-up was read the
moment it was seen and pictured 380 ms later — often before its body came in, or as it faded in, or blank under the
page's veil — and once hidden it could be neither read whole again (its words ran together) nor pictured again (the
popup's dialog covers the web view), so it showed its title and buttons round an empty body. A pop-up that holds what
the app cannot draw (a video, a form, another site's frame), whose body has nothing that can be read, or that the page
script fails to read is never shown empty, nor left out of sight: it is given back to the page, shown there as Alta
draws it until it closes (`DialogInPage`), and why goes in the Error Logs. A picture run waits for a pop-up seen to be
shown, and writes "alta: a pop-up shown as the app's own: …" with its words, formulas and pictures
(`scripts/dev/alta-test.mjs` reads the page script's side of it in Chromium).

Alta's page is light (0.3.5), as it is made — the owner's choice for every outside tool (0.3.1) — whatever the app's
appearance; until then Alta's popup followed the app, its question's colours turned over on a dark ground in dark mode.
Above the objectives, Light and Dark (`AltaPageBar`, under the words "Alta's page") choose, and the choice is kept for
every assignment (`altaDark`; `--alta-look dark` starts a development run in Dark). Dark is the old dark look. The
page is told the student prefers light (`LightPages`, as an outside tool's), and turned over by the page script when
Dark is chosen. What the popup shows in the page's place — the page's ground, the panels behind its question, its
buttons and the graphs' Light or Dark at the foot, the start screen, the skeleton, Loading — is in the page's look:
that part of the popup (`region`) is dressed with WPF-UI's own theme for the look, the light theme's secondary text
solid as the app's, and the accent's shades for it written as WPF-UI writes them (`AltaLook.Look`; nothing of its own
when the look is the app's). The header, the objectives and the popup's own dialogs keep the app's appearance; a
graph's window takes the page's look.

Beside Light and Dark, Show browser (0.3.5; "Browser" where the column is narrow) shows Alta's own page as it is, as in
a browser — the same web view, signed in, on white: its header, title and mastery, its own buttons, its cover, its
pop-ups in it — in place of the question laid out; the objectives stay and keep reading the page. Off, the question is
laid out again (a pop-up open in the page taken up as the app's again). The page script is told before each page and
at once (`window.__simplBrowser`, `__simplBrowserNow`): it lays nothing out, hides nothing, gives the app no buttons or
panels and never veils the page; Light and Dark wait meanwhile. Kept for every assignment (`altaBrowser`;
`--alta-browser` starts a development run with it).

## Icons

WPF-UI 4.3 draws only the Fluent icons whose code is at most U+FFFF; the rest show as a stray character. Check a new
`SymbolRegular` against that before using it.
