using System.Text.Json;
using System.Text.Json.Nodes;
using Simpl.Models;
using Simpl.Screens;
using Simpl.Support;

namespace Simpl.Engine;

/// <summary>
/// The engine's side of the window's live check (Shell/LiveCheck.cs, README: Live checks): a call asked again with the
/// engine's maxAge and the hash of the answer on screen, what the page has read of the school so far (for the check's
/// one line in the log), the pane's counts as something a check reads, and — against the mock school only — a change
/// made behind the engine's back, for the screenshot suite to see a check find it.
/// </summary>
public sealed partial class Engine
{
    private PaneRead? pane;

    /// <summary>The pane's counts (the snapshot: Notifications' and the Inbox's badges, who is signed in), as a check reads
    /// them beside the screen on show.</summary>
    public ILiveRead PaneCounts => Pane;

    private PaneRead Pane => pane ??= new PaneRead(this);

    /// <summary>The student is at the window whose screen is being checked (it is the one in front, the PC unlocked, its
    /// display on): set by the live check as each check begins. Only then may a check mark something read on Canvas — an
    /// open conversation's new message, a discussion's new reply.</summary>
    public bool LiveSeen { get; set; }

    /// <summary>
    /// A call asked again for a live check: <paramref name="args"/> are the screen's own (those that write a preference
    /// left out), with <paramref name="maxAge"/> (seconds) and <paramref name="known"/>, the hash of the answer on screen
    /// (none yet: ''), and whether the student is there (<see cref="LiveSeen"/>). The answer's envelope read off the
    /// window's thread; a call that failed is a failed answer.
    /// </summary>
    public async Task<LiveAnswer> CheckCall(string name, JsonObject args, double maxAge, string? known)
    {
        var a = LiveRules.CheckArgs(name, args, maxAge, known, LiveSeen);
        string text;
        try { text = await CallText(name, a); }
        catch (Exception e) { return LiveAnswer.Failed(e.Message); }
        return await Task.Run(() => LiveAnswer.Parse(text));
    }

    /// <summary>How many reads the page has asked of the school so far (BCVNative.stats), or -1 when it cannot say.</summary>
    public async Task<long> Reads()
    {
        if (!ready || Web.CoreWebView2 is not { } core) return -1;
        try
        {
            var r = await core.ExecuteScriptAsync("(function(){try{var s=window.BCVNative&&BCVNative.stats&&BCVNative.stats();return s&&typeof s.reads==='number'?s.reads:-1}catch(e){return -1}})()");
            return long.TryParse(r, out var n) ? n : -1;
        }
        catch { return -1; }
    }

    /// <summary>
    /// The mock school changed behind the engine's back (the screenshot suite's --live-change): `done` marks the soonest
    /// work done, `grade` moves a course's total, `task` adds a task for today — written to the mock's own routes, so only
    /// a check that reads again sees it. The page refuses it anywhere but the mock. What it did, for the log: never a
    /// title, only the kind and the id the engine names.
    /// </summary>
    public async Task<string> LiveDevChange(string what)
    {
        try
        {
            var text = await CallText("liveDevChange", new JsonObject { ["what"] = what });
            using var doc = JsonDocument.Parse(text);
            var r = doc.RootElement;
            if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("error", out var err)) return "refused: " + err;
            var id = r.ValueKind == JsonValueKind.Object && r.TryGetProperty("id", out var i) ? i.ToString() : "?";
            var today = r.ValueKind == JsonValueKind.Object && r.TryGetProperty("today", out var t) && t.ValueKind == JsonValueKind.True;
            return $"ok, {id}{(what == "done" ? today ? " (due today)" : " (due this week)" : "")}";
        }
        catch (Exception e) { return "failed: " + e.Message; }
    }

    /// <summary>
    /// The pane's counts as a check reads them: the snapshot asked again with the screen's maxAge, the same answer leaving
    /// the pane as it is; a new one set as the engine's Snapshot, which the pane draws again row by row (only the rows
    /// whose counts changed).
    /// </summary>
    private sealed class PaneRead : ILiveRead
    {
        private readonly Engine engine;
        private string? text, hash;
        private long at;
        private int turn;

        public PaneRead(Engine engine) => this.engine = engine;

        public string? Call => "snapshot";
        public bool CanCheck => text != null && engine.Snapshot != null;
        public long FreshAt => at;

        /// <summary>The snapshot read the usual way (the start, a change under the screens): what a check compares with.</summary>
        public void Read(string answer)
        {
            if (answer != text) { text = answer; hash = null; }
            at = Environment.TickCount64;
            turn++;
        }

        public async Task<LivePending> Check(Engine e, double maxAge)
        {
            var t0 = turn;
            var answer = await e.CheckCall("snapshot", new JsonObject(), maxAge, hash);
            if (t0 != turn) return LivePending.Gone(this, "snapshot");
            if (answer.Kind == LiveAnswerKind.Failed)
            {
                Log.Write($"live: snapshot could not be checked: {answer.Error}");
                return LivePending.Failed(this, "snapshot");
            }
            if (answer.Kind == LiveAnswerKind.Same || answer.Value == text)
            {
                hash = answer.Hash;
                at = Environment.TickCount64;
                return LivePending.Same(this, "snapshot");
            }
            Snapshot s;
            try { s = await Task.Run(() => Decode<Snapshot>("snapshot", answer.Value!)); }
            catch { return LivePending.Failed(this, "snapshot"); }
            if (t0 != turn) return LivePending.Gone(this, "snapshot");
            var value = answer.Value!;
            var h = answer.Hash;
            return LivePending.New(this, "snapshot", () =>
            {
                if (t0 != turn) return false;
                text = value;
                hash = h;
                at = Environment.TickCount64;
                turn++;
                e.Answers.Keep(value, "snapshot", new JsonObject(), e.DataVersion);
                e.Snapshot = s;
                e.CountsLive = true;
                if (s.Lms != null && s.Lms != e.Lms) e.Lms = s.Lms;
                return true;
            });
        }
    }
}
