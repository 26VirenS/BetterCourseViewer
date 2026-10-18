using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Shell;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

/// <summary>
/// (Mac 1.3.66) A Macmillan Learning assignment (Achieve, Sapling, LaunchPad) in a popup over the window, as an Alta one
/// opens (the Mac's MacmillanScreen): across the top the Canvas assignment's name over "Macmillan Learning · Due Oct 12 at
/// 11:59 PM" (as Alta's header has it), the pinned tools and Close; below, Macmillan's page, on white and in light
/// whatever the app's appearance (Shell's LightPages) — the popup's Loading while it opens, or why it could not.
/// (0.3.32, the Mac's 1.3.114) Skinned as Alta's is (extension/tools/macmillan/skin.js): before it is opened, its cover as
/// a start screen (its questions, its due line, its grading settings, Open assignment); then down the left the score, how
/// many questions are done, every question as a tile — the one shown filled, a solved one ticked, one tried and not yet
/// right marked — Show browser and the grading settings; in the rest Macmillan's own question, its parts on cards, and
/// along its foot the arrows, Question or My answer, Resources, AI Tutor, Solution, Switch to new layout and the main
/// button. Show browser puts Macmillan's page back as it draws it; another kind of Macmillan assignment is shown as it is.
/// </summary>
public static class MacmillanPopup
{
    public static void Show(Engine.Engine engine, AltaLaunch launch, Window over) => new MacmillanPopupView(engine, launch, over).Open();
}

internal sealed class MacmillanPopupView
{
    private sealed class LaunchAnswer { public string Url { get; set; } = ""; }

    private readonly Engine.Engine engine;
    private readonly AltaLaunch launch;
    private readonly Window over;
    private readonly Grid root = new();
    private readonly WebView2 web = new() { Visibility = Visibility.Hidden, DefaultBackgroundColor = System.Drawing.Color.White };
    private readonly Border stateSlot = new();
    private readonly TextBlock title, subtitle;
    private Sheet? sheet;
    private readonly FrameworkElement header;
    /// <summary>The page still opening (the launch through Canvas): a spinner in its place meanwhile.</summary>
    private bool opening = true;
    /// <summary>The launch could not be had from Canvas.</summary>
    private string? failure;
    private bool started, closing, closed, saving;
    /// <summary>(0.3.27) Save Page Code, with the developer tools on; the page's frames kept for it.</summary>
    private readonly ContentControl codeSlot = new() { Focusable = false, VerticalAlignment = VerticalAlignment.Center };
    private PageCode? code;
    /// <summary>(0.3.32) The assessment as the skin reads it, and the frame it reads it in (where its presses go).</summary>
    private MacmillanState? state;
    private CoreWebView2Frame? player;
    private bool browser = Prefs.Bool("macmillan:browser");
    private readonly ContentControl railSlot = new() { Focusable = false };
    private readonly ContentControl barSlot = new() { Focusable = false };
    private readonly ContentControl coverSlot = new() { Focusable = false };
    private static readonly System.Text.Json.JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public MacmillanPopupView(Engine.Engine engine, AltaLaunch launch, Window over)
    {
        this.engine = engine;
        this.launch = launch;
        this.over = over;
        root.Bg(B.Solid);
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var tile = K.IconTile(SymbolRegular.Library24, K.Tone("red"), 32);
        title = K.Text(launch.Title, Ty.Subtitle);
        subtitle = K.Text("Macmillan Learning", Ty.Body, B.Text2);
        header = AltaLook.Header(engine, tile, title, subtitle, codeSlot, Close, "Close — your work is kept by Macmillan");
        root.Children.Add(header.Row(0));
        // (the page on white, edge to edge under the header, as Macmillan draws it — clear of the popup's rounded foot;
        // the popup's own ground in its place while it opens)
        stateSlot.Bg(B.Solid);
        var holder = new Grid { Background = Brushes.White, Children = { web, coverSlot, stateSlot } };
        // (clear of the popup's sides, which size it: FloatPopup.EdgeRoom)
        web.Margin = new Thickness(FloatPopup.EdgeRoom, 0, FloatPopup.EdgeRoom, 8);
        // (0.3.32) the rail at the left, the page and the bar at its foot at the right
        var column = new Grid();
        column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        column.Children.Add(holder.Row(0));
        column.Children.Add(barSlot.Row(1));
        var body = K.Columns("auto, *", railSlot.Col(0), column.Col(1));
        var page = new Border { Child = body, BorderThickness = new Thickness(0, 1, 0, 0) }.Stroke(B.Divider);
        page.BorderThickness = new Thickness(0, 1, 0, 0);
        root.Children.Add(page.Row(1));
    }

