namespace Simpl.Support;

/// <summary>
/// When Windows Hello is asked for the saved sign-in's password (LoginVault holds the one gate for the whole app): the
/// decisions alone, with the prompt handed in, so the tests can count prompts without Windows Hello.
/// <list type="bullet">
/// <item>However many ask at once — the sign-in page saying so again while Windows Hello asks, or another of Simpl's
/// sign-ins (the engine made anew for another school) — Windows Hello is asked once, and every one of them waits for
/// that one answer.</item>
/// <item>Once Windows Hello has given the password (or the student has just typed it and it was kept), it is held for
/// the rest of the run (RunSecret: encrypted for this process alone, never a plain string kept) and Windows Hello is
/// not asked again.</item>
/// <item>Its prompt cancelled (or Windows could not): not asked again by itself this run (<see cref="HeldBack"/>) until
/// the student does something that asks for it — Start Over, signing in by hand, Settings (<see cref="AskAgain"/>).</item>
/// <item>Let go (<see cref="Wipe"/>) when what it holds no longer stands — the sign-in kept anew or forgotten, signed
/// out, everything erased, the password refused by the school, Settings locking it — and an answer still on its way
/// then is not held.</item>
/// </list>
/// </summary>
public sealed class UnlockGate
{
    /// <summary>What one prompt came to: the password, or none — <paramref name="Declined"/> when the saved sign-in is
    /// still kept but was not opened (the prompt cancelled, Windows could not), not when there was none to open.</summary>
    public readonly record struct Answer(string? Pass, bool Declined);

    private readonly Func<string, Task<Answer>> prompt;
    private readonly object sync = new();
    /// <summary>The one request under way, which every caller meanwhile awaits.</summary>
    private Task<string?>? asking;
    private RunSecret? held;
    private bool heldBack;
    /// <summary>Moved on by <see cref="Wipe"/> and <see cref="Hold"/>: an answer asked for before is not held.</summary>
    private int generation;

    public UnlockGate(Func<string, Task<Answer>> prompt) => this.prompt = prompt;

    /// <summary>The password is held for this run: opening it asks nothing.</summary>
    public bool Holds { get { lock (sync) return held != null; } }

    /// <summary>A prompt was cancelled this run (or Windows could not): not asked again until <see cref="AskAgain"/>.</summary>
    public bool HeldBack { get { lock (sync) return heldBack; } }

    /// <summary>Windows Hello is being asked now.</summary>
    public bool Asking { get { lock (sync) return asking != null; } }

    /// <summary>
    /// The password: the one held, at once; none while held back; else Windows Hello asked (<paramref name="reason"/>
    /// says why) — or, already being asked, that same answer awaited. Null when it was not opened. Never throws.
    /// </summary>
    public Task<string?> OpenAsync(string reason)
    {
        TaskCompletionSource<string?> done;
        int at;
        lock (sync)
        {
            if (held != null)
            {
                if (held.Open() is { } pass) return Task.FromResult<string?>(pass);
                held.Dispose(); // (it would not open: asked for again)
                held = null;
            }
            if (heldBack) return Task.FromResult<string?>(null);
            if (asking != null) return asking;
            done = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            asking = done.Task;
            at = generation;
        }
        // (begun outside the lock: the prompt reads the vault under a lock of its own, which may let go of this one)
        _ = Ask(reason, at, done);
        return done.Task;
    }

    private async Task Ask(string reason, int at, TaskCompletionSource<string?> done)
    {
        string? pass = null;
        try
        {
            Answer answer;
            try { answer = await prompt(reason); }
            catch { answer = new Answer(null, true); } // (as Windows could not: not asked again by itself)
            lock (sync)
            {
                if (asking == done.Task) asking = null;
                if (at == generation)
                {
                    if (answer.Pass != null)
                    {
                        Keep(answer.Pass);
                        pass = answer.Pass;
                    }
                    else if (answer.Declined) heldBack = true;
                }
            }
        }
        finally
        {
            // (whatever happened above, the request is over and every one waiting on it is answered: none waits forever,
            //  and the next one asks anew)
            lock (sync) if (asking == done.Task) asking = null;
            done.TrySetResult(pass);
        }
    }

    /// <summary>The password the student has just typed, kept (Stay logged in; a sign-in locked as it is used): held for
    /// the rest of the run, as one Windows Hello gave would be, and nothing held back any more.</summary>
    public void Hold(string pass)
    {
        lock (sync)
        {
            generation++;
            Keep(pass);
            heldBack = false;
        }
    }

    /// <summary>The student did something that asks for the saved sign-in (Start Over, signing in by hand, Settings):
    /// Windows Hello may be asked again after a cancelled prompt.</summary>
    public void AskAgain()
    {
        lock (sync) heldBack = false;
    }

    /// <summary>What is held let go (its bytes zeroed), and an answer still on its way not held.</summary>
    public void Wipe()
    {
        lock (sync)
        {
            generation++;
            held?.Dispose();
            held = null;
        }
    }

    /// <summary>(Under sync.) Held, in place of anything held before. Never throws: should it not be held (it could not
    /// be encrypted), nothing is, and Windows Hello is asked the next time, as before anything was held.</summary>
    private void Keep(string pass)
    {
        held?.Dispose();
        held = null;
        try { held = RunSecret.Hold(pass); }
        catch { /* (not held: never kept unencrypted instead) */ }
    }
}
