using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Simpl.Kit;
using Wpf.Ui.Controls;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

// What the Inbox's screens share (InboxScreen, ConversationScreen, ComposeSheet — the Mac's InboxShared): the replies
// begun, what the Inbox hands the conversation beside its list, a person's round picture, and the pieces they draw alike.

/// <summary>
/// What the Inbox keeps while the app is open (the Mac's InboxStore): a reply begun in a conversation — kept while others
/// are read, while the window goes elsewhere, and when the conversation is opened on a screen of its own. The mailboxes
/// and conversations as last read are the engine's kept answers. Let go on signing out and when the school changes.
/// </summary>
internal static class InboxStore
{
    private static readonly Dictionary<string, string> drafts = new();
    private static WeakReference<Engine.Engine>? owner;

    /// <summary>A reply begun or let go (the conversation's id): the Inbox's list shows its pencil.</summary>
    public static event Action<string>? DraftChanged;

    /// <summary>The engine whose school is showing: another one (another school) lets go of what the last one kept.</summary>
    public static void Bind(Engine.Engine engine)
    {
        if (owner != null && owner.TryGetTarget(out var e) && ReferenceEquals(e, engine)) return;
        Forget();
        owner = new WeakReference<Engine.Engine>(engine);
        engine.PropertyChanged += (_, a) =>
        {
            // (signed out: the snapshot goes)
            if (a.PropertyName == nameof(Engine.Engine.Snapshot) && engine.Snapshot == null) Forget();
        };
    }

    public static string Draft(string id) => drafts.TryGetValue(id, out var d) ? d : "";

    public static bool HasDraft(string id) => drafts.ContainsKey(id);

    public static void SetDraft(string id, string text)
    {
        var had = drafts.ContainsKey(id);
        if (string.IsNullOrEmpty(text)) drafts.Remove(id); else drafts[id] = text;
        if (had != drafts.ContainsKey(id)) DraftChanged?.Invoke(id);
    }

    private static void Forget()
    {
        var ids = drafts.Keys.ToList();
        drafts.Clear();
        foreach (var id in ids) DraftChanged?.Invoke(id);
    }
}

/// <summary>
/// What the Inbox hands the conversation it shows beside its list (the Mac's InboxLink): the list's word on its star (null
/// once the mailbox showing no longer holds it — Unread, after it is read), and the star itself.
/// </summary>
internal sealed record InboxLink(Func<bool?> Starred, Action<bool> Star);

/// <summary>What the page's <c>messageUpload</c> answers: the file's id in Canvas, its name and its size in words.</summary>
internal sealed class MessageUploaded
{
    public string Id { get; set; } = "";
    public string? Name { get; set; }
    public long? Size { get; set; }
    public string? SizeText { get; set; }
}

/// <summary>
/// The files going with one message, a new one or a reply (the Mac's MessageAttachments): each picked file is uploaded at
/// once to the student's own "conversation attachments" folder in Canvas (<c>messageUpload</c>), as Canvas's own Inbox
/// does, and the message carries its id. Send waits until every one is up and takes the ids of those that are; one that
/// could not go up shows why and is left out. A file already uploaded and then taken out stays in the student's files in
/// Canvas, as Canvas's own Inbox leaves it.
/// </summary>
internal sealed class MessageFiles
{
    /// <summary>A file going with the message: its name and size, Canvas's id once it is up, or why it could not go.</summary>
    public sealed class Item
    {
        public Guid Key { get; } = Guid.NewGuid();
        public required string Name { get; init; }
        public string SizeText { get; set; } = "";
        public string? Id { get; set; }
        public string? Failed { get; set; }
        public bool Uploading => Id == null && Failed == null;
    }

    /// <summary>The largest file sent (as a hand-in's): a larger one is sent from Canvas's own Inbox.</summary>
    public const long Limit = 50L * 1024 * 1024;

    private readonly Engine.Engine engine;
    private readonly List<Item> items = new();

    public MessageFiles(Engine.Engine engine) => this.engine = engine;

