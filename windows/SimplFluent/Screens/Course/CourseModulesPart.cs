using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Support;
using Wpf.Ui.Controls;

namespace Simpl.Screens;

/// <summary>
/// A course's modules (the Mac's ModulesList), each a card that opens under its name: its items indented as the course
/// sets them, under their sub-headings — what each asks of you (View, Submit, Mark done), a tick once it is done, why one
/// is locked, and Mark done where it is yours to mark. Only the module last opened here opens by itself (kept on this
/// PC, let go when it is closed again) — or a course's only module, at once (one closed card was a click for nothing);
/// others opened since stay open while the list is up. A locked module says why, or when it opens ("Opens Oct 20"),
/// under its name, without being opened.
/// </summary>
internal sealed class CourseModulesPart : CourseListPart<ModulesData>
{
    private readonly HashSet<string> opened = new();
    /// <summary>Marked done (or not) here before the school has answered: the tick shows at once, and goes back if the school says no.</summary>
    private readonly Dictionary<string, bool> marking = new();
    private bool restored;
    /// <summary>The module just opened or closed (its chevron turns, its items arrive), and the item just ticked.</summary>
    private string? toggled, ticked;

    public CourseModulesPart(Engine.Engine engine, string ctx) : base(engine, ctx, "modules", "modules", "Modules") { }

    private string LastKey => "modLast:" + ctx;

    protected override string? Context(ModulesData d) => d.Context;

    protected override FrameworkElement Content(ModulesData d)
    {
        var color = K.Hex(d.Color);
        if (d.Modules.Count == 0) return CourseKit.EmptyCard(d.Empty ?? "No modules", SymbolRegular.Stack24);
        Restore(d);
        var v = K.V(14, d.Modules.Select(m => Module(m, color)).ToArray());
        toggled = null;
        ticked = null;
        return v;
    }

    private void Restore(ModulesData d)
    {
        if (restored) return;
        restored = true;
        if (Prefs.String(LastKey) is { Length: > 0 } last && d.Modules.Any(m => m.Id == last)) opened.Add(last);
        else if (d.Modules.Count == 1) opened.Add(d.Modules[0].Id); // (a course's only module, open at once)
    }

    // ---- a module ----

    private FrameworkElement Module(ModuleData m, Color color)
    {
        var open = opened.Contains(m.Id);
        var card = K.V(0, Header(m, open));
        if (open)
        {
            var items = Items(m, color);
            card.Children.Add(items);
            // (its items arriving under its name: a short drop and a fade)
            if (toggled == m.Id) Motion.Enter(items, 0, -6, Motion.Slow);
        }
        return K.Card(card, new Thickness(6));
    }

    /// <summary>Its name with the chevron that opens it, a lock or a tick, why it is locked or when it opens (under its
    /// name), and how far through it you are.</summary>
    private FrameworkElement Header(ModuleData m, bool open)
    {
        var chevron = K.Icon(SymbolRegular.ChevronRight20, 12, B.Text2);
        chevron.Width = 16;
        var turn = new RotateTransform(open ? 90 : 0);
        chevron.RenderTransformOrigin = new Point(0.5, 0.5);
        chevron.RenderTransform = turn;
        if (toggled == m.Id) Motion.FromTo(turn, RotateTransform.AngleProperty, open ? 0 : 90, open ? 90 : 0, Motion.Medium, Motion.Decelerate);
        var marks = K.H(8);
        if (m.Locked == true)
        {
            var l = K.Icon(SymbolRegular.LockClosed24, 16, B.Text2, filled: true);
            AutomationProperties.SetName(l, "Locked");
            marks.Children.Add(l);
        }
        if (m.Done == true)
        {
            var t = K.Icon(SymbolRegular.CheckmarkCircle24, 16, CourseKit.Green, filled: true);
            AutomationProperties.SetName(t, "Done");
            marks.Children.Add(t);
        }
        var name = K.Wrapped(m.Name, Ty.BodyStrong, B.Text, 2);
        FrameworkElement words = name;
        if (LockLine(m) is { } why)
        {
            // (why it is locked, or when it opens, without opening it)
            var line = K.Wrapped(why, Ty.Caption, B.Text2, 2);
            words = K.V(2, name, line);
        }
        words.VerticalAlignment = VerticalAlignment.Center;
        var g = K.Columns("auto, auto, *, auto",
            chevron.Margin(0, 0, 10, 0).Col(0),
            marks.Margin(0, 0, marks.Children.Count > 0 ? 10 : 0, 0).Col(1),
            words.Col(2));
        if (!string.IsNullOrEmpty(m.Progress)) g.Children.Add(K.Text(m.Progress!, Ty.Caption, B.Text2).Margin(8, 0, 0, 0).Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(3));
        var b = K.Subtle(g, () => Toggle(m), new Thickness(10, 12, 10, 12), 6);
        AutomationProperties.SetName(b, LockLine(m) is { } locked ? $"{m.Name}, {locked}" : m.Name);
        AutomationProperties.SetHelpText(b, open ? "Hides the module’s items" : "Shows the module’s items");
        return b;
    }

