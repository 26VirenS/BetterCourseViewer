using System.Windows.Media;
using Simpl.Shell;
using Simpl.Support;
using Wpf.Ui.Controls;
using Xunit;

namespace Simpl.Tests;

/// <summary>The pane's More from Canvas (Shell/MoreFromCanvas.cs): the engine's answers read, the rows they make, and
/// a tool's picture taken only from what the page drew.</summary>
public class MoreFromCanvasTests
{
    public MoreFromCanvasTests() => _ = Profile.Folder;

    /// <summary>A PNG one pixel across, as a data: address.</summary>
    private const string Pixel = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    /// <summary>moreFromCanvas's answer for the mock Canvas: History, My Materials, Help — the tool's picture as the
    /// page drew it (a JSON string), or null where it could not.</summary>
    private static string Answer(string? iconData) => """
        {"title":"More from Canvas","from":"page","items":[
          {"id":"history:History","kind":"history","label":"History","note":"What you opened lately","href":null,"icon":null,"iconData":null,"tile":null,"external":false,"open":"history","url":null},
          {"id":"tool:https://school.test/accounts/1/external_tools/77?launch_type=global_navigation","kind":"tool","label":"My Materials","note":"Opens in its own window",
           "href":"https://school.test/accounts/1/external_tools/77?launch_type=global_navigation","icon":"https://school.test/images/tool-icon.svg",
           "iconData":ICON,"tile":"#0b2b52","external":true,"open":"tool","url":"https://school.test/accounts/1/external_tools/77?launch_type=global_navigation&display=borderless"},
          {"id":"help:Help","kind":"help","label":"Help","note":"Your school's help links","href":null,"icon":null,"iconData":null,"tile":null,"external":false,"open":"help","url":null}
        ]}
        """.Replace("ICON", iconData == null ? "null" : $"\"{iconData}\"");

    [Fact]
    public void TheSectionIsItsHeadingThenItsItemsInCanvassOrder()
    {
        var nav = Json.Read<MoreNav>(Answer(Pixel));
        Assert.NotNull(nav);
        Assert.True(MoreFromCanvas.Shown(nav));
        var rows = MoreFromCanvas.Entries(nav, _ => { }).ToList();
        Assert.Equal(new[] { "More from Canvas", "History", "My Materials", "Help" }, rows.Select(r => r.Title));
        Assert.True(rows[0].Header);
        Assert.Equal(SymbolRegular.History24, rows[1].Icon);
        Assert.Equal(SymbolRegular.QuestionCircle24, rows[3].Icon);
        Assert.False(rows[1].OwnWindow);
        Assert.Equal("What you opened lately", rows[1].Tip);
        // (the tool: the school's picture on a square of its nav's colour, the mark of a window of its own at its end)
        var tool = rows[2];
        Assert.Equal(Color.FromRgb(0x0b, 0x2b, 0x52), tool.Square);
        Assert.Null(tool.Letter);
        Assert.NotNull(tool.Picture);
        Assert.True(tool.OwnWindow);
        Assert.Equal("Opens in its own window", tool.Tip);
        Assert.Equal(rows.Count, rows.Select(r => r.Key).Distinct().Count());
    }

    [Fact]
    public void AToolOrALinkHasAMenuAndALongNameItsTooltip()
    {
        var nav = Json.Read<MoreNav>(Answer(null))!;
        var rows = MoreFromCanvas.Entries(nav, _ => { }).ToList();
        // (a tool or a link: Open and Copy link, as the Mac's sidebar; History and Help none)
        Assert.NotNull(rows[2].Menu);
        Assert.Null(rows[1].Menu);
        Assert.Null(rows[3].Menu);
        // (Copy link copies the nav's own address, not the borderless launch the window opens)
        var tool = nav.Items[1];
        Assert.Equal("https://school.test/accounts/1/external_tools/77?launch_type=global_navigation", MoreFromCanvas.Link(tool));
        tool.Href = null;
        Assert.Equal(tool.Url, MoreFromCanvas.Link(tool));
        tool.Url = "/accounts/1/external_tools/77";
        Assert.Null(MoreFromCanvas.Link(tool));
        // (a name too long for its row says itself whole over what a press does)
        nav.Items[1].Label = "The University Library's Course Reserves and Readings";
        var longRow = MoreFromCanvas.Entries(nav, _ => { }).ToList()[2];
        Assert.Equal("The University Library's Course Reserves and Readings\nOpens in its own window", longRow.Tip);
    }

