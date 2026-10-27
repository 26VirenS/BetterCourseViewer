using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Threading;

namespace Simpl.Support;

/// <summary>(0.3.81) A calendar account signed in to in Sync: its calendars, which of them Simpl shows, whether Simpl writes
/// its own calendar there, and how the last sync went. Nothing secret: the passwords and tokens are sealed apart.</summary>
public sealed class CalendarAccount
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = CalendarKinds.ICloud;
    /// <summary>Who: the Apple ID or the email signed in with.</summary>
    public string Name { get; set; } = "";
    public List<AccountCalendar> Calendars { get; set; } = new();
    /// <summary>Simpl's own calendar — the work, the student's own tasks, the plans — written into a calendar named Simpl there.</summary>
    public bool Push { get; set; } = true;
    public string? SimplCalendar { get; set; }
    public DateTime? Synced { get; set; }
    public string Problem { get; set; } = "";
    public int Pulled { get; set; }
    public int Pushed { get; set; }
    /// <summary>Its sign-in has ended: signed in to again before anything more is read.</summary>
    public bool SignedOut { get; set; }
}

/// <summary>What an account signs in with, sealed by the vault: an iCloud app password, or Google's and Microsoft's tokens.</summary>
public sealed class CalendarSecret
{
    public string? Password { get; set; }
    public OAuthTokens? Tokens { get; set; }
}

/// <summary>
/// (0.3.81) Sync's calendar accounts (the Mac's CalendarAccounts, 1.3.184), the real sign-in the student asked for in place
/// of copying links: iCloud Calendar (CalDAV, with an app password), Google Calendar and Outlook (CalendarProviders.cs).
/// Each, once signed in to:
/// <list type="bullet">
/// <item>its calendars, each shown on Simpl's calendar or not (chosen with Calendars, beside Sync; its main one at first) —
/// the events of those shown, a month back to six months on, kept in the page's storage as a linked calendar's are
/// (cal:links with source "account", cal:feed:&lt;id&gt;), so the Calendar lays them over Canvas's and Simpl counts them
/// busy when it finds a free time (background.js leaves these to the app);</item>
/// <item>Simpl's own calendar (the page's feedEvents: content/app/feed.js) written into a calendar named Simpl in the
/// account, made there the first time: each event put in, changed when what it says changed (its digest kept beside its
/// id), and taken out when it is gone from Simpl's.</item>
/// </list>
/// Read and written soon after Simpl opens, every fifteen minutes, when Sync opens and at Sync Now. The accounts are a
/// list in the preferences; the passwords and tokens are sealed by the vault (Vault.cs) in a file of their own, never in
/// the preferences or the page. Signed out: its secret forgotten (Google's token given back), its calendars gone from
/// Simpl's; the Simpl calendar it made in the account stays there.
/// </summary>
public static class CalendarAccounts
{
    private const string ListKey = "calendarAccounts";
    private const string BookKey = "calendarPushed.";
    private const string SecretPurpose = "calendar-accounts";
    private static string SecretFile => Path.Combine(AppPaths.Root, "calendar-accounts.bin");
    private static readonly TimeSpan Back = TimeSpan.FromDays(30), Ahead = TimeSpan.FromDays(180);

    /// <summary>The screenshot suite's --cal-accounts-demo yes: two accounts as if signed in to, nothing read or written.</summary>
    public static bool Demo => Launch.Flag("cal-accounts-demo");

    private static List<CalendarAccount>? accounts;
    private static Dictionary<string, CalendarSecret>? secrets;
    private static Engine.Engine? engine;
    private static DispatcherTimer? timer;
    private static CancellationTokenSource? signing;

    /// <summary>The accounts being synced or signed in to now ("new:&lt;kind&gt;" while one is being added).</summary>
    public static HashSet<string> Busy { get; } = new();

    /// <summary>Anything about the accounts changed — one added or gone, a sync begun or done: Sync draws them again.</summary>
    public static event Action? Changed;

    /// <summary>What an account's calendars put on Simpl's calendar changed: the Calendar reads again.</summary>
    public static event Action? Synced;

    public static IReadOnlyList<CalendarAccount> Accounts => accounts ??= Load();

    private static List<CalendarAccount> Load()
    {
        if (Demo) return DemoAccounts();
        try
        {
            if (Prefs.String(ListKey) is { Length: > 0 } json && JsonSerializer.Deserialize<List<CalendarAccount>>(json, Json.Options) is { } list) return list;
        }
        catch (Exception e) { Log.Write("calendar accounts: list not read: " + e.Message); }
        return new List<CalendarAccount>();
    }

