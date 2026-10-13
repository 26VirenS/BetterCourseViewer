using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using Simpl.Support;

namespace Simpl.Tools;

// Flashcards' sets and the text work on them (the Mac's Flashcards.swift, its model half): a set is a title and its
// terms, written here, pasted in (one card a line, the term and the definition split by a tab, a comma or a dash) or
// imported from a two-column CSV, kept on this PC in a file of the app's own — flashcards.json, in the Mac's format,
// sealed by the vault (Support/Vault.cs). A set exported as CSV goes where the student says, as plain text.

/// <summary>A card: its term and definition, how far Learn has it, its star, and its Flashcards mark.</summary>
public sealed class FCCard
{
    /// <summary>The Mac's dates are seconds from the start of 2001 (Foundation's reference date).</summary>
    private static readonly DateTime Reference = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public string Id { get; set; } = NewId();
    public string Term { get; set; } = "";
    public string Def { get; set; } = "";
    /// <summary>Mastery in Learn: 0, 1, 2 (learned).</summary>
    public int Level { get; set; }
    public bool Star { get; set; }
    /// <summary>Marked Know (true) or Still learning (false) in Flashcards.</summary>
    public bool? Know { get; set; }
    /// <summary>The gap before a learned card's next check, in days.</summary>
    public double? Iv { get; set; }
    /// <summary>When a learned card's next check is due, in the Mac's seconds since 2001.</summary>
    public double? Due { get; set; }

    public FCCard() { }
    public FCCard(string term, string def) { Term = term; Def = def; }

    public static string NewId() => Guid.NewGuid().ToString().ToUpperInvariant();

    [JsonIgnore]
    public DateTime? DueAt
    {
        get => Due is { } d ? Reference.AddSeconds(d) : null;
        set => Due = value is { } v ? (v.ToUniversalTime() - Reference).TotalSeconds : null;
    }

    [JsonIgnore] public bool Learned => Know == true || Level >= 2;

    public bool DueNow(DateTime? now = null) => Level >= 2 && DueAt is { } due && due <= (now ?? DateTime.UtcNow);
}

/// <summary>A set: its name, a line about it, its cards and the best time in Match.</summary>
public sealed class FCDeck
{
    public string Id { get; set; } = FCCard.NewId();
    public string Name { get; set; } = "";
    public string? Desc { get; set; }
    public List<FCCard> Cards { get; set; } = new();
    /// <summary>The best time in Match, in seconds.</summary>
    public double? Best { get; set; }

    [JsonIgnore] public string Title => Name.Length == 0 ? "Untitled set" : Name;

    [JsonIgnore]
    public int LearnedPct => Cards.Count == 0 ? 0 : (int)Math.Round((double)Cards.Count(c => c.Learned) / Cards.Count * 100, MidpointRounding.AwayFromZero);

    [JsonIgnore] public int Mastered => Cards.Count(c => c.Level >= 2);
}

