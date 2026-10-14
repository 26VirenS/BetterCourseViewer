using System.Windows;
using System.Windows.Controls;
using Simpl.Kit;
using Simpl.Models;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;

namespace Simpl.Screens;

/// <summary>
/// A page of a course or a group (the Mac's PageView; its front page when no name is given), to read at a reading width:
/// its title, its course and when it was last edited and by whom under it, why it is locked if it is, and its text.
/// Beside the title: Previous and Next (the page before and after it, as the school's own buttons walk them) and the
/// school's own page for it.
/// </summary>
public sealed class PageScreen : Screen
{
    /// <summary>The items either side of one (the page's `neighbours`): each a name and its address.</summary>
    private sealed class ItemNeighbours
    {
        public sealed class Item
        {
            public string Name { get; set; } = "";
            public string Url { get; set; } = "";
        }

        public Item? Prev { get; set; }
        public Item? Next { get; set; }
    }

    private readonly string ctx, slug;
    private readonly Loader<PageData> data;
    private ItemNeighbours? around;
    private string? aroundFor;
    private FrameworkElement? commands;
    private string? shownTitle, shownSub;

    public PageScreen(Engine.Engine engine, string ctx, string slug) : base(engine)
    {
        this.ctx = ctx;
        this.slug = slug;
        data = Watch(new Loader<PageData>());
    }

    private object Args => new { ctx, slug = slug.Length == 0 ? null : slug };

    public override string Title => data.Data?.Title is { Length: > 0 } t ? t : "Page";

    /// <summary>Its course, and when it was last edited and by whom (the Mac's subtitle and the line under its heading);
    /// the shell drops the course where the breadcrumb names it already.</summary>
    public override string? Subtitle => data.Data is { } d ? string.Join(" · ", new[] { d.Context, d.Edited }.Where(s => !string.IsNullOrEmpty(s))) : null;

    /// <summary>A page's long name wraps to three lines before it is cut.</summary>
    public override int TitleLines => 3;

    /// <summary>Its course's colour, in the square before its title.</summary>
    public override System.Windows.Media.Color? TitleColor => Support.Appearance.Hex(data.Data?.Color) ?? CourseKit.ContextColor(engine, ctx);

    /// <summary>The page's own address on the school's site (the front page's, when it has no name).</summary>
    private string CanvasPath
    {
        get
        {
            var s = data.Data?.Slug is { Length: > 0 } known ? known : slug;
            return s.Length == 0 ? $"/{ctx}/wiki" : $"/{ctx}/pages/{s}";
        }
    }

    /// <summary>Previous and Next (once they are known, and only where there is something either side), and More.</summary>
    public override FrameworkElement? Commands
    {
        get
        {
            if (commands != null) return commands;
            var row = K.H(8);
            if (around is { } a && (a.Prev != null || a.Next != null))
            {
                var prev = NeighbourButton(a.Prev, "Previous", false);
                var next = NeighbourButton(a.Next, "Next", true);
                row.Children.Add(K.H(4, prev, next));
            }
            row.Children.Add(CourseKit.MoreButton(engine, () => CanvasPath, () => Title));
            return commands = row;
        }
    }

    /// <summary>Words on them, not bare chevrons, so they are never taken for Back and Forward.</summary>
    private Button NeighbourButton(ItemNeighbours.Item? item, string word, bool forward)
    {
        var text = K.Text(word, Ty.Body);
        var chevron = K.Icon(forward ? SymbolRegular.ChevronRight20 : SymbolRegular.ChevronLeft20, 12, B.Text);
        var b = new Button
        {
            Content = forward ? K.H(6, text, chevron) : K.H(6, chevron, text),
            Appearance = ControlAppearance.Secondary,
            IsEnabled = item != null,
            ToolTip = item != null ? $"{word}: {item.Name}" : forward ? "Nothing after this" : "Nothing before this",
        };
        ToolTipService.SetShowOnDisabled(b, true);
        if (item != null) b.Click += (_, _) => engine.OpenWeb(item.Url, item.Name);
        return b;
    }

    public override void Refresh()
    {
        data.Ensure(engine, "page", Args);
        Around();
    }

    /// <summary>The pages either side of this one, asked for once its name is known.</summary>
    private async void Around()
    {
        var s = data.Data?.Slug is { Length: > 0 } known ? known : slug;
        if (s.Length == 0 || aroundFor == s) return;
        aroundFor = s;
        try { around = await engine.Call<ItemNeighbours>("neighbours", new { ctx, type = "Page", id = s }); }
        catch { around = null; }
        commands = null;
        HeaderChange();
    }

    protected override FrameworkElement Build()
    {
        if (data.Data is not { } d) return K.LoadState(data.Error, () => data.Reload(engine, "page", Args));
        if (Title != shownTitle || Subtitle != shownSub) { shownTitle = Title; shownSub = Subtitle; HeaderChange(); }
        Around();
        var v = K.V(22);
        if (!string.IsNullOrEmpty(d.LockText))
        {
            // why it is locked, on the caution's tint
            var words = K.Wrapped(d.LockText!, Ty.Body, B.Text);
            var line = K.Columns("auto, *", K.Icon(SymbolRegular.LockClosed24, 18, K.Tone("orange"), filled: true).Margin(0, 1, 12, 0).Col(0), words.Col(1));
            var card = K.Card(line, new Thickness(16)).Bg(B.CautionBack);
            v.Children.Add(card);
        }
        if (d.Html.Length > 0) v.Children.Add(K.Card(CourseKit.Rich(engine, d.Html), new Thickness(24, 20, 24, 20)));
        return CourseKit.Capped(v, 920); // (to read at a reading width, as the Mac's page of 920)
    }
}
