using System.IO;
using System.Windows;
using Simpl.Engine;
using Simpl.Graphics;
using Simpl.Shell;
using Simpl.Support;
using Simpl.UI;
using Simpl.Models;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using FontWeight = Vortice.DirectWrite.FontWeight;
using TextAlignment = Vortice.DirectWrite.TextAlignment;

namespace Simpl.Screens;

/// <summary>What the page's settingsInfo says: the switches, the goal, and the history and record in the options page's own words.</summary>
public sealed class SettingsInfo
{
    public bool Tracking { get; set; }
    public double Goal { get; set; } = 4;
    public bool WhatIf { get; set; }
    public int Days { get; set; }
    public string History { get; set; } = "";
    public string HistoryNote { get; set; } = "";
    public string? Record { get; set; }
    public string RecordNote { get; set; } = "";
}

public sealed class SettingsMessage
{
    public string Message { get; set; } = "";
    public SettingsInfo Info { get; set; } = new();
}

public sealed class SettingsFile
{
    public string Name { get; set; } = "";
    public string Text { get; set; } = "";
}

/// <summary>
/// Settings (the Mac's own settings window, Simpl ▸ Settings, measured on its pictures): a tab for each part — General
/// (the school, the sign-in kept), Appearance, Notifications, Grades, Data, Updates and About — the Mac's grouped form
/// under a strip of tabs, 560 wide. Simpl's own settings (the grades') are read and written through the page's settings
/// calls, as on the Mac; the rest live in the app's own preferences.
/// </summary>
public sealed class SettingsView
{
    public static readonly (string Key, string Title, string Icon)[] Tabs =
    {
        ("general", "General", "gearshape"), ("appearance", "Appearance", "paintpalette"), ("notifications", "Notifications", "bell.badge"),
        ("grades", "Grades", "chart.bar.xaxis"), ("data", "Data", "externaldrive"), ("updates", "Updates", "arrow.down.circle"), ("about", "About", "info.circle"),
    };

    public const float TitleBar = 28, Strip = 60, Width = 560, Height = 548;

    private readonly Engine.Engine engine;
    private readonly GlassHost host;
    private readonly Window window;
    public int Tab;

    // the grades' settings, as the page says them
    private SettingsInfo? info;
    private string? infoError;
    private bool infoAsked, busy;
    private double goal = 4;
    private double goalSavedAt = -1;
    private bool goalDirty;
    // the word at the foot after something was done
    private string? said;
    private bool saidError;
    private double saidAt;
    // Appearance: the place whose photo is edited; the picker's state
    private string slot = "side";
    private bool versionsOpen;
    private string? versionsProblem;
    private bool versionsAsked;

    public SettingsView(Engine.Engine engine, GlassHost host, Window window, string? tab)
    {
        this.engine = engine;
        this.host = host;
        this.window = window;
        Tab = Math.Max(0, Array.FindIndex(Tabs, t => t.Key == (tab ?? "general")));
        if (Launch.Get("photo-slot") is { } s && Appearance.Slots.Any(x => x.Key == s)) slot = s;
    }

    public string Title => Tabs[Tab].Title;

    public Color4 Ground => host.Ui.T.Dark ? Theme.Rgb(30, 30, 32) : Theme.Rgb(252, 252, 252);

    private Color4 Box => host.Ui.T.Dark ? Theme.Rgb(44, 44, 46) : Theme.Rgb(245, 245, 247);

    public void Frame(Ui ui)
    {
        ui.T.Dark = AppView.Dark(engine.Snapshot?.Dark);
        var t = ui.T;
        ui.Layer(Ui.Page);
        ui.Fill(new R(0, 0, ui.W, ui.H), Ground);

        // the content, scrolling under the strip
        var top = TitleBar + Strip;
        var vp = new R(0, top, ui.W, ui.H - top);
        var off = ui.BeginScroll("settings:" + Tabs[Tab].Key, vp);
        var y = top + 24 - off;
        var x = 20f;
        var w = ui.W - 40;
        var end = Tabs[Tab].Key switch
        {
            "general" => General(ui, x, y, w),
            "appearance" => AppearancePane(ui, x, y, w),
            "notifications" => Notifications(ui, x, y, w),
            "grades" => Grades(ui, x, y, w),
            "data" => Data(ui, x, y, w),
            "updates" => UpdatesPane(ui, x, y, w),
            _ => About(ui, x, y, w),
        };
        ui.EndScroll("settings:" + Tabs[Tab].Key, vp, end - y + 24);

        // the title bar and the strip of tabs, over it
        ui.Layer(Ui.Chrome);
        ui.Fill(new R(0, 0, ui.W, top), Ground);
        ui.Fill(new R(0, top - 1, ui.W, 1), t.Separator);
        ui.Text(Title, 0, 7, Theme.Bold(Theme.Subheadline), t.Ink(), ui.W, TextAlignment.Center);
        TabStrip(ui);
        SaidBar(ui);
        Alerts.Draw(ui, host);
    }

