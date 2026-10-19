using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Shell;

/// <summary>The courses and goals the setup reads and writes (setupInfo), as the page answers them.</summary>
public sealed class SetupChoices
{
    public sealed class Course
    {
        public string Id { get; set; } = "";
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string Nickname { get; set; } = "";
        public string Color { get; set; } = "";
        public bool On { get; set; }
        public string Target { get; set; } = "A+";
        /// <summary>(2.99.33) The name the navigation pane will show (its nickname, else its name), and what is new
        /// about it — its real title under a nickname, its term, its teacher ("Fall 2026 · Yue Lei"). Null from an
        /// older page.</summary>
        public string? Label { get; set; }
        public string? Sub { get; set; }
    }

    public bool Done { get; set; }
    public List<Course> Courses { get; set; } = new();
    public List<string> Grades { get; set; } = new();
    public double Goal { get; set; } = 4;
    public bool Tracking { get; set; } = true;
    /// <summary>(2.99.33) The goal as the engine writes it ("4.00").</summary>
    public string? GoalText { get; set; }
}

/// <summary>
/// The guided setup (the Mac's SetupScreen), as a Windows 11 dialog over the window: straight to the courses Simpl shows
/// (the school's search has already said "Welcome to Simpl"), each by one name on one line; a nickname for each, if you
/// like; the grade history, the term's GPA goal and the grade aimed for in each course; and a read-back. Back and Next
/// sit at its foot with Windows' pips for the four steps, and each step slides in from the side it is going to. Shown on
/// the first run, every current course ticked, with Set up later (the app works as it stands until then, and the setup
/// is not put up again at launch); and again from Settings ▸ General ▸ Courses shown and goals, with Cancel.
/// </summary>
public sealed class SetupSheet
{
    private enum Step { Courses, Nicknames, Grades, Done }

    /// <summary>The first run's setup was put off (Set up later) and not finished since: not put up again at launch
    /// (Engine), and the tour may be offered meanwhile.</summary>
    public static bool Later => Prefs.Bool(Engine.Engine.SetupLaterKey);

    private readonly Engine.Engine engine;
    private readonly Sheet sheet = new() { Bare = true, Width = 640, Height = 560, Modal = true };
    private readonly ContentControl stepHost = new() { Focusable = false };
    private readonly Grid foot = new();
    private SetupChoices? d;
    private string? loadError;
    private Step step = Step.Courses;
    private bool forward = true, saving;
    private string? error;
    private readonly HashSet<string> chosen = new();
    private readonly Dictionary<string, string> nicknames = new(), targets = new();
    private bool tracking = true;
    private double goal = 4;

    public static void Show(Engine.Engine engine, Window over) => new SetupSheet(engine).Open(over);

    /// <summary>Shown and put away this run, finished or not (the Mac's sawSetup): the tour may follow it.</summary>
    public static bool Seen { get; private set; }

    private SetupSheet(Engine.Engine engine)
    {
        this.engine = engine;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        stepHost.ClipToBounds = true;
        root.Children.Add(stepHost.Row(0));
        var footBorder = new Border { Child = foot, Padding = new Thickness(24, 16, 24, 16), BorderThickness = new Thickness(0, 1, 0, 0) };
        footBorder.SetResourceReference(Border.BackgroundProperty, "ContentDialogBackground");
        footBorder.SetResourceReference(Border.BorderBrushProperty, "ContentDialogSeparatorBorderBrush");
        root.Children.Add(footBorder.Row(1));
        sheet.Body = root;
        sheet.Closed += () => Seen = true;
    }

    private async void Open(Window over)
    {
        Dialogs.Show(sheet, over);
        Draw();
        try
        {
            d = await engine.Call<SetupChoices>("setupInfo");
            // (--setup-first yes, a development run's: the setup as a first run shows it — Set up later, no Cancel — for
            //  the pictures, whose student has set up before)
            if (Launch.IsDevelopment && Launch.Flag("setup-first")) d.Done = false;
            foreach (var c in d.Courses)
            {
                if (c.On && d.Done) chosen.Add(c.Id); // (0.3.40) a first run ticks nothing: the student picks the courses they take; run again, it starts from those chosen then
                nicknames[c.Id] = c.Nickname ?? "";
                targets[c.Id] = string.IsNullOrEmpty(c.Target) ? "A+" : c.Target;
            }
            tracking = d.Tracking;
            goal = GoalFigure.Value(d.Goal);
            sheet.Modal = !d.Done;
        }
        catch (Exception e) { loadError = e.Message; sheet.Modal = false; }
        Draw();
        // (the screenshot suite's --setup-save yes: run through to the end and saved, as Start using Simpl does)
        if (d != null && !autoSaved && Launch.Get("setup-save") == "yes")
        {
            autoSaved = true;
            step = Step.Done;
            Draw();
            await Save();
        }
    }

