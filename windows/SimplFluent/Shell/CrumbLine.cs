using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Simpl.Kit;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;

namespace Simpl.Shell;

/// <summary>
/// The page's title as a breadcrumb on one line, as Windows 11's BreadcrumbBar keeps it: the steps that lead back
/// ("Tools ›", "Periodic table ›") and the page's own title last. When they do not all fit beside the page's commands,
/// the leading steps fold into "…" (a menu of them) one by one, from the first, so the line never breaks after a "›"
/// and leaves the title alone on a line of its own; a title still too long for what is left is cut (or, a title that
/// may wrap, wrapped in its own room). A course's square, when there is one, comes first and always shows.
/// </summary>
internal sealed class CrumbLine : Panel
{
    private readonly FrameworkElement? lead;
    private readonly List<(FrameworkElement Step, FrameworkElement Chevron, string Title, Action Go)> steps = new();
    private readonly FrameworkElement last;
    private readonly Button more;
    private readonly FrameworkElement moreChevron;
    private int folded;

    public CrumbLine(FrameworkElement? lead, IEnumerable<(FrameworkElement Step, string Title, Action Go)> steps, FrameworkElement last)
    {
        this.lead = lead;
        this.last = last;
        if (lead != null) Children.Add(lead);
        more = K.Subtle(K.Text("…", Ty.Title, B.Text2), Menu, new Thickness(6, 0, 6, 0));
        more.ToolTip = "More of the path";
        System.Windows.Automation.AutomationProperties.SetName(more, "More of the path");
        moreChevron = Chevron();
        Children.Add(more);
        Children.Add(moreChevron);
        foreach (var (step, title, go) in steps)
        {
            var chevron = Chevron();
            this.steps.Add((step, chevron, title, go));
            Children.Add(step);
            Children.Add(chevron);
        }
        Children.Add(last);
    }

    /// <summary>The "›" after a step.</summary>
    public static FrameworkElement Chevron() => K.Icon(SymbolRegular.ChevronRight20, 16, B.Text2).Margin(10, 6, 10, 0);

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

    protected override Size MeasureOverride(Size available)
    {
        var free = new Size(double.PositiveInfinity, double.PositiveInfinity);
        foreach (UIElement c in InternalChildren) if (!ReferenceEquals(c, last)) c.Measure(free);
        last.Measure(free);
        static double W(UIElement e) => e.DesiredSize.Width;
        var leadWidth = lead == null ? 0 : W(lead);
        var stepWidths = steps.Select(s => W(s.Step) + W(s.Chevron)).ToList();
        var moreWidth = W(more) + W(moreChevron);
        double Before(int f) => leadWidth + (f > 0 ? moreWidth : 0) + stepWidths.Skip(f).Sum();
        folded = 0;
        if (!double.IsInfinity(available.Width))
            while (folded < steps.Count && Before(folded) + W(last) > available.Width + 0.5) folded++;
        var used = Before(folded);
        var room = double.IsInfinity(available.Width) ? double.PositiveInfinity : Math.Max(0, available.Width - used);
        last.Measure(new Size(room, available.Height));
        var height = last.DesiredSize.Height;
        foreach (var e in Shown()) height = Math.Max(height, e.DesiredSize.Height);
        return new Size(used + last.DesiredSize.Width, height);
    }

    /// <summary>What shows before the title: the square, "…" while steps are folded, and the steps left.</summary>
    private IEnumerable<FrameworkElement> Shown()
    {
        if (lead != null) yield return lead;
        if (folded > 0) { yield return more; yield return moreChevron; }
        foreach (var (step, chevron, _, _) in steps.Skip(folded)) { yield return step; yield return chevron; }
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
                c.Arrange(new Rect(x, 0, c.DesiredSize.Width, c.DesiredSize.Height));
                x += c.DesiredSize.Width;
            }
            // (a folded step: nowhere, and out of reach of the keyboard and the pointer)
            else c.Arrange(new Rect(0, 0, 0, 0));
            c.IsHitTestVisible = c is FrameworkElement g && shown.Contains(g);
            if (c is Control k) k.IsTabStop = k.IsHitTestVisible;
        }
        last.Arrange(new Rect(x, 0, Math.Max(0, final.Width - x), Math.Max(last.DesiredSize.Height, final.Height)));
        return final;
    }
}
