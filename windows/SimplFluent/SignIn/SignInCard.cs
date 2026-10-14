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
/// the lock, "Sign in to" the school the student confirmed (its name, when it was picked by name) and, smaller, the
/// address the details go to; the username and password, one line on what happens to them, Sign in in the accent, and
/// the ways out (the school's own sign-in page, another school). Everything on it is centred, the line under the fields
/// too. What is typed goes into the school page's own fields and its own button is pressed; a wrong password shakes the
/// fields and says so in the card.
/// </summary>
public sealed class SignInCard : Grid
{
    private readonly Engine.Engine engine;
    private readonly LoginAssist assist;
    private readonly TextBox user = new() { PlaceholderText = "Username", ClearButtonEnabled = true };
    private readonly PasswordBox pass = new() { PlaceholderText = "Password" };
    private readonly TextBlock host;
    private readonly TextBlock note;
    private readonly Wpf.Ui.Controls.Button go;
    private readonly FrameworkElement fields;
    private string? shownError;
    private bool autoDone;

    /// <summary>The line under the fields while nothing has gone wrong: the details go into the school's own page, and
    /// "Stay logged in?" (after) says the rest.</summary>
    public const string Helper = "Simpl types these into your school’s page.";

    public SignInCard(Engine.Engine engine, Action changeSchool)
    {
        this.engine = engine;
        assist = engine.Login;

        var lockTile = K.IconTile(SymbolRegular.LockClosed24, Appearance.Accent, 48, solid: true);
        lockTile.HorizontalAlignment = HorizontalAlignment.Center;
        lockTile.CornerRadius = new CornerRadius(12);
        // (a long school name wraps, centred, rather than being cut)
        var title = K.Wrapped(SchoolName.For(engine.Host) is { } school ? $"Sign in to {school}" : "Sign in to your school", Ty.Subtitle).Center();
        title.TextAlignment = TextAlignment.Center;
        host = K.Text("", Ty.Caption, B.Text2).Center();

        user.TextChanged += (_, _) => Ready();
        pass.PasswordChanged += (_, _) => Ready();
        user.KeyDown += (_, e) => { if (e.Key == Key.Enter) { pass.Focus(); e.Handled = true; } };
        pass.KeyDown += (_, e) => { if (e.Key == Key.Enter) { SignIn(); e.Handled = true; } };
        fields = K.V(12, user, pass);

        // (centred like the rest of the card; a problem's symbol sits at the start of its words, wrapping with them)
        note = K.Wrapped("", Ty.Caption, B.Text2);
        note.TextAlignment = TextAlignment.Center;
        note.HorizontalAlignment = HorizontalAlignment.Stretch;

        go = K.Button("Sign in", SignIn, accent: true);
        go.HorizontalAlignment = HorizontalAlignment.Stretch;
        go.Height = 34;
        go.IsEnabled = false;
        var website = K.Link("Sign in on my school’s own page", () => assist.UseWebsite());
        website.HorizontalAlignment = HorizontalAlignment.Center;
        website.ToolTip = "Your school’s own sign-in page, in the whole window — for a sign-in that asks for something this card can’t";
        var other = K.Link("Not your school? Change school…", changeSchool, Ty.Caption);
        other.HorizontalAlignment = HorizontalAlignment.Center;

        var content = K.V(0,
            lockTile.Margin(0, 0, 0, 16),
            title,
            host.Margin(0, 2, 0, 24),
            fields,
            note.Margin(0, 16, 0, 20),
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
        host.Text = $"at {assist.CaptureHost}";
        var error = assist.CaptureError;
        note.Inlines.Clear();
        if (error != null)
        {
            var icon = K.Icon(SymbolRegular.ErrorCircle24, 14, B.Critical, filled: true);
            icon.Margin = new Thickness(0, 0, 6, -2);
            note.Inlines.Add(new System.Windows.Documents.InlineUIContainer(icon) { BaselineAlignment = BaselineAlignment.Center });
            note.Inlines.Add(new System.Windows.Documents.Run(error));
            note.Fg(B.Critical);
            if (error != shownError) { Motion.Shake(fields); pass.Password = ""; pass.Focus(); }
        }
        else
        {
            note.Inlines.Add(new System.Windows.Documents.Run(Helper));
            note.Fg(B.Text2);
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
/// school's page — and, after a moment, why it may be taking a while (the school asking for a code or a click) with the
/// one next step for it, Open sign-in page, and Start over beside it. While Windows Hello asks for the saved password,
/// what it is asking for is said under it (its prompt is Windows' own, and can open behind the window). The title bar
/// has no Start over of its own meanwhile (MainWindow): one way back, here.
/// </summary>
public sealed class LoggingIn : Grid
{
    public LoggingIn(Engine.Engine engine)
    {
        var assist = engine.Login;
        // (Windows 11's order: the primary action first)
        var show = K.Button("Open sign-in page", () => assist.ShowPage(), accent: true, icon: SymbolRegular.Globe24);
        show.ToolTip = "Your school’s sign-in page, to finish what it asks for by hand";
        var restart = K.Button("Start over", () => assist.Restart());
        restart.ToolTip = "Begin signing in again from the start";
        var buttons = K.H(10, show, restart);
        buttons.HorizontalAlignment = HorizontalAlignment.Center;
        var why = K.Wrapped("Your school may be asking for a code or a click.", Ty.Body, B.Text2).Center();
        why.TextAlignment = TextAlignment.Center;
        why.MaxWidth = 380;
        var offer = K.V(12, why, buttons);
        offer.Visibility = Visibility.Collapsed;
        var hello = K.Text("", Ty.Body, B.Text2).Center();
        hello.Visibility = Visibility.Collapsed;
        var title = K.Text("Logging you in", Ty.Subtitle).Center();
        var v = K.V(16, K.Spinner(32), title, hello, offer);
        v.HorizontalAlignment = HorizontalAlignment.Center;
        v.VerticalAlignment = VerticalAlignment.Center;
        v.Margin = new Thickness(0, 0, 0, 48);
        Children.Add(v);
        void Asking(string? reason) => Dispatcher.BeginInvoke(() =>
        {
            hello.Text = reason ?? "";
            hello.Visibility = reason == null ? Visibility.Collapsed : Visibility.Visible;
        });
        Loaded += (_, _) => { Hello.Asking += Asking; Asking(Hello.Reason); };
        Unloaded += (_, _) => Hello.Asking -= Asking;
        Loaded += async (_, _) =>
        {
            Motion.FadeIn(v, Motion.Medium);
            await Task.Delay(2500);
            // (the Mac's words: the title says it is taking a while, the line under it why)
            title.Text = "Still signing in…";
            Motion.FadeIn(title, Motion.Normal);
            offer.Visibility = Visibility.Visible;
            Motion.Enter(offer, 0, 20, 420);
            Shot.Moved();
        };
    }
}

/// <summary>
/// Over the school's own page in the middle of a sign-in (the Mac's HandoffNote), as Windows 11's InfoBar: what to do
/// there, that Simpl carries on by itself once you are through, Start over, and ✕ to put it away.
/// </summary>
public sealed class HandoffBar : Border
{
    private Engine.Engine? engine;

    public HandoffBar()
    {
        var icon = K.Icon(SymbolRegular.Info24, 18, B.AccentText, filled: true).Margin(0, 0, 12, 0);
        var words = K.H(8, K.Text("Finish signing in below", Ty.BodyStrong), K.Text("Simpl carries on once you’re through.", Ty.Body));
        words.VerticalAlignment = VerticalAlignment.Center;
        var restart = K.Button("Start over", () => engine?.Login.Restart());
        restart.ToolTip = "Begin signing in again from the start";
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
