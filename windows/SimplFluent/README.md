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
| `Shell/` | The window (`MainWindow`), the Settings-style shell (`AppShell`, `NavPane`), Settings, Setup, What's New, dialogs, the web window. |
| `SignIn/` | Finding the school, confirming it, the sign-in card, "Logging you in", the handoff bar. |
| `Screens/` | One class per screen, built on `Screen`; `ScreenFactory` says which screen shows a place or a route. |

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
