# Simpl for Windows — Windows 11 look

The Mac app's screens, steps and layouts (`mac/Simpl`), drawn with Windows 11's own design: Mica, the Settings app's
navigation pane and cards, Segoe UI Variable, Fluent System Icons, WinUI's motion. WPF on .NET 8 with
[WPF-UI](https://github.com/lepoco/wpfui) 4.3. The first Windows app (`windows/Simpl`) is untouched, for rollback.

Build (Windows, or Linux with the targeting pack): `dotnet build windows/SimplFluent/SimplFluent.csproj -c Release -p:EnableWindowsTargeting=true`.
CI (`.github/workflows/windows-fluent.yml`) builds it once (the app, Simpl-Setup.exe and the zip, kept as `release`),
then at the same time pictures it against the mock Canvas in eight parts, tries its installer and walks it for its
timings (`windows-fluent-perf`); the parts come back together as `windows-fluent-shots`. A push takes about five minutes. A preview (`windows-fluent-v<run>`) is published
when a commit message carries `[preview]`, or by Run workflow, which publishes the green run already made for the
commit (or the run named by `from_run`) as it is, in about a minute. Keep changes to the workflow in commits of their
own: GitHub lets a workflow tag a commit only if the commit itself leaves the workflows alone.

## Installing

Each preview ships two ways: **Simpl-Setup.exe**, the installer, and **Simpl-Windows-x64.zip**, the same folder to
unzip anywhere (portable). Neither is code-signed yet: SmartScreen's "More info → Run anyway". The installer is
Inno Setup 6's (`Installer/Simpl.iss`; Inno Setup 7 compiles it too), compiled by CI right after `dotnet publish`, the
build's version passed in:

```
ISCC /DAppVersion=0.2.0.12 "/DAppDisplayVersion=0.2.0 (v012)" /DSourceDir=<dist\Simpl> /DOutputDir=<dist> windows\SimplFluent\Installer\Simpl.iss
```

What the installer does:

- installs for the current user only, with no administrator prompt (`PrivilegesRequired=lowest`), into
  `%LOCALAPPDATA%\Programs\Simpl` — never Simpl's own data folder, `%LOCALAPPDATA%\Simpl` (settings, grade history,
  the web view's data; the saved sign-in is in Credential Manager);
- asks one thing — a desktop shortcut too? (unticked) — then installs; the last page has **Open Simpl** ticked;
- makes a Start menu shortcut (and the desktop one), both carrying Simpl's app id `SimplCourses.Simpl`, the one
  `Program.Main` sets first of all (`AppIdentity`), so the window, its taskbar button and the shortcuts are one app and
  pinning pins Simpl; it registers that id's name and picture for notifications (`HKCU\Software\Classes\AppUserModelId`);
- adds Simpl to Settings ▸ Apps (AppId `{B65F70B7-E13E-4CCC-9E1C-0B659660D130}`, per user); uninstalling removes the
  program, its shortcuts and that entry and **keeps** `%LOCALAPPDATA%\Simpl` and the saved sign-in in Credential
  Manager, and says where both are;
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

Updates (`Support/Updates.cs`): a copy the installer put here — its uninstall entry names the folder Simpl.exe runs
from — downloads the new release's Simpl-Setup.exe (checked against the size and SHA-256 GitHub publishes) to
`%TEMP%\Simpl-Update\<tag>`, and **Restart to Update** runs it with the arguments above and quits Simpl. A portable
copy opens the release's page, as before. Every Windows 11-look build is published as a GitHub pre-release, so a
**beta** is marked by "beta" in the release's title, not by that flag: with Get beta updates off (the default), the
usual previews are offered. CI tries the installer on every build: it installs silently into a folder of its own,
checks Simpl.exe, the Start menu shortcut (and its app id) and the Settings ▸ Apps entry, opens the installed Simpl
against the mock to picture the Dashboard (`installed-dashboard`; its log must say it knows it was installed); runs
the update exactly as Restart to Update does over a Simpl left open (it must take the quit event, Setup must see it
go, and `/LAUNCH` must open Simpl again); runs an update that cannot finish (a stand-in holding Simpl's mutex: Setup
must give up and open the old copy again); uninstalls silently with that Simpl open (the uninstaller must close it),
and checks the folder, the shortcut and the entry are gone and Simpl's data folder is not; then installs again and
uninstalls from Simpl itself (`--uninstall yes`: Simpl must start its uninstaller and quit, and the uninstaller — both
its phases — must finish), checking the same. The installer's and each Simpl's logs are kept with the pictures.

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
stalls (input kept waiting over 200 ms, probed twice a second while the window shows) and memory (.NET's, the
process's, the web view's processes').

`--perf-walk <folder>` walks one fixed route once the first answers are in — To Do, the Calendar, Grades, the Inbox, a
course, an assignment, Notifications, the New task sheet, Settings, Tools, a search typed a letter at a time, the
window dragged and resized, the Dashboard again — writes `perf.json` and `perf.txt` there (and the table to
simpl.log), and quits. Each step: drawn (`ui`), its answers in and shown (`data`), the builds and calls it took, the
longest stall, the frames in its first 700 ms and the longest gap between them, the memory it allocated.
`--perf-name warm` names the files `perf-warm.*`; `--perf-course` and `--perf-assignment` choose what is opened (else
the first course, and the first assignment To Do lists); `--perf-timeout` (150 s) ends a walk that cannot finish,
writing what it has; a first setup still to answer stops it, with a note. The walk only looks: New task and Settings
are opened and closed, nothing saved.

On a student's own PC: quit Simpl from the notification area (right-click its icon, Quit), then from Command Prompt
(a copy from the zip: its own Simpl.exe):

```
"%LOCALAPPDATA%\Programs\Simpl\Simpl.exe" --perf-walk "%USERPROFILE%\Desktop"
```

`perf.txt` lands on the desktop; its first two lines say what the PC is, which decides most of the rest.

CI's Performance walk, beside the pictures, does the same against the mock twice with one profile — `cold`, then
`warm` (the answers kept) — prints both tables in its log and the run's summary, keeps them as `windows-fluent-perf`
and with the pictures, and compares each with the last green run's (before, after, the change). It only reports: a
slower number never fails a run, and nothing waits for it but the pictures' merge.

## Layout of the code

| Folder | What is there |
| --- | --- |
| `Engine/` | The school's site in WebView2 as the app's engine (carried over): `Engine.Call<T>(name, args)` asks the page (the extension's `BCVNative.call`), navigation (`Go`, `Push`, `Open`, `OpenWeb`, `GoBack`), the sign-in assist. |
| `Models/` | The page's answers, generated from the Swift models (`windows/tools/models.sh`). |
| `Kit/` | Windows 11 pieces in code: `K` (text ramp, icons, cards, settings rows, chips, rings, bars, avatars, load and empty states), `B` (theme brush keys), `Motion` (WinUI curves and transitions), `SelectorBar`, `CheckCircle`, `Heading`, `Sheet`/`SheetHost` (dialogs), `Glyph` (symbols for the school's things), `Styles.xaml` (card, subtle and row buttons). |
| `Shell/` | The window (`MainWindow`), the Settings-style shell (`AppShell`, `NavPane`), Settings, Setup, What's New, the tour (`Tour`), keeping Simpl on the taskbar (`Taskbar`: asked once when the tour is followed to its end, and Settings ▸ General's Pin to taskbar…), dialogs, the tray, the web window. `Shell/Search/`: the search box's suggestions and commands (`SearchPalette`, Ctrl+K). |
| `SignIn/` | Finding the school, confirming it, the sign-in card, "Logging you in", the handoff bar. |
| `Screens/` | One class per screen, built on `Screen`; `ScreenFactory` says which screen shows a place or a route. A folder per area: `Todo/`, `Calendar/` (with Notifications), `Grades/`, `Inbox/` (with Groups and New Message), `Course/` (All Courses, a course's home, its sections, pages, files), `Assignment/` (with Hand In, feedback, the rubric), `More/` (Search, External Tools, Tools, a discussion), `Quiz/`, `Alta/` (the Alta and Macmillan popups), `Preview/` (the quick look). |
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
- Motion: `Motion.Enter` / `Stagger` for content arriving, `SlideIn` for steps, `DrillIn` for something opened;
  everything respects Windows' "Animation effects" and is off in the screenshot suite.
- Never `using` both `Wpf.Ui.Controls` and `System.Windows.Controls` without aliasing the clashes (`Button`,
  `TextBlock`, `TextBox`, `Image`, `MenuItem`). Inside a `Screen`, the engine is the field `engine`.

## The quick look

A row of work (the Dashboard's, To Do's, the Calendar's, a counter's panel) grows a card out of itself with
`WorkPreview.Press(item, row)`: build the `PreviewItem` with `WorkPreview.Dash`, `Work` or `Sheet`, or by hand (the
Calendar's `CalendarPeek`). The card reads the work's details as it opens, Mark done ticks it, a double-click or a
Ctrl-click opens the work whole, and a click round it, Escape or Close folds it back into its row.

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
