using System.Globalization;
using System.Text.Json.Nodes;

namespace Simpl.Screens;

// The quiz popup's answers from the page (the Mac's QuizModels.swift, shared with the iPhone): every field optional, so
// a page a version apart still reads. The page's calls are native-app.js's quizIntro, quizBegin, quizAnswer, quizFlag,
// quizUpload, quizGo, quizSubmit and quizFeedback.

/// <summary>A quiz before an attempt: what it is, its rules, and whether (and how) it can be begun (quizIntro).</summary>
public sealed class QuizIntro
{
    public sealed class Rule
    {
        public string Symbol { get; set; } = "";
        public string Tint { get; set; } = "";
        public string Text { get; set; } = "";
    }

    public sealed class LastAttempt
    {
        public int Attempt { get; set; } = 1;
        public string? Score { get; set; }
        public bool Feedback { get; set; }
        public string? Why { get; set; }
    }

    /// <summary>A finished attempt: its score when Canvas shows results (else why not), when it was handed in, and
    /// whether it is the one Canvas keeps (the Mac's 1.3.32).</summary>
    public sealed class PastAttempt
    {
        public int Attempt { get; set; } = 1;
        public string? Score { get; set; }
        public string? When { get; set; }
        public string? Why { get; set; }
        public bool? Kept { get; set; }
    }

    public string Title { get; set; } = "Quiz";
    public string? Context { get; set; }
    public string? Color { get; set; }
    public List<string> Facts { get; set; } = new();
    public string Html { get; set; } = "";
    public List<Rule> Rules { get; set; } = new();
    public bool NeedsCode { get; set; }
    public string? Code { get; set; }
    public bool CanStart { get; set; }
    public bool Survey { get; set; }
    public bool Timed { get; set; }
    public int? TimeLimit { get; set; }
    public bool OneAtATime { get; set; }
    public bool NoBack { get; set; }
    public string Begin { get; set; } = "Begin Attempt";
    /// <summary>(2.99.33) The one line on where the attempts stand, counting the attempt under way ("Attempt 2 of 5 · 3 more
    /// after this one", "This is your only attempt").</summary>
    public string? Note { get; set; }
    /// <summary>(2.99.33) The attempts as the quiz counts them ("0 of 1 attempt used"), how many are left (null: no limit),
    /// and the assignment page's facts line with them.</summary>
    public string? AttemptsText { get; set; }
    public int? AttemptsLeft { get; set; }
    public string? FactsLine { get; set; }
    public string? LockText { get; set; }
    public LastAttempt? Last { get; set; }
    public List<PastAttempt>? Attempts { get; set; }
    public string? KeptNote { get; set; }
    public string TakeUrl { get; set; } = "";
}

/// <summary>Begin's answer: the attempt, or why it did not begin (an access code wanted or refused, a network refused).</summary>
public sealed class QuizBeginAnswer
{
    public QuizAttempt? Attempt { get; set; }
    public bool? NeedsCode { get; set; }
    public string? Refused { get; set; }
}

/// <summary>The attempt as the page holds it: every question with its answer so far, the clock, and how it moves.</summary>
public sealed class QuizAttempt
{
    public string Title { get; set; } = "Quiz";
    public string? Context { get; set; }
    public string? Color { get; set; }
    public int Attempt { get; set; } = 1;
    public bool Paged { get; set; }
    public bool NoBack { get; set; }
    public bool Survey { get; set; }
    public string Html { get; set; } = "";
    public bool Timed { get; set; }
    public string? EndAt { get; set; }
    public string? StartedAt { get; set; }
    public int Idx { get; set; }
    public bool CanPrev { get; set; }
    /// <summary>A one-at-a-time attempt: whether Canvas's page says this is its last question (null otherwise: the list says).</summary>
    public bool? Last { get; set; }
    public List<QuizQuestion> Questions { get; set; } = new();
    public string TakeUrl { get; set; } = "";
}

/// <summary>
/// One question, as picked so far. Its kind: choice, multi, text, number, essay, match, drops, blanks, file, info (words
/// only), pending (not read from Canvas's page yet), other (a kind Canvas's own page answers).
/// </summary>
public sealed class QuizQuestion
{
    public sealed class Option
    {
        public string Id { get; set; } = "";
        public string Letter { get; set; } = "";
        public string Text { get; set; } = "";
        public string Html { get; set; } = "";
    }

    public sealed class Choice
    {
        public string Id { get; set; } = "";
        public string Text { get; set; } = "";
    }

