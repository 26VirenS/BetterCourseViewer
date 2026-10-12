using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using PasswordBox = Wpf.Ui.Controls.PasswordBox;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.SignIn;

/// <summary>
/// The app's own sign-in card over the school's page (the Mac's LoginForm), as Windows 11 draws a sign-in: a card with
/// the lock, "Sign in to your school" and where, the username and password, Sign in in the accent, and the ways out
/// (the website itself, another school). What is typed goes into the school page's own fields and its own button is
/// pressed; a wrong password shakes the fields and says so in the card.
/// </summary>
public sealed class SignInCard : Grid
{
    private readonly Engine.Engine engine;
    private readonly LoginAssist assist;
    private readonly TextBox user = new() { PlaceholderText = "Username", ClearButtonEnabled = true };
    private readonly PasswordBox pass = new() { PlaceholderText = "Password" };
    private readonly TextBlock host;
    private readonly TextBlock note;
    private readonly SymbolIcon noteIcon;
    private readonly Wpf.Ui.Controls.Button go;
    private readonly FrameworkElement fields;
    private string? shownError;
    private bool autoDone;

    public SignInCard(Engine.Engine engine, Action changeSchool)
    {
        this.engine = engine;
        assist = engine.Login;

        var lockTile = K.IconTile(SymbolRegular.LockClosed24, Appearance.Accent, 48, solid: true);
        lockTile.HorizontalAlignment = HorizontalAlignment.Center;
        lockTile.CornerRadius = new CornerRadius(12);
        var title = K.Text("Sign in to your school", Ty.Subtitle).Center();
        host = K.Text("", Ty.Body, B.Text2).Center();

        user.TextChanged += (_, _) => Ready();
        pass.PasswordChanged += (_, _) => Ready();
        user.KeyDown += (_, e) => { if (e.Key == Key.Enter) { pass.Focus(); e.Handled = true; } };
        pass.KeyDown += (_, e) => { if (e.Key == Key.Enter) { SignIn(); e.Handled = true; } };
        fields = K.V(12, user, pass);

        noteIcon = K.Icon(SymbolRegular.ErrorCircle24, 16, B.Critical, filled: true);
        noteIcon.VerticalAlignment = VerticalAlignment.Top;
        noteIcon.Margin = new Thickness(0, 2, 8, 0);
        note = K.Wrapped("", Ty.Caption, B.Text2);
        var noteRow = K.Columns("auto, *", noteIcon.Col(0), note.Col(1));

        go = K.Button("Sign in", SignIn, accent: true);
        go.HorizontalAlignment = HorizontalAlignment.Stretch;
        go.Height = 34;
        go.IsEnabled = false;
        var website = K.Link("Use the website instead", () => assist.UseWebsite());
        website.HorizontalAlignment = HorizontalAlignment.Center;
        var other = K.Link("Not your school? Change school…", changeSchool, Ty.Caption);
        other.HorizontalAlignment = HorizontalAlignment.Center;

        var content = K.V(0,
            lockTile.Margin(0, 0, 0, 16),
            title,
            host.Margin(0, 4, 0, 24),
            fields,
            noteRow.Margin(0, 16, 0, 20),
            go,
            website.Margin(0, 12, 0, 0),
            other.Margin(0, 2, 0, 0));
        var card = K.Card(content, new Thickness(32, 32, 32, 24));
        card.Width = 440;
        card.HorizontalAlignment = HorizontalAlignment.Center;
        card.VerticalAlignment = VerticalAlignment.Center;
        card.Margin = new Thickness(24, 0, 24, 48);
        card.Effect = K.Shadow(40, 12, 0.14);
        Children.Add(new ScrollViewer { Content = card, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Loaded += (_, _) =>
        {
            Motion.Pop(card);
            user.Text = assist.PrefillUser;
            (user.Text.Length == 0 ? (UIElement)user : pass).Focus();
            AutoSignIn();
        };
        Update();
    }

    /// <summary>What the sign-in says now: where, and what went wrong (once, with a shake).</summary>
    public void Update()
    {
        host.Text = $"Signing in at {assist.CaptureHost}";
        var error = assist.CaptureError;
        if (error != null)
        {
            note.Text = error;
            note.Fg(B.Critical);
            noteIcon.Visibility = Visibility.Visible;
            if (error != shownError) { Motion.Shake(fields); pass.Password = ""; pass.Focus(); }
        }
        else
        {
            note.Text = "These go into your school’s own sign-in page. After you sign in, you can choose to stay logged in.";
            note.Fg(B.Text2);
            noteIcon.Visibility = Visibility.Collapsed;
        }
        shownError = error;
    }

    private void Ready() => go.IsEnabled = user.Text.Trim().Length > 0 && pass.Password.Length > 0;

    private void SignIn()
    {
        var u = user.Text.Trim();
        if (u.Length == 0 || pass.Password.Length == 0) return;
        assist.Submit(u, pass.Password);
        pass.Password = "";
    }

    /// <summary>The screenshot suite's sign-in (--sign-in user:pass): typed into the card a moment after it comes, and sent.</summary>
    private async void AutoSignIn()
    {
        if (autoDone || Launch.Get("sign-in") is not { } creds) return;
        autoDone = true;
        var colon = creds.IndexOf(':');
        if (colon <= 0) return;
        await Task.Delay(1200);
        user.Text = creds[..colon];
        pass.Password = creds[(colon + 1)..];
        await Task.Delay(600);
        SignIn();
    }
}

/// <summary>
/// "Logging you in" (the Mac's LoggingInView): Windows' progress ring over the window while the details go into the
/// school's page — and, after a moment, a way to the page, for a sign-in that wants something done by hand.
/// </summary>
public sealed class LoggingIn : Grid
{
    public LoggingIn(Engine.Engine engine)
    {
        var assist = engine.Login;
        var restart = K.Button("Start Over", () => assist.Restart());
        var show = K.Button("Show the Page", () => assist.ShowPage(), accent: true, icon: SymbolRegular.Globe24);
        var buttons = K.H(10, restart, show);
        buttons.HorizontalAlignment = HorizontalAlignment.Center;
        var offer = K.V(12, K.Text("Something to do on your school’s page?", Ty.Body, B.Text2).Center(), buttons);
        offer.Visibility = Visibility.Collapsed;
        var v = K.V(16, K.Spinner(32), K.Text("Logging you in", Ty.Subtitle).Center(), offer);
        v.HorizontalAlignment = HorizontalAlignment.Center;
        v.VerticalAlignment = VerticalAlignment.Center;
        v.Margin = new Thickness(0, 0, 0, 48);
        Children.Add(v);
        Loaded += async (_, _) =>
        {
            Motion.FadeIn(v, Motion.Medium);
            await Task.Delay(2500);
            offer.Visibility = Visibility.Visible;
            Motion.Enter(offer, 0, 20, 420);
            Shot.Moved();
        };
    }
}

/// <summary>
/// Over the school's own page in the middle of a sign-in (the Mac's HandoffNote), as Windows 11's InfoBar: what to do
/// there, that Simpl carries on by itself once you are through, Start Over, and ✕ to put it away.
/// </summary>
public sealed class HandoffBar : Border
{
    private Engine.Engine? engine;

    public HandoffBar()
    {
        var icon = K.Icon(SymbolRegular.Info24, 18, B.AccentText, filled: true).Margin(0, 0, 12, 0);
        var words = K.H(8, K.Text("Finish signing in below", Ty.BodyStrong), K.Text("Simpl carries on once you’re through.", Ty.Body));
        words.VerticalAlignment = VerticalAlignment.Center;
        var restart = K.Button("Start Over", () => engine?.Login.Restart());
        restart.Margin = new Thickness(12, 0, 4, 0);
        var close = K.IconButton(SymbolRegular.Dismiss24, () => { if (engine != null) engine.Login.Handoff = false; }, "Hide", 14, 32);
        Child = K.Columns("auto, *, auto, auto", icon.Col(0), words.Col(1), restart.Col(2), close.Col(3));
        Padding = new Thickness(16, 8, 8, 8);
        Margin = new Thickness(8, 0, 8, 8);
        CornerRadius = new CornerRadius(4);
        BorderThickness = new Thickness(1);
        SetResourceReference(BackgroundProperty, "InfoBarInformationalSeverityBackgroundBrush");
        SetResourceReference(BorderBrushProperty, "InfoBarBorderBrush");
        Visibility = Visibility.Collapsed;
    }

    public void Bind(Engine.Engine? e)
    {
        if (engine != null) engine.Login.PropertyChanged -= Changed;
        engine = e;
        if (engine != null) engine.Login.PropertyChanged += Changed;
        Changed(null, null);
    }

    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs? e)
    {
        var on = engine?.Login.Handoff == true;
        if (on == (Visibility == Visibility.Visible)) return;
        Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on) Motion.Enter(this, 0, -12, 300);
    }
}
