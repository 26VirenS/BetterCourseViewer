using System.Text;
using Simpl.Support;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// When Windows Hello is asked for the saved password (Support/UnlockGate.cs) and how the run holds it after
/// (Support/RunSecret.cs): once for however many ask at once, not again once it has answered, again once what it gave
/// is let go, and not again after a cancelled prompt until the student asks. The prompt here is the tests' own, which
/// counts; Windows Hello itself is never asked.
/// </summary>
public class UnlockTests
{
    /// <summary>The tests' prompt: counts each time it is shown, and answers when told to (or at once, given an answer).</summary>
    private sealed class Prompt
    {
        private readonly UnlockGate.Answer? now;
        private TaskCompletionSource<UnlockGate.Answer> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Shown;

        public Prompt(UnlockGate.Answer? now = null) => this.now = now;

        public Task<UnlockGate.Answer> Ask(string reason)
        {
            Interlocked.Increment(ref Shown);
            return now is { } a ? Task.FromResult(a) : pending.Task;
        }

        public void Answer(UnlockGate.Answer a)
        {
            var was = pending;
            pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            was.SetResult(a);
        }
    }

    private const string Reason = "Unlock your saved sign-in with Windows Hello";
    private static readonly UnlockGate.Answer Opened = new("correct horse", false), Cancelled = new(null, true), None = new(null, false);

    [Fact]
    public async Task ManyAskingAtOnceShowOnePrompt()
    {
        var prompt = new Prompt();
        var gate = new UnlockGate(prompt.Ask);
        var asks = Enumerable.Range(0, 8).Select(_ => gate.OpenAsync(Reason)).ToArray();
        Assert.Equal(1, prompt.Shown);
        Assert.True(gate.Asking);
        Assert.All(asks, a => Assert.False(a.IsCompleted));
        prompt.Answer(Opened);
        Assert.All(await Task.WhenAll(asks), p => Assert.Equal("correct horse", p));
        Assert.False(gate.Asking);
        Assert.Equal(1, prompt.Shown);
    }

    [Fact]
    public async Task ManyThreadsAskingAtOnceShowOnePrompt()
    {
        var prompt = new Prompt();
        var gate = new UnlockGate(prompt.Ask);
        using var start = new ManualResetEventSlim();
        // (each on a thread of its own, all let go at once; what each is handed is its wait for the answer)
        var asks = Enumerable.Range(0, 32).Select(_ => Task.Factory.StartNew(() => { start.Wait(); return gate.OpenAsync(Reason); }, TaskCreationOptions.LongRunning)).ToArray();
        start.Set();
        var waiting = await Task.WhenAll(asks);
        Assert.Equal(1, prompt.Shown);
        prompt.Answer(Opened);
        Assert.All(await Task.WhenAll(waiting), p => Assert.Equal("correct horse", p));
        Assert.Equal(1, prompt.Shown);
    }

    [Fact]
    public async Task OnceItHasAnsweredItIsNotAskedAgain()
    {
        var prompt = new Prompt(Opened);
        var gate = new UnlockGate(prompt.Ask);
        Assert.Equal("correct horse", await gate.OpenAsync(Reason));
        Assert.True(gate.Holds);
        for (var i = 0; i < 5; i++) Assert.Equal("correct horse", await gate.OpenAsync(Reason));
        Assert.Equal(1, prompt.Shown);
    }

    [Fact]
    public async Task APasswordJustKeptAsksNothing()
    {
        var prompt = new Prompt(Opened);
        var gate = new UnlockGate(prompt.Ask);
        gate.Hold("typed at Stay logged in");
        Assert.Equal("typed at Stay logged in", await gate.OpenAsync(Reason));
        Assert.Equal(0, prompt.Shown);
    }

