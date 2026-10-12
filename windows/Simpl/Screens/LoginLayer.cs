using Simpl.Engine;
using Simpl.Support;
using Simpl.UI;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Simpl.Screens;

/// <summary>
/// What covers the window while Simpl signs in (the Mac's LoginLayer; LoginAssist decides which): the app's own sign-in
/// card, when the school's page has a username and password the reader found; "Logging you in" while they go into the
/// page; and, once Canvas is reached, "Stay logged in?". (The note over the school's page while something there is the
/// student's to do, and Start Over on the school's pages, are LoginOverlay's: they sit over the page itself.)
/// </summary>
public sealed class LoginLayer
{
    private readonly Engine.Engine engine;
    private readonly LoginAssist assist;
    private string user = "", pass = "";
    private bool formUp, autoDone;
    private double formShownAt, workingSince = -1, shakeAt = -100;
    private string? shownError;

    /// <summary>The Mac's large control size: its fields and buttons.</summary>
    private const float FieldH = 30, ButtonH = 30;

    public LoginLayer(Engine.Engine engine)
    {
        this.engine = engine;
        assist = engine.Login;
    }

    /// <summary>The app's own cover is up: the card, or "Logging you in".</summary>
    public bool Covers => assist.Covers;

    /// <summary>The cover, over everything (the page's own ground under it, as the Mac's PageGround).</summary>
    public void Frame(Ui ui)
    {
        ui.T.Dark = AppView.Dark(engine.Snapshot?.Dark);
        ui.Layer(Ui.Overlay);
        ui.Fill(new R(0, 0, ui.W, ui.H), ui.T.Page);
        if (assist.Phase == LoginAssist.Mode.Capture)
        {
            workingSince = -1;
            Form(ui);
        }
        else
        {
            formUp = false;
            Working(ui);
        }
    }

    // MARK: - The sign-in card

    /// <summary>The app's own sign-in card over the school's page (the Mac's LoginForm): what is typed goes into that
    /// page's own username and password fields, and its own button is pressed; the address it goes to is shown.</summary>
    private void Form(Ui ui)
    {
        var t = ui.T;
        if (!formUp)
        {
            formUp = true;
            formShownAt = ui.Time;
            user = assist.PrefillUser;
            pass = "";
            ui.Focus = user.Length == 0 ? "login:user" : "login:pass";
            shownError = assist.CaptureError;
            if (shownError != null) Refused(ui);
        }
        else if (assist.CaptureError != shownError)
        {
            shownError = assist.CaptureError;
            if (shownError != null) Refused(ui);
        }

        const float cardW = 380, pad = 28, gap = 18;
        var inner = cardW - 2 * pad;
        var error = assist.CaptureError;
        var note = error ?? "These go into your school’s own sign-in page. After you sign in, you can choose to stay logged in.";
        var noteH = ui.Measure(note, Theme.Callout, inner, 0).Y;
        const float headerH = 44 + 8 + 27 + 8 + 18, fieldsH = FieldH + 10 + FieldH, buttonsH = ButtonH + 10 + 16;
        var cardH = pad + headerH + gap + fieldsH + gap + noteH + gap + buttonsH + pad;
        var card = new R(MathF.Round((ui.W - cardW) / 2), MathF.Round((ui.H - cardH) / 2), cardW, cardH);
        ui.Glass(card, 20, GlassKind.Panel, t.Card, 0.22f, Ui.Shadow.Panel);
        var x = card.X + pad;
        var y = card.Y + pad;

        // the header: the lock, "Sign in to your school", where
        ui.Icon("lock.shield.fill", card.CenterX, y + 22, 40, t.Accent);
        y += 44 + 8;
        ui.Text("Sign in to your school", x, y, Theme.Title2, t.Ink(), inner, TextAlignment.Center);
        y += 27 + 8;
        ui.Text($"Signing in at {assist.CaptureHost}", x, y, Theme.Callout, t.Ink2, inner, TextAlignment.Center);
        y += 18 + gap;

        // the fields (shaken once, side to side, for a wrong password — as the Mac's own login window does)
        var shake = Shake(ui);
        foreach (var k in ui.Keys)
            if (k == Key.Tab) ui.Focus = ui.Focus == "login:user" ? "login:pass" : "login:user";
        var userR = new R(x + shake, y, inner, FieldH);
        var passR = new R(x + shake, y + FieldH + 10, inner, FieldH);
        var enterUser = ui.TextField("login:user", userR, ref user, "Username", Theme.Body, null, false, false, true);
        var enterPass = ui.TextField("login:pass", passR, ref pass, "Password", Theme.Body, null, false, true, true);
        if (enterUser) ui.Focus = "login:pass";
        y += fieldsH + gap;

        // the note, or what went wrong
        ui.Text(note, x, y, Theme.Callout, error != null ? t.Red : t.Ink2, inner, TextAlignment.Center, 0);
        y += noteH + gap;

        // Sign In, and the way to the website
        var ready = user.Trim().Length > 0 && pass.Length > 0;
        if (ui.Button("login:go", new R(x, y, inner, ButtonH), "Sign In", prominent: true, enabled: ready) || (enterPass && ready)) SignIn();
        y += ButtonH + 10;
        const string link = "Use the website instead";
        var lw = ui.Measure(link, Theme.Subheadline).X;
        if (ui.LinkButton("login:web", card.CenterX - lw / 2, y, link, Theme.Subheadline)) assist.UseWebsite();

        AutoSignIn(ui);
    }