    /// <summary>The Mac's toolbar of tabs: a symbol over its name, the one chosen on a soft well, in the accent.</summary>
    private void TabStrip(Ui ui)
    {
        var t = ui.T;
        var widths = Tabs.Select(tab => Math.Max(66, ui.Measure(tab.Title, Theme.Caption2).X + 20)).ToArray();
        var total = widths.Sum();
        var x = (ui.W - total) / 2;
        for (var k = 0; k < Tabs.Length; k++)
        {
            var r = new R(x, TitleBar + 4, widths[k], 48);
            var i = ui.Press("tab:" + Tabs[k].Key, r);
            var on = k == Tab;
            var hover = ui.Animate("tab:h:" + Tabs[k].Key, !on && i.Hovered ? 1 : 0, 0.16f);
            if (on)
            {
                ui.Fill(r, 8, t.Primary(t.Dark ? 0.08f : 0.05f));
                ui.Stroke(r, 8, t.Edge);
            }
            else if (hover > 0.01f) ui.Fill(r, 8, t.Primary(0.04f * hover));
            var ink = on ? t.Accent : t.Ink2;
            ui.Icon(Tabs[k].Icon, r.CenterX, r.Y + 17, 21, ink);
            if (Tabs[k].Icon == "bell.badge") ui.Circle(r.CenterX + 7, r.Y + 9, 3.2f, ink); // (the bell's badge)
            ui.Text(Tabs[k].Title, r.X, r.Y + 32, Theme.Caption2, ink, r.W, TextAlignment.Center);
            if (i.Clicked && !on)
            {
                Tab = k;
                window.Title = "Simpl — " + Title;
            }
            x += widths[k];
        }
    }

    // ---- the form's pieces (the Mac's grouped style) ----

    private const float Inset = 10, RowH = 36, Row2H = 50;

    private float Header(Ui ui, float x, float y, string title)
    {
        ui.Text(title, x + Inset, y, Theme.Semibold(Theme.Headline), ui.T.Ink());
        return y + 20 + 8;
    }

    private float Footer(Ui ui, float x, float y, float w, string text)
    {
        var h = ui.Text(text, x + Inset, y + 8, Theme.Subheadline, ui.T.Ink2, w - 2 * Inset, TextAlignment.Leading, 0);
        return y + 8 + h;
    }

    /// <summary>The box the rows sit in: drawn once their height is known (the rows are laid out first, into a list).</summary>
    private void BoxBehind(Ui ui, R r)
    {
        var was = ui.CurrentLayer;
        ui.Layer(Ui.Page - 1);
        ui.Fill(r, 10, Box);
        ui.Stroke(r, 10, ui.T.Edge);
        ui.Layer(was);
    }

    private void Separator(Ui ui, float x, float y, float w) => ui.Fill(new R(x + Inset, y, w - 2 * Inset, 1), ui.T.Separator);

    /// <summary>A row's label (and its second line), and its value at the right.</summary>
    private void Label(Ui ui, float x, float cy, string label, string? sub = null, float maxW = 100000)
    {
        var t = ui.T;
        if (sub == null) ui.Text(label, x + Inset, cy - 10, Theme.Body, t.Ink(), maxW);
        else
        {
            ui.Text(label, x + Inset, cy - 19, Theme.Body, t.Ink(), maxW);
            ui.Text(sub, x + Inset, cy + 1, Theme.Callout, t.Ink2, maxW);
        }
    }

    private void Value(Ui ui, float right, float cy, string value)
    {
        var m = ui.Measure(value, Theme.Body);
        ui.Text(value, right - Inset - m.X, cy - m.Y / 2, Theme.Body, ui.T.Ink2);
    }

    // ---- General ----

    private float General(Ui ui, float x, float y, float w)
    {
        var t = ui.T;
        y = Header(ui, x, y, "School");
        var box = new R(x, y, w, RowH + 44);
        BoxBehind(ui, box);
        Label(ui, x, y + RowH / 2, engine.LmsName);
        Value(ui, x + w, y + RowH / 2, engine.Host);
        Separator(ui, x, y + RowH, w);
        var cy = y + RowH + 22;
        if (ui.FormButton("set:change", x + Inset, cy, "Change School…")) ChangeSchool?.Invoke();
        if (ui.FormButton("set:signout", x + w - Inset - ui.FormButtonWidth("Sign Out…"), cy, "Sign Out…"))
            Alerts.Confirm($"Sign out of {engine.LmsName} on this PC?", $"Your {engine.LmsName} session and any saved sign-in are cleared; settings stay.", "Sign Out", () => { engine.SignOut(); window.Close(); }, owner: host);
        y = box.Bottom + 24;

        y = Header(ui, x, y, "Sign-In");
        var saved = LoginVault.Load();
        if (saved != null)
        {
            box = new R(x, y, w, RowH * 2 + 44);
            BoxBehind(ui, box);
            Label(ui, x, y + RowH / 2, "Saved for");
            Value(ui, x + w, y + RowH / 2, saved.User);
            Separator(ui, x, y + RowH, w);
            Label(ui, x, y + RowH * 1.5f, "Sign-in page");
            Value(ui, x + w, y + RowH * 1.5f, saved.Host);
            Separator(ui, x, y + RowH * 2, w);
            if (ui.FormButton("set:forget", x + Inset, y + RowH * 2 + 22, "Forget Saved Sign-In…"))
                Alerts.Confirm("Forget the saved sign-in?", "Simpl will ask for your username and password the next time your school does.", "Forget", () => { LoginVault.Clear(); host.Invalidate(); }, owner: host);
        }
        else
        {
            var note = "Not saved. When your school’s sign-in page comes up, sign in with Simpl’s card and answer Stay Logged In.";
            var h = ui.Measure(note, Theme.Body, w - 2 * Inset, 0).Y;
            box = new R(x, y, w, h + 20);
            BoxBehind(ui, box);
            ui.Text(note, x + Inset, y + 10, Theme.Body, t.Ink2, w - 2 * Inset, TextAlignment.Leading, 0);
        }
        y = Footer(ui, x, box.Bottom, w, "Kept in Windows’ Credential Manager on this PC only: never synced, and only ever typed into your school’s own sign-in page. Signing out forgets it too.");
        return y;
    }

