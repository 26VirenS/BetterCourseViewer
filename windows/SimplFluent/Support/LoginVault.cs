using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Simpl.Support;

/// <summary>
/// The school sign-in the student chose to keep ("Stay logged in?"), as Settings and the sign-in see it (the Mac's
/// SavedLogin): its sign-in page's host, the username, when it was saved, and whether its password is locked with
/// Windows Hello. Never the password: <see cref="LoginVault.UnlockAsync"/> alone gives that, when it is needed.
/// </summary>
public sealed record SavedLogin(string Host, string User, DateTime SavedAt, bool LockedWithHello);

/// <summary>
/// Where the saved sign-in is kept (the Mac's LoginVault, its Keychain): inside Simpl, in login.bin, sealed by the vault
/// (Vault) — so it is encrypted with a key this PC's TPM and Windows hold for this user alone, never roamed, opened with
/// no prompt — and its password locked with Windows Hello besides (Hello, LoginRecord): sealed again under a key only
/// Windows Hello gives back, once the student has shown their face, finger or PIN. So Simpl opens, and Settings and the
/// sign-in know there is a saved sign-in and whose, without asking anything; the password is opened only when Simpl is
/// about to type it into the school's sign-in page, and Windows asks first.
/// <para/>
/// Saved on a PC without Windows Hello (or where it would not lock it: its prompt cancelled, an error), the password
/// stays in the vault, as Simpl 0.2.7 kept every one, and Settings says so. One kept so — 0.2.7's, or one saved before
/// Windows Hello was set up — is locked with Windows Hello at its next use, just after Simpl has signed in with it
/// (cancelled, not asked again: Settings' Lock with Windows Hello does it). Windows Hello's key gone (Windows Hello
/// reset, its PIN removed), or giving back a key the password does not open with: what it locked can never be opened,
/// so the saved sign-in is forgotten, said in one line of the log, and the next "Stay logged in?" keeps it again.
/// <para/>
/// Windows Hello is asked for the password at most once a run (UnlockGate): however many ask at once, one prompt, and
/// every one of them given its answer. What it gives — or the password just typed at "Stay logged in?", or just locked
/// as it was used — is held for the rest of the run, encrypted for this process alone (RunSecret), and let go when the
/// sign-in is kept anew or forgotten, signed out, everything erased, the school refuses the password, or Settings locks
/// it. Its prompt cancelled, it is not asked again by itself this run until the student asks: Start Over, signing in by
/// hand, Settings.
/// <para/>
/// login.bin is held in memory only as sealed, and opened each time it is asked for. The browser underneath never sees
/// it: WebView2's own password saving and autofill are off (Engine). A development run (the mock, a demo, a picture)
/// keeps nothing past the run and never asks Windows Hello. An earlier Simpl kept it in Windows' Credential Manager as
/// "Simpl Courses/sign-in": found there, it is moved into the vault and deleted from there (and deleted again whenever
/// the sign-in is forgotten, should it be back).
/// </summary>
public static class LoginVault
{
    /// <summary>Where an earlier Simpl kept it: Credential Manager's generic credential of this name.</summary>
    public const string Target = "Simpl Courses/sign-in";
    private const string Purpose = "login";
    /// <summary>What locking it with Windows Hello is said to be for (Hello: the log, and what shows meanwhile).</summary>
    public const string LockReason = "Lock your saved sign-in with Windows Hello";
    /// <summary>Raised when the saved sign-in is kept, locked or forgotten (Settings shows it); from any thread.</summary>
    public static event Action? Changed;
    private static readonly object Gate = new();
    private static SavedLogin? memory;
    private static string? memoryPass;
    private static bool sampled;
    /// <summary>The saved password's one unlock this run, for the whole app (UnlockGate): Windows Hello asked once however
    /// many ask, what it gives held for the rest of the run, a cancelled prompt not asked again by itself.</summary>
    private static readonly UnlockGate Once = new(OpenWithHello);
    /// <summary>The lock under way (Settings', or one as the sign-in is used): asked for again meanwhile, it is awaited.</summary>
    private static Task<HelloResult>? locking;
    /// <summary>login.bin's bytes, sealed, once read (null: none kept).</summary>
    private static byte[]? kept;
    private static bool read;
    /// <summary>Moved on each time login.bin is written or deleted: what was read before is stale.</summary>
    private static int version;

