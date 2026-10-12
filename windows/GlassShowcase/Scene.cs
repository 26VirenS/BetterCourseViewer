using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace GlassShowcase;

/// <summary>
/// What the glass sits over: a Simpl Dashboard as it looks behind a popover — a greeting, counters, the course
/// "skyline" of coloured towers, a row of course cards — over soft colour fields, drifting slowly so the refraction and
/// the frost can be seen moving. (The glass's backdrop is this layer alone.)
/// </summary>
public sealed class Scene : Grid
{
    private readonly Canvas fields = new();
    private readonly TranslateTransform drift = new();
    private readonly StackPanel page = new() { Margin = new Thickness(56, 92, 56, 40) };
    private bool dark = true;

    public static readonly (string Code, Color Color, double Score)[] Courses =
    {
        ("MATH 021", Color.FromRgb(52, 199, 89), 0.92),
        ("PHYS 008", Color.FromRgb(48, 176, 199), 0.81),
        ("PHYS 008L", Color.FromRgb(255, 45, 85), 0.74),
        ("SPRK 010", Color.FromRgb(255, 149, 0), 0.88),
        ("WRI 010", Color.FromRgb(200, 144, 28), 0.95),
        ("CHEM 002", Color.FromRgb(175, 82, 222), 0.69),
        ("BIO 001", Color.FromRgb(0, 122, 255), 0.84),
    };

    public Scene()
    {
        ClipToBounds = true;
        Children.Add(fields);
        var scroller = new Grid { RenderTransform = drift };
        scroller.Children.Add(page);
        Children.Add(scroller);
        Build();
        Loaded += (_, _) => Animate(true);
    }

    public bool Dark
    {
        get => dark;
        set { dark = value; Build(); }
    }

    /// <summary>The page drifts up and down slowly, and the colour fields wander (on by default).</summary>
    public void Animate(bool on)
    {
        if (!on) { drift.BeginAnimation(TranslateTransform.YProperty, null); foreach (var b in fields.Children.OfType<Ellipse>()) b.RenderTransform = null; return; }
        var a = new DoubleAnimation(0, -160, TimeSpan.FromSeconds(14)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
        drift.BeginAnimation(TranslateTransform.YProperty, a);
        var i = 0;
        foreach (var e in fields.Children.OfType<Ellipse>())
        {
            var t = new TranslateTransform();
            e.RenderTransform = t;
            var dur = TimeSpan.FromSeconds(9 + i * 2.3);
            t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-90 + i * 20, 120 - i * 15, dur) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
            t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(60 - i * 10, -80 + i * 25, dur * 1.3) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
            i++;
        }
    }

    private static TextBlock T(string s, double size, Brush b, FontWeight? w = null) => new()
    {
        Text = s, FontSize = size, Foreground = b, FontWeight = w ?? FontWeights.Normal,
        FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
    };

