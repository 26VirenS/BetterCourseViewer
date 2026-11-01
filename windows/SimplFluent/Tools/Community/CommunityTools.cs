using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Simpl.Screens;
using Simpl.Support;
using Wpf.Ui.Controls;

namespace Simpl.Tools;

/// <summary>
/// (0.3.105) Simpl Community tools (docs/community-tools.md): small tools students made — Ohm's law, a tip splitter, a
/// grade-points table — read from the site's gallery (simplcourses.com/api/community/tools, open to the apps, over the
/// same connection Appearance's Community themes use: Support/Community.cs), and the ones the student adds, kept on this
/// PC. A tool is data, never code: Add downloads it (api/community/tools/&lt;id&gt;), checks it again here
/// (ToolFormat.Validate), and keeps it — sealed by the vault, as the flashcards are, in community-tools.json — or, when it
/// fails, says why and keeps nothing. An added tool is a tile on the Tools page and opens in a popup like Simpl's own
/// (key "community:&lt;id&gt;"), remembers the values last put in (the preferences, per tool), and works with no
/// connection. It cannot be pinned: the title bar's pins are Simpl's own tools.
/// </summary>
public static class CommunityTools
{
    /// <summary>An added tool's key among the tools: "community:" and its id.</summary>
    public const string Prefix = "community:";
    private const string Purpose = "community-tools";

    /// <summary>A tool added or removed, the gallery read, a tool being added: on the UI thread, as each is.</summary>
    public static event Action? Changed;

    public static bool IsKey(string? key) => key != null && key.StartsWith(Prefix, StringComparison.Ordinal);

    public static string KeyOf(string id) => Prefix + id;

    // ---- what the spec's names are here ----

    /// <summary>The spec's symbols as Fluent System Icons (WPF-UI's, the app's every icon): the nearest at most U+FFFF,
    /// which WPF-UI draws (README, Icons) — percent, triangle and atom have none of their own there.</summary>
    public static SymbolRegular SymbolOf(string symbol) => symbol switch
    {
        "bolt" => SymbolRegular.Flash24, "percent" => SymbolRegular.DocumentPercent24, "function" => SymbolRegular.MathFormula24,
        "sum" => SymbolRegular.Autosum24, "divide" => SymbolRegular.MathSymbols24, "ruler" => SymbolRegular.Ruler24,
        "scale" => SymbolRegular.Scales24, "thermometer" => SymbolRegular.Temperature24, "clock" => SymbolRegular.Clock24,
        "calendar" => SymbolRegular.CalendarLtr24, "money" => SymbolRegular.Money24, "chart" => SymbolRegular.DataBarVertical24,
        "atom" => SymbolRegular.Molecule24, "flask" => SymbolRegular.Beaker24, "leaf" => SymbolRegular.LeafOne24,
        "heart" => SymbolRegular.Heart24, "book" => SymbolRegular.Book24, "pencil" => SymbolRegular.Edit24,
        "globe" => SymbolRegular.Globe24, "car" => SymbolRegular.VehicleCar24, "drop" => SymbolRegular.Drop24,
        "flame" => SymbolRegular.Fire24, "gauge" => SymbolRegular.Gauge24, "star" => SymbolRegular.Star24,
        "lightbulb" => SymbolRegular.Lightbulb24, "music" => SymbolRegular.MusicNote124, "dice" => SymbolRegular.Cube24,
        "triangle" => SymbolRegular.Shapes24,
        _ => SymbolRegular.Apps24,
    };

    /// <summary>The five groups as the browser's filter names them.</summary>
    public static string GroupName(string group) => group switch
    {
        "study" => "Study", "math" => "Math", "science" => "Science", "money" => "Money", "everyday" => "Everyday", _ => group,
    };

    /// <summary>A checked tool as one of the tools: its key, its name, its line, its symbol and its colour.</summary>
    public static ToolsScreen.Kind Kind(CommunityTool t) => new(KeyOf(t.Id ?? ""), t.Name, t.Summary, SymbolOf(t.Symbol), t.Color);

    /// <summary>An added tool by its key ("community:&lt;id&gt;"), as one of the tools; null for any other key.</summary>
    public static ToolsScreen.Kind? KindOf(string key) => IsKey(key) && Find(key[Prefix.Length..]) is { } t ? Kind(t) : null;

    // ---- the tools added, kept ----

    private sealed class Kept
    {
        public required string Raw { get; init; }
        public required string Added { get; init; }
        public CommunityTool? Tool { get; init; }
    }

    private static readonly object FileGate = new();
    private static List<Kept>? kept;

    private static List<Kept> All => kept ??= Load();

