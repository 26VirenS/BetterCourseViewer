using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Simpl.Tools.Verbs;

// Verb Conjugations' rules engine (tools), free of any window: a port, line for line, of the reference engine that comes
// with the language data (scratchpad tools/verbs/check.mjs, the normative reading of the JSON schema: the Mac runs the
// same). A verb's forms in a tense come from its irregular entry where it has one, else from its class's endings on its
// stem — a stem change, a spelling change and an accent applied — or are composed of other forms (a compound tense of
// an auxiliary and a participle, a tense made of another's persons), a separable German prefix split off and put back
// after. The five languages' data files are embedded resources (Tools/Study/Verbs/Data). Tested in
// SimplFluent.Tests/VerbsTests.cs with the data's own test vectors.

/// <summary>A tense as the data names it: its id, its name in the language, its group, and whether it is one form (a participle) rather than six.</summary>
internal sealed record VerbTense(string Id, string Name, string Group, bool Single);

/// <summary>A language's data file, read once.</summary>
internal sealed class VerbLanguage
{
    public string Code { get; }
    public string Name { get; }
    public IReadOnlyList<string> Pronouns { get; }
    public IReadOnlyList<string> ImperativePronouns { get; }
    public IReadOnlyList<VerbTense> Tenses { get; }
    public IReadOnlyDictionary<string, string> GroupNames { get; }
    public IReadOnlyList<string> CommonVerbs { get; }
    internal JsonElement Rules { get; }
    internal JsonElement Irregulars { get; }
    internal JsonElement StemChangeKinds { get; }
    internal JsonElement StemChanging { get; }
    internal HashSet<string> Separable { get; }
    private readonly JsonDocument doc;

    /// <summary>Every verb the data knows by name: the common ones, the irregular ones, the stem-changing and the separable.</summary>
    public IReadOnlyList<string> Known { get; }

    private VerbLanguage(JsonDocument d)
    {
        doc = d;
        var root = d.RootElement;
        Code = root.GetProperty("language").GetString() ?? "";
        Name = root.GetProperty("name").GetString() ?? Code;
        Pronouns = Strings(root.GetProperty("pronouns"));
        ImperativePronouns = Strings(root.GetProperty("imperativePronouns"));
        Tenses = root.GetProperty("tenses").EnumerateArray()
            .Select(t => new VerbTense(t.GetProperty("id").GetString() ?? "", t.GetProperty("name").GetString() ?? "", t.GetProperty("group").GetString() ?? "", t.TryGetProperty("single", out var s) && s.ValueKind == JsonValueKind.True))
            .ToList();
        GroupNames = root.GetProperty("groupNames").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? p.Name);
        CommonVerbs = Strings(root.GetProperty("commonVerbs"));
        Rules = root.GetProperty("rules");
        Irregulars = root.GetProperty("irregulars");
        StemChangeKinds = root.GetProperty("stemChangeKinds");
        StemChanging = root.GetProperty("stemChanging");
        Separable = root.TryGetProperty("separable", out var sep) && sep.ValueKind == JsonValueKind.Array ? Strings(sep).ToHashSet() : new HashSet<string>();
        var known = new List<string>(CommonVerbs);
        var seen = new HashSet<string>(CommonVerbs);
        foreach (var p in Irregulars.EnumerateObject()) if (seen.Add(p.Name)) known.Add(p.Name);
        foreach (var p in StemChanging.EnumerateObject()) if (seen.Add(p.Name)) known.Add(p.Name);
        foreach (var v in Separable) if (seen.Add(v)) known.Add(v);
        known.Sort(StringComparer.Ordinal);
        Known = known;
    }

    private static List<string> Strings(JsonElement a) => a.EnumerateArray().Select(e => e.GetString() ?? "").ToList();

    public VerbTense? Tense(string id) => Tenses.FirstOrDefault(t => t.Id == id);

    public bool Knows(string verb) => Known.Contains(verb);

    /// <summary>The language's data read from the app's resources (Simpl.Verbs.&lt;code&gt;.json), once each.</summary>
    public static VerbLanguage Load(string code)
    {
        lock (Loaded)
        {
            if (Loaded.TryGetValue(code, out var l)) return l;
            var name = $"Simpl.Verbs.{code}.json";
            using var s = typeof(VerbLanguage).Assembly.GetManifestResourceStream(name) ?? throw new FileNotFoundException($"No verb data for {code}.", name);
            var d = JsonDocument.Parse(s, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            return Loaded[code] = new VerbLanguage(d);
        }
    }

    /// <summary>A language's data read from a file (the tests, against the data as it is in the repo).</summary>
    public static VerbLanguage FromJson(string json) => new(JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }));

    private static readonly Dictionary<string, VerbLanguage> Loaded = new();

    /// <summary>The five, in the picker's order, by their code.</summary>
    public static readonly (string Code, string Name)[] All = { ("es", "Spanish"), ("fr", "French"), ("de", "German"), ("it", "Italian"), ("pt", "Portuguese") };
}

