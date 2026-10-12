using System.Text.Json.Nodes;

namespace Simpl.Screens;

/// <summary>What changed in a quiz being taken, for the popup to show it.</summary>
[Flags]
internal enum QuizChange
{
    None = 0,
    Stage = 1,
    Intro = 2,
    Attempt = 4,
    Index = 8,
    Answer = 16,
    Saving = 32,
    Moving = 64,
    Banner = 128,
    Feedback = 256,
    Uploading = 512,
    AskCode = 1024,
    Missing = 2048,
    TimeUp = 4096,
    Refused = 8192,
    Flag = 16384,
}

/// <summary>
/// A quiz being taken in the app (the Mac's QuizRun, shared with the iPhone): its intro, the attempt with every answer
/// as picked, the saves on their way to Canvas, the clock, the review, the receipt and the feedback. Every action is one
/// of the page's quiz calls (native-app.js: the web quiz screen's own rules); nothing is ever handed in unless the
/// student says so. It says what changed (<see cref="Changed"/>) for the popup to show it.
/// </summary>
internal sealed class QuizRun
{
    public enum Stage { Intro, Starting, Taking, Review, Submitting, Receipt, Feedback }

    public sealed record Note(string Text, bool Error);

    public string Course { get; }
    public string Quiz { get; }
    private readonly Engine.Engine engine;

    public event Action<QuizChange>? Changed;

    private Stage stage = Stage.Intro;
    public Stage Now { get => stage; set { if (stage == value) return; stage = value; Say(QuizChange.Stage); } }
    public QuizIntro? Intro { get; private set; }
    public string? IntroError { get; private set; }
    public QuizAttempt? Attempt { get; private set; }
    public int Idx { get; private set; }
    public string Code { get; set; } = "";
    public string? Refused { get; private set; }
    private int saving;
    private bool saved;
    public Note? Banner { get; private set; }
    /// <summary>Canvas wants the access code to save an answer: the popup asks, then the saves waiting on it go again.</summary>
    public bool AskCode { get; set; }
    /// <summary>A move on a one-at-a-time attempt under way, to this question.</summary>
    public int? Moving { get; private set; }
    /// <summary>Which way the last move went (the page slides that way): on to a later question, or back.</summary>
    public bool Forward { get; private set; } = true;
    public QuizReceipt? Receipt { get; private set; }
    public QuizFeedback? Feedback { get; private set; }
    public string? FeedbackError { get; private set; }
    /// <summary>Canvas did not keep these answers (by number): the student decides whether to hand in all the same.</summary>
    public List<int> Missing { get; set; } = new();
    public string? Uploading { get; private set; }
    public bool TimeUp { get; set; }
    /// <summary>The stage the feedback was opened from (Back goes there).</summary>
    private Stage feedbackFrom = Stage.Intro;
    public bool Submitted { get; private set; }
    private readonly Dictionary<string, CancellationTokenSource> typing = new();
    private readonly List<string> waitingForCode = new();
    private readonly HashSet<int> warned = new();
    private CancellationTokenSource? bannerTimer;

    public QuizRun(Engine.Engine engine, string course, string quiz)
    {
        this.engine = engine;
        Course = course;
        Quiz = quiz;
    }

    private void Say(QuizChange c) => Changed?.Invoke(c);

    private JsonObject Ids() => new() { ["course"] = Course, ["quiz"] = Quiz };

    public List<QuizQuestion> Questions => Attempt?.Questions ?? new List<QuizQuestion>();
    public QuizQuestion? Current => Idx >= 0 && Idx < Questions.Count ? Questions[Idx] : null;
    public int AnsweredCount => Questions.Count(q => q.IsAnswered);

    /// <summary>The last question: Canvas's page says so on a one-at-a-time attempt, the list otherwise.</summary>
    public bool IsLast
    {
        get
        {
            if (Attempt is not { } a) return true;
            if (a.Paged && a.Last is { } last) return last;
            return Idx >= a.Questions.Count - 1;
        }
    }

    public bool CanGoBack => Attempt is { NoBack: false } a && Idx > 0 && (!a.Paged || a.CanPrev);

    public string SaveWord => saving > 0 ? "Saving…" : saved ? "Saved" : "";

    /// <summary>A save that failed, a warning of the clock: said at the top of the popup, gone by itself after a moment.</summary>
    public void Tell(string text, bool error = false)
    {
        Banner = new Note(text, error);
        Say(QuizChange.Banner);
        bannerTimer?.Cancel();
        var cts = bannerTimer = new CancellationTokenSource();
        _ = ClearBanner(cts.Token);
    }

    private async Task ClearBanner(CancellationToken token)
    {
        try { await Task.Delay(3200, token); } catch (TaskCanceledException) { return; }
        if (token.IsCancellationRequested) return;
        DismissBanner();
    }

    public void DismissBanner()
    {
        if (Banner == null) return;
        Banner = null;
        Say(QuizChange.Banner);
    }

    // ---- intro and begin ----

