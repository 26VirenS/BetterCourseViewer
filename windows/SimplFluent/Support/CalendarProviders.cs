using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Simpl.Support;

/// <summary>
/// (0.3.81) The kinds of calendar account Sync signs in to, and what each is read and written through (the Mac's
/// CalendarProviders.swift, 1.3.184):
/// <list type="bullet">
/// <item>iCloud Calendar — the student's Apple ID and an app-specific password (made at account.apple.com), over CalDAV
/// (caldav.icloud.com): Windows has no Apple Calendar to ask, and Apple offers no sign-in page to anyone but itself.</item>
/// <item>Google Calendar — the student signs in with Google in the browser (OAuth 2 with PKCE, back to Simpl on this PC's
/// own loopback address); read with calendar.readonly, written only in a calendar Simpl makes there.</item>
/// <item>Outlook — a Microsoft account, personal or a school's (Microsoft 365), the same way (Microsoft Graph).</item>
/// </list>
/// What is read comes in as a linked calendar's events (PulledEvent, background.js calFetch's shape); what is written is
/// Simpl's own calendar (content/app/feed.js events: the work, the student's own tasks and the plans), in a calendar
/// named Simpl in that account.
/// </summary>
public static class CalendarKinds
{
    public const string ICloud = "icloud", Google = "google", Microsoft = "microsoft";
    public static readonly string[] All = { ICloud, Google, Microsoft };

    public static string Name(string kind) => kind switch { ICloud => "iCloud Calendar", Google => "Google Calendar", _ => "Outlook" };
    public static string Action(string kind) => kind switch { ICloud => "Sign In with iCloud", Google => "Sign In with Google", _ => "Sign In with Microsoft" };
    public static string Subtitle(string kind) => kind switch { ICloud => "Your Apple ID and an app password", Google => "Google Calendar", _ => "Outlook, Microsoft 365, school accounts" };
    public static string Tint(string kind) => kind switch { ICloud => "#0a84ff", Google => "#4285f4", _ => "#0078d4" };

    /// <summary>Whether this build can sign in to it: Google's and Microsoft's need this app's client, made in their
    /// consoles (CalendarKeys); iCloud needs nothing.</summary>
    public static bool Available(string kind) => kind == ICloud || OAuthClient.Of(kind) != null;
}

/// <summary>(0.3.81) This app's sign-in clients — public identifiers: Google's desktop OAuth client (with the "secret"
/// Google gives a desktop client, which it says is not one), and a Microsoft Entra app registration (a public client with
/// the redirect http://localhost). Empty: that sign-in is not offered yet.</summary>
public static class CalendarKeys
{
    public const string Google = "";
    public const string GoogleDesktopSecret = "";
    public const string Microsoft = "";
}

public enum CalendarFailure { Cancelled, SignedOut, NotFound, Other }

/// <summary>Why a calendar could not be read or written, in the student's words.</summary>
public sealed class CalendarException(CalendarFailure kind, string message) : Exception(message)
{
    public CalendarFailure Kind { get; } = kind;
    public static CalendarException SignedOut() => new(CalendarFailure.SignedOut, "Sign in again: this account's sign-in has ended.");
    public static CalendarException NotFound() => new(CalendarFailure.NotFound, "That calendar is no longer there.");
    public static CalendarException Cancelled() => new(CalendarFailure.Cancelled, "Sign-in was cancelled.");
}

/// <summary>A calendar in an account.</summary>
public sealed class AccountCalendar
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#8e8e93";
    /// <summary>Where it lives in the account, when that says something; else null.</summary>
    public string? Group { get; set; }
    /// <summary>Shown on Simpl's calendar (and counted as busy when Simpl finds a free time).</summary>
    public bool On { get; set; }
}

/// <summary>An event read from an account's calendar, as the page keeps a linked calendar's (background.js calFetch):
/// start and end in ms; a whole day by its day ("2026-10-27") and how many days it runs.</summary>
public sealed record PulledEvent(string Id, string Title, double Start, double End, bool AllDay, string? Day, int? Days, string Location, bool Free)
{
    public JsonObject Page()
    {
        var o = new JsonObject { ["id"] = Id, ["title"] = Title, ["start"] = Start, ["end"] = End, ["allDay"] = AllDay, ["location"] = Location, ["free"] = Free };
        if (Day != null) o["day"] = Day;
        if (Days is { } d) o["days"] = d;
        return o;
    }

    public static double Ms(DateTime utc) => (utc - DateTime.UnixEpoch).TotalMilliseconds;
}

/// <summary>An event of Simpl's own calendar (content/app/feed.js events), written into the account's Simpl calendar.</summary>
public sealed class PushEvent
{
    public string Uid { get; set; } = "";
    public string Title { get; set; } = "";
    public double? Start { get; set; }
    public double? End { get; set; }
    public bool? AllDay { get; set; }
    public string? Day { get; set; }
    public int? Days { get; set; }
    public string? Description { get; set; }
    public string? Url { get; set; }
    public string? Location { get; set; }
    public bool? Free { get; set; }
    public string? Rrule { get; set; }
    public double? Alarm { get; set; }

    private static string N(double? v) => v?.ToString("R", CultureInfo.InvariantCulture) ?? "";

    /// <summary>What it says, as a short digest: an event unchanged since it was last written is left alone.</summary>
    public string Signature
    {
        get
        {
            var parts = new[] { Title, N(Start), N(End), AllDay == true ? "1" : "0", Day ?? "", (Days ?? 1).ToString(CultureInfo.InvariantCulture),
                Description ?? "", Url ?? "", Location ?? "", Free == true ? "1" : "0", Rrule ?? "", N(Alarm) };
            var digest = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001f', parts)));
            return Convert.ToHexString(digest, 0, 12).ToLowerInvariant();
        }
    }

    public bool IsAllDay => AllDay == true && Day != null;

    /// <summary>Its start and its end, in UTC (a due time, a moment, given a quarter of an hour: a calendar will not take an
    /// event with no length). A whole day: its first day and the day after its last, as dates.</summary>
    public (DateTime Start, DateTime End)? Span
    {
        get
        {
            if (IsAllDay && DayDate(Day!) is { } first) return (first, first.AddDays(Math.Max(1, Days ?? 1)));
            if (Start is not { } s) return null;
            var a = DateTime.UnixEpoch.AddMilliseconds(s);
            var b = DateTime.UnixEpoch.AddMilliseconds(End ?? s);
            return (a, b > a.AddMinutes(15) ? b : a.AddMinutes(15));
        }
    }

