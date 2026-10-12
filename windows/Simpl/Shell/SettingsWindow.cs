using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Simpl.Graphics;
using Simpl.Screens;
using Simpl.Support;
using Simpl.UI;

namespace Simpl.Shell;

/// <summary>
/// The Settings window (the Mac's, Simpl ▸ Settings): 560 wide, drawn by Simpl's renderer — its title in the band at the
/// top, the strip of tabs, the panes — with Windows' own minimize and close at the top right. One at a time, over the
/// main window.
/// </summary>
public sealed class SettingsWindow : Window
{
    private static SettingsWindow? open;
    private readonly GlassHost host = new();
    private readonly SettingsView view;
    private readonly Func<(byte[], int, int)?>? shotBefore;
    private bool dark;

    public static void Show(MainWindow main, Engine.Engine engine, string? tab, Action changeSchool)
    {
        if (open != null)
        {
            if (tab != null) { open.view.Tab = Math.Max(0, Array.FindIndex(SettingsView.Tabs, t => t.Key == tab)); open.host.Invalidate(); }
            open.Activate();
            return;
        }
        open = new SettingsWindow(main, engine, tab, changeSchool);
        open.Show();
    }

    private SettingsWindow(MainWindow main, Engine.Engine engine, string? tab, Action changeSchool)
    {
        Owner = main;
        Title = "Simpl — Settings";
        Width = SettingsView.Width;
        Height = SettingsView.Height;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = SettingsView.TitleBar,
            GlassFrameThickness = new Thickness(0),
            ResizeBorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
            NonClientFrameEdges = NonClientFrameEdges.None,
        });
        var root = new Grid();
        Content = root;
        root.Children.Add(host);
        view = new SettingsView(engine, host, this, tab) { ChangeSchool = () => { Close(); changeSchool(); } };
        Title = "Simpl — " + view.Title;
        dark = AppView.Dark(engine.Snapshot?.Dark);
        host.Ui.T.Dark = dark;
        Background = new SolidColorBrush(dark ? Color.FromRgb(30, 30, 32) : Color.FromRgb(252, 252, 252));
        host.Build = ui => { view.Frame(ui); CaptionButtons(ui); };
        host.Ground = () => view.Ground;
        host.Chrome = p => p.Y < SettingsView.TitleBar && !host.Ui.HitsAt(p) ? 2 /* HTCAPTION */ : 0;
        // (the screenshot suite pictures this window while it is up)
        shotBefore = Shot.Frame;
        Shot.Frame = () => host.IsVisible ? host.Pixels() : null;
        SourceInitialized += (_, _) =>
        {
            var on = dark ? 1 : 0;
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref on, sizeof(int));
        };
        Appearance.Changed += Invalidate;
        Updates.Changed += Invalidate;
        LoginVault.Changed += Invalidate;
        Alerts.Changed += Invalidate;
        Closed += (_, _) =>
        {
            Appearance.Changed -= Invalidate;
            Updates.Changed -= Invalidate;
            LoginVault.Changed -= Invalidate;
            Alerts.Changed -= Invalidate;
            Shot.Frame = shotBefore;
            if (open == this) open = null;
        };
    }

    private void Invalidate()
    {
        var d = AppView.Dark(null);
        if (d != dark)
        {
            dark = d;
            Background = new SolidColorBrush(dark ? Color.FromRgb(30, 30, 32) : Color.FromRgb(252, 252, 252));
            var on = dark ? 1 : 0;
            try { DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref on, sizeof(int)); } catch { }
        }
        host.Invalidate();
    }

    private void CaptionButtons(Ui ui)
    {
        ui.Layer(Ui.Overlay + 5);
        var t = ui.T;
        var ink = t.Ink(0.9f);
        var wash = t.Dark ? new Vortice.Mathematics.Color4(1, 1, 1, 0.08f) : new Vortice.Mathematics.Color4(0, 0, 0, 0.06f);
        const float bw = MainWindow.CaptionButtonW, bh = SettingsView.TitleBar;
        var min = new R(ui.W - bw * 2, 0, bw, bh);
        var close = new R(ui.W - bw, 0, bw, bh);
        var i = ui.Press("win:min", min);
        if (i.Hovered) ui.Fill(min, wash);
        ui.Glyph("", min.CenterX, min.CenterY, 10, ink);
        if (i.Clicked) SystemCommands.MinimizeWindow(this);
        var c = ui.Press("win:close", close);
        if (c.Hovered) ui.Fill(close, new Vortice.Mathematics.Color4(0.77f, 0.17f, 0.11f, 1));
        ui.Glyph("", close.CenterX, close.CenterY, 10, c.Hovered ? new Vortice.Mathematics.Color4(1, 1, 1, 1) : ink);
        if (c.Clicked) Close();
        foreach (var k in ui.Keys) if (k == Key.Escape && Alerts.Current == null && !ui.MenuOpen("set:photo:kindmenu") && !ui.MenuOpen("set:rem:leadmenu")) Close();
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