    /// <summary>The list changed (a file added, up, failed or taken out): its chips are drawn again.</summary>
    public event Action? Changed;

    public IReadOnlyList<Item> Items => items;
    /// <summary>A file still on its way up.</summary>
    public bool Uploading => items.Any(i => i.Uploading);
    /// <summary>Canvas's ids of the files that are up.</summary>
    public List<string> Ids => items.Where(i => i.Id != null).Select(i => i.Id!).ToList();

    /// <summary>Files picked or dropped: each read and uploaded (one picked twice is taken once).</summary>
    public void Add(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            var name = System.IO.Path.GetFileName(path);
            if (name.Length == 0 || items.Any(i => i.Name == name && i.Failed == null)) continue;
            long size;
            try { size = new System.IO.FileInfo(path).Length; }
            catch { continue; }
            var item = new Item { Name = name, SizeText = SizeText(size) };
            items.Add(item);
            if (size > Limit) item.Failed = $"Larger than 50 MB: send it from {engine.LmsName}’s own Inbox.";
            else _ = Upload(item, path);
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// The screenshot suite's (a development run only): --attach sample attaches a small text file of its own making
    /// ("Lab notes.txt"), --attach &lt;path&gt; that file — to the first new message or reply opened, once.
    /// </summary>
    public void TakeLaunchFile()
    {
        if (!Support.Launch.IsDevelopment || Support.Launch.Take("", "attach") is not { Length: > 0 } a) return;
        var path = a;
        if (a is "sample" or "true")
        {
            try
            {
                var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Simpl-shots");
                System.IO.Directory.CreateDirectory(dir);
                path = System.IO.Path.Combine(dir, "Lab notes.txt");
                System.IO.File.WriteAllText(path, "Notes for the lab: the readings for week 2, and the questions to bring.\n");
            }
            catch { return; }
        }
        if (System.IO.File.Exists(path)) Add(new[] { path });
    }

    /// <summary>A file taken out.</summary>
    public void Remove(Item item)
    {
        items.Remove(item);
        Changed?.Invoke();
    }

    /// <summary>Every file let go (the message sent).</summary>
    public void Clear()
    {
        if (items.Count == 0) return;
        items.Clear();
        Changed?.Invoke();
    }

    private async Task Upload(Item item, string path)
    {
        try
        {
            var data = await System.IO.File.ReadAllBytesAsync(path);
            var up = await engine.Call<MessageUploaded>("messageUpload", new { name = item.Name, type = AssignKit.Mime(item.Name), data = Convert.ToBase64String(data) });
            item.Id = up.Id;
            if (!string.IsNullOrEmpty(up.SizeText)) item.SizeText = up.SizeText!;
        }
        catch (System.IO.IOException) { item.Failed = "This file could not be read."; }
        catch (UnauthorizedAccessException) { item.Failed = "This file could not be read."; }
        catch (Exception e) { item.Failed = e.Message.Length > 0 ? e.Message : "It could not be attached."; }
        if (items.Contains(item)) Changed?.Invoke(); // (not one taken out while it went up)
    }

    /// <summary>A file's size as the page writes one: "16 B", "12 KB", "1.5 MB".</summary>
    public static string SizeText(long n) =>
        n < 1024 ? $"{n} B"
        : n < 1048576 ? $"{Math.Round((decimal)n / 1024, 0, MidpointRounding.AwayFromZero)} KB"
        : $"{Math.Round((decimal)n / 1048576, 1, MidpointRounding.AwayFromZero).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} MB";

    /// <summary>
    /// The files as chips in lines that wrap (the Mac's MessageAttachmentChips): each its name and size, a ring while it
    /// goes up (a red mark, and why under the pointer, if it could not), and × to take it out. A chip comes in by a short
    /// fade and rise, as the To field's people do; with Windows' animation effects off, at once.
    /// </summary>
    public FrameworkElement Chips(HashSet<Guid> seen)
    {
        var flow = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var f in items)
        {
            var file = f;
            var failed = f.Failed != null;
            UIElement mark = f.Uploading ? K.Spinner(12)
                : failed ? K.Icon(SymbolRegular.Warning16, 14, B.Critical, filled: true)
                : K.Icon(SymbolRegular.Attach16, 14, B.Text2);
            var name = K.Text(f.Name, Ty.Body);
            name.MaxWidth = 220;
            var size = K.Text(f.SizeText, Ty.Caption, B.Text2);
            size.VerticalAlignment = VerticalAlignment.Center;
            var remove = K.Subtle(K.Icon(SymbolRegular.DismissCircle24, 15, B.Text2, filled: true), () => Remove(file), new Thickness(0), 9);
            remove.Width = 20;
            remove.Height = 20;
            remove.HorizontalContentAlignment = HorizontalAlignment.Center;
            remove.ToolTip = $"Remove {f.Name}";
            System.Windows.Automation.AutomationProperties.SetName(remove, $"Remove {f.Name}");
            var row = K.H(6, mark, name, size, remove);
            var tint = failed ? K.Tone("red") : Support.Appearance.Accent;
            var chip = new Border
            {
                Child = row, Padding = new Thickness(9, 3, 3, 3), CornerRadius = new CornerRadius(13), Margin = new Thickness(0, 0, 6, 6),
                Background = K.Brush(Color.FromArgb(Support.Appearance.Dark ? (byte)56 : (byte)30, tint.R, tint.G, tint.B)),
                ToolTip = f.Failed ?? (f.Uploading ? $"Uploading {f.Name}…" : f.Name),
            };
            System.Windows.Automation.AutomationProperties.SetName(chip, failed ? $"{f.Name}, not attached: {f.Failed}" : f.Uploading ? $"{f.Name}, uploading" : $"{f.Name}, {f.SizeText}, attached");
            if (seen.Add(f.Key)) Motion.Enter(chip, 0, 6, Motion.Normal);
            flow.Children.Add(chip);
        }
        seen.IntersectWith(items.Select(i => i.Key));
        return flow;
    }
}