/// <summary>A verb's forms in a tense: six (one a pronoun, "—" for a person that has none) or one for a single-form tense.</summary>
internal sealed record VerbForms(IReadOnlyList<string> Forms)
{
    public const string Placeholder = "—";
    public bool Single => Forms.Count == 1;
    public string One => Forms[0];
}

/// <summary>The engine (check.mjs, conjugate and table).</summary>
internal static class VerbEngine
{
    /// <summary>A whole table: every tense's forms, and which of them are irregular (differ from the purely regular result).</summary>
    public sealed record Table(IReadOnlyDictionary<string, VerbForms> Forms, IReadOnlyDictionary<string, bool[]> Irregular);

    private static readonly Dictionary<string, Regex> Regexes = new();

    private static Regex Re(string pattern)
    {
        lock (Regexes)
        {
            if (!Regexes.TryGetValue(pattern, out var r)) Regexes[pattern] = r = new Regex(pattern, RegexOptions.CultureInvariant);
            return r;
        }
    }

    private static bool Test(string pattern, string s) => Re(pattern).IsMatch(s);

    /// <summary>JavaScript's String.replace with a RegExp without the g flag: the first match only.</summary>
    private static string ReplaceFirst(string s, string pattern, string to) => Re(pattern).Replace(s, to, 1);

    private static string? Str(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static JsonElement? Prop(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind != JsonValueKind.Null ? v : null;

    private static bool Has(JsonElement? list, string s) => list is { ValueKind: JsonValueKind.Array } a && a.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString() == s);

    /// <summary>"all", or a list of persons: whether a person is among them.</summary>
    private static bool Persons(JsonElement? spec, int person)
    {
        if (spec == null) return false;
        if (spec.Value.ValueKind == JsonValueKind.String) return spec.Value.GetString() == "all";
        return spec.Value.ValueKind == JsonValueKind.Array && spec.Value.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.Number && x.GetInt32() == person);
    }

    private static List<string> AsForms(JsonElement e) => e.ValueKind == JsonValueKind.Array ? e.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new List<string> { e.GetString() ?? "" };

    // ---- classes ----

    /// <summary>The first class whose pattern matches the verb (and whose verb list or exceptions agree), or none.</summary>
    public static JsonElement? Classify(VerbLanguage L, string verb)
    {
        foreach (var c in L.Rules.GetProperty("classes").EnumerateArray())
        {
            if (!Test(Str(c, "match") ?? "", verb)) continue;
            if (Prop(c, "verbs") is { } verbs && !Has(verbs, verb)) continue;
            if (Prop(c, "except") is { } except && Has(except, verb)) continue;
            return c;
        }
        return null;
    }

    private static JsonElement? ClassById(VerbLanguage L, string id) => L.Rules.GetProperty("classes").EnumerateArray().Cast<JsonElement?>().FirstOrDefault(c => Str(c!.Value, "id") == id);

    private static (string Prefix, string Base)? SplitSeparable(VerbLanguage L, string verb)
    {
        if (!L.Separable.Contains(verb)) return null;
        var prefixes = Prop(L.Rules, "separablePrefixes") is { } p ? p.EnumerateArray().Select(x => x.GetString() ?? "").OrderByDescending(x => x.Length).ToList() : new List<string>();
        foreach (var pre in prefixes)
            if (verb.StartsWith(pre, StringComparison.Ordinal) && verb.Length - pre.Length >= 3) return (pre, verb[pre.Length..]);
        throw new InvalidOperationException($"separable verb \"{verb}\" has no known prefix");
    }

    // ---- conjugating ----