    public sealed class Blank
    {
        public string Id { get; set; } = "";
        public int N { get; set; }
        public string Label { get; set; } = "";
        /// <summary>(2.99.33) The words either side of it in the question, "the ___ of" ("" when there are none): its field's label.</summary>
        public string Around { get; set; } = "";
        public List<Choice> Options { get; set; } = new();
    }

    public sealed class File
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
    }

    public string Id { get; set; } = "";
    public int N { get; set; }
    public string Kind { get; set; } = "other";
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public string Html { get; set; } = "";
    public string Plain { get; set; } = "";
    public double? Points { get; set; }
    public bool Flagged { get; set; }
    public bool Answered { get; set; }
    public bool Loaded { get; set; } = true;
    public List<Option> Options { get; set; } = new();
    public List<Choice> Matches { get; set; } = new();
    public List<Blank> Blanks { get; set; } = new();
    public string Hint { get; set; } = "";
    public string? Pick { get; set; }
    public List<string> Picks { get; set; } = new();
    public string Text { get; set; } = "";
    public Dictionary<string, string> Map { get; set; } = new();
    public List<File> Files { get; set; } = new();

    /// <summary>Whether every part of it is answered, as the page counts it (a blank kind: every blank; matching: every row).</summary>
    public bool IsAnswered => Kind switch
    {
        "info" => true,
        "pending" => Answered,
        "choice" => Pick != null,
        "multi" => Picks.Count > 0,
        "text" or "number" or "essay" => Text.Trim().Length > 0,
        "match" => Map.Count > 0 && Map.Count >= Options.Count,
        "drops" or "blanks" => Blanks.Count > 0 && Blanks.All(b => (Map.TryGetValue(b.Id, out var v) ? v : "").Trim().Length > 0),
        "file" => Files.Count > 0,
        _ => Answered,
    };

    /// <summary>The answer as the call takes it.</summary>
    public JsonObject Value()
    {
        var o = new JsonObject();
        switch (Kind)
        {
            case "choice":
                if (Pick != null) o["pick"] = Pick;
                break;
            case "multi":
                o["picks"] = new JsonArray(Picks.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray());
                break;
            case "text" or "number" or "essay":
                o["text"] = Text;
                break;
            case "match" or "drops" or "blanks":
            {
                var m = new JsonObject();
                foreach (var (k, v) in Map) m[k] = v;
                o["map"] = m;
                break;
            }
            case "file":
                o["files"] = new JsonArray(Files.Select(f => (JsonNode?)new JsonObject { ["id"] = f.Id, ["name"] = f.Name }).ToArray());
                break;
        }
        return o;
    }

    /// <summary>The answer in a line, for the review.</summary>
    public string? Summary
    {
        get
        {
            if (!IsAnswered) return null;
            static string Name(Option o) => o.Text.Length == 0 ? $"Option {o.Letter}" : o.Text;
            switch (Kind)
            {
                case "info": return null;
                case "choice":
                    return Options.FirstOrDefault(o => o.Id == Pick) is { } chosen ? Name(chosen) : "Answered";
                case "multi":
                    return string.Join(", ", Options.Where(o => Picks.Contains(o.Id)).Select(Name));
                case "text" or "number" or "essay":
                    return Text.Replace("\n", " ");
                case "match":
                {
                    var pairs = new List<string>();
                    foreach (var o in Options)
                    {
                        if (!Map.TryGetValue(o.Id, out var m) || Matches.FirstOrDefault(x => x.Id == m) is not { } match) continue;
                        pairs.Add($"{(o.Text.Length == 0 ? o.Letter : o.Text)} → {match.Text}");
                    }
                    return string.Join(", ", pairs);
                }
                case "drops":
                {
                    var picked = new List<string>();
                    foreach (var b in Blanks)
                    {
                        if (!Map.TryGetValue(b.Id, out var v) || b.Options.FirstOrDefault(c => c.Id == v) is not { } c2) continue;
                        picked.Add(c2.Text);
                    }
                    return string.Join(", ", picked);
                }
                case "blanks":
                    return string.Join(", ", Blanks.Where(b => Map.ContainsKey(b.Id)).Select(b => Map[b.Id]));
                case "file":
                    return string.Join(", ", Files.Select(f => f.Name));
                default:
                    return "Answered";
            }
        }
    }
}