    /// <summary>login.bin read (and a sign-in an earlier Simpl kept in Credential Manager moved in) on a worker as the app
    /// starts, once the vault is open, so asking for it later reads nothing from disk; and whether Windows Hello is set
    /// up here (what Settings and "Stay logged in?" say).</summary>
    public static void Preload()
    {
        if (Launch.IsDevelopment) return;
        _ = Task.Run(async () =>
        {
            await Vault.Ready;
            lock (Gate) Ensure();
        });
        _ = Hello.CheckAsync();
    }

    /// <summary>The saved sign-in's facts — without its password, and with nothing asked — or null: none kept.</summary>
    public static SavedLogin? Load()
    {
        if (Launch.IsDevelopment) return Development();
        return Record(out _) is { } r ? new SavedLogin(r.Host, r.User, r.SavedAt, r.LockedWithHello) : null;
    }

    /// <summary>Windows Hello is being asked for the saved password now (one request for the whole app).</summary>
    public static bool Unlocking => Once.Asking;

    /// <summary>Windows Hello's prompt for the saved password was cancelled this run (or Windows could not): the sign-in
    /// page is not filled by itself, and Windows Hello not asked, until the student asks (<see cref="AskAgain"/>).</summary>
    public static bool HeldBack => !Launch.IsDevelopment && Once.HeldBack;

    /// <summary>The student did something that asks for the saved sign-in (Start Over, signing in by hand, Settings):
    /// after a cancelled prompt, Windows Hello may be asked again.</summary>
    public static void AskAgain() => Once.AskAgain();

    /// <summary>The school refused the saved password (a wrong or changed one): what this run held of it let go.</summary>
    public static void Refused()
    {
        if (!Once.Holds) return;
        Once.Wipe();
        Log.Write("sign-in: the password held for this run let go (the school did not take it)");
    }

    /// <summary>
    /// The saved sign-in's password, for the sign-in page about to be filled. One in the vault: at once. One locked with
    /// Windows Hello: held already this run, at once; else Windows Hello asks (<paramref name="reason"/> says why: the
    /// log, and the "Logging you in" cover meanwhile) — once for however many ask at once, every one given its answer —
    /// and what it gives is held for the rest of the run. Null when there is none, or the prompt was cancelled or failed
    /// (kept, and not asked again by itself this run: <see cref="HeldBack"/>) — or Windows Hello's key is gone, or gives
    /// a key the password does not open with, when it is forgotten (<see cref="Load"/> then says none).
    /// </summary>
    public static async Task<string?> UnlockAsync(string reason)
    {
        if (Launch.IsDevelopment) return memoryPass;
        var r = Record(out _);
        if (r == null) return null;
        if (!r.LockedWithHello) return r.Pass;
        var held = Once.Holds;
        var pass = await Once.OpenAsync(reason);
        if (held && pass != null) Log.Write("sign-in: opened without asking Windows Hello again (held since it answered this run)");
        return pass;
    }

    /// <summary>
    /// Windows Hello asked to open the saved password (the gate's one prompt): its key signs the record's challenge, and
    /// the password opens with that signature's key. This is also where a PC whose Windows Hello could not give the same
    /// key back would show (Hello.CreateAsync signs once and leaves that to here): the password does not open, so the
    /// sign-in is forgotten, said in the log, and the next "Stay logged in?" keeps it again — as when its key is gone.
    /// </summary>
    private static async Task<UnlockGate.Answer> OpenWithHello(string reason)
    {
        var r = Record(out var at);
        if (r == null) return new(null, false);
        if (!r.LockedWithHello) return new(r.Pass, false); // (kept anew in the vault meanwhile: nothing to ask)
        var (result, key) = await Hello.SignAsync(r.ChallengeBytes!, reason);
        try
        {
            switch (LoginRecord.AfterUnlock(result))
            {
                case UnlockStep.Open:
                    if (key == null) return new(null, true);
                    if (r.OpenPass(key) is { } pass) return new(pass, false);
                    Forget(at, "Windows Hello's key now is not the one it was locked with");
                    return new(null, false);
                case UnlockStep.Forget:
                    Forget(at, "its Windows Hello key is gone (Windows Hello reset, or its PIN removed)");
                    return new(null, false);
                default:
                    Log.Write("sign-in: kept, not opened; Windows Hello is not asked again by itself this run (Start Over or signing in asks again)");
                    return new(null, true);
            }
        }
        finally { if (key != null) CryptographicOperations.ZeroMemory(key); }
    }