    /// <summary>A verb's forms in a tense (<paramref name="ignoreIrregular"/>: that verb's purely regular forms, to tell its irregular cells).</summary>
    public static VerbForms Conjugate(VerbLanguage L, string verb, string tense, string? ignoreIrregular = null)
    {
        var info = L.Tense(tense) ?? throw new InvalidOperationException($"unknown tense {tense}");
        if (Prop(L.Irregulars, verb) is { } irr && Prop(irr, tense) is { } given && ignoreIrregular != verb) return new VerbForms(AsForms(given));
        var R = L.Rules;
        var sep = SplitSeparable(L, verb);
        if (Prop(R, "compound") is { } compound && Prop(compound, tense) is { } c) return Compound(L, verb, tense, ignoreIrregular, sep, c);
        if (sep is { } s)
        {
            if (info.Single) return new VerbForms(new[] { s.Prefix + Conjugate(L, s.Base, tense, ignoreIrregular).One });
            return new VerbForms(Conjugate(L, s.Base, tense, ignoreIrregular).Forms.Select(f => f == VerbForms.Placeholder ? f : f + " " + s.Prefix).ToList());
        }
        if (Prop(R, "compose") is { } compose && Prop(compose, tense) is { } spec) return Compose(L, verb, tense, ignoreIrregular, spec);
        return Regular(L, verb, tense, ignoreIrregular, info);
    }

    private static VerbForms Compound(VerbLanguage L, string verb, string tense, string? ignore, (string Prefix, string Base)? sep, JsonElement c)
    {
        var auxes = (Str(c, "aux") ?? "").Split('|');
        var aux = auxes[0];
        // (the auxiliary itself has no purely regular compound tense: it would be made of itself)
        if (ignore == verb && auxes.Contains(verb)) throw new InvalidOperationException($"{verb} has no regular {tense} (it is the auxiliary)");
        if (Prop(c, "auxOf") is { } auxOf)
            foreach (var p in auxOf.EnumerateObject())
                if (Has(p.Value, verb) || (sep is { } s && Has(p.Value, s.Base))) aux = p.Name;
        var auxForms = Conjugate(L, aux, Str(c, "auxTense") ?? "", ignore);
        var with = Str(c, "with") ?? "";
        var part = with == "infinitive" ? verb : Conjugate(L, verb, with, ignore).One;
        // (the participle's agreement after an auxiliary that asks for one: the passé composé with être, "sommes allés")
        var agree = Prop(c, "agreement") is { } agreement && Prop(agreement, aux) is { } list ? AsForms(list) : null;
        return new VerbForms(auxForms.Forms.Select((a, i) => a + " " + part + (agree != null && i < agree.Count ? agree[i] : "")).ToList());
    }

    private static VerbForms Compose(VerbLanguage L, string verb, string tense, string? ignore, JsonElement spec)
    {
        var cls = Classify(L, verb);
        if (spec.ValueKind != JsonValueKind.Array)
        {
            if (cls == null) throw new InvalidOperationException($"cannot classify {verb}");
            spec = Prop(spec, Str(cls.Value, "id") ?? "") ?? Prop(spec, "*") ?? throw new InvalidOperationException($"no compose spec for {tense}");
        }
        var forms = new List<string>();
        var person = 0;
        foreach (var item in spec.EnumerateArray())
        {
            if (Str(item, "literal") is { } literal) forms.Add(literal);
            else if (Str(item, "form") is { } form)
            {
                var f = Conjugate(L, verb, form, ignore).Forms[item.GetProperty("person").GetInt32()];
                if (Str(item, "stripIf") is { } stripIf && Test(stripIf, f)) f = f[..^1];
                forms.Add(f);
            }
            else if (Str(item, "stem") is { } stem)
            {
                if (cls == null) throw new InvalidOperationException($"cannot classify {verb}");
                forms.Add(BuildForm(L, verb, cls.Value, tense, person, JsonSpec(("kind", stem)), Str(item, "ending") ?? "", ignore));
            }
            else throw new InvalidOperationException($"bad compose item in {tense}");
            person++;
        }
        return new VerbForms(forms);
    }

