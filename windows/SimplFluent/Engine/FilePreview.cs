using System.IO;
using System.Net;
using System.Net.Http;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Simpl.Support;

namespace Simpl.Engine;

/// <summary>
/// A file of the school's, opened: fetched with the signed-in session (the web view's own cookies) and shown in Simpl's
/// preview window (Shell/FileViewWindow.cs, the Mac's 1.3.191) before anything is downloaded. While it comes, the
/// window's foot says so (<see cref="Status"/>). (0.3.85) A file previewed is fetched into a folder of Simpl's own
/// (<see cref="Previews"/>, cleared of what is a day old), not Downloads: only the preview's Download, and Open in its
/// app, put a copy in Downloads (<see cref="KeepCopy"/>) — every file opened went there, and straight to its app, before.
/// </summary>
public sealed class FilePreview : Observable
{
    public static readonly FilePreview Shared = new();

    private string? status;
    /// <summary>What the toast at the window's foot says ("Opening Syllabus.pdf…"), or null.</summary>
    public string? Status { get => status; private set => Set(ref status, value); }

    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true, UseCookies = false, AutomaticDecompression = DecompressionMethods.All });

    public static void Open(Uri url, string name, WebView2 from) => _ = Shared.Fetch(url, name, from);

    private async Task Fetch(Uri url, string name, WebView2 from)
    {
        var shown = string.IsNullOrWhiteSpace(name) ? "the file" : name;
        Status = $"Opening {shown}…";
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (from.CoreWebView2 != null)
            {
                var cookies = await from.CoreWebView2.CookieManager.GetCookiesAsync(url.ToString());
                if (cookies.Count > 0) req.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}")));
                req.Headers.TryAddWithoutValidation("User-Agent", from.CoreWebView2.Settings.UserAgent);
            }
            using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (!res.IsSuccessStatusCode) throw new Exception($"The school answered {(int)res.StatusCode} {res.ReasonPhrase}.");
            var fileName = res.Content.Headers.ContentDisposition?.FileNameStar ?? res.Content.Headers.ContentDisposition?.FileName?.Trim('"');
            if (string.IsNullOrWhiteSpace(fileName)) fileName = string.IsNullOrWhiteSpace(name) ? Path.GetFileName(url.AbsolutePath) : name;
            var path = Path.Combine(Previews(), Clean(fileName));
            await using (var outFile = File.Create(path)) await res.Content.CopyToAsync(outFile);
            Status = null;
            Shell.FileViewWindow.Show(path, string.IsNullOrWhiteSpace(name) ? fileName : name);
        }
        catch (Exception e)
        {
            Status = $"{shown} could not be opened. {e.Message}";
            await Task.Delay(4000);
            Status = null;
        }
    }

    /// <summary>A download the page started itself: into Simpl's own folder, then shown in the preview window.</summary>
    public static void Downloading(CoreWebView2DownloadStartingEventArgs e)
    {
        var name = Path.GetFileName(e.ResultFilePath);
        e.ResultFilePath = Path.Combine(Previews(), Clean(name));
        var op = e.DownloadOperation;
        Shared.Status = $"Opening {name}…";
        op.StateChanged += (_, _) =>
        {
            if (op.State == CoreWebView2DownloadState.InProgress) return;
            Shared.Status = null;
            if (op.State == CoreWebView2DownloadState.Completed)
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Shell.FileViewWindow.Show(op.ResultFilePath, name));
        };
    }

    /// <summary>
    /// (0.3.85) A copy of a file previewed kept in Downloads, under a name not yet taken: the preview's Download (the
    /// window's foot says so a moment), or — <paramref name="quiet"/> — its Open in its app. Null when it cannot be (the
    /// foot saying why).
    /// </summary>
    public static string? KeepCopy(string file, string name, bool quiet = false)
    {
        try
        {
            var dest = Path.GetDirectoryName(file) is { } dir && string.Equals(Path.GetFullPath(dir).TrimEnd('\\'), Path.GetFullPath(Downloads).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                ? file
                : Unique(Path.Combine(Downloads, Path.GetFileName(file)));
            if (dest != file) File.Copy(file, dest);
            if (!quiet) _ = Shared.Say($"Saved {Path.GetFileName(dest)} to Downloads");
            return dest;
        }
        catch (Exception e)
        {
            _ = Shared.Say($"{name} could not be saved to Downloads. {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// The screenshot suite's file (--file-view): the notes' PDF, or — <paramref name="card"/> — a Word document no web
    /// view draws, put in Simpl's own folder as a file of the school's opened is, and shown in the preview window.
    /// </summary>
    internal static void Sample(bool card)
    {
        var dir = Previews();
        string path;
        if (card)
        {
            path = Path.Combine(dir, "Lab report template.docx");
            File.WriteAllBytes(path, new byte[48 * 1024]);
        }
        else if (Tools.FileKit.Sample() is { } pdf)
        {
            path = Path.Combine(dir, "Week 1 notes.pdf");
            File.Copy(pdf, path, overwrite: true);
        }
        else return;
        Shell.FileViewWindow.Show(path, Path.GetFileName(path));
    }

    /// <summary>The foot says something for a moment.</summary>
    private async Task Say(string words)
    {
        Status = words;
        await Task.Delay(3000);
        if (Status == words) Status = null;
    }

    public static string Downloads
    {
        get
        {
            var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    private static bool previewsCleared;

    /// <summary>(0.3.85) Simpl's own folder for a file fetched to be previewed (…\Simpl\Previews): each in a folder of its
    /// own, so it keeps its own name; what is a day old cleared away the first time it is asked for in a run.</summary>
    public static string Previews()
    {
        var root = Path.Combine(AppPaths.Root, "Previews");
        if (!previewsCleared)
        {
            previewsCleared = true;
            try
            {
                foreach (var dir in Directory.Exists(root) ? Directory.GetDirectories(root) : Array.Empty<string>())
                    if (Directory.GetLastWriteTimeUtc(dir) < DateTime.UtcNow.AddDays(-1))
                        try { Directory.Delete(dir, recursive: true); } catch { /* still open in its app */ }
            }
            catch { /* none yet */ }
        }
        var one = Path.Combine(root, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(one);
        return one;
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
}
