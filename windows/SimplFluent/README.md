# Simpl for Windows — Windows 11 look

The Mac app's screens, steps and layouts (`mac/Simpl`), drawn with Windows 11's own design: Mica, the Settings app's
navigation pane and cards, Segoe UI Variable, Fluent System Icons, WinUI's motion. WPF on .NET 8 with
[WPF-UI](https://github.com/lepoco/wpfui) 4.3. The first Windows app (`windows/Simpl`) is untouched, for rollback.

Build (Windows, or Linux with the targeting pack): `dotnet build windows/SimplFluent/SimplFluent.csproj -c Release -p:EnableWindowsTargeting=true`.
CI (`.github/workflows/windows-fluent.yml`) builds it, pictures it against the mock Canvas, and publishes a preview
(`windows-fluent-v<run>`) when a commit message carries `[preview]`.

## Layout of the code

| Folder | What is there |
| --- | --- |
| `Engine/` | The school's site in WebView2 as the app's engine (carried over): `Engine.Call<T>(name, args)` asks the page (the extension's `BCVNative.call`), navigation (`Go`, `Push`, `Open`, `OpenWeb`, `GoBack`), the sign-in assist. |
| `Models/` | The page's answers, generated from the Swift models (`windows/tools/models.sh`). |
| `Kit/` | Windows 11 pieces in code: `K` (text ramp, icons, cards, settings rows, chips, rings, bars, avatars, load and empty states), `B` (theme brush keys), `Motion` (WinUI curves and transitions), `SelectorBar`, `CheckCircle`, `Heading`, `Sheet`/`SheetHost` (dialogs), `Glyph` (symbols for the school's things), `Styles.xaml` (card, subtle and row buttons). |
| `Shell/` | The window (`MainWindow`), the Settings-style shell (`AppShell`, `NavPane`), Settings, Setup, What's New, the tour (`Tour`), dialogs, the tray, the web window. `Shell/Search/`: the search box's suggestions and commands (`SearchPalette`, Ctrl+K). |
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

The whole tool opens under the Tools page's header ("Tools › Calculator", its pin beside the title) and, from a pin's
flyout, over the window (`ToolPopup`); the quick version is the pin's flyout. `ToolContext` hands it the engine, what
was handed over (`TakeFiles`, `TakeInfo`), and `OpenFull` / `Close` for the flyout. `ToolKit` has the page, two
columns, fields, notes and the drop zone; `Files` has Windows' file dialogs.

## Icons

WPF-UI 4.3 draws only the Fluent icons whose code is at most U+FFFF; the rest show as a stray character. Check a new
`SymbolRegular` against that before using it.
