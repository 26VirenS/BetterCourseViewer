using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Simpl.Tools;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// Simpl Community tools, format 1 (Tools/Community/ToolFormat.cs) against the answers every app's copy must give:
/// Vectors/community-tools-vectors.json, a copy of the site's docs/community-tools-vectors.json, which
/// scripts/dev/tool-vectors.mjs writes from the reference (tool-format.js) — copied here as it is, never edited by hand.
/// Every formula's text, every refusal, every tool's verdict (its code when refused, the tool as checked when not) and
/// every run's texts and shown results. Pure: no window, no profile, nothing of WPF's.
/// </summary>
public class CommunityToolFormatTests
{
    private static readonly Lazy<JsonDocument> Vectors = new(() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Vectors", "community-tools-vectors.json"), Encoding.UTF8)));

    private static JsonElement Part(string name) => Vectors.Value.RootElement.GetProperty(name);

    /// <summary>A number as the vectors write it ("NaN", "Infinity", "-Infinity" and "-0" as strings).</summary>
    private static double Decoded(JsonElement v) => v.ValueKind == JsonValueKind.String
        ? v.GetString() switch { "NaN" => double.NaN, "Infinity" => double.PositiveInfinity, "-Infinity" => double.NegativeInfinity, "-0" => -0.0, var s => double.Parse(s!, CultureInfo.InvariantCulture) }
        : v.GetDouble();

    [Fact]
    public void EveryFormulaGivesItsText()
    {
        var wrong = new List<string>();
        var n = 0;
        foreach (var e in Part("eval").EnumerateArray())
        {
            var expr = e.GetProperty("expr").GetString()!;
            var names = e.GetProperty("names").EnumerateObject().ToDictionary(p => p.Name, p => Decoded(p.Value));
            var decimals = e.GetProperty("decimals").GetInt32();
            var want = e.GetProperty("text").GetString();
            var got = ToolFormat.FormatNumber(ToolFormat.Evaluate(expr, names), decimals);
            if (got != want) wrong.Add($"{expr} ({decimals}): {got}, not {want}");
            n++;
        }
        Assert.True(n > 100, $"only {n} formulas read");
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    [Fact]
    public void EveryFormulaThatCannotBeUsedIsRefused()
    {
        var wrong = new List<string>();
        var n = 0;
        foreach (var e in Part("errors").EnumerateArray())
        {
            var expr = e.GetProperty("expr").GetString()!;
            var names = e.GetProperty("names").EnumerateArray().Select(x => x.GetString()!).ToList();
            // (the formula alone, as the vectors' writer checks it: a tool reading the names it may, its one result the formula)
            var inputs = new JsonArray((names.Count > 0 ? names : new List<string> { "zz" }).Select(id => (JsonNode)new JsonObject { ["id"] = id, ["kind"] = "number", ["label"] = id }).ToArray());
            var tool = new JsonObject
            {
                ["format"] = 1, ["name"] = "X", ["summary"] = "Y", ["group"] = "math", ["symbol"] = "sum", ["color"] = "#000000", ["inputs"] = inputs,
                ["outputs"] = new JsonArray(new JsonObject { ["label"] = "B", ["value"] = expr }),
            };
            var check = ToolFormat.Validate(tool.ToJsonString());
            if (check.Ok) wrong.Add($"“{expr}” was not refused");
            else if (check.Refusal!.Code is not ("SC-T-FORMULA" or "SC-T-NAME-USE")) wrong.Add($"“{expr}”: {check.Refusal.Code}, not a formula's refusal");
            if (ToolFormat.FormulaProblem(expr, names) == null) wrong.Add($"“{expr}”: no problem found with it alone");
            n++;
        }
        Assert.True(n > 20, $"only {n} refusals read");
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    [Fact]
    public void EveryToolIsCheckedAndRunAsTheReferenceDoes()
    {
        var wrong = new List<string>();
        var runs = 0;
        foreach (var e in Part("tools").EnumerateArray())
        {
            var name = e.GetProperty("name").GetString();
            var raw = e.GetProperty("tool");
            // (read both ways the reference takes one: as the element the gallery's list holds, and as text)
            var check = ToolFormat.Validate(raw);
            var fromText = ToolFormat.Validate(raw.GetRawText());
            if (check.Ok != fromText.Ok || check.Refusal?.Code != fromText.Refusal?.Code) wrong.Add($"{name}: read as text it is {fromText.Refusal?.Code ?? "valid"}");
            if (!e.GetProperty("valid").GetBoolean())
            {
                var code = e.GetProperty("code").GetString();
                if (check.Ok) wrong.Add($"{name}: valid, not refused with {code}");
                else if (check.Refusal!.Code != code) wrong.Add($"{name}: refused with {check.Refusal.Code} ({check.Refusal.Message}), not {code}");
                continue;
            }
            if (!check.Ok) { wrong.Add($"{name}: refused with {check.Refusal!.Code} ({check.Refusal.Message} at {check.Refusal.Path})"); continue; }
            var want = Canonical(e.GetProperty("checked"));
            var got = Canonical(JsonDocument.Parse(ToolFormat.ToJson(check.Tool!).ToJsonString()).RootElement);
            if (want != got) wrong.Add($"{name}: checked as\n  {got}\nnot\n  {want}");
            // (what this app keeps — the checked tool as JSON — reads back as the same tool)
            var again = ToolFormat.Validate(ToolFormat.ToJson(check.Tool!).ToJsonString());
            if (!again.Ok || Canonical(JsonDocument.Parse(ToolFormat.ToJson(again.Tool!).ToJsonString()).RootElement) != got) wrong.Add($"{name}: kept, it does not read back the same");
            foreach (var run in e.GetProperty("runs").EnumerateArray())
            {
                var given = run.GetProperty("given").EnumerateObject().ToDictionary(p => p.Name, p => Given(p.Value));
                var results = ToolFormat.Run(check.Tool!, given);
                var expected = run.GetProperty("results").EnumerateArray().ToList();
                if (results.Count != expected.Count) { wrong.Add($"{name} {run.GetProperty("given").GetRawText()}: {results.Count} results, not {expected.Count}"); continue; }
                for (var i = 0; i < results.Count; i++)
                {
                    var text = expected[i].GetProperty("text").GetString();
                    var shown = expected[i].GetProperty("shown").GetBoolean();
                    if (results[i].Text != text || results[i].Shown != shown)
                        wrong.Add($"{name} {run.GetProperty("given").GetRawText()} result {i}: {results[i].Text} ({(results[i].Shown ? "shown" : "hidden")}), not {text} ({(shown ? "shown" : "hidden")})");
                }
                runs++;
            }
        }
        Assert.True(runs > 5, $"only {runs} runs read");
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    [Fact]
    public void ANewerFormatSaysSo()
    {
        var check = ToolFormat.Validate("""{"format":2,"name":"X","summary":"Y","group":"math","symbol":"sum","color":"#000000","inputs":[],"outputs":[]}""");
        Assert.Equal("SC-T-FORMAT", check.Refusal?.Code);
        Assert.Equal("This tool needs a newer Simpl.", check.Refusal?.Message);
        Assert.Equal("That is not a Simpl tool.", ToolFormat.Validate("""{"name":"X"}""").Refusal?.Message);
        Assert.Equal("SC-T-JSON", ToolFormat.Validate("not json").Refusal?.Code);
        Assert.Equal("SC-T-BIG", ToolFormat.Validate(new string(' ', 40000)).Refusal?.Code);
    }

    [Theory]
    [InlineData(90, "90")]
    [InlineData(3.5, "3.5")]
    [InlineData(0.000001, "0.000001")]
    [InlineData(1e-7, "1e-7")]
    [InlineData(1e21, "1e+21")]
    [InlineData(123456789012345680000.0, "123456789012345680000")]
    [InlineData(-0.5, "-0.5")]
    [InlineData(0.1 + 0.2, "0.30000000000000004")]
    public void ANumberCellIsWrittenAsJavaScriptWritesIt(double v, string text) => Assert.Equal(text, ToolFormat.JsNumber(v));

    /// <summary>A given value as the reference's runTool takes it: a number, true or false, or anything else (text, null).</summary>
    private static object? Given(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Number => v.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => v.GetString(),
        _ => null,
    };

    /// <summary>JSON written one way whatever its order: keys sorted, numbers as the shortest that read back.</summary>
    private static string Canonical(JsonElement e)
    {
        var sb = new StringBuilder();
        void Write(JsonElement x)
        {
            switch (x.ValueKind)
            {
                case JsonValueKind.Object:
                    sb.Append('{');
                    var first = true;
                    foreach (var p in x.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        sb.Append(JsonSerializer.Serialize(p.Name)).Append(':');
                        Write(p.Value);
                    }
                    sb.Append('}');
                    break;
                case JsonValueKind.Array:
                    sb.Append('[');
                    var i = 0;
                    foreach (var y in x.EnumerateArray()) { if (i++ > 0) sb.Append(','); Write(y); }
                    sb.Append(']');
                    break;
                case JsonValueKind.Number: sb.Append(x.GetDouble().ToString("R", CultureInfo.InvariantCulture)); break;
                case JsonValueKind.String: sb.Append(JsonSerializer.Serialize(x.GetString())); break;
                default: sb.Append(x.GetRawText()); break;
            }
        }
        Write(e);
        return sb.ToString();
    }
}