    /// <summary>
    /// "Stay Logged In": kept, its password locked with Windows Hello where it is set up (Windows Hello asks:
    /// Hello.CreateAsync) and held for the rest of the run, so it is not asked for again; else — no Windows Hello, its
    /// prompt cancelled, or it could not — kept in the vault, saying why. False only when it could not be written at
    /// all. Never throws.
    /// </summary>
    public static async Task<bool> SaveAsync(string host, string user, string pass)
    {
        var at = DateTime.UtcNow;
        if (Launch.IsDevelopment)
        {
            memory = new SavedLogin(host, user, at, Hello.DevState == "locked");
            memoryPass = pass;
            sampled = true;
            Changed?.Invoke();
            return true;
        }
        LoginRecord record;
        try
        {
            if (await Hello.CheckAsync())
            {
                var challenge = RandomNumberGenerator.GetBytes(LoginRecord.ChallengeSize);
                var (result, key) = await Hello.CreateAsync(challenge, LockReason);
                try
                {
                    record = result == HelloResult.Success
                        ? LoginRecord.WithHello(host, user, pass, at, challenge, key)
                        : LoginRecord.InVault(host, user, pass, at, LoginRecord.NotLockedBecause(result));
                }
                finally { if (key != null) CryptographicOperations.ZeroMemory(key); }
            }
            else record = LoginRecord.InVault(host, user, pass, at);
        }
        catch (Exception e)
        {
            Log.Write($"sign-in: Windows Hello: {e.GetType().Name}: {e.Message.Trim()}");
            record = LoginRecord.InVault(host, user, pass, at, LoginRecord.Unusable);
        }
        bool ok;
        lock (Gate)
        {
            read = true;
            ok = Keep(record);
            if (ok) ForgetOld();
            // (the password was just typed: held for this run, so Windows Hello is not asked for it again)
            if (ok && record.LockedWithHello) Once.Hold(pass);
            else Once.AskAgain();
        }
        if (ok) Log.Write(record.LockedWithHello ? "sign-in: kept, locked with Windows Hello" : $"sign-in: kept in the vault{(record.Hello is { } why ? $" ({why}: not locked with Windows Hello)" : "")}");
        Changed?.Invoke();
        return ok;
    }

    /// <summary>
    /// The saved sign-in, kept in the vault, locked with Windows Hello now (Windows Hello asks: Hello.CreateAsync):
    /// Settings' Lock with Windows Hello. Cancelled or refused, it stays in the vault, saying why. The student asked, so
    /// nothing is held back any more; and nothing is held for the run after it either: the next sign-in asks Windows
    /// Hello, as one who has just locked it expects.
    /// </summary>
    public static Task<HelloResult> LockAsync(string reason) => LockOnce(reason, hold: false);

    /// <summary>
    /// Simpl has just signed in with the saved sign-in: one kept in the vault alone — by Simpl 0.2.7, or while Windows
    /// Hello was not set up — is locked with Windows Hello now, where it is, and its password (just used) held for the
    /// rest of the run, so locking it is the run's only prompt. Never one whose lock was cancelled or could not be made,
    /// and never one with no saved sign-in to lock.
    /// </summary>
    public static async Task LockAtUseAsync()
    {
        if (Launch.IsDevelopment || Record(out _) is not { } r || r.LockedWithHello || r.Hello != null) return;
        if (!r.LockAtUse(await Hello.CheckAsync())) return;
        await LockOnce(LockReason, hold: true);
    }