/// <summary>
/// The sets (the Mac's FlashcardStore), kept in flashcards.json in the app's folder, sealed by the vault; a change is
/// written at once, except typing in the editor, which is written once a pause comes. A file an earlier Simpl kept in
/// plain text is sealed as the app starts (<see cref="SealSoon"/>), or when it is first read, whichever comes first; one
/// that cannot be opened is set aside by the vault, never written over.
/// </summary>
public static class FlashcardStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    private const string Purpose = "flashcards";
    /// <summary>The file: read, written, sealed — one at a time (the window's thread, and the seal as the app starts).</summary>
    private static readonly object FileGate = new();
    private static List<FCDeck>? decks;
    private static DispatcherTimer? pending;

    /// <summary>A set or a card changed (a view on screen draws itself again).</summary>
    public static event Action? Changed;

    private static string FilePath => AppPaths.Flashcards;

    public static IReadOnlyList<FCDeck> Decks => decks ??= Load();

    private static List<FCDeck> All => decks ??= Load();

    public static FCDeck? Deck(string? id) => id == null ? null : All.FirstOrDefault(d => d.Id == id);

    public static FCCard? Card(string deck, string card) => Deck(deck)?.Cards.FirstOrDefault(c => c.Id == card);

    public static string Add(FCDeck deck)
    {
        All.Add(deck);
        Write();
        Changed?.Invoke();
        return deck.Id;
    }

    public static void Delete(string id)
    {
        All.RemoveAll(d => d.Id == id);
        Write();
        Changed?.Invoke();
    }

    public static void Change(string id, Action<FCDeck> fn, bool later = false)
    {
        if (Deck(id) is not { } d) return;
        fn(d);
        if (later) WriteSoon(); else Write();
        Changed?.Invoke();
    }

    public static void ChangeCard(string deck, string card, Action<FCCard> fn, bool later = false) =>
        Change(deck, d =>
        {
            if (d.Cards.FirstOrDefault(c => c.Id == card) is { } c) fn(c);
        }, later);

    private static void WriteSoon()
    {
        if (pending == null)
        {
            pending = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.35) };
            pending.Tick += (_, _) => Write();
        }
        pending.Stop();
        pending.Start();
    }

    public static void Write()
    {
        pending?.Stop();
        var json = JsonSerializer.SerializeToUtf8Bytes(All, Options);
        try { lock (FileGate) Vault.Write(FilePath, json, Purpose); }
        catch (Exception e) { Log.Write($"flashcards: {e.Message}"); }
        finally { CryptographicOperations.ZeroMemory(json); }
    }

    private static List<FCDeck> Load()
    {
        lock (FileGate)
        {
            var file = Vault.Read(FilePath, Purpose);
            try
            {
                if (file.Plain != null && JsonSerializer.Deserialize<List<FCDeck>>(file.Plain, Options) is { } list)
                {
                    // (as the Mac reads a card: anything missing takes its default)
                    foreach (var d in list)
                    {
                        d.Id = string.IsNullOrEmpty(d.Id) ? FCCard.NewId() : d.Id;
                        d.Name ??= "";
                        d.Cards ??= new();
                        d.Cards.RemoveAll(c => c == null);
                        foreach (var c in d.Cards)
                        {
                            c.Id = string.IsNullOrEmpty(c.Id) ? FCCard.NewId() : c.Id;
                            c.Term ??= "";
                            c.Def ??= "";
                        }
                    }
                    // (kept in plain text by an earlier Simpl: sealed now, over it, as it was)
                    if (file.Legacy)
                    {
                        try { Vault.Write(FilePath, file.Plain, Purpose); }
                        catch (Exception e) { Log.Write($"flashcards: not sealed yet: {e.Message}"); }
                    }
                    return list.Where(d => d != null).ToList();
                }
            }
            catch (Exception e) { Log.Write($"flashcards: {e.Message}"); }
            finally { file.Zero(); }
        }
        return new List<FCDeck>();
    }

    /// <summary>A flashcards.json an earlier Simpl kept in plain text, sealed on a worker as the app starts (Program.Main)
    /// — not left as it was until Flashcards is next opened.</summary>
    public static void SealSoon() => Task.Run(async () =>
    {
        await Vault.Ready;
        lock (FileGate)
        {
            if (decks != null || !File.Exists(FilePath)) return; // (read already: sealed then)
            var file = Vault.Read(FilePath, Purpose);
            try { if (file.Legacy && file.Plain != null) Vault.Write(FilePath, file.Plain, Purpose); }
            catch (Exception e) { Log.Write($"flashcards: {e.Message}"); }
            finally { file.Zero(); }
        }
    });

    /// <summary>
    /// Reset Everything keeps the student's sets, as the Mac's does: their JSON taken before the vault is erased (the sets
    /// as they are now, when they were read), to be put back sealed under the new vault (<see cref="Put"/>). Null: none.
    /// </summary>
    public static byte[]? Take()
    {
        pending?.Stop();
        if (decks != null) return decks.Count == 0 ? null : JsonSerializer.SerializeToUtf8Bytes(decks, Options);
        lock (FileGate)
        {
            var file = Vault.Read(FilePath, Purpose);
            return file.Plain;
        }
    }

    /// <summary>The sets <see cref="Take"/> took, sealed under the vault made after the erase.</summary>
    public static void Put(byte[] json)
    {
        try { lock (FileGate) Vault.Write(FilePath, json, Purpose); }
        catch (Exception e) { Log.Write($"flashcards: kept through the reset: {e.Message}"); }
        finally { CryptographicOperations.ZeroMemory(json); }
    }
}