    private static void Save()
    {
        if (Demo || accounts == null) return;
        Prefs.Set(ListKey, JsonSerializer.Serialize(accounts, Json.Options));
    }

    private static void Tell()
    {
        Changed?.Invoke();
    }

    // ---- started, and kept up to date ----

    /// <summary>Once the app's screens are up: read soon, then every fifteen minutes.</summary>
    public static void Started(Engine.Engine e)
    {
        engine = e;
        if (Demo || timer != null) return;
        timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
        timer.Tick += (_, _) => _ = SyncAll();
        timer.Start();
        if (Accounts.Count == 0) return;
        _ = Task.Delay(8000).ContinueWith(_ => System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => _ = SyncAll()), TaskScheduler.Default);
    }

    public static async Task SyncAll()
    {
        foreach (var a in Accounts.Where(a => !a.SignedOut).ToList()) await Sync(a.Id);
    }

    /// <summary>The accounts not read in the last five minutes, read (Sync opened).</summary>
    public static async Task SyncStale()
    {
        foreach (var a in Accounts.Where(a => !a.SignedOut && DateTime.UtcNow - (a.Synced ?? DateTime.MinValue) > TimeSpan.FromMinutes(5)).ToList()) await Sync(a.Id);
    }

    // ---- signing in and out ----

    /// <summary>iCloud signed in to with the Apple ID and an app password (found, and read for the first time). Why not, or null.</summary>
    public static async Task<string?> ConnectICloud(string appleId, string password)
    {
        if (Demo) return null;
        appleId = appleId.Trim();
        password = password.Trim();
        if (appleId.Length == 0 || password.Length == 0) return "Type your Apple ID and an app-specific password.";
        const string key = "new:" + CalendarKinds.ICloud;
        if (!Busy.Add(key)) return null;
        Tell();
        try
        {
            var service = new ICloudCalendar(appleId, password);
            await service.Home(); // (the Apple ID and password asked about at once: wrong, said now)
            var id = $"{CalendarKinds.ICloud}:{appleId.ToLowerInvariant()}";
            SetSecret(id, new CalendarSecret { Password = password });
            Add(new CalendarAccount { Id = id, Kind = CalendarKinds.ICloud, Name = appleId });
            Log.Write("calendar accounts: icloud signed in");
            await Sync(id);
            return null;
        }
        catch (CalendarException e) when (e.Kind == CalendarFailure.SignedOut)
        {
            return "iCloud did not take that Apple ID and password. Use an app-specific password, not your Apple ID's own.";
        }
        catch (Exception e)
        {
            Log.Write("calendar accounts: icloud not signed in: " + e.Message);
            return e is CalendarException ? e.Message : "iCloud could not be reached. Check the connection and try again.";
        }
        finally
        {
            Busy.Remove(key);
            Tell();
        }
    }

    /// <summary>Google or Microsoft signed in to in the browser, and read for the first time. Why not, or null.</summary>
    public static async Task<string?> ConnectOAuth(string kind)
    {
        if (Demo) return null;
        if (OAuthClient.Of(kind) is not { } client) return $"{CalendarKinds.Name(kind)} sign-in is not ready in this version yet.";
        var key = "new:" + kind;
        if (!Busy.Add(key)) return null;
        Tell();
        signing?.Cancel();
        signing = new CancellationTokenSource();
        try
        {
            var t = await OAuthSignIn.SignIn(client, signing.Token);
            var who = t.Email.Length > 0 ? t.Email : CalendarKinds.Name(kind);
            var id = $"{kind}:{who.ToLowerInvariant()}";
            SetSecret(id, new CalendarSecret { Tokens = t });
            Add(new CalendarAccount { Id = id, Kind = kind, Name = who });
            Log.Write($"calendar accounts: {kind} signed in");
            await Sync(id);
            return null;
        }
        catch (CalendarException e) when (e.Kind == CalendarFailure.Cancelled) { return null; }
        catch (Exception e)
        {
            Log.Write($"calendar accounts: {kind} not signed in: " + e.Message);
            return e is CalendarException ? e.Message : "The sign-in could not be finished. Try again.";
        }
        finally
        {
            Busy.Remove(key);
            Tell();
        }
    }

    /// <summary>A browser sign-in waiting, given up (Sync closed).</summary>
    public static void StopSigningIn() => signing?.Cancel();

    /// <summary>An account added, or signed in to again: what was chosen for it kept.</summary>
    private static void Add(CalendarAccount account)
    {
        var list = (List<CalendarAccount>)Accounts;
        var i = list.FindIndex(x => x.Id == account.Id);
        if (i >= 0)
        {
            account.Calendars = list[i].Calendars;
            account.Push = list[i].Push;
            account.SimplCalendar = list[i].SimplCalendar;
            list[i] = account;
        }
        else list.Add(account);
        Save();
        Tell();
    }

    /// <summary>Signed out: its secret forgotten (Google's token given back), its calendars gone from Simpl's, what was
    /// written for it forgotten. The Simpl calendar in the account stays.</summary>
    public static async Task Disconnect(string id)
    {
        var list = (List<CalendarAccount>)Accounts;
        if (list.FirstOrDefault(a => a.Id == id) is not { } a) return;
        list.Remove(a);
        Save();
        Tell();
        if (!Demo)
        {
            if (a.Kind == CalendarKinds.Google && Secrets().TryGetValue(id, out var s) && s.Tokens is { Refresh.Length: > 0 } t)
            {
                try
                {
                    using var r = await CalendarHttp.Client.PostAsync("https://oauth2.googleapis.com/revoke",
                        new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = t.Refresh }));
                }
                catch { /* (given back or not, it is forgotten here) */ }
            }
            SetSecret(id, null);
            Prefs.Remove(BookKey + id);
            WriteLinks();
        }
        Log.Write($"calendar accounts: {a.Kind} signed out");
        Synced?.Invoke();
    }

    // ---- choices ----

    /// <summary>A calendar shown on Simpl's calendar, or not: its events read (or put away) at once.</summary>
    public static void Show(string calendar, string id, bool on)
    {
        if (Accounts.FirstOrDefault(a => a.Id == id) is not { } a || a.Calendars.FirstOrDefault(c => c.Id == calendar) is not { } c) return;
        c.On = on;
        Save();
        Tell();
        if (Demo) return;
        if (on) _ = Sync(id);
        else
        {
            WriteLinks();
            Synced?.Invoke();
        }
    }

    /// <summary>Simpl's own calendar written into the account, or no longer: turned off, what was written there is taken out.</summary>
    public static void SetPush(string id, bool on)
    {
        if (Accounts.FirstOrDefault(a => a.Id == id) is not { } a) return;
        a.Push = on;
        Save();
        Tell();
        if (Demo) return;
        _ = on ? Sync(id) : TakeOut(id);
    }

    // ---- syncing one account ----

    public static async Task Sync(string id)
    {
        if (Demo || engine is not { } eng || Accounts.FirstOrDefault(x => x.Id == id) is not { } a || !Busy.Add(id)) return;
        Tell();
        try
        {
            var service = Service(a);
            // (its calendars as the account has them now: a new one not shown, one gone gone; never Simpl's own, nor one
            //  named Simpl, shown back)
            var listed = (await service.Calendars()).Where(c => c.Id != a.SimplCalendar && c.Name != "Simpl").ToList();
            if (a.Calendars.Count > 0)
                foreach (var c in listed) c.On = a.Calendars.FirstOrDefault(x => x.Id == c.Id)?.On ?? false;
            a.Calendars = listed;
            // in: the shown calendars' events
            var now = DateTime.UtcNow;
            var pulled = 0;
            var feeds = new JsonObject();
            foreach (var c in a.Calendars.Where(c => c.On))
            {
                var events = await service.Events(c.Id, now - Back, now + Ahead);
                pulled += events.Count;
                feeds[$"cal:feed:{LinkId(a.Id, c.Id)}"] = new JsonObject
                {
                    ["at"] = PulledEvent.Ms(now), ["ok"] = true, ["error"] = "", ["name"] = c.Name,
                    ["events"] = new JsonArray(events.Select(e => (JsonNode)e.Page()).ToArray()),
                };
            }
            a.Pulled = pulled;
            // out: Simpl's own calendar
            if (a.Push)
            {
                var reply = await eng.Call<FeedEvents>("feedEvents");
                var cal = await service.SimplCalendar(a.SimplCalendar);
                a.SimplCalendar = cal;
                a.Pushed = await Push(reply.Events, service, a.Id, cal);
                a.Calendars.RemoveAll(c => c.Id == cal); // (made just now in an account read above: not one of the student's own)
            }
            a.Synced = DateTime.UtcNow;
            a.Problem = "";
            a.SignedOut = false;
            Save();
            Engine.Bridge.Shared.Keep(feeds);
            WriteLinks();
            Log.Write($"calendar accounts: {a.Kind} synced, {a.Pulled} events in, {(a.Push ? a.Pushed : 0)} out");
        }
        catch (CalendarException e) when (e.Kind == CalendarFailure.SignedOut)
        {
            a.SignedOut = true;
            a.Problem = e.Message;
            Save();
            Log.Write($"calendar accounts: {a.Kind} signed out by its provider");
        }
        catch (Exception e)
        {
            a.Problem = e is CalendarException ? e.Message : "Not synced: the calendar could not be reached.";
            Save();
            Log.Write($"calendar accounts: {a.Kind} not synced: {e.Message}");
        }
        finally
        {
            Busy.Remove(id);
            Tell();
            Synced?.Invoke();
        }
    }

    /// <summary>What was written for an event: its id there, and the digest of what it said.</summary>
    private sealed class Pushed
    {
        public string Id { get; set; } = "";
        public string Sig { get; set; } = "";
    }

    private sealed class PushedBook
    {
        public string Calendar { get; set; } = "";
        public Dictionary<string, Pushed> Events { get; set; } = new();
    }

    /// <summary>Simpl's calendar written: each event put in or changed if what it says changed, and one gone from Simpl's
    /// taken out. How many it holds.</summary>
    private static async Task<int> Push(List<PushEvent> events, ICalendarService service, string id, string cal)
    {
        var book = ReadBook(id);
        if (book.Calendar != cal) book = new PushedBook { Calendar = cal }; // (a new calendar: everything again)
        var wanted = new Dictionary<string, PushEvent>();
        foreach (var e in events.Where(e => e.Uid.Length > 0 && e.Span != null)) wanted[e.Uid] = e;
        var written = 0;
        foreach (var e in wanted.Values.OrderBy(e => e.Uid, StringComparer.Ordinal))
        {
            book.Events.TryGetValue(e.Uid, out var had);
            var sig = e.Signature;
            if (had?.Sig == sig) continue;
            var rid = await service.Write(e, cal, had?.Id);
            book.Events[e.Uid] = new Pushed { Id = rid, Sig = sig };
            if (++written % 25 == 0) SaveBook(book, id); // (a long first sync kept as it goes)
        }
        foreach (var (uid, had) in book.Events.Where(kv => !wanted.ContainsKey(kv.Key)).ToList())
        {
            await service.Remove(had.Id, cal);
            book.Events.Remove(uid);
        }
        SaveBook(book, id);
        return wanted.Count;
    }

    /// <summary>What was written into the account's Simpl calendar, taken out (Simpl's calendar no longer written there).</summary>
    private static async Task TakeOut(string id)
    {
        if (Accounts.FirstOrDefault(a => a.Id == id) is not { } a || a.SimplCalendar is not { } cal || !Busy.Add(id)) return;
        Tell();
        try
        {
            var service = Service(a);
            var book = ReadBook(id);
            foreach (var (uid, had) in book.Events.ToList())
            {
                try { await service.Remove(had.Id, cal); } catch { /* (gone or not, forgotten here) */ }
                book.Events.Remove(uid);
            }
            SaveBook(book, id);
            a.Pushed = 0;
            Save();
        }
        catch (Exception e) { Log.Write($"calendar accounts: {a.Kind} not taken out: {e.Message}"); }
        finally
        {
            Busy.Remove(id);
            Tell();
        }
    }

    private static ICalendarService Service(CalendarAccount a)
    {
        var secret = Secrets().GetValueOrDefault(a.Id);
        if (a.Kind == CalendarKinds.ICloud)
        {
            if (secret?.Password is not { Length: > 0 } pass) throw CalendarException.SignedOut();
            return new ICloudCalendar(a.Name, pass);
        }
        if (OAuthClient.Of(a.Kind) is not { } client) throw new CalendarException(CalendarFailure.Other, $"{CalendarKinds.Name(a.Kind)} sign-in is not ready in this version.");
        if (secret?.Tokens is not { } tokens) throw CalendarException.SignedOut();
        var web = new CalendarWeb(client, tokens, fresh => SetSecret(a.Id, new CalendarSecret { Tokens = fresh }));
        return a.Kind == CalendarKinds.Google ? new GoogleCalendar(web) : new OutlookCalendar(web);
    }

    // ---- the page's storage ----

    /// <summary>A shown calendar's id among the page's linked calendars: short, and the same each time.</summary>
    public static string LinkId(string account, string calendar) =>
        "a" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{account}\u001f{calendar}")), 0, 8).ToLowerInvariant();

    /// <summary>The page's linked calendars: those linked by their address as they are, then every shown calendar of every
    /// account (source "account": background.js leaves them to the app); the events of one no longer shown taken away.</summary>
    private static void WriteLinks()
    {
        var had = Engine.Bridge.Shared.Value("cal:links") as JsonArray ?? new JsonArray();
        var byAddress = had.OfType<JsonObject>().Where(l => CalendarHttp.Str(l["source"]) != "account").Select(l => l.DeepClone()).ToList();
        var hadIds = had.OfType<JsonObject>().Where(l => CalendarHttp.Str(l["source"]) == "account").Select(l => CalendarHttp.Str(l["id"])).OfType<string>().ToHashSet();
        var mine = new List<JsonNode>();
        foreach (var a in Accounts.Where(a => !a.SignedOut))
            foreach (var c in a.Calendars.Where(c => c.On))
                mine.Add(new JsonObject
                {
                    ["id"] = LinkId(a.Id, c.Id), ["name"] = c.Name, ["color"] = c.Color, ["on"] = true, ["source"] = "account",
                    ["account"] = a.Kind, ["accountName"] = a.Name,
                });
        var now = mine.Select(l => CalendarHttp.Str(l["id"])).OfType<string>().ToHashSet();
        var links = new JsonArray(byAddress.Concat(mine).ToArray());
        Engine.Bridge.Shared.Keep(new JsonObject { ["cal:links"] = links }, hadIds.Except(now).Select(x => $"cal:feed:{x}"));
    }

    /// <summary>The calendars shown that are not Canvas's: each account's turned on (for Calendars' count).</summary>
    public static int ShownCount => Accounts.Where(a => !a.SignedOut).Sum(a => a.Calendars.Count(c => c.On));

    // ---- kept ----

    private static PushedBook ReadBook(string id)
    {
        try
        {
            if (Prefs.String(BookKey + id) is { Length: > 0 } json && JsonSerializer.Deserialize<PushedBook>(json, Json.Options) is { } b) return b;
        }
        catch { /* (an unreadable book: everything written again) */ }
        return new PushedBook();
    }

    private static void SaveBook(PushedBook b, string id) => Prefs.Set(BookKey + id, JsonSerializer.Serialize(b, Json.Options));

    private static Dictionary<string, CalendarSecret> Secrets()
    {
        if (secrets != null) return secrets;
        secrets = new Dictionary<string, CalendarSecret>();
        try
        {
            var file = Vault.Read(SecretFile, SecretPurpose);
            if (file.State == VaultState.Sealed && file.Text is { } text && JsonSerializer.Deserialize<Dictionary<string, CalendarSecret>>(text, Json.Options) is { } kept) secrets = kept;
            file.Zero();
        }
        catch (Exception e) { Log.Write("calendar accounts: sign-ins not read: " + e.Message); }
        return secrets;
    }

    private static void SetSecret(string id, CalendarSecret? s)
    {
        var all = Secrets();
        if (s == null) all.Remove(id); else all[id] = s;
        try
        {
            if (all.Count == 0) File.Delete(SecretFile);
            else Vault.Write(SecretFile, JsonSerializer.Serialize(all, Json.Options), SecretPurpose);
        }
        catch (Exception e) { Log.Write("calendar accounts: sign-ins not sealed, kept until Simpl quits: " + e.Message); }
    }

    // ---- the pictures' accounts ----

    private static List<CalendarAccount> DemoAccounts()
    {
        var ago = DateTime.UtcNow.AddMinutes(-2);
        return new List<CalendarAccount>
        {
            new()
            {
                Id = "icloud:sam.student@icloud.com", Kind = CalendarKinds.ICloud, Name = "sam.student@icloud.com", Push = true, SimplCalendar = "simpl",
                Synced = ago, Pulled = 23, Pushed = 41,
                Calendars = new()
                {
                    new AccountCalendar { Id = "home", Name = "Home", Color = "#34c759", On = true },
                    new AccountCalendar { Id = "work", Name = "Work", Color = "#ff9500" },
                    new AccountCalendar { Id = "family", Name = "Family", Color = "#af52de" },
                },
            },
            new()
            {
                Id = "google:sam.student@gmail.com", Kind = CalendarKinds.Google, Name = "sam.student@gmail.com", Push = false, Synced = ago, Pulled = 58,
                Calendars = new()
                {
                    new AccountCalendar { Id = "primary", Name = "Sam Student", Color = "#4285f4", On = true },
                    new AccountCalendar { Id = "club", Name = "Robotics Club", Color = "#af52de", On = true },
                    new AccountCalendar { Id = "holidays", Name = "Holidays in United States", Color = "#0b8043" },
                },
            },
        };
    }
}