    public async Task LoadIntro()
    {
        try
        {
            var i = await engine.Call<QuizIntro>("quizIntro", Ids());
            Intro = i;
            if (Code.Length == 0 && !string.IsNullOrEmpty(i.Code)) Code = i.Code!;
            IntroError = null;
        }
        catch (Exception e)
        {
            IntroError = e.Message;
        }
        Say(QuizChange.Intro);
    }

    public async Task Begin()
    {
        Refused = null;
        Now = Stage.Starting;
        var args = Ids();
        args["code"] = Code.Trim();
        try
        {
            var r = await engine.Call<QuizBeginAnswer>("quizBegin", args);
            if (r.Attempt is { } a)
            {
                Attempt = a;
                Idx = a.Idx;
                warned.Clear();
                Say(QuizChange.Attempt);
                Now = Stage.Taking;
            }
            else
            {
                Refused = r.Refused ?? "The attempt could not begin.";
                if (r.NeedsCode == true && Intro != null) Intro.NeedsCode = true;
                Say(QuizChange.Refused);
                Now = Stage.Intro;
            }
        }
        catch (Exception e)
        {
            Refused = e.Message;
            Say(QuizChange.Refused);
            Now = Stage.Intro;
        }
    }

    // ---- answers ----

    /// <summary>An answer changed: a pick is saved at once, typing once it pauses.</summary>
    public void Set(string id, Action<QuizQuestion> change, bool typed = false)
    {
        if (Questions.FirstOrDefault(q => q.Id == id) is not { } question) return;
        change(question);
        if (typing.Remove(id, out var waiting)) waiting.Cancel();
        Say(QuizChange.Answer);
        if (typed)
        {
            var cts = new CancellationTokenSource();
            typing[id] = cts;
            _ = SaveAfterPause(id, cts.Token);
        }
        else _ = Save(id);
    }

    private async Task SaveAfterPause(string id, CancellationToken token)
    {
        try { await Task.Delay(650, token); } catch (TaskCanceledException) { return; }
        if (token.IsCancellationRequested) return;
        await Save(id);
    }

    private async Task Save(string id, string? code = null)
    {
        if (Questions.FirstOrDefault(q => q.Id == id) is not { } q) return;
        if (typing.Remove(id, out var t)) t.Cancel();
        saving++;
        Say(QuizChange.Saving);
        var args = Ids();
        args["question"] = id;
        args["value"] = q.Value();
        if (code != null) args["code"] = code;
        try
        {
            var r = await engine.Call<QuizSaved>("quizAnswer", args);
            if (r.NeedsCode == true)
            {
                if (!waitingForCode.Contains(id)) waitingForCode.Add(id);
                AskCode = true;
                Say(QuizChange.AskCode);
                return;
            }
            saved = true;
        }
        catch (Exception e)
        {
            Tell($"Could not save that answer: {e.Message}", error: true);
        }
        finally
        {
            saving--;
            Say(QuizChange.Saving);
        }
    }

    /// <summary>The access code typed in: the answers that waited on it are saved, with it.</summary>
    public async Task CodeGiven()
    {
        var c = Code.Trim();
        if (c.Length == 0) return;
        var waiting = waitingForCode.ToList();
        waitingForCode.Clear();
        foreach (var id in waiting) await Save(id, c);
    }

    /// <summary>Every answer on its way: typing still waiting on its pause is sent now, and the saves under way are awaited.</summary>
    public async Task Flush()
    {
        foreach (var id in typing.Keys.ToList())
        {
            if (typing.Remove(id, out var t)) t.Cancel();
            await Save(id);
        }
        var waited = 0;
        while (saving > 0 && waited < 300)
        {
            await Task.Delay(50);
            waited++;
        }
    }

    public async Task Flag(string id)
    {
        if (Questions.FirstOrDefault(q => q.Id == id) is not { } q) return;
        var on = !q.Flagged;
        q.Flagged = on; // (as a pick is: the button and the tile's flag change together)
        Say(QuizChange.Flag);
        var args = Ids();
        args["question"] = id;
        args["on"] = on;
        try
        {
            await engine.Call<QuizSaved>("quizFlag", args);
        }
        catch (Exception e)
        {
            q.Flagged = !on;
            Say(QuizChange.Flag);
            Tell($"Could not flag it: {e.Message}", error: true);
        }
    }

    /// <summary>A file for a file-upload question: uploaded to the student's own quiz files on Canvas, the answer set to name it.</summary>
    public async Task Upload(string id, string name, string type, byte[] data)
    {
        if (data.Length > 50 * 1024 * 1024)
        {
            Tell($"{name} is larger than 50 MB. Answer this one on Canvas’s own page.", error: true);
            return;
        }
        Uploading = id;
        Say(QuizChange.Uploading);
        var args = Ids();
        args["question"] = id;
        args["name"] = name;
        args["type"] = type;
        args["data"] = Convert.ToBase64String(data);
        try
        {
            var r = await engine.Call<QuizSaved>("quizUpload", args);
            if (r.NeedsCode == true)
            {
                AskCode = true;
                Say(QuizChange.AskCode);
                Tell("Enter the access code, then choose the file again.", error: true);
                return;
            }
            if (r.File is { } file && Questions.FirstOrDefault(q => q.Id == id) is { } q)
            {
                q.Files = new List<QuizQuestion.File> { file };
                saved = true;
                Say(QuizChange.Answer);
            }
        }
        catch (Exception e)
        {
            Tell($"Could not upload {name}: {e.Message}", error: true);
        }
        finally
        {
            Uploading = null;
            Say(QuizChange.Uploading);
        }
    }