    /// <summary>Why a locked module is locked, or when it opens ("Opens Oct 20"); null for one that is not.</summary>
    private static string? LockLine(ModuleData m) => m.Locked == true && m.LockText is { Length: > 0 } t ? t : null;

    private FrameworkElement Items(ModuleData m, Color color)
    {
        var v = K.V(0, K.Divider().Margin(10, 0, 10, 4)); // (why it is locked is said under its name)
        for (var i = 0; i < m.Items.Count; i++)
        {
            var it = m.Items[i];
            if (i > 0 && it.Header != true && m.Items[i - 1].Header != true) v.Children.Add(CourseKit.Divider(48 + (it.Indent ?? 0) * 20));
            v.Children.Add(Item(it, m, color));
        }
        if (m.Items.Count == 0) v.Children.Add(K.Text("Nothing in this module yet.", Ty.Caption, B.Text2).Margin(10, 8, 10, 8));
        return v;
    }

    // ---- an item ----

    private FrameworkElement Item(ModuleItem it, ModuleData m, Color color)
    {
        var pad = (it.Indent ?? 0) * 20.0;
        if (it.Header == true)
        {
            var h = K.Wrapped(it.Title, Ty.Caption, B.Text2).Strong();
            h.Margin = new Thickness(8 + pad, 10, 8, 4);
            AutomationProperties.SetHeadingLevel(h, AutomationHeadingLevel.Level3);
            return h;
        }
        var locked = it.Locked == true;
        var done = marking.TryGetValue(it.Id, out var mk) ? mk : it.Done == true;
        var label = ItemLabel(it, locked, done, color);
        // (a locked item opens too: its page says why it is locked and when it opens)
        var link = it.Url != null || it.Type == "ExternalTool" ? (FrameworkElement)CourseKit.RowLink(label, () => OpenItem(it)) : CourseKit.StillRow(label);
        var g = K.Columns("*, auto", link.Col(0));
        if (it.Markable == true && !done)
        {
            var b = K.Button("Mark done", () => Mark(it, m, true));
            b.MinWidth = 0;
            b.IsEnabled = !locked;
            b.ToolTip = $"Mark this item done in {engine.LmsName}";
            b.Margin = new Thickness(8, 0, 6, 0);
            b.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(b.Col(1));
        }
        g.Margin = new Thickness(pad, 0, 0, 0);
        g.Background = Brushes.Transparent;
        CourseKit.Menu(g, items => ItemMenu(items, it, m, locked, done));
        return g;
    }

    private FrameworkElement ItemLabel(ModuleItem it, bool locked, bool done, Color color)
    {
        var tile = K.IconTile(locked ? SymbolRegular.LockClosed24 : CourseKit.Item(it.Type), locked ? CourseKit.Gray : color, 28);
        var words = K.V(2, K.Wrapped(it.Title, Ty.Body, locked ? B.Text2 : B.Text, 2));
        if (!string.IsNullOrEmpty(it.Sub)) words.Children.Add(K.Text(it.Sub!, Ty.Caption, B.Text2));
        if (locked && !string.IsNullOrEmpty(it.LockText)) words.Children.Add(K.Wrapped(it.LockText!, Ty.Caption, B.Text2, 2));
        // (what the item asks for — "Mark done", "View", "Submit" — unless its own Mark done button says it already)
        else if (!string.IsNullOrEmpty(it.Requirement) && !done && it.Markable != true)
        {
            var r = K.Text(it.Requirement!, Ty.Caption);
            r.FontWeight = FontWeights.Medium;
            r.Foreground = CourseKit.Ink(color);
            words.Children.Add(r);
        }
        words.VerticalAlignment = VerticalAlignment.Center;
        var end = K.H(8);
        if (done)
        {
            var tick = K.Icon(SymbolRegular.CheckmarkCircle24, 20, CourseKit.Green, filled: true).Tip("Done");
            AutomationProperties.SetName(tick, "Done");
            if (ticked == it.Id)
            {
                // (a tick arriving on an item marked done)
                var s = new ScaleTransform(0.6, 0.6);
                tick.RenderTransformOrigin = new Point(0.5, 0.5);
                tick.RenderTransform = s;
                Motion.FromTo(s, ScaleTransform.ScaleXProperty, 0.6, 1, Motion.Medium, Motion.Decelerate);
                Motion.FromTo(s, ScaleTransform.ScaleYProperty, 0.6, 1, Motion.Medium, Motion.Decelerate);
                Motion.FadeIn(tick, Motion.Normal);
            }
            end.Children.Add(tick);
        }
        // (another site, or a tool: in a Simpl window of its own — its symbol as a course's More and External Tools' chips
        //  have it; nothing here leaves for the browser, so no ↗)
        if (it.External == true || it.Type == "ExternalTool" && !locked) end.Children.Add(CourseKit.OwnWindowMark());
        end.VerticalAlignment = VerticalAlignment.Center;
        return K.Columns("auto, *, auto", tile.Margin(0, 0, 12, 0).Col(0), words.Col(1), end.Margin(6, 0, 0, 0).Col(2));
    }

