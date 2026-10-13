using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Windows.Foundation;
using Windows.Security.Credentials;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;

namespace Simpl.Support;

/// <summary>
/// Windows Hello, as the saved sign-in uses it (LoginVault): a key of Windows' own for Simpl — made by
/// KeyCredentialManager in this user's Windows Hello container (in the TPM where there is one), never exportable —
/// that signs only once the student has shown their face, finger or PIN. What it signs is a challenge of 32 random
/// bytes kept beside the password; the SHA-256 of its signature (RSA, PKCS#1 v1.5: the same each time) is the
/// AES-256-GCM key the password is sealed with (LoginRecord). So the password opens only after Windows Hello has
/// asked: neither the vault alone nor another program running as the student, asking the TPM and Windows what Simpl
/// asks them, can open it without a prompt on screen. (KeePassXC's Windows Hello quick unlock works the same way.)
/// <para/>
/// Windows' prompt takes no words of the app's ("Making sure it's you"), so the reason each request is made for is
/// Simpl's to say: in simpl.log, and through <see cref="Asking"/> to whatever shows meanwhile (the "Logging you in"
/// cover). An app without a package of its own can have that prompt open behind its window: each request looks for
/// it (its window's class, "Credential Dialog Xaml Host") three times 150 ms apart, then once more 600 ms on, and
/// brings it forward. One request at a time. A development run (the mock, a demo, a picture) never asks Windows Hello
/// anything: --hello-state locked|none says what its Settings shows instead (the screenshot suite's pictures of each).
/// </summary>
public static class Hello
{
    private const string Name = "SimplCourses.Simpl.SignIn";
    private const string PromptClass = "Credential Dialog Xaml Host";

    private static readonly SemaphoreSlim One = new(1, 1);
    private static readonly object Gate = new();
    private static Task<bool>? checking;
    /// <summary>Whether Windows Hello is set up: -1 not yet known, 0 no, 1 yes (written by a worker, read anywhere).</summary>
    private static volatile int supported = -1;
    private static volatile string? asking;

    /// <summary>The key's name in Windows Hello: SimplCourses.Simpl.SignIn; a --profile's (the screenshot suite's, a
    /// developer's) its own, as the vault's TPM key is, so forgetting one never takes another's key with it.</summary>
    public static string KeyName => Vault.KeyNameFor(Name);

    /// <summary>--hello-state in a development run: "locked" (Settings as on a PC with Windows Hello, the sign-in locked
    /// with it) or "none" (as on one without); otherwise null.</summary>
    public static string? DevState => Launch.IsDevelopment ? Launch.Get("hello-state") : null;

    /// <summary>Windows Hello is set up for this user on this PC (a PIN at least): true or false, or null while not yet
    /// known (<see cref="CheckAsync"/>, begun as Simpl starts).</summary>
    public static bool? Supported => Launch.IsDevelopment ? DevState == "locked" : supported < 0 ? null : supported == 1;

    /// <summary>Raised when <see cref="Supported"/> changes (from any thread).</summary>
    public static event Action? Changed;

    /// <summary>Raised with the reason Windows Hello is being asked, as its prompt opens, and with null once it has
    /// answered (from any thread).</summary>
    public static event Action<string?>? Asking;

    /// <summary>What Windows Hello is being asked for now, or null.</summary>
    public static string? Reason => asking;

    /// <summary>
    /// Whether Windows Hello is set up here (KeyCredentialManager.IsSupportedAsync: nothing shown), asked once on a worker
    /// and kept; <paramref name="fresh"/> asks again (Settings coming forward: Windows Hello may have been set up in
    /// Windows' Settings meanwhile). Never throws: no answer is "no".
    /// </summary>
    public static Task<bool> CheckAsync(bool fresh = false)
    {
        if (Launch.IsDevelopment) return Task.FromResult(Supported == true);
        lock (Gate)
        {
            if (checking != null && !(fresh && checking.IsCompleted)) return checking;
            return checking = Task.Run(async () =>
            {
                bool yes;
                try { yes = await KeyCredentialManager.IsSupportedAsync(); }
                catch (Exception e)
                {
                    Log.Write($"hello: could not be asked whether it is set up ({e.GetType().Name}: {e.Message.Trim()})");
                    yes = false;
                }
                var was = Interlocked.Exchange(ref supported, yes ? 1 : 0);
                if (was != (yes ? 1 : 0))
                {
                    Log.Write(yes ? "hello: set up on this PC" : "hello: not set up on this PC");
                    Changed?.Invoke();
                }
                return yes;
            });
        }
    }

