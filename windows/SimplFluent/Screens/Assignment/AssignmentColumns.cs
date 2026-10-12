using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Models;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

public sealed partial class AssignmentScreen
{
    private RichText? instructions;
    private string? instructionsHtml;

    // ---- the left: what to read, what was handed in, what was said ----

    private FrameworkElement MainColumn(AssignmentData d)
    {
        var v = new Stack { Orientation = Orientation.Vertical, Spacing = 32 };
        if (d.Html.Length > 0)
        {
            // (the same words kept from build to build: their pictures are not fetched again)
            if (instructions == null || instructionsHtml != d.Html)
            {
                instructions = new RichText(d.Html, url => engine.GoTo(url, ""), engine.BaseUrl, 15);
                instructionsHtml = d.Html;
            }
            else AssignKit.Detach(instructions);
            v.Children.Add(K.V(0, Heading.Section("Instructions"), K.Card(instructions, new Thickness(22, 18, 22, 18))));
        }
        workPart = WorkSection(d);
        if (workPart != null) v.Children.Add(workPart);
        v.Children.Add(CommentsSection(d));
        return v;
    }

    /// <summary>What you handed in: when, the text, the web address, the files (a click opens one with its feedback).</summary>
    private FrameworkElement? WorkSection(AssignmentData d)
    {
        seal = null;
        var files = d.Submission?.Files ?? new List<Attachment>();
        var link = d.Submission?.Url ?? "";
        var text = d.Submission?.Text ?? "";
        var submitted = d.Submitted ?? "";
        if (submitted.Length == 0 && files.Count == 0 && link.Length == 0 && text.Length == 0) return null;
        var rows = new Stack { Orientation = Orientation.Vertical };
        if (submitted.Length > 0)
        {
            var icon = K.Icon(SymbolRegular.CheckmarkStarburst24, 18, B.Success, filled: true);
            seal = icon;
            var word = K.Wrapped(submitted, Ty.BodyStrong);
            word.Foreground = new SolidColorBrush(K.Darkened(K.Tone("green")));
            rows.Children.Add(K.Columns("auto, *", icon.Margin(0, 1, 8, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Col(0), word.Col(1)).Margin(10, 8, 10, 8));
        }
        if (text.Length > 0)
        {
            if (submitted.Length > 0) rows.Children.Add(K.Divider(10));
            rows.Children.Add(AssignKit.Selectable(text, Ty.Body, B.Text, 14).Margin(10, 10, 10, 10));
        }
        if (link.Length > 0)
        {
            if (submitted.Length > 0 || text.Length > 0) rows.Children.Add(K.Divider(54));
            var words = K.V(2, K.Wrapped(link, Ty.Body, B.Text, 2), K.Text("Website", Ty.Caption, B.Text2));
            words.VerticalAlignment = VerticalAlignment.Center;
            var row = K.Subtle(K.Columns("auto, *", K.IconTile(SymbolRegular.Link24, K.Tone("blue"), 32).Margin(0, 0, 12, 0).Col(0), words.Col(1)), () => Visit(link), new Thickness(8, 9, 8, 9));
            row.ToolTip = $"Open {link}";
            var menu = new ContextMenu();
            menu.Items.Add(AssignKit.Item("Open", SymbolRegular.Open24, () => Visit(link)));
            menu.Items.Add(AssignKit.Item("Copy Link", SymbolRegular.Link24, () => AssignKit.Copy(link)));
            row.ContextMenu = menu;
            rows.Children.Add(row);
        }
        for (var i = 0; i < files.Count; i++)
        {
            if (i > 0 || submitted.Length > 0 || text.Length > 0 || link.Length > 0) rows.Children.Add(K.Divider(54));
            rows.Children.Add(AttachmentRow(d, files[i]));
        }
        return K.V(0, Heading.Section("Your Work"), K.Card(rows, new Thickness(8)));
    }

    /// <summary>A file handed in, as a row of Your Work: a click — or Preview — opens it with its feedback (the school's
    /// viewer, the teacher's marks on it, the comments beside); its menu has the rest.</summary>
    private FrameworkElement AttachmentRow(AssignmentData d, Attachment f)
    {
        var name = K.Wrapped(f.Name, Ty.Body, B.Text, 2);
        name.VerticalAlignment = VerticalAlignment.Center;
        var open = K.Subtle(K.Columns("auto, *", AssignKit.FileIcon(f.Name, 32).Margin(0, 0, 12, 0).Col(0), name.Col(1)), () => Review(d, f), new Thickness(8, 9, 8, 9));
        open.ToolTip = $"Open {f.Name} with its feedback";
        var preview = K.Button("Preview", () => Review(d, f), icon: SymbolRegular.Eye24);
        preview.MinWidth = 0;
        preview.ToolTip = $"{f.Name} with your teacher's marks and comments";
        var g = K.Columns("*, auto", open.Col(0), preview.Margin(8, 0, 4, 0).Col(1));
        g.ContextMenu = AssignKit.FileMenu(engine, f.Url, f.Name);
        return g;
    }

    /// <summary>The comments on your work, each with its files, and a box to write one.</summary>
    private FrameworkElement CommentsSection(AssignmentData d)
    {
        UIElement? add = null;
        if (!commenting)
        {
            var b = K.Button("Add a Comment", StartCommenting, icon: SymbolRegular.CommentAdd24);
            b.ToolTip = "Write a comment for your teacher";
            add = b;
        }
        var rows = new Stack { Orientation = Orientation.Vertical };
        if (d.Comments.Count == 0 && !commenting) rows.Children.Add(K.Empty(SymbolRegular.Comment24, "No comments yet."));
        for (var i = 0; i < d.Comments.Count; i++)
        {
            if (i > 0) rows.Children.Add(K.Divider(56));
            rows.Children.Add(CommentRow(d.Comments[i]));
        }
        if (commenting && composer != null)
        {
            AssignKit.Detach(composer);
            composer.Margin = new Thickness(8, d.Comments.Count == 0 ? 4 : 12, 8, 4);
            rows.Children.Add(composer);
        }
        var card = K.Card(rows, new Thickness(8));
        commentsLit = Lit(d, 12);
        commentsLit.Margin = new Thickness(-10);
        var section = K.V(0, Heading.Section("Comments", d.Comments.Count == 0 ? null : d.Comments.Count.ToString(CultureInfo.InvariantCulture), add), card);
        commentsPart = new Grid { Children = { section, commentsLit } };
        return commentsPart;
    }

    private FrameworkElement CommentRow(CommentRow c)
    {
        var who = K.Text(c.Author, Ty.BodyStrong);
        var top = K.Columns("auto, auto, *, auto", who.Col(0));
        if (c.Attempt is > 0) top.Children.Add(K.Text($"Attempt {c.Attempt}", Ty.Caption, B.Text3).Margin(8, 0, 0, 1).Align(HorizontalAlignment.Left, VerticalAlignment.Bottom).Col(1));
        if (!string.IsNullOrEmpty(c.When)) top.Children.Add(K.Text(c.When!, Ty.Caption, B.Text2).Margin(6, 0, 0, 1).Align(HorizontalAlignment.Right, VerticalAlignment.Bottom).Col(3));
        var words = K.V(5, top);
        if (c.Text.Length > 0) words.Children.Add(AssignKit.Selectable(c.Text, Ty.Body));
        foreach (var f in c.Attachments ?? new List<Attachment>())
        {
            var file = f;
            var link = K.Subtle(K.H(6, AssignKit.FileIcon(f.Name, 18), K.Text(f.Name, Ty.Body, B.AccentText)), () => AssignKit.OpenFile(engine, file.Url, file.Name), new Thickness(4, 2, 6, 2));
            link.HorizontalAlignment = HorizontalAlignment.Left;
            link.Margin = new Thickness(-4, 0, 0, 0);
            link.ToolTip = $"Open {f.Name}";
            link.Cursor = Cursors.Hand;
            link.ContextMenu = AssignKit.FileMenu(engine, f.Url, f.Name);
            words.Children.Add(link);
        }
        var avatar = K.Avatar(c.Author, null, c.Avatar, 34);
        avatar.VerticalAlignment = VerticalAlignment.Top;
        var g = K.Columns("auto, *", avatar.Margin(0, 0, 12, 0).Col(0), words.Col(1));
        g.Margin = new Thickness(10);
        g.Background = Brushes.Transparent;
        if (c.Text.Length > 0)
        {
            var m = new ContextMenu();
            m.Items.Add(AssignKit.Item("Copy Comment", SymbolRegular.Copy24, () => AssignKit.Copy(c.Text)));
            g.ContextMenu = m;
        }
        return g;
    }

    /// <summary>A part's edge in the course's colour, shown for a moment when the page has just gone to it.</summary>
    private static Border Lit(AssignmentData d, double corner) => new()
    {
        BorderBrush = new SolidColorBrush(Tint(d)), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(corner),
        Opacity = 0, IsHitTestVisible = false,
    };

    // ---- the right: the next step, the grade, the facts ----

    private FrameworkElement SideColumn(AssignmentData d)
    {
        var v = new Stack { Orientation = Orientation.Vertical, Spacing = 16 };
        gradePart = null;
        gradeLit = null;
        if (ActionPanel(d) is { } action) v.Children.Add(action);
        if (d.Grade is { } g) v.Children.Add(GradeCard(g, d));
        else if (d.Held == true)
            v.Children.Add(K.Card(Note(SymbolRegular.EyeOff24, "Graded, but your teacher has not released the grade yet.", Ty.Body)));
        v.Children.Add(FactsCard(d));
        return v;
    }

    private static FrameworkElement Note(SymbolRegular icon, string text, Ty ty = Ty.Body) =>
        K.Columns("auto, *", K.Icon(icon, 16, B.Text2).Margin(0, 2, 8, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Col(0), K.Wrapped(text, ty, B.Text2).Col(1));

    /// <summary>The one thing to do next, as wide as the column (two side by side, the new one lit, where there are two),
    /// with why it cannot be handed in here when it cannot; with nothing to do here, that reason — and the work's
    /// feedback, where something was handed in.</summary>
    private FrameworkElement? ActionPanel(AssignmentData d)
    {
        var why = d.Why ?? "";
        var steps = NextSteps(d);
        if (steps.Count > 0)
        {
            var row = new Grid();
            for (var i = 0; i < steps.Count; i++)
            {
                if (i > 0) row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var step = steps[i];
                var b = new Wpf.Ui.Controls.Button
                {
                    Content = StepLabel(step), Appearance = i == steps.Count - 1 ? ControlAppearance.Primary : ControlAppearance.Secondary,
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(12, 10, 12, 10), MinHeight = 44, ToolTip = step.Help,
                };
                System.Windows.Automation.AutomationProperties.SetName(b, step.Title);
                b.Click += (_, _) => Act(d, step);
                row.Children.Add(b.Col(i * 2));
            }
            var v = K.V(10, row);
            if (TakesDrops(d)) v.Children.Add(K.Text(d.Types.Contains("online_upload") ? "Or drop files on this page." : "Or drop a link on this page.", Ty.Body, B.Text2).Center());
            if (why.Length > 0) v.Children.Add(Note(SymbolRegular.Info24, why));
            return v;
        }
        if (why.Length == 0) return null;
        var inner = K.V(12, Note(SymbolRegular.Info24, why));
        if (HasWork(d))
        {
            var see = K.Button("See Feedback", () => Review(d, d.Submission?.Files?.FirstOrDefault()), icon: SymbolRegular.Comment24);
            see.HorizontalAlignment = HorizontalAlignment.Stretch;
            see.ToolTip = "Your work, with your teacher's marks and comments";
            inner.Children.Add(see);
        }
        return K.Card(inner);
    }

    /// <summary>A step's symbol and words; a title too long for its half of the column is said shorter, then without its
    /// symbol, rather than cut off.</summary>
    private static FrameworkElement StepLabel(AssignStep step)
    {
        FrameworkElement Row(string words, bool symbol)
        {
            var t = new TextBlock { Text = words, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            return symbol ? K.H(8, new SymbolIcon { Symbol = step.Symbol, FontSize = 18, VerticalAlignment = VerticalAlignment.Center }, t) : t;
        }
        var fit = new AssignFit();
        fit.Children.Add(Row(step.Title, true));
        if (step.Short != null) fit.Children.Add(Row(step.Short, true));
        fit.Children.Add(Row(step.Short ?? step.Title, false));
        return fit;
    }

    /// <summary>What the ring holds: the letter (or the percentage); a complete / incomplete grade as a tick or a cross,
    /// and any other word on one line, made small enough to fit.</summary>
    private static UIElement RingMark(GradeInfo g, Color color)
    {
        var word = (g.Letter ?? "").Trim();
        switch (word.ToLowerInvariant())
        {
            case "complete" or "pass":
            {
                var i = K.Icon(SymbolRegular.Checkmark24, 30, color);
                System.Windows.Automation.AutomationProperties.SetName(i, CultureInfo.CurrentCulture.TextInfo.ToTitleCase(word));
                return i;
            }
            case "incomplete" or "fail":
            {
                var i = K.Icon(SymbolRegular.Dismiss24, 26, B.Text2);
                System.Windows.Automation.AutomationProperties.SetName(i, CultureInfo.CurrentCulture.TextInfo.ToTitleCase(word));
                return i;
            }
            default:
            {
                var t = new TextBlock
                {
                    Text = word.Length == 0 ? g.Pct is { } p ? $"{Math.Round(p, MidpointRounding.AwayFromZero)}%" : "" : word,
                    FontSize = 22, FontWeight = FontWeights.Bold, FontFamily = K.DisplayFace, Foreground = new SolidColorBrush(K.Darkened(color)),
                };
                return new Viewbox { Child = t, StretchDirection = StretchDirection.DownOnly, MaxWidth = 54, MaxHeight = 40 };
            }
        }
    }

    /// <summary>The grade: its ring and letter, the points, the percentage, what lateness took, the class's numbers (and
    /// their box plot), and the way to the rubric.</summary>
    private FrameworkElement GradeCard(GradeInfo g, AssignmentData d)
    {
        var color = Tint(d);
        var ring = K.Ring(g.Pct is { } pct ? pct / 100 : null, color, 76, 7, RingMark(g, color));
        TextBlock score;
        if (g.Score is { } s && g.Possible is { } p && p > 0) score = K.Text($"{AssignKit.Num(s)} / {AssignKit.Num(p)}", Ty.Subtitle);
        else score = K.Wrapped(g.Text, Ty.Subtitle);
        var facts = K.V(3, score);
        if (g.Pct is { } pc) facts.Children.Add(K.Text(pc.ToString("0.0", CultureInfo.InvariantCulture) + "%", Ty.Body, B.Text2));
        if (!string.IsNullOrEmpty(g.Late))
        {
            var late = K.Text(g.Late!, Ty.BodyStrong);
            late.Foreground = new SolidColorBrush(K.Darkened(K.Tone("orange")));
            facts.Children.Add(late);
        }
        facts.VerticalAlignment = VerticalAlignment.Center;
        var top = K.Columns("auto, *", ring.Margin(0, 0, 16, 0).Col(0), facts.Col(1));
        var v = K.V(14, K.Text("Grade", Ty.BodyStrong, B.Text2), top);
        if (!string.IsNullOrEmpty(d.Stats)) v.Children.Add(Note(SymbolRegular.PeopleTeam24, d.Stats!));
        if (d.ClassStats is { } cs)
        {
            var plot = new AssignClassPlot(cs, color);
            v.Children.Add(K.Columns("auto, *", K.Text("Class", Ty.Caption, B.Text2).Margin(0, 0, 10, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(0), plot.Col(1)).Margin(0, 6, 0, 0));
        }
        if (d.Rubric.Count > 0) v.Children.Add(RubricJump(d));
        var card = K.Card(v, new Thickness(18));
        gradeLit = Lit(d, 8);
        gradePart = new Grid { Children = { card, gradeLit } };
        return gradePart;
    }

    /// <summary>The grade's way to the rubric that explains it: the ring in miniature, its name and score; the ring comes
    /// up over the window.</summary>
    private FrameworkElement RubricJump(AssignmentData d)
    {
        var m = new RubricModel(d.Rubric);
        var g = K.Columns("auto, *, auto, auto",
            new RubricMiniRing(m, 26).Margin(0, 0, 10, 0).Col(0),
            K.Text(d.RubricTitle ?? "Rubric", Ty.BodyStrong).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(1));
        if (!string.IsNullOrEmpty(d.RubricScore)) g.Children.Add(K.Text(d.RubricScore!, Ty.Body, B.Text2).Margin(6, 0, 0, 0).Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(2));
        g.Children.Add(K.Icon(SymbolRegular.ArrowMaximize24, 14, B.Text2).Margin(10, 0, 0, 0).Col(3));
        var b = new Wpf.Ui.Controls.Button
        {
            Content = g, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(11, 8, 11, 8), ToolTip = "See how this was marked, criterion by criterion",
        };
        b.Click += (_, _) => OpenRubric(d);
        return b;
    }

    /// <summary>The facts, one to a line: when it is due, what it is worth, when it opens and closes, how it is handed in,
    /// the attempts used.</summary>
    private static FrameworkElement FactsCard(AssignmentData d)
    {
        var color = Tint(d);
        var lines = new List<(SymbolRegular Symbol, string Label, string Value)>
        {
            (SymbolRegular.CalendarLtr24, "Due", string.IsNullOrEmpty(d.Due) ? "No due date" : d.Due!),
        };
        if (!string.IsNullOrEmpty(d.Points)) lines.Add((SymbolRegular.Star24, "Points", d.Points!));
        if (!string.IsNullOrEmpty(d.Available)) lines.Add((SymbolRegular.Clock24, "Available", d.Available!));
        if (!string.IsNullOrEmpty(d.TypesText)) lines.Add((SymbolRegular.ArrowUpload24, "Handed in as", d.TypesText!));
        if (!string.IsNullOrEmpty(d.AttemptsText)) lines.Add((SymbolRegular.ArrowCounterclockwise24, "Attempts", d.AttemptsText!));
        var v = new Stack { Orientation = Orientation.Vertical };
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0) v.Children.Add(K.Divider(56));
            var (symbol, label, value) = lines[i];
            var tile = K.IconTile(symbol, color, 30);
            tile.VerticalAlignment = VerticalAlignment.Top;
            var words = K.V(2, K.Text(label, Ty.Caption, B.Text2), AssignKit.Selectable(value, Ty.Body));
            var row = K.Columns("auto, *", tile.Margin(0, 0, 12, 0).Col(0), words.Col(1));
            row.Margin = new Thickness(14, 9, 14, 9);
            System.Windows.Automation.AutomationProperties.SetName(row, $"{label}: {value}");
            v.Children.Add(row);
        }
        return K.Card(v, new Thickness(0, 6, 0, 6));
    }
}