    /// <summary>The tools added that this Simpl can use, in the order they were added.</summary>
    public static IReadOnlyList<CommunityTool> Added => All.Select(k => k.Tool).OfType<CommunityTool>().ToList();

    public static CommunityTool? Find(string id) => All.FirstOrDefault(k => k.Tool?.Id == id)?.Tool;

    public static bool Has(string id) => Find(id) != null;

    private static List<Kept> Load()
    {
        lock (FileGate)
        {
            var file = Vault.Read(AppPaths.CommunityTools, Purpose);
            try
            {
                if (file.Text is not { } text) return new();
                using var doc = JsonDocument.Parse(text);
                var list = new List<Kept>();
                if (doc.RootElement.TryGetProperty("tools", out var tools) && tools.ValueKind == JsonValueKind.Array)
                    foreach (var e in tools.EnumerateArray())
                    {
                        if (!e.TryGetProperty("tool", out var raw)) continue;
                        var added = e.TryGetProperty("added", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString()! : "";
                        // (checked again each time it is read: one this Simpl cannot use is kept as it was, not shown)
                        var check = ToolFormat.Validate(raw);
                        if (!check.Ok) Log.Write($"community tools: a kept tool cannot be used here ({check.Refusal!.Code})");
                        list.Add(new Kept { Raw = raw.GetRawText(), Added = added, Tool = check.Ok && check.Tool!.Id != null ? check.Tool : null });
                    }
                if (file.Legacy) Write(list);
                return list;
            }
            catch (Exception e)
            {
                Log.Write($"community tools: {e.Message}");
                return new();
            }
            finally { file.Zero(); }
        }
    }

    private static void Write(List<Kept> list)
    {
        var o = new JsonObject
        {
            ["tools"] = new JsonArray(list.Select(k => (JsonNode)new JsonObject { ["added"] = k.Added, ["tool"] = JsonNode.Parse(k.Raw) }).ToArray()),
        };
        var json = Encoding.UTF8.GetBytes(o.ToJsonString(Json.Script));
        try { lock (FileGate) Vault.Write(AppPaths.CommunityTools, json, Purpose); }
        catch (Exception e) { Log.Write($"community tools: not kept: {e.Message}"); }
        finally { CryptographicOperations.ZeroMemory(json); }
    }

    /// <summary>A tool checked here kept (one with its id already kept: replaced, in its place).</summary>
    private static void Keep(JsonElement raw, CommunityTool tool)
    {
        var k = new Kept { Raw = raw.GetRawText(), Added = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), Tool = tool };
        var at = All.FindIndex(x => x.Tool?.Id == tool.Id);
        if (at >= 0) All[at] = k; else All.Add(k);
        Write(All);
    }

    /// <summary>An added tool taken off the Tools page: its popup closed, the values put in it forgotten.</summary>
    public static void Remove(string id)
    {
        if (All.RemoveAll(k => k.Tool?.Id == id) == 0) return;
        Write(All);
        Prefs.Remove(ValuesKey(id));
        ToolPopup.CloseAll(KeyOf(id));
        Log.Write($"community tools: removed {id}");
        Changed?.Invoke();
    }

    // ---- the values put in, remembered per tool ----

    private static string ValuesKey(string id) => $"community-values:{id}";

    /// <summary>The values last put in a tool, by its inputs' ids: a number, a choice's value, a toggle's true or false.</summary>
    public static Dictionary<string, object?> Values(string id)
    {
        var d = new Dictionary<string, object?>(StringComparer.Ordinal);
        try
        {
            if (Prefs.String(ValuesKey(id)) is not { Length: > 0 } s) return d;
            using var doc = JsonDocument.Parse(s);
            foreach (var p in doc.RootElement.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDouble(out var v)) d[p.Name] = v;
                else if (p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False) d[p.Name] = p.Value.GetBoolean();
        }
        catch (Exception e) { Log.Write($"community tools: values: {e.Message}"); }
        return d;
    }

    public static void Remember(string id, IReadOnlyDictionary<string, object?> values)
    {
        var o = new JsonObject();
        foreach (var (k, v) in values)
            if (v is double d && double.IsFinite(d)) o[k] = d;
            else if (v is bool b) o[k] = b;
        Prefs.Set(ValuesKey(id), o.Count == 0 ? null : o.ToJsonString());
    }

    // ---- the gallery ----

    public enum Phase { Idle, Loading, Ready, Failed }

