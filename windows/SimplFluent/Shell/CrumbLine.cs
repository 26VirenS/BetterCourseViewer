using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Simpl.Kit;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;

namespace Simpl.Shell;

/// <summary>
/// The way back to where a page was opened from, on one line as Windows 11's BreadcrumbBar keeps it ("F26-MATH 021 20 ›
/// Discussions"), each step going back to it; the page's own title on the line too (<c>last</c>), or — a page opened on
/// a place, whose title has the header's whole width on a line of its own under this one — not (<c>last</c> null: no
/// "›" after the last step). When the steps do not all fit, the longest are cut short first (a course's name to its
/// first words and "…", never to nothing), down to <see cref="StepLeast"/> each; only then do the leading steps fold into
/// "…" (a menu of them) one by one, from the first. A course's square, when there is one, comes first and always shows.
/// </summary>
internal sealed class CrumbLine : Panel
{
    private readonly FrameworkElement? lead;
    private readonly List<(FrameworkElement Step, FrameworkElement Chevron, string Title, Action Go)> steps = new();
    private readonly FrameworkElement? last;
    private readonly Button more;
    private readonly FrameworkElement moreChevron;
    private int folded;
    /// <summary>The widest a step is drawn now (cut short with "…" past it).</summary>
    private double cap = double.PositiveInfinity;

    /// <summary>The narrowest a step is cut to before the steps fold into "…".</summary>
    public const double StepLeast = 96;

    public CrumbLine(FrameworkElement? lead, IEnumerable<(FrameworkElement Step, string Title, Action Go)> steps, FrameworkElement? last, bool small = false)
    {
        this.lead = lead;
        this.last = last;
        if (lead != null) Children.Add(lead);
        more = K.Subtle(K.Text("…", small ? Ty.Body : Ty.Title, B.Text2), Menu, small ? new Thickness(6, 2, 6, 2) : new Thickness(6, 0, 6, 0));
        more.ToolTip = "More of the path";
        more.VerticalAlignment = VerticalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetName(more, "More of the path");
        moreChevron = Chevron(small);
        Children.Add(more);
        Children.Add(moreChevron);
        foreach (var (step, title, go) in steps)
        {
            var chevron = Chevron(small);
            this.steps.Add((step, chevron, title, go));
            Children.Add(step);
            Children.Add(chevron);
        }
        if (last != null) Children.Add(last);
    }

    /// <summary>The "›" after a step: the title's size, or (<paramref name="small"/>) the breadcrumb line's over a title.</summary>
    public static FrameworkElement Chevron(bool small = false) => small
        ? K.Icon(SymbolRegular.ChevronRight20, 12, B.Text2).Margin(2, 0, 2, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Center)
        : K.Icon(SymbolRegular.ChevronRight20, 16, B.Text2).Margin(10, 6, 10, 0);