    /// <summary>One lock at a time for the whole app: asked for while one is under way, that one is awaited (its prompt
    /// the only one).</summary>
    private static Task<HelloResult> LockOnce(string reason, bool hold)
    {
        TaskCompletionSource<HelloResult> done;
        lock (Gate)
        {
            if (locking != null) return locking;
            done = new TaskCompletionSource<HelloResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            locking = done.Task;
        }
        _ = Run();
        return done.Task;

        async Task Run()
        {
            var result = HelloResult.Failed;
            try { result = await Lock(reason, hold); }
            catch (Exception e) { Log.Write($"sign-in: locking: {e.GetType().Name}: {e.Message.Trim()}"); }
            lock (Gate) locking = null;
            done.SetResult(result);
        }
    }

    private static async Task<HelloResult> Lock(string reason, bool hold)
    {
        if (Launch.IsDevelopment) return HelloResult.Failed;
        if (!hold) Once.AskAgain();
        var r = Record(out var at);
        if (r == null || r.Pass == null) return r?.LockedWithHello == true ? HelloResult.Success : HelloResult.Failed;
        if (!await Hello.CheckAsync()) return HelloResult.Failed;
        var challenge = RandomNumberGenerator.GetBytes(LoginRecord.ChallengeSize);
        var (result, key) = await Hello.CreateAsync(challenge, reason);
        LoginRecord next;
        try
        {
            next = result == HelloResult.Success
                ? LoginRecord.WithHello(r.Host, r.User, r.Pass, r.SavedAt, challenge, key)
                : r.NotLocked(LoginRecord.NotLockedBecause(result));
        }
        finally { if (key != null) CryptographicOperations.ZeroMemory(key); }
        bool ok;
        lock (Gate)
        {
            // (kept anew or forgotten while Windows Hello asked: that stands)
            ok = at == version && Keep(next);
            // (locked as it was used: the password just used held for this run, so Windows Hello is not asked for it)
            if (ok && hold && next.LockedWithHello) Once.Hold(r.Pass);
        }
        if (!ok) return HelloResult.Failed;
        if (result == HelloResult.Success) Log.Write("sign-in: locked with Windows Hello");
        Changed?.Invoke();
        return result;
    }

    /// <summary>Forgotten (Forget, Sign out): login.bin deleted and Windows Hello's key with it — that in the background.</summary>
    public static void Clear() => _ = ClearAsync();

    /// <summary>Forgotten, and Windows Hello's key deleted (awaited: Reset Everything and Uninstall's erase); what this run
    /// held of the password let go, and nothing held back.</summary>
    public static Task ClearAsync()
    {
        memory = null;
        memoryPass = null;
        sampled = true;
        Once.Wipe();
        Once.AskAgain();
        if (Launch.IsDevelopment)
        {
            Changed?.Invoke();
            return Task.CompletedTask;
        }
        lock (Gate) Remove();
        ForgetOld();
        Changed?.Invoke();
        return Hello.DeleteAsync();
    }

    /// <summary>One locked with Windows Hello that can never be opened now: forgotten — unless it was kept anew or
    /// forgotten meanwhile — said in the log, and its key deleted.</summary>
    private static void Forget(int at, string why)
    {
        lock (Gate)
        {
            if (at != version) return;
            Remove();
        }
        Log.Write($"sign-in: forgotten, as {why}; the next Stay Logged In keeps it again");
        _ = Hello.DeleteAsync();
        Changed?.Invoke();
    }

    /// <summary>(Under Gate.) login.bin deleted, and what this run held of its password let go.</summary>
    private static void Remove()
    {
        read = true;
        kept = null;
        version++;
        Once.Wipe();
        try { File.Delete(AppPaths.Login); } catch (Exception e) { Log.Write("sign-in: login.bin could not be deleted: " + e.Message); }
        try { File.Delete(AppPaths.Login + ".tmp"); } catch { }
    }

