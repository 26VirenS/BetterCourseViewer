using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using PasswordBox = Wpf.Ui.Controls.PasswordBox;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Screens;

/// <summary>
/// (0.3.81) Sync's calendar accounts (Support/CalendarAccounts.cs), at the head of its sheet (the Mac's SyncAccounts):
/// each account signed in to — who, how the last sync went, Sync Now, how many of its calendars show (chosen with
/// Calendars, beside Sync), and whether Simpl's own calendar is written there — then a button for each kind of account:
/// iCloud (its Apple ID and an app-specific password, asked for here), Google and Microsoft (signed in to in the browser).
/// Drawn again by the sheet as anything changes (<c>repaint</c>); its fields kept across those drawings.
/// </summary>
internal sealed class SyncAccounts(Action repaint)
{
    private string? problem;
    private string? leaving;
    private bool askingICloud;
    private readonly TextBox appleId = new() { PlaceholderText = "Apple ID (your iCloud email)", MinHeight = 32 };
    private readonly PasswordBox appPassword = new() { PlaceholderText = "App-specific password", MinHeight = 32 };

    private static SymbolRegular Symbol(string kind) => kind switch
    {
        CalendarKinds.ICloud => SymbolRegular.Cloud24,
        CalendarKinds.Google => SymbolRegular.Globe24,
        _ => SymbolRegular.Mail24,
    };

    /// <summary>The iCloud form asked for at once (the screenshot suite's --open sync-icloud).</summary>
    public void AskICloud() => askingICloud = true;

    public FrameworkElement Build()
    {
        var v = K.V(10);
        foreach (var a in CalendarAccounts.Accounts) v.Children.Add(Card(a));
        v.Children.Add(askingICloud ? ICloudForm() : Buttons());
        if (problem != null && !askingICloud) v.Children.Add(Line(problem, SymbolRegular.Warning24, B.Critical));
        return v;
    }