    public void Open()
    {
        sheet = AltaLook.Popup(root, header, Close, "Macmillan Learning", "macmillan");
        sheet.Closed += Cleanup;
        // (no update goes in while it is open: Support/Updates.cs, the Mac's UpdateHold)
        sheet.Closed += Support.Updates.Hold("Macmillan Learning", "when you close Macmillan Learning").Dispose;
        root.PreviewKeyDown += OnKey;
        DevMode.Changed += OnDevMode;
        CodeButton();
        RoutedEventHandler? loaded = null;
        loaded = (_, _) =>
        {
            root.Loaded -= loaded;
            AltaLook.Settle(root);
            Start();
        };
        root.Loaded += loaded;
        Draw();
        Dialogs.Show(sheet, over);
        Name();
    }

    /// <summary>The header from the Canvas assignment: its name (opened from a tool's row, the popup had the tool's), and
    /// when it is due beside "Macmillan Learning".</summary>
    private async void Name()
    {
        Doubled();
        if (await AltaLook.Assignment(engine, launch) is not { } a || closed) return;
        if (a.Title is { Length: > 0 } name) { title.Text = name; named = true; }
        if (a.DueLine is { Length: > 0 } due) subtitle.Text = $"Macmillan Learning · {due}";
        Doubled();
    }

    /// <summary>The header named from the Canvas assignment (else, once the page is in, from the page's own heading).</summary>
    private bool named;

    /// <summary>
    /// No Canvas assignment to name it (opened from External Tools' row, which has no assignment): the page's own heading
    /// ("Chapter 3 Homework") as the title, or its document's title — never "Macmillan Learning" over "Macmillan Learning".
    /// </summary>
    private async void NameFromPage(CoreWebView2 core)
    {
        if (named || closed) return;
        try
        {
            var json = await core.ExecuteScriptAsync("(function(){var h=document.querySelector('h1');var t=(h&&h.innerText||'').trim();return t||(document.title||'').trim();})()");
            var heading = System.Text.Json.JsonSerializer.Deserialize<string>(json)?.Trim();
            if (named || closed || string.IsNullOrEmpty(heading) || heading.Length > 120) return;
            if (!string.Equals(heading, "Macmillan Learning", StringComparison.OrdinalIgnoreCase)) title.Text = heading;
        }
        catch (Exception e) { Log.Write("macmillan heading: " + e.Message); }
        Doubled();
    }

    /// <summary>The line under the title only when it says more than the title: with no due line, and the title the
    /// vendor's own name, no "Macmillan Learning" under "Macmillan Learning".</summary>
    private void Doubled() =>
        subtitle.Visibility = string.Equals(subtitle.Text.Trim(), title.Text.Trim(), StringComparison.OrdinalIgnoreCase) ? Visibility.Collapsed : Visibility.Visible;

    private void Draw()
    {
        if (closing) return;
        stateSlot.Child = failure is { } why ? AltaLook.Failure("Macmillan Learning could not be opened", why)
            : opening ? AltaLook.Opening("Opening Macmillan Learning…") : null;
        stateSlot.Visibility = stateSlot.Child == null ? Visibility.Collapsed : Visibility.Visible;
        web.Visibility = failure == null && !opening ? Visibility.Visible : Visibility.Hidden;
        Shot.Moved();
    }

    private bool holding;

    /// <summary>The picture held while the page opens, let go once it is in, it failed, or the popup closed.</summary>
    private void Hold(bool on)
    {
        if (on == holding) return;
        holding = on;
        if (on) Shot.Began(); else Shot.Ended();
    }