    /// <summary>A tool in the gallery: checked here (its tool), or refused (why — one that needs a newer Simpl is still
    /// listed, as such), with its name and line as the gallery gave them.</summary>
    public sealed record Entry(string Id, string Name, string Summary, string Maker, string Group, double? Version, CommunityTool? Tool, ToolRefusal? Refusal)
    {
        public bool NeedsNewer => Refusal?.Code == "SC-T-FORMAT" && Refusal.Message.Contains("newer", StringComparison.Ordinal);
    }

    public static Phase State { get; private set; } = Phase.Idle;

    /// <summary>Why the gallery could not be read, and whether because there was no connection.</summary>
    public static string? Why { get; private set; }
    public static bool Offline { get; private set; }

    public static IReadOnlyList<Entry> Gallery { get; private set; } = Array.Empty<Entry>();

    /// <summary>The tool being added now (its id), if one is.</summary>
    public static string? Busy { get; private set; }

    /// <summary>Why a tool could not be added, by its id (until it is tried again).</summary>
    public static IReadOnlyDictionary<string, string> Problems => problems;
    private static readonly Dictionary<string, string> problems = new(StringComparer.Ordinal);

    private static Uri Api(string path) => new(Community.Site, "api/community/" + path);

    private static string Unreachable => "Simpl Community can’t be reached. Check your connection and try again — the tools you added still work without one.";

