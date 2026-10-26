using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Simpl.Support;

/// <summary>
/// The look chosen in Settings ▸ Appearance (the Mac's AppearanceStore and AppLook), worn as Windows 11 wears it: light,
/// dark or as Windows is set; Mica behind every window; the accent Windows uses, or one of the web's colours, or a ready
/// theme's; and a photo of your own for the navigation pane and each Dashboard counter, kept in the app's folder. All of
/// it in the app's own preferences, none of it sent anywhere.
/// </summary>
public static class Appearance
{
    public static event Action? Changed;

    public readonly record struct Ready(string Name, string? Accent, string? Scene);
    public readonly record struct Swatch(string Name, string Hex);

    public static readonly Ready[] ReadyThemes =
    {
        new("Default", null, null), new("Dusk", "#ff375f", "Dusk"), new("Ocean", "#40c8e0", "Ocean"),
        new("Forest", "#30d158", "Forest"), new("Sand", "#ff9f0a", "Sand"), new("Peaks", "#5e5ce6", "Peaks"), new("City", "#bf5af2", "City"),
    };

    public static readonly Swatch[] Colours =
    {
        new("Pink", "#ff375f"), new("Red", "#ff453a"), new("Amber", "#ff9f0a"), new("Green", "#30d158"),
        new("Teal", "#40c8e0"), new("Indigo", "#5e5ce6"), new("Purple", "#bf5af2"),
    };

    /// <summary>The places a photo can go: the navigation pane, and each of the Dashboard's six counters.</summary>
    public static readonly (string Key, string Name)[] Slots =
    {
        ("side", "Navigation pane"), ("today", "Due today"), ("tomorrow", "Tomorrow"), ("next", "Next 7 days"),
        ("overdue", "Overdue"), ("unread", "Announcements"), ("graded", "Graded this week"),
    };

    /// <summary>"light", "dark" or "system".</summary>
    public static string Look
    {
        get => Prefs.String("appearance") ?? "system";
        set { Prefs.Set("appearance", value == "system" ? null : value); Apply(); }
    }

    private static bool? dark;
    private static Color? accent;

    /// <summary>Light or dark now: as asked at launch (--appearance), else as chosen, else as Windows is set. Read once
    /// and kept until the look is worn again (Apply): it is asked per chip and tile.</summary>
    public static bool Dark => dark ??= Launch.Get("appearance") switch
    {
        "dark" => true,
        "light" => false,
        _ => Look switch { "dark" => true, "light" => false, _ => SystemDark() },
    };