    /// <summary>
    /// A new key made — one there by that name replaced — and the challenge signed by it twice: Windows Hello asks (at
    /// making the key, and at signing unless Windows lets the answer just given stand). The same signature both times:
    /// its SHA-256, the key to seal with (Success; zeroed by the caller). Different: this PC's Windows Hello cannot give
    /// the same key back, so it is not used (Unusable) and the key just made is deleted; likewise when signing is
    /// cancelled or fails after the key was made.
    /// </summary>
    public static async Task<(HelloResult Result, byte[]? Key)> CreateAsync(byte[] challenge, string reason)
    {
        if (Launch.IsDevelopment) return (HelloResult.Failed, null);
        await One.WaitAsync();
        var watch = Stopwatch.StartNew();
        Ask(reason);
        HelloResult result = HelloResult.Failed;
        try
        {
            (result, var key) = await Task.Run(async () =>
            {
                var made = await Prompt(() => KeyCredentialManager.RequestCreateAsync(KeyName, KeyCredentialCreationOption.ReplaceExisting));
                // (nothing found while making one is Windows Hello not set up after all: it could not, not a key gone)
                if (made.Status != KeyCredentialStatus.Success) return (made.Status == KeyCredentialStatus.NotFound ? HelloResult.Failed : Outcome(made.Status), (byte[]?)null);
                byte[]? first = null, second = null;
                try
                {
                    (var one, first) = await Sign(made.Credential, challenge);
                    if (one != HelloResult.Success) { await Delete(); return (one, null); }
                    (var two, second) = await Sign(made.Credential, challenge);
                    if (two != HelloResult.Success) { await Delete(); return (two, null); }
                    if (!LoginRecord.SameSignature(first, second))
                    {
                        await Delete();
                        return (HelloResult.Unusable, null);
                    }
                    return (HelloResult.Success, LoginRecord.KeyFrom(first));
                }
                finally
                {
                    if (first != null) CryptographicOperations.ZeroMemory(first);
                    if (second != null) CryptographicOperations.ZeroMemory(second);
                }
            });
            return (result, key);
        }
        catch (Exception e)
        {
            Log.Write($"hello: making its key: {e.GetType().Name}: {e.Message.Trim()}");
            return (HelloResult.Failed, null);
        }
        finally
        {
            Log.Write($"hello: {reason} — {Said(result)} ({watch.ElapsedMilliseconds} ms)");
            Ask(null);
            One.Release();
        }
    }

    /// <summary>
    /// The key back: Simpl's key in Windows Hello found (nothing shown) and the challenge signed by it — Windows Hello
    /// asks — its signature's SHA-256 (Success; zeroed by the caller). Not found: Gone.
    /// </summary>
    public static async Task<(HelloResult Result, byte[]? Key)> SignAsync(byte[] challenge, string reason)
    {
        if (Launch.IsDevelopment) return (HelloResult.Failed, null);
        await One.WaitAsync();
        var watch = Stopwatch.StartNew();
        HelloResult result = HelloResult.Failed;
        try
        {
            (result, var key) = await Task.Run(async () =>
            {
                var found = await KeyCredentialManager.OpenAsync(KeyName);
                if (found.Status != KeyCredentialStatus.Success) return (Outcome(found.Status), (byte[]?)null);
                Ask(reason);
                var (r, signature) = await Sign(found.Credential, challenge);
                if (signature == null) return (r, null);
                try { return (r, LoginRecord.KeyFrom(signature)); }
                finally { CryptographicOperations.ZeroMemory(signature); }
            });
            return (result, key);
        }
        catch (Exception e)
        {
            Log.Write($"hello: asking for its key: {e.GetType().Name}: {e.Message.Trim()}");
            return (HelloResult.Failed, null);
        }
        finally
        {
            Log.Write($"hello: {reason} — {Said(result)} ({watch.ElapsedMilliseconds} ms)");
            Ask(null);
            One.Release();
        }
    }