/// <summary>What a save, a flag or an upload says back.</summary>
public sealed class QuizSaved
{
    public bool? Ok { get; set; }
    public bool? NeedsCode { get; set; }
    public bool? Done { get; set; }
    public int? Answered { get; set; }
    public int? Total { get; set; }
    public QuizQuestion.File? File { get; set; }
    public bool? Flagged { get; set; }
}

/// <summary>Submit's answer: the receipt, or the questions Canvas did not keep (missing, by number).</summary>
public sealed class QuizReceipt
{
    public bool? Ok { get; set; }
    public List<int>? Missing { get; set; }
    public bool? Survey { get; set; }
    public string? Title { get; set; }
    public int? Attempt { get; set; }
    public string? Lead { get; set; }
    public string? Answered { get; set; }
    public string? Score { get; set; }
    public string? TakingPart { get; set; }
    public bool? Feedback { get; set; }
}

/// <summary>A finished attempt question by question, or why its results are kept back (hidden).</summary>
public sealed class QuizFeedback
{
    public sealed class Part
    {
        public string Text { get; set; } = "";
        public string Html { get; set; } = "";
        public bool Block { get; set; }
    }

    public sealed class MatchRow
    {
        public string Left { get; set; } = "";
        public string? Mine { get; set; }
        public string? Right { get; set; }
        public bool? Ok { get; set; }
    }

    public sealed class MatchTable
    {
        public bool ShowRight { get; set; }
        public List<MatchRow> Rows { get; set; } = new();
    }

    public sealed class Opt
    {
        public string Id { get; set; } = "";
        public string Letter { get; set; } = "";
        public string Text { get; set; } = "";
        public string Html { get; set; } = "";
        public bool Mine { get; set; }
        public bool Right { get; set; }
    }

    public sealed class Solution
    {
        public string Html { get; set; } = "";
        public string Text { get; set; } = "";
    }

    public sealed class Row
    {
        public int N { get; set; }
        public string Verdict { get; set; } = "none";
        public string VerdictText { get; set; } = "";
        public string Score { get; set; } = "";
        public string Html { get; set; } = "";
        public List<Part> Yours { get; set; } = new();
        public bool Essay { get; set; }
        public List<Part> Right { get; set; } = new();
        public MatchTable? Match { get; set; }
        public List<Opt>? Options { get; set; }
        public Solution? Solution { get; set; }
        public bool NoSolution { get; set; }
    }

    public sealed class Comment
    {
        public string Author { get; set; } = "";
        public string Text { get; set; } = "";
    }

    public string? Hidden { get; set; }
    public string? Title { get; set; }
    public string? Color { get; set; }
    public int? Attempt { get; set; }
    public List<int>? Attempts { get; set; }
    public string? Score { get; set; }
    public string? Possible { get; set; }
    public double? Pct { get; set; }
    /// <summary>(2.99.33) The percentage as every app shows it ("81.3%") and the ring's whole percent (81), from that figure.</summary>
    public string? PctText { get; set; }
    public int? PctWhole { get; set; }
    public string? Band { get; set; }
    public string? Summary { get; set; }
    public bool? Released { get; set; }
    public List<Comment>? Comments { get; set; }
    public List<Row>? Rows { get; set; }
}

/// <summary>The attempt's clock: the page's times read, and a span written as the Mac's QuizTime writes it.</summary>
internal static class QuizTime
{
    public static DateTimeOffset? Date(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;
    }

    /// <summary>1:05:09, 4:07, 0:09.</summary>
    public static string Clock(double seconds)
    {
        var s = double.IsFinite(seconds) ? Math.Max(0, (int)Math.Round(Math.Min(seconds, 359_999))) : 0;
        int h = s / 3600, m = s % 3600 / 60, sec = s % 60;
        return h > 0 ? $"{h}:{m:00}:{sec:00}" : $"{m}:{sec:00}";
    }
}

/// <summary>How the quiz popup writes a number of points (2, 1.5) and names an option (for Narrator and the menus).</summary>
internal static class QuizFormat
{
    public static string Points(double v)
    {
        if (!double.IsFinite(v)) return "—";
        return v == Math.Round(v) && Math.Abs(v) < 1e15 ? ((long)v).ToString(CultureInfo.InvariantCulture) : v.ToString("G", CultureInfo.InvariantCulture);
    }

    public static string OptionName(QuizQuestion.Option o) => o.Text.Length == 0 ? $"Option {o.Letter}" : $"{o.Letter}. {o.Text}";
}