    // ---- moving ----

    public async Task Go(int k)
    {
        if (Attempt is not { } a || k < 0 || k >= a.Questions.Count || k == Idx || Moving != null) return;
        if (a.NoBack && k < Idx) return;
        Forward = k > Idx;
        if (a.Paged) await Move("to", a.Questions[k].Id, k);
        else { Idx = k; Say(QuizChange.Index); }
    }

    public async Task Next()
    {
        if (Attempt is not { } a || Moving != null) return;
        Forward = true;
        if (a.Paged) await Move("next", null, Idx + 1);
        else
        {
            var to = Math.Min(Idx + 1, a.Questions.Count - 1);
            if (to != Idx) { Idx = to; Say(QuizChange.Index); }
        }
    }

    public async Task Back()
    {
        if (Attempt is not { } a || Moving != null || !CanGoBack) return;
        Forward = false;
        if (a.Paged) await Move("prev", null, Idx - 1);
        else { Idx = Math.Max(0, Idx - 1); Say(QuizChange.Index); }
    }

    /// <summary>A move on a one-at-a-time attempt, through Canvas's own page (every answer is on Canvas first).</summary>
    private async Task Move(string how, string? question, int k)
    {
        Moving = k;
        Say(QuizChange.Moving);
        try
        {
            await Flush();
            var args = Ids();
            args["move"] = how;
            if (question != null) args["question"] = question;
            var a = await engine.Call<QuizAttempt>("quizGo", args);
            Attempt = a;
            Idx = a.Idx;
            Moving = null;
            Say(QuizChange.Attempt | QuizChange.Index | QuizChange.Moving);
        }
        catch (Exception e)
        {
            Moving = null;
            Say(QuizChange.Moving);
            Tell($"Could not move to that question: {e.Message}", error: true);
        }
    }

    // ---- review, submit, feedback ----

    public async Task ToReview()
    {
        await Flush();
        Now = Stage.Review;
    }

    public async Task KeepWorking(int? k = null)
    {
        Now = Stage.Taking;
        if (k is { } at) await Go(at);
    }

    public async Task Submit(bool force = false)
    {
        Now = Stage.Submitting;
        await Flush();
        var args = Ids();
        args["force"] = force;
        try
        {
            var r = await engine.Call<QuizReceipt>("quizSubmit", args);
            if (r.Ok == false && r.Missing is { Count: > 0 } m)
            {
                Missing = m;
                Now = Stage.Review;
                Say(QuizChange.Missing);
                return;
            }
            Receipt = r;
            Submitted = true;
            Now = Stage.Receipt;
        }
        catch (Exception e)
        {
            Now = Stage.Review;
            Tell($"Could not submit: {e.Message}", error: true);
        }
    }

    public async Task OpenFeedback(int? attempt = null)
    {
        if (stage != Stage.Feedback) feedbackFrom = stage;
        Feedback = null;
        FeedbackError = null;
        Now = Stage.Feedback;
        Say(QuizChange.Feedback);
        var args = Ids();
        if (attempt is { } n) args["attempt"] = n;
        try
        {
            Feedback = await engine.Call<QuizFeedback>("quizFeedback", args);
        }
        catch (Exception e)
        {
            FeedbackError = e.Message;
        }
        Say(QuizChange.Feedback);
    }

    public void LeaveFeedback() => Now = feedbackFrom == Stage.Feedback ? Stage.Intro : feedbackFrom;

    // ---- the clock ----

    public double? Remaining(DateTimeOffset now)
    {
        if (Attempt is not { Timed: true } a || QuizTime.Date(a.EndAt) is not { } end) return null;
        return (end - now).TotalSeconds;
    }

    public double? Elapsed(DateTimeOffset now)
    {
        if (QuizTime.Date(Attempt?.StartedAt) is not { } start) return null;
        return (now - start).TotalSeconds;
    }

    /// <summary>Five minutes left, one minute left, time up: said once each (time up goes to the review; Canvas closes the attempt).</summary>
    public void CheckClock(DateTimeOffset now)
    {
        if (stage is not (Stage.Taking or Stage.Review) || Remaining(now) is not { } left) return;
        if (left <= 300 && left > 290 && warned.Add(5)) Tell("5 minutes left.");
        if (left <= 60 && left > 50 && warned.Add(1)) Tell("1 minute left.", error: true);
        if (left <= 0 && stage == Stage.Taking && warned.Add(0))
        {
            TimeUp = true;
            Say(QuizChange.TimeUp);
            _ = ToReview();
        }
    }

    /// <summary>The popup closed: nothing more is said.</summary>
    public void Stop()
    {
        bannerTimer?.Cancel();
        Changed = null;
    }
}
