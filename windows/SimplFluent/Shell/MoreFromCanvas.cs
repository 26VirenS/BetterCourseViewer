using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Screens;
using Simpl.Support;
using Wpf.Ui.Controls;

namespace Simpl.Shell;

/// <summary>The pane's More from Canvas as the engine reads it (moreFromCanvas): its heading and its items, in the order
/// of Canvas's own navigation.</summary>
public sealed class MoreNav
{
    /// <summary>The section's heading as the engine words it: "More from Canvas", "More from Brightspace".</summary>
    public string Title { get; set; } = "";
    /// <summary>Where the items were read: page (the page's own menu), saved (the last menu seen), none.</summary>
    public string? From { get; set; }
    public List<MoreNavItem> Items { get; set; } = new();
}

/// <summary>One of the section's items: History, Help, a tool of the school's own, or a link.</summary>
public sealed class MoreNavItem
{
    public string Id { get; set; } = "";
    /// <summary>history, help, tool or link.</summary>
    public string Kind { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>What a press does, in words ("Opens in its own window"): the row's tooltip.</summary>
    public string? Note { get; set; }
    /// <summary>A tool's icon as Canvas gives it: an address, or Canvas's inline drawing as an SVG data: address.</summary>
    public string? Icon { get; set; }
    /// <summary>That icon as the page drew it, a PNG 64 across in a data: address; null where the page could not.</summary>
    public string? IconData { get; set; }
    /// <summary>The colour of Canvas's nav, which a tool's icon is made for ("#rrggbb").</summary>
    public string? Tile { get; set; }
    /// <summary>It opens away from the window (a tool, a link).</summary>
    public bool? External { get; set; }
    /// <summary>What a press does: history or help (its popup), tool (Canvas's launch page for it in the window for
    /// tools), browser (Simpl's browser window).</summary>
    public string? Open { get; set; }
    /// <summary>Where a tool or a link opens.</summary>
    public string? Url { get; set; }
}

/// <summary>History or Help as the engine lists them (moreHistory, moreHelp): the rows the extension's sheets list.</summary>
public sealed class MoreList
{
    public string Title { get; set; } = "";
    public string? Note { get; set; }
    /// <summary>What it says with no rows.</summary>
    public string? Empty { get; set; }
    /// <summary>Help's word under its rows (Report a problem is Canvas's own form); empty otherwise.</summary>
    public string? Foot { get; set; }
    public List<MoreListRow> Rows { get; set; } = new();
}

/// <summary>A row of History or Help.</summary>
public sealed class MoreListRow
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>The line under it: History's course · kind · when, Help's what it is.</summary>
    public string? Sub { get; set; }
    /// <summary>History's: Canvas's name for what was opened (assignment, quiz, discussion…).</summary>
    public string? Kind { get; set; }
    /// <summary>History's: its course's colour, when the course is one of the student's.</summary>
    public string? Color { get; set; }
    public string? Url { get; set; }
    /// <summary>place (an address of the school's own, opened as Simpl opens any) or browser (another site).</summary>
    public string? Open { get; set; }
}

/// <summary>
/// What the school added to Canvas's own navigation that no list of Canvas's gives — History, Help, its tools for every
/// course ("My Materials", Studio, a library), sometimes a link — in the pane under Simpl's own places, as the extension's
/// sidebar has it (its More from Canvas), read by the engine from the school's page (moreFromCanvas). A tool opens in the
/// window for tools on Canvas's launch page for it (light, as every outside page is); a link in Simpl's browser window,
/// the school's page for it or another site's; History and Help in popups floating over the window, listing what the
/// extension's sheets list. No section when the school adds nothing; on Brightspace, the links its navbar offers that
/// Simpl does not show already, under "More from Brightspace".
/// </summary>
public static class MoreFromCanvas
{
    private static readonly Color Gray = Color.FromRgb(0x76, 0x76, 0x7c);
    /// <summary>A tool's square where the school gives no colour of its own: Canvas's nav, as the extension draws it.</summary>
    private static readonly Color CanvasNav = Color.FromRgb(0x2d, 0x3b, 0x45);

    /// <summary>There is a section: the school added something.</summary>
    public static bool Shown(MoreNav? nav) => nav is { Items.Count: > 0 };

    /// <summary>The pane's rows for it: its heading, then its items in Canvas's order — History a clock, Help a question
    /// mark, a tool or a link the school's own picture (else a puzzle piece, a link) on a square of Canvas's nav colour,
    /// with the mark of a window of its own at the row's end. None when it lists nothing.</summary>
    public static IEnumerable<NavEntry> Entries(MoreNav? nav, Action<MoreNavItem> open)
    {
        if (!Shown(nav)) yield break;
        yield return new NavEntry { Header = true, Title = string.IsNullOrEmpty(nav!.Title) ? "More from Canvas" : nav.Title };
        foreach (var it in nav.Items) yield return Entry(it, open);
    }