    /// <summary>The steps folded into "…", to go back to.</summary>
    private void Menu()
    {
        if (folded == 0) return;
        var menu = new ContextMenu { PlacementTarget = more, Placement = PlacementMode.Bottom, MinWidth = 200 };
        foreach (var (_, _, title, go) in steps.Take(folded))
        {
            var item = new MenuItem { Header = title };
            item.Click += (_, _) => go();
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    /// <summary>The "›" after step <paramref name="i"/>: none after the last when no title follows on the line.</summary>
    private bool ChevronShown(int i) => last != null || i < steps.Count - 1;

    protected override Size MeasureOverride(Size available)
    {
        var free = new Size(double.PositiveInfinity, double.PositiveInfinity);
        foreach (UIElement c in InternalChildren) if (!ReferenceEquals(c, last)) c.Measure(free);
        last?.Measure(free);
        static double W(UIElement e) => e.DesiredSize.Width;
        var leadWidth = lead == null ? 0 : W(lead);
        var natural = steps.Select(s => W(s.Step)).ToList();
        var chevrons = steps.Select((s, i) => ChevronShown(i) ? W(s.Chevron) : 0).ToList();
        var moreWidth = W(more) + W(moreChevron);
        var lastWidth = last == null ? 0 : W(last);
        double Before(int f, double c) => leadWidth + (f > 0 ? moreWidth : 0) + natural.Select((w, i) => i < f ? 0 : Math.Min(w, c) + chevrons[i]).Sum();
        folded = 0;
        cap = double.PositiveInfinity;
        if (!double.IsInfinity(available.Width))
        {
            var room = available.Width + 0.5 - lastWidth;
            if (Before(0, cap) > room && natural.Count > 0)
            {
                // (the longest steps cut short first, all to the same width, down to StepLeast; then the first ones fold)
                var widest = natural.Max();
                cap = widest;
                while (cap > StepLeast && Before(0, cap) > room) cap = Math.Max(StepLeast, cap - 4);
                while (folded < steps.Count && Before(folded, cap) > room) folded++;
            }
        }
        // (each step measured again at the width it is given: its words cut with "…" where it is narrower than they are)
        for (var i = 0; i < steps.Count; i++)
            if (natural[i] > cap) steps[i].Step.Measure(new Size(cap, double.PositiveInfinity));
        var used = leadWidth + (folded > 0 ? moreWidth : 0) + steps.Select((s, i) => i < folded ? 0 : W(s.Step) + chevrons[i]).Sum();
        double height = 0;
        if (last != null)
        {
            var titleRoom = double.IsInfinity(available.Width) ? double.PositiveInfinity : Math.Max(0, available.Width - used);
            last.Measure(new Size(titleRoom, available.Height));
            height = last.DesiredSize.Height;
            used += last.DesiredSize.Width;
        }
        foreach (var e in Shown()) height = Math.Max(height, e.DesiredSize.Height);
        return new Size(used, height);
    }

    /// <summary>What shows before the title: the square, "…" while steps are folded, and the steps left.</summary>
    private IEnumerable<FrameworkElement> Shown()
    {
        if (lead != null) yield return lead;
        if (folded > 0) { yield return more; yield return moreChevron; }
        for (var i = folded; i < steps.Count; i++)
        {
            yield return steps[i].Step;
            if (ChevronShown(i)) yield return steps[i].Chevron;
        }
    }

    protected override Size ArrangeOverride(Size final)
    {
        var shown = Shown().ToHashSet();
        var x = 0.0;
        foreach (UIElement c in InternalChildren)
        {
            if (ReferenceEquals(c, last)) continue;
            if (c is FrameworkElement f && shown.Contains(f))
            {
                // (the breadcrumb line's parts centred on it; the title's, at its top, by its first line)
                var h = c.DesiredSize.Height;
                var y = last == null ? Math.Max(0, (final.Height - h) / 2) : 0;
                c.Arrange(new Rect(x, y, c.DesiredSize.Width, h));
                x += c.DesiredSize.Width;
            }
            // (a folded step: nowhere, and out of reach of the keyboard and the pointer)
            else c.Arrange(new Rect(0, 0, 0, 0));
            c.IsHitTestVisible = c is FrameworkElement g && shown.Contains(g);
            if (c is Control k) k.IsTabStop = k.IsHitTestVisible;
        }
        last?.Arrange(new Rect(x, 0, Math.Max(0, final.Width - x), Math.Max(last.DesiredSize.Height, final.Height)));
        // (a picture run says how the breadcrumb over a title fitted, for the screenshot suite's checks)
        if (last == null && Support.Shot.Asked)
        {
            var now = $"header: the breadcrumb shows {steps.Count - folded} of {steps.Count} steps{(double.IsInfinity(cap) ? "" : $", cut to {cap:0}")}, the title under it";
            if (now != said) { said = now; Support.Log.Write(now); }
        }
        return final;
    }

    /// <summary>What a picture run last said of this breadcrumb.</summary>
    private string? said;
}
