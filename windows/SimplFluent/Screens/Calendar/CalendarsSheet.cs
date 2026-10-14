using System.Windows;
using System.Windows.Controls;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Shell;

namespace Simpl.Screens;

/// <summary>
/// The calendars shown (the Mac's CalendarsPopover), as a Windows 11 dialog over the window: each with its colour, your
/// own courses first, the rest under Other Calendars — ten at most, as Canvas allows (the others wait, greyed, until one is
/// turned off). Each change is kept by the page at once and the calendar read again behind the dialog.
/// </summary>
internal static class CalendarsSheet
{
    public static void Show(Engine.Engine engine, List<CalendarChoice> choices, Action changed, Window? over)
    {
        var on = new HashSet<string>(choices.Where(c => c.On).Select(c => c.Code));
        var own = choices.Where(c => c.Own == true).ToList();
        var other = choices.Where(c => c.Own != true).ToList();
        var count = K.Text("", Ty.Body, B.Text2);
        var foot = K.Wrapped("", Ty.Caption, B.Text2);
        var boxes = new List<(CalendarChoice Choice, CheckBox Box)>();
        var quiet = false;

        void Paint()
        {
            var full = on.Count >= 10;
            count.Text = $"{on.Count} of {choices.Count} on";
            quiet = true;
            foreach (var (c, b) in boxes)
            {
                b.IsChecked = on.Contains(c.Code);
                b.IsEnabled = on.Contains(c.Code) || !full;
            }
            quiet = false;
            foot.Text = full ? "Ten calendars at most: turn one off to show another."
                : engine.OnBrightspace ? "At most 10 calendars show at once." : "Canvas shows at most 10 calendars at once.";
            foot.Fg(full ? B.Caution : B.Text2);
        }

        void Set(CalendarChoice c, bool value)
        {
            if (quiet) return;
            if (value && !on.Contains(c.Code) && on.Count >= 10) { Paint(); return; }
            if (value) on.Add(c.Code); else on.Remove(c.Code);
            Paint();
            var codes = choices.Select(x => x.Code).Where(on.Contains).ToList();
            _ = Save(codes);
        }

        async Task Save(List<string> codes)
        {
            await engine.Act("setCalendars", new { codes });
            changed();
        }

        CheckBox Row(CalendarChoice c)
        {
            var name = K.Text(c.Name, Ty.Body);
            var label = K.Columns("auto, *", K.Dot(K.Hex(c.Color), 11).Margin(0, 0, 8, 0).Col(0), name.Col(1));
            var b = new CheckBox { Content = label, ToolTip = c.Name, VerticalContentAlignment = VerticalAlignment.Center, MinHeight = 28, Padding = new Thickness(8, 0, 0, 0) };
            b.Checked += (_, _) => Set(c, true);
            b.Unchecked += (_, _) => Set(c, false);
            System.Windows.Automation.AutomationProperties.SetName(b, c.Name);
            boxes.Add((c, b));
            return b;
        }

        // (the title the popup's own, beside its ×: how many are on under it)
        var head = count.Align(HorizontalAlignment.Left, VerticalAlignment.Bottom);
        FrameworkElement body;
        if (choices.Count == 0) body = K.Text("No calendars to show.", Ty.Body, B.Text2);
        else
        {
            var list = new Stack { Orientation = Orientation.Vertical, Spacing = 2 };
            foreach (var c in own) list.Children.Add(Row(c));
            if (other.Count > 0)
            {
                list.Children.Add(K.Text("Other calendars", Ty.Caption, B.Text2).Strong().Margin(0, own.Count == 0 ? 0 : 10, 0, 4));
                foreach (var c in other) list.Children.Add(Row(c));
            }
            body = new ScrollViewer
            {
                Content = list, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 4, 0),
            };
        }
        var all = K.V(12, head, body, K.Divider(), foot);
        Paint();
        var s = new Sheet { Title = "Calendars", Body = all, Width = 400, Floats = true };
        s.Add("Done", SheetButtonKind.Primary);
        Dialogs.Show(s, over);
    }
}
