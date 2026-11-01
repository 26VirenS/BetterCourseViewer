using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace Simpl.Support;

/// <summary>
/// (0.3.67, the Mac's 1.3.173 and the web's 2.99.106) Simpl Community in Appearance: themes students made and shared,
/// read from the site's gallery (simplcourses.com/api/community/themes, open to the apps), each worn in full with a
/// click — its colour, its look, and its pictures, each where it was pinned: one of Simpl's drawings by name, or a photo
/// read from the site. What was worn before is replaced. (0.3.104, the Mac's 1.3.208 and the web's Share yours) And this
/// theme shared: named, signed if the student likes, and sent to the site (simplcourses.com/api/community/submit) to be
/// looked at before it shows in the gallery.
/// </summary>
public static class Community
{
    /// <summary>A place's picture as the gallery gives it (site/src/community.js publicTheme): a drawing by name, or a
    /// photo's address on the site; where it sits (x%, y%, zoom); a photo shared with its Ink switch off.</summary>
    public sealed class Place
    {
        public string? Scene { get; set; }
        public string? Photo { get; set; }
        public double? X { get; set; }
        public double? Y { get; set; }
        public double? Z { get; set; }
        public bool? Ink { get; set; }
    }

    public sealed class Theme
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Maker { get; set; }
        public string? Accent { get; set; }
        public string? Look { get; set; }
        public Dictionary<string, Place>? Places { get; set; }

