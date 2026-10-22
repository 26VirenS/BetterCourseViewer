using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Simpl.Support;

namespace Simpl.Engine;

/// <summary>
/// (0.3.55) The school's sign-in kept across a restart. Canvas's own session is a session cookie unless "Stay signed in"
/// was ticked on its page — and so is a school's sign-in service's — and WebView2 drops every session cookie when Simpl
/// closes: each restart, an update's above all, had everyone signing in again. The session cookies of Simpl's own
/// profile are written here (session.bin, sealed with DPAPI for this Windows account, as the sign-in vault is) after a
/// page loads and every five minutes, and put back before the first page of the next start. Kept 30 days at the most,
/// and gone at Sign Out and Erase All; the school still ends a session when it likes, and then it is signed in again as
/// before.
/// </summary>
internal static class SessionKeep
{
    private sealed record Kept(string Name, string Value, string Domain, string Path, bool Secure, bool HttpOnly, int SameSite);
    private sealed record Saved(DateTime At, List<Kept> Cookies);

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Simpl session cookies 1");
    private static readonly TimeSpan MostAge = TimeSpan.FromDays(30);
    private static string FilePath => Path.Combine(AppPaths.Root, "session.bin");
    private static string? lastWritten;
    private static bool saving;

    /// <summary>The session cookies as they are now, written if they changed.</summary>
    public static async Task SaveAsync(CoreWebView2 core)
    {
        if (saving) return;
        saving = true;
        try
        {
            var all = await core.CookieManager.GetCookiesAsync("");
            var kept = all.Where(c => c.IsSession && !string.IsNullOrEmpty(c.Value))
                .Select(c => new Kept(c.Name, c.Value, c.Domain, c.Path, c.IsSecure, c.IsHttpOnly, (int)c.SameSite))
                .OrderBy(k => k.Domain).ThenBy(k => k.Name).ToList();
            if (kept.Count == 0) return; // (signed out, or a page with none: what was kept stays until Sign Out says otherwise)
            var text = JsonSerializer.Serialize(kept);
            if (text == lastWritten) return;
            var sealedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Saved(DateTime.UtcNow, kept))), Entropy, DataProtectionScope.CurrentUser);
            var tmp = FilePath + ".tmp";
            await File.WriteAllBytesAsync(tmp, sealedBytes);
            File.Move(tmp, FilePath, true);
            lastWritten = text;
        }
        catch (Exception e) { Log.Write("session: not kept: " + e.Message); }
        finally { saving = false; }
    }

    /// <summary>What the last run kept, put back before the first page loads (none that the profile still has).</summary>
    public static async Task RestoreAsync(CoreWebView2 core)
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var saved = JsonSerializer.Deserialize<Saved>(Encoding.UTF8.GetString(ProtectedData.Unprotect(await File.ReadAllBytesAsync(FilePath), Entropy, DataProtectionScope.CurrentUser)));
            if (saved == null || DateTime.UtcNow - saved.At > MostAge) { Clear(); return; }
            var have = await core.CookieManager.GetCookiesAsync("");
            var n = 0;
            foreach (var k in saved.Cookies)
            {
                if (have.Any(h => h.Name == k.Name && h.Domain == k.Domain && h.Path == k.Path)) continue;
                var c = core.CookieManager.CreateCookie(k.Name, k.Value, k.Domain, k.Path);
                c.IsSecure = k.Secure;
                c.IsHttpOnly = k.HttpOnly;
                c.SameSite = (CoreWebView2CookieSameSiteKind)k.SameSite;
                core.CookieManager.AddOrUpdateCookie(c);
                n++;
            }
            Log.Write($"session: {n} cookie(s) kept from the last run put back");
        }
        catch (Exception e)
        {
            // (sealed for another Windows account, or unreadable: forgotten, and signed in as before)
            Log.Write("session: what was kept could not be read: " + e.Message);
            Clear();
        }
    }

    /// <summary>Signed out, or everything erased: nothing kept.</summary>
    public static void Clear()
    {
        lastWritten = null;
        try { File.Delete(FilePath); } catch { }
        try { File.Delete(FilePath + ".tmp"); } catch { }
    }
}
