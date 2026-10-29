using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Wpf;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Shell;

/// <summary>
/// (0.3.85) A file from the school in a preview window of Simpl's own before anything is downloaded (the Mac's 1.3.191):
/// a click on a file fetches it into a folder of Simpl's own (<see cref="FilePreview"/>, not Downloads) and shows it here
/// — a PDF in Edge's own viewer, a picture, a film or a sound, plain text — with its app and Download over it. Only
/// Download (Ctrl+S) puts a copy in Downloads (the window's foot says so), and then offers Show in folder; Open in its app
/// opens a copy kept in Downloads too. A kind of file no web view draws (a Word document, a slide deck, a zip) shows its
/// name, kind and size, with the same two ways out. Escape closes it; another file opened while it is up takes its place.
/// One window, kept where it was last put.
/// </summary>
public sealed class FileViewWindow : FluentWindow
{
    private const double BarHeight = 48;
    private static FileViewWindow? shared;

    /// <summary>What a web view draws as it is: Edge's PDF viewer, pictures, films and sounds, plain text.</summary>
    private static readonly HashSet<string> Drawn = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg", ".ico", ".avif",
        ".mp4", ".webm", ".m4v", ".mov", ".mp3", ".m4a", ".wav", ".ogg", ".oga", ".aac", ".flac",
        ".txt", ".log", ".json", ".xml", ".htm", ".html",
    };

    private readonly TitleBar bar;
    private readonly WebView2 web = new() { DefaultBackgroundColor = System.Drawing.Color.White, Visibility = Visibility.Collapsed };
    private readonly ContentControl card = new() { Focusable = false };
    private readonly ContentControl actions = new() { Focusable = false, VerticalAlignment = VerticalAlignment.Center };
    private bool webReady;
    private string file = "", name = "";
    /// <summary>Its copy in Downloads, once it has one.</summary>
    private string? saved;

    /// <summary>The file in the preview window (made the first time; brought up over the window it was opened from).</summary>
    public static void Show(string path, string name)
    {
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w is not FileViewWindow) ?? Application.Current?.MainWindow;
        if (shared == null || shared.closed)
        {
            shared = new FileViewWindow();
            if (owner != null && owner.IsVisible) { shared.Owner = owner; shared.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        }
        var w = shared;
        w.Open(path, name);
        if (!w.IsVisible) w.Show();
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
    }

    private bool closed;

    private FileViewWindow()
    {
        Title = "Preview";
        WindowBackdropType = Appearance.Backdrop;
        ExtendsContentIntoTitleBar = true;
        FontFamily = K.TextFace;
        SetResourceReference(ForegroundProperty, B.Text);
        if (Shot.Asked || Fx.Opaque) SetResourceReference(BackgroundProperty, B.Window);
        Placement.Restore(this, "preview", 1000, 760, 520, 400);
        bar = new TitleBar
        {
            Title = Title, Height = BarHeight, Margin = new Thickness(8, 0, 0, 0), TrailingContent = actions,
            Icon = new ImageIcon { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/Simpl-256.png")), Width = 16, Height = 16 },
        };
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(BarHeight) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(bar);
        Grid.SetRow(web, 1);
        root.Children.Add(web);
        Grid.SetRow(card, 1);
        root.Children.Add(card);
        Content = root;
        SizeChanged += (_, _) => Placement.Save(this, "preview");
        LocationChanged += (_, _) => Placement.Save(this, "preview");
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control && saved == null) { Download(); e.Handled = true; }
        };
        Closed += (_, _) =>
        {
            closed = true;
            if (ReferenceEquals(shared, this)) shared = null;
            try { web.Dispose(); } catch { /* already gone */ }
        };
    }

    /// <summary>The file shown: in the web view where one draws it, else its card.</summary>
    private async void Open(string path, string shownName)
    {
        file = path;
        name = string.IsNullOrWhiteSpace(shownName) ? Path.GetFileName(path) : shownName;
        saved = null;
        Title = name;
        bar.Title = name;
        Actions();
        Log.Write($"files: preview {Path.GetExtension(path)} file, not downloaded"); // (never its name: the student's)
        if (!Drawn.Contains(Path.GetExtension(path)))
        {
            Card(null);
            return;
        }
        card.Content = null;
        try
        {
            if (!webReady)
            {
                var env = await Engine.Engine.WebEnvironment();
                await web.EnsureCoreWebView2Async(env);
                web.CoreWebView2.Settings.AreDevToolsEnabled = false;
                web.CoreWebView2.Settings.IsStatusBarEnabled = false;
                // (a file the web view will not draw after all, and offers to download: its card instead)
                web.CoreWebView2.DownloadStarting += (_, e) => { e.Cancel = true; Dispatcher.BeginInvoke(() => Card(null)); };
                webReady = true;
            }
            if (file != path) return; // (another file came meanwhile)
            web.Visibility = Visibility.Visible;
            web.CoreWebView2.Navigate(new Uri(path).AbsoluteUri);
        }
        catch (Exception e)
        {
            Card(e.Message);
        }
    }

    /// <summary>A file no web view draws here: its kind, its size and its name, and the ways out.</summary>
    private void Card(string? why)
    {
        web.Visibility = Visibility.Collapsed;
        long size = 0;
        try { size = new FileInfo(file).Length; } catch { /* gone */ }
        var kind = Path.GetExtension(file).TrimStart('.').ToUpperInvariant();
        var icon = K.Icon(SymbolRegular.Document48, 64, B.Text2);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        var title = K.Text(name, Ty.Subtitle, B.Text, TextWrapping.Wrap);
        title.TextAlignment = TextAlignment.Center;
        title.HorizontalAlignment = HorizontalAlignment.Center;
        var line = K.Text($"{(kind.Length > 0 ? kind + " · " : "")}{Size(size)}", Ty.Body, B.Text2);
        line.HorizontalAlignment = HorizontalAlignment.Center;
        var note = K.Text(why ?? $"Simpl can’t show this kind of file here. {AppNote()}", Ty.Body, B.Text2, TextWrapping.Wrap);
        note.TextAlignment = TextAlignment.Center;
        note.HorizontalAlignment = HorizontalAlignment.Center;
        note.MaxWidth = 420;
        var open = K.Button(Screens.AssignKit.OpenTitle(name), OpenInApp, icon: SymbolRegular.Open24);
        var down = K.Button("Download", Download, accent: true, icon: SymbolRegular.ArrowDownload24);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0), Children = { open, down } };
        open.Margin = new Thickness(0, 0, 8, 0);
        var stack = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 520, Margin = new Thickness(24) };
        foreach (var e in new UIElement[] { icon, title, line, note, buttons }) stack.Children.Add(e);
        title.Margin = new Thickness(0, 16, 0, 4);
        note.Margin = new Thickness(0, 16, 0, 8);
        card.Content = stack;
        Motion.Enter(stack, 0, 12, 300);
    }

    private string AppNote() => Screens.AssignKit.AppFor(name) is { } app ? $"Open it in {app}, or download it." : "Download it to open it.";

    private static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} bytes",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.0} MB",
    };

    /// <summary>The title bar's buttons: its app, and Download — Show in folder once it is downloaded.</summary>
    private void Actions()
    {
        var open = K.Button(Screens.AssignKit.OpenTitle(name), OpenInApp, icon: SymbolRegular.Open24);
        open.ToolTip = "A copy in Downloads, opened in its app";
        FrameworkElement last;
        if (saved == null)
        {
            var down = K.Button("Download", Download, accent: true, icon: SymbolRegular.ArrowDownload24);
            down.ToolTip = "Save it to your Downloads folder (Ctrl+S)";
            last = down;
        }
        else
        {
            var show = K.Button("Show in folder", () => Reveal(saved), icon: SymbolRegular.FolderOpen24);
            show.ToolTip = "Downloaded: show it in File Explorer";
            last = show;
        }
        open.Margin = new Thickness(0, 0, 8, 0);
        actions.Content = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0), Children = { open, last } };
    }

    /// <summary>Download: a copy in Downloads, under a name not yet taken.</summary>
    private void Download()
    {
        if (FilePreview.KeepCopy(file, name) is not { } copy) return;
        saved = copy;
        Actions();
        Log.Write("files: preview downloaded");
    }

    /// <summary>Its app: the copy in Downloads (made now, if it has none) opened there.</summary>
    private void OpenInApp()
    {
        var path = saved ?? FilePreview.KeepCopy(file, name, quiet: true);
        if (path == null) return;
        if (saved == null) { saved = path; Actions(); }
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception e) { Dialogs.Fail($"{name} could not be opened", e.Message); }
    }

    private static void Reveal(string? path)
    {
        if (path == null) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); } catch { /* no Explorer */ }
    }
}