    private static bool autoSaved;

    private void Draw()
    {
        FrameworkElement content;
        if (d == null) content = K.LoadState(loadError, () => { loadError = null; Draw(); _ = Retry(); });
        else content = step switch
        {
            Step.Courses => Courses(d),
            Step.Nicknames => Nicknames(d),
            Step.Grades => Grades(d),
            _ => ReadBack(d),
        };
        stepHost.Content = content;
        if (d != null) Motion.SlideIn(content, forward);
        Footer();
        Shot.Moved();
    }

    private async Task Retry()
    {
        try { d = await engine.Call<SetupChoices>("setupInfo"); } catch (Exception e) { loadError = e.Message; }
        Draw();
    }

    // ---- the steps ----

    /// <summary>A step's title and the line under it, in grey (onboarding-1: an accent-coloured line read as a link).</summary>
    private static FrameworkElement Heading(string title, string sub) =>
        K.V(4, K.Text(title, Ty.Subtitle), K.Wrapped(sub, Ty.Body, B.Text2)).Margin(30, 26, 30, 14);

    /// <summary>The course's own name: what the navigation pane shows when it has no nickname.</summary>
    private static string OwnName(SetupChoices.Course c) => !string.IsNullOrEmpty(c.Name) ? c.Name : c.Code ?? "";

    /// <summary>The name the pane will show once this is saved: the nickname typed here (a saved one arrives typed), else
    /// the course's own.</summary>
    private string Shown(SetupChoices.Course c) =>
        nicknames.TryGetValue(c.Id, out var n) && !string.IsNullOrWhiteSpace(n) ? n.Trim() : OwnName(c);

    /// <summary>What the page says is new about a course (its term and teacher, its real title under a nickname), less
    /// anything that only repeats <paramref name="name"/> — never the code over the same name spelled another way
    /// ("MATH-021-20" over "F26-MATH 021 20").</summary>
    private static string Detail(SetupChoices.Course c, string name)
    {
        var sub = (c.Sub ?? "").Trim();
        if (sub == name) return "";
        if (sub.StartsWith(name + " · ", StringComparison.Ordinal)) return sub[(name.Length + 3)..];
        return sub;
    }

    /// <summary>A course on one line (onboarding-30): the pane's name for it, bold, and after it in grey what that name
    /// does not say, cut with an ellipsis where it runs out of room — the whole of it in the tooltip — so a term's list
    /// is in view.</summary>
    private static FrameworkElement Line(string name, string more)
    {
        var bold = K.Text(name, Ty.BodyStrong);
        bold.TextTrimming = TextTrimming.CharacterEllipsis;
        bold.MaxWidth = 300;
        var line = K.Columns("auto, *", bold.Col(0));
        if (more.Length > 0)
        {
            var grey = K.Text(more, Ty.Body, B.Text2);
            grey.TextTrimming = TextTrimming.CharacterEllipsis;
            grey.Margin = new Thickness(12, 0, 0, 0);
            grey.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(grey.Col(1));
        }
        line.ToolTip = more.Length > 0 ? $"{name} · {more}" : name;
        return line;
    }

    /// <summary>A course by its own name, and what is new about it under it (the nicknames step, where the name typed
    /// beside it would replace it).</summary>
    private static FrameworkElement Names(SetupChoices.Course c)
    {
        var name = OwnName(c);
        var v = K.V(1, K.Text(name, Ty.BodyStrong));
        if (Detail(c, name) is { Length: > 0 } more) v.Children.Add(K.Wrapped(more, Ty.Caption, B.Text2, 1));
        return v;
    }

    private static FrameworkElement Scroll(UIElement content) =>
        new ScrollViewer { Content = new Border { Child = content, Padding = new Thickness(0, 0, 0, 20) }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(30, 0, 30, 0) };