    [Fact]
    public void APressGoesWhereItsItemLeads()
    {
        var school = new Uri("https://school.test/");
        Uri? Absolute(string? u) => string.IsNullOrEmpty(u) ? null : new Uri(school, u);
        bool IsSchool(Uri u) => u.Host == school.Host;
        var nav = Json.Read<MoreNav>(Answer(null))!;
        // (History and Help: their popups)
        Assert.Equal(MoreFromCanvas.Goes.List, MoreFromCanvas.Route(nav.Items[0], Absolute, IsSchool).Where);
        Assert.Equal(MoreFromCanvas.Goes.List, MoreFromCanvas.Route(nav.Items[2], Absolute, IsSchool).Where);
        // (a tool: the window for tools, on its borderless launch address)
        Assert.Equal((MoreFromCanvas.Goes.Tool, "https://school.test/accounts/1/external_tools/77?launch_type=global_navigation&display=borderless"),
            MoreFromCanvas.Route(nav.Items[1], Absolute, IsSchool));
        // (a link: the school's own page for it, or another site in the browser window)
        var own = new MoreNavItem { Kind = "link", Open = "browser", Url = "/accounts/1/library" };
        Assert.Equal((MoreFromCanvas.Goes.School, "https://school.test/accounts/1/library"), MoreFromCanvas.Route(own, Absolute, IsSchool));
        var away = new MoreNavItem { Kind = "link", Open = "browser", Url = "https://library.example.org/reserves" };
        Assert.Equal((MoreFromCanvas.Goes.Browser, "https://library.example.org/reserves"), MoreFromCanvas.Route(away, Absolute, IsSchool));
        Assert.Equal(MoreFromCanvas.Goes.Nowhere, MoreFromCanvas.Route(new MoreNavItem { Kind = "tool", Open = "tool" }, Absolute, IsSchool).Where);
    }

    [Fact]
    public void NoSectionWhenTheSchoolAddsNothing()
    {
        Assert.False(MoreFromCanvas.Shown(null));
        var none = Json.Read<MoreNav>("""{"title":"More from Canvas","items":[],"from":"none"}""");
        Assert.False(MoreFromCanvas.Shown(none));
        Assert.Empty(MoreFromCanvas.Entries(none, _ => { }));
    }

    [Fact]
    public void AToolsPictureIsOnlyWhatThePageDrew()
    {
        Assert.NotNull(MoreFromCanvas.PictureBytes(Pixel));
        // (an SVG is not drawn, another site's address is not fetched: the tool's symbol on its square instead)
        Assert.Null(MoreFromCanvas.PictureBytes("data:image/svg+xml;utf8,%3Csvg%3E%3C/svg%3E"));
        Assert.Null(MoreFromCanvas.PictureBytes("https://cdn.example.com/icon.png"));
        Assert.Null(MoreFromCanvas.PictureBytes("data:image/png;base64,not base64!"));
        var nav = Json.Read<MoreNav>(Answer(null))!;
        var tool = MoreFromCanvas.Entries(nav, _ => { }).Single(r => r.Title == "My Materials");
        Assert.Null(tool.Picture);
        Assert.Equal(SymbolRegular.PuzzlePiece24, tool.Icon);
        Assert.NotNull(tool.Square);
    }

    [Fact]
    public void HistoryAndHelpRowsAreRead()
    {
        var history = Json.Read<MoreList>("""
            {"title":"History","note":"What you opened lately, as Canvas recorded it.","empty":"Nothing visited yet.","rows":[
              {"id":"0:https://school.test/courses/101/assignments/1002","title":"Composition of Functions","sub":"F26-MATH 021 20 · Assignment · today",
               "kind":"assignment","category":"Assignment","course":"F26-MATH 021 20","color":"#34c759","when":"2026-10-14T12:00:00.000Z","whenText":"today",
               "url":"https://school.test/courses/101/assignments/1002","open":"place"}]}
            """)!;
        Assert.Single(history.Rows);
        Assert.Equal("place", history.Rows[0].Open);
        Assert.Equal("#34c759", history.Rows[0].Color);
        Assert.Equal(SymbolRegular.DocumentText24, MoreFromCanvas.HistoryGlyph(history.Rows[0].Kind));
        Assert.Equal(SymbolRegular.DocumentQuestionMark24, MoreFromCanvas.HistoryGlyph("quiz"));
        Assert.Equal(SymbolRegular.DocumentOnePage24, MoreFromCanvas.HistoryGlyph("something-new"));
        var help = Json.Read<MoreList>("""
            {"title":"Help","note":"","empty":"No help links.","foot":"Reporting a problem uses Canvas's own form: open the page in stock Canvas and choose Help.",
             "rows":[{"id":"0:https://it.example.edu/help","title":"IT Help Desk","sub":"Campus technology support","url":"https://it.example.edu/help","open":"browser"}]}
            """)!;
        Assert.Equal("browser", help.Rows[0].Open);
        Assert.StartsWith("Reporting a problem", help.Foot);
    }
}