    /// <summary>Change School…: the window that opens Settings does it (the school forgotten, the picker again).</summary>
    public Action? ChangeSchool;

    // ---- Appearance ----

    private float AppearancePane(Ui ui, float x, float y, float w)
    {
        var t = ui.T;
        // light, dark, or as Windows is
        var box = new R(x, y, w, 44);
        BoxBehind(ui, box);
        Label(ui, x, y + 22, "Appearance");
        var looks = new[] { "Light", "Dark", "System" };
        var look = Appearance.Look switch { "light" => 0, "dark" => 1, _ => 2 };
        if (ui.Segmented("set:look", new R(x + w - Inset - 264, y + 9, 264, 26), looks, ref look, false))
        {
            Appearance.Look = look switch { 0 => "light", 1 => "dark", _ => "system" };
        }
        // (the segmented control's own well, as the Mac's picker in a form)
        y = box.Bottom + 24;

        y = Header(ui, x, y, "Theme");
        const float gap = 14;
        var tileW = (w - 2 * Inset - 2 * gap) / 3;
        var ready = Appearance.ReadyThemes.Take(6).ToArray();
        var rows = (ready.Length + 2) / 3;
        box = new R(x, y, w, Inset + rows * (80 + 6 + 20) + (rows - 1) * gap + Inset);
        BoxBehind(ui, box);
        for (var k = 0; k < ready.Length; k++)
        {
            var r = ready[k];
            var tx = x + Inset + (k % 3) * (tileW + gap);
            var ty = y + Inset + (k / 3) * (80 + 26 + gap);
            var on = Appearance.ThemeName == r.Name;
            var swatch = new R(tx, ty, tileW, 80);
            var i = ui.Press("set:theme:" + r.Name, new R(tx, ty, tileW, 106));
            ThemeSwatch(ui, swatch, r);
            ui.Stroke(swatch, 12, on ? t.Accent : t.Primary(0.12f), on ? 2.5f : 1);
            var dot = r.Accent is { } hex ? Theme.Hex(hex) : (Theme.CustomAccent == null ? t.Accent : (t.Dark ? Theme.Rgb(10, 132, 255) : Theme.Rgb(0, 122, 255)));
            ui.Circle(tx + 6, ty + 80 + 16, 4.5f, dot);
            ui.Text(r.Name, tx + 16, ty + 80 + 7, on ? Theme.Semibold(Theme.Callout) : Theme.Callout, t.Ink());
            if (on) ui.Icon("checkmark", tx + 16 + ui.Measure(r.Name, Theme.Semibold(Theme.Callout)).X + 10, ty + 80 + 16, 10, t.Accent, FontWeight.Bold);
            if (i.Clicked) Appearance.Wear(r);
        }
        y = box.Bottom + 24;

        y = Header(ui, x, y, "Colour");
        box = new R(x, y, w, 50);
        BoxBehind(ui, box);
        var cx = x + Inset + 15;
        var cy = y + 25;
        var accent = Appearance.AccentHex;
        if (ColourDot(ui, "set:colour:regular", cx, cy, t.Dark ? Theme.Rgb(10, 132, 255) : Theme.Rgb(0, 122, 255), accent == null, "Regular")) Appearance.SetAccent(null);
        foreach (var c in Appearance.Colours)
        {
            cx += 38;
            if (ColourDot(ui, "set:colour:" + c.Name, cx, cy, Theme.Hex(c.Hex), accent == c.Hex, c.Name)) Appearance.SetAccent(c.Hex);
        }
        y = Footer(ui, x, box.Bottom, w, "The sidebar, the cards’ glyphs, the titles and every control take it, each shade stepped until it reads.");
        y += 24;

        y = Header(ui, x, y, "Photos");
        var slotsH = 10 + 162 + 10;
        var editorH = 10 + 150 + 10;
        box = new R(x, y, w, slotsH + 1 + editorH);
        BoxBehind(ui, box);
        // the places: the sidebar tall at the left, the six counters in a grid
        var sy = y + 10;
        PhotoThumb(ui, "side", new R(x + Inset, sy, 66, 150), "Sidebar");
        for (var k = 1; k < Appearance.Slots.Length; k++)
        {
            var col = (k - 1) % 3;
            var row = (k - 1) / 3;
            PhotoThumb(ui, Appearance.Slots[k].Key, new R(x + Inset + 66 + 14 + col * 92, sy + row * 86, 78, 50), Appearance.Slots[k].Name);
        }
        Separator(ui, x, y + slotsH, w);
        // the editor for the place chosen
        var ey = y + slotsH + 1 + 10;
        var preview = new R(x + Inset, ey, 220, 150);
        PhotoPreview(ui, slot, preview, 12);
        var ex = preview.Right + 18;
        var slotName = Appearance.Slots.First(s => s.Key == slot).Name;
        ui.Text(slot == "side" ? "Sidebar" : slotName + " card", ex, ey + 2, Theme.Title3, t.Ink());
        if (ui.FormButton("set:photo:upload", ex, ey + 44, "Upload Photo…")) UploadPhoto();
        var kind = Appearance.PhotoKind(slot);
        var kindLabel = kind == "photo" ? "Photo" : "Drawing";
        if (ui.PopupButton("set:photo:kind", ex + ui.Measure(kindLabel, Theme.Body).X + 46, ey + 78, kindLabel)) ui.OpenMenu("set:photo:kindmenu");
        var pick = ui.Menu("set:photo:kindmenu", new R(ex, ey + 65, 120, 26), new string?[] { "Drawing", "Photo" });
        if (pick == 0) Appearance.SetKind(slot, "drawing");
        if (pick == 1) Appearance.SetKind(slot, "photo");
        ui.Icon("magnifyingglass.minus", ex + 8, ey + 108, 14, t.Ink2);
        var zoom = Appearance.Zoom(slot);
        if (ui.Slider("set:photo:zoom", ex + 24, ey + 108, 176, ref zoom, 1, 3)) Appearance.SetZoom(slot, zoom);
        ui.Icon("magnifyingglass.plus", ex + 24 + 176 + 16, ey + 108, 14, t.Ink2);
        if (ui.FormButton("set:photo:reset", ex, ey + 137, "Reset", enabled: Math.Abs(zoom - 1) > 0.001f)) Appearance.SetZoom(slot, 1);
        if (ui.FormButton("set:photo:remove", ex + ui.FormButtonWidth("Reset") + 8, ey + 137, "Remove", enabled: Appearance.Photo(slot) != null)) Appearance.RemovePhoto(slot);
        return box.Bottom + 8;
    }