    private static NavEntry Entry(MoreNavItem it, Action<MoreNavItem> open)
    {
        var e = new NavEntry { Key = "more:" + it.Id, Title = it.Label, Tip = it.Note, Go = () => open(it) };
        switch (it.Kind)
        {
            case "history": e.Icon = SymbolRegular.History24; e.Tint = Gray; break;
            case "help": e.Icon = SymbolRegular.QuestionCircle24; e.Tint = Gray; break;
            default:
                // (a tool's icon is drawn for Canvas's dark nav, often white: on a square of the nav's own colour)
                e.Square = K.Hex(it.Tile, CanvasNav);
                e.Picture = Picture(it);
                e.Icon = it.Kind == "tool" ? SymbolRegular.PuzzlePiece24 : SymbolRegular.Link24;
                e.OwnWindow = it.External != false;
                break;
        }
        return e;
    }

    /// <summary>Pictures decoded, by their data: address (the same picture each time the pane is drawn, so its rows are
    /// not built again for it).</summary>
    private static readonly Dictionary<string, ImageSource?> Pictures = new();

    /// <summary>
    /// A tool's icon as the page drew it, decoded once. None where the page could not draw it (another site's picture
    /// that does not let it): Simpl does not fetch it itself, as nothing goes anywhere but the school, and draws no SVG —
    /// the tool's symbol shows on its square instead.
    /// </summary>
    public static ImageSource? Picture(MoreNavItem it)
    {
        if (it.Kind is not ("tool" or "link") || PictureBytes(it.IconData) is not { } bytes) return null;
        var key = it.IconData!;
        if (!Pictures.TryGetValue(key, out var made)) Pictures[key] = made = Decoding.Picture(bytes)?.Source;
        return made;
    }

    /// <summary>The bytes of a picture in a base64 data: address (a PNG, a JPEG, a GIF); null for anything else (an SVG,
    /// an address).</summary>
    public static byte[]? PictureBytes(string? data)
    {
        if (string.IsNullOrEmpty(data)) return null;
        var comma = data.IndexOf(',');
        if (comma < 0) return null;
        var head = data[..comma].ToLowerInvariant();
        if (head is not ("data:image/png;base64" or "data:image/jpeg;base64" or "data:image/gif;base64")) return null;
        try { return Convert.FromBase64String(data[(comma + 1)..]); }
        catch (FormatException) { return null; }
    }

    /// <summary>A press on an item: History's or Help's popup over the window; a tool in the window for tools, on Canvas's
    /// launch page for it, without Canvas round it; a link in Simpl's browser window — the school's own page for it with
    /// the session, another site's as it is.</summary>
    public static void Open(Engine.Engine engine, MoreNavItem it, Window over)
    {
        switch (it.Open)
        {
            case "history" or "help":
                ShowList(engine, it, over);
                break;
            case "tool" when !string.IsNullOrEmpty(it.Url):
                engine.OpenTool(ToolLaunch.Page(it.Url!, it.Label));
                break;
            case "browser" when engine.Absolute(it.Url) is { } u:
                if (engine.IsSchool(u)) engine.OpenWebScreen(u.ToString(), it.Label);
                else engine.OpenBrowser(u, it.Label);
                break;
        }
    }

    /// <summary>The History and Help popups up now, by kind: pressed again, the one there comes to the front, never a
    /// second beside it.</summary>
    private static readonly Dictionary<string, Sheet> Showing = new();
    /// <summary>The kinds being read for a popup about to show: a press meanwhile adds nothing.</summary>
    private static readonly HashSet<string> Reading = new();