    /// <summary>The school said no: the fields shake (once the card has faded back in).</summary>
    private void Refused(Ui ui) => shakeAt = ui.Time + 0.25;

    /// <summary>The fields' offset while they shake: the Mac's keyframes (−10, 10, −7, 4, then settling).</summary>
    private float Shake(Ui ui)
    {
        var dt = ui.Time - shakeAt;
        if (dt < 0) { ui.Animating = true; return 0; }
        if (dt > 0.5) return 0;
        ui.Animating = true;
        static float Lerp(double a, double b, double u) => (float)(a + (b - a) * Math.Clamp(u, 0, 1));
        if (dt < 0.06) return Lerp(0, -10, dt / 0.06);
        if (dt < 0.14) return Lerp(-10, 10, (dt - 0.06) / 0.08);
        if (dt < 0.22) return Lerp(10, -7, (dt - 0.14) / 0.08);
        if (dt < 0.29) return Lerp(-7, 4, (dt - 0.22) / 0.07);
        if (dt < 0.44) { var u = (dt - 0.29) / 0.15; return Lerp(4, 0, 1 - Math.Pow(1 - u, 2)); }
        return 0;
    }

    private void SignIn()
    {
        var u = user.Trim();
        if (u.Length == 0 || pass.Length == 0) return;
        assist.Submit(u, pass);
        pass = "";
    }

    /// <summary>The screenshot suite's sign-in (--sign-in user:pass): typed into the card a moment after it comes, and sent.</summary>
    private void AutoSignIn(Ui ui)
    {
        if (autoDone || Launch.Get("sign-in") is not { } creds) return;
        var colon = creds.IndexOf(':');
        if (colon <= 0) { autoDone = true; return; }
        ui.Animating = true;
        var since = ui.Time - formShownAt;
        if (since > 1.2)
        {
            user = creds[..colon];
            pass = creds[(colon + 1)..];
            ui.Focus = "login:pass";
        }
        if (since > 1.8)
        {
            autoDone = true;
            SignIn();
        }
    }

    // MARK: - Logging you in

