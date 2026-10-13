using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Simpl.Kit;
using Simpl.Models;
using Wpf.Ui.Controls;
using Image = System.Windows.Controls.Image;

namespace Simpl.Shell;

/// <summary>
/// What changed in Simpl for Windows (the Mac's WhatsNewSheet), after an update or from the account menu: the app's
/// mark, each version's notes with their kind's symbol on its tile — new, better, fixed — and Continue, as a Windows 11
/// dialog over the window.
/// </summary>
public static class WhatsNewSheet
{
    public static void Show(WhatsNewData data, Window over)
    {
        var icon = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/Simpl-256.png")), Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Center };
        var head = K.V(6, icon, K.Text("What’s New in Simpl for Windows", Ty.Subtitle).Center());
        if ((data.Version ?? data.Releases.FirstOrDefault()?.Version) is { } v) head.Children.Add(K.Text($"Version {v}", Ty.Body, B.Text2).Center());
        head.Margin = new Thickness(0, 28, 0, 16);

        var list = new Stack { Orientation = Orientation.Vertical, Spacing = 18 };
        foreach (var r in data.Releases)
        {
            var group = new Stack { Orientation = Orientation.Vertical, Spacing = 10 };
            if (data.Releases.Count > 1)
                group.Children.Add(K.Columns("*, auto", K.Text($"Version {r.Version}", Ty.BodyStrong).Col(0), K.Text(r.Date ?? "", Ty.Caption, B.Text2).Col(1)));
            foreach (var n in r.Notes)
            {
                var (sym, tone) = n.Kind switch
                {
                    "new" => (SymbolRegular.Sparkle24, Color.FromRgb(0x2f, 0xa8, 0x4f)),
                    "fixed" => (SymbolRegular.Wrench24, Color.FromRgb(0xf7, 0x8c, 0x1a)),
                    _ => (SymbolRegular.ArrowCircleUp24, Color.FromRgb(0x29, 0x7b, 0xe6)),
                };
                var words = K.V(2, K.Text(n.Title, Ty.BodyStrong));
                if (!string.IsNullOrEmpty(n.Body)) words.Children.Add(K.Wrapped(n.Body!, Ty.Body, B.Text2));
                group.Children.Add(K.Columns("auto, *", K.IconTile(sym, tone, 32).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Col(0), words.Margin(12, 0, 0, 0).Col(1)));
            }
            list.Children.Add(group);
        }
        list.Margin = new Thickness(0, 0, 0, 16); // (room at the foot that scrolls with the list)
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(32, 0, 32, 0) };
        var body = K.Rows("auto, *", head.Row(0), scroll.Row(1));
        var s = new Sheet { Body = body, Bare = true, Width = 500, Height = 560, HoldsUpdates = false };
        s.Add("Continue", SheetButtonKind.Primary);
        Motion.Stagger(list.Children.OfType<UIElement>(), 60, 16);
        Dialogs.Show(s, over);
    }
}