    public static bool SystemDark()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return true; }
    }

    /// <summary>The theme worn, by name ("Default" when none).</summary>
    public static string ThemeName => Launch.Get("theme") ?? Prefs.String("theme") ?? "Default";

    /// <summary>The accent's hex, or null for Windows' own accent colour.</summary>
    public static string? AccentHex => Launch.Get("theme") is { } t ? ReadyThemes.FirstOrDefault(r => r.Name == t).Accent : Prefs.String("accent");

    public static void Wear(Ready r)
    {
        Prefs.Set("theme", r.Name == "Default" ? null : r.Name);
        Prefs.Set("accent", r.Accent);
        Apply();
    }

    public static void SetAccent(string? hex)
    {
        Prefs.Set("accent", hex);
        if (hex == null || ReadyThemes.FirstOrDefault(r => r.Name == ThemeName).Accent != hex) Prefs.Set("theme", null); // (a colour of your own: no longer the theme's)
        Apply();
    }

    /// <summary>The backdrop every window wears: Mica where Windows has it (11), a plain ground elsewhere, in the
    /// screenshot suite's pictures (Windows Server draws no Mica) and where WPF draws without the graphics card (Fx).</summary>
    public static WindowBackdropType Backdrop => Launch.Get("shot") != null || Kit.Fx.Opaque ? WindowBackdropType.None : WindowBackdropType.Mica;

    /// <summary>At launch, and after a change: light or dark and the accent, for every control and every window.</summary>
    public static void Apply()
    {
        dark = null; // (read again: --appearance and --theme stay as launched, the rest changes only through here)
        accent = null;
        if (Application.Current == null) return;
        var theme = Dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ApplicationThemeManager.Apply(theme, Backdrop, AccentHex == null);
        if (AccentHex is { } h && Hex(h) is { } c) ApplicationAccentColorManager.Apply(c, theme);
        ReadableText(Application.Current.Resources, Dark);
        foreach (Window w in Application.Current.Windows)
        {
            try { WindowBackgroundManager.UpdateBackground(w, theme, w is FluentWindow fw ? fw.WindowBackdropType : WindowBackdropType.None); } catch { }
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// The light theme's secondary and tertiary text, solid: Fluent's are black at 62% and 45% (#9E000000, #72000000),
    /// and WPF draws a see-through text colour without ClearType, its glyphs' edges paler still — the picker's line,
    /// the sign-in card's, every row's description in Settings measured about #8D8D8D on #F9F9F9 (3.2 to 1), under the
    /// 4.5 to 1 small text needs. Solid greys instead: #5F5F5F (6 to 1 on the window, 6.4 on a card) and #707070 (4.7 and
    /// 5), the second still a step dimmer than the first, the first a step dimmer than the text itself (#1B1B1B). Written
    /// at the application's level, over the theme's dictionary (as the accent is); the dark theme keeps Fluent's own,
    /// which read well (#C5FFFFFF on #202020 is 10 to 1).
    /// </summary>
    internal static void ReadableText(ResourceDictionary app, bool dark)
    {
        (string Key, Color Light)[] solid =
        {
            ("TextFillColorSecondary", Color.FromRgb(0x5F, 0x5F, 0x5F)),
            ("TextFillColorTertiary", Color.FromRgb(0x70, 0x70, 0x70)),
        };
        foreach (var (key, light) in solid)
        {
            if (dark)
            {
                app.Remove(key);
                app.Remove(key + "Brush");
                continue;
            }
            app[key] = light;
            var brush = new SolidColorBrush(light);
            brush.Freeze();
            app[key + "Brush"] = brush;
        }
    }

    /// <summary>The accent now (Windows' own, or the one chosen), kept until the look is worn again (Apply).</summary>
    public static Color Accent => accent ??= AccentHex is { } h && Hex(h) is { } c ? c : ApplicationAccentColorManager.SystemAccent;

    public static Color? Hex(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return null;
        try { return (Color)ColorConverter.ConvertFromString(hex.StartsWith('#') ? hex : "#" + hex); } catch { return null; }
    }

    // ---- photos ----

    public static string PhotosDir => System.IO.Path.Combine(AppPaths.Root, "Photos");

    /// <summary>A place's photo of the student's own. (A screenshot run's --theme-photo &lt;file&gt;: that picture in every
    /// place, nothing kept; with --theme-plain, its Ink off.)</summary>
    public static string? Photo(string slot) =>
        Launch.Get("theme-photo") is { Length: > 0 } shot && System.IO.File.Exists(shot) ? shot
        : Prefs.String("photo:" + slot) is { Length: > 0 } p && System.IO.File.Exists(p) ? p : null;

    /// <summary>"drawing" (the theme's) or "photo" (your own): what a place shows.</summary>
    public static string PhotoKind(string slot) => Launch.Get("theme-photo") is { Length: > 0 } ? "photo" : Prefs.String($"photo:{slot}:kind") ?? (Photo(slot) != null ? "photo" : "drawing");

    /// <summary>(0.3.59) A photo of the student's own with its Ink switch off: shown as it is, nothing over it, the words on
    /// it dark with a gentle white border (the web's plain[key], the Mac's SlotPhoto.plain). A drawing is always inked.</summary>
    public static bool Plain(string slot) => PhotoKind(slot) == "photo" && Photo(slot) != null && (Launch.Flag("theme-plain") || Prefs.Bool($"photo:{slot}:plain"));

    /// <summary>(0.3.59) A photo of the student's own inked (false) or shown as it is (true).</summary>
    public static void SetPlain(string slot, bool plain) { Prefs.Set($"photo:{slot}:plain", plain ? true : null); Changed?.Invoke(); }

    public static double Zoom(string slot) => Prefs.Double($"photo:{slot}:zoom", 1);

    public static void SetZoom(string slot, double z) { Prefs.Set($"photo:{slot}:zoom", z); Changed?.Invoke(); }

    public static void SetKind(string slot, string kind) { Prefs.Set($"photo:{slot}:kind", kind); Changed?.Invoke(); }

    /// <summary>How far a place's picture is moved from where it is pinned (the Mac's drag in its preview), as a share
    /// of the place's size each way: (0, 0) where it is pinned, at most half the place either way.</summary>
    public static (double X, double Y) Pan(string slot) =>
        (Math.Clamp(Prefs.Double($"photo:{slot}:x", 0), -0.5, 0.5), Math.Clamp(Prefs.Double($"photo:{slot}:y", 0), -0.5, 0.5));

    public static void SetPan(string slot, double x, double y)
    {
        KeepPan(slot, x, y);
        Changed?.Invoke();
    }

    /// <summary>(0.3.61) Where a place's picture is and how large, kept at once (the photo in its place, let go): the look
    /// worn again once, not for each.</summary>
    public static void SetPlace(string slot, double x, double y, double zoom)
    {
        KeepPan(slot, x, y);
        Prefs.Set($"photo:{slot}:zoom", Math.Round(Math.Clamp(zoom, 0.5, 3), 3));
        Changed?.Invoke();
    }

    private static void KeepPan(string slot, double x, double y)
    {
        Prefs.Set($"photo:{slot}:x", Math.Abs(x) < 0.001 ? null : Math.Round(Math.Clamp(x, -0.5, 0.5), 3));
        Prefs.Set($"photo:{slot}:y", Math.Abs(y) < 0.001 ? null : Math.Round(Math.Clamp(y, -0.5, 0.5), 3));
    }

    /// <summary>A place's picture back where it is pinned, at its own size.</summary>
    public static void ResetPicture(string slot)
    {
        Prefs.Remove($"photo:{slot}:x");
        Prefs.Remove($"photo:{slot}:y");
        Prefs.Set($"photo:{slot}:zoom", 1.0);
        Changed?.Invoke();
    }

    /// <summary>The scene a place draws when it shows a drawing: one picked for it (any ready theme's — the Mac's
    /// Drawing menu), else the worn theme's; null when neither has one (Default, nothing picked).</summary>
    public static string? SceneFor(string slot) =>
        Prefs.String($"photo:{slot}:scene") is { Length: > 0 } picked && ReadyThemes.Any(r => r.Scene == picked) ? picked
        : ReadyThemes.FirstOrDefault(r => r.Name == ThemeName).Scene;

    /// <summary>The drawing picked for a place (a ready theme's scene), or null to follow the theme worn.</summary>
    public static string? PickedScene(string slot) => Prefs.String($"photo:{slot}:scene") is { Length: > 0 } s ? s : null;

    /// <summary>A place set to show a drawing: a scene of its own, or (null) the theme worn's.</summary>
    public static void SetScene(string slot, string? scene)
    {
        Prefs.Set($"photo:{slot}:scene", scene);
        Prefs.Set($"photo:{slot}:kind", "drawing");
        Changed?.Invoke();
    }

    /// <summary>A picture chosen for a place: a copy of it kept in the app's folder.</summary>
    public static bool SetPhoto(string slot, string file)
    {
        try
        {
            System.IO.Directory.CreateDirectory(PhotosDir);
            var dest = System.IO.Path.Combine(PhotosDir, slot + System.IO.Path.GetExtension(file));
            System.IO.File.Copy(file, dest, true);
            Prefs.Set("photo:" + slot, dest);
            Prefs.Set($"photo:{slot}:kind", "photo");
            Prefs.Set($"photo:{slot}:zoom", 1.0);
            Prefs.Remove($"photo:{slot}:x");
            Prefs.Remove($"photo:{slot}:y");
            Prefs.Remove($"photo:{slot}:plain"); // (a new photo starts inked)
            Changed?.Invoke();
            return true;
        }
        catch (Exception e) { Log.Write("photo: " + e.Message); return false; }
    }

    public static void RemovePhoto(string slot)
    {
        Clear(slot);
        Changed?.Invoke();
    }

    private static void Clear(string slot)
    {
        if (Prefs.String("photo:" + slot) is { } p) { try { System.IO.File.Delete(p); } catch { } }
        Prefs.Remove("photo:" + slot);
        Prefs.Remove($"photo:{slot}:kind");
        Prefs.Remove($"photo:{slot}:zoom");
        Prefs.Remove($"photo:{slot}:x");
        Prefs.Remove($"photo:{slot}:y");
        Prefs.Remove($"photo:{slot}:plain");
        Prefs.Remove($"photo:{slot}:scene"); // (back to the worn theme's drawing, not one picked before the photo)
    }

    // ---- Simpl Community ----

    /// <summary>(0.3.67) A photo read from Simpl Community for a place: its bytes, its file's extension, its width over
    /// its height.</summary>
    public readonly record struct Fetched(byte[] Bytes, string Extension, double Aspect);

    /// <summary>
    /// (0.3.67, the Mac's wearCommunity) A Community theme worn in full (Community.Wear): its colour and its look, and in
    /// each of its places its drawing or its photo (kept as a photo of your own is, inked unless it was shared with its
    /// Ink switch off), each where it was pinned; a place it leaves out shows nothing. What was worn before is replaced,
    /// the photos chosen before with it. The look worn again once, at the end.
    /// </summary>
    public static void WearCommunity(Community.Theme t, IReadOnlyDictionary<string, Fetched> photos)
    {
        foreach (var (slot, _) in Slots)
        {
            Clear(slot);
            if (t.At(slot) is not { } p) continue;
            var at = (X: p.X ?? (slot == "side" ? 50 : 100), Y: p.Y ?? 100, Z: Math.Clamp(p.Z ?? 1, 0.5, 3));
            double? aspect;
            if (p.Scene is { } scene && ReadyThemes.Any(r => r.Scene == scene))
            {
                Prefs.Set($"photo:{slot}:scene", scene);
                Prefs.Set($"photo:{slot}:kind", "drawing");
                aspect = Kit.Scenes.Aspect(scene, slot);
            }
            else if (photos.TryGetValue(slot, out var f))
            {
                try
                {
                    System.IO.Directory.CreateDirectory(PhotosDir);
                    var dest = System.IO.Path.Combine(PhotosDir, slot + f.Extension);
                    System.IO.File.WriteAllBytes(dest, f.Bytes);
                    Prefs.Set("photo:" + slot, dest);
                }
                catch (Exception e) { Log.Write("community photo: " + e.Message); continue; }
                Prefs.Set($"photo:{slot}:kind", "photo");
                if (p.Ink == false) Prefs.Set($"photo:{slot}:plain", true);
                aspect = f.Aspect;
            }
            else continue;
            var (x, y) = PanFrom(slot, at.X, at.Y, at.Z, aspect ?? 0);
            KeepPan(slot, x, y);
            Prefs.Set($"photo:{slot}:zoom", Math.Round(at.Z, 3));
        }
        Prefs.Set("theme", null); // (no ready theme's drawings where the Community theme has none)
        Prefs.Set("accent", Hex(t.Accent) != null ? t.Accent!.ToLowerInvariant() : null);
        if (t.Look is "light" or "dark") Prefs.Set("appearance", t.Look);
        Kit.Scenes.Forget(); // (a place's photo replaced: its ink made again)
        Apply();
    }

    /// <summary>A counter's usual shape, width over height, and the navigation pane's (304 wide, about 720 tall).</summary>
    private const double CounterShape = 2.6, PaneShape = 304.0 / 720;

    /// <summary>
    /// (0.3.67) Where the web and the Mac put a place's picture — the point of it at x%, y% on the place's same point, as
    /// CSS's object-position, zoomed z times, the picture first covering its place — as this app keeps it: moved from
    /// where it is pinned (a counter's bottom right, the pane's bottom middle) by a share of the place. That share hangs
    /// on how far the picture overflows its place, so on the picture's shape (<paramref name="aspect"/>, its width over
    /// its height; 0 when not known) and the place's usual one. (0, 0) where it is pinned.
    /// </summary>
    public static (double X, double Y) PanFrom(string slot, double x, double y, double z, double aspect)
    {
        var side = slot == "side";
        var place = side ? PaneShape : CounterShape;
        if (!(aspect > 0) || !double.IsFinite(aspect)) aspect = place;
        z = Math.Clamp(z, 0.5, 3);
        // (how much wider and taller than its place the picture is drawn, as shares of it: none, drawn just so)
        var overX = Math.Max(1, aspect / place) * z - 1;
        var overY = Math.Max(1, place / aspect) * z - 1;
        var px = overX * ((side ? 0.5 : 1) - Math.Clamp(x, 0, 100) / 100);
        var py = overY * (1 - Math.Clamp(y, 0, 100) / 100);
        static double Kept(double v) => Math.Abs(v) < 0.001 ? 0 : Math.Round(Math.Clamp(v, -0.5, 0.5), 3);
        return (Kept(px), Kept(py));
    }
}