    /// <summary>Opened: a web view with the engine's web data, then the assignment's launch through Canvas (or the page asked for).</summary>
    private async void Start()
    {
        if (started) return;
        started = true;
        Hold(true); // (the screenshot suite waits for the page, not only for the app's own calls)
        try
        {
            var env = await Simpl.Engine.Engine.WebEnvironment();
            await web.EnsureCoreWebView2Async(env);
        }
        catch (Exception e)
        {
            Log.Write("macmillan: " + e.Message);
            Fail(e.Message);
            return;
        }
        if (closed) return;
        var core = web.CoreWebView2;
        core.Settings.AreDevToolsEnabled = true;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        // (Macmillan's page in light, as it is made, whatever the app's appearance: the popup's own header stays as the app is)
        await LightPages.Apply(core, "macmillan");
        if (closed) return;
        // (0.3.32) the skin, in every page and frame; its reports heard from each
        var skin = ScriptBundle.File("tools/macmillan/skin.js");
        if (skin.Length > 0) await core.AddScriptToExecuteOnDocumentCreatedAsync($"window.__simplMacmillanSkinned = {(browser ? "false" : "true")};\n" + skin);
        if (closed) return;
        core.WebMessageReceived += (_, e) => Heard(e, null);
        core.FrameCreated += (_, e) => WatchFrame(e.Frame);
        code = new PageCode(core, "Macmillan");
        core.NavigationCompleted += (_, e) =>
        {
            if (e.IsSuccess) NameFromPage(core);
            LightPages.Tell(core, "macmillan");
            if (!opening) return;
            opening = false;
            Hold(false);
            Draw();
        };
        core.NewWindowRequested += (_, e) => { e.Handled = true; engine.GoTo(e.Uri, ""); };
        core.DownloadStarting += (_, e) => { e.Handled = true; FilePreview.Downloading(e); };
        if (launch.Page is { } p && engine.Absolute(p) is { } page)
        {
            core.Navigate(page.ToString());
            return;
        }
        try
        {
            var a = await engine.Call<LaunchAnswer>("toolLaunch", new { course = launch.Course, assignment = launch.Assignment });
            if (!Uri.TryCreate(a.Url, UriKind.Absolute, out var u)) throw new EngineException("The answer could not be read.");
            if (!closed) core.Navigate(u.ToString());
        }
        catch (Exception e) { Fail(e.Message); }
    }

    private void WatchFrame(CoreWebView2Frame f)
    {
        try
        {
            f.WebMessageReceived += (_, e) => Heard(e, f);
            f.FrameCreated += (_, e) => WatchFrame(e.Frame);
            f.Destroyed += (_, _) => { if (ReferenceEquals(player, f)) player = null; };
        }
        catch { /* an older runtime: the frame is not heard, and the page is shown as it is */ }
    }

    /// <summary>A report of the skin's: the popup drawn round the assessment.</summary>
    private void Heard(CoreWebView2WebMessageReceivedEventArgs e, CoreWebView2Frame? frame)
    {
        string? text;
        try { text = e.TryGetWebMessageAsString(); } catch { return; }
        if (text == null || !text.Contains("\"macmillan\"") || !text.Contains("\"state\"")) return;
        MacmillanState? s;
        try { s = System.Text.Json.JsonSerializer.Deserialize<MacmillanState>(text, Json); } catch { return; }
        if (s == null || s.Kind != "state" || closing) return;
        if (frame != null) player = frame;
        var first = state == null;
        state = s;
        if (opening) { opening = false; Hold(false); Draw(); }
        if (browser) _ = Act("skin", false);
        if (first) Log.Write($"macmillan: the assessment's {s.Page}");
        // (the screenshot suite's --macmillan-open yes: the cover's Open assignment pressed, once)
        if (s.IsCover && !launchOpened && Launch.Flag("macmillan-open")) { launchOpened = true; _ = OpenSoon(); }
        Skin();
    }

    /// <summary>A press of the popup's, done on Macmillan's page.</summary>
    private bool launchOpened;

    private async Task OpenSoon()
    {
        await Task.Delay(600);
        await Act("open");
    }

