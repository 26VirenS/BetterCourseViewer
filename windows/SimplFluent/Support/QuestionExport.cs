using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Simpl.Support;

/// <summary>
/// (0.3.107) Export questions, in a publisher's popup (Macmillan Learning, ALEKS, Knewton Alta): a picture of every
/// question the activity lets the popup open, in one zip in Downloads — "Week 3 Homework questions.zip", holding
/// "Question 1.png", "Question 2.png" and on. Macmillan's assignment lists its questions, so each is opened in turn and
/// pictured as it is on screen (the popup opens the one that was open again after); ALEKS and Alta give one question at
/// a time (each comes from the answer before), so theirs is the one on screen. A picture is the page as WebView2 shows
/// it, laid on the popup's own ground.
/// (0.3.108) The whole question, however far it scrolls: a question often runs on past what the popup shows — a
/// Macmillan module of a fixed height that scrolls inside itself, ALEKS's or Alta's page that scrolls — and a picture of
/// the popup showed only what was in sight. extension/tools/question-shot.js, in every frame of the popup's page, finds
/// the place the question scrolls in; here it is scrolled a view at a time from its top, each view pictured, and the
/// views laid one under the other: the view whole at the top, then the place's part of each view at its depth in the
/// question, then what was under the place moved down by as much as there was more. What floats over the place
/// (Macmillan's ↓) is hidden while the pictures are taken, and the page is put back as it was.
/// </summary>
internal static class QuestionExport
{
    /// <summary>What came of it: the zip saved (null if none), how many were pictured of how many, and why there is none.</summary>
    public sealed record Result(string? Zip, int Pictured, int Of, IReadOnlyList<int> Missed, string? Why);

    /// <summary>One question's picture: PNG, how many views were laid together (1: nothing scrolls), its height and the
    /// view's, in pixels.</summary>
    private sealed record Picture(byte[] Png, int Views, int Height, int ViewHeight);

    private static string? shotSource;

    /// <summary>(0.3.108) The page's half of a whole question's picture, in every frame of the popup's page from its start.</summary>
    public static async Task AddScript(CoreWebView2 core)
    {
        try
        {
            shotSource ??= File.ReadAllText(Path.Combine(Engine.ScriptBundle.ExtensionDir, "tools", "question-shot.js"));
            await core.AddScriptToExecuteOnDocumentCreatedAsync(shotSource);
        }
        catch (Exception e) { Log.Write("export questions: the page's script could not be added: " + e.Message); }
    }