    [Fact]
    public async Task LetGoItIsAskedForAgain()
    {
        var prompt = new Prompt(Opened);
        var gate = new UnlockGate(prompt.Ask);
        await gate.OpenAsync(Reason);
        gate.Wipe(); // (forgotten, signed out, erased, refused by the school, locked from Settings)
        Assert.False(gate.Holds);
        Assert.Equal("correct horse", await gate.OpenAsync(Reason));
        Assert.Equal(2, prompt.Shown);
    }

    [Fact]
    public async Task CancelledItIsNotAskedAgainUntilTheStudentAsks()
    {
        var prompt = new Prompt(Cancelled);
        var gate = new UnlockGate(prompt.Ask);
        Assert.Null(await gate.OpenAsync(Reason));
        Assert.True(gate.HeldBack);
        for (var i = 0; i < 5; i++) Assert.Null(await gate.OpenAsync(Reason));
        Assert.Equal(1, prompt.Shown);
        gate.Wipe(); // (letting go of nothing held does not ask again by itself)
        Assert.Null(await gate.OpenAsync(Reason));
        Assert.Equal(1, prompt.Shown);
        gate.AskAgain(); // (Start Over, signing in by hand, Settings)
        Assert.False(gate.HeldBack);
        Assert.Null(await gate.OpenAsync(Reason));
        Assert.Equal(2, prompt.Shown);
    }

    [Fact]
    public async Task ACancelSharedByEveryoneWaiting()
    {
        var prompt = new Prompt();
        var gate = new UnlockGate(prompt.Ask);
        var asks = Enumerable.Range(0, 4).Select(_ => gate.OpenAsync(Reason)).ToArray();
        prompt.Answer(Cancelled);
        Assert.All(await Task.WhenAll(asks), p => Assert.Null(p));
        Assert.Null(await gate.OpenAsync(Reason));
        Assert.Equal(1, prompt.Shown);
    }

    [Fact]
    public async Task NothingToOpenIsNotHeldBack()
    {
        // (the key gone, the sign-in forgotten: nothing held back, so a sign-in saved later is asked for as usual)
        var prompt = new Prompt(None);
        var gate = new UnlockGate(prompt.Ask);
        Assert.Null(await gate.OpenAsync(Reason));
        Assert.False(gate.HeldBack);
        Assert.False(gate.Holds);
        Assert.Null(await gate.OpenAsync(Reason));
        Assert.Equal(2, prompt.Shown);
    }

    [Fact]
    public async Task AnAnswerLetGoOnItsWayIsNotHeld()
    {
        var prompt = new Prompt();
        var gate = new UnlockGate(prompt.Ask);
        var ask = gate.OpenAsync(Reason);
        gate.Wipe(); // (signed out while Windows Hello asked)
        prompt.Answer(Opened);
        Assert.Null(await ask);
        Assert.False(gate.Holds);
        var again = gate.OpenAsync(Reason);
        Assert.Equal(2, prompt.Shown);
        prompt.Answer(Opened);
        Assert.Equal("correct horse", await again);
    }

    [Fact]
    public async Task APromptThatFailsIsHeldBack()
    {
        var gate = new UnlockGate(_ => throw new InvalidOperationException("Windows could not"));
        Assert.Null(await gate.OpenAsync(Reason));
        Assert.True(gate.HeldBack);
        Assert.False(gate.Asking);
    }

    [Fact]
    public void TheHeldPasswordIsNotInMemoryAsText()
    {
        const string pass = "correct horse battery staple ✓";
        using var held = RunSecret.Hold(pass);
        var bytes = held.InMemory()!;
        Assert.Equal(-1, bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes("correct horse")));
        Assert.Equal(-1, bytes.AsSpan().IndexOf(Encoding.Unicode.GetBytes("correct horse")));
        Assert.Equal(pass, held.Open());
        Assert.Equal(pass, held.Open());
        held.Dispose();
        Assert.Null(held.Open());
        Assert.Null(held.InMemory());
        Assert.Equal("", RunSecret.Hold("").Open());
    }
}