    /// <summary>
    /// History or Help as a popup floating over the window — nothing held: the pane, the title bar and the page stay at
    /// hand — listing what the extension's sheet lists: what it is under its title, then its rows (History's each with
    /// its kind's symbol in its course's colour, what it was and the course · kind · when under it), and under them
    /// Help's word on Report a problem. Read afresh each time it opens (Canvas's History changes as the student moves),
    /// and shown once read, as the extension's sheet is, whole from its first frame (a popup's window does not follow its
    /// card growing after it shows: a strip of bare window stays under it). A row opens where it leads — one of the
    /// school's own pages as Simpl opens any (its own screen where it has one), another site in Simpl's browser window,
    /// the mark of a window of its own at its end — and the popup goes, as the extension's sheet does.
    /// </summary>
    public static async void ShowList(Engine.Engine engine, MoreNavItem item, Window over)
    {
        var kind = item.Kind;
        if (Showing.TryGetValue(kind, out var open)) { open.Window?.Activate(); return; }
        if (!Reading.Add(kind)) return;
        var help = kind == "help";
        var name = item.Label.Length > 0 ? item.Label : help ? "Help" : "History";
        var holder = new ContentControl { Focusable = false };
        var sheet = new Sheet { Title = name, Body = holder, Width = 460, Floats = true, HoldsUpdates = false, Name = name };
        try
        {
            var list = await engine.Call<MoreList>(help ? "moreHelp" : "moreHistory");
            holder.Content = Body(engine, list, help, sheet, over);
            if (Shot.Asked) Log.Write($"more list: {kind} {list.Rows.Count} rows");
        }
        catch (Exception e)
        {
            // (Try again: this popup closed and the list asked for anew, its popup shown once it is in)
            var retry = K.Link("Try again", () => { sheet.Close(); Showing.Remove(kind); ShowList(engine, item, over); });
            retry.HorizontalAlignment = HorizontalAlignment.Left;
            retry.Margin = new Thickness(-6, 0, 0, 0);
            holder.Content = K.V(6, K.Wrapped(e.Message, Ty.Body, B.Text2), retry);
        }
        finally { Reading.Remove(kind); }
        Showing[kind] = sheet;
        sheet.Closed += () => { if (Showing.TryGetValue(kind, out var s) && s == sheet) Showing.Remove(kind); };
        Dialogs.Show(sheet, over);
    }

    private static FrameworkElement Body(Engine.Engine engine, MoreList list, bool help, Sheet sheet, Window over)
    {
        var v = K.V(12);
        if (!string.IsNullOrEmpty(list.Note)) v.Children.Add(K.Wrapped(list.Note!, Ty.Body, B.Text2));
        if (list.Rows.Count == 0)
            v.Children.Add(K.Empty(help ? SymbolRegular.QuestionCircle24 : SymbolRegular.History24, string.IsNullOrEmpty(list.Empty) ? "Nothing here yet." : list.Empty!));
        else
        {
            // (the rows reach the card's sides by their padding, so their words line up with the title; as many as the
            //  window leaves room for, then they scroll)
            var rows = CourseKit.Divided(list.Rows, r => Row(engine, r, help, sheet));
            v.Children.Add(new ScrollViewer
            {
                Content = rows, MaxHeight = Math.Clamp(over.ActualHeight - 300, 200, 440), Margin = new Thickness(-8, 0, -8, 0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });
        }
        if (!string.IsNullOrEmpty(list.Foot)) v.Children.Add(K.V(10, K.Divider(), K.Wrapped(list.Foot!, Ty.Caption, B.Text2)));
        return v;
    }

    private static FrameworkElement Row(Engine.Engine engine, MoreListRow r, bool help, Sheet sheet)
    {
        var browser = r.Open == "browser";
        var symbol = help ? SymbolRegular.Link24 : HistoryGlyph(r.Kind);
        var tint = help ? Gray : K.Hex(r.Color, Gray);
        var end = browser ? CourseKit.OwnWindowMark() : K.Icon(SymbolRegular.ChevronRight20, 12, B.Text3);
        void Open()
        {
            sheet.Close();
            if (browser) { if (engine.Absolute(r.Url) is { } u) engine.OpenBrowser(u, r.Title); }
            else if (!string.IsNullOrEmpty(r.Url)) engine.OpenWeb(r.Url!, r.Title);
        }
        var row = CourseKit.RowLink(CourseKit.InfoRow(r.Title, r.Sub, symbol, tint, end), Open);
        System.Windows.Automation.AutomationProperties.SetName(row, string.IsNullOrEmpty(r.Sub) ? r.Title : $"{r.Title}, {r.Sub}");
        // (one of the school's own pages has the menu every such row has: Open in new window, Open in Canvas, Copy link)
        if (!browser && !string.IsNullOrEmpty(r.Url)) CourseKit.CanvasRowMenu(row, engine, r.Title, r.Url, Open);
        return row;
    }

    /// <summary>What History's row was, by Canvas's name for it, as the extension's sheet draws it: the symbols Simpl gives
    /// the same things elsewhere; a page's for anything else.</summary>
    public static SymbolRegular HistoryGlyph(string? kind) => kind switch
    {
        "assignment" => SymbolRegular.DocumentText24,
        "quiz" => SymbolRegular.DocumentQuestionMark24,
        "discussion" => SymbolRegular.ChatMultiple24,
        "announcement" => SymbolRegular.Megaphone24,
        "module" => SymbolRegular.Stack24,
        "folder" => SymbolRegular.Folder24,
        "calendar-month" => SymbolRegular.CalendarLtr24,
        "home" => SymbolRegular.Home24,
        "syllabus" => SymbolRegular.BookOpen24,
        "grades" => SymbolRegular.DataBarVertical24,
        "group" or "user" => SymbolRegular.People24,
        _ => SymbolRegular.DocumentOnePage24,
    };
}
