using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Simpl.Support;

/// <summary>JSON as the page speaks it (camelCase, every field optional) and as JavaScript source.</summary>
public static class Json
{
    /// <summary>Reading the page's answers: names in any case, numbers given as strings too, nothing required.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Written for JavaScript: no HTML escaping of quotes and angle brackets (the text is code, not markup).</summary>
    public static readonly JsonSerializerOptions Script = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static T? Read<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    public static string Write(object? value) => JsonSerializer.Serialize(value, Script);

    /// <summary>A JSON literal that is also valid JavaScript (U+2028 and U+2029 escaped).</summary>
    public static string Literal(object? value)
    {
        var text = value is JsonNode node ? node.ToJsonString(Script) : JsonSerializer.Serialize(value, Script);
        return text.Replace("\u2028", "\\u2028").Replace("\u2029", "\\u2029");
    }

    /// <summary>A string as a JavaScript string literal.</summary>
    public static string Str(string s) => Literal(s);
}
