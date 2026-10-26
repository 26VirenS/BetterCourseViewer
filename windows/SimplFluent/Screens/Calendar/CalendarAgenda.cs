using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;

namespace Simpl.Screens;

/// <summary>
/// (0.3.65, the Mac's 1.3.147 and 1.3.171, the web's agenda) The list's days, picked on a small month in To Do's column:
/// the list being the work already, To Do's own list is not shown beside it. A day clicked starts new days and the next
/// ends them (120 at most); the days between are washed in the accent, their ends on it. ‹ › move the small month; Reset
/// goes back to the next three weeks. Days picked are kept while they are not the next three weeks.
/// </summary>
public sealed partial class CalendarScreen
{
    private const string AgendaPref = "calAgenda";
    private const int AgendaMost = 120;

    /// <summary>The days the list shows, its first and its last.</summary>
    private (DateTime Start, DateTime Last) agenda = SavedAgenda() ?? StandardAgenda;
    /// <summary>The first day clicked of new days, until their last is.</summary>
    private DateTime? agendaFrom;
    /// <summary>The small month shown, when moved off the one the days start in.</summary>
    private DateTime? agendaMonth;
    private readonly Border agendaCard = K.Card(null, new Thickness(14, 12, 14, 12), 12);

    private static (DateTime Start, DateTime Last) StandardAgenda => (DateTime.Today, DateTime.Today.AddDays(20));
    private bool AgendaStandard => agenda == StandardAgenda;

    /// <summary>The column beside the calendar: To Do's (when shown), or in the list the month its days are picked on.</summary>
    private bool LeftShown => mode == "list" || TodoOn;
    private bool LeftBeside => LeftShown && Width >= TodoBesideFrom;

    private static (DateTime Start, DateTime Last)? SavedAgenda()
    {
        var parts = Prefs.String(AgendaPref)?.Split('|');
        if (parts is not { Length: 2 }) return null;
        if (!DateTime.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var s)) return null;
        if (!DateTime.TryParseExact(parts[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var e) || e < s) return null;
        return (s, e);
    }

    /// <summary>"Oct 26".</summary>
    private static string AgendaWords(DateTime d) => d.ToString("MMM d", Culture);

    /// <summary>The list's line under its title: the next three weeks, or the days picked.</summary>
    private string AgendaLine => AgendaStandard ? "The next three weeks" : $"{AgendaWords(agenda.Start)} – {AgendaWords(agenda.Last)}";

    /// <summary>The column beside the calendar, out of the page it was last in: the small month in the list, else To Do.</summary>
    private Border LeftPanel()
    {
        if (mode != "list") return TodoPanel();
        PaintAgenda();
        return Adopt(agendaCard);
    }

    /// <summary>A day clicked: the first of new days, then their last (one before the first makes that the first). The
    /// list moves once both are picked.</summary>
    private void PickAgenda(DateTime day)
    {
        FocusPage();
        if (agendaFrom is not { } from)
        {
            agendaFrom = day;
            PaintAgenda();
            return;
        }
        var start = from < day ? from : day;
        var last = from < day ? day : from;
        if (last > start.AddDays(AgendaMost - 1)) last = start.AddDays(AgendaMost - 1);
        agendaFrom = null;
        agendaMonth = null;
        agenda = (start, last);
        Prefs.Set(AgendaPref, AgendaStandard ? null : $"{DayKey(start)}|{DayKey(last)}");
        Read();
        Render();
    }

    /// <summary>Back to the next three weeks.</summary>
    private void ResetAgenda()
    {
        FocusPage();
        agendaFrom = null;
        agendaMonth = null;
        agenda = StandardAgenda;
        Prefs.Set(AgendaPref, null);
        Read();
        Render();
    }

    private void TurnAgenda(int way)
    {
        agendaMonth = AgendaShown.AddMonths(way);
        PaintAgenda();
    }

    private DateTime AgendaShown => agendaMonth ?? MonthStart(agendaFrom ?? agenda.Start);