    private static JsonElement JsonSpec(params (string Key, string Value)[] pairs)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            foreach (var (k, v) in pairs) w.WriteString(k, v);
            w.WriteEndObject();
        }
        return JsonDocument.Parse(ms.ToArray()).RootElement.Clone();
    }

    private static JsonElement? EndingsFor(VerbLanguage L, JsonElement cls, string tense)
    {
        var E = L.Rules.GetProperty("endings");
        JsonElement? c = cls;
        while (c is { } cc)
        {
            if (Prop(E, Str(cc, "id") ?? "") is { } byClass && Prop(byClass, tense) is { } e) return e;
            c = Str(cc, "inherits") is { } parent ? ClassById(L, parent) : null;
        }
        return null;
    }

    private static JsonElement StemSpecFor(VerbLanguage L, JsonElement cls, string tense, int person)
    {
        JsonElement? spec = Prop(L.Rules, "stems") is { } stems ? Prop(stems, tense) : null;
        spec ??= JsonSpec(("kind", "infinitiveStem"));
        if (Prop(spec.Value, "byClass") is { } byClass) spec = Prop(byClass, Str(cls, "id") ?? "") ?? Prop(byClass, "*");
        if (spec is { ValueKind: JsonValueKind.Array } arr)
            spec = arr.EnumerateArray().Cast<JsonElement?>().FirstOrDefault(s => Persons(Prop(s!.Value, "persons"), person));
        return spec ?? throw new InvalidOperationException($"no stem spec for {tense} person {person}");
    }

    private static VerbForms Regular(VerbLanguage L, string verb, string tense, string? ignore, VerbTense info)
    {
        var cls = Classify(L, verb) ?? throw new InvalidOperationException($"cannot classify \"{verb}\"");
        var endings = EndingsFor(L, cls, tense) ?? throw new InvalidOperationException($"no endings for class {Str(cls, "id")} tense {tense}");
        if (info.Single) return new VerbForms(new[] { BuildForm(L, verb, cls, tense, 0, StemSpecFor(L, cls, tense, 0), endings.GetString() ?? "", ignore) });
        var forms = new List<string>();
        var i = 0;
        foreach (var e in endings.EnumerateArray())
        {
            forms.Add(BuildForm(L, verb, cls, tense, i, StemSpecFor(L, cls, tense, i), e.GetString() ?? "", ignore));
            i++;
        }
        return new VerbForms(forms);
    }

    private static string BaseStem(VerbLanguage L, string verb, JsonElement cls, JsonElement spec, string? ignore)
    {
        switch (Str(spec, "kind"))
        {
            case "infinitiveStem": return verb[..(verb.Length - cls.GetProperty("strip").GetInt32())];
            case "infinitive": return verb;
            case "infinitiveMinusR": return verb.EndsWith('r') ? verb[..^1] : verb;
            case "form":
            {
                var forms = Conjugate(L, verb, Str(spec, "tense") ?? "", ignore);
                var f = forms.Single ? forms.One : forms.Forms[spec.GetProperty("person").GetInt32()];
                if (Str(spec, "strip") is { } strip && f.EndsWith(strip, StringComparison.Ordinal)) f = f[..(f.Length - strip.Length)];
                return f;
            }
            default: throw new InvalidOperationException($"unknown stem kind {Str(spec, "kind")}");
        }
    }

    private static string ApplyStemChange(VerbLanguage L, string verb, string stem, string tense, int person)
    {
        if (Prop(L.StemChanging, verb) is not { } sc) return stem;
        var raised = Prop(L.Rules, "raisedStemPersons") is { } rp ? Prop(rp, tense) : null;
        if (Str(sc, "raisedStem") is { } raisedStem && raised != null && Persons(raised, person)) return raisedStem;
        var kind = Prop(L.StemChangeKinds, Str(sc, "kind") ?? "") ?? throw new InvalidOperationException($"unknown stem change kind {Str(sc, "kind")}");
        var persons = Prop(kind.GetProperty("persons"), tense);
        if (persons != null && Persons(persons, person))
        {
            var from = Str(kind, "from") ?? "";
            var to = Str(kind, "to") ?? "";
            var idx = from.Length == 0 ? -1 : stem.LastIndexOf(from, StringComparison.Ordinal);
            if (idx >= 0) stem = stem[..idx] + to + stem[(idx + from.Length)..];
        }
        return stem;
    }

    private static (string Stem, string Ending) ApplySpelling(VerbLanguage L, string verb, JsonElement cls, string stem, string ending, string tense, int person)
    {
        if (Prop(L.Rules, "spelling") is not { } rules) return (stem, ending);
        var clsId = Str(cls, "id") ?? "";
        // (every rule's conditions are tested against the stem and ending as they were before any rule ran, so rules never
        //  cascade: conozc must not then set off "consonant + c → z")
        string stem0 = stem, ending0 = ending;
        foreach (var rule in rules.EnumerateArray())
        {
            var persons = Prop(rule.GetProperty("tenses"), tense);
            if (persons == null) continue;
            if (!Persons(persons, person)) continue;
            if (Prop(rule, "classes") is { } classes && !Has(classes, clsId)) continue;
            if (Prop(rule, "verbs") is { } verbs && !Has(verbs, verb)) continue;
            if (Prop(rule, "except") is { } except && Has(except, verb)) continue;
            if (Str(rule, "match") is { } match && !Test(match, stem0)) continue;
            if (Str(rule, "verbMatch") is { } verbMatch && !Test(verbMatch, verb)) continue;
            if (Str(rule, "endingMatch") is { } endingMatch && !Test(endingMatch, ending0)) continue;
            var action = rule.GetProperty("do");
            if (Prop(action, "stem") is { } ds) stem = ReplaceFirst(stem, Str(ds, "from") ?? "", Str(ds, "to") ?? "");
            if (Prop(action, "ending") is { } de) ending = ReplaceFirst(ending, Str(de, "from") ?? "", Str(de, "to") ?? "");
        }
        return (stem, ending);
    }

    private static string AccentLastVowel(string stem, JsonElement? map)
    {
        if (map == null) return stem;
        for (var i = stem.Length - 1; i >= 0; i--)
        {
            if (Str(map.Value, stem[i].ToString()) is { } accented) return stem[..i] + accented + stem[(i + 1)..];
        }
        return stem;
    }

    private static string BuildForm(VerbLanguage L, string verb, JsonElement cls, string tense, int person, JsonElement spec, string ending, string? ignore)
    {
        var stem = BaseStem(L, verb, cls, spec, ignore);
        if (Str(spec, "kind") == "infinitiveStem") stem = ApplyStemChange(L, verb, stem, tense, person);
        (stem, ending) = ApplySpelling(L, verb, cls, stem, ending, tense, person);
        if (Prop(spec, "suffix") is { } suffix)
            stem += suffix.ValueKind == JsonValueKind.String ? suffix.GetString() : Str(suffix, Str(cls, "id") ?? "") ?? Str(suffix, "*") ?? "";
        if (ending.StartsWith('´'))
        {
            stem = AccentLastVowel(stem, Prop(L.Rules, "accentMap"));
            ending = ending[1..];
        }
        return ending.Contains('%') ? ending.Replace("%", stem) : stem + ending;
    }

    /// <summary>The whole table: every tense's forms, and which cells are irregular (check.mjs table).</summary>
    public static Table Make(VerbLanguage L, string verb)
    {
        var forms = new Dictionary<string, VerbForms>();
        var irregular = new Dictionary<string, bool[]>();
        foreach (var t in L.Tenses)
        {
            var f = Conjugate(L, verb, t.Id);
            VerbForms? r;
            try { r = Conjugate(L, verb, t.Id, ignoreIrregular: verb); } catch (Exception) { r = null; }
            forms[t.Id] = f;
            irregular[t.Id] = f.Forms.Select((x, i) => r == null || i >= r.Forms.Count || x != r.Forms[i]).ToArray();
        }
        return new Table(forms, irregular);
    }

    /// <summary>Whether the engine can conjugate a verb at all (it is known, or it falls into a class).</summary>
    public static bool CanConjugate(VerbLanguage L, string verb)
    {
        if (verb.Length < 2) return false;
        if (L.Knows(verb)) return true;
        try { return Classify(L, verb) != null && Conjugate(L, verb, L.Tenses[0].Id).Forms.Count > 0; }
        catch (Exception) { return false; }
    }

    /// <summary>The verbs the data knows that start with (then contain) what was typed, best first, at most so many.</summary>
    public static IReadOnlyList<string> Suggest(VerbLanguage L, string typed, int max = 8)
    {
        var t = typed.Trim().ToLowerInvariant();
        if (t.Length == 0) return Array.Empty<string>();
        var starts = L.Known.Where(v => v.StartsWith(t, StringComparison.OrdinalIgnoreCase)).Take(max).ToList();
        if (starts.Count < max) starts.AddRange(L.Known.Where(v => !v.StartsWith(t, StringComparison.OrdinalIgnoreCase) && v.Contains(t, StringComparison.OrdinalIgnoreCase)).Take(max - starts.Count));
        return starts;
    }
}
