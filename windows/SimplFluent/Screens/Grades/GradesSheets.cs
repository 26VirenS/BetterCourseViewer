using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Shell;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Screens;

/// <summary>The grades screens' sheets: a what-if assignment for a group, and a course's grading scale.</summary>
internal static class GradesSheets
{
    /// <summary>
    /// A what-if assignment for a group (the Mac's AddWhatIfSheet): its name, the score to try and what it is out of. Add
    /// hands them back; nothing is saved to the school.
    /// </summary>
    public static void AddWhatIf(Engine.Engine engine, CGGroup group, Action<string, double, double?> add)
    {
        var name = new TextBox { PlaceholderText = "What-if assignment", MaxLength = 120 };
        var score = new TextBox { PlaceholderText = "Optional" };
        var possible = new TextBox { PlaceholderText = "Points", Text = "100" };
        var note = K.Wrapped("", Ty.Caption, B.Text2);
        var body = K.V(14, Field("Name", name), Field("Score", score), Field("Out of", possible), note);
        var sheet = new Sheet { Title = "What-If Assignment", Body = body, Width = 460 };

        // what it is out of, when that is more than nothing
        double? Points() => GradesKit.Number(possible.Text) is { } p && p > 0 ? p : null;

        string Footnote()
        {
            var weight = string.IsNullOrEmpty(group.WeightText) ? "" : $" ({group.WeightText})";
            var counts = $"Counts in {group.Name}{weight}. Nothing is saved to {engine.LmsName}.";
            if (Points() is not { } p || GradesKit.Number(score.Text) is not { } s) return counts;
            return (s / p * 100).ToString("0.0", GradesKit.Inv) + "%. " + counts;
        }

        var addButton = sheet.Add("Add", SheetButtonKind.Primary, () =>
        {
            if (Points() is not { } p) return;
            var n = name.Text.Trim();
            add(n.Length == 0 ? "What-if assignment" : n, p, GradesKit.Number(score.Text));
        });
        sheet.Add("Cancel", SheetButtonKind.Close);
        void Update()
        {
            note.Text = Footnote();
            addButton.Enabled = Points() != null;
        }
        score.TextChanged += (_, _) => Update();
        possible.TextChanged += (_, _) => Update();
        note.Text = Footnote();
        Dialogs.Show(sheet);
    }

    /// <summary>
    /// A course's grading scale, as its instructor set it (the Mac's GradingScaleSheet; the syllabus's cutoffs): the lowest
    /// percentage for each letter from A+ down to D (F is anything under). Saved, the course's letter, its target and Grade
    /// needed follow it; Standard Scale goes back to the usual cutoffs.
    /// </summary>
    public static void Scale(Engine.Engine engine, string courseId, string course, List<ScaleStep> scale, bool own)
    {
        var letters = scale.Where(s => s.Letter != "F").ToList();
        var boxes = new List<(ScaleStep Step, TextBox Box)>();
        var rows = new Stack { Orientation = Orientation.Vertical, Spacing = 4 };
        foreach (var s in letters)
        {
            var box = new TextBox { Text = Fmt(s.Min), PlaceholderText = Fmt(s.Min), Width = 88, TextAlignment = TextAlignment.Right, ClearButtonEnabled = false };
            System.Windows.Automation.AutomationProperties.SetName(box, $"{s.Letter}, lowest percentage");
            boxes.Add((s, box));
            var row = K.Columns("*, auto, auto",
                K.Text(s.Letter, Ty.BodyStrong).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(0),
                box.Col(1),
                K.Text("% and up", Ty.Body, B.Text2).Margin(10, 0, 0, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(2));
            rows.Children.Add(K.Card(row, new Thickness(16, 6, 12, 6), 4));
        }
        var list = new ScrollViewer { Content = rows, MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var problem = new ContentControl { Focusable = false, IsTabStop = false };
        var body = K.V(12,
            K.Text(course, Ty.Body, B.Text2),
            list,
            K.Wrapped("Enter the lowest percentage for each letter, as your syllabus or instructor sets it.", Ty.Caption, B.Text2),
            problem);
        var sheet = new Sheet { Title = "Grading Scale", Body = body, Width = 420 };
        var saving = false;
        var buttons = new List<SheetButton>();

        void Say(string? error) => problem.Content = error == null ? null
            : new InfoBar { Title = "", Message = error, Severity = InfoBarSeverity.Error, IsOpen = true, IsClosable = false };

        // the cutoffs typed, checked: each from 0 to 100, each lower than the letter above it
        Dictionary<string, double>? Collect()
        {
            var map = new Dictionary<string, double>();
            var last = double.PositiveInfinity;
            foreach (var (s, box) in boxes)
            {
                var raw = box.Text.Trim().Replace("%", "");
                if (GradesKit.Number(raw) is not { } v || v < 0 || v > 100) { Say($"{s.Letter} needs a percentage from 0 to 100."); return null; }
                if (v >= last) { Say($"{s.Letter}’s cutoff must be lower than the letter above it."); return null; }
                last = v;
                map[s.Letter.Replace("−", "-")] = v;
            }
            Say(null);
            return map;
        }

        // saved (null: the standard scale again), then everything that shows a letter reads again
        async void Save(Dictionary<string, double>? map)
        {
            if (saving) return;
            saving = true;
            foreach (var b in buttons) b.Enabled = false;
            try
            {
                JsonNode? cutoffs = null;
                if (map != null)
                {
                    var o = new JsonObject();
                    foreach (var (k, v) in map) o[k] = v;
                    cutoffs = o;
                }
                await engine.Call<OK>("setScale", new JsonObject { ["id"] = courseId, ["scale"] = cutoffs });
                engine.DataChanged();
                sheet.Close();
            }
            catch (Exception e) { Say(e.Message); }
            finally
            {
                saving = false;
                foreach (var b in buttons) b.Enabled = true;
            }
        }

        buttons.Add(sheet.Add("Save", SheetButtonKind.Primary, () => { if (Collect() is { } m) Save(m); }, closes: false));
        if (own) buttons.Add(sheet.Add("Standard Scale", SheetButtonKind.Secondary, () => Save(null), closes: false));
        buttons.Add(sheet.Add("Cancel", SheetButtonKind.Close));
        Dialogs.Show(sheet);
    }

    private static FrameworkElement Field(string label, TextBox box)
    {
        System.Windows.Automation.AutomationProperties.SetName(box, label);
        return K.V(6, K.Text(label, Ty.Body), box);
    }

    private static string Fmt(double v) => v == Math.Round(v) ? ((long)v).ToString(GradesKit.Inv) : v.ToString("G6", GradesKit.Inv);
}