    /// <summary>A theme's swatch (the Mac's ThemeSwatch): its colour over the page, a sidebar and two cards sketched in it.</summary>
    private void ThemeSwatch(Ui ui, R r, Appearance.Ready theme)
    {
        var t = ui.T;
        var tint = theme.Accent is { } hex ? Theme.Hex(hex) : t.Primary(0.4f);
        ui.Fill(r, 12, theme.Accent == null ? Box : Theme.WithAlpha(tint, t.Dark ? 0.22f : 0.18f));
        var sideW = r.W * 0.2f;
        var inner = Theme.WithAlpha(theme.Accent == null ? t.Card : Theme.Mix(t.Card, tint, 0.08f), 0.9f);
        ui.Fill(new R(r.X + 6, r.Y + 6, sideW, r.H - 12), 6, inner);
        ui.Fill(new R(r.X + 6 + sideW + 5, r.Y + 6, r.W - sideW - 17, (r.H - 17) / 2), 6, inner);
        ui.Fill(new R(r.X + 6 + sideW + 5, r.Y + 6 + (r.H - 17) / 2 + 5, r.W - sideW - 17, (r.H - 17) / 2), 6, inner);
        if (theme.Accent != null)
        {
            // (a hint of the scene: a horizon and a sun, in the colour)
            var line = Theme.WithAlpha(tint, 0.35f);
            ui.Fill(new R(r.X + 6 + sideW + 14, r.Y + r.H * 0.36f, r.W - sideW - 34, 1.5f), line);
            ui.Fill(new R(r.X + 6 + sideW + 14, r.Y + r.H * 0.8f, r.W - sideW - 34, 1.5f), line);
            ui.Ring(r.Right - 30, r.Y + r.H * 0.3f, 7, line, 1.5f);
        }
    }

    private bool ColourDot(Ui ui, string id, float cx, float cy, Color4 colour, bool on, string name)
    {
        var i = ui.Press(id, new R(cx - 15, cy - 15, 30, 30));
        ui.Circle(cx, cy, 11, colour);
        if (on) ui.Ring(cx, cy, 14, ui.T.Primary(0.85f), 2);
        return i.Clicked;
    }

    private void PhotoThumb(Ui ui, string key, R r, string name)
    {
        var t = ui.T;
        var on = slot == key;
        var i = ui.Press("set:slot:" + key, new R(r.X, r.Y, r.W, r.H + 22));
        PhotoPreview(ui, key, r, 9);
        ui.Stroke(r, 9, on ? t.Accent : t.Primary(0.12f), on ? 2.5f : 1);
        ui.Text(name, r.X - 10, r.Bottom + 5, Theme.Caption, on ? t.Ink() : t.Ink2, r.W + 20, TextAlignment.Center);
        if (i.Clicked) slot = key;
    }

    /// <summary>A place's picture as it is now: the photo of your own at its zoom, else the theme's tint (its drawing comes later).</summary>
    private void PhotoPreview(Ui ui, string key, R r, float radius)
    {
        var t = ui.T;
        var theme = Appearance.ReadyThemes.FirstOrDefault(x => x.Name == Appearance.ThemeName);
        var tint = theme.Accent is { } hex ? Theme.Hex(hex) : t.Accent;
        ui.Fill(r, radius, Theme.WithAlpha(tint, t.Dark ? 0.18f : 0.12f));
        if (Appearance.PhotoKind(key) == "photo" && Appearance.Photo(key) is { } file)
        {
            var z = Appearance.Zoom(key);
            ui.PushClip(r);
            // (the picture fills its place, zoomed from its middle)
            var pw = r.W * z;
            var ph = r.H * z;
            ui.Image(file, new R(r.CenterX - pw / 2, r.CenterY - ph / 2, pw, ph));
            ui.PopClip();
        }
    }