    private void Build()
    {
        Background = new SolidColorBrush(dark ? Color.FromRgb(20, 20, 22) : Color.FromRgb(242, 242, 245));
        var text = new SolidColorBrush(dark ? Color.FromArgb(235, 255, 255, 255) : Color.FromArgb(225, 0, 0, 0));
        var text2 = new SolidColorBrush(dark ? Color.FromArgb(150, 235, 235, 245) : Color.FromArgb(150, 60, 60, 67));
        var card = new SolidColorBrush(dark ? Color.FromRgb(38, 38, 41) : Colors.White);

        // the colour fields: big soft blobs under everything
        fields.Children.Clear();
        (double X, double Y, double R, Color C)[] blobs =
        {
            (120, 520, 420, Color.FromRgb(255, 149, 0)), (760, 120, 380, Color.FromRgb(0, 122, 255)),
            (980, 600, 460, Color.FromRgb(52, 199, 89)), (420, 260, 300, Color.FromRgb(255, 45, 85)),
            (1180, 240, 320, Color.FromRgb(175, 82, 222)),
        };
        foreach (var (x, y, r, c) in blobs)
        {
            var e = new Ellipse
            {
                Width = r * 2, Height = r * 2,
                Fill = new RadialGradientBrush(Color.FromArgb((byte)(dark ? 110 : 90), c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B)),
            };
            Canvas.SetLeft(e, x - r);
            Canvas.SetTop(e, y - r);
            fields.Children.Add(e);
        }

        page.Children.Clear();
        page.Children.Add(T("Good evening, Sam", 40, text, FontWeights.Bold));
        page.Children.Add(T("Monday, October 12", 16, text2));

        // counters
        var counters = new UniformGrid3(6);
        (string Label, string N, string Note, Color C)[] cs =
        {
            ("Due today", "4", "35 points total", Color.FromRgb(255, 59, 48)), ("Tomorrow", "3", "52 points total", Color.FromRgb(255, 149, 0)),
            ("Next 7 days", "14", "Across 5 courses", Color.FromRgb(52, 199, 89)), ("Overdue", "1", "1 not submitted", Color.FromRgb(255, 59, 48)),
            ("Unread", "3", "From 2 courses", Color.FromRgb(255, 149, 0)), ("Graded", "6", "79 / 109 points", Color.FromRgb(88, 86, 214)),
        };
        foreach (var (label, n, note, c) in cs)
        {
            var sp = new StackPanel();
            sp.Children.Add(T(label, 15, new SolidColorBrush(c), FontWeights.SemiBold));
            sp.Children.Add(T(n, 34, text, FontWeights.Bold));
            sp.Children.Add(T(note, 13, text2));
            counters.Add(new Border { Background = card, CornerRadius = new CornerRadius(18), Padding = new Thickness(16, 14, 16, 14), Margin = new Thickness(0, 0, 14, 14), Child = sp });
        }
        counters.Margin = new Thickness(0, 26, 0, 10);
        page.Children.Add(counters);

        // the skyline: a tower per course, as tall as its score, windows lit in its grades' colours
        page.Children.Add(T("Your courses", 22, text, FontWeights.Bold));
        var sky = new StackPanel { Orientation = Orientation.Horizontal, Height = 300, Margin = new Thickness(0, 14, 0, 18) };
        foreach (var (code, c, score) in Courses)
        {
            var tower = new Grid { Width = 120, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Bottom };
            var h = 280 * score;
            tower.Children.Add(new Border
            {
                Height = h, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(14, 14, 6, 6),
                Background = new LinearGradientBrush(c, Color.FromArgb(255, (byte)(c.R * 0.7), (byte)(c.G * 0.7), (byte)(c.B * 0.7)), 90),
            });
            var windows = new WrapPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(14, 0, 14, 12), Height = h - 40 };
            for (var i = 0; i < (int)(h / 26) * 3; i++)
                windows.Children.Add(new Border { Width = 18, Height = 12, Margin = new Thickness(5), CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromArgb((byte)(i % 4 == 0 ? 90 : 200), 255, 248, 220)) });
            tower.Children.Add(windows);
            tower.Children.Add(new TextBlock { Text = code, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Bottom, RenderTransform = new TranslateTransform(0, 22) });
            sky.Children.Add(tower);
        }
        page.Children.Add(sky);

        // the list: rows of work, with their courses' colours
        page.Children.Add(T("Coming up", 22, text, FontWeights.Bold));
        var list = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        string[] work = { "Dis01", "Qz01", "Week 2 Post Class Assignment: GC articles", "Lab report 3: Projectile motion", "Essay draft: The rhetoric of place", "Problem set 6", "Reading response 4" };
        for (var i = 0; i < work.Length; i++)
        {
            var c = Courses[i % Courses.Length];
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var tile = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(Color.FromArgb(60, c.Color.R, c.Color.G, c.Color.B)), Margin = new Thickness(0, 0, 14, 0) };
            DockPanel.SetDock(tile, Dock.Left);
            row.Children.Add(tile);
            var words = new StackPanel();
            words.Children.Add(T($"{c.Code} · Assignment", 13, text2));
            words.Children.Add(T(work[i], 17, text));
            row.Children.Add(words);
            list.Children.Add(new Border { Background = card, CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 8), Child = row });
        }
        page.Children.Add(list);
    }

    /// <summary>A grid that lays its children in rows of n columns, each as wide as the others.</summary>
    private sealed class UniformGrid3 : System.Windows.Controls.Primitives.UniformGrid
    {
        public UniformGrid3(int columns) { Columns = columns; }
        public void Add(UIElement e) => Children.Add(e);
    }
}
