using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Simpl.Support;

namespace Simpl.Shell;

/// <summary>
/// (0.3.91, the Mac's 1.3.196 PageRecorder) A tool window's page recorded, for developers: Record in its title bar
/// (developer mode) watches the page — a load, a change to what it shows, a field changed — and each time it settles
/// saves what it is then into a folder of its own in Downloads (Simpl Recordings ▸ the tool and the time): the page's
/// code (its HTML, with any web component's own tree, and each frame that changed), what it holds (its address, title,
/// scroll and form fields — a password's or a token's value left out, addresses without their queries, where a launch's
/// tokens ride) and a picture of the page. Stop (or the window closed) ends it and shows the folder in Explorer.
///
/// Kept small: only what changed is written (a page or picture the same as the last is not written again; the data
/// file names the one it still is), the code gzipped, the picture a JPEG at most 1280 pixels wide, each script and style
/// sheet once for the whole recording (in assets), a change saved at most every 1.5 seconds once the page has been
/// still a moment — and it stops by itself at 2,000 steps or 500 MB.
/// </summary>
public sealed class PageRecorder
{
    public const int MaxSteps = 2000;
    public const long MaxBytes = 500L * 1024 * 1024;
    private static readonly TimeSpan Gap = TimeSpan.FromSeconds(1.5);
    private const int ShotWidth = 1280;

    private readonly CoreWebView2 core;
    private readonly List<CoreWebView2Frame> frames = new();
    private string? scriptId;
    private string? folder;
    private bool busy, again, timed;
    private DateTime lastAt = DateTime.MinValue;
    private readonly HashSet<string> asked = new();
    private readonly Dictionary<string, (string Hash, string File)> lastCode = new();
    private (string Hash, string File)? lastShot;
    private readonly Dictionary<string, string> assets = new();
    private readonly Dictionary<string, string> frameNames = new();

    public bool On { get; private set; }
    public int Steps { get; private set; }
    public long Bytes { get; private set; }
    public string? Folder => folder;

    /// <summary>A step saved, or the recording begun or ended (the title bar's button and count follow).</summary>
    public event Action? Changed;

    /// <summary>The page's web view, its frames followed from now on (a frame's code is asked of it, its changes heard).</summary>
    public PageRecorder(CoreWebView2 core)
    {
        this.core = core;
        core.WebMessageReceived += (_, e) => Heard("page", e);
        core.FrameCreated += (_, e) =>
        {
            var f = e.Frame;
            frames.Add(f);
            f.Destroyed += (_, _) => frames.Remove(f);
            f.WebMessageReceived += (_, m) => Heard(Key(f), m);
        };
    }

    private static string Key(CoreWebView2Frame f) => "frame " + f.FrameId.ToString(CultureInfo.InvariantCulture);

    public void Toggle(string name) { if (On) Stop(); else _ = Start(name); }

