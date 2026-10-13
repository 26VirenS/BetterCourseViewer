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
/// Saved on a PC without Windows Hello (or where it would not lock it: its prompt cancelled, its signatures not the
/// same twice, an error), the password stays in the vault, as Simpl 0.2.7 kept every one, and Settings says so. One
/// kept so — 0.2.7's, or one saved before Windows Hello was set up — is locked with Windows Hello at its next use, just
/// after Simpl has signed in with it (one prompt; cancelled, not asked again: Settings' Lock with Windows Hello does it).
/// Windows Hello's key gone (Windows Hello reset, its PIN removed): what it locked can never be opened, so the saved
/// sign-in is forgotten, said in one line of the log, and the next "Stay logged in?" keeps it again.
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

    /// <summary>
    /// The saved sign-in's password, for the sign-in page about to be filled: one locked with Windows Hello asks for it
    /// (<paramref name="reason"/> says why: the log, and the "Logging you in" cover meanwhile); one in the vault, at once.
    /// Null when there is none, or the prompt was cancelled or failed (kept, and asked again next time) — or Windows
    /// Hello's key is gone, when it is forgotten (<see cref="Load"/> then says none).
    /// </summary>
    public static async Task<string?> UnlockAsync(string reason)
    {
        if (Launch.IsDevelopment) return memoryPass;
        var r = Record(out var at);
        if (r == null) return null;
        if (!r.LockedWithHello) return r.Pass;
        var (result, key) = await Hello.SignAsync(r.ChallengeBytes!, reason);
        try
        {
            switch (LoginRecord.AfterUnlock(result))
            {
                case UnlockStep.Open:
                    if (key == null) return null;
                    if (r.OpenPass(key) is { } pass) return pass;
                    Forget(at, "Windows Hello's key now is not the one it was locked with");
                    return null;
                case UnlockStep.Forget:
                    Forget(at, "its Windows Hello key is gone (Windows Hello reset, or its PIN removed)");
                    return null;
                default:
                    return null;
            }
        }
        finally { if (key != null) CryptographicOperations.ZeroMemory(key); }
    }

    /// <summary>
    /// "Stay Logged In": kept, its password locked with Windows Hello where it is set up (Windows Hello asks); else — no
    /// Windows Hello, its prompt cancelled, or it could not — kept in the vault, saying why. False only when it could
    /// not be written at all. Never throws.
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
        }
        if (ok) Log.Write(record.LockedWithHello ? "sign-in: kept, locked with Windows Hello" : $"sign-in: kept in the vault{(record.Hello is { } why ? $" ({why}: not locked with Windows Hello)" : "")}");
        Changed?.Invoke();
        return ok;
    }

    /// <summary>
    /// The saved sign-in, kept in the vault, locked with Windows Hello now (Windows Hello asks): Settings' Lock with
    /// Windows Hello, or <see cref="LockAtUseAsync"/>. Cancelled or refused, it stays in the vault, saying why.
    /// </summary>
    public static async Task<HelloResult> LockAsync(string reason)
    {
        if (Launch.IsDevelopment) return HelloResult.Failed;
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
        }
        if (!ok) return HelloResult.Failed;
        if (result == HelloResult.Success) Log.Write("sign-in: locked with Windows Hello");
        Changed?.Invoke();
        return result;
    }

    /// <summary>
    /// Simpl has just signed in with the saved sign-in: one kept in the vault alone — by Simpl 0.2.7, or while Windows
    /// Hello was not set up — is locked with Windows Hello now, where it is (one prompt). Never one whose lock was
    /// cancelled or could not be made, and never one with no saved sign-in to lock.
    /// </summary>
    public static async Task LockAtUseAsync()
    {
        if (Launch.IsDevelopment || Record(out _) is not { } r || r.LockedWithHello || r.Hello != null) return;
        if (!r.LockAtUse(await Hello.CheckAsync())) return;
        await LockAsync(LockReason);
    }

    /// <summary>Forgotten (Forget, Sign out): login.bin deleted and Windows Hello's key with it — that in the background.</summary>
    public static void Clear() => _ = ClearAsync();

    /// <summary>Forgotten, and Windows Hello's key deleted (awaited: Reset Everything and Uninstall's erase).</summary>
    public static Task ClearAsync()
    {
        memory = null;
        memoryPass = null;
        sampled = true;
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

    /// <summary>(Under Gate.) login.bin deleted.</summary>
    private static void Remove()
    {
        read = true;
        kept = null;
        version++;
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

    /// <summary>(Under Gate.) Sealed into login.bin, and held as sealed.</summary>
    private static bool Keep(LoginRecord record)
    {
        var plain = record.ToJson();
        try
        {
            kept = Vault.Write(AppPaths.Login, plain, Purpose);
            version++;
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