    /// <summary>The gallery read (the first time the browser opens; again with Try again).</summary>
    public static async Task Load(bool force = false)
    {
        if (State == Phase.Loading || (State == Phase.Ready && !force)) return;
        State = Phase.Loading;
        Why = null;
        Offline = false;
        Changed?.Invoke();
        Shot.Began();
        try
        {
            string body;
            System.Net.HttpStatusCode status;
            try
            {
                using var res = await Community.Http.GetAsync(Api("tools"));
                status = res.StatusCode;
                body = await res.Content.ReadAsStringAsync();
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
            {
                Log.Write($"community tools: the gallery could not be read: {e.Message}");
                Fail(Unreachable, offline: true);
                return;
            }
            JsonDocument doc;
            try { doc = JsonDocument.Parse(body); }
            catch (JsonException)
            {
                Log.Write($"community tools: the gallery's answer could not be read ({(int)status})");
                Fail("Simpl Community’s tools could not be read just now. Try again in a little while.");
                return;
            }
            using (doc)
            {
                var root = doc.RootElement;
                var message = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
                if ((int)status is < 200 or > 299 || root.ValueKind != JsonValueKind.Object || (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
                    || !root.TryGetProperty("tools", out var tools) || tools.ValueKind != JsonValueKind.Array)
                {
                    Log.Write($"community tools: the gallery said no ({(int)status}{(message != null ? ": " + message : "")})");
                    Fail(message ?? "Simpl Community’s tools could not be read just now. Try again in a little while.");
                    return;
                }
                var list = new List<Entry>();
                var refused = 0;
                foreach (var t in tools.EnumerateArray().Take(300))
                {
                    if (EntryOf(t) is { } e) list.Add(e); else refused++;
                }
                Gallery = list;
                State = Phase.Ready;
                Log.Write($"community tools: {list.Count} read ({list.Count(e => e.Tool != null)} to add, {list.Count(e => e.NeedsNewer)} need a newer Simpl{(refused > 0 ? $", {refused} that cannot be used left out" : "")})");
                Changed?.Invoke();
            }
        }
        finally { Shot.Ended(); }
    }

    /// <summary>A gallery's tool as the browser lists it: checked here; one refused for anything but a newer format is
    /// left out (the site checks every tool strictly, so none should be).</summary>
    private static Entry? EntryOf(JsonElement t)
    {
        if (t.ValueKind != JsonValueKind.Object) return null;
        string Text(string k, int most)
        {
            var s = ToolFormat.Clean(t.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null);
            return s.Length > most ? s[..most] : s;
        }
        var id = t.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String && ToolFormat.IsSiteId(i.GetString()!) ? i.GetString()! : null;
        if (id == null) return null;
        var check = ToolFormat.Validate(t);
        var version = t.TryGetProperty("version", out var ver) && ver.ValueKind == JsonValueKind.Number && ver.TryGetDouble(out var vd) ? vd : (double?)null;
        var e = new Entry(id, check.Tool?.Name ?? Text("name", ToolFormat.Limits.Name), check.Tool?.Summary ?? Text("summary", ToolFormat.Limits.Summary),
            check.Tool?.Maker ?? Text("maker", ToolFormat.Limits.Maker), check.Tool?.Group ?? Text("group", 12), version, check.Tool, check.Refusal);
        if (e.Tool != null || (e.NeedsNewer && e.Name.Length > 0)) return e;
        Log.Write($"community tools: {id} cannot be used here ({check.Refusal?.Code} at {check.Refusal?.Path})");
        return null;
    }

    private static void Fail(string why, bool offline = false)
    {
        State = Phase.Failed;
        Why = why;
        Offline = offline;
        Changed?.Invoke();
    }

    /// <summary>A gallery's tool there to add: not added yet, or added at an earlier version of it.</summary>
    public static bool Newer(Entry e) => e.Tool != null && Find(e.Id) is { } have && (e.Version ?? 0) > (have.Version ?? 0);

    /// <summary>
    /// A tool added: downloaded from the gallery by its id, checked again here, and kept — or, when it cannot be read,
    /// fails here or Simpl Community cannot be reached, why said (<see cref="Problems"/>) and nothing kept. True when kept.
    /// </summary>
    public static async Task<bool> Add(string id)
    {
        if (Busy != null || !ToolFormat.IsSiteId(id)) return false;
        Busy = id;
        problems.Remove(id);
        Changed?.Invoke();
        Shot.Began();
        try
        {
            string body;
            int status;
            try
            {
                using var res = await Community.Http.GetAsync(Api("tools/" + id));
                status = (int)res.StatusCode;
                body = await res.Content.ReadAsStringAsync();
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
            {
                Log.Write($"community tools: {id} could not be downloaded: {e.Message}");
                problems[id] = "Simpl Community can’t be reached. Check your connection and try again.";
                return false;
            }
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (status is < 200 or > 299 || root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("tool", out var raw))
                {
                    var message = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
                    Log.Write($"community tools: {id} was not given ({status}{(message != null ? ": " + message : "")})");
                    problems[id] = message ?? "That tool isn’t in Simpl Community any more.";
                    return false;
                }
                // (checked again on this PC, whatever the site said of it: one that fails here is not kept)
                var check = ToolFormat.Validate(raw);
                if (!check.Ok)
                {
                    Log.Write($"community tools: {id} refused here ({check.Refusal!.Code} at {check.Refusal.Path})");
                    problems[id] = check.Refusal.Message;
                    return false;
                }
                if (check.Tool!.Id != id)
                {
                    Log.Write($"community tools: {id} came back as {check.Tool.Id ?? "a tool with no id"}");
                    problems[id] = "That tool could not be read.";
                    return false;
                }
                Keep(raw, check.Tool);
                Log.Write($"community tools: added {id} ({check.Tool.Name}, version {(check.Tool.Version ?? 0).ToString(CultureInfo.InvariantCulture)})");
                return true;
            }
            catch (JsonException)
            {
                Log.Write($"community tools: {id}: the answer could not be read ({status})");
                problems[id] = "That tool could not be read.";
                return false;
            }
        }
        finally
        {
            Busy = null;
            Shot.Ended();
            Changed?.Invoke();
        }
    }

    // ---- the screenshot suite's ----

    private static bool launched;

    /// <summary>
    /// The screenshot suite's --community-add s-ohms,s-gpa (the gallery's tools added, each as its Add button adds it),
    /// --community-values "v=9,r=3" (put in the tool --community-open names, as if typed) and --community-open s-ohms (that
    /// tool opened in its popup once it is added). Once, from the Tools page.
    /// </summary>
    public static async Task OnLaunch(Engine.Engine engine)
    {
        if (launched) return;
        launched = true;
        var add = Launch.Get("community-add");
        var open = Launch.Get("community-open");
        if (add == null && open == null) return;
        Shot.Began();
        try
        {
            foreach (var id in (add ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                // (one at a time, as presses come)
                while (Busy != null) await Task.Delay(100);
                if (!await Add(id)) Log.Write($"community tools: {id} not added: {(problems.TryGetValue(id, out var why) ? why : "")}");
            }
            if (open is not { Length: > 0 }) return;
            if (Find(open) is not { } tool) { Log.Write($"community tools: no tool {open} to open"); return; }
            if (Launch.Get("community-values") is { Length: > 0 } given)
            {
                var values = Values(open);
                foreach (var part in given.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var eq = part.IndexOf('=');
                    if (eq <= 0 || tool.Inputs.FirstOrDefault(x => x.Id == part[..eq].Trim()) is not { } input) continue;
                    var v = part[(eq + 1)..].Trim();
                    if (input.Kind == ToolInputKind.Toggle) values[input.Id] = v is "true" or "yes" or "1" or "on";
                    else if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) values[input.Id] = d;
                }
                Remember(open, values);
            }
            ToolsCenter.Show(KeyOf(open), engine);
        }
        finally { Shot.Ended(); }
    }
}