        public Place? At(string slot) => Places != null && Places.TryGetValue(slot, out var p) ? p : null;
    }

    private sealed class Gallery
    {
        public bool? Ok { get; set; }
        public List<Theme>? Themes { get; set; }
        public string? Message { get; set; }
    }

    public enum Phase { Idle, Loading, Ready, Failed }

    /// <summary>The gallery read, a tile's photo come, a theme put on (or not): on the UI thread, as each is.</summary>
    public static event Action? Changed;

    public static Phase State { get; private set; } = Phase.Idle;

    /// <summary>Why the gallery could not be read.</summary>
    public static string? Why { get; private set; }

    public static IReadOnlyList<Theme> Themes { get; private set; } = Array.Empty<Theme>();

    /// <summary>The theme being put on now (its photos read), if one is.</summary>
    public static string? Busy { get; private set; }

    /// <summary>Why the last theme could not be put on.</summary>
    public static string? Problem { get; private set; }

    /// <summary>The last theme worn from here (kept).</summary>
    public static string? Worn => Prefs.String("community-worn");

    /// <summary>The places a tile shows: the pane's and two counters' (the Mac's CommunitySwatch).</summary>
    public static readonly string[] TileSlots = { "side", "today", "overdue" };

    private static readonly Dictionary<string, BitmapSource> Thumbs = new();
    /// <summary>The connection to the site, Community tools' too (Tools/Community/CommunityTools.cs): no cookies.</summary>
    internal static readonly HttpClient Http = new(new HttpClientHandler { UseCookies = false, AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly Regex PhotoPath = new(@"^/api/community/photo/[a-z0-9]+/[a-z]+$", RegexOptions.CultureInvariant);

    /// <summary>The site (the screenshot suite's --community-base: the mock's own stand-in for it).</summary>
    public static Uri Site => Uri.TryCreate(Launch.Get("community-base") ?? "", UriKind.Absolute, out var u) ? u : new Uri("https://simplcourses.com");

    /// <summary>The gallery's own photos only: an approved theme's, by its id and place, on the site (simplcourses.com's
    /// over https; else the one --community-base names, as it is).</summary>
    public static Uri? PhotoUrl(string? raw, Uri? site = null)
    {
        site ??= Site;
        if (!Uri.TryCreate(raw ?? "", UriKind.Absolute, out var u) || !PhotoPath.IsMatch(u.AbsolutePath)) return null;
        var ours = u.Scheme == Uri.UriSchemeHttps && u.Host is "simplcourses.com" or "www.simplcourses.com";
        var named = u.Scheme == site.Scheme && u.Host == site.Host && u.Port == site.Port;
        return ours || named ? u : null;
    }

    /// <summary>The gallery read (once, the first time Community is opened; again with Try again).</summary>
    public static async Task Load(bool force = false)
    {
        if (State == Phase.Loading || (State == Phase.Ready && !force)) return;
        State = Phase.Loading;
        Why = null;
        Problem = null;
        Changed?.Invoke();
        try
        {
            using var res = await Http.GetAsync(new Uri(Site, "api/community/themes"));
            var g = JsonSerializer.Deserialize<Gallery>(await res.Content.ReadAsStringAsync(), Json.Options);
            if (!res.IsSuccessStatusCode || g == null || g.Ok == false || g.Themes == null)
            {
                Fail(g?.Message ?? "Simpl Community could not be reached.");
                return;
            }
            Themes = g.Themes.Where(t => t.Id.Length > 0 && t.Name.Length > 0).Take(80).ToList();
            State = Phase.Ready;
            Log.Write($"community: {Themes.Count} themes read");
            Changed?.Invoke();
            foreach (var t in Themes) _ = ReadThumbs(t);
        }
        catch (Exception e)
        {
            Log.Write("community: " + e.Message);
            Fail("Simpl Community could not be reached. Check your connection and try again.");
        }
    }

    private static void Fail(string why)
    {
        State = Phase.Failed;
        Why = why;
        Changed?.Invoke();
    }

    /// <summary>A tile's pictures: the photo of each place a tile shows, small.</summary>
    private static async Task ReadThumbs(Theme t)
    {
        foreach (var slot in TileSlots)
        {
            var key = $"{t.Id}/{slot}";
            if (Thumbs.ContainsKey(key) || PhotoUrl(t.At(slot)?.Photo) is not { } url) continue;
            try
            {
                if (Decode(await Http.GetByteArrayAsync(url), 160) is not { } img) continue;
                Thumbs[key] = img;
                Changed?.Invoke();
            }
            catch (Exception e) { Log.Write("community: a tile's photo: " + e.Message); }
        }
    }

    public static BitmapSource? Thumb(Theme t, string slot) => Thumbs.TryGetValue($"{t.Id}/{slot}", out var b) ? b : null;

    /// <summary>A theme worn in full: its photos read from the site, then its colour, its look and its pictures put on
    /// (Appearance.WearCommunity). One that cannot be read leaves what is worn as it was, and says so.</summary>
    public static async Task Wear(Theme t)
    {
        if (Busy != null) return;
        Busy = t.Id;
        Problem = null;
        Changed?.Invoke();
        try
        {
            var photos = new Dictionary<string, Appearance.Fetched>();
            foreach (var (slot, _) in Appearance.Slots)
            {
                if (t.At(slot) is not { Scene: null, Photo: { } raw }) continue;
                byte[]? bytes = null;
                if (PhotoUrl(raw) is { } url)
                {
                    try
                    {
                        using var res = await Http.GetAsync(url);
                        if (res.IsSuccessStatusCode) bytes = await res.Content.ReadAsByteArrayAsync();
                    }
                    catch (Exception e) { Log.Write("community: " + e.Message); }
                }
                // (known by its first bytes, as the site knows it, and read whole: its shape places it)
                if (bytes == null || Extension(bytes) is not { } ext || Decode(bytes, 64) is not { } probe)
                {
                    Problem = $"A photo of “{t.Name}” could not be read. Try again.";
                    Log.Write($"community: {t.Id}'s {slot} photo could not be read");
                    return;
                }
                photos[slot] = new Appearance.Fetched(bytes, ext, probe.PixelWidth / (double)Math.Max(1, probe.PixelHeight));
            }
            Appearance.WearCommunity(t, photos);
            Prefs.Set("community-worn", t.Id);
            Log.Write($"community: wore {t.Id}");
        }
        finally
        {
            Busy = null;
            Changed?.Invoke();
        }
    }

    // ---- this theme shared ----

    public enum ShareState { Idle, Sending, Sent, Failed }

    /// <summary>Where sharing this theme stands, and why it could not go.</summary>
    public static ShareState Sharing { get; private set; }
    public static string? ShareWhy { get; private set; }

    /// <summary>A theme's photos can be several megabytes: a minute for them to go.</summary>
    private static readonly HttpClient Upload = new(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>Something to share: a colour, or a drawing or a photo in one of the places (the site takes nothing less).</summary>
    public static bool CanShare => Appearance.AccentHex != null || Appearance.Slots.Any(s => Appearance.ShareablePlace(s.Key) != null);

    /// <summary>
    /// This theme sent to Simpl Community (the web's lib/theme.js communityOf, background.js community 'share'): its name
    /// and maker, the colour, the look chosen (light, dark, or none for Windows' own), and in each place its drawing, or
    /// its photo — sent beside it as a JPEG small enough for the site — where it sits, a photo's Ink switch as it is. No
    /// cookie, nothing else of the student's. A place's own colour is not part of a shared theme (the web's is not either).
    /// True when the site took it; it shows in the gallery once the owner has looked at it.
    /// </summary>
    public static async Task<bool> Share(string name, string maker)
    {
        if (Sharing == ShareState.Sending) return false;
        name = name.Trim();
        maker = maker.Trim();
        if (name.Length > 40) name = name[..40];
        if (maker.Length > 30) maker = maker[..30];
        if (name.Length == 0) return Refused("Give the theme a name.");
        if (!CanShare) return Refused("Pick a colour or a picture first.");
        Sharing = ShareState.Sending;
        ShareWhy = null;
        Changed?.Invoke();
        try
        {
            var places = new Dictionary<string, object?>();
            var photos = new Dictionary<string, byte[]>();
            foreach (var (slot, _) in Appearance.Slots)
            {
                if (Appearance.ShareablePlace(slot) is not { } p) continue;
                var aspect = p.Aspect;
                var d = new Dictionary<string, object?>();
                if (p.Scene is { } scene) d["scene"] = scene;
                else if (p.File is { } file)
                {
                    if (await Task.Run(() => ShareJpeg(file)) is not { } jpeg) continue;
                    photos[slot] = jpeg.Bytes;
                    aspect = jpeg.Aspect;
                    d["photo"] = true;
                    if (p.Plain) d["ink"] = false;
                }
                var (x, y) = Appearance.PlaceFrom(slot, p.PanX, p.PanY, p.Zoom, aspect);
                d["x"] = x;
                d["y"] = y;
                d["z"] = Math.Round(p.Zoom, 3);
                places[slot] = d;
            }
            var look = Appearance.Look is "light" or "dark" ? Appearance.Look : null;
            var theme = new Dictionary<string, object?> { ["name"] = name, ["maker"] = maker, ["accent"] = Appearance.AccentHex?.ToLowerInvariant(), ["look"] = look, ["places"] = places };
            // (the form written out as the Mac and the web send it: every name in quotes, the boundary bare — .NET's own
            //  MultipartFormDataContent leaves the names unquoted and adds filename*=, which the site's form reader turns
            //  down: "That did not arrive as a form.")
            var boundary = "SimplCommunity-" + Guid.NewGuid().ToString("N");
            using var sent = new MemoryStream();
            void Text(string t) { var b = System.Text.Encoding.UTF8.GetBytes(t); sent.Write(b, 0, b.Length); }
            Text($"--{boundary}\r\nContent-Disposition: form-data; name=\"theme\"\r\nContent-Type: application/json; charset=utf-8\r\n\r\n{JsonSerializer.Serialize(theme)}\r\n");
            foreach (var (slot, bytes) in photos)
            {
                Text($"--{boundary}\r\nContent-Disposition: form-data; name=\"photo-{slot}\"; filename=\"{slot}.jpg\"\r\nContent-Type: image/jpeg\r\n\r\n");
                sent.Write(bytes, 0, bytes.Length);
                Text("\r\n");
            }
            Text($"--{boundary}--\r\n");
            using var form = new ByteArrayContent(sent.ToArray());
            form.Headers.TryAddWithoutValidation("Content-Type", $"multipart/form-data; boundary={boundary}");
            using var res = await Upload.PostAsync(new Uri(Site, "api/community/submit"), form);
            var body = await res.Content.ReadAsStringAsync();
            string? id = null, message = null;
            var ok = false;
            try
            {
                using var doc = JsonDocument.Parse(body);
                ok = doc.RootElement.TryGetProperty("ok", out var o) && o.ValueKind == JsonValueKind.True;
                if (doc.RootElement.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String) id = i.GetString();
                if (doc.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String) message = m.GetString();
            }
            catch (JsonException) { /* not the site's answer */ }
            if (!res.IsSuccessStatusCode || !ok)
            {
                Log.Write($"community: share refused ({message ?? ((int)res.StatusCode).ToString()})");
                return Refused(message ?? "That did not go through. Try again later.");
            }
            Sharing = ShareState.Sent;
            Log.Write($"community: shared {name} ({id}, {photos.Count} photos)");
            Changed?.Invoke();
            return true;
        }
        catch (Exception e)
        {
            Log.Write("community: share failed: " + e.Message);
            return Refused("Simpl Community could not be reached. Check your connection and try again.");
        }
    }

    private static bool Refused(string why)
    {
        Sharing = ShareState.Failed;
        ShareWhy = why;
        Changed?.Invoke();
        return false;
    }

    /// <summary>Back to not yet (the form opened afresh).</summary>
    public static void ShareAgain()
    {
        if (Sharing == ShareState.Sending) return;
        Sharing = ShareState.Idle;
        ShareWhy = null;
    }

    /// <summary>A photo made small enough to share: no more than 1,600 pixels on its longer side, then 1,200 and 900 at
    /// less quality while it is still over 1.4 MB (the site takes up to 1.5 MB a photo); and its shape.</summary>
    private static (byte[] Bytes, double Aspect)? ShareJpeg(string file)
    {
        try
        {
            var bytes = File.ReadAllBytes(file);
            int w, h;
            using (var probe = new MemoryStream(bytes))
            {
                var frame = BitmapFrame.Create(probe, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                w = frame.PixelWidth;
                h = frame.PixelHeight;
            }
            if (w <= 0 || h <= 0) return null;
            foreach (var (side, quality) in new[] { (1600, 82), (1200, 75), (900, 70) })
            {
                using var ms = new MemoryStream(bytes);
                var img = new BitmapImage();
                img.BeginInit();
                img.StreamSource = ms;
                img.CacheOption = BitmapCacheOption.OnLoad;
                if (w >= h && w > side) img.DecodePixelWidth = side;
                else if (h > w && h > side) img.DecodePixelHeight = side;
                img.EndInit();
                img.Freeze();
                var enc = new JpegBitmapEncoder { QualityLevel = quality };
                enc.Frames.Add(BitmapFrame.Create(img));
                using var outStream = new MemoryStream();
                enc.Save(outStream);
                if (outStream.Length <= 1_400_000) return (outStream.ToArray(), w / (double)h);
            }
            return null;
        }
        catch (Exception e)
        {
            Log.Write("community: a photo could not be made ready to share: " + e.Message);
            return null;
        }
    }

    /// <summary>The screenshot suite's --community-wear: a theme of the gallery worn by its name, as its tile's click would.</summary>
    public static async Task WearNamed(string name)
    {
        await Load();
        if (Themes.FirstOrDefault(t => t.Name == name) is { } t) await Wear(t);
        else Log.Write($"community: no theme named {name}");
    }

    /// <summary>A photo's kind by its first bytes (the site's imageType), as its file's extension; null for anything else.</summary>
    internal static string? Extension(byte[] b) =>
        b.Length > 3 && b[0] == 0xff && b[1] == 0xd8 && b[2] == 0xff ? ".jpg"
        : b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4e && b[3] == 0x47 ? ".png"
        : b.Length > 12 && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46 && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50 ? ".webp"
        : null;

    private static BitmapSource? Decode(byte[] bytes, int width)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            var b = new BitmapImage();
            b.BeginInit();
            b.StreamSource = ms;
            b.CacheOption = BitmapCacheOption.OnLoad;
            b.DecodePixelWidth = width;
            b.EndInit();
            b.Freeze();
            return b;
        }
        catch { return null; }
    }
}
