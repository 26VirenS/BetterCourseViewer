using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Simpl.Support;

namespace Simpl.Engine;

/// <summary>
/// A file of the school's, opened: fetched with the signed-in session (the web view's own cookies), kept in Downloads
/// and opened in the app Windows has for it — the Mac's Quick Look, the Windows way. While it comes, the window's
/// foot says so (<see cref="Status"/>).
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
            var path = Unique(Path.Combine(Downloads, Clean(fileName)));
            await using (var outFile = File.Create(path)) await res.Content.CopyToAsync(outFile);
            Status = null;
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            Status = $"{shown} could not be opened. {e.Message}";
            await Task.Delay(4000);
            Status = null;
        }
    }

    /// <summary>A download the page started itself: into Downloads, then opened.</summary>
    public static void Downloading(CoreWebView2DownloadStartingEventArgs e)
    {
        var name = Path.GetFileName(e.ResultFilePath);
        e.ResultFilePath = Unique(Path.Combine(Downloads, Clean(name)));
        var op = e.DownloadOperation;
        Shared.Status = $"Downloading {name}…";
        op.StateChanged += (_, _) =>
        {
            if (op.State == CoreWebView2DownloadState.InProgress) return;
            Shared.Status = null;
            if (op.State == CoreWebView2DownloadState.Completed)
                try { Process.Start(new ProcessStartInfo(op.ResultFilePath) { UseShellExecute = true }); } catch { }
        };
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