    /// <summary>The repeat, as a calendar keeps one: how often (DAILY, WEEKLY, MONTHLY), and every how many.</summary>
    public (string Frequency, int Interval)? Repeats
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Rrule)) return null;
            var fields = new Dictionary<string, string>();
            foreach (var p in Rrule.Split(';'))
            {
                var kv = p.Split('=', 2);
                if (kv.Length == 2) fields.TryAdd(kv[0].Trim().ToUpperInvariant(), kv[1].Trim().ToUpperInvariant());
            }
            if (!fields.TryGetValue("FREQ", out var f) || f is not ("DAILY" or "WEEKLY" or "MONTHLY")) return null;
            var n = fields.TryGetValue("INTERVAL", out var i) && int.TryParse(i, out var k) ? k : 1;
            return (f, Math.Max(1, n));
        }
    }

    public static DateTime? DayDate(string day) =>
        DateTime.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}

/// <summary>What the page's feedEvents answers: Simpl's own calendar.</summary>
public sealed class FeedEvents
{
    public List<PushEvent> Events { get; set; } = new();
}

/// <summary>One account's calendars, read and written.</summary>
public interface ICalendarService
{
    Task<List<AccountCalendar>> Calendars();
    Task<List<PulledEvent>> Events(string calendar, DateTime fromUtc, DateTime toUtc);
    /// <summary>Simpl's calendar in this account: the one made before (<paramref name="known"/>), else one named Simpl
    /// already there, else one made now. Its id.</summary>
    Task<string> SimplCalendar(string? known);
    /// <summary>An event written: put in, or (<paramref name="existing"/>) changed. Its id there.</summary>
    Task<string> Write(PushEvent e, string calendar, string? existing);
    Task Remove(string id, string calendar);
}

internal static class CalendarHttp
{
    public static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>CalDAV's own: redirects followed by hand, so the sign-in goes with them (HttpClient drops it to another host).</summary>
    public static readonly HttpClient Dav = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };

    public static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    public static double? Num(JsonNode? n)
    {
        if (n is not JsonValue v) return null;
        if (v.TryGetValue<double>(out var d)) return d;
        if (v.TryGetValue<long>(out var l)) return l;
        return v.TryGetValue<string>(out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : null;
    }

    public static bool Bool(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    public static JsonNode? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return JsonNode.Parse(text); } catch { return null; }
    }

    public static string Enc(string s) => Uri.EscapeDataString(s);
}

// ---- Signing in (Google, Microsoft) ----

/// <summary>A sign-in client: where its pages and tokens are, what it asks for.</summary>
public sealed record OAuthClient(string Kind, string ClientId, string? Secret, Uri Authorize, Uri Token, string[] Scopes, string LoopbackHost, (string, string)[] Extra)
{
    public static OAuthClient? Google => CalendarKeys.Google.Length == 0 ? null : new(CalendarKinds.Google, CalendarKeys.Google, CalendarKeys.GoogleDesktopSecret,
        new Uri("https://accounts.google.com/o/oauth2/v2/auth"), new Uri("https://oauth2.googleapis.com/token"),
        new[] { "openid", "email", "https://www.googleapis.com/auth/calendar.readonly", "https://www.googleapis.com/auth/calendar.app.created" },
        "127.0.0.1", new[] { ("access_type", "offline"), ("prompt", "consent") });

    public static OAuthClient? Microsoft => CalendarKeys.Microsoft.Length == 0 ? null : new(CalendarKinds.Microsoft, CalendarKeys.Microsoft, null,
        new Uri("https://login.microsoftonline.com/common/oauth2/v2.0/authorize"), new Uri("https://login.microsoftonline.com/common/oauth2/v2.0/token"),
        new[] { "openid", "profile", "email", "offline_access", "User.Read", "Calendars.ReadWrite" },
        "localhost", new[] { ("prompt", "select_account") });

    public static OAuthClient? Of(string kind) => kind switch { CalendarKinds.Google => Google, CalendarKinds.Microsoft => Microsoft, _ => null };
}

/// <summary>An account's tokens: the access token and when it ends, the refresh token that makes the next, and who it is.</summary>
public sealed class OAuthTokens
{
    public string Access { get; set; } = "";
    public string Refresh { get; set; } = "";
    public DateTime Expires { get; set; }
    public string Email { get; set; } = "";

    /// <summary>A form sent to the client's token address: the tokens it answers with (a refresh keeps the refresh token
    /// it was made with when no new one comes).</summary>
    public static async Task<OAuthTokens> Exchange(OAuthClient c, Dictionary<string, string> form, string keeping = "", string known = "")
    {
        form["client_id"] = c.ClientId;
        if (c.Secret is { Length: > 0 } secret) form["client_secret"] = secret;
        if (c.Kind == CalendarKinds.Microsoft) form["scope"] = string.Join(' ', c.Scopes);
        using var r = await CalendarHttp.Client.PostAsync(c.Token, new FormUrlEncodedContent(form));
        var j = CalendarHttp.Parse(await r.Content.ReadAsStringAsync()) as JsonObject;
        var access = CalendarHttp.Str(j?["access_token"]);
        if (!r.IsSuccessStatusCode || access == null)
        {
            if (CalendarHttp.Str(j?["error"]) == "invalid_grant" || r.StatusCode == HttpStatusCode.Unauthorized) throw CalendarException.SignedOut();
            var why = CalendarHttp.Str(j?["error_description"]);
            throw new CalendarException(CalendarFailure.Other, why != null ? $"The sign-in could not be finished: {why}" : $"The sign-in could not be finished ({(int)r.StatusCode}).");
        }
        var seconds = CalendarHttp.Num(j?["expires_in"]) ?? 3600;
        var who = CalendarHttp.Str(j?["id_token"]) is { } jwt ? Identity(jwt) : null;
        return new OAuthTokens { Access = access, Refresh = CalendarHttp.Str(j?["refresh_token"]) ?? keeping, Expires = DateTime.UtcNow.AddSeconds(seconds - 60), Email = who ?? known };
    }

    /// <summary>Who signed in, from the ID token's claims (shown only; nothing is trusted from it).</summary>
    public static string? Identity(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2) return null;
        var b = parts[1].Replace('-', '+').Replace('_', '/');
        while (b.Length % 4 != 0) b += "=";
        try
        {
            var claims = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(b))) as JsonObject;
            return CalendarHttp.Str(claims?["email"]) ?? CalendarHttp.Str(claims?["preferred_username"]);
        }
        catch { return null; }
    }
}

/// <summary>PKCE's verifier and its challenge (RFC 7636, S256).</summary>
public static class Pkce
{
    public static string Verifier() => Base64Url(RandomNumberGenerator.GetBytes(48));
    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    public static string Base64Url(byte[] d) => Convert.ToBase64String(d).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}

