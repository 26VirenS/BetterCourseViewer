using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Support;
using Wpf.Ui.Controls;
using Image = System.Windows.Controls.Image;
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
    }

    public bool Done { get; set; }
    public List<Course> Courses { get; set; } = new();
    public List<string> Grades { get; set; } = new();
    public double Goal { get; set; } = 4;
    public bool Tracking { get; set; } = true;
}

/// <summary>
/// The guided setup (the Mac's SetupScreen), as a Windows 11 dialog over the window: a welcome; the courses that count,
/// by their names; a nickname for each, if you like; the grade history, its GPA goal and the grade aimed for in each
/// course; and a read-back. Back and Next sit at its foot with Windows' pips for the steps between, and each step slides
/// in from the side it is going to. Shown on the first run, and again from Settings ▸ General ▸ Courses and goals
/// (straight to the courses, with Cancel).
/// </summary>
public sealed class SetupSheet
{
    private enum Step { Welcome, Courses, Nicknames, Grades, Done }

    private readonly Engine.Engine engine;
    private readonly Sheet sheet = new() { Bare = true, Width = 640, Height = 560, Modal = true };
    private readonly ContentControl stepHost = new() { Focusable = false };
    private readonly Grid foot = new();
    private SetupChoices? d;
    private string? loadError;
    private Step step = Step.Welcome;
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
            foreach (var c in d.Courses)
            {
                if (c.On) chosen.Add(c.Id);
                nicknames[c.Id] = c.Nickname;
                targets[c.Id] = string.IsNullOrEmpty(c.Target) ? "A+" : c.Target;
            }
            tracking = d.Tracking;
            goal = d.Goal;
            if (d.Done) step = Step.Courses; // (run again from Settings: straight to the courses)
            sheet.Modal = !d.Done;
        }
        catch (Exception e) { loadError = e.Message; sheet.Modal = false; }
        Draw();
    }

    private void Draw()
    {
        FrameworkElement content;
        if (d == null) content = K.LoadState(loadError, () => { loadError = null; Draw(); _ = Retry(); });
        else content = step switch
        {
            Step.Welcome => Welcome(),
            Step.Courses => Courses(d),
            Step.Nicknames => Nicknames(d),
            Step.Grades => Grades(d),
            _ => ReadBack(d),
        };
        stepHost.Content = content;
        if (d != null && step != Step.Welcome) Motion.SlideIn(content, forward);
        Footer();
        Shot.Moved();
    }

    private async Task Retry()
    {
        try { d = await engine.Call<SetupChoices>("setupInfo"); } catch (Exception e) { loadError = e.Message; }
        Draw();
    }

    // ---- the steps ----

    private FrameworkElement Welcome()
    {
        var icon = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/Simpl-256.png")), Width = 88, Height = 88, HorizontalAlignment = HorizontalAlignment.Center };
        var title = K.Text("Welcome to Simpl", Ty.Title).Center();
        var sub = K.Wrapped($"{engine.LmsName}, simply, on your PC. A few quick questions and you’re in.", Ty.BodyLarge, B.Text2).Center();
        sub.TextAlignment = TextAlignment.Center;
        FrameworkElement Feature(SymbolRegular s, Color c, string t, string text) =>
            K.Columns("auto, *", K.IconTile(s, c, 36).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Col(0),
                K.V(2, K.Text(t, Ty.BodyStrong), K.Wrapped(text, Ty.Body, B.Text2)).Margin(14, 0, 0, 0).Col(1));
        var features = K.V(16,
            Feature(SymbolRegular.WeatherSunny24, Color.FromRgb(0xf7, 0x8c, 0x1a), "Dashboard", "What is due, what is new, and what was graded — at a glance."),
            Feature(SymbolRegular.DataBarVertical24, Color.FromRgb(0x2f, 0xa8, 0x4f), "Grades", "Rings for every course, your GPA, and what-if scores."),
            Feature(SymbolRegular.TaskListLtr24, Color.FromRgb(0x29, 0x7b, 0xe6), "Quizzes and hand-ins", "Take quizzes and hand in work without leaving the app."));
        features.MaxWidth = 420;
        features.Margin = new Thickness(0, 8, 0, 0);
        var v = K.V(20, icon, K.V(6, title, sub), features);
        v.HorizontalAlignment = HorizontalAlignment.Center;
        v.VerticalAlignment = VerticalAlignment.Center;
        v.Margin = new Thickness(48, 24, 48, 24);
        Motion.Stagger(new UIElement[] { icon }.Concat(v.Children.OfType<UIElement>().Skip(1)).Concat(features.Children.OfType<UIElement>()), 60, 10);
        return v;
    }

    private static FrameworkElement Heading(string title, string sub, bool accent = false)
    {
        var s = K.Wrapped(sub, Ty.Body, accent ? B.AccentText : B.Text2);
        if (accent) s.FontWeight = FontWeights.SemiBold;
        return K.V(4, K.Text(title, Ty.Subtitle), s).Margin(30, 26, 30, 14);
    }

    private static FrameworkElement Names(SetupChoices.Course c)
    {
        var v = K.V(1, K.Text(c.Code, Ty.BodyStrong));
        if (!string.IsNullOrEmpty(c.Name) && c.Name != c.Code) v.Children.Add(K.Wrapped(c.Name, Ty.Caption, B.Text2, 2));
        return v;
    }

    private static FrameworkElement Scroll(UIElement content) =>
        new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(30, 0, 30, 20) };

    private FrameworkElement Courses(SetupChoices data)
    {
        var head = Heading("Which classes are you in?", "Only select the courses that count towards your GPA.", accent: true);
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
            var cb = new CheckBox { IsChecked = chosen.Contains(c.Id), Content = Names(c), VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(10, 0, 0, 0) };
            var id = c.Id;
            cb.Checked += (_, _) => { chosen.Add(id); count.Text = $"{chosen.Count} of {data.Courses.Count} selected"; Footer(); };
            cb.Unchecked += (_, _) => { chosen.Remove(id); count.Text = $"{chosen.Count} of {data.Courses.Count} selected"; Footer(); };
            if (rows.Children.Count > 0) rows.Children.Add(K.Divider());
            rows.Children.Add(cb.Margin(16, 10, 16, 10));
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
            var box = new TextBox { PlaceholderText = c.Code, Text = nicknames.TryGetValue(c.Id, out var n) ? n : "", Width = 220, MaxLength = 60 };
            var id = c.Id;
            box.TextChanged += (_, _) => nicknames[id] = box.Text;
            var row = K.Card(K.Columns("*, auto", Names(c).Margin(0, 0, 16, 0).Align(HorizontalAlignment.Stretch, VerticalAlignment.Center).Col(0), box.Col(1)), new Thickness(16, 12, 16, 12), 4);
            rows.Children.Add(row);
        }
        rows.Children.Add(K.Wrapped(engine.OnBrightspace ? "A nickname shows everywhere in place of the course’s code — on this PC only." : "A nickname shows everywhere in place of the course’s code — in Canvas too.", Ty.Caption, B.Text2).Margin(2, 6, 2, 0));
        return K.Rows("auto, *", head.Row(0), Scroll(rows).Row(1));
    }

    private FrameworkElement Grades(SetupChoices data)
    {
        var head = Heading("Grades", $"{engine.LmsName} keeps no history. Simpl can, on this PC.");
        var keep = new ToggleSwitch { IsChecked = tracking };
        var goalBox = new NumberBox { Value = goal, Minimum = 0, Maximum = 4, SmallChange = 0.05, MaxDecimalPlaces = 2, Width = 140, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        goalBox.ValueChanged += (_, _) => goal = Math.Clamp(Math.Round((goalBox.Value ?? 4) * 100) / 100, 0, 4);
        var goalRow = K.Row(SymbolRegular.Target24, "GPA goal", null, goalBox);
        goalRow.Visibility = tracking ? Visibility.Visible : Visibility.Collapsed;
        keep.Checked += (_, _) => { tracking = true; goalRow.Visibility = Visibility.Visible; };
        keep.Unchecked += (_, _) => { tracking = false; goalRow.Visibility = Visibility.Collapsed; };
        var v = K.V(4, K.Row(SymbolRegular.History24, "Keep a history", "One snapshot a day, on this PC only.", keep), goalRow);
        v.Children.Add(K.SectionHeader("Aiming for", 24));
        var picked = data.Courses.Where(c => chosen.Contains(c.Id)).ToList();
        if (picked.Count == 0) v.Children.Add(K.Row(null, "No courses chosen: nothing to aim at yet.", null));
        var grades = data.Grades.AsEnumerable().Reverse().Concat(new[] { "Pass/Fail" }).ToList();
        foreach (var c in picked)
        {
            var label = nicknames.TryGetValue(c.Id, out var n) && n.Trim().Length > 0 ? n.Trim() : c.Code;
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
        var head = K.V(6, seal, K.Text("You’re set", Ty.Subtitle), K.Wrapped("Change any of this later in Settings (Ctrl+,) ▸ General ▸ Courses and goals.", Ty.Body, B.Text2));
        head.Margin = new Thickness(30, 26, 30, 14);
        var s = new ScaleTransform(0.6, 0.6);
        seal.RenderTransformOrigin = new Point(0.5, 0.5);
        seal.RenderTransform = s;
        Motion.FromTo(s, ScaleTransform.ScaleXProperty, 0.6, 1, 420, Motion.Gentle, 120);
        Motion.FromTo(s, ScaleTransform.ScaleYProperty, 0.6, 1, 420, Motion.Gentle, 120);
        var rows = K.V(4,
            K.Row(SymbolRegular.BookOpen24, "Courses shown", null, K.Text($"{chosen.Count} of {data.Courses.Count}", Ty.Body, B.Text2)),
            K.Row(SymbolRegular.History24, "Grade history", null, K.Text(tracking ? $"On · goal {goal:0.00}" : "Off", Ty.Body, B.Text2)));
        if (error != null)
        {
            var bar = new InfoBar { Title = "Couldn’t save", Message = error, Severity = InfoBarSeverity.Error, IsOpen = true, IsClosable = false };
            rows.Children.Add(bar.Margin(0, 8, 0, 0));
        }
        return K.Rows("auto, *", head.Row(0), Scroll(rows).Row(1));
    }

    // ---- the foot: Cancel, the pips, Back, Next ----

    private void Footer()
    {
        foot.Children.Clear();
        foot.ColumnDefinitions.Clear();
        foreach (var w in new[] { "auto", "*", "auto" }) foot.ColumnDefinitions.Add(new ColumnDefinition { Width = K.Length(w) });
        var canCancel = d?.Done == true || (d == null && loadError != null);
        if (canCancel) foot.Children.Add(K.Button("Cancel", () => sheet.Close()).Col(0));
        // (the first run's save failed: never a dead end — it can be done later from Settings)
        else if (error != null) foot.Children.Add(K.Button("Skip for Now", () => sheet.Close()).Col(0));
        if (d != null && step != Step.Welcome)
        {
            // Windows' PipsPager: a pip for each step after the welcome, the current one longer and in the accent
            var pips = K.H(6);
            for (var i = 1; i < 5; i++)
            {
                var on = i <= (int)step;
                var pip = new Border { Width = i == (int)step ? 18 : 6, Height = 6, CornerRadius = new CornerRadius(3) };
                pip.SetResourceReference(Border.BackgroundProperty, on ? B.Accent : B.StrongStroke);
                pips.Children.Add(pip);
            }
            pips.HorizontalAlignment = HorizontalAlignment.Center;
            pips.VerticalAlignment = VerticalAlignment.Center;
            System.Windows.Automation.AutomationProperties.SetName(pips, $"Step {(int)step} of 4");
            foot.Children.Add(pips.Col(1));
        }
        if (d == null) return;
        var buttons = K.H(8);
        if ((int)step > (d.Done ? 1 : 0)) buttons.Children.Add(Enabled(K.Button("Back", () => Go(-1)), !saving));
        var word = step switch
        {
            Step.Welcome => "Get started",
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
        try
        {
            await engine.Call<OK>("setupSave", args);
            Prefs.Set(Engine.Engine.SetupPendingKey, false);
            engine.DataChanged();
            _ = engine.LoadSidebar(); // (the pane's courses: the ones just chosen, by their new names)
            sheet.Close();
            // (the Mac's NotificationAsk.afterSetup: once the setup is done, reminders and new-activity alerts are on —
            // Windows asks nothing for them; Settings ▸ Notifications turns either off)
            if (!Prefs.Bool("notifications:asked") && !Launch.IsDevelopment)
            {
                Prefs.Set("notifications:asked", true);
                Prefs.Set("reminders:on", true);
                Prefs.Set("activity:on", true);
                Notices.SettingsChanged();
            }
        }
        catch (Exception e)
        {
            Log.Write("setup save: " + e);
            error = e.Message;
            forward = true;
            sheet.Modal = false; // (a save that fails never keeps the student in: Escape, or Skip for Now, lets them go)
            Draw();
        }
        saving = false;
        Footer();
    }
}
