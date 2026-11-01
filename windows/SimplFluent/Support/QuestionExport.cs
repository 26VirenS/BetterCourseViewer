using System.IO;
using System.IO.Compression;
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
/// </summary>
internal static class QuestionExport
{
    /// <summary>What came of it: the zip saved (null if none), how many were pictured of how many, and why there is none.</summary>
    public sealed record Result(string? Zip, int Pictured, int Of, IReadOnlyList<int> Missed, string? Why);

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
        try
        {
            Directory.CreateDirectory(folder);
            var pictured = 0;
            for (var i = 0; i < list.Count; i++)
            {
                progress?.Invoke(i, list.Count);
                var q = list[i];
                if (q > 0 && !await open(q)) { missed.Add(q); continue; }
                var png = await Picture(web, ground);
                if (png == null) { missed.Add(q); continue; }
                await File.WriteAllBytesAsync(Path.Combine(folder, q > 0 ? $"Question {q}.png" : "Question.png"), png);
                pictured++;
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
            try { Directory.Delete(work, recursive: true); } catch { /* (the temporary folder: Windows clears it in time) */ }
        }
    }

    private static Result Failed(string why, string tool, int of, IReadOnlyList<int> missed)
    {
        Log.Write($"export questions: none saved ({tool}): {why}");
        return new Result(null, 0, of, missed, why);
    }

    /// <summary>The page as it is on screen (CapturePreviewAsync), laid on <paramref name="ground"/>, as PNG.</summary>
    private static async Task<byte[]?> Picture(WebView2 web, Color ground)
    {
        if (web.CoreWebView2 is not { } c) return null;
        using var raw = new MemoryStream();
        try { await c.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, raw); } catch { return null; }
        if (raw.Length == 0) return null;
        raw.Position = 0;
        var shot = BitmapFrame.Create(raw, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        int w = shot.PixelWidth, h = shot.PixelHeight;
        if (w == 0 || h == 0) return null;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(ground), null, new Rect(0, 0, w, h));
            dc.DrawImage(shot, new Rect(0, 0, w, h));
        }
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        png.Save(output);
        return output.ToArray();
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