/// <summary>The Inbox's own pieces, drawn alike on its screens.</summary>
internal static class InboxUi
{
    /// <summary>A conversation's own address (Canvas's links name it so, and Simpl's web Inbox opens at it).</summary>
    public static string Address(string id) => $"/conversations?id={id}";

    /// <summary>The colours a person's initials sit on (the Mac's: blue, indigo, purple, pink, orange, teal, green, mint,
    /// cyan, brown — as Windows' shared palette has them, deep enough for white letters).</summary>
    private static readonly Color[] Tones =
    {
        Color.FromRgb(0x2b, 0x88, 0xd8), Color.FromRgb(0x5b, 0x5f, 0xc7), Color.FromRgb(0x87, 0x64, 0xb8), Color.FromRgb(0xe3, 0x00, 0x8c),
        Color.FromRgb(0xca, 0x50, 0x10), Color.FromRgb(0x03, 0x83, 0x87), Color.FromRgb(0x49, 0x82, 0x05), Color.FromRgb(0x00, 0x9e, 0x85),
        Color.FromRgb(0x00, 0x99, 0xbc), Color.FromRgb(0x8e, 0x56, 0x2e),
    };

    /// <summary>The name's own colour (from its letters, so the same on every launch — and as the Mac picks it).</summary>
    public static Color Tone(string name)
    {
        var seed = 0;
        foreach (var r in name.EnumerateRunes()) seed = unchecked(seed * 31 + r.Value) & 0xFFFF;
        return Tones[seed % Tones.Length];
    }

