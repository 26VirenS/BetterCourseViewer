using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Support;
using Wpf.Ui.Controls;

namespace Simpl.Screens;

/// <summary>
/// A quick look at a piece of work or an event of the calendar (the Mac's WorkPreview card, as PreviewItem.event makes
/// it): a click on an item — on the month's grid, in the day, the week or the list — grows the app's quick-look card out of it (WorkPreview, as the Dashboard's rows have it)
/// with what it is: its kind and course, its title, when it is due or happens, its points, where it stands and the first
/// words of what it says, with the way through to it (Open, or Open in Canvas for an event of Canvas's own) and Hand in,
/// Take quiz, Reply or See feedback where it is work. A click round it, Escape or its Close folds it back; the item that
/// opened it, pressed again, folds it back too. A double-click (or a Ctrl+click) on an item opens it whole.
/// </summary>
internal sealed class CalendarPeek
{
    private readonly Engine.Engine engine;

    public CalendarPeek(Engine.Engine engine) => this.engine = engine;

    /// <summary>An item's own key, on its button (Tag) as on its card.</summary>
    public static string KeyOf(CalEvent ev) => "cal:" + ev.Id;

    public bool IsOpen => WorkPreview.IsOpen;

    // ---- what a calendar item is, for its card ----

    /// <summary>
    /// (1.2.3) A piece of work or an event of the calendar: its line under the title is "course · kind · points · place"
    /// (native-app.js calendar). An event whose address is the calendar itself opens on Canvas's page.
    /// </summary>
    private PreviewItem Of(CalEvent ev)
    {
        var kind = CalendarKit.Text(ev.Kind) ?? "Event";
        var work = CalendarKit.IsWork(kind);
        var parts = (ev.Sub ?? "").Split(" · ").Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        var course = parts.FirstOrDefault() is { } c && c != kind && !c.EndsWith(" pts") ? c : null;
        var place = parts.Where(p => p != course && p != kind && !p.EndsWith(" pts") && !p.EndsWith(" pt")).ToList();
        var it = new PreviewItem { Id = KeyOf(ev), Title = ev.Title, Kind = kind, Symbol = CalendarKit.Glyph(kind), Course = course, Color = ev.Color };
        var day = CalendarKit.Date(ev.Date) is { } d ? CalendarKit.DayWord(d) : null;
        it.When = CalendarKit.When(day, ev.Time, work);
        it.Points = CalendarKit.Points(ev.Sub);
        it.Detail = place.Count == 0 ? null : string.Join(" · ", place);
        if (ev.Missing == true) it.Flags.Add(new WorkFlag { Word = "Missing", Kind = "bad" });
        else if (ev.Excused == true) it.Flags.Add(new WorkFlag { Word = "Excused", Kind = "muted" });
        else if (work && ev.Done == true) it.Flags.Add(new WorkFlag { Word = "Submitted", Kind = "good" });
        if (CalendarKit.Text(ev.Url) is { } url)
        {
            it.Url = url;
            var e = engine;
            if (e.TabName(url) == "calendar")
            {
                // (Canvas's own page for an event: here the calendar would only open on itself)
                it.OpenLabel = $"Open in {e.LmsName}";
                it.OpenWhole = () => e.OpenWebScreen(url, ev.Title);
            }
            else it.OpenWhole = () => e.OpenWeb(url, ev.Title);
            if (work) it.Work = (kind.ToLowerInvariant(), ev.Done == true && ev.Missing != true, true);
        }
        return it;
    }

    // ---- pressed, opened, folded back ----

    /// <summary>An item pressed: its quick look grows out of it — or, for a double-click or a Ctrl+click, the item itself.</summary>
    public void Press(CalEvent ev, FrameworkElement from) => WorkPreview.Press(Of(ev), from);

    /// <summary>A card for an item, at once (the screenshot suite's preview).</summary>
    public void Open(CalEvent ev, FrameworkElement from) => WorkPreview.Press(Of(ev), from);

    /// <summary>The screen built again with the card out (the quick look closes with the page it grew from).</summary>
    public void Anchor(FrameworkElement b, CalEvent ev) { }

    public void Close() => WorkPreview.Close();

    public void Dismiss() => WorkPreview.Dismiss();
}
