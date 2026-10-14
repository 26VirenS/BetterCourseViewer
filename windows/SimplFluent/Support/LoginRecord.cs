using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Simpl.Support;

/// <summary>What Windows Hello answered a request of Simpl's (Hello), as the saved sign-in takes it.</summary>
public enum HelloResult
{
    /// <summary>Done: the student showed their face, finger or PIN, and the key signed.</summary>
    Success,
    /// <summary>The prompt was cancelled (or the student chose their password instead): nothing signed, nothing lost.</summary>
    Canceled,
    /// <summary>Simpl's key is not there any more (Windows Hello reset, its PIN removed, another PC or user).</summary>
    Gone,
    /// <summary>Anything else Windows answered (an error, the security chip locked after wrong PINs, no Windows Hello).</summary>
    Failed,
}

/// <summary>What a sign-in locked with Windows Hello does after Windows Hello was asked to open it.</summary>
public enum UnlockStep
{
    /// <summary>The key given: the password opened with it.</summary>
    Open,
    /// <summary>Its key is gone: the saved sign-in can never be opened again, and is forgotten.</summary>
    Forget,
    /// <summary>Cancelled, or Windows could not say: kept as it is, and nothing filled in this time.</summary>
    Keep,
}

/// <summary>
/// login.bin's record, as the vault opens it (LoginVault): the saved sign-in's facts — its sign-in page's host, the
/// username, when it was saved — and its password, kept one of two ways. Locked with Windows Hello ("lock": "hello"):
/// in "sealed", AES-256-GCM under a key only Windows Hello gives back — the SHA-256 of its key's signature over
/// "challenge", 32 random bytes kept here, which nothing but that key in this PC's Windows Hello can make — and bound
/// to the host and the username (the cipher's associated data), so the vault alone cannot open it. Or in the vault
/// ("lock" missing, as Simpl 0.2.7 kept every one): "pass", inside the sealed file. One kept in the vault says in
/// "hello" why it is not locked: "declined" (Windows Hello's prompt was cancelled when it was to be), "unusable" (it
/// could not lock it on this PC); none, it is locked at its next use if Windows Hello is set up by then.
/// <para/>
/// Nothing here touches Windows: reading, sealing, opening and what to do next are plain logic, the same on any PC.
/// </summary>
public sealed class LoginRecord
{
    public const string HelloLock = "hello";
    public const string Declined = "declined", Unusable = "unusable";
    /// <summary>The challenge Windows Hello signs: 32 random bytes, new each time a sign-in is locked.</summary>
    public const int ChallengeSize = 32;
    private const int NonceSize = 12, TagSize = 16, KeySize = 32;
    /// <summary>What the sealed password is bound to besides its host and username: this record's kind, its version.</summary>
    private const string Binding = "SimplCourses.Simpl.SignIn.v1";

    public string Host { get; set; } = "";
    public string User { get; set; } = "";
    public DateTime SavedAt { get; set; }
    /// <summary>"hello": the password sealed under Windows Hello's key; missing (or anything else): in the vault.</summary>
    public string? Lock { get; set; }
    /// <summary>The password, for one kept in the vault; never written for one locked with Windows Hello.</summary>
    public string? Pass { get; set; }
    /// <summary>Base64: what Windows Hello's key signs to give the key back.</summary>
    public string? Challenge { get; set; }
    /// <summary>Base64: the nonce, the tag, then the password sealed.</summary>
    public string? Sealed { get; set; }
    /// <summary>Why one kept in the vault is not locked with Windows Hello (Declined, Unusable), or null.</summary>
    public string? Hello { get; set; }

    [JsonIgnore] public bool LockedWithHello => Lock == HelloLock;

    /// <summary>The challenge's bytes (a locked record always has them: <see cref="Parse"/> says so).</summary>
    [JsonIgnore] public byte[]? ChallengeBytes => Base64(Challenge) is { Length: ChallengeSize } c ? c : null;