    private static FrameworkElement Line(string text, SymbolRegular icon, string color)
    {
        var words = K.Wrapped(text, Ty.Caption, color);
        words.VerticalAlignment = VerticalAlignment.Center;
        return K.Columns("auto, *", K.Icon(icon, 15, color, filled: true).Margin(2, 0, 8, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(0), words.Col(1));
    }

    private static string Ago(DateTime utc)
    {
        var ago = DateTime.UtcNow - utc;
        return ago.TotalSeconds < 60 ? "just now"
            : ago.TotalMinutes < 60 ? $"{(int)ago.TotalMinutes} min ago"
            : ago.TotalHours < 24 ? $"{(int)ago.TotalHours} hr ago"
            : CalendarKit.DateWords(utc.ToLocalTime().Date);
    }

    private static string Events(int n) => n == 1 ? "1 event" : $"{n} events";

    // ---- an account ----

    private FrameworkElement Card(CalendarAccount a)
    {
        var syncing = CalendarAccounts.Busy.Contains(a.Id);
        var tile = K.IconTile(Symbol(a.Kind), K.Hex(CalendarKinds.Tint(a.Kind)), 34);
        var who = K.V(2, K.Text(CalendarKinds.Name(a.Kind), Ty.BodyStrong), K.Text(a.Name, Ty.Caption, B.Text2));
        who.VerticalAlignment = VerticalAlignment.Center;
        FrameworkElement act;
        if (a.SignedOut) act = K.Button("Sign In Again", () => Again(a));
        else if (syncing) act = K.Spinner(16).Margin(6, 0, 6, 0);
        else act = K.IconButton(SymbolRegular.ArrowSync24, () => _ = CalendarAccounts.Sync(a.Id), "Sync Now", 14, 28);
        act.VerticalAlignment = VerticalAlignment.Center;
        Button? more = null;
        more = K.IconButton(SymbolRegular.MoreHorizontal24, () =>
        {
            var menu = new ContextMenu { PlacementTarget = more, Placement = PlacementMode.Bottom };
            menu.Items.Add(CalendarKit.Item("Sign Out…", SymbolRegular.SignOut24, () => { leaving = a.Id; repaint(); }));
            menu.IsOpen = true;
        }, "More", 14, 28);
        more.Margin = new Thickness(4, 0, 0, 0);
        var head = K.Columns("auto, *, auto, auto", tile.Margin(0, 0, 12, 0).Col(0), who.Col(1), act.Col(2), more.Col(3));

        var status = K.V(4);
        if (syncing) status.Children.Add(K.H(8, K.Spinner(14), K.Text(a.Synced == null ? "Reading your calendars…" : "Syncing…", Ty.Caption, B.Text2)));
        else if (a.Problem.Length > 0) status.Children.Add(Line(a.Problem, SymbolRegular.Warning24, B.Critical));
        else if (a.Synced is { } at)
        {
            var outWords = a.Push ? $" · {Events(a.Pushed)} out" : "";
            status.Children.Add(Line($"Synced {Ago(at)} · {Events(a.Pulled)} in{outWords}", SymbolRegular.CheckmarkCircle24, B.Success));
        }
        if (a.Calendars.Count > 0)
            status.Children.Add(K.Text($"{a.Calendars.Count(c => c.On)} of {a.Calendars.Count} calendars shown · choose them with Calendars", Ty.Caption, B.Text2));

        FrameworkElement foot;
        if (leaving == a.Id)
        {
            var yes = K.Button("Sign Out", () => _ = SignOut(a));
            yes.Foreground = K.Brush(K.Tone("red"));
            var no = K.Button("Cancel", () => { leaving = null; repaint(); });
            var words = a.Push
                ? $"Sign out of {a.Name}? Its calendars leave Simpl's calendar. The Simpl calendar in {CalendarKinds.Name(a.Kind)} stays there; delete it there if you don't want it."
                : $"Sign out of {a.Name}? Its calendars leave Simpl's calendar.";
            foot = K.V(8, K.Wrapped(words, Ty.Body), K.H(8, yes, no));
        }
        else
        {
            var push = new ToggleSwitch { IsChecked = a.Push, IsEnabled = !a.SignedOut, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetName(push, "Add My Work and Plans");
            push.Click += (_, _) => CalendarAccounts.SetPush(a.Id, push.IsChecked == true);
            var label = K.V(2,
                K.Text("Add My Work and Plans", Ty.Body),
                K.Wrapped($"Due dates, your own tasks and your plans, in a calendar named Simpl in {CalendarKinds.Name(a.Kind)}.", Ty.Caption, B.Text2));
            foot = K.Columns("*, auto", label.Col(0), push.Col(1));
        }
        return K.Card(K.V(10, head, status, K.Divider(0), foot), new Thickness(14), 8);
    }

    private void Again(CalendarAccount a)
    {
        if (a.Kind == CalendarKinds.ICloud)
        {
            appleId.Text = a.Name;
            askingICloud = true;
            repaint();
            return;
        }
        _ = Connect(a.Kind);
    }

    private async Task SignOut(CalendarAccount a)
    {
        leaving = null;
        await CalendarAccounts.Disconnect(a.Id);
        repaint();
    }

    // ---- signing in ----

    /// <summary>A button for each kind of account: another of the same kind once one is signed in to.</summary>
    private FrameworkElement Buttons()
    {
        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -8, -8) };
        foreach (var kind in CalendarKinds.All)
        {
            var adding = CalendarAccounts.Busy.Contains("new:" + kind);
            var available = CalendarKinds.Available(kind);
            var another = CalendarAccounts.Accounts.Any(a => a.Kind == kind);
            var title = another ? $"Add Another {(kind == CalendarKinds.ICloud ? "iCloud" : kind == CalendarKinds.Google ? "Google" : "Microsoft")} Account" : CalendarKinds.Action(kind);
            FrameworkElement mark = adding ? K.Spinner(16) : K.Icon(Symbol(kind), 16, available ? K.Hex(CalendarKinds.Tint(kind)) : K.Hex("#8a8a8a"));
            var words = K.V(1, K.Text(title, Ty.Body), K.Text(available ? CalendarKinds.Subtitle(kind) : "Coming in a later beta", Ty.Caption, B.Text2));
            var k = kind;
            var b = K.Subtle(K.Columns("auto, *", mark.Margin(0, 0, 10, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(0), words.Col(1)),
                () => { if (k == CalendarKinds.ICloud) { problem = null; askingICloud = true; repaint(); } else _ = Connect(k); },
                new Thickness(12, 0, 12, 0), 8);
            b.Height = 50;
            b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            b.Bg(B.Subtle);
            b.IsEnabled = available && !adding;
            b.ToolTip = available ? title : $"{CalendarKinds.Name(kind)} sign-in comes in a later beta.";
            b.Margin = new Thickness(0, 0, 8, 8);
            System.Windows.Automation.AutomationProperties.SetName(b, title);
            grid.Children.Add(b);
        }
        return grid;
    }

    private async Task Connect(string kind)
    {
        problem = null;
        repaint();
        problem = await CalendarAccounts.ConnectOAuth(kind);
        repaint();
    }

    /// <summary>iCloud's own: Windows has no Apple Calendar to ask, so the Apple ID and an app-specific password, made at
    /// account.apple.com for Simpl alone (never the Apple ID's own password, which Apple does not let a calendar use).</summary>
    private FrameworkElement ICloudForm()
    {
        if (appleId.Parent is Panel p1) p1.Children.Remove(appleId);
        if (appPassword.Parent is Panel p2) p2.Children.Remove(appPassword);
        var busy = CalendarAccounts.Busy.Contains("new:" + CalendarKinds.ICloud);
        var tile = K.IconTile(SymbolRegular.Cloud24, K.Hex(CalendarKinds.Tint(CalendarKinds.ICloud)), 34);
        var intro = K.V(3,
            K.Text("Sign in to iCloud Calendar", Ty.BodyStrong),
            K.Wrapped("Use your Apple ID and an app-specific password, not your Apple ID's own password. Make one at account.apple.com, under Sign-In and Security, App-Specific Passwords.", Ty.Caption, B.Text2));
        var head = K.Columns("auto, *", tile.Margin(0, 0, 12, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Col(0), intro.Col(1));
        var make = K.Subtle(K.H(6, K.Icon(SymbolRegular.Key24, 14, B.AccentText), K.Text("Make an App-Specific Password", Ty.Body, B.AccentText)),
            () => Simpl.Engine.Engine.OpenExternally(new Uri("https://account.apple.com/account/manage")), new Thickness(4, 4, 8, 4), 6);
        make.HorizontalAlignment = HorizontalAlignment.Left;
        System.Windows.Automation.AutomationProperties.SetName(appleId, "Apple ID");
        System.Windows.Automation.AutomationProperties.SetName(appPassword, "App-specific password");
        appleId.IsEnabled = !busy;
        appPassword.IsEnabled = !busy;
        var go = K.Button(busy ? "Signing In…" : "Sign In", () => _ = SignInICloud(), accent: true);
        go.IsEnabled = !busy;
        var cancel = K.Button("Cancel", () => { askingICloud = false; problem = null; appPassword.Password = ""; repaint(); });
        cancel.IsEnabled = !busy;
        var form = K.V(10, head, appleId, appPassword, make, K.H(8, go, cancel));
        if (problem != null) form.Children.Add(Line(problem, SymbolRegular.Warning24, B.Critical));
        appPassword.KeyDown -= Enter;
        appPassword.KeyDown += Enter;
        return K.Card(form, new Thickness(14), 8);
    }

    private void Enter(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        e.Handled = true;
        _ = SignInICloud();
    }

    private async Task SignInICloud()
    {
        if (CalendarAccounts.Busy.Contains("new:" + CalendarKinds.ICloud)) return;
        problem = null;
        repaint();
        problem = await CalendarAccounts.ConnectICloud(appleId.Text, appPassword.Password);
        if (problem == null)
        {
            askingICloud = false;
            appPassword.Password = "";
        }
        repaint();
    }
}
