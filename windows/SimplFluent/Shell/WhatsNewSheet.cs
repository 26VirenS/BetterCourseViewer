using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Support;
using Wpf.Ui.Controls;
using Image = System.Windows.Controls.Image;

namespace Simpl.Shell;

/// <summary>
/// What changed in Simpl for Windows (the Mac's WhatsNewSheet), after an update or from the account menu: the app's
/// mark and the version, then the one to three changes a student will notice (<see cref="WhatsNew.Leading"/>), each with
/// its kind's symbol on its tile — new, better, fixed — and See all changes under them, which opens every version's notes
/// in place, each version named once over its notes with the day it came out as every date is written ("Oct 13"). A
/// dialog over the window, which it dims, as Windows 11's dialogs are (a click on the dimmed window closes it, as
/// Continue and Escape do): it holds no update up. The build ("v058") is Settings ▸ About's to say.
/// </summary>
public static class WhatsNewSheet
{
    public static void Show(WhatsNewData data, Window over)
    {
        var icon = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/Simpl-256.png")), Width = 56, Height = 56, HorizontalAlignment = HorizontalAlignment.Center };
        var newest = data.Releases.FirstOrDefault();
        var head = K.V(4, icon, K.Text("What’s new in Simpl for Windows", Ty.Subtitle).Center());
        if (newest != null)
        {
            var date = newest.DateText ?? DateOf(newest.Date);
            head.Children.Add(K.Text(date.Length > 0 ? $"Version {newest.Version} · {date}" : $"Version {newest.Version}", Ty.Body, B.Text2).Center());
        }
        head.Margin = new Thickness(0, 24, 0, 16);

        // (the few that lead, then every version's notes a click away)
        var leading = WhatsNew.Leading(data.Releases);
        var list = new Stack { Orientation = Orientation.Vertical, Spacing = 14 };
        foreach (var n in leading) list.Children.Add(Note(n));
        var total = data.Releases.Sum(r => r.Notes.Count);
        var all = AllChanges(data);
        all.Visibility = Visibility.Collapsed;
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(32, 0, 32, 0) };
        if (total > leading.Count)
        {
            Wpf.Ui.Controls.Button? toggle = null;
            toggle = K.Link("See all changes", () =>
            {
                var open = all.Visibility != Visibility.Visible;
                all.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                if (toggle!.Content is System.Windows.Controls.TextBlock t) t.Text = open ? "Hide all changes" : "See all changes";
                if (open) Motion.Stagger(all.Children.OfType<UIElement>(), 40, 12);
            });
            toggle.HorizontalAlignment = HorizontalAlignment.Left;
            toggle.Margin = new Thickness(38, 0, 0, 0);
            list.Children.Add(toggle);
            list.Children.Add(all);
            // (the screenshot suite: --whatsnew-all yes opens every version's notes at once)
            if (Launch.Flag("whatsnew-all")) all.Visibility = Visibility.Visible;
        }
        list.Margin = new Thickness(0, 0, 0, 20); // (room at the foot that scrolls with the list)
        scroll.Content = list;
        var body = K.Rows("auto, *", head.Row(0), scroll.Row(1));
        // (over the window, dimming it, as Windows 11's other dialogs are — not floating beside it, where it read as part
        //  of the page; Light: a click on the dimmed window closes it)
        var s = new Sheet { Body = body, Bare = true, Width = 500, HoldsUpdates = false, Light = true, Name = "What’s new" };
        s.Add("Continue", SheetButtonKind.Primary);
        Motion.Stagger(list.Children.OfType<UIElement>(), 60, 16);
        if (Shot.Asked) Log.Write($"what's new: leads with {string.Join(", ", leading.Select(n => n.Title))}; {total - leading.Count} more under See all changes");
        Dialogs.Show(s, over);
    }

    /// <summary>A change: its kind's symbol on its tile, its title, and what it means for the student.</summary>
    private static FrameworkElement Note(ReleaseNote n)
    {
        var (sym, tone) = n.Kind switch
        {
            "new" => (SymbolRegular.Sparkle24, Color.FromRgb(0x2f, 0xa8, 0x4f)),
            "fixed" => (SymbolRegular.Wrench24, Color.FromRgb(0xf7, 0x8c, 0x1a)),
            "dev" => (SymbolRegular.WindowDevTools24, Color.FromRgb(0x76, 0x76, 0x7c)),
            _ => (SymbolRegular.ArrowCircleUp24, Color.FromRgb(0x29, 0x7b, 0xe6)),
        };
        var words = K.V(2, K.Text(n.Title, Ty.BodyStrong));
        if (!string.IsNullOrEmpty(n.Body)) words.Children.Add(K.Wrapped(n.Body!, Ty.Body, B.Text2));
        return K.Columns("auto, *", K.IconTile(sym, tone, 32).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Col(0), words.Margin(12, 0, 0, 0).Col(1));
    }

    /// <summary>Every version's notes, newest first: each version named once over its notes, with its day.</summary>
    private static Stack AllChanges(WhatsNewData data)
    {
        var all = new Stack { Orientation = Orientation.Vertical, Spacing = 18, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var r in data.Releases)
        {
            var group = new Stack { Orientation = Orientation.Vertical, Spacing = 10 };
            group.Children.Add(K.Columns("*, auto", K.Text($"Version {r.Version}", Ty.BodyStrong).Col(0), K.Text(r.DateText ?? DateOf(r.Date), Ty.Caption, B.Text2).Col(1)));
            foreach (var n in r.Notes) group.Children.Add(Note(n));
            all.Children.Add(group);
        }
        return all;
    }

    /// <summary>A release's day ("2026-10-13") as every date is written: "Oct 13", with its year only when it is not
    /// this one ("Oct 13, 2025").</summary>
    public static string DateOf(string? iso)
    {
        if (string.IsNullOrEmpty(iso) || !DateTime.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return iso ?? "";
        return d.ToString(d.Year == DateTime.Today.Year ? "MMM d" : "MMM d, yyyy", CultureInfo.InvariantCulture);
    }
}