/// <summary>The provider's own sign-in page in the student's browser, coming back to this PC's loopback address (the way
/// Google and Microsoft ask a desktop app to sign in): the code it brings exchanged for the tokens.</summary>
public static class OAuthSignIn
{
    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public static async Task<OAuthTokens> SignIn(OAuthClient c, CancellationToken cancel)
    {
        var verifier = Pkce.Verifier();
        var state = Pkce.Verifier();
        var redirect = $"http://{c.LoopbackHost}:{FreePort()}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(redirect);
        listener.Start();
        var query = new List<(string, string)>
        {
            ("client_id", c.ClientId), ("redirect_uri", redirect), ("response_type", "code"), ("scope", string.Join(' ', c.Scopes)),
            ("code_challenge", Pkce.Challenge(verifier)), ("code_challenge_method", "S256"), ("state", state),
        };
        query.AddRange(c.Extra);
        var start = c.Authorize + "?" + string.Join('&', query.Select(q => $"{q.Item1}={CalendarHttp.Enc(q.Item2)}"));
        Process.Start(new ProcessStartInfo(start) { UseShellExecute = true });
        var asked = listener.GetContextAsync();
        var gave = await Task.WhenAny(asked, Task.Delay(TimeSpan.FromMinutes(5), cancel));
        if (gave != asked) throw CalendarException.Cancelled();
        var ctx = await asked;
        var back = ctx.Request.QueryString;
        var page = "<!doctype html><meta charset=utf-8><title>Simpl</title><body style=\"font:16px Segoe UI,sans-serif;padding:48px\"><h2>You're signed in.</h2><p>You can close this tab and go back to Simpl.</p>";
        var bytes = Encoding.UTF8.GetBytes(page);
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes, cancel);
        ctx.Response.Close();
        if (back["error"] is { } e) throw e == "access_denied" ? CalendarException.Cancelled() : new CalendarException(CalendarFailure.Other, back["error_description"] ?? $"The sign-in was refused ({e}).");
        if (back["state"] != state) throw new CalendarException(CalendarFailure.Other, "The sign-in came back without its check. Try again.");
        if (back["code"] is not { Length: > 0 } code) throw new CalendarException(CalendarFailure.Other, "The sign-in came back without its code. Try again.");
        return await OAuthTokens.Exchange(c, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = redirect, ["code_verifier"] = verifier,
        });
    }
}

/// <summary>A web API asked with an account's token: its JSON, or why not. A token turned down is refreshed once and asked again.</summary>
public sealed class CalendarWeb(OAuthClient client, OAuthTokens tokens, Action<OAuthTokens> saved)
{
    private OAuthTokens tokens = tokens;

    private async Task<string> Fresh()
    {
        if (tokens.Expires > DateTime.UtcNow) return tokens.Access;
        if (tokens.Refresh.Length == 0) throw CalendarException.SignedOut();
        tokens = await OAuthTokens.Exchange(client, new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = tokens.Refresh }, tokens.Refresh, tokens.Email);
        saved(tokens);
        return tokens.Access;
    }

    /// <summary>JSON sent and answered (null for an answer with no body).</summary>
    public async Task<JsonObject?> Ask(HttpMethod method, string url, JsonObject? body = null, Dictionary<string, string>? headers = null, bool again = true)
    {
        using var r = new HttpRequestMessage(method, url);
        r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await Fresh());
        if (headers != null) foreach (var (k, v) in headers) r.Headers.TryAddWithoutValidation(k, v);
        if (body != null) r.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var a = await CalendarHttp.Client.SendAsync(r);
        var status = (int)a.StatusCode;
        if (status == 401 && again)
        {
            tokens.Expires = DateTime.MinValue;
            return await Ask(method, url, body, headers, false);
        }
        if (status == 401) throw CalendarException.SignedOut();
        if (status is 404 or 410) throw CalendarException.NotFound();
        var text = await a.Content.ReadAsStringAsync();
        var j = CalendarHttp.Parse(text) as JsonObject;
        if (!a.IsSuccessStatusCode)
        {
            var e = j?["error"];
            var why = CalendarHttp.Str((e as JsonObject)?["message"]) ?? CalendarHttp.Str(e) ?? $"it answered {status}";
            throw new CalendarException(CalendarFailure.Other, $"{CalendarKinds.Name(client.Kind)} said no: {why}");
        }
        return j;
    }
}

// ---- Google Calendar ----

public sealed class GoogleCalendar(CalendarWeb web) : ICalendarService
{
    private const string Base = "https://www.googleapis.com/calendar/v3";

    private static string Url(string path, params (string, string)[] query) =>
        Base + path + (query.Length == 0 ? "" : "?" + string.Join('&', query.OrderBy(q => q.Item1, StringComparer.Ordinal).Select(q => $"{q.Item1}={CalendarHttp.Enc(q.Item2)}")));