    /// <summary>"Logging you in": the spinner over the window while the page underneath is filled in and sent — and,
    /// after a moment, a way to the page, for a sign-in that wants something done by hand (the Mac's LoggingInView).</summary>
    private void Working(Ui ui)
    {
        var t = ui.T;
        if (workingSince < 0) workingSince = ui.Time;
        var offer = ui.Time - workingSince > 2.5;
        if (!offer) ui.Animating = true;
        var slide = ui.Animate("login:offer", offer ? 1 : 0, 0.42f, 0.92f); // (Motion.gentle)
        const float spinner = 32, titleH = 23, offerH = 10 + 18 + 10 + ButtonH;
        var total = spinner + 16 + titleH + offerH * slide;
        var y = (ui.H - total) / 2;
        ui.Spinner(ui.W / 2, y + spinner / 2, 14);
        y += spinner + 16;
        ui.Text("Logging you in", 0, y, Theme.Title3, t.Ink(), ui.W, TextAlignment.Center);
        y += titleH;
        if (slide > 0.02f)
        {
            // (the offer comes up from below, fading in)
            var a = slide;
            y += 10 + (1 - slide) * 28;
            ui.Text("Something to do on your school’s page?", 0, y, Theme.Callout, Theme.WithAlpha(t.Ink2, t.Ink2.A * a), ui.W, TextAlignment.Center);
            y += 18 + 10;
            var w1 = ui.ButtonWidth("Start Over");
            var w2 = ui.ButtonWidth("Show the Page", icon: true);
            var x = MathF.Round(ui.W / 2 - (w1 + 10 + w2) / 2);
            if (ui.Button("login:restart", new R(x, y, w1, ButtonH), "Start Over")) assist.Restart();
            if (ui.Button("login:show", new R(x + w1 + 10, y, w2, ButtonH), "Show the Page", prominent: true, icon: "safari")) assist.ShowPage();
        }
    }

    // MARK: - Stay logged in?

    /// <summary>"Stay logged in?", over the app's screens once Canvas is reached with details typed in the card (the Mac's alert).</summary>
    public void StayAlert(Ui ui)
    {
        if (!assist.AskStay) return;
        var t = ui.T;
        ui.Layer(Ui.Overlay + 2);
        const float w = 260, pad = 16;
        const string title = "Stay logged in?";
        const string msg = "Simpl keeps your username and password on this PC only, locked in Windows’ Credential Manager, and signs you in when your school asks again. Two-step codes still come to you.";
        var msgH = ui.Measure(msg, Theme.Caption2, w - 2 * pad, 0).Y;
        var h = pad + 64 + 12 + 16 + 8 + msgH + 16 + 28 + pad;
        var panel = new R(MathF.Round((ui.W - w) / 2), MathF.Round((ui.H - h) / 2 - 40), w, h);
        ui.Glass(panel, 12, GlassKind.Panel, null, 0, Ui.Shadow.Panel);
        var y = panel.Y + pad;
        ui.Image(System.IO.Path.Combine(AppPaths.AppDir, "Assets", "Simpl-256.png"), new R(panel.CenterX - 32, y, 64, 64));
        y += 64 + 12;
        ui.Text(title, panel.X + pad, y, Theme.Bold(Theme.Subheadline), t.Ink(), w - 2 * pad, TextAlignment.Center);
        y += 16 + 8;
        ui.Text(msg, panel.X + pad, y, Theme.Caption2, t.Ink(), w - 2 * pad, TextAlignment.Center, 0);
        y += msgH + 16;
        var bw = (w - 2 * pad - 8) / 2;
        var no = ui.Button("stay:no", new R(panel.X + pad, y, bw, 28), "Not Now");
        var yes = ui.Button("stay:yes", new R(panel.X + pad + bw + 8, y, bw, 28), "Stay Logged In", prominent: true);
        foreach (var k in ui.Keys)
        {
            if (k == Key.Enter) yes = true;
            if (k == Key.Escape) no = true;
        }
        if (yes) assist.Stay(true);
        else if (no) assist.Stay(false);
    }
}