    /// <summary>The small month as things are now: its name and ‹ ›, its six weeks, the days picked and Reset.</summary>
    private void PaintAgenda()
    {
        var month = AgendaShown;
        var start = agendaFrom ?? agenda.Start;
        var last = agendaFrom ?? agenda.Last;
        var prev = K.IconButton(SymbolRegular.ChevronLeft24, () => TurnAgenda(-1), "Previous month", 14, 28);
        var next = K.IconButton(SymbolRegular.ChevronRight24, () => TurnAgenda(1), "Next month", 14, 28);
        var name = K.Text(month.ToString("MMMM yyyy", Culture), Ty.BodyStrong);
        name.VerticalAlignment = VerticalAlignment.Center;
        var head = K.Columns("*, auto, auto", name.Col(0), prev.Col(1), next.Col(2));

        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 7 };
        var first = FirstOfWeek(month);
        for (var i = 0; i < 7; i++)
        {
            var letter = K.Text(first.AddDays(i).ToString("ddd", Culture)[..1], Ty.Caption, B.Text2).Center();
            letter.FontWeight = FontWeights.SemiBold;
            grid.Children.Add(letter.Margin(0, 0, 0, 4));
        }
        for (var i = 0; i < 42; i++) grid.Children.Add(AgendaDay(first.AddDays(i), month, start, last));
        AutomationProperties.SetName(grid, $"{month.ToString("MMMM yyyy", Culture)}: click a day");

        var words = K.Text(agendaFrom is { } f ? $"{AgendaWords(f)} – …" : $"{AgendaWords(agenda.Start)} – {AgendaWords(agenda.Last)}", Ty.BodyStrong);
        var hint = K.Text(agendaFrom == null ? "Click a day to start new days" : "Click the last day", Ty.Caption, B.Text2);
        var reset = K.Button("Reset", ResetAgenda);
        reset.MinWidth = 0;
        reset.IsEnabled = agendaFrom != null || !AgendaStandard;
        reset.ToolTip = "Back to the next three weeks";
        reset.VerticalAlignment = VerticalAlignment.Center;
        var foot = K.Columns("*, auto", K.V(2, words, hint).Col(0), reset.Margin(8, 0, 0, 0).Col(1));

        agendaCard.Child = K.V(10, head, grid, K.Divider(), foot);
        agendaCard.VerticalAlignment = VerticalAlignment.Top;
        AutomationProperties.SetName(agendaCard, "Days listed");
    }

    /// <summary>A day of the small month: its ends filled with the accent, the days between washed in it (a band from end
    /// to end), today in the accent's words, a day of the month before or after dimmed.</summary>
    private FrameworkElement AgendaDay(DateTime day, DateTime month, DateTime start, DateTime last)
    {
        var isEnd = day == start || day == last;
        var inside = day > start && day < last;
        var off = day.Month != month.Month;
        var today = day == DateTime.Today;
        var cell = new Grid { Height = 32 };
        // the band: the days between whole, and the half of each end that faces them
        if (inside || (isEnd && start != last))
        {
            var band = new Border { Opacity = 0.16, Margin = new Thickness(inside || day == last ? 0 : 16, 2, inside || day == start ? 0 : 16, 2) };
            band.SetResourceReference(Border.BackgroundProperty, B.Accent);
            cell.Children.Add(band);
        }
        var number = K.Text(day.Day.ToString(CultureInfo.CurrentCulture), Ty.Body, isEnd ? B.OnAccent : off ? B.Text3 : today ? B.AccentText : B.Text).Center();
        number.VerticalAlignment = VerticalAlignment.Center;
        if (isEnd || today) number.FontWeight = FontWeights.SemiBold;
        var dot = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Child = number };
        if (isEnd) dot.SetResourceReference(Border.BackgroundProperty, B.Accent);
        var press = K.Subtle(dot, () => PickAgenda(day), new Thickness(0), 14);
        press.Width = press.Height = 28;
        press.HorizontalAlignment = HorizontalAlignment.Center;
        press.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(press, day.ToString("D", Culture));
        cell.Children.Add(press);
        return cell;
    }
}