    /// <summary>Simpl's key deleted from Windows Hello (the sign-in forgotten, signed out, everything erased): nothing
    /// shown, and nothing when there is none (or no Windows Hello here). Never throws.</summary>
    public static Task DeleteAsync() => Launch.IsDevelopment || supported == 0 ? Task.CompletedTask : Task.Run(Delete);

    private static async Task Delete()
    {
        try { await KeyCredentialManager.DeleteAsync(KeyName); }
        catch { /* (none there — what Windows says most often here — or Windows Hello not set up: nothing to delete) */ }
    }

    /// <summary>The challenge signed (Windows Hello asks): its signature's bytes, out of Windows' buffer.</summary>
    private static async Task<(HelloResult, byte[]?)> Sign(KeyCredential credential, byte[] challenge)
    {
        var signed = await Prompt(() => credential.RequestSignAsync(CryptographicBuffer.CreateFromByteArray(challenge)));
        if (signed.Status != KeyCredentialStatus.Success) return (Outcome(signed.Status), null);
        if (signed.Result is not { Length: > 0 } result) return (HelloResult.Failed, null); // (done, but nothing signed)
        CryptographicBuffer.CopyToByteArray(result, out var bytes);
        Wipe(result);
        return (HelloResult.Success, bytes);
    }

    /// <summary>A request that may show Windows Hello's prompt: Windows told any process may come forward (the prompt's
    /// own), then the request begun, and the prompt brought forward should it open behind.</summary>
    private static async Task<T> Prompt<T>(Func<IAsyncOperation<T>> start)
    {
        try { AllowSetForegroundWindow(ASFW_ANY); } catch { /* (only a nicety) */ }
        var request = start().AsTask();
        BringForward(request);
        return await request;
    }

    /// <summary>Windows Hello's prompt looked for while it may still be opening, and brought in front of Simpl.</summary>
    private static void BringForward(Task request) => _ = Task.Run(async () =>
    {
        foreach (var wait in new[] { 150, 150, 150, 600 })
        {
            if (await Task.WhenAny(request, Task.Delay(wait)) == request) return;
            try
            {
                var prompt = FindWindowW(PromptClass, null);
                if (prompt != IntPtr.Zero && SetForegroundWindow(prompt)) return;
            }
            catch { return; }
        }
    });

    /// <summary>The signature Windows handed back, zeroed where it lies (as far as Windows lets it be reached).</summary>
    private static void Wipe(IBuffer buffer)
    {
        try
        {
            var length = (int)buffer.Length;
            if (length > 0 && System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeMarshal.TryGetDataUnsafe(buffer, out var data) && data != IntPtr.Zero)
                Marshal.Copy(new byte[length], 0, data, length);
        }
        catch { /* (a buffer Windows will not let be written: freed as it is) */ }
    }

    private static void Ask(string? reason)
    {
        asking = reason;
        try { Asking?.Invoke(reason); } catch (Exception e) { Log.Write("hello: " + e.Message); }
    }

    private static HelloResult Outcome(KeyCredentialStatus status) => status switch
    {
        KeyCredentialStatus.Success => HelloResult.Success,
        KeyCredentialStatus.UserCanceled or KeyCredentialStatus.UserPrefersPassword => HelloResult.Canceled,
        KeyCredentialStatus.NotFound => HelloResult.Gone,
        _ => HelloResult.Failed,
    };

    private static string Said(HelloResult r) => r switch
    {
        HelloResult.Success => "done",
        HelloResult.Canceled => "cancelled",
        HelloResult.Gone => "its key is not there",
        HelloResult.Unusable => "its signatures differ on this PC, so it is not used",
        _ => "Windows could not",
    };

    private const uint ASFW_ANY = unchecked((uint)-1);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowW(string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