    public async Task Start(string name)
    {
        if (On) return;
        var stamp = DateTime.Now.ToString("yyyy-MM-dd 'at' HH.mm.ss", CultureInfo.InvariantCulture);
        var safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)).Trim();
        if (safe.Length > 60) safe = safe[..60];
        var dir = Path.Combine(Screens.AltaSession.Downloads(), "Simpl Recordings", $"{(safe.Length == 0 ? "Tool" : safe)} {stamp}");
        try { Directory.CreateDirectory(Path.Combine(dir, "assets")); }
        catch (Exception e)
        {
            Log.Write("recorder: the folder could not be made: " + e.Message);
            System.Media.SystemSounds.Beep.Play();
            return;
        }
        folder = dir;
        Steps = 0; Bytes = 0;
        asked.Clear(); lastCode.Clear(); lastShot = null; assets.Clear(); frameNames.Clear();
        lastAt = DateTime.MinValue;
        busy = false; again = false; timed = false;
        Write(Readme(name), "README.txt");
        Write("", "timeline.txt");
        On = true;
        // the page's watcher: in every frame of every page from here on, and in the page and frames shown now
        try { scriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(Watch); } catch (Exception e) { Log.Write("recorder: the watcher could not be added: " + e.Message); }
        await Run(Watch, null);
        foreach (var f in frames.ToList()) await Run(Watch, f);
        Log.Write($"recorder: on, into {dir}");
        Changed?.Invoke();
        Nudge();
    }

    /// <summary>Stopped: the watchers told to stop, the folder shown in Explorer (not as the window closes).</summary>
    public void Stop(bool reveal = true)
    {
        if (!On) return;
        On = false;
        if (scriptId != null) { try { core.RemoveScriptToExecuteOnDocumentCreated(scriptId); } catch { /* the web view gone */ } scriptId = null; }
        const string stop = "window.__simplRecStop && window.__simplRecStop()";
        _ = Run(stop, null);
        foreach (var f in frames.ToList()) _ = Run(stop, f);
        Timeline($"stopped: {Steps} step{(Steps == 1 ? "" : "s")}, {Size(Bytes)}");
        Log.Write($"recorder: off after {Steps} steps, {Bytes} bytes");
        Changed?.Invoke();
        if (reveal && folder != null)
        {
            try { System.Diagnostics.Process.Start("explorer.exe", $"\"{folder}\""); } catch { /* no Explorer */ }
        }
    }

    /// <summary>A frame said it changed (or loaded): looked at once the page has been still a moment, at most every 1.5 s.</summary>
    private void Heard(string key, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!On) return;
        string? said;
        try { said = e.TryGetWebMessageAsString(); } catch { return; }
        if (said is not ("simplRecord:change" or "simplRecord:load")) return;
        asked.Add(key);
        Nudge();
    }

    private async void Nudge()
    {
        if (!On) return;
        if (busy) { again = true; return; }
        if (timed) return;
        timed = true;
        var wait = Gap - (DateTime.Now - lastAt);
        if (wait > TimeSpan.Zero) await Task.Delay(wait);
        timed = false;
        if (On) await Step();
    }

    private async Task Step()
    {
        if (!On || folder == null) return;
        busy = true;
        again = false;
        lastAt = DateTime.Now;
        try
        {
            var reads = new List<(string Key, JsonObject Read)>();
            if (await Run(Collect, null) is JsonObject main0) reads.Add(("page", main0));
            foreach (var f in frames.ToList())
            {
                var k = Key(f);
                if (!asked.Contains(k)) continue;
                if (await Run(Collect, f) is JsonObject r) reads.Add((k, r));
            }
            asked.Clear();
            var shot = await Picture();
            if (!On) return;
            var n = Steps + 1;
            var tag = n.ToString("0000", CultureInfo.InvariantCulture);
            var written = new List<string>();
            var code = new JsonArray();
            var changed = false;
            foreach (var (k, r) in reads)
            {
                var bytes = Encoding.UTF8.GetBytes(Str(r["html"]) ?? "");
                var hash = Convert.ToHexString(SHA256.HashData(bytes));
                var name = k == "page" ? "page" : FrameName(k);
                if (lastCode.TryGetValue(k, out var last) && last.Hash == hash)
                {
                    code.Add(new JsonObject { ["frame"] = k, ["file"] = last.File, ["same"] = true });
                    continue;
                }
                var file = $"{tag} {name}.html.gz";
                Write(Gzip(bytes), file);
                lastCode[k] = (hash, file);
                written.Add(file);
                code.Add(new JsonObject { ["frame"] = k, ["file"] = file, ["same"] = false });
                changed = true;
            }
            string? shotFile = null;
            if (shot != null)
            {
                var hash = Convert.ToHexString(SHA256.HashData(shot));
                if (lastShot is { } ls && ls.Hash == hash) shotFile = ls.File;
                else
                {
                    shotFile = $"{tag} screenshot.jpg";
                    Write(shot, shotFile);
                    lastShot = (hash, shotFile);
                    written.Add(shotFile);
                    changed = true;
                }
            }
            if (!changed) return; // (nothing new to keep: no step)
            Steps = n;
            // each script and style sheet the page loads, once for the whole recording
            var fresh = new List<string>();
            foreach (var (_, r) in reads)
                foreach (var a in (r["assets"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
                {
                    if (Str(a["url"]) is not { } u || assets.ContainsKey(Strip(u)) || fresh.Count >= 40) continue;
                    assets[Strip(u)] = "(reading)";
                    fresh.Add(u);
                }
            if (fresh.Count > 0 && await Fetch(fresh) is JsonArray got)
                foreach (var g in got.OfType<JsonObject>())
                {
                    if (Str(g["url"]) is not { } u) continue;
                    var s = Strip(u);
                    if (Str(g["text"]) is { } text)
                    {
                        var leaf = Uri.TryCreate(s, UriKind.Absolute, out var uu) ? Path.GetFileName(uu.AbsolutePath) : "";
                        leaf = string.Concat(leaf.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));
                        if (leaf.Length > 60) leaf = leaf[..60];
                        var file = $"assets/{(assets.Values.Count(v => v.StartsWith("assets/", StringComparison.Ordinal)) + 1).ToString("000", CultureInfo.InvariantCulture)} {(leaf.Length == 0 ? "file" : leaf)}.gz";
                        Write(Gzip(Encoding.UTF8.GetBytes(text)), file);
                        assets[s] = file;
                        written.Add(file);
                    }
                    else assets[s] = $"(not read: {Str(g["error"]) ?? "unknown"})";
                }
            var main = reads.FirstOrDefault(r => r.Key == "page").Read ?? new JsonObject();
            var data = new JsonObject
            {
                ["step"] = n,
                ["time"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                ["url"] = Strip(Str(main["url"]) ?? ""),
                ["title"] = Str(main["title"]) ?? "",
                ["viewport"] = new JsonObject { ["width"] = Num(main["w"]), ["height"] = Num(main["h"]) },
                ["scroll"] = new JsonObject { ["x"] = Num(main["sx"]), ["y"] = Num(main["sy"]) },
                ["code"] = code,
                ["screenshot"] = shotFile,
                ["frames"] = new JsonArray(reads.Select(r => (JsonNode)new JsonObject
                {
                    ["frame"] = r.Key,
                    ["url"] = Strip(Str(r.Read["url"]) ?? ""),
                    ["title"] = Str(r.Read["title"]) ?? "",
                    ["fields"] = r.Read["fields"]?.DeepClone() ?? new JsonArray(),
                    ["webComponents"] = r.Read["shadows"]?.DeepClone() ?? new JsonArray(),
                    ["framesInIt"] = new JsonArray((r.Read["frames"] as JsonArray ?? new JsonArray()).Select(x => (JsonNode?)Strip(Str(x) ?? "")).ToArray()),
                }).ToArray()),
                ["assets"] = new JsonArray(reads.SelectMany(r => (r.Read["assets"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
                    .Where(a => Str(a["url"]) != null)
                    .Select(a => (JsonNode)new JsonObject { ["url"] = Strip(Str(a["url"])!), ["kind"] = Str(a["kind"]) ?? "", ["file"] = assets.GetValueOrDefault(Strip(Str(a["url"])!), "") })
                    .ToArray()),
            };
            Write(Encoding.UTF8.GetBytes(data.ToJsonString(new JsonSerializerOptions { WriteIndented = true })), $"{tag} data.json");
            Timeline($"{tag}  {DateTime.Now:HH:mm:ss}  {Str(main["title"]) ?? ""}  {Strip(Str(main["url"]) ?? "")}  —  {(written.Count == 0 ? "data" : string.Join(", ", written))}");
            Log.Write($"recorder: step {n} ({string.Join(", ", written.Append($"{tag} data.json"))}), {Bytes} bytes so far");
            Changed?.Invoke();
            if (Steps >= MaxSteps || Bytes >= MaxBytes)
            {
                Timeline($"the recording reached its limit ({MaxSteps} steps or {Size(MaxBytes)}) and stopped");
                busy = false;
                Stop();
            }
        }
        catch (Exception e) { Log.Write("recorder: a step could not be saved: " + e.Message); }
        finally
        {
            busy = false;
            if (again && On) Nudge();
        }
    }

    private string FrameName(string k)
    {
        if (frameNames.TryGetValue(k, out var n)) return n;
        n = $"frame {frameNames.Count + 1}";
        frameNames[k] = n;
        return n;
    }

    /// <summary>The page as a picture: a JPEG at most 1280 pixels wide.</summary>
    private async Task<byte[]?> Picture()
    {
        try
        {
            using var png = new MemoryStream();
            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, png);
            png.Position = 0;
            BitmapSource src = BitmapFrame.Create(png, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            if (src.PixelWidth < 4 || src.PixelHeight < 4) return null;
            if (src.PixelWidth > ShotWidth)
            {
                var s = (double)ShotWidth / src.PixelWidth;
                src = new TransformedBitmap(src, new ScaleTransform(s, s));
            }
            src = new FormatConvertedBitmap(src, PixelFormats.Bgr24, null, 0); // (no alpha: a JPEG has none)
            var jpeg = new JpegBitmapEncoder { QualityLevel = 60 };
            jpeg.Frames.Add(BitmapFrame.Create(src));
            using var out_ = new MemoryStream();
            jpeg.Save(out_);
            return out_.ToArray();
        }
        catch (Exception e) { Log.Write("recorder: no picture: " + e.Message); return null; }
    }

    /// <summary>A script run in the page (null) or a frame, its answer as JSON.</summary>
    private async Task<JsonNode?> Run(string js, CoreWebView2Frame? frame)
    {
        try
        {
            string said;
            if (frame == null) said = await core.ExecuteScriptAsync(js);
            else if (frame.IsDestroyed() == 0) said = await frame.ExecuteScriptAsync(js);
            else return null;
            return string.IsNullOrEmpty(said) || said == "null" ? null : JsonNode.Parse(said);
        }
        catch { return null; }
    }

    /// <summary>Each address read in the page with its own session (the browser's own protocol waits for the reads).</summary>
    private async Task<JsonNode?> Fetch(List<string> urls)
    {
        try
        {
            var expression = "(async function (urls) {\n" + FetchScript + "\n})(" + JsonSerializer.Serialize(urls) + ")";
            var said = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new { expression, awaitPromise = true, returnByValue = true }));
            return JsonNode.Parse(said)?["result"]?["value"];
        }
        catch (Exception e) { Log.Write("recorder: the scripts and style sheets could not be read: " + e.Message); return null; }
    }

    // ---- files ----

    private void Write(string text, string name) => Write(Encoding.UTF8.GetBytes(text), name);

    private void Write(byte[] bytes, string name)
    {
        if (folder == null) return;
        try
        {
            File.WriteAllBytes(Path.Combine(folder, name.Replace('/', Path.DirectorySeparatorChar)), bytes);
            Bytes += bytes.Length;
        }
        catch (Exception e) { Log.Write($"recorder: {name} could not be written: {e.Message}"); }
    }

    private void Timeline(string line)
    {
        if (folder == null) return;
        try
        {
            var b = Encoding.UTF8.GetBytes(line + "\n");
            using var f = new FileStream(Path.Combine(folder, "timeline.txt"), FileMode.Append, FileAccess.Write);
            f.Write(b);
            Bytes += b.Length;
        }
        catch { /* the folder gone */ }
    }

    private static byte[] Gzip(byte[] d)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.SmallestSize, leaveOpen: true)) gz.Write(d);
        return ms.ToArray();
    }

    public static string Size(long bytes) => bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.#} KB" : $"{bytes / 1048576.0:0.#} MB";

    private static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static long Num(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? (long)Math.Round(d) : 0;

    /// <summary>An address without its query or fragment (a launch's tokens ride in them).</summary>
    public static string Strip(string u)
    {
        if (u.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return "data:…";
        if (!Uri.TryCreate(u, UriKind.Absolute, out var x)) return u.Split('?', '#')[0];
        return x.GetLeftPart(UriPartial.Path);
    }

    private static string Readme(string name) => $"""
        Simpl for Windows {Updates.Version}: a recording of “{name}”, started {DateTime.Now.ToString("F", CultureInfo.CurrentCulture)}.

        Each time the page changed (it loaded, what it shows changed, or a field was changed) and then settled, a step
        was saved, numbered in order. timeline.txt lists them.

          NNNN page.html.gz     the page's code at that step (its HTML, with any web component's own tree), gzipped
                                (open it with 7-Zip, or Windows' own Extract All). Only written when it changed.
          NNNN frame N.html.gz  a frame in the page whose code changed.
          NNNN screenshot.jpg   the page at that step. Only written when it changed.
          NNNN data.json        what the page held: its address, title, size, scroll, form fields (a password's or a
                                token's value left out) and which files hold its code and picture at that step.
          assets\               each script and style sheet the page loaded, once, gzipped.

        Addresses are written without their queries, where a tool's launch tokens ride.
        """;

    // ---- the page's side ----

    /// <summary>In each frame: a change to what it shows (not a style an animation moves), or a field changed, said to the
    /// app once the frame has been still for 0.7 s (or after 3 s of steady change), and once as it loads; stopped by
    /// __simplRecStop.</summary>
    private const string Watch = """
        (function () {
          if (window.__simplRec) return;
          var w = window.chrome && window.chrome.webview;
          if (!w || !w.postMessage) return;
          window.__simplRec = true;
          var t = 0, first = 0, obs = null;
          var say = function (why) { try { w.postMessage('simplRecord:' + why); } catch (e) { stop(); } };
          var soon = function () {
            clearTimeout(t);
            if (!first) first = Date.now();
            t = setTimeout(function () { first = 0; say('change'); }, Date.now() - first > 3000 ? 0 : 700);
          };
          var stop = function () { clearTimeout(t); if (obs) obs.disconnect(); removeEventListener('change', soon, true); removeEventListener('input', soon, true); window.__simplRec = false; };
          obs = new MutationObserver(function (list) {
            for (var i = 0; i < list.length; i++) { if (list[i].type !== 'attributes' || list[i].attributeName !== 'style') { soon(); return; } }
          });
          obs.observe(document.documentElement, { subtree: true, childList: true, characterData: true, attributes: true });
          addEventListener('change', soon, true);
          addEventListener('input', soon, true);
          window.__simplRecStop = stop;
          say('load');
        })();
        """;

    /// <summary>In one frame: its code, what it holds, and the scripts and style sheets it loads (their addresses).</summary>
    private const string Collect = """
        (function () {
          var shadows = [];
          var walk = function (root, path) {
            [].forEach.call(root.querySelectorAll('*'), function (el) {
              if (!el.shadowRoot) return;
              var p = path + ' > ' + el.tagName.toLowerCase() + (el.id ? '#' + el.id : '');
              shadows.push({ host: p, html: el.shadowRoot.innerHTML });
              walk(el.shadowRoot, p);
            });
          };
          walk(document, '');
          var html = '<!doctype html>\n' + document.documentElement.outerHTML;
          shadows.forEach(function (s) { html += '\n<!-- simpl: web component\'s own tree: ' + s.host.replace(/--/g, '- -') + ' -->\n' + s.html; });
          var secret = /pass|token|csrf|jwt|auth|session|secret|nonce|key|code_verifier/i;
          var fields = [].slice.call(document.querySelectorAll('input, select, textarea'), 0, 300).map(function (f) {
            var type = (f.type || f.tagName || '').toLowerCase();
            var name = f.name || f.id || f.getAttribute('aria-label') || '';
            if (type === 'checkbox' || type === 'radio') return { name: name, type: type, checked: !!f.checked, value: String(f.value || '').slice(0, 200) };
            var hide = type === 'password' || (type === 'hidden' && secret.test(name));
            return { name: name, type: type, value: hide ? '(left out)' : String(f.value || '').slice(0, 1000) };
          });
          var assets = [], had = {};
          var add = function (kind, u) { if (u && !had[u] && !/^(data|blob):/.test(u)) { had[u] = 1; assets.push({ kind: kind, url: u }); } };
          [].forEach.call(document.querySelectorAll('script[src]'), function (s) { add('script', s.src); });
          [].forEach.call(document.querySelectorAll('link[rel~="stylesheet"][href]'), function (l) { add('style sheet', l.href); });
          return { url: location.href, title: document.title, html: html, shadows: shadows.map(function (s) { return s.host; }), fields: fields, assets: assets,
                   frames: [].map.call(document.querySelectorAll('iframe, frame'), function (f) { return f.src || ''; }),
                   w: innerWidth, h: innerHeight, sx: Math.round(scrollX), sy: Math.round(scrollY) };
        })()
        """;

    /// <summary>In the page: each address read with the page's own session (3 MB of each at most, 15 s at most).</summary>
    private const string FetchScript = """
        var read = function (u) {
          var late = new Promise(function (_, no) { setTimeout(function () { no(new Error('timed out')); }, 15000); });
          return Promise.race([fetch(u).then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.text(); }), late])
            .then(function (t) { return { url: u, text: t.length > 3145728 ? t.slice(0, 3145728) + '\n/* simpl: cut at 3 MB */' : t }; },
                  function (e) { return { url: u, error: String((e && e.message) || e) }; });
        };
        return await Promise.all(urls.map(read));
        """;
}