    private async Task Act(string command, object? argument = null)
    {
        var arg = argument switch { int n => n.ToString(System.Globalization.CultureInfo.InvariantCulture), bool b => b ? "true" : "false", _ => "null" };
        var js = $"window.__simplMacmillan ? window.__simplMacmillan(\"{command}\", {arg}) : false";
        try
        {
            if (player != null && player.IsDestroyed() == 0) await player.ExecuteScriptAsync(js);
            else if (web.CoreWebView2 != null) await web.CoreWebView2.ExecuteScriptAsync(js);
        }
        catch (Exception e) { Log.Write("macmillan act: " + e.Message); }
    }

    /// <summary>Show browser on or off, kept for every Macmillan assignment.</summary>
    private void SetBrowser(bool on)
    {
        browser = on;
        Prefs.Set("macmillan:browser", on);
        _ = Act("skin", !on);
        try { _ = web.CoreWebView2?.ExecuteScriptAsync($"window.__simplMacmillan && window.__simplMacmillan('skin', {(on ? "false" : "true")})"); } catch { }
        Skin();
    }

    /// <summary>The rail, the bar and the start screen as the assessment now stands.</summary>
    private void Skin()
    {
        var s = state;
        railSlot.Content = s is { IsQuestion: true } ? MacmillanParts.Rail(s, browser, SetBrowser, n => _ = Act("go", n)) : null;
        barSlot.Content = s is { IsQuestion: true } && !browser ? MacmillanParts.Bar(s, (c, a) => _ = Act(c, a)) : null;
        coverSlot.Content = s is { IsCover: true, Cover: { } c } && !browser ? MacmillanParts.Start(c, () => _ = Act("open")) : null;
        coverSlot.Visibility = coverSlot.Content == null ? Visibility.Collapsed : Visibility.Visible;
        if (!named && (s?.Title ?? s?.Cover?.Name) is { Length: > 0 } t) title.Text = t;
        if (subtitle.Text == "Macmillan Learning" && s?.Cover?.Due is { Length: > 0 } due) subtitle.Text = $"Macmillan Learning · {due}";
        Doubled();
        Shot.Moved();
    }

    private void Fail(string why)
    {
        Hold(false);
        failure = "Macmillan Learning could not be opened. " + why;
        opening = false;
        Draw();
    }

    /// <summary>Close (the button, Escape): the page goes first — nothing of the app's can fade a web view out — then the popup.</summary>
    private void Close()
    {
        if (closing) return;
        closing = true;
        web.Visibility = Visibility.Collapsed;
        if (sheet != null) sheet.Close();
        else Cleanup();
    }

    private void Cleanup()
    {
        if (closed) return;
        closed = true;
        Hold(false);
        closing = true;
        root.PreviewKeyDown -= OnKey;
        DevMode.Changed -= OnDevMode;
        try { web.Dispose(); } catch { }
    }

    private void OnDevMode() => root.Dispatcher.BeginInvoke(() => { if (!closing) CodeButton(); });

    private void CodeButton()
    {
        if (!DevMode.On) { codeSlot.Content = null; return; }
        if (codeSlot.Content != null) return;
        var save = K.Button("Save Page Code", SaveCode, icon: SymbolRegular.Code24);
        save.ToolTip = "Every bit of Macmillan’s page code, in one text file in Downloads";
        codeSlot.Content = save;
    }

    /// <summary>The page's code saved (PageCode), then said where — or a beep when nothing could be read.</summary>
    private async void SaveCode()
    {
        if (saving || code == null || opening || failure != null) { System.Media.SystemSounds.Beep.Play(); return; }
        saving = true;
        var file = await code.Save();
        saving = false;
        if (closing) return;
        if (file == null) { System.Media.SystemSounds.Beep.Play(); return; }
        if (await Dialogs.Ask(Window.GetWindow(root) ?? over, "Page code saved", $"Every bit of Macmillan’s page code — the page, each frame in it, and the scripts and style sheets they load — is in Downloads as “{System.IO.Path.GetFileName(file)}”.", "Show in File Explorer", "OK"))
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true }); } catch { }
    }

    /// <summary>Escape closes, unless the page has the keyboard (its own Escape: a menu, a pop-up of Macmillan's).</summary>
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (closing || !root.IsVisible || e.Key != Key.Escape) return;
        if (ReferenceEquals(e.OriginalSource, web) || ReferenceEquals(Keyboard.FocusedElement, web)) return;
        Close();
        e.Handled = true;
    }
}