    /// <summary>
    /// login.bin's plain bytes (or what an earlier Simpl kept in Credential Manager — the same JSON, Simpl 0.2.7's
    /// host, user, pass and savedAt), read; null when it is not a whole record: a host and a username, and either a
    /// password or, locked, a 32-byte challenge and something sealed. Never throws.
    /// </summary>
    public static LoginRecord? Parse(ReadOnlySpan<byte> json)
    {
        LoginRecord? r;
        try { r = JsonSerializer.Deserialize<LoginRecord>(json, Json.Options); }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException) { return null; }
        if (r == null || string.IsNullOrEmpty(r.Host) || string.IsNullOrEmpty(r.User)) return null;
        if (!r.LockedWithHello) return r.Pass == null ? null : r;
        return r.ChallengeBytes != null && Base64(r.Sealed) is { Length: >= NonceSize + TagSize } ? r : null;
    }

    /// <summary>The record as login.bin keeps it (inside the vault's seal): to be zeroed by whoever writes it.</summary>
    public byte[] ToJson() => JsonSerializer.SerializeToUtf8Bytes(this, Json.Script);

    /// <summary>One kept in the vault, as Simpl 0.2.7 kept every one; <paramref name="hello"/>: why it is not locked.</summary>
    public static LoginRecord InVault(string host, string user, string pass, DateTime savedAt, string? hello = null) =>
        new() { Host = host, User = user, SavedAt = savedAt, Pass = pass, Hello = hello };

    /// <summary>The same sign-in, still in the vault, saying why it is not locked with Windows Hello.</summary>
    public LoginRecord NotLocked(string? hello) => new() { Host = Host, User = User, SavedAt = SavedAt, Pass = Pass, Hello = hello };

    /// <summary>
    /// One locked with Windows Hello: <paramref name="pass"/> sealed with <paramref name="key"/> (<see cref="KeyFrom"/>
    /// of Windows Hello's signature over <paramref name="challenge"/>), and only the challenge and what is sealed kept.
    /// </summary>
    public static LoginRecord WithHello(string host, string user, string pass, DateTime savedAt, byte[] challenge, ReadOnlySpan<byte> key)
    {
        if (challenge.Length != ChallengeSize) throw new ArgumentException("the challenge is the wrong size", nameof(challenge));
        if (key.Length != KeySize) throw new ArgumentException("the key is the wrong size", nameof(key));
        var r = new LoginRecord { Host = host, User = user, SavedAt = savedAt, Lock = HelloLock, Challenge = Convert.ToBase64String(challenge) };
        var plain = Encoding.UTF8.GetBytes(pass);
        var box = new byte[NonceSize + TagSize + plain.Length];
        try
        {
            RandomNumberGenerator.Fill(box.AsSpan(0, NonceSize));
            using var gcm = new AesGcm(key, TagSize);
            gcm.Encrypt(box.AsSpan(0, NonceSize), plain, box.AsSpan(NonceSize + TagSize), box.AsSpan(NonceSize, TagSize), r.Bound());
            r.Sealed = Convert.ToBase64String(box);
            return r;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(box);
        }
    }

    /// <summary>The password, opened with the key Windows Hello gave back; null when that key is not the one it was
    /// sealed with (Windows Hello's key made again since), or what is kept was changed.</summary>
    public string? OpenPass(ReadOnlySpan<byte> key)
    {
        if (!LockedWithHello || key.Length != KeySize || Base64(Sealed) is not { Length: >= NonceSize + TagSize } box) return null;
        var plain = new byte[box.Length - NonceSize - TagSize];
        try
        {
            using var gcm = new AesGcm(key, TagSize);
            gcm.Decrypt(box.AsSpan(0, NonceSize), box.AsSpan(NonceSize + TagSize), box.AsSpan(NonceSize, TagSize), plain, Bound());
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException) { return null; }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    /// <summary>The key a signature of Windows Hello's gives: its SHA-256 (32 bytes, AES-256's), to be zeroed once used.
    /// The same each time, as a Windows Hello key's RSA PKCS#1 v1.5 signature of one challenge is; were a PC's ever not,
    /// <see cref="OpenPass"/> would not open with it, and LoginVault would forget the sign-in.</summary>
    public static byte[] KeyFrom(ReadOnlySpan<byte> signature) => SHA256.HashData(signature);

    /// <summary>At its use (Simpl just signed in with it), to be locked with Windows Hello now: one an earlier Simpl kept
    /// in the vault alone (or kept there while Windows Hello was not set up), on a PC where it is — never one whose lock
    /// was cancelled or could not be made (Settings' Lock with Windows Hello does those, when asked).</summary>
    public bool LockAtUse(bool helloSupported) => helloSupported && !LockedWithHello && Pass != null && Hello == null;

    /// <summary>Why one could not be locked, as it is kept in the vault instead: null when it was.</summary>
    public static string? NotLockedBecause(HelloResult result) => result switch
    {
        HelloResult.Success => null,
        HelloResult.Canceled => Declined,
        _ => Unusable,
    };

    /// <summary>What a locked one does after Windows Hello was asked to open it.</summary>
    public static UnlockStep AfterUnlock(HelloResult result) => result switch
    {
        HelloResult.Success => UnlockStep.Open,
        HelloResult.Gone => UnlockStep.Forget,
        _ => UnlockStep.Keep,
    };

    /// <summary>The sealed password's associated data: this kind of record, its host and its username.</summary>
    private byte[] Bound() => Encoding.UTF8.GetBytes(Binding + "\n" + Host + "\n" + User);

    private static byte[]? Base64(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        try { return Convert.FromBase64String(s); }
        catch (FormatException) { return null; }
    }
}