    /// <summary>Pictures each of <paramref name="questions"/> (opening each first with <paramref name="open"/>; none: the
    /// one on screen), then zips them into Downloads. <paramref name="progress"/> hears (pictured so far, of how many).</summary>
    public static async Task<Result> Run(WebView2 web, string title, string tool, IReadOnlyList<int> questions,
        Func<int, Task<bool>> open, Color ground, Action<int, int>? progress = null)
    {
        var list = questions.Count == 0 ? new List<int> { 0 } : questions.ToList();
        var name = FileName(title);
        var work = Path.Combine(Path.GetTempPath(), "simpl-questions-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(work, name + " questions");
        var missed = new List<int>();
        // (the picture suite's picture waits for it: each question, pictured whole, takes a second or two — the picture
        // was taken, and the app closed, part way through)
        Shot.Began();
        try
        {
            Directory.CreateDirectory(folder);
            var pictured = 0;
            for (var i = 0; i < list.Count; i++)
            {
                progress?.Invoke(i, list.Count);
                var q = list[i];
                if (q > 0 && !await open(q)) { missed.Add(q); continue; }
                var shot = await Whole(web, ground);
                if (shot == null) { missed.Add(q); continue; }
                await File.WriteAllBytesAsync(Path.Combine(folder, q > 0 ? $"Question {q}.png" : "Question.png"), shot.Png);
                pictured++;
                if (shot.Views > 1)
                    Log.Write($"export questions: {(q > 0 ? $"question {q}" : "question")} pictured whole, {shot.Views} views laid together ({shot.Height} px tall, the view {shot.ViewHeight})");
            }
            progress?.Invoke(list.Count, list.Count);
            if (pictured == 0) return Failed("No question could be pictured.", tool, list.Count, missed);
            var zip = Unique(Path.Combine(Engine.FilePreview.Downloads, $"{name} questions.zip"));
            await Task.Run(() => ZipFile.CreateFromDirectory(folder, zip, CompressionLevel.Optimal, includeBaseDirectory: true));
            Log.Write($"export questions: {pictured} of {list.Count} pictured, saved to {Path.GetFileName(zip)} ({tool})"
                + (missed.Count > 0 ? $" — not pictured: {string.Join(", ", missed)}" : ""));
            return new Result(zip, pictured, list.Count, missed, null);
        }
        catch (Exception e)
        {
            return Failed($"The pictures could not be saved: {e.Message}", tool, list.Count, missed);
        }
        finally
        {
            Shot.Ended();
            try { Directory.Delete(work, recursive: true); } catch { /* (the temporary folder: Windows clears it in time) */ }
        }
    }

    private static Result Failed(string why, string tool, int of, IReadOnlyList<int> missed)
    {
        Log.Write($"export questions: none saved ({tool}): {why}");
        return new Result(null, 0, of, missed, why);
    }

    // ---- the whole question ----

    /// <summary>What a frame said of its place: where it is in the top frame (CSS pixels), how far it scrolls, the top
    /// frame's width.</summary>
    private sealed class Said
    {
        public string frame { get; set; } = "";
        public double? x { get; set; }
        public double? y { get; set; }
        public double? w { get; set; }
        public double? h { get; set; }
        public double? sh { get; set; }
        public double? ch { get; set; }
        public double? st { get; set; }
        public double? vw { get; set; }
    }

    private static async Task<Picture?> Whole(WebView2 web, Color ground)
    {
        if (web.CoreWebView2 is not { } c) return null;
        var id = "q" + Guid.NewGuid().ToString("N")[..8];
        await Run(c, $"window.__simplShot && window.__simplShot({{ id: '{id}f', op: 'find' }})");
        await Task.Delay(400); // (every frame's answer in: each says only if it has a place)
        var found = await SaidTo(c, id + "f");
        // (of the places, those at least a quarter as large as the largest; of them, the one with the most out of sight)
        var most = found.Count == 0 ? 0 : found.Max(f => (f.w ?? 0) * (f.h ?? 0));
        var place = found.Where(f => (f.w ?? 0) * (f.h ?? 0) >= most / 4)
            .OrderByDescending(f => (f.sh ?? 0) - (f.ch ?? 0)).ThenByDescending(f => (f.w ?? 0) * (f.h ?? 0)).FirstOrDefault();
        if (place == null || (place.sh ?? 0) - (place.ch ?? 0) <= 8) return await Single(c, ground);
        var frame = new string(place.frame.Where(char.IsLetterOrDigit).ToArray());
        await Run(c, $"window.__simplShot({{ id: '{id}b', op: 'begin', frame: '{frame}' }})");
        var begun = await Heard(c, id + "b");
        if (begun?.sh is not { } sh || begun.ch is not { } ch)
        {
            await Run(c, $"window.__simplShot({{ id: '{id}e', op: 'end', frame: '{frame}' }})");
            return await Single(c, ground);
        }
        var views = new List<(BitmapSource Image, double St)>();
        await Task.Delay(150);
        if (await View(c) is { } first) views.Add((first, begun.st ?? 0));
        var st = begun.st ?? 0;
        for (var k = 1; views.Count > 0 && st < sh - ch - 1 && k <= 40; k++)
        {
            // (a view a little short of the place's height on from the last: their rounded corners overlap, not shown)
            var y = Math.Min(st + ch - 64, sh - ch);
            await Run(c, $"window.__simplShot({{ id: '{id}s{k}', op: 'step', frame: '{frame}', y: {Math.Round(y)} }})");
            var s = await Heard(c, $"{id}s{k}");
            if (s?.st is not { } now || now <= st) break;
            await Task.Delay(150);
            if (await View(c) is not { } image) break;
            views.Add((image, now));
            st = now;
        }
        await Run(c, $"window.__simplShot({{ id: '{id}e', op: 'end', frame: '{frame}' }})");
        return views.Count == 0 ? null : Lay(views, begun, ground);
    }

    private static async Task<Picture?> Single(CoreWebView2 c, Color ground) =>
        await View(c) is { } image ? Lay(new() { (image, 0) }, null, ground) : null;

    /// <summary>The page as it is on screen (CapturePreviewAsync), in its own pixels.</summary>
    private static async Task<BitmapSource?> View(CoreWebView2 c)
    {
        using var raw = new MemoryStream();
        try { await c.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, raw); } catch { return null; }
        if (raw.Length == 0) return null;
        raw.Position = 0;
        var shot = BitmapFrame.Create(raw, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return shot.PixelWidth == 0 || shot.PixelHeight == 0 ? null : shot;
    }

    /// <summary>The views laid one under the other on the ground (or the one view as it is, <paramref name="p"/> null).</summary>
    private static Picture? Lay(List<(BitmapSource Image, double St)> views, Said? p, Color ground)
    {
        var (first, top) = views[0];
        int wide = first.PixelWidth, high = first.PixelHeight;
        double s = 1, more = 0;
        var stitched = p is { vw: > 0, x: { }, y: { }, w: { }, h: { }, ch: { } } && views.Count > 1;
        if (stitched)
        {
            s = wide / p!.vw!.Value;
            more = views[^1].St - top;
        }
        var tall = high + (int)Math.Round(more * s);
        if (tall > 30000) return null;
        var brush = new SolidColorBrush(ground);
        brush.Freeze();
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(brush, null, new Rect(0, 0, wide, tall));
            // (a part of a view, by pixels from its top, put at a depth from the picture's top — the ground under it first,
            // so a clear page is laid on the ground once, not on what was there)
            void Put(BitmapSource image, double px, double py, double pw, double ph, double depth)
            {
                var r = new Int32Rect((int)Math.Round(px), (int)Math.Round(py), (int)Math.Round(pw), (int)Math.Round(ph));
                r.Width = Math.Min(r.Width, image.PixelWidth - r.X);
                r.Height = Math.Min(r.Height, image.PixelHeight - r.Y);
                if (r.X < 0 || r.Y < 0 || r.Width <= 0 || r.Height <= 0) return;
                var to = new Rect(r.X, Math.Round(depth), r.Width, r.Height);
                dc.DrawRectangle(brush, null, to);
                dc.DrawImage(new CroppedBitmap(image, r), to);
            }
            if (!stitched)
            {
                Put(first, 0, 0, wide, high, 0);
            }
            else
            {
                double x = p!.x!.Value, y = p.y!.Value, w = p.w!.Value, h = p.h!.Value, ch = p.ch!.Value;
                double Px(double v) => Math.Round(v * s);
                // the view whole above the place's foot; what is under the place, moved down by as much as there was more
                Put(first, 0, 0, wide, Px(y + h), 0);
                Put(first, 0, Px(y + h), wide, high - Px(y + h), Px(y + h + more));
                // the place's part of each view at its depth (each after the first from a little above where the one
                // before ended: the place's rounded corners cut each view's top and foot)
                double? before = null;
                foreach (var (image, st) in views)
                {
                    var from = before is { } b ? Math.Max(0, b + ch - 32 - st) : 0;
                    Put(image, Px(x), Px(y + from), Px(x + w) - Px(x), Px(y + h) - Px(y + from), Px(y + st - top + from));
                    before = st;
                }
            }
        }
        var bitmap = new RenderTargetBitmap(wide, tall, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        png.Save(output);
        return new Picture(output.ToArray(), stitched ? views.Count : 1, tall, high);
    }

    /// <summary>A script in the top frame (nothing if it fails: a page on its way out).</summary>
    private static async Task<string?> Run(CoreWebView2 c, string js)
    {
        try
        {
            var run = c.ExecuteScriptAsync(js);
            return await Task.WhenAny(run, Task.Delay(3000)) == run ? await run : null;
        }
        catch { return null; }
    }

    /// <summary>What the frames have said to the ask <paramref name="id"/> so far.</summary>
    private static async Task<List<Said>> SaidTo(CoreWebView2 c, string id)
    {
        var json = await Run(c, $"window.__simplShotSaid ? window.__simplShotSaid('{id}') : '[]'");
        try
        {
            var inner = json == null ? null : JsonSerializer.Deserialize<string>(json);
            return inner == null ? new() : JsonSerializer.Deserialize<List<Said>>(inner) ?? new();
        }
        catch { return new(); }
    }

    /// <summary>The frame's answer to the ask <paramref name="id"/>, once it has given it (three seconds at most).</summary>
    private static async Task<Said?> Heard(CoreWebView2 c, string id)
    {
        for (var i = 0; i < 60; i++)
        {
            if ((await SaidTo(c, id)).FirstOrDefault(s => s.x != null) is { } said) return said;
            await Task.Delay(50);
        }
        return null;
    }

    /// <summary>The assignment's name made safe for a file name.</summary>
    public static string FileName(string title)
    {
        var bad = Path.GetInvalidFileNameChars();
        var s = new string(title.Where(ch => !bad.Contains(ch) && !char.IsControl(ch)).ToArray()).Trim().Trim('.').Trim();
        if (s.Length > 80) s = s[..80].Trim();
        return s.Length == 0 ? "Simpl" : s;
    }

    /// <summary>The path itself, or "name (2).zip" and on while one is there.</summary>
    private static string Unique(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path) ?? "";
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var n = 2; ; n++)
        {
            var p = Path.Combine(dir, $"{stem} ({n}){ext}");
            if (!File.Exists(p)) return p;
        }
    }
}
