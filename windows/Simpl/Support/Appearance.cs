using Simpl.UI;
using Vortice.Mathematics;

namespace Simpl.Support;

/// <summary>
/// The look chosen in Settings ▸ Appearance (the Mac's AppearanceStore and AppLook): light, dark or as Windows is; a
/// ready-made theme (the web's: a colour, and a drawing in every place — the drawings come later on Windows); the
/// accent colour (one of the web's, or the theme's); and a photo of your own for the sidebar and each Dashboard
/// counter, kept in the app's folder. All of it in the app's own preferences, none of it sent anywhere.
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

    /// <summary>The places a photo can go: the sidebar, and each of the Dashboard's six counters.</summary>
    public static readonly (string Key, string Name)[] Slots =
    {
        ("side", "Sidebar"), ("today", "Due today"), ("tomorrow", "Tomorrow"), ("week", "Next 7 days"),
        ("overdue", "Overdue"), ("unread", "Unread"), ("graded", "Graded"),
    };

    /// <summary>"light", "dark" or "system".</summary>
    public static string Look
    {
        get => Prefs.String("appearance") ?? "system";
        set { Prefs.Set("appearance", value == "system" ? null : value); Changed?.Invoke(); }
    }

    /// <summary>Light or dark now: as asked at launch (--appearance), else as chosen, else as the page says, else as Windows is set.</summary>
    public static bool Dark(bool? page) => Launch.Get("appearance") switch
    {
        "dark" => true,
        "light" => false,
        _ => Look switch { "dark" => true, "light" => false, _ => page ?? SystemDark() },
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
    public static string ThemeName => Prefs.String("theme") ?? "Default";

    /// <summary>The accent's hex, or null for the regular one (the Mac's own blue).</summary>
    public static string? AccentHex => Prefs.String("accent");

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

    /// <summary>At launch, and after a change: the accent the screens draw with.</summary>
    public static void Apply()
    {
        Theme.CustomAccent = AccentHex is { } h ? Theme.Hex(h) : null;
        Changed?.Invoke();
    }

    // ---- photos ----

    public static string PhotosDir => System.IO.Path.Combine(AppPaths.Root, "Photos");

    public static string? Photo(string slot) => Prefs.String("photo:" + slot) is { Length: > 0 } p && System.IO.File.Exists(p) ? p : null;

    /// <summary>"drawing" (the theme's) or "photo" (your own): what a place shows.</summary>
    public static string PhotoKind(string slot) => Prefs.String($"photo:{slot}:kind") ?? (Photo(slot) != null ? "photo" : "drawing");

    public static float Zoom(string slot) => (float)Prefs.Double($"photo:{slot}:zoom", 1);

    public static void SetZoom(string slot, float z) { Prefs.Set($"photo:{slot}:zoom", z); Changed?.Invoke(); }

    public static void SetKind(string slot, string kind) { Prefs.Set($"photo:{slot}:kind", kind); Changed?.Invoke(); }

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
            Changed?.Invoke();
            return true;
        }
        catch (Exception e) { Log.Write("photo: " + e.Message); return false; }
    }

    public static void RemovePhoto(string slot)
    {
        if (Prefs.String("photo:" + slot) is { } p) { try { System.IO.File.Delete(p); } catch { } }
        Prefs.Remove("photo:" + slot);
        Prefs.Remove($"photo:{slot}:kind");
        Prefs.Remove($"photo:{slot}:zoom");
        Changed?.Invoke();
    }
}