/// <summary>How near a typed answer is (the Mac's FCText.Mark).</summary>
public enum FCMark { Right, Close, Wrong }

/// <summary>The text work of the cards: pasting, CSV, and how near an answer is (the Mac's FCText).</summary>
public static class FCText
{
    public static string Norm(string s) => Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]+", " ").Trim(' ', '\t');

    private static readonly Regex LineRule = new(@"^\s*(.+?)\s*(?:\t|,|\s[-–—]\s|:\s)\s*(.+?)\s*$");
    private static readonly char[] Newlines = { '\n', '\r', '\u2028', '\u2029', '\u0085', '\v', '\f' };

    /// <summary>Pasted text → cards: one card a line, the term and the definition split by a tab, a comma or a dash.</summary>
    public static List<FCCard> CardsFromText(string text) =>
        text.Split(Newlines)
            .Select(line => LineRule.Match(line))
            .Where(m => m.Success)
            .Select(m => new FCCard(m.Groups[1].Value, m.Groups[2].Value))
            .ToList();

    /// <summary>A real CSV reader: quoted fields with commas, quotes and new lines.</summary>
    public static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var chars = text.Replace("\r\n", "\n").Replace('\r', '\n');
        for (var i = 0; i < chars.Length; i++)
        {
            var ch = chars[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < chars.Length && chars[i + 1] == '"') { cell.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else cell.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',') { row.Add(cell.ToString()); cell.Clear(); }
            else if (ch == '\n')
            {
                row.Add(cell.ToString());
                rows.Add(row);
                row = new();
                cell.Clear();
            }
            else cell.Append(ch);
        }
        row.Add(cell.ToString());
        if (row.Any(c => c.Length > 0)) rows.Add(row);
        return rows;
    }

    /// <summary>CSV text → cards (Term, Definition; a header row is found by its first cell).</summary>
    public static List<FCCard> CardsFromCsv(string text)
    {
        var rows = ParseCsv(text).Where(r => r.Count >= 2).ToList();
        var head = rows.Count > 0 && Norm(rows[0][0]) == "term";
        return (head ? rows.Skip(1) : rows)
            .Select(r => (T: r[0].Trim(), D: r[1].Trim()))
            .Where(x => x.T.Length > 0 && x.D.Length > 0)
            .Select(x => new FCCard(x.T, x.D))
            .ToList();
    }

    public static string Cell(string s) => s.Contains(',') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    public static string Csv(FCDeck deck) => "Term,Definition\n" + string.Join("\n", deck.Cards.Select(c => $"{Cell(c.Term)},{Cell(c.Def)}")) + "\n";

    /// <summary>How alike two definitions are (words in common, lengths near): the wrong choices that look most like the right one.</summary>
    public static double Alike(string x, string y)
    {
        var a = Norm(x).Split(' ').ToHashSet();
        var b = Norm(y).Split(' ').ToHashSet();
        a.IntersectWith(b);
        a.Remove("");
        return a.Count * 10 - Math.Abs(Norm(x).Length - Norm(y).Length) / 8.0;
    }

    public static int Edits(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        var prev = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var cur = new int[b.Length + 1];
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            prev = cur;
        }
        return prev[b.Length];
    }

    /// <summary>A typed answer: right, close (a near miss, in a longer answer) or wrong.</summary>
    public static FCMark Mark(string typed, string want)
    {
        var x = Norm(typed);
        var y = Norm(want);
        if (x.Length == 0) return FCMark.Wrong;
        if (x == y) return FCMark.Right;
        var slack = y.Length >= 12 ? 2 : (y.Length >= 6 ? 1 : 0);
        return slack > 0 && Edits(x, y) <= slack ? FCMark.Close : FCMark.Wrong;
    }
}