    private FrameworkElement Courses(SetupChoices data)
    {
        // (onboarding-1) what a tick does — the course in the pane and on the Dashboard, and on Canvas one of the student's
        // favourites (Brightspace's ticks are kept on this PC) — and where the GPA is decided instead: never "only the
        // courses that count towards your GPA", which hid a lab's or a pass/fail course's work
        const string shownWhere = "Ticked courses show in the navigation pane and on your Dashboard";
        const string gpa = "Pass/Fail, on the Grades step, keeps a course out of your GPA.";
        var head = Heading("Which courses should Simpl show?", engine.OnBrightspace ? $"{shownWhere}. {gpa}" : $"{shownWhere}, and become your Canvas favourites. {gpa}");
        if (data.Courses.Count == 0)
            return K.Rows("auto, *", head.Row(0), Scroll(K.Row(SymbolRegular.Info24, "No active courses right now", "You can finish now and choose courses later from Settings.")).Row(1));
        var count = K.Text($"{chosen.Count} of {data.Courses.Count} selected", Ty.Body, B.Text2);
        var all = K.Link(chosen.Count == data.Courses.Count ? "Clear all" : "Select all", () =>
        {
            if (chosen.Count == data.Courses.Count) chosen.Clear(); else foreach (var c in data.Courses) chosen.Add(c.Id);
            forward = true;
            Redraw();
        });
        var bar = K.Columns("*, auto", count.Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(0), all.Col(1));
        var rows = K.V(0);
        foreach (var c in data.Courses)
        {
            var name = Shown(c);
            var cb = new CheckBox { IsChecked = chosen.Contains(c.Id), Content = Line(name, Detail(c, name)), VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(10, 0, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            System.Windows.Automation.AutomationProperties.SetName(cb, name);
            var id = c.Id;
            cb.Checked += (_, _) => { chosen.Add(id); count.Text = $"{chosen.Count} of {data.Courses.Count} selected"; Footer(); };
            cb.Unchecked += (_, _) => { chosen.Remove(id); count.Text = $"{chosen.Count} of {data.Courses.Count} selected"; Footer(); };
            if (rows.Children.Count > 0) rows.Children.Add(K.Divider());
            // (rows about 40 tall, a one-line course each: the eight courses of most terms in view at once on a 1024×681
            //  window, nothing under the footer)
            cb.MinHeight = 28;
            rows.Children.Add(cb.Margin(16, 4, 16, 4));
        }
        var list = K.V(8, bar.Margin(2, 0, 2, 0), K.Card(rows, new Thickness(0), 4));
        return K.Rows("auto, *", head.Row(0), Scroll(list).Row(1));
    }

    private void Redraw()
    {
        var c = stepHost.Content;
        Draw();
        _ = c;
    }

    private FrameworkElement Nicknames(SetupChoices data)
    {
        var head = Heading("Nicknames", "Give a course a shorter name, if you like. Leave it empty to keep its own.");
        var picked = data.Courses.Where(c => chosen.Contains(c.Id)).ToList();
        var rows = K.V(4);
        if (picked.Count == 0) rows.Children.Add(K.Row(null, "No courses chosen: nothing to name.", null));
        foreach (var c in picked)
        {
            var box = new TextBox { PlaceholderText = OwnName(c), Text = nicknames.TryGetValue(c.Id, out var n) ? n : "", Width = 220, MaxLength = 60 };
            var id = c.Id;
            box.TextChanged += (_, _) => nicknames[id] = box.Text;
            var row = K.Card(K.Columns("*, auto", Names(c).Margin(0, 0, 16, 0).Align(HorizontalAlignment.Stretch, VerticalAlignment.Center).Col(0), box.Col(1)), new Thickness(16, 12, 16, 12), 4);
            rows.Children.Add(row);
        }
        rows.Children.Add(K.Wrapped(engine.OnBrightspace ? "A nickname shows everywhere in place of the course’s name — on this PC only." : "A nickname shows everywhere in place of the course’s name — in Canvas too.", Ty.Caption, B.Text2).Margin(2, 6, 2, 0));
        return K.Rows("auto, *", head.Row(0), Scroll(rows).Row(1));
    }

    private FrameworkElement Grades(SetupChoices data)
    {
        var head = Heading("Grades", $"{engine.LmsName} keeps no history. Simpl can, on this PC.");
        var keep = new ToggleSwitch { IsChecked = tracking };
        // (onboarding-20: one name for each, as Settings ▸ Grades says them — grade history, the term GPA goal — and the
        //  goal with its two decimals, kept half up as the engine keeps it)
        // (three places kept by the box, so it never rounds a typed 3.425 its own way first: GoalFigure rounds it, and
        //  shows two)
        var goalBox = new NumberBox { Value = goal, Minimum = 0, Maximum = 4, SmallChange = 0.05, MaxDecimalPlaces = 3, Width = 140, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, NumberFormatter = new GoalFigure.Formatter() };
        goalBox.ValueChanged += (_, _) => goal = GoalFigure.Value(goalBox.Value ?? 4);
        var goalRow = K.Row(SymbolRegular.Target24, "Term GPA goal", null, goalBox);
        goalRow.Visibility = tracking ? Visibility.Visible : Visibility.Collapsed;
        keep.Checked += (_, _) => { tracking = true; goalRow.Visibility = Visibility.Visible; };
        keep.Unchecked += (_, _) => { tracking = false; goalRow.Visibility = Visibility.Collapsed; };
        var v = K.V(4, K.Row(SymbolRegular.History24, "Keep grade history", "One snapshot a day, on this PC.", keep), goalRow);
        v.Children.Add(K.SectionHeader("Aiming for", 24));
        var picked = data.Courses.Where(c => chosen.Contains(c.Id)).ToList();
        if (picked.Count == 0) v.Children.Add(K.Row(null, "No courses chosen: nothing to aim at yet.", null));
        var grades = data.Grades.AsEnumerable().Reverse().Concat(new[] { "Pass/Fail" }).ToList();
        foreach (var c in picked)
        {
            var label = Shown(c);
            var box = new ComboBox { ItemsSource = grades, MinWidth = 140 };
            var t = targets.TryGetValue(c.Id, out var tv) ? tv : "A+";
            box.SelectedItem = t == "P/F" ? "Pass/Fail" : grades.Contains(t) ? t : grades.FirstOrDefault();
            var id = c.Id;
            box.SelectionChanged += (_, _) => targets[id] = box.SelectedItem as string == "Pass/Fail" ? "P/F" : box.SelectedItem as string ?? "A+";
            v.Children.Add(K.Card(K.Columns("auto, *, auto", K.Dot(K.Hex(c.Color), 10).Margin(0, 0, 12, 0).Col(0), K.Text(label, Ty.Body).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(1), box.Col(2)), new Thickness(16, 10, 16, 10), 4));
        }
        v.Children.Add(K.Wrapped("Pass/Fail keeps a course out of your GPA.", Ty.Caption, B.Text2).Margin(2, 6, 2, 0));
        return K.Rows("auto, *", head.Row(0), Scroll(v).Row(1));
    }

    private FrameworkElement ReadBack(SetupChoices data)
    {
        var seal = K.Icon(SymbolRegular.CheckmarkStarburst24, 44, Color.FromRgb(0x2f, 0xa8, 0x4f), filled: true);
        seal.HorizontalAlignment = HorizontalAlignment.Left;
        var head = K.V(6, seal, K.Text("You’re set", Ty.Subtitle), K.Wrapped("Change any of this later in Settings (Ctrl+,) ▸ General ▸ Courses shown and goals.", Ty.Body, B.Text2));
        head.Margin = new Thickness(30, 26, 30, 14);
        var s = new ScaleTransform(0.6, 0.6);
        seal.RenderTransformOrigin = new Point(0.5, 0.5);
        seal.RenderTransform = s;
        Motion.FromTo(s, ScaleTransform.ScaleXProperty, 0.6, 1, 420, Motion.Gentle, 120);
        Motion.FromTo(s, ScaleTransform.ScaleYProperty, 0.6, 1, 420, Motion.Gentle, 120);
        var rows = K.V(4,
            K.Row(SymbolRegular.BookOpen24, "Courses shown", null, K.Text($"{chosen.Count} of {data.Courses.Count}", Ty.Body, B.Text2)),
            K.Row(SymbolRegular.History24, "Grade history", null, K.Text(tracking ? $"On · goal {GoalFigure.Text(goal)}" : "Off", Ty.Body, B.Text2)));
        if (error != null)
        {
            var bar = new InfoBar { Title = "Couldn’t save", Message = error, Severity = InfoBarSeverity.Error, IsOpen = true, IsClosable = false };
            rows.Children.Add(bar.Margin(0, 8, 0, 0));
        }
        return K.Rows("auto, *", head.Row(0), Scroll(rows).Row(1));
    }

    // ---- the foot: Cancel (or Set up later), the pips, Back, Next ----

    private void Footer()
    {
        foot.Children.Clear();
        foot.ColumnDefinitions.Clear();
        foreach (var w in new[] { "auto", "*", "auto" }) foot.ColumnDefinitions.Add(new ColumnDefinition { Width = K.Length(w) });
        var canCancel = d?.Done == true || (d == null && loadError != null);
        if (canCancel) foot.Children.Add(K.Button("Cancel", () => sheet.Close()).Col(0));
        else if (d != null)
        {
            // (onboarding-4) the first run's way past it — a save that failed too: never a dead end; the app works as it
            // stands meanwhile, and Settings ▸ General ▸ Courses shown and goals runs it whenever the student likes
            var later = Enabled(K.Button("Set up later", PutOff), !saving);
            later.ToolTip = "Use Simpl as it is now, and set it up any time from Settings ▸ General ▸ Courses shown and goals";
            foot.Children.Add(later.Col(0));
        }
        if (d != null)
        {
            // Windows' PipsPager: a pip for each step, the current one longer and in the accent
            var pips = K.H(6);
            var count = Enum.GetValues<Step>().Length;
            for (var i = 0; i < count; i++)
            {
                var on = i <= (int)step;
                var pip = new Border { Width = i == (int)step ? 18 : 6, Height = 6, CornerRadius = new CornerRadius(3) };
                pip.SetResourceReference(Border.BackgroundProperty, on ? B.Accent : B.StrongStroke);
                pips.Children.Add(pip);
            }
            pips.HorizontalAlignment = HorizontalAlignment.Center;
            pips.VerticalAlignment = VerticalAlignment.Center;
            System.Windows.Automation.AutomationProperties.SetName(pips, $"Step {(int)step + 1} of {count}");
            foot.Children.Add(pips.Col(1));
        }
        if (d == null) return;
        var buttons = K.H(8);
        if (step > Step.Courses) buttons.Children.Add(Enabled(K.Button("Back", () => Go(-1)), !saving));
        var word = step switch
        {
            Step.Done => saving ? "Saving…" : d.Done ? "Done" : "Start using Simpl",
            _ => "Next",
        };
        var next = K.Button(word, Primary, accent: true);
        next.IsEnabled = !saving && !(step == Step.Courses && chosen.Count == 0 && d.Courses.Count > 0);
        next.IsDefault = true;
        buttons.Children.Add(next);
        foot.Children.Add(buttons.Col(2));
    }

    private static T Enabled<T>(T e, bool on) where T : UIElement { e.IsEnabled = on; return e; }

    private void Primary()
    {
        if (step == Step.Done) _ = Save();
        else Go(1);
    }

    private void Go(int by)
    {
        var next = (int)step + by;
        if (next < 0 || next > (int)Step.Done) return;
        forward = by > 0;
        step = (Step)next;
        Draw();
    }

    private async Task Save()
    {
        if (saving || d == null) return;
        saving = true;
        error = null;
        Footer();
        var picked = d.Courses.Where(c => chosen.Contains(c.Id)).ToList();
        var args = new
        {
            courses = chosen.ToArray(),
            nicknames = picked.ToDictionary(c => c.Id, c => nicknames.TryGetValue(c.Id, out var n) ? n : ""),
            targets = picked.ToDictionary(c => c.Id, c => targets.TryGetValue(c.Id, out var t) ? t : "A+"),
            tracking,
            goal,
        };
        try { await engine.Call<OK>("setupSave", args); }
        catch (Exception e)
        {
            Log.Write("setup save: " + e);
            error = e.Message;
            forward = true;
            sheet.Modal = false; // (a save that fails never keeps the student in: Escape, or Set up later, lets them go)
            saving = false;
            Draw();
            return;
        }
        // saved: what follows tells the rest of the app, and nothing it meets can make the save look as if it failed
        saving = false;
        Quietly("pending", () => { Prefs.Set(Engine.Engine.SetupPendingKey, false); Prefs.Remove(Engine.Engine.SetupLaterKey); });
        Quietly("close", sheet.Close);
        Quietly("data", engine.DataChanged);
        _ = engine.LoadSidebar(); // (the pane's courses: the ones just chosen, by their new names)
        NoticesOn();
    }

    /// <summary>
    /// (onboarding-4) Set up later: nothing saved — the courses stay as the school's site has them, the Dashboard working
    /// with them as it does — and the setup not put up again at launch; Settings ▸ General ▸ Courses shown and goals (or
    /// Courses ▸ Choose courses) runs it whenever the student likes.
    /// </summary>
    private void PutOff()
    {
        Prefs.Set(Engine.Engine.SetupLaterKey, true);
        Prefs.Set(Engine.Engine.SetupPendingKey, false);
        sheet.Close();
        NoticesOn();
    }

    /// <summary>The Mac's NotificationAsk.afterSetup: once the first setup is done or put off, reminders and new-activity
    /// alerts are on — Windows asks nothing for them; Settings ▸ Notifications turns either off.</summary>
    private static void NoticesOn()
    {
        if (Prefs.Bool("notifications:asked") || Launch.IsDevelopment) return;
        Quietly("notifications", () =>
        {
            Prefs.Set("notifications:asked", true);
            Prefs.Set("reminders:on", true);
            Prefs.Set("activity:on", true);
            Notices.SettingsChanged();
        });
    }

    private static void Quietly(string what, Action a)
    {
        try { a(); } catch (Exception e) { Log.Write($"setup saved, then {what}: {e}"); }
    }
}