    /// <summary>
    /// A development run's: only what this run was given — or, with --hello-state (the screenshot suite's Settings), one
    /// as a student's would show, locked with Windows Hello ("locked") or not ("none"): never on disk, never asked for.
    /// </summary>
    private static SavedLogin? Development()
    {
        if (!sampled)
        {
            sampled = true;
            if (Hello.DevState is { } state) memory ??= new SavedLogin("login.example.edu", "student", DateTime.UtcNow, state == "locked");
        }
        return memory;
    }

    /// <summary>login.bin's record, opened from the sealed bytes held, and the version it is (null: none kept).</summary>
    private static LoginRecord? Record(out int at)
    {
        byte[]? plain = null;
        at = -1;
        try
        {
            lock (Gate)
            {
                Ensure();
                at = version;
                if (kept == null) return null;
                plain = Vault.Open(kept, Purpose);
            }
            return plain == null ? null : LoginRecord.Parse(plain);
        }
        catch { return null; } // (the vault erased: Simpl is on its way out)
        finally { if (plain != null) CryptographicOperations.ZeroMemory(plain); }
    }

    /// <summary>(Under Gate.) login.bin read once; none there, an earlier Simpl's from Credential Manager moved in.</summary>
    private static void Ensure()
    {
        if (read) return;
        read = true;
        try
        {
            byte[]? data = null;
            try { data = File.ReadAllBytes(AppPaths.Login); }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { }
            if (data != null)
            {
                var plain = Vault.Open(data, Purpose);
                if (plain != null)
                {
                    CryptographicOperations.ZeroMemory(plain);
                    kept = data;
                }
                else if (Vault.SetAside(AppPaths.Login) is { } dir) Log.Write($"sign-in: login.bin could not be opened with this vault's key: moved to {dir}");
                return;
            }
            if (ReadOld() is not { } old) return;
            if (Keep(old))
            {
                ForgetOld();
                Log.Write("sign-in: moved from Windows' Credential Manager into Simpl's vault");
            }
        }
        catch (Exception e) { Log.Write("sign-in: login.bin could not be read: " + e.Message); }
    }

    /// <summary>(Under Gate.) Sealed into login.bin, and held as sealed; what this run held of the password before let go
    /// (the caller holds the new one, where it should be held).</summary>
    private static bool Keep(LoginRecord record)
    {
        var plain = record.ToJson();
        try
        {
            kept = Vault.Write(AppPaths.Login, plain, Purpose);
            version++;
            Once.Wipe();
            return true;
        }
        catch (Exception e)
        {
            Log.Write("sign-in: could not be kept: " + e.Message);
            return false;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    // ---- what an earlier Simpl kept in Credential Manager ----

    private static LoginRecord? ReadOld()
    {
        var ptr = IntPtr.Zero;
        byte[]? bytes = null;
        try
        {
            if (!CredReadW(Target, CRED_TYPE_GENERIC, 0, out ptr) || ptr == IntPtr.Zero) return null;
            var c = Marshal.PtrToStructure<CREDENTIAL>(ptr);
            if (c.CredentialBlob == IntPtr.Zero || c.CredentialBlobSize == 0) return null;
            bytes = new byte[c.CredentialBlobSize];
            Marshal.Copy(c.CredentialBlob, bytes, 0, bytes.Length);
            // (kept as Simpl 0.2.7 kept one: in the vault, to be locked with Windows Hello at its next use)
            return LoginRecord.Parse(bytes);
        }
        catch { return null; }
        finally
        {
            if (bytes != null) CryptographicOperations.ZeroMemory(bytes);
            if (ptr != IntPtr.Zero) CredFree(ptr);
        }
    }

    /// <summary>Credential Manager's copy deleted, if there is one (never in a development run).</summary>
    public static void ForgetOld()
    {
        if (Launch.IsDevelopment) return;
        try { CredDeleteW(Target, CRED_TYPE_GENERIC, 0); } catch { }
    }

    private const uint CRED_TYPE_GENERIC = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CredReadW(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CredDeleteW(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr buffer);
}
