using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Simpl.Kit;
using Wpf.Ui.Controls;
using Image = System.Windows.Controls.Image;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

/// <summary>
/// A file of the school's, its pieces (the Mac's FileViews): its icon as Explorer draws it, the app Windows opens it in
/// ("Open in Microsoft Edge"), a copy fetched with the signed-in session for the preview beside a folder's list, a copy
/// kept in Downloads, and the preview itself — drawn by Windows' own renderers (a PDF's pages, a picture, a film or a
/// sound to play, a text to read), as Quick Look's view is on the Mac.
/// </summary>
internal static class CourseFiles
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true, UseCookies = false, AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromMinutes(3) };
    private static readonly Dictionary<string, string> Fetched = new();
    private static readonly Dictionary<string, ImageSource?> Icons = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string?> Apps = new(StringComparer.OrdinalIgnoreCase);

    // ---- what a file is ----

    /// <summary>What a file is, in a word (the Mac's FilesBrowser.kind).</summary>
    public static string Kind(string name, string? kind)
    {
        var ext = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
        if (kind == "pdf" || ext == "pdf") return "PDF";
        if (kind == "image") return "Image";
        if (kind == "video") return "Video";
        if (kind == "audio") return "Audio";
        if (ext is "doc" or "docx" or "pages" or "txt" or "rtf") return "Document";
        if (ext is "ppt" or "pptx" or "key") return "Presentation";
        if (ext is "xls" or "xlsx" or "csv" or "numbers") return "Spreadsheet";
        if (ext is "zip" or "gz" or "tar" or "7z" or "rar") return "Archive";
        return ext.Length == 0 ? "File" : ext.ToUpperInvariant();
    }

    /// <summary>"Open in Microsoft Edge", or "Open in Default App" when Windows has none for it (the Mac's FileKind.openTitle).</summary>
    public static string OpenTitle(string name) => AppName(name) is { } app ? $"Open in {app}" : "Open in Default App";

    private static string? AppName(string name)
    {
        var ext = Path.GetExtension(name);
        if (ext.Length == 0) return null;
        if (Apps.TryGetValue(ext, out var known)) return known;
        string? found = null;
        try
        {
            uint size = 0;
            AssocQueryString(AssocfInitIgnoreUnknown, AssocstrFriendlyAppName, ext, null, null, ref size);
            if (size > 1)
            {
                var sb = new StringBuilder((int)size);
                if (AssocQueryString(AssocfInitIgnoreUnknown, AssocstrFriendlyAppName, ext, null, sb, ref size) == 0 && sb.Length > 0) found = sb.ToString();
            }
        }
        catch { /* not on Windows: no name */ }
        Apps[ext] = found;
        return found;
    }

    /// <summary>A file's icon as Explorer draws it, by the kind its name says it is (the Mac's FileIcon).</summary>
    public static FrameworkElement Icon(string name, double size)
    {
        var ext = Path.GetExtension(name);
        if (!Icons.TryGetValue(ext, out var src))
        {
            src = null;
            try
            {
                var info = new ShFileInfo();
                SHGetFileInfo("file" + ext, FileAttributeNormal, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), ShgfiIcon | ShgfiLargeIcon | ShgfiUseFileAttributes);
                if (info.hIcon != IntPtr.Zero)
                {
                    var bmp = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    bmp.Freeze();
                    src = bmp;
                    DestroyIcon(info.hIcon);
                }
            }
            catch { /* the shell would not say: a symbol instead */ }
            Icons[ext] = src;
        }
        if (src != null) return new Image { Source = src, Width = size, Height = size, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
        var i = K.Icon(SymbolRegular.Document24, size * 0.8, B.Text2);
        i.Width = size;
        i.Height = size;
        return i;
    }

    // ---- fetching ----

    /// <summary>The file fetched with the school's session (the engine's cookies) into a folder of its own; a file
    /// fetched before is there at once.</summary>
    public static async Task<string> Local(Engine.Engine engine, string raw, string name)
    {
        var url = engine.Absolute(raw) ?? throw new InvalidOperationException("This file has no address.");
        var key = url.ToString();
        if (Fetched.TryGetValue(key, out var kept) && File.Exists(kept)) return kept;
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (engine.Web.CoreWebView2 is { } core)
        {
            var cookies = await core.CookieManager.GetCookiesAsync(key);
            if (cookies.Count > 0) req.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}")));
            req.Headers.TryAddWithoutValidation("User-Agent", core.Settings.UserAgent);
        }
        using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"The school answered {(int)res.StatusCode} {res.ReasonPhrase}.");
        var given = res.Content.Headers.ContentDisposition?.FileNameStar ?? res.Content.Headers.ContentDisposition?.FileName?.Trim('"');
        var fileName = Path.HasExtension(name) || string.IsNullOrWhiteSpace(given) ? name : given!;
        var dir = Path.Combine(Path.GetTempPath(), "Simpl Files", Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Clean(fileName));
        await using (var f = File.Create(path)) await res.Content.CopyToAsync(f);
        Fetched[key] = path;
        return path;
    }

    /// <summary>A copy of the file kept in Downloads (fetched first, if it was not): where it went.</summary>
    public static async Task<string> Keep(Engine.Engine engine, string raw, string name)
    {
        var local = await Local(engine, raw, name);
        var dest = Unique(Path.Combine(Engine.FilePreview.Downloads, Path.GetFileName(local)));
        File.Copy(local, dest);
        return dest;
    }

    /// <summary>Windows' "How do you want to open this file?" for a copy kept in Downloads.</summary>
    public static async Task OpenWith(Engine.Engine engine, string raw, string name)
    {
        var path = await Keep(engine, raw, name);
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "openas" }); }
        catch { Process.Start(new ProcessStartInfo("rundll32.exe", $"shell32.dll,OpenAs_RunDLL \"{path}\"") { UseShellExecute = false }); }
    }

    /// <summary>A file kept, shown selected in its folder in File Explorer.</summary>
    public static void Reveal(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); } catch { }
    }

    private static string Clean(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "file" : name;
    }

    private static string Unique(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 2; ; i++)
        {
            var p = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(p)) return p;
        }
    }

    // ---- the preview ----

    private static readonly HashSet<string> Pictures = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff", ".ico", ".webp", ".heic", ".jfif" };
    private static readonly HashSet<string> Texts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".csv", ".tsv", ".md", ".json", ".xml", ".log", ".py", ".js", ".ts", ".java", ".c", ".h", ".cpp", ".cs", ".swift", ".r", ".m", ".sql",
        ".css", ".html", ".htm", ".yml", ".yaml", ".ini", ".tex", ".rtf", ".srt", ".vtt",
    };
    private static readonly HashSet<string> Films = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mkv", ".webm" };
    private static readonly HashSet<string> Sounds = new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".m4a", ".wav", ".wma", ".aac", ".flac" };

    /// <summary>Whether Windows can show a file of this name here.</summary>
    public static bool CanShow(string name)
    {
        var ext = Path.GetExtension(name);
        return Pictures.Contains(ext) || Texts.Contains(ext) || Films.Contains(ext) || Sounds.Contains(ext) || ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A file drawn in the page as it is: a PDF's pages one under another, a picture fitted, a film or a sound with Play,
    /// a text to read. <paramref name="playing"/> hears of a player made (to stop it when the file is let go).
    /// </summary>
    public static FrameworkElement Preview(string path, double width, Action<MediaElement> playing)
    {
        var ext = Path.GetExtension(path);
        if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase)) return Pdf(path, width);
        if (Pictures.Contains(ext)) return Picture(path);
        if (Films.Contains(ext) || Sounds.Contains(ext)) return Player(path, Films.Contains(ext), playing);
        return Text(path);
    }

    private static FrameworkElement Picture(string path)
    {
        var img = new Image { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        // (no wider than the screen needs: a phone's photo is 48 MB decoded whole)
        if (Kit.Decoding.Picture(path) is { } p) Kit.Decoding.Show(img, p);
        return img;
    }

    private static FrameworkElement Text(string path)
    {
        string text;
        using (var s = File.OpenRead(path))
        using (var r = new StreamReader(s, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            var buf = new char[200_000];
            var n = r.ReadBlock(buf, 0, buf.Length);
            text = new string(buf, 0, n);
            if (!r.EndOfStream) text += "\n…";
        }
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12.5, Margin = new Thickness(14, 10, 14, 14) };
        t.SetResourceReference(TextBlock.ForegroundProperty, B.Text);
        return new ScrollViewer { Content = t, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    /// <summary>
    /// A PDF's pages, drawn by Windows' own PDF renderer at the pane's width, one under another: each drawn as it comes
    /// near the part of the pane shown, and let go again once far from it, its place kept the same size meanwhile (all
    /// sixty pages drawn at the pane's width are 4 MB a page at 150 % scaling, a quarter of a gigabyte for one file).
    /// </summary>
    private static FrameworkElement Pdf(string path, double width)
    {
        var pages = new Stack { Orientation = Orientation.Vertical, Spacing = 12, Margin = new Thickness(12) };
        var scroller = new ScrollViewer { Content = pages, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var reading = K.Reading("Drawing the pages…");
        pages.Children.Add(reading);
        var w = Math.Max(200, width - 24);
        PdfSheets? sheets = null;
        scroller.Loaded += async (_, _) =>
        {
            if (sheets != null) { sheets.Fill(); return; }
            if (pages.Tag != null) return;
            pages.Tag = "drawn";
            try
            {
                sheets = await PdfSheets.Open(path, scroller, pages, w, VisualTreeHelper.GetDpi(scroller).PixelsPerDip);
                await sheets.FillNow();
            }
            catch (Exception e) { pages.Children.Add(K.Empty(SymbolRegular.Warning24, "The pages could not be drawn. " + e.Message)); }
            pages.Children.Remove(reading);
        };
        // (a pane taken away gives its pages back; they are drawn again if it comes back)
        scroller.Unloaded += (_, _) => sheets?.LetGo(all: true);
        scroller.ScrollChanged += (_, _) => sheets?.Fill();
        return scroller;
    }

    /// <summary>A PDF's pages in a pane: a sheet each, the size its page will be, drawn while near what is shown.</summary>
    private sealed class PdfSheets
    {
        private sealed class Sheet
        {
            public required Image Img;
            public required ImageSource Blank;
            public BitmapSource? Drawn;
            public bool Failed;
        }

        /// <summary>How far past what is shown pages are drawn, and kept, in heights of the pane.</summary>
        private const double DrawAhead = 1.5, KeepAhead = 4;

        private readonly global::Windows.Data.Pdf.PdfDocument doc;
        private readonly ScrollViewer scroller;
        private readonly List<Sheet> sheets = new();
        private readonly uint across;
        private bool filling, again;

        private PdfSheets(global::Windows.Data.Pdf.PdfDocument doc, ScrollViewer scroller, uint across)
        {
            this.doc = doc;
            this.scroller = scroller;
            this.across = across;
        }

        /// <summary>The document read and a blank sheet laid for each page (up to 60), the size each will be once drawn.</summary>
        public static async Task<PdfSheets> Open(string path, ScrollViewer scroller, Stack pages, double width, double scale)
        {
            var file = await global::Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            var doc = await global::Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
            var across = (uint)Math.Round(width * scale);
            var made = new PdfSheets(doc, scroller, across);
            var count = Math.Min(doc.PageCount, 60u);
            for (uint i = 0; i < count; i++)
            {
                double tall;
                using (var page = doc.GetPage(i)) tall = page.Size.Width > 0 ? Math.Round(across * page.Size.Height / page.Size.Width) : across;
                var blank = Blank(across, Math.Max(1, tall));
                var img = new Image { Source = blank, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
                var sheet = new Border { Child = img, BorderThickness = new Thickness(1), Effect = K.Shadow(8, 1, 0.12), HorizontalAlignment = HorizontalAlignment.Center };
                sheet.Stroke(B.CardStroke);
                pages.Children.Insert(Math.Max(0, pages.Children.Count - 1), sheet);
                made.sheets.Add(new Sheet { Img = img, Blank = blank });
            }
            if (doc.PageCount > count) pages.Children.Add(K.Text($"{doc.PageCount} pages in all: open it to read the rest.", Ty.Caption, B.Text2).Center());
            return made;
        }

        /// <summary>Nothing drawn, the size of a page drawn <paramref name="w"/> by <paramref name="h"/> pixels at 96 dpi.</summary>
        private static ImageSource Blank(double w, double h)
        {
            var d = new DrawingImage(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, w, h))));
            d.Freeze();
            return d;
        }

        /// <summary>The pages near what is shown drawn (soon), the far ones let go.</summary>
        public void Fill()
        {
            if (filling) { again = true; return; }
            _ = FillNow();
        }

        public async Task FillNow()
        {
            if (filling) { again = true; return; }
            filling = true;
            try
            {
                do
                {
                    again = false;
                    // (once laid out: where each sheet is is known only then)
                    await scroller.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
                    LetGo(all: false);
                    while (scroller.IsLoaded && Next() is { } i)
                    {
                        var s = sheets[i];
                        try
                        {
                            byte[] png;
                            using (var page = doc.GetPage((uint)i))
                            using (var stream = new global::Windows.Storage.Streams.InMemoryRandomAccessStream())
                            {
                                await page.RenderToStreamAsync(stream, new global::Windows.Data.Pdf.PdfPageRenderOptions { DestinationWidth = across });
                                using var ms = new MemoryStream();
                                stream.AsStream().CopyTo(ms);
                                png = ms.ToArray();
                            }
                            // (decoded off the window's thread)
                            var bmp = await Task.Run(() =>
                            {
                                var b = new BitmapImage();
                                b.BeginInit();
                                b.CacheOption = BitmapCacheOption.OnLoad;
                                b.StreamSource = new MemoryStream(png, writable: false);
                                b.EndInit();
                                b.Freeze();
                                return b;
                            });
                            s.Drawn = bmp;
                            s.Img.Source = bmp;
                            // (let go again later at the size it was drawn)
                            s.Blank = Blank(bmp.Width, bmp.Height);
                        }
                        catch { s.Failed = true; /* (a page that cannot be drawn stays blank; the rest are drawn) */ }
                    }
                } while (again && scroller.IsLoaded);
            }
            finally { filling = false; }
        }

        /// <summary>Where a sheet is against what the pane shows: its top and bottom, in heights of the pane from its top.</summary>
        private (double Top, double Bottom)? Place(int i)
        {
            var img = sheets[i].Img;
            var h = scroller.ViewportHeight;
            if (!img.IsLoaded || h <= 0 || !scroller.IsAncestorOf(img)) return null;
            var top = img.TransformToAncestor(scroller).Transform(new Point(0, 0)).Y;
            return (top / h, (top + img.ActualHeight) / h);
        }

        /// <summary>The page to draw next: the one shown first, then the nearest beyond it.</summary>
        private int? Next()
        {
            int? best = null;
            var bestDist = double.MaxValue;
            for (var i = 0; i < sheets.Count; i++)
            {
                if (sheets[i].Drawn != null || sheets[i].Failed || Place(i) is not { } p) continue;
                if (p.Bottom < -DrawAhead || p.Top > 1 + DrawAhead) continue;
                var dist = p.Bottom < 0 ? -p.Bottom : p.Top > 1 ? p.Top - 1 : 0;
                if (dist < bestDist) { best = i; bestDist = dist; }
            }
            return best;
        }

        /// <summary>The pages far from what is shown (all of them, when the pane is gone) back to blank sheets.</summary>
        public void LetGo(bool all)
        {
            for (var i = 0; i < sheets.Count; i++)
            {
                var s = sheets[i];
                if (s.Drawn == null) continue;
                if (!all && Place(i) is { } p && p.Bottom >= -KeepAhead && p.Top <= 1 + KeepAhead) continue;
                s.Img.Source = s.Blank;
                s.Drawn = null;
            }
        }
    }

    /// <summary>A film (or a sound) to play, stopped until Play is pressed.</summary>
    private static FrameworkElement Player(string path, bool film, Action<MediaElement> playing)
    {
        var media = new MediaElement { Source = new Uri(path), LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, Stretch = Stretch.Uniform, ScrubbingEnabled = true };
        playing(media);
        var stage = new Grid();
        if (!film) stage.Children.Add(K.Icon(SymbolRegular.MusicNote224, 64, B.Text3));
        stage.Children.Add(media);
        var on = false;
        Wpf.Ui.Controls.Button play = null!;
        play = K.Button("Play", () =>
        {
            on = !on;
            if (on) media.Play(); else media.Pause();
            play.Content = on ? "Pause" : "Play";
            play.Icon = new SymbolIcon { Symbol = on ? SymbolRegular.Pause24 : SymbolRegular.Play24 };
        }, icon: SymbolRegular.Play24);
        play.HorizontalAlignment = HorizontalAlignment.Center;
        media.MediaOpened += (_, _) => { if (!on) { media.Play(); media.Pause(); } }; // (the first frame shown)
        media.MediaEnded += (_, _) =>
        {
            on = false;
            media.Stop();
            play.Content = "Play";
            play.Icon = new SymbolIcon { Symbol = SymbolRegular.Play24 };
        };
        media.Loaded += (_, _) => { media.Play(); media.Pause(); };
        return K.Rows("*, auto", stage.Row(0), play.Margin(0, 10, 0, 6).Row(1));
    }

    // ---- Windows' shell ----

    private const int AssocfInitIgnoreUnknown = 0x400;
    private const int AssocstrFriendlyAppName = 4;
    private const uint FileAttributeNormal = 0x80;
    private const uint ShgfiIcon = 0x100, ShgfiLargeIcon = 0x0, ShgfiUseFileAttributes = 0x10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int AssocQueryString(int flags, int str, string pszAssoc, string? pszExtra, StringBuilder? pszOut, ref uint pcchOut);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref ShFileInfo psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