    /// <summary>
    /// A person's picture, or their initials on a colour of their own (the same colour every time for the same name);
    /// for a conversation with several people ("Ana Ruiz, Ben Ode +2"), two figures — the Mac's InboxAvatar.
    /// </summary>
    public static FrameworkElement Avatar(string name, string? picture, double size)
    {
        var tone = Tone(name);
        var g = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Top };
        g.Children.Add(new Ellipse { Fill = new LinearGradientBrush(K.Mix(tone, Colors.White, 0.12), tone, 90) });
        var many = name.Contains(',') || name.Contains('+');
        var letters = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2)
            .Select(w => w.FirstOrDefault(char.IsLetter)).Where(c => c != default)).ToUpperInvariant();
        UIElement face = many ? K.Icon(SymbolRegular.People24, size * 0.46, Colors.White, filled: true)
            : letters.Length == 0 ? K.Icon(SymbolRegular.Person24, size * 0.5, Colors.White, filled: true)
            : new TextBlock
            {
                Text = letters, FontSize = size * 0.38, FontWeight = FontWeights.SemiBold, FontFamily = K.DisplayFace, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
        g.Children.Add(face);
        if (!string.IsNullOrEmpty(picture) && Uri.TryCreate(picture, UriKind.Absolute, out var u) && u.Scheme is "http" or "https")
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = u;
                bmp.DecodePixelWidth = (int)(size * 2);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                g.Children.Add(new Ellipse { Fill = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill } });
                bmp.DownloadCompleted += (_, _) => face.Visibility = Visibility.Hidden;
            }
            catch { /* the initials stay */ }
        }
        System.Windows.Automation.AutomationProperties.SetName(g, name);
        return g;
    }

    /// <summary>A file's kind by the end of its name, as a symbol (the Mac's InboxFiles).</summary>
    public static SymbolRegular FileSymbol(string name) => System.IO.Path.GetExtension(name).TrimStart('.').ToLowerInvariant() switch
    {
        "pdf" => SymbolRegular.DocumentPdf24,
        "png" or "jpg" or "jpeg" or "gif" or "heic" or "webp" or "bmp" or "tif" or "tiff" or "svg" => SymbolRegular.Image24,
        "mp4" or "mov" or "m4v" or "avi" or "webm" => SymbolRegular.Video24,
        "mp3" or "m4a" or "wav" or "aac" or "ogg" => SymbolRegular.MusicNote224,
        "zip" or "gz" or "tar" or "rar" or "7z" => SymbolRegular.FolderZip24,
        "doc" or "docx" or "pages" or "txt" or "rtf" or "odt" => SymbolRegular.DocumentText24,
        "xls" or "xlsx" or "numbers" or "csv" => SymbolRegular.Table24,
        "ppt" or "pptx" or "key" => SymbolRegular.SlideLayout24,
        _ => SymbolRegular.Attach24,
    };

    /// <summary>A file sent with a message, opened (the Mac's Quick Look: kept in Downloads and opened in its app).</summary>
    public static void OpenFile(Engine.Engine engine, string url, string name)
    {
        if (engine.Absolute(url) is { } u) Engine.FilePreview.Open(u, name, engine.Web);
    }

    public static void Copy(string text)
    {
        try { Clipboard.SetText(text); } catch { /* the clipboard was busy */ }
    }

    /// <summary>An address copied as the school gives it.</summary>
    public static void CopyLink(Engine.Engine engine, string url)
    {
        if (engine.Absolute(url) is { } u) Copy(u.ToString());
    }

    public static MenuItem Item(string text, SymbolRegular icon, Action act, bool enabled = true)
    {
        var mi = new MenuItem { Header = text, Icon = new SymbolIcon { Symbol = icon }, IsEnabled = enabled };
        mi.Click += (_, _) => act();
        return mi;
    }

    /// <summary>A menu opened under what was pressed (or at the pointer).</summary>
    public static void Open(ContextMenu menu, FrameworkElement at, bool atPointer = false)
    {
        menu.PlacementTarget = at;
        menu.Placement = atPointer ? System.Windows.Controls.Primitives.PlacementMode.MousePoint : System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.MinWidth = 220;
        menu.IsOpen = true;
    }

    /// <summary>The Mac's CanvasMenu in a screen's commands (⋯): Open in Canvas, Copy link — for the address at the moment.</summary>
    public static FrameworkElement CanvasMenu(Engine.Engine engine, Func<string?> url, Func<string> title)
    {
        System.Windows.Controls.Button? b = null;
        b = K.IconButton(SymbolRegular.MoreHorizontal24, () =>
        {
            var u = url();
            var m = new ContextMenu();
            m.Items.Add(Item($"Open in {engine.LmsName}", SymbolRegular.Globe24, () => { if (u != null) engine.OpenWebScreen(u, title()); }, u != null));
            m.Items.Add(Item("Copy link", SymbolRegular.Link24, () => { if (u != null) CopyLink(engine, u); }, u != null));
            Open(m, b!);
        }, "More");
        return b;
    }

    /// <summary>The Mac's ContentUnavailableView: a quiet symbol, a title, what to do, and perhaps a button — in the middle of its room.</summary>
    public static FrameworkElement Unavailable(SymbolRegular icon, string title, string? description, FrameworkElement? action = null)
    {
        var v = K.V(10, K.Icon(icon, 44, B.Text3).Margin(0, 0, 0, 4), K.Wrapped(title, Ty.Subtitle).Center());
        if (!string.IsNullOrEmpty(description)) v.Children.Add(K.Wrapped(description!, Ty.Body, B.Text2).Center());
        if (action != null)
        {
            action.HorizontalAlignment = HorizontalAlignment.Center;
            action.Margin = new Thickness(0, 6, 0, 0);
            v.Children.Add(action);
        }
        v.HorizontalAlignment = HorizontalAlignment.Center;
        v.VerticalAlignment = VerticalAlignment.Center;
        v.MaxWidth = 380;
        v.Margin = new Thickness(24);
        foreach (var t in v.Children.OfType<TextBlock>()) t.TextTrimming = TextTrimming.None;
        return v;
    }

    /// <summary>A symbol and a line of words in the secondary colour (the Mac's Fact).</summary>
    public static FrameworkElement Fact(SymbolRegular icon, string text)
    {
        var t = K.Wrapped(text, Ty.Body, B.Text2);
        var i = K.Icon(icon, 16, B.Text2).Margin(0, 2, 9, 0);
        i.Width = 20;
        i.VerticalAlignment = VerticalAlignment.Top;
        return K.Columns("auto, *", i.Col(0), t.Col(1));
    }

    // ---- a message's words ----

    private static readonly Regex Links = new(@"(?<url>\b(?:https?://|www\.)[^\s<>""]+)|(?<mail>\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b)", RegexOptions.Compiled);

    /// <summary>
    /// A message's words (plain text, as Canvas keeps them) as the HTML RichText draws: paragraphs where a line is left
    /// empty, a line break for each other, and their web addresses made links (the Mac's inboxLinkedText).
    /// </summary>
    public static string Html(string text)
    {
        var sb = new StringBuilder();
        foreach (var para in Regex.Split(text.Replace("\r\n", "\n").Trim('\n'), @"\n[ \t]*\n"))
        {
            if (para.Trim().Length == 0) continue;
            sb.Append("<p>");
            var lines = para.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0) sb.Append("<br>");
                sb.Append(Linked(lines[i]));
            }
            sb.Append("</p>");
        }
        return sb.ToString();
    }

    private static string Linked(string line)
    {
        var sb = new StringBuilder();
        var at = 0;
        foreach (Match m in Links.Matches(line))
        {
            var raw = m.Value;
            // (the sentence's own stop or bracket is not part of the address)
            var trimmed = raw.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '\'', '"');
            if (trimmed.Length == 0) continue;
            sb.Append(WebUtility.HtmlEncode(line[at..m.Index]));
            var href = m.Groups["mail"].Success ? "mailto:" + trimmed : trimmed.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + trimmed : trimmed;
            sb.Append("<a href=\"").Append(WebUtility.HtmlEncode(href)).Append("\">").Append(WebUtility.HtmlEncode(trimmed)).Append("</a>");
            at = m.Index + trimmed.Length;
        }
        sb.Append(WebUtility.HtmlEncode(line[at..]));
        return sb.ToString();
    }
}