    private void ItemMenu(ItemCollection items, ModuleItem it, ModuleData m, bool locked, bool done)
    {
        if (it.Url != null || it.Type == "ExternalTool") items.Add(CourseKit.MenuEntry("Open", SymbolRegular.DocumentArrowRight24, () => OpenItem(it)));
        if (it.Markable == true && !locked)
            items.Add(CourseKit.MenuEntry(done ? "Mark not done" : "Mark done", done ? SymbolRegular.DismissCircle24 : SymbolRegular.CheckmarkCircle24, () => Mark(it, m, !done)));
        if (string.IsNullOrEmpty(it.Url)) return;
        if (items.Count > 0) items.Add(new Separator());
        if (it.External != true) items.Add(CourseKit.MenuEntry($"Open in {engine.LmsName}", SymbolRegular.Globe24, () => engine.OpenWebScreen(PageOf(it.Url!), it.Title)));
        items.Add(CourseKit.MenuEntry("Copy link", SymbolRegular.Link24, () => CourseKit.CopyLink(engine, PageOf(it.Url!))));
    }

    /// <summary>A module item's own page on the school's site: a file's page, not its download.</summary>
    private static string PageOf(string url)
    {
        var i = url.IndexOf("/download", StringComparison.Ordinal);
        return i < 0 ? url : url[..i];
    }

    // ---- doing ----

    /// <summary>An item opened: a module's tool in a window of its own; everything else where it leads. A locked file or
    /// tool has nothing to download or launch: its page on the school's site, which says why, instead.</summary>
    private void OpenItem(ModuleItem it)
    {
        if (it.Locked == true && it.Type is "File" or "ExternalTool" && !string.IsNullOrEmpty(it.Url)) engine.OpenWebScreen(PageOf(it.Url!), it.Title);
        // (0.3.99: Macmillan Learning's and ALEKS's in their popup)
        else if (it.Type == "ExternalTool" && ctx.StartsWith("courses/", StringComparison.Ordinal)) engine.OpenModuleTool(CourseKit.IdOf(ctx), it.Id, it.Title, it.Sub, it.ToolHost);
        // (modules-file) a file as the Files screen opens one: by its id, under the module's name for it
        else if (it.Type == "File" && !string.IsNullOrEmpty(it.Url)) engine.OpenModuleFile(it.Url!, it.Title);
        else engine.GoTo(it.Url, it.Title);
    }

    private void Toggle(ModuleData m)
    {
        if (opened.Remove(m.Id))
        {
            if (Prefs.String(LastKey) == m.Id) Prefs.Remove(LastKey);
        }
        else
        {
            opened.Add(m.Id);
            Prefs.Set(LastKey, m.Id);
        }
        toggled = m.Id;
        Render();
    }

    /// <summary>Marked done (or not): shown at once, the school told, then the school's own answer — the modules read
    /// again, or back as it was.</summary>
    private async void Mark(ModuleItem it, ModuleData m, bool done)
    {
        marking[it.Id] = done;
        if (done) ticked = it.Id;
        Render();
        if (await engine.Act("markDone", new { ctx, module = m.Id, item = it.Id, done }))
        {
            try
            {
                var (d, text) = await engine.CallWithText<ModulesData>("modules", new { ctx });
                model.Take(d, text);
            }
            catch { /* as it was */ }
        }
        marking.Remove(it.Id);
        Render();
    }
}