    public async Task<List<AccountCalendar>> Calendars()
    {
        var all = new List<AccountCalendar>();
        string? page = null;
        do
        {
            var q = new List<(string, string)> { ("minAccessRole", "reader"), ("maxResults", "250") };
            if (page != null) q.Add(("pageToken", page));
            var j = await web.Ask(HttpMethod.Get, Url("/users/me/calendarList", q.ToArray()));
            foreach (var c in (j?["items"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            {
                if (CalendarHttp.Str(c["id"]) is not { } id || CalendarHttp.Bool(c["deleted"])) continue;
                all.Add(new AccountCalendar
                {
                    Id = id, Name = CalendarHttp.Str(c["summaryOverride"]) ?? CalendarHttp.Str(c["summary"]) ?? id,
                    Color = CalendarHttp.Str(c["backgroundColor"]) ?? "#4285f4", On = CalendarHttp.Bool(c["primary"]),
                });
            }
            page = CalendarHttp.Str(j?["nextPageToken"]);
        } while (page != null);
        return all;
    }

    public async Task<List<PulledEvent>> Events(string calendar, DateTime fromUtc, DateTime toUtc)
    {
        var all = new List<PulledEvent>();
        string? page = null;
        do
        {
            var q = new List<(string, string)>
            {
                ("timeMin", fromUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)), ("timeMax", toUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
                ("singleEvents", "true"), ("maxResults", "2500"), ("showDeleted", "false"),
            };
            if (page != null) q.Add(("pageToken", page));
            var j = await web.Ask(HttpMethod.Get, Url($"/calendars/{CalendarHttp.Enc(calendar)}/events", q.ToArray()));
            foreach (var e in (j?["items"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
                if (Pulled(e) is { } ev) all.Add(ev);
            page = CalendarHttp.Str(j?["nextPageToken"]);
        } while (page != null && all.Count < 4000);
        return all;
    }

    /// <summary>A Google event in the page's shape: a whole day by its dates, else its times; cancelled ones left out.</summary>
    public static PulledEvent? Pulled(JsonObject e)
    {
        if (CalendarHttp.Str(e["id"]) is not { } id || CalendarHttp.Str(e["status"]) == "cancelled") return null;
        if (e["start"] is not JsonObject s || e["end"] is not JsonObject en) return null;
        var title = CalendarHttp.Str(e["summary"]) ?? "Busy";
        var location = CalendarHttp.Str(e["location"]) ?? "";
        var free = CalendarHttp.Str(e["transparency"]) == "transparent";
        if (CalendarHttp.Str(s["date"]) is { } d && PushEvent.DayDate(d) is { } first)
        {
            var last = CalendarHttp.Str(en["date"]) is { } ld && PushEvent.DayDate(ld) is { } l ? l : first.AddDays(1);
            var days = Math.Max(1, (int)Math.Round((last - first).TotalDays));
            var startLocal = DateTime.SpecifyKind(first, DateTimeKind.Local).ToUniversalTime();
            return new PulledEvent(id, title, PulledEvent.Ms(startLocal), PulledEvent.Ms(startLocal.AddDays(days)), true, d, days, location, free);
        }
        if (Ics.Instant(CalendarHttp.Str(s["dateTime"])) is not { } a || Ics.Instant(CalendarHttp.Str(en["dateTime"])) is not { } b) return null;
        return new PulledEvent(id, title, PulledEvent.Ms(a), PulledEvent.Ms(b), false, null, null, location, free);
    }

    public async Task<string> SimplCalendar(string? known)
    {
        if (known != null)
        {
            try { await web.Ask(HttpMethod.Get, Url($"/calendars/{CalendarHttp.Enc(known)}")); return known; }
            catch (CalendarException e) when (e.Kind == CalendarFailure.NotFound) { }
        }
        var j = await web.Ask(HttpMethod.Post, Url("/calendars"), new JsonObject
        {
            ["summary"] = "Simpl", ["description"] = "Your work, your own tasks and your plans, from Simpl.", ["timeZone"] = TimeZoneIana(),
        });
        return CalendarHttp.Str(j?["id"]) ?? throw new CalendarException(CalendarFailure.Other, "Google did not make the Simpl calendar.");
    }

    public async Task<string> Write(PushEvent e, string calendar, string? existing)
    {
        var body = Body(e) ?? throw new CalendarException(CalendarFailure.Other, "An event with no time.");
        if (existing != null)
        {
            try { await web.Ask(HttpMethod.Put, Url($"/calendars/{CalendarHttp.Enc(calendar)}/events/{CalendarHttp.Enc(existing)}"), body); return existing; }
            catch (CalendarException x) when (x.Kind == CalendarFailure.NotFound) { }
        }
        var j = await web.Ask(HttpMethod.Post, Url($"/calendars/{CalendarHttp.Enc(calendar)}/events"), Body(e));
        return CalendarHttp.Str(j?["id"]) ?? throw new CalendarException(CalendarFailure.Other, "Google did not keep an event.");
    }

    public static JsonObject? Body(PushEvent e)
    {
        if (e.Span is not { } span) return null;
        var b = new JsonObject
        {
            ["summary"] = e.Title, ["description"] = e.Description ?? "", ["location"] = e.Location ?? "",
            ["transparency"] = e.Free == true ? "transparent" : "opaque",
            ["extendedProperties"] = new JsonObject { ["private"] = new JsonObject { ["simplUid"] = e.Uid } },
        };
        if (e.IsAllDay)
        {
            b["start"] = new JsonObject { ["date"] = e.Day };
            b["end"] = new JsonObject { ["date"] = span.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        }
        else
        {
            b["start"] = new JsonObject { ["dateTime"] = span.Start.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) };
            b["end"] = new JsonObject { ["dateTime"] = span.End.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) };
        }
        if (e.Url is { Length: > 0 } u) b["source"] = new JsonObject { ["title"] = "Simpl", ["url"] = u };
        if (e.Repeats is { } r) b["recurrence"] = new JsonArray($"RRULE:FREQ={r.Frequency}{(r.Interval > 1 ? $";INTERVAL={r.Interval}" : "")}");
        var overrides = new JsonArray();
        if (e.Alarm is { } m && m >= 0) overrides.Add(new JsonObject { ["method"] = "popup", ["minutes"] = (int)m });
        b["reminders"] = new JsonObject { ["useDefault"] = false, ["overrides"] = overrides };
        return b;
    }

    public async Task Remove(string id, string calendar)
    {
        try { await web.Ask(HttpMethod.Delete, Url($"/calendars/{CalendarHttp.Enc(calendar)}/events/{CalendarHttp.Enc(id)}")); }
        catch (CalendarException e) when (e.Kind == CalendarFailure.NotFound) { }
    }

    /// <summary>This PC's time zone by its IANA name (Google's), else UTC.</summary>
    internal static string TimeZoneIana() =>
        TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana) ? iana : "UTC";
}

// ---- Outlook (Microsoft Graph) ----

public sealed class OutlookCalendar(CalendarWeb web) : ICalendarService
{
    private const string Base = "https://graph.microsoft.com/v1.0";
    private static readonly Dictionary<string, string> Utc = new() { ["Prefer"] = "outlook.timezone=\"UTC\"" };

    private static string Url(string path, params (string, string)[] query) =>
        Base + path + (query.Length == 0 ? "" : "?" + string.Join('&', query.OrderBy(q => q.Item1, StringComparer.Ordinal).Select(q => $"{q.Item1}={CalendarHttp.Enc(q.Item2)}")));

    /// <summary>Outlook's named colours, as near as Simpl's own.</summary>
    private static readonly Dictionary<string, string> Colors = new()
    {
        ["lightBlue"] = "#0a84ff", ["lightGreen"] = "#34c759", ["lightOrange"] = "#ff9500", ["lightGray"] = "#8e8e93", ["lightYellow"] = "#ffcc00",
        ["lightTeal"] = "#30b0c7", ["lightPink"] = "#ff2d55", ["lightBrown"] = "#a2845e", ["lightRed"] = "#ff3b30", ["maxColor"] = "#0078d4", ["auto"] = "#0078d4",
    };

    public async Task<List<AccountCalendar>> Calendars()
    {
        var j = await web.Ask(HttpMethod.Get, Url("/me/calendars", ("$top", "100"), ("$select", "id,name,color,hexColor,isDefaultCalendar")));
        var all = new List<AccountCalendar>();
        foreach (var c in (j?["value"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
        {
            if (CalendarHttp.Str(c["id"]) is not { } id) continue;
            var hex = CalendarHttp.Str(c["hexColor"]) is { Length: > 0 } h ? h : Colors.GetValueOrDefault(CalendarHttp.Str(c["color"]) ?? "auto", "#0078d4");
            all.Add(new AccountCalendar { Id = id, Name = CalendarHttp.Str(c["name"]) ?? "Calendar", Color = hex, On = CalendarHttp.Bool(c["isDefaultCalendar"]) });
        }
        return all;
    }

    public async Task<List<PulledEvent>> Events(string calendar, DateTime fromUtc, DateTime toUtc)
    {
        var all = new List<PulledEvent>();
        string? next = Url($"/me/calendars/{CalendarHttp.Enc(calendar)}/calendarView",
            ("startDateTime", fromUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)), ("endDateTime", toUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
            ("$top", "500"), ("$select", "id,subject,start,end,isAllDay,location,showAs,isCancelled"));
        while (next != null && all.Count < 4000)
        {
            var j = await web.Ask(HttpMethod.Get, next, headers: Utc);
            foreach (var e in (j?["value"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
                if (Pulled(e) is { } ev) all.Add(ev);
            next = CalendarHttp.Str(j?["@odata.nextLink"]);
        }
        return all;
    }

    /// <summary>An Outlook event in the page's shape (its times asked for in UTC).</summary>
    public static PulledEvent? Pulled(JsonObject e)
    {
        if (CalendarHttp.Str(e["id"]) is not { } id || CalendarHttp.Bool(e["isCancelled"])) return null;
        if (Ics.Instant(CalendarHttp.Str((e["start"] as JsonObject)?["dateTime"]), utc: true) is not { } a) return null;
        if (Ics.Instant(CalendarHttp.Str((e["end"] as JsonObject)?["dateTime"]), utc: true) is not { } b) return null;
        var title = CalendarHttp.Str(e["subject"]) is { Length: > 0 } t ? t : "Busy";
        var location = CalendarHttp.Str((e["location"] as JsonObject)?["displayName"]) ?? "";
        var free = CalendarHttp.Str(e["showAs"]) == "free";
        if (CalendarHttp.Bool(e["isAllDay"]))
        {
            // (a whole day's midnights, in UTC as asked: its days as written)
            var day = a.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var days = Math.Max(1, (int)Math.Round((b - a).TotalDays));
            var first = DateTime.SpecifyKind(a.Date, DateTimeKind.Local).ToUniversalTime();
            return new PulledEvent(id, title, PulledEvent.Ms(first), PulledEvent.Ms(first.AddDays(days)), true, day, days, location, free);
        }
        return new PulledEvent(id, title, PulledEvent.Ms(a), PulledEvent.Ms(b), false, null, null, location, free);
    }

    public async Task<string> SimplCalendar(string? known)
    {
        if (known != null)
        {
            try { await web.Ask(HttpMethod.Get, Url($"/me/calendars/{CalendarHttp.Enc(known)}", ("$select", "id"))); return known; }
            catch (CalendarException e) when (e.Kind == CalendarFailure.NotFound) { }
        }
        // (one named Simpl already there — made by Simpl on another computer — is used, not another made beside it)
        var list = await web.Ask(HttpMethod.Get, Url("/me/calendars", ("$top", "100"), ("$select", "id,name")));
        var had = (list?["value"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().FirstOrDefault(c => CalendarHttp.Str(c["name"]) == "Simpl");
        if (CalendarHttp.Str(had?["id"]) is { } hid) return hid;
        var j = await web.Ask(HttpMethod.Post, Url("/me/calendars"), new JsonObject { ["name"] = "Simpl", ["color"] = "lightBlue" });
        return CalendarHttp.Str(j?["id"]) ?? throw new CalendarException(CalendarFailure.Other, "Outlook did not make the Simpl calendar.");
    }

    public async Task<string> Write(PushEvent e, string calendar, string? existing)
    {
        if (Body(e) is null) throw new CalendarException(CalendarFailure.Other, "An event with no time.");
        if (existing != null)
        {
            try { await web.Ask(HttpMethod.Patch, Url($"/me/events/{CalendarHttp.Enc(existing)}"), Body(e)); return existing; }
            catch (CalendarException x) when (x.Kind == CalendarFailure.NotFound) { }
        }
        var j = await web.Ask(HttpMethod.Post, Url($"/me/calendars/{CalendarHttp.Enc(calendar)}/events"), Body(e));
        return CalendarHttp.Str(j?["id"]) ?? throw new CalendarException(CalendarFailure.Other, "Outlook did not keep an event.");
    }

    public static JsonObject? Body(PushEvent e)
    {
        if (e.Span is not { } span) return null;
        // (a timed event in UTC; a whole day as its dates, at midnight, in UTC as well — Graph takes them as written)
        string At(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        var b = new JsonObject
        {
            ["subject"] = e.Title,
            ["body"] = new JsonObject { ["contentType"] = "text", ["content"] = e.Description ?? "" },
            ["start"] = new JsonObject { ["dateTime"] = At(span.Start), ["timeZone"] = "UTC" },
            ["end"] = new JsonObject { ["dateTime"] = At(span.End), ["timeZone"] = "UTC" },
            ["isAllDay"] = e.IsAllDay,
            ["showAs"] = e.Free == true ? "free" : "busy",
            ["location"] = new JsonObject { ["displayName"] = e.Location ?? "" },
        };
        if (e.Alarm is { } m && m >= 0)
        {
            b["isReminderOn"] = true;
            b["reminderMinutesBeforeStart"] = (int)m;
        }
        else b["isReminderOn"] = false;
        if (e.Repeats is { } r)
        {
            var local = e.IsAllDay ? span.Start : span.Start.ToLocalTime();
            var dow = local.DayOfWeek.ToString().ToLowerInvariant();
            JsonObject pattern = r.Frequency switch
            {
                "DAILY" => new JsonObject { ["type"] = "daily", ["interval"] = r.Interval },
                "WEEKLY" => new JsonObject { ["type"] = "weekly", ["interval"] = r.Interval, ["daysOfWeek"] = new JsonArray(dow), ["firstDayOfWeek"] = "sunday" },
                _ => new JsonObject { ["type"] = "absoluteMonthly", ["interval"] = r.Interval, ["dayOfMonth"] = local.Day },
            };
            b["recurrence"] = new JsonObject
            {
                ["pattern"] = pattern,
                ["range"] = new JsonObject { ["type"] = "noEnd", ["startDate"] = local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
            };
        }
        return b;
    }

    public async Task Remove(string id, string calendar)
    {
        try { await web.Ask(HttpMethod.Delete, Url($"/me/events/{CalendarHttp.Enc(id)}")); }
        catch (CalendarException e) when (e.Kind == CalendarFailure.NotFound) { }
    }
}

// ---- iCloud Calendar (CalDAV) ----

/// <summary>
/// iCloud's calendars over CalDAV, signed in with the Apple ID and an app-specific password (Basic, over HTTPS): the
/// account found from caldav.icloud.com (its principal, then its calendar home on the iCloud server that keeps it), its
/// calendars listed there, their events asked for a span with each repeat's times spelled out by iCloud (expand), and
/// Simpl's calendar made there (MKCALENDAR) and written an event to a file (PUT …/simpl-….ics).
/// </summary>
public sealed class ICloudCalendar(string appleId, string password) : ICalendarService
{
    public const string Root = "https://caldav.icloud.com/";
    private string? home;

    private async Task<(string Body, Uri Url)> Dav(string method, string url, string? body, int? depth = null, string contentType = "application/xml; charset=utf-8")
    {
        var at = new Uri(url);
        for (var hop = 0; ; hop++)
        {
            using var r = new HttpRequestMessage(new HttpMethod(method), at);
            r.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{appleId}:{password}")));
            if (depth is { } d) r.Headers.TryAddWithoutValidation("Depth", d.ToString(CultureInfo.InvariantCulture));
            if (body != null) r.Content = new StringContent(body, Encoding.UTF8, MediaTypeHeaderValue.Parse(contentType));
            using var a = await CalendarHttp.Dav.SendAsync(r);
            var status = (int)a.StatusCode;
            if (status is 301 or 302 or 307 or 308 && a.Headers.Location is { } to && hop < 4)
            {
                at = to.IsAbsoluteUri ? to : new Uri(at, to);
                continue;
            }
            if (status == 401) throw CalendarException.SignedOut();
            if (status is 404 or 410) throw CalendarException.NotFound();
            if (status >= 400) throw new CalendarException(CalendarFailure.Other, $"iCloud said no ({status}).");
            return (await a.Content.ReadAsStringAsync(), at);
        }
    }

    /// <summary>The account's calendar home, found once.</summary>
    public async Task<string> Home()
    {
        if (home != null) return home;
        var (who, at) = await Dav("PROPFIND", Root, CalDav.PrincipalAsk, 0);
        var principal = CalDav.Href(who, CalDav.D + "current-user-principal") ?? throw new CalendarException(CalendarFailure.Other, "iCloud did not say where your calendars are.");
        var (where, at2) = await Dav("PROPFIND", new Uri(at, principal).ToString(), CalDav.HomeAsk, 0);
        var h = CalDav.Href(where, CalDav.C + "calendar-home-set") ?? throw new CalendarException(CalendarFailure.Other, "iCloud did not say where your calendars are.");
        var url = new Uri(at2, h).ToString();
        return home = url.EndsWith('/') ? url : url + "/";
    }

    public async Task<List<AccountCalendar>> Calendars()
    {
        var h = await Home();
        var (xml, at) = await Dav("PROPFIND", h, CalDav.CalendarsAsk, 1);
        var all = CalDav.Calendars(xml, at);
        // (at first, the one called Home — iCloud's own first calendar — or else the first)
        var main = all.FirstOrDefault(c => c.Name == "Home") ?? all.FirstOrDefault();
        if (main != null) main.On = true;
        return all;
    }

    public async Task<List<PulledEvent>> Events(string calendar, DateTime fromUtc, DateTime toUtc)
    {
        var (xml, _) = await Dav("REPORT", calendar, CalDav.EventsAsk(fromUtc, toUtc), 1);
        return CalDav.CalendarData(xml).SelectMany(t => Ics.Events(t)).Take(4000).ToList();
    }

    public async Task<string> SimplCalendar(string? known)
    {
        if (known != null)
        {
            try { await Dav("PROPFIND", known, CalDav.NameAsk, 0); return known; }
            catch (CalendarException e) when (e.Kind == CalendarFailure.NotFound) { }
        }
        var h = await Home();
        var (xml, at) = await Dav("PROPFIND", h, CalDav.CalendarsAsk, 1);
        if (CalDav.Calendars(xml, at).FirstOrDefault(c => c.Name == "Simpl") is { } had) return had.Id;
        var made = new Uri(new Uri(h), "simpl-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant() + "/").ToString();
        await Dav("MKCALENDAR", made, CalDav.MakeSimpl);
        return made;
    }

    public async Task<string> Write(PushEvent e, string calendar, string? existing)
    {
        var url = existing ?? new Uri(new Uri(calendar.EndsWith('/') ? calendar : calendar + "/"), Ics.FileName(e.Uid)).ToString();
        var body = Ics.Calendar(e, DateTime.UtcNow) ?? throw new CalendarException(CalendarFailure.Other, "An event with no time.");
        await Dav("PUT", url, body, contentType: "text/calendar; charset=utf-8");
        return url;
    }

    public async Task Remove(string id, string calendar)
    {
        try { await Dav("DELETE", id, null); }
        catch (CalendarException e) when (e.Kind == CalendarFailure.NotFound) { }
    }
}

/// <summary>CalDAV's questions and the reading of its answers (RFC 4791), apart so they are tested on their own.</summary>
public static class CalDav
{
    public static readonly XNamespace D = "DAV:", C = "urn:ietf:params:xml:ns:caldav", A = "http://apple.com/ns/ical/";

    public const string PrincipalAsk = "<?xml version=\"1.0\" encoding=\"utf-8\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:current-user-principal/></d:prop></d:propfind>";
    public const string HomeAsk = "<?xml version=\"1.0\" encoding=\"utf-8\"?><d:propfind xmlns:d=\"DAV:\" xmlns:c=\"urn:ietf:params:xml:ns:caldav\"><d:prop><c:calendar-home-set/></d:prop></d:propfind>";
    public const string NameAsk = "<?xml version=\"1.0\" encoding=\"utf-8\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:displayname/></d:prop></d:propfind>";
    public const string CalendarsAsk = "<?xml version=\"1.0\" encoding=\"utf-8\"?><d:propfind xmlns:d=\"DAV:\" xmlns:c=\"urn:ietf:params:xml:ns:caldav\" xmlns:a=\"http://apple.com/ns/ical/\"><d:prop><d:resourcetype/><d:displayname/><a:calendar-color/><c:supported-calendar-component-set/></d:prop></d:propfind>";
    public const string MakeSimpl = "<?xml version=\"1.0\" encoding=\"utf-8\"?><c:mkcalendar xmlns:d=\"DAV:\" xmlns:c=\"urn:ietf:params:xml:ns:caldav\" xmlns:a=\"http://apple.com/ns/ical/\"><d:set><d:prop><d:displayname>Simpl</d:displayname><a:calendar-color>#0A84FFFF</a:calendar-color><c:supported-calendar-component-set><c:comp name=\"VEVENT\"/></c:supported-calendar-component-set></d:prop></d:set></c:mkcalendar>";

    /// <summary>The events touching a span, each repeat's times spelled out by the server (expand).</summary>
    public static string EventsAsk(DateTime fromUtc, DateTime toUtc)
    {
        string T(DateTime d) => d.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        return "<?xml version=\"1.0\" encoding=\"utf-8\"?><c:calendar-query xmlns:d=\"DAV:\" xmlns:c=\"urn:ietf:params:xml:ns:caldav\">"
            + $"<d:prop><c:calendar-data><c:expand start=\"{T(fromUtc)}\" end=\"{T(toUtc)}\"/></c:calendar-data></d:prop>"
            + $"<c:filter><c:comp-filter name=\"VCALENDAR\"><c:comp-filter name=\"VEVENT\"><c:time-range start=\"{T(fromUtc)}\" end=\"{T(toUtc)}\"/></c:comp-filter></c:comp-filter></c:filter>"
            + "</c:calendar-query>";
    }

    private static XDocument? Doc(string xml)
    {
        try { return XDocument.Parse(xml); } catch { return null; }
    }

    /// <summary>The first href under the first element of that name in an answer.</summary>
    public static string? Href(string xml, XName under) =>
        Doc(xml)?.Descendants(under).FirstOrDefault()?.Descendants(D + "href").FirstOrDefault()?.Value.Trim();

    /// <summary>The event calendars under a calendar home (not the inbox and outbox, not a reminders list), by their
    /// address: name, colour (iCloud's #RRGGBBAA cut to #RRGGBB).</summary>
    public static List<AccountCalendar> Calendars(string xml, Uri at)
    {
        var all = new List<AccountCalendar>();
        if (Doc(xml) is not { } doc) return all;
        foreach (var r in doc.Descendants(D + "response"))
        {
            var href = r.Element(D + "href")?.Value.Trim();
            var prop = r.Descendants(D + "prop").FirstOrDefault(p => p.Element(D + "resourcetype") != null) ?? r.Descendants(D + "prop").FirstOrDefault();
            if (href == null || prop == null) continue;
            if (prop.Element(D + "resourcetype")?.Element(C + "calendar") == null) continue;
            var comps = prop.Element(C + "supported-calendar-component-set")?.Elements(C + "comp").Select(c => (string?)c.Attribute("name")).ToList();
            if (comps is { Count: > 0 } && !comps.Contains("VEVENT")) continue;
            var color = prop.Element(A + "calendar-color")?.Value.Trim() ?? "";
            all.Add(new AccountCalendar
            {
                Id = new Uri(at, href).ToString(),
                Name = prop.Element(D + "displayname")?.Value.Trim() is { Length: > 0 } n ? n : "Calendar",
                Color = Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}") ? color[..7].ToLowerInvariant() : "#0a84ff",
            });
        }
        return all;
    }

    /// <summary>Each calendar-data (an iCalendar text) in a multistatus answer.</summary>
    public static IEnumerable<string> CalendarData(string xml) =>
        Doc(xml)?.Descendants(C + "calendar-data").Select(e => e.Value).Where(t => t.Length > 0) ?? Enumerable.Empty<string>();
}

/// <summary>iCalendar (RFC 5545), as much of it as a calendar's events need: read into the page's shape, and Simpl's own
/// written for a CalDAV server.</summary>
public static class Ics
{
    /// <summary>A time as the calendars' JSON APIs write it (ISO 8601, with or without its zone; Outlook's, asked for in
    /// UTC, with no zone and seven digits of fraction), in UTC.</summary>
    public static DateTime? Instant(string? s, bool utc = false)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var styles = DateTimeStyles.AllowWhiteSpaces | (utc ? DateTimeStyles.AssumeUniversal : DateTimeStyles.AssumeLocal) | DateTimeStyles.AdjustToUniversal;
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, styles, out var d) ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null;
    }

    private static IEnumerable<string> Unfolded(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t')) { sb.Append(line, 1, line.Length - 1); continue; }
            if (sb.Length > 0) yield return sb.ToString();
            sb.Clear().Append(line);
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    private static string Unescape(string v) =>
        Regex.Replace(v, @"\\([\\;,nN])", m => m.Groups[1].Value is "n" or "N" ? "\n" : m.Groups[1].Value);

    /// <summary>A DTSTART or DTEND: its time in UTC, and whether it is a whole day (then its date, as a local midnight).</summary>
    private static (DateTime Utc, bool Date, DateTime Day)? When(string paramsPart, string value)
    {
        value = value.Trim();
        var isDate = paramsPart.Contains("VALUE=DATE", StringComparison.OrdinalIgnoreCase) && !paramsPart.Contains("VALUE=DATE-TIME", StringComparison.OrdinalIgnoreCase) || value.Length == 8;
        if (isDate)
        {
            if (!DateTime.TryParseExact(value[..Math.Min(8, value.Length)], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return null;
            return (DateTime.SpecifyKind(d, DateTimeKind.Local).ToUniversalTime(), true, d);
        }
        var z = value.EndsWith('Z');
        if (!DateTime.TryParseExact(value.TrimEnd('Z'), "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) return null;
        if (z) return (DateTime.SpecifyKind(t, DateTimeKind.Utc), false, default);
        var tzid = Regex.Match(paramsPart, @"TZID=""?([^;:""]+)""?", RegexOptions.IgnoreCase);
        if (tzid.Success && Zone(tzid.Groups[1].Value) is { } zone)
            return (TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(t, DateTimeKind.Unspecified), zone), false, default);
        return (DateTime.SpecifyKind(t, DateTimeKind.Local).ToUniversalTime(), false, default); // (a floating time: this PC's)
    }

    private static TimeZoneInfo? Zone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch
        {
            try { return TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var win) ? TimeZoneInfo.FindSystemTimeZoneById(win) : null; }
            catch { return null; }
        }
    }

    private static TimeSpan? Duration(string v)
    {
        var m = Regex.Match(v.Trim(), @"^([+-])?P(?:(\d+)W)?(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?$");
        if (!m.Success) return null;
        int G(int i) => m.Groups[i].Success ? int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture) : 0;
        var span = new TimeSpan(G(2) * 7 + G(3), G(4), G(5), G(6));
        return m.Groups[1].Value == "-" ? -span : span;
    }

    /// <summary>The events of an iCalendar text in the page's shape (cancelled ones left out); each by its UID and start.</summary>
    public static List<PulledEvent> Events(string text)
    {
        var all = new List<PulledEvent>();
        Dictionary<string, (string Params, string Value)>? ev = null;
        var depth = 0;
        foreach (var line in Unfolded(text))
        {
            if (line.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase)) { ev = new(StringComparer.OrdinalIgnoreCase); depth = 0; continue; }
            if (ev == null) continue;
            if (line.StartsWith("BEGIN:", StringComparison.OrdinalIgnoreCase)) { depth++; continue; } // (an alarm inside it)
            if (line.StartsWith("END:", StringComparison.OrdinalIgnoreCase))
            {
                if (depth > 0) { depth--; continue; }
                if (Read(ev) is { } e) all.Add(e);
                ev = null;
                continue;
            }
            if (depth > 0) continue;
            var colon = ColonOf(line);
            if (colon < 0) continue;
            var head = line[..colon];
            var semi = head.IndexOf(';');
            var name = semi < 0 ? head : head[..semi];
            ev.TryAdd(name, (semi < 0 ? "" : head[(semi + 1)..], line[(colon + 1)..]));
        }
        return all;
    }

    /// <summary>The colon that ends a line's name and parameters (one inside a quoted parameter is not it).</summary>
    private static int ColonOf(string line)
    {
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') quoted = !quoted;
            else if (line[i] == ':' && !quoted) return i;
        }
        return -1;
    }

    private static PulledEvent? Read(Dictionary<string, (string Params, string Value)> ev)
    {
        if (ev.TryGetValue("STATUS", out var st) && st.Value.Trim().Equals("CANCELLED", StringComparison.OrdinalIgnoreCase)) return null;
        if (!ev.TryGetValue("DTSTART", out var ds) || When(ds.Params, ds.Value) is not { } start) return null;
        var uid = ev.TryGetValue("UID", out var u) ? u.Value.Trim() : Guid.NewGuid().ToString("n");
        var title = ev.TryGetValue("SUMMARY", out var s) && Unescape(s.Value).Trim() is { Length: > 0 } t ? t : "Busy";
        var location = ev.TryGetValue("LOCATION", out var l) ? Unescape(l.Value).Trim() : "";
        var free = ev.TryGetValue("TRANSP", out var tr) && tr.Value.Trim().Equals("TRANSPARENT", StringComparison.OrdinalIgnoreCase);
        DateTime endUtc;
        if (ev.TryGetValue("DTEND", out var de) && When(de.Params, de.Value) is { } end) endUtc = end.Utc;
        else if (ev.TryGetValue("DURATION", out var du) && Duration(du.Value) is { } span) endUtc = start.Utc + span;
        else endUtc = start.Date ? start.Utc.AddDays(1) : start.Utc;
        var id = $"{uid}|{(long)PulledEvent.Ms(start.Utc)}";
        if (start.Date)
        {
            var days = Math.Max(1, (int)Math.Round((endUtc - start.Utc).TotalDays));
            return new PulledEvent(id, title, PulledEvent.Ms(start.Utc), PulledEvent.Ms(start.Utc.AddDays(days)), true,
                start.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), days, location, free);
        }
        return new PulledEvent(id, title, PulledEvent.Ms(start.Utc), PulledEvent.Ms(endUtc < start.Utc ? start.Utc : endUtc), false, null, null, location, free);
    }

    /// <summary>The file an event of Simpl's is kept in on a CalDAV server: the same name for the same event, every time.</summary>
    public static string FileName(string uid) =>
        "simpl-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uid)), 0, 12).ToLowerInvariant() + ".ics";

    private static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n");

    /// <summary>A line folded at 75 octets, as iCalendar asks (never inside a character: an emoji's two halves kept together).</summary>
    internal static void Line(StringBuilder sb, string line)
    {
        var bytes = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            var n = rune.Utf8SequenceLength;
            if (bytes + n > 74) { sb.Append("\r\n "); bytes = 1; }
            sb.Append(rune.ToString());
            bytes += n;
        }
        sb.Append("\r\n");
    }

    /// <summary>One of Simpl's events as an iCalendar file: a whole day by its dates, else its times in UTC; its repeat,
    /// its alert, and whether it counts as busy. Null for one with no time.</summary>
    public static string? Calendar(PushEvent e, DateTime stampUtc)
    {
        if (e.Span is not { } span) return null;
        var sb = new StringBuilder();
        string Utc(DateTime d) => d.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        string Date(DateTime d) => d.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        Line(sb, "BEGIN:VCALENDAR");
        Line(sb, "VERSION:2.0");
        Line(sb, "PRODID:-//Simpl Courses//Simpl for Windows//EN");
        Line(sb, "BEGIN:VEVENT");
        Line(sb, "UID:" + e.Uid.Replace("\r", "").Replace("\n", ""));
        Line(sb, "DTSTAMP:" + Utc(stampUtc));
        if (e.IsAllDay)
        {
            Line(sb, "DTSTART;VALUE=DATE:" + Date(span.Start));
            Line(sb, "DTEND;VALUE=DATE:" + Date(span.End));
        }
        else
        {
            Line(sb, "DTSTART:" + Utc(span.Start));
            Line(sb, "DTEND:" + Utc(span.End));
        }
        Line(sb, "SUMMARY:" + Escape(e.Title));
        if (!string.IsNullOrEmpty(e.Description)) Line(sb, "DESCRIPTION:" + Escape(e.Description));
        if (!string.IsNullOrEmpty(e.Location)) Line(sb, "LOCATION:" + Escape(e.Location));
        if (!string.IsNullOrEmpty(e.Url)) Line(sb, "URL:" + e.Url.Replace("\r", "").Replace("\n", ""));
        Line(sb, "TRANSP:" + (e.Free == true ? "TRANSPARENT" : "OPAQUE"));
        if (e.Repeats is { } r) Line(sb, $"RRULE:FREQ={r.Frequency}" + (r.Interval > 1 ? $";INTERVAL={r.Interval}" : ""));
        if (e.Alarm is { } m && m >= 0)
        {
            Line(sb, "BEGIN:VALARM");
            Line(sb, "ACTION:DISPLAY");
            Line(sb, "DESCRIPTION:" + Escape(e.Title));
            Line(sb, $"TRIGGER:-PT{(int)m}M");
            Line(sb, "END:VALARM");
        }
        Line(sb, "END:VEVENT");
        Line(sb, "END:VCALENDAR");
        return sb.ToString();
    }
}
