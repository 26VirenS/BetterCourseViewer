using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Simpl.Support;
using Wpf.Ui.Controls;

namespace Simpl.Kit;

/// <summary>
/// How much of the look this PC can draw (WPF's render tier): drop shadows, motion and Mica cost the graphics card next
/// to nothing, and the processor a great deal where WPF draws without one (a virtual machine, a remote session, a PC
/// with no graphics driver). Under "auto", the default: at tier 2 everything, as it was always drawn; at tier 1 no drop
/// shadows (a pixel shader's work; the hairline edges stay); at tier 0 no motion either (exactly as with Windows'
/// Animation effects off) and solid windows, not Mica. --effects full|lite (or the hidden pref "effects") asks for all
/// or none of it; a picture of the screenshot suite is always full unless it asks. The tier is read once the window
/// has drawn its first frame (it is not known before: until then, everything) and again whenever it changes.
/// </summary>
public static class Fx
{
    private static string? mode;
    private static int? tier;
    private static bool watching;
    private static string? said;

    /// <summary>"auto", "full" or "lite": as asked at launch, else as the pref says, else auto (a picture: full).</summary>
    private static string Mode => mode ??= Asked() switch { "full" => "full", "lite" => "lite", _ => "auto" };

    private static string? Asked()
    {
        try { return (Launch.Get("effects") ?? (Shot.Asked ? "full" : Prefs.String("effects")))?.Trim().ToLowerInvariant(); }
        catch { return null; } // (a pref that is not a word)
    }

    /// <summary>Drop shadows under raised surfaces and on words (K.Shadow, K.TextShadow).</summary>
    public static bool Shadows => Mode == "full" || (Mode == "auto" && (tier ?? 2) >= 2);

    /// <summary>Things moving and fading in and out (Motion.On).</summary>
    public static bool Motion => Mode == "full" || (Mode == "auto" && (tier ?? 2) >= 1);

    /// <summary>Solid windows, a plain ground and no Mica behind them (Appearance.Backdrop).</summary>
    public static bool Opaque => Mode == "lite" || (Mode == "auto" && tier == 0);

    /// <summary>The tier read, once the window has drawn its first frame; and again whenever it changes.</summary>
    public static void Decide()
    {
        if (!watching)
        {
            watching = true;
            RenderCapability.TierChanged += (_, _) => Read();
        }
        Read();
    }

    private static void Read()
    {
        var wasOpaque = Opaque;
        tier = RenderCapability.Tier >> 16;
        var lost = new[] { Shadows ? null : "no shadows", Motion ? null : "no motion", Opaque ? "solid windows" : null }.OfType<string>().ToList();
        var line = $"effects: {Mode} (render tier {tier}): {(lost.Count == 0 ? "all" : string.Join(", ", lost))}";
        if (line != said) { said = line; Log.Write(line); }
        if (Opaque != wasOpaque) Ground();
    }

    /// <summary>
    /// The windows already open made solid (as the screenshot suite's are: no backdrop, the window's ground) or given
    /// their Mica back, then the look worn again over them. A window opened afterwards is made so from the start.
    /// </summary>
    private static void Ground()
    {
        if (Application.Current == null) return;
        var solid = Opaque || Shot.Asked;
        foreach (Window w in Application.Current.Windows)
        {
            if (w is not FluentWindow fw) continue;
            if (!solid) fw.ClearValue(Control.BackgroundProperty);
            fw.WindowBackdropType = Appearance.Backdrop;
            if (solid) fw.SetResourceReference(Control.BackgroundProperty, B.Window);
        }
        Appearance.Apply();
    }
}