    private void UploadPhoto()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Upload Photo", Filter = "Pictures|*.jpg;*.jpeg;*.png;*.heic;*.webp;*.bmp|All files|*.*" };
        if (dlg.ShowDialog(window) == true)
        {
            if (Appearance.SetPhoto(slot, dlg.FileName)) Say("Photo set."); else Say("The picture could not be read.", true);
        }
    }

    // ---- Notifications ----

    private static readonly string[] Leads = { "1 hour before", "3 hours before", "1 day before", "1 day and 1 hour before", "2 days before" };
    private static readonly (string Key, string Label)[] ActivityKinds =
    {
        ("announcements", "Announcements"), ("grades", "Grades"), ("comments", "Comments on Your Work"), ("inbox", "Inbox Messages"), ("discussions", "Discussions"),
    };

    private float Notifications(Ui ui, float x, float y, float w)
    {
        var t = ui.T;
        y = Header(ui, x, y, "Reminders");
        var on = Prefs.Bool("reminders:on");
        var box = new R(x, y, w, Row2H + (on ? RowH : 0));
        BoxBehind(ui, box);
        Label(ui, x, y + Row2H / 2, "Due-Date Reminders", "Alerts for work still to hand in over the next three weeks.", w - 80);
        if (ui.Toggle("set:rem:on", x + w - Inset - 36, y + Row2H / 2, ref on)) Prefs.Set("reminders:on", on);
        if (on)
        {
            Separator(ui, x, y + Row2H, w);
            var cy = y + Row2H + RowH / 2;
            Label(ui, x, cy, "Remind Me");
            var lead = Prefs.String("reminders:lead") ?? Leads[3];
            if (ui.PopupButton("set:rem:lead", x + w - Inset, cy, lead)) ui.OpenMenu("set:rem:leadmenu");
            var pick = ui.Menu("set:rem:leadmenu", new R(x + w - Inset - 220, cy + 13, 220, 1), Leads, alignRight: true);
            if (pick >= 0) Prefs.Set("reminders:lead", Leads[pick]);
        }
        y = Footer(ui, x, box.Bottom, w, "Set on this PC: they come on time even with Simpl closed, and nothing is sent anywhere. New work is picked up each time you open Simpl.");
        y += 24;

        y = Header(ui, x, y, "New Activity");
        var act = Prefs.Bool("activity:on");
        box = new R(x, y, w, Row2H + (act ? ActivityKinds.Length * RowH : 0));
        BoxBehind(ui, box);
        Label(ui, x, y + Row2H / 2, "New Activity", "Alerts when something is posted in your courses.", w - 80);
        if (ui.Toggle("set:act:on", x + w - Inset - 36, y + Row2H / 2, ref act)) Prefs.Set("activity:on", act);
        if (act)
        {
            var kinds = (Prefs.String("activity:kinds") ?? "announcements,grades,comments,inbox").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            var ry = y + Row2H;
            foreach (var (key, label) in ActivityKinds)
            {
                Separator(ui, x, ry, w);
                var has = kinds.Contains(key);
                if (ui.Checkbox("set:act:" + key, x + Inset, ry + RowH / 2, ref has, label))
                {
                    if (has) kinds.Add(key); else kinds.Remove(key);
                    Prefs.Set("activity:kinds", string.Join(",", ActivityKinds.Select(k => k.Key).Where(kinds.Contains)));
                }
                ry += RowH;
            }
        }
        var foot = "Simpl looks about every 3 minutes while it is open — with its window closed too — reading with the sign-in on this PC: nothing is sent anywhere.";
        if (engine.OnBrightspace) foot += " Brightspace ends a sign-in left unused after a while (your school sets how long): the alerts pause then, until you next open Simpl.";
        return Footer(ui, x, box.Bottom, w, foot);
    }

    // ---- Grades ----

    private float Grades(Ui ui, float x, float y, float w)
    {
        var t = ui.T;
        if (engine.Phase != Phase.Native)
        {
            var box0 = new R(x, y, w, 44);
            BoxBehind(ui, box0);
            ui.Text($"Grade settings appear once {engine.LmsName} has loaded and you are signed in.", x + Inset, y + 12, Theme.Body, t.Ink2, w - 2 * Inset);
            return box0.Bottom;
        }
        if (!infoAsked) { infoAsked = true; _ = LoadInfo(); }
        if (info == null)
        {
            var box0 = new R(x, y, w, 44);
            BoxBehind(ui, box0);
            if (infoError != null)
            {
                ui.Text(infoError, x + Inset, y + 12, Theme.Body, t.Ink2, w - 2 * Inset - 100);
                if (ui.FormButton("set:grades:again", x + w - Inset - ui.FormButtonWidth("Try Again"), y + 22, "Try Again")) _ = LoadInfo();
            }
            else
            {
                ui.Text("Reading your settings…", x + Inset, y + 12, Theme.Body, t.Ink2);
                ui.Spinner(x + w - Inset - 10, y + 22, 7);
            }
            return box0.Bottom;
        }
        var i = info;
        y = Header(ui, x, y, "Grades");
        var box = new R(x, y, w, RowH * 3);
        BoxBehind(ui, box);
        var tracking = i.Tracking;
        Label(ui, x, y + RowH / 2, "Track grades over time");
        if (ui.Toggle("set:g:track", x + w - Inset - 36, y + RowH / 2, ref tracking)) { i.Tracking = tracking; Save(new { tracking }); }
        Separator(ui, x, y + RowH, w);
        var cy = y + RowH * 1.5f;
        Label(ui, x, cy, "Term GPA goal");
        ui.Text(goal.ToString("0.00"), x + Inset + ui.Measure("Term GPA goal", Theme.Body).X + 12, cy - 10, Theme.Body, t.Ink());
        var step = ui.Stepper("set:g:goal", x + w - Inset - 20, cy);
        if (step != 0)
        {
            goal = Math.Clamp(Math.Round((goal + step * 0.05) * 100) / 100, 0, 4);
            goalDirty = true;
            goalSavedAt = ui.Time;
        }
        if (goalDirty && ui.Time - goalSavedAt > 0.35) { goalDirty = false; _ = SaveGoal(); } // (saved once the stepper rests, as the options page does)
        if (goalDirty) ui.Animating = true;
        Separator(ui, x, y + RowH * 2, w);
        var whatIf = i.WhatIf;
        Label(ui, x, y + RowH * 2.5f, "Show what-if scores");
        if (ui.Toggle("set:g:whatif", x + w - Inset - 36, y + RowH * 2.5f, ref whatIf)) { i.WhatIf = whatIf; Save(new { whatIf }); }
        y = Footer(ui, x, box.Bottom, w, "One snapshot a day, kept on this PC.");
        y += 24;

        y = Header(ui, x, y, "Grade History");
        box = new R(x, y, w, Row2H);
        BoxBehind(ui, box);
        Label(ui, x, y + Row2H / 2, i.History, i.HistoryNote.Length > 0 ? i.HistoryNote : null, w - 200);
        var bx = x + w - Inset - ui.FormButtonWidth("Export…");
        if (ui.FormButton("set:g:export", bx, y + Row2H / 2, "Export…", enabled: !busy && i.Days > 0)) _ = Export("historyExport", "csv", "CSV|*.csv");
        bx -= 8 + ui.FormButtonWidth("Import…");
        if (ui.FormButton("set:g:import", bx, y + Row2H / 2, "Import…", enabled: !busy)) Import("historyImport", "CSV|*.csv;*.txt|All files|*.*");
        y = Footer(ui, x, box.Bottom, w, "A CSV exported before (a date and a GPA a line) adds the days it holds; a day already here is kept as it is.");
        y += 24;

        y = Header(ui, x, y, "Record Before This Term");
        box = new R(x, y, w, RowH + 8);
        BoxBehind(ui, box);
        Label(ui, x, y + (RowH + 8) / 2, i.Record ?? "No record before this term", null, w - 220);
        bx = x + w - Inset;
        if (i.Record != null)
        {
            bx -= ui.FormButtonWidth("Clear");
            if (ui.FormButton("set:g:clear", bx, y + (RowH + 8) / 2, "Clear", enabled: !busy, destructive: true)) _ = ClearRecord();
            bx -= 8;
        }
        var up = i.Record == null ? "Upload CSV…" : "Replace…";
        bx -= ui.FormButtonWidth(up);
        if (ui.FormButton("set:g:record", bx, y + (RowH + 8) / 2, up, enabled: !busy)) Import("recordImport", "CSV|*.csv;*.txt|All files|*.*");
        return Footer(ui, x, box.Bottom, w, i.RecordNote);
    }

    private async Task LoadInfo()
    {
        try
        {
            info = await engine.Call<SettingsInfo>("settingsInfo");
            goal = info.Goal;
            infoError = null;
        }
        catch (Exception e) { infoError = e.Message; }
        host.Invalidate();
    }

    private async void Save(object change)
    {
        try { info = await engine.Call<SettingsInfo>("settingsSave", change); }
        catch (Exception e) { Say("Could not save: " + e.Message, true); await LoadInfo(); }
        host.Invalidate();
    }

    private async Task SaveGoal()
    {
        try { await engine.Call<SettingsInfo>("settingsSave", new { goal }); }
        catch (Exception e) { Say("Could not save the goal: " + e.Message, true); }
    }

    private async Task ClearRecord()
    {
        busy = true;
        try { info = await engine.Call<SettingsInfo>("recordClear"); Say("Record cleared; tracking stays on."); }
        catch (Exception e) { Say("Could not clear it: " + e.Message, true); }
        busy = false;
        host.Invalidate();
    }

    /// <summary>A file the page writes (the GPA history, the settings), saved where the student says.</summary>
    private async Task Export(string call, string ext, string filter)
    {
        busy = true;
        try
        {
            var f = await engine.Call<SettingsFile>(call);
            var dlg = new Microsoft.Win32.SaveFileDialog { FileName = f.Name, DefaultExt = ext, Filter = filter };
            if (dlg.ShowDialog(window) == true)
            {
                await File.WriteAllTextAsync(dlg.FileName, f.Text);
                Say($"Saved {Path.GetFileName(dlg.FileName)}.");
            }
        }
        catch (Exception e) { Say("Could not export: " + e.Message, true); }
        busy = false;
        host.Invalidate();
    }

    private async void Import(string call, string filter)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = filter };
        if (dlg.ShowDialog(window) != true) return;
        string text;
        try
        {
            var fi = new FileInfo(dlg.FileName);
            if (fi.Length > 5 * 1024 * 1024) throw new Exception("too large");
            text = await File.ReadAllTextAsync(dlg.FileName);
        }
        catch { Say($"{Path.GetFileName(dlg.FileName)} could not be read.", true); return; }
        busy = true;
        try
        {
            if (call == "settingsImport")
            {
                await engine.Call<OK>("settingsImport", new { text });
                await LoadInfo();
                Say("Imported.");
            }
            else
            {
                var r = await engine.Call<SettingsMessage>(call, new { text, name = Path.GetFileName(dlg.FileName) });
                info = r.Info;
                goal = r.Info.Goal;
                Say(r.Message);
            }
        }
        catch (Exception e) { Say(e.Message, true); }
        busy = false;
        host.Invalidate();
    }

    // ---- Data ----

    private float Data(Ui ui, float x, float y, float w)
    {
        var t = ui.T;
        y = Header(ui, x, y, "Data");
        var box = new R(x, y, w, 44);
        BoxBehind(ui, box);
        if (engine.Phase == Phase.Native)
        {
            Label(ui, x, y + 22, "Settings");
            var bx = x + w - Inset - ui.FormButtonWidth("Export…");
            if (ui.FormButton("set:d:export", bx, y + 22, "Export…", enabled: !busy)) _ = Export("settingsExport", "json", "JSON|*.json");
            bx -= 8 + ui.FormButtonWidth("Import…");
            if (ui.FormButton("set:d:import", bx, y + 22, "Import…", enabled: !busy)) Import("settingsImport", "JSON|*.json;*.txt|All files|*.*");
            y = Footer(ui, x, box.Bottom, w, engine.OnBrightspace
                ? "Brightspace data is never kept here: only your preferences, grade history and goals, and the course nicknames, colours, tasks and ticks Brightspace has no place for. Your Brightspace account and favourites live on Brightspace."
                : "Canvas data is never kept here: only your preferences, grade history and goals. Your Canvas account, favourites and course nicknames live on Canvas.");
        }
        else
        {
            ui.Text($"Data settings appear once {engine.LmsName} has loaded and you are signed in.", x + Inset, y + 12, Theme.Body, t.Ink2, w - 2 * Inset);
            y = box.Bottom;
        }
        y += 24;
        // (always here, signed in or not: the way out of anything)
        box = new R(x, y, w, Row2H + 6);
        BoxBehind(ui, box);
        Label(ui, x, y + (Row2H + 6) / 2, "Reset", "Erases everything Simpl keeps on this PC, signs you out and restarts Simpl.", w - 170);
        if (ui.FormButton("set:d:reset", x + w - Inset - ui.FormButtonWidth("Reset Everything…"), y + (Row2H + 6) / 2, "Reset Everything…", enabled: !busy))
            Alerts.Confirm("Erase everything and restart Simpl?", $"Your settings, grade history, goals, saved pages, reminders and {engine.LmsName} sign-in are erased from this PC. Simpl then restarts as if it were new. This can’t be undone.", "Erase and Restart", () => _ = EraseAll.Run(engine), owner: host);
        return box.Bottom;
    }

    // ---- Updates ----

    private float UpdatesPane(Ui ui, float x, float y, float w)
    {
        var t = ui.T;
        y = Header(ui, x, y, "Simpl for Windows");
        var dev = Updates.IsDevelopmentRun;
        var autoSubH = 2 * 18f;
        var betaSubH = 3 * 18f;
        var rowAuto = 22 + autoSubH + 10;
        var rowBeta = 22 + betaSubH + 10;
        var box = new R(x, y, w, RowH + rowAuto + rowBeta + 44);
        BoxBehind(ui, box);
        Label(ui, x, y + RowH / 2, "Version");
        Value(ui, x + w, y + RowH / 2, Updates.Version);
        Separator(ui, x, y + RowH, w);
        var ry = y + RowH;
        var auto = Updates.Automatic;
        ui.Text("Install updates automatically", x + Inset, ry + 8, Theme.Body, t.Ink());
        ui.Text("A new version goes in when it arrives, and Simpl opens again on it — never while a quiz or a tool is open.", x + Inset, ry + 30, Theme.Callout, t.Ink2, w - 2 * Inset - 56, TextAlignment.Leading, 0);
        if (ui.Toggle("set:u:auto", x + w - Inset - 36, ry + 18, ref auto, !dev)) Updates.Automatic = auto;
        ry += rowAuto;
        Separator(ui, x, ry, w);
        var beta = Updates.Beta;
        ui.Text("Get beta updates", x + Inset, ry + 8, Theme.Body, t.Ink());
        ui.Text("Try new features before everyone else. A beta may have rough edges; turn this off to go back to the usual releases when the next one comes out.", x + Inset, ry + 30, Theme.Callout, t.Ink2, w - 2 * Inset - 56, TextAlignment.Leading, 0);
        if (ui.Toggle("set:u:beta", x + w - Inset - 36, ry + 18, ref beta, !dev)) Updates.Beta = beta;
        ry += rowBeta;
        Separator(ui, x, ry, w);
        var cy = ry + 22;
        var status = Updates.State switch
        {
            Updates.Mode.Checking => "Checking…",
            Updates.Mode.UpToDate => "Up to date — checked " + Ago(Updates.LastCheck),
            Updates.Mode.Available => $"Version {Updates.Found?.Version} available",
            Updates.Mode.Failed => Updates.Message ?? "Could not check.",
            _ => dev ? "Updates are off in a development run." : Updates.LastCheck is { } last ? "Last checked " + Ago(last) : "Not checked yet",
        };
        if (Updates.State == Updates.Mode.Checking) { ui.Spinner(x + Inset + 8, cy, 6); ui.Text(status, x + Inset + 22, cy - 10, Theme.Body, t.Ink()); }
        else if (Updates.State == Updates.Mode.Failed) { ui.Icon("exclamationmark.triangle.fill", x + Inset + 8, cy, 14, t.Orange); ui.Text(status, x + Inset + 24, cy - 10, Theme.Body, t.Ink(), w - 2 * Inset - 140); }
        else ui.Text(status, x + Inset, cy - 10, Updates.State == Updates.Mode.Available ? Theme.Semibold(Theme.Body) : Theme.Body, dev ? t.Ink2 : t.Ink(), w - 2 * Inset - 130);
        if (Updates.State == Updates.Mode.Available && Updates.Found is { } found)
        {
            if (ui.FormButton("set:u:get", x + w - Inset - ui.FormButtonWidth("Update Now"), cy, "Update Now", prominent: true)) Updates.Open(found);
        }
        else if (Updates.State != Updates.Mode.Checking)
        {
            if (ui.FormButton("set:u:check", x + w - Inset - ui.FormButtonWidth("Check Now"), cy, "Check Now", enabled: !dev)) _ = Updates.Check();
        }
        y = Footer(ui, x, box.Bottom, w, dev
            ? "This is a development run of Simpl (the mock Canvas or the screenshot suite): it never looks for updates or replaces itself."
            : "Simpl looks for a new version every hour. For now a new version is a download from its own page: unzipped over this folder, it takes this copy’s place.");
        y += 24;

        // the versions published before, folded away until asked for
        box = new R(x, y, w, RowH);
        var rows = versionsOpen ? (Updates.Known ?? new List<Updates.Release>()) : new List<Updates.Release>();
        var listH = versionsOpen ? (Updates.Known == null ? RowH : Math.Max(RowH, rows.Count * Row2H)) : 0;
        box = new R(x, y, w, RowH + listH);
        BoxBehind(ui, box);
        var open = versionsOpen;
        if (ui.Disclosure("set:u:versions", x + Inset, y + RowH / 2, "Earlier Versions", ref open))
        {
            versionsOpen = open;
            if (open && !versionsAsked) { versionsAsked = true; _ = LoadVersions(); }
        }
        if (versionsOpen)
        {
            var ry2 = y + RowH;
            if (Updates.Known == null)
            {
                ui.Text(versionsProblem ?? "Reading the versions…", x + Inset, ry2 + 10, Theme.Callout, t.Ink2, w - 2 * Inset - 100);
                if (versionsProblem != null) { if (ui.FormButton("set:u:vagain", x + w - Inset - ui.FormButtonWidth("Try Again"), ry2 + RowH / 2, "Try Again")) { versionsProblem = null; _ = LoadVersions(); } }
                else ui.Spinner(x + w - Inset - 10, ry2 + RowH / 2, 6);
            }
            else if (rows.Count == 0) ui.Text("No versions are published yet.", x + Inset, ry2 + 10, Theme.Callout, t.Ink2);
            else
            {
                foreach (var r in rows)
                {
                    Label(ui, x, ry2 + Row2H / 2, $"Simpl {r.Version}{(r.Beta ? " Beta" : "")}", r.Detail.Length > 0 ? r.Detail : null, w - 140);
                    if (r.Run == Updates.CurrentRun) Value(ui, x + w, ry2 + Row2H / 2, "Installed");
                    else if (ui.FormButton("set:u:open:" + r.Tag, x + w - Inset - ui.FormButtonWidth("Open…"), ry2 + Row2H / 2, "Open…", enabled: !dev)) Updates.Open(r);
                    ry2 += Row2H;
                }
            }
        }
        return Footer(ui, x, box.Bottom, w, "Another version is a download from its page: this copy stays until that one is unzipped over it.");
    }

    private async Task LoadVersions()
    {
        try { await Updates.Releases(); }
        catch (Exception e) { versionsProblem = e.Message; }
        host.Invalidate();
    }

    private static string Ago(DateTime? at)
    {
        if (at == null) return "just now";
        var d = DateTime.UtcNow - at.Value.ToUniversalTime();
        if (d.TotalSeconds < 60) return "just now";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes} minute{((int)d.TotalMinutes == 1 ? "" : "s")} ago";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours} hour{((int)d.TotalHours == 1 ? "" : "s")} ago";
        return $"{(int)d.TotalDays} day{((int)d.TotalDays == 1 ? "" : "s")} ago";
    }

    // ---- About ----

    private float About(Ui ui, float x, float y, float w)
    {
        var t = ui.T;
        var box = new R(x, y, w, 84 + RowH * 2);
        BoxBehind(ui, box);
        // the app's icon in its tile, its name and what it is
        ui.Fill(new R(x + Inset, y + 14, 56, 56), 13, t.Dark ? new Color4(1, 1, 1, 0.08f) : new Color4(0, 0, 0, 0.05f));
        ui.Image(Path.Combine(AppPaths.AppDir, "Assets", "Simpl-256.png"), new R(x + Inset + 4, y + 18, 48, 48));
        ui.Text("Simpl", x + Inset + 72, y + 16, Theme.Title2, t.Ink());
        ui.Text($"{engine.LmsName}, as a Windows app.", x + Inset + 72, y + 48, Theme.Body, t.Ink2);
        Separator(ui, x, y + 84, w);
        Label(ui, x, y + 84 + RowH / 2, "Version");
        Value(ui, x + w, y + 84 + RowH / 2, Updates.Version);
        Separator(ui, x, y + 84 + RowH, w);
        Label(ui, x, y + 84 + RowH * 1.5f, "Interface");
        Value(ui, x + w, y + 84 + RowH * 1.5f, ScriptBundle.ExtensionVersion);
        return Footer(ui, x, box.Bottom, w, $"To take Simpl off this PC with everything it stored, sign out here first, then delete Simpl’s folder and {AppPaths.Root}. Your {engine.LmsName} account is not affected.");
    }

    // ---- the word at the foot ----

    public void Say(string text, bool error = false)
    {
        said = text;
        saidError = error;
        saidAt = host.Ui.Time;
        host.Invalidate();
    }

    private void SaidBar(Ui ui)
    {
        if (said == null) return;
        var age = ui.Time - saidAt;
        if (age > 3.3) { said = null; return; }
        ui.Animating = true;
        var a = age < 0.25 ? (float)(age / 0.25) : age > 3 ? (float)(1 - (age - 3) / 0.3) : 1;
        var t = ui.T;
        ui.Layer(Ui.PopoverOver);
        var f = Theme.Medium(Theme.Callout);
        var m = ui.Measure(said, f);
        var r = new R(ui.W / 2 - (m.X + 46) / 2, ui.H - 12 - 34 + (1 - a) * 10, m.X + 46, 34);
        ui.Glass(r, 17, GlassKind.Panel, null, 0, Ui.Shadow.Card);
        ui.Icon(saidError ? "exclamationmark.triangle.fill" : "checkmark.circle.fill", r.X + 18, r.CenterY, 14, saidError ? t.Red : t.Ink());
        ui.Text(said, r.X + 32, r.CenterY - m.Y / 2, f, saidError ? t.Red : t.Ink());
    }
}