/// <summary>Where Flashcards is: your sets, a set's page, its editor, or a way to study it.</summary>
internal sealed record FCWhere(string Kind, string? Deck)
{
    public static readonly FCWhere Sets = new("sets", null);
}

/// <summary>
/// For the screenshot suite: --fc-demo [sets|set|edit|cards|flipped|learn|test|match] makes two sample sets (Cell
/// Biology and Spanish food, once) and opens Cell Biology's view named — Flashcards ("cards") unless another is named.
/// </summary>
internal static class FlashcardDemo
{
    public const string CellId = "DEMO-CELL-BIOLOGY";
    public const string FoodId = "DEMO-SPANISH-FOOD";
    private static bool seeded;

    /// <summary>The first card shown on its back (--fc-demo flipped), taken once.</summary>
    public static bool FlipFirst { get; set; }

    public static void SeedIfAsked()
    {
        if (seeded || Launch.Get("fc-demo") == null) return;
        seeded = true;
        var day = 86_400.0;
        var now = (DateTime.UtcNow - new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        if (FlashcardStore.Deck(CellId) == null)
            FlashcardStore.Add(new FCDeck
            {
                Id = CellId, Name = "Cell Biology", Desc = "Unit 3 — the organelles and what each one does.",
                Cards =
                {
                    new FCCard("Mitochondria", "Organelle that makes ATP through cellular respiration") { Level = 2, Star = true, Know = true, Iv = 1, Due = now - day / 2 },
                    new FCCard("Nucleus", "Holds the cell’s DNA and controls which genes are expressed") { Know = true },
                    new FCCard("Ribosome", "Builds proteins by reading messenger RNA"),
                    new FCCard("Golgi apparatus", "Modifies, sorts and packages proteins for transport") { Level = 1 },
                    new FCCard("Endoplasmic reticulum", "Network of membranes that makes proteins (rough) and lipids (smooth)") { Star = true, Know = false },
                    new FCCard("Lysosome", "Breaks down waste and worn-out parts with digestive enzymes"),
                    new FCCard("Cell membrane", "Phospholipid bilayer that controls what enters and leaves the cell") { Level = 2, Iv = 3, Due = now + 2 * day },
                    new FCCard("Chloroplast", "Captures light energy for photosynthesis in plant cells"),
                },
            });
        if (FlashcardStore.Deck(FoodId) == null)
            FlashcardStore.Add(new FCDeck
            {
                Id = FoodId, Name = "Spanish — La comida",
                Cards =
                {
                    new FCCard("la manzana", "the apple") { Know = true }, new FCCard("el pan", "the bread") { Know = true },
                    new FCCard("el queso", "the cheese"), new FCCard("la leche", "the milk"), new FCCard("el pollo", "the chicken"), new FCCard("las uvas", "the grapes"),
                },
            });
    }

    /// <summary>The view --fc-demo names.</summary>
    public static FCWhere Where(string v)
    {
        var kind = v.ToLowerInvariant();
        if (kind == "flipped") { FlipFirst = true; kind = "cards"; }
        return kind switch
        {
            "sets" => FCWhere.Sets,
            "set" => new("page", CellId),
            "edit" or "learn" or "test" or "match" => new(kind, CellId),
            _ => new("cards", CellId),
        };
    }
}
