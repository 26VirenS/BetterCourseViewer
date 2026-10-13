using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Simpl.Support;

/// <summary>What holds the vault's key on this PC, as Settings says it.</summary>
public enum VaultProtection
{
    /// <summary>Nothing could keep it (Windows refused, or the folder cannot be written): it lasts while Simpl runs.</summary>
    None,
    /// <summary>The student's Windows account (DPAPI): a PC or VM without a TPM, or one not yet asked.</summary>
    WindowsAccount,
    /// <summary>This PC's security chip (a TPM key that never leaves it), and the Windows account over that.</summary>
    Tpm,
}

/// <summary>A file read through the vault: what it says, and how it was found.</summary>
public enum VaultState
{
    /// <summary>Not there (or not readable at all).</summary>
    Missing,
    /// <summary>Sealed by this vault, and opened.</summary>
    Sealed,
    /// <summary>Plain text, as Simpl kept it before the vault: to be written sealed by whoever reads it (migration).</summary>
    Legacy,
    /// <summary>Sealed, but not by this vault's key or changed since: set aside (or, for a cache, deleted), never read.</summary>
    Unreadable,
}

/// <summary>What <see cref="Vault.Read"/> found: the plain bytes (zeroed by <see cref="Zero"/> once used), and how.</summary>
public readonly record struct VaultFile(VaultState State, byte[]? Plain)
{
    public bool Legacy => State == VaultState.Legacy;
    public string? Text => Plain == null ? null : Encoding.UTF8.GetString(Plain);
    public void Zero()
    {
        if (Plain != null) CryptographicOperations.ZeroMemory(Plain);
    }
}

/// <summary>
/// Simpl's own encryption: the student wanted what Simpl keeps of theirs walled off, and nothing to unlock. Every store of
/// theirs on this PC — the preferences, the extension's storage, the answers kept, the flashcards, the saved sign-in — is
/// sealed with one key, AES-256-GCM, each file bound to what it is (its purpose is the cipher's associated data, so one
/// cannot be swapped for another) and a cache's file named by an HMAC of what it holds, so the names say nothing either.
/// <para/>
/// The key is 32 random bytes, made the first time and kept in vault.key wrapped twice: inside, by an RSA-2048 key of
/// this PC's TPM that never leaves the chip (Windows' Platform Crypto Provider, this user's own key store, OAEP); outside,
/// by Windows for this user alone (DPAPI). A PC or VM without a TPM keeps the outer wrap only, and says so. Nothing is
/// ever asked: the TPM and Windows open it for this user as Simpl starts — <see cref="Preload"/>, first thing in
/// Program.Main, on a worker (50–300 ms), so the first read of the preferences rarely waits.
/// <para/>
/// What it stops: the files copied, backed up, read by another account or an administrator, a lost disk — all noise
/// without this user's Windows sign-in on this very PC. What it cannot: a program running as this same user, while Simpl
/// is installed, may ask the TPM and Windows just as Simpl does; only an unlock prompt (Windows Hello) closes that, and
/// the student chose to have none.
/// <para/>
/// A vault.key that cannot be opened (the TPM cleared, the profile copied to another PC or user) never stops Simpl: one
/// line in the log, it and what it sealed moved to an Unreadable-… folder beside them (not deleted: put back, they open
/// again if the key ever does; only Reset Everything erases them), and a new vault made. Not here: simpl.log (for a
/// report, and nothing secret goes in it), files saved where the student chose, and WebView2's own folder (Chromium's:
/// it encrypts its cookies with DPAPI itself).
/// </summary>
public static class Vault
{
    private static ReadOnlySpan<byte> SealMagic => "SIMPLENC"u8;
    private static ReadOnlySpan<byte> KeyMagic => "SIMPLVK1"u8;
    /// <summary>What DPAPI binds the wrap to besides the user: Simpl's alone.</summary>
    private static ReadOnlySpan<byte> Entropy => "SimplCourses.Simpl.Vault.v1"u8;
    private const byte SealVersion = 1, KeyVersion = 1;
    private const int NonceSize = 12, TagSize = 16, KeySize = 32;
    /// <summary>A sealed file: SIMPLENC, its version, the nonce, the tag, then the ciphertext.</summary>
    private const int SealHeader = 8 + 1 + NonceSize + TagSize;
    /// <summary>vault.key: SIMPLVK1, its version, its flags, two bytes spare, the DPAPI blob's length, then the blob.</summary>
    private const int KeyHeader = 8 + 1 + 1 + 2 + 4;
    private const byte TpmLayer = 1, OaepSha1 = 2;
    private const string PlatformProvider = "Microsoft Platform Crypto Provider";

    // Gate guards the key's task and the erased state, held only briefly; KeyFileGate guards vault.key on disk (taken
    // alone, never inside Gate); TpmGate lets one TPM wrap be made at a time. Each vault made has its generation: what
    // was begun for an earlier one writes nothing.
    private static readonly object Gate = new();
    private static readonly object KeyFileGate = new();
    private static readonly object TpmGate = new();
    private static Task<byte[]>? opening;
    private static bool erased;
    private static int generation;
    private static volatile VaultProtection protection;
    private static string? aside;

    /// <summary>The key unwrapped (or made) on a worker, from the start (Program.Main, before anything reads a file).</summary>
    public static void Preload() => _ = Opening();

    /// <summary>The vault open: what reads Simpl's files on a worker as the app starts (BridgeStore's preload) awaits it.</summary>
    public static Task Ready => Opening();

    /// <summary>What holds the key now (the TPM's wrap is added a moment after a vault is made: it can take seconds).</summary>
    public static VaultProtection Protection
    {
        get
        {
            try { _ = Key; } catch { /* erased: none */ }
            return protection;
        }
    }

    private static Task<byte[]> Opening()
    {
        lock (Gate)
        {
            if (opening != null) return opening;
            var gen = generation;
            return opening = Task.Run(() => OpenOrMake(gen));
        }
    }

    /// <summary>The key, waited for the first time (the vault opening); after <see cref="Erase"/>, nothing — it throws.</summary>
    private static byte[] Key
    {
        get
        {
            Task<byte[]> t;
            lock (Gate)
            {
                if (erased) throw new InvalidOperationException("Simpl's vault was erased.");
                t = Opening();
            }
            return t.GetAwaiter().GetResult();
        }
    }

    // ---- sealing ----

    /// <summary>Sealed for <paramref name="purpose"/> (what the bytes are: "prefs", "login" …): only opened as that.</summary>
    public static byte[] Seal(ReadOnlySpan<byte> plain, string purpose)
    {
        var key = Key;
        var output = new byte[SealHeader + plain.Length];
        SealMagic.CopyTo(output);
        output[8] = SealVersion;
        var nonce = output.AsSpan(9, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var gcm = new AesGcm(key, TagSize);
        gcm.Encrypt(nonce, plain, output.AsSpan(SealHeader), output.AsSpan(9 + NonceSize, TagSize), Encoding.UTF8.GetBytes(purpose));
        return output;
    }

    /// <summary>The plain bytes, or null: not sealed, sealed with another key, sealed for another purpose, or changed.</summary>
    public static byte[]? Open(ReadOnlySpan<byte> data, string purpose)
    {
        if (!IsSealed(data) || data[8] != SealVersion) return null;
        var key = Key;
        var plain = new byte[data.Length - SealHeader];
        try
        {
            using var gcm = new AesGcm(key, TagSize);
            gcm.Decrypt(data.Slice(9, NonceSize), data[SealHeader..], data.Slice(9 + NonceSize, TagSize), plain, Encoding.UTF8.GetBytes(purpose));
            return plain;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plain);
            return null;
        }
    }

    public static byte[] SealText(string text, string purpose)
    {
        var plain = Encoding.UTF8.GetBytes(text);
        try { return Seal(plain, purpose); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public static string? OpenText(ReadOnlySpan<byte> data, string purpose)
    {
        var plain = Open(data, purpose);
        if (plain == null) return null;
        try { return Encoding.UTF8.GetString(plain); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    /// <summary>Begins with the vault's mark (SIMPLENC): sealed, by this vault or another.</summary>
    public static bool IsSealed(ReadOnlySpan<byte> data) => data.Length >= SealHeader && data.StartsWith(SealMagic);

    /// <summary>A file's name for <paramref name="purpose"/> that says nothing of it: 32 hex characters of an HMAC-SHA256
    /// under the vault's key (the answers kept are named so, and their school's folder too).</summary>
    public static string FileName(string purpose)
    {
        var mac = HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(purpose));
        return Convert.ToHexString(mac, 0, 16).ToLowerInvariant();
    }

    // ---- files ----

    /// <summary>Sealed and written whole or not at all (beside it first, then moved over it); the sealed bytes, as written.
    /// Throws when it cannot be written (the callers' writers try again).</summary>
    public static byte[] Write(string path, ReadOnlySpan<byte> plain, string purpose)
    {
        var data = Seal(plain, purpose);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, data);
        File.Move(tmp, path, true);
        return data;
    }

    public static byte[] Write(string path, string text, string purpose)
    {
        var plain = Encoding.UTF8.GetBytes(text);
        try { return Write(path, plain, purpose); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    /// <summary>
    /// A file of Simpl's, opened: sealed → its plain bytes; plain text from before the vault → as it is, marked Legacy, for
    /// the caller to write it sealed (and so the plain file is gone); sealed but unreadable → set aside in the Unreadable-…
    /// folder (a <paramref name="cache"/>'s: deleted), and nothing. Never throws.
    /// </summary>
    public static VaultFile Read(string path, string purpose, bool cache = false)
    {
        byte[] data;
        try { data = File.ReadAllBytes(path); }
        catch { return new VaultFile(VaultState.Missing, null); }
        if (!data.AsSpan().StartsWith(SealMagic))
        {
            // (written by Notepad, perhaps: the byte-order mark is not the JSON's)
            var bom = data.AsSpan().StartsWith("﻿"u8) ? 3 : 0;
            return new VaultFile(VaultState.Legacy, bom == 0 ? data : data[bom..]);
        }
        byte[]? plain;
        try { plain = Open(data, purpose); }
        catch { return new VaultFile(VaultState.Missing, null); } // (the vault erased: Simpl is on its way out)
        if (plain != null) return new VaultFile(VaultState.Sealed, plain);
        if (cache) { try { File.Delete(path); } catch { } }
        else if (SetAside(path) is { } dir) Log.Write($"vault: {Path.GetFileName(path)} could not be opened with this vault's key: moved to {dir}");
        return new VaultFile(VaultState.Unreadable, null);
    }

    /// <summary>A file that cannot be read moved, as it is, into this run's Unreadable-… folder beside the others (never
    /// over one there already): that folder's name, or null (nothing there, or it would not move — said in the log).</summary>
    public static string? SetAside(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var dir = AsideFolder();
            var name = Path.GetFileName(path);
            var to = Path.Combine(dir, name);
            for (var n = 2; File.Exists(to); n++) to = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(name)} ({n}){Path.GetExtension(name)}");
            File.Move(path, to);
            return Path.GetFileName(dir);
        }
        catch (Exception e)
        {
            Log.Write($"vault: {Path.GetFileName(path)} could not be moved aside: {e.Message}");
            return null;
        }
    }

    /// <summary>This run's Unreadable-yyyyMMdd-HHmmss folder (one of its own, should another have that second's name).</summary>
    private static string AsideFolder()
    {
        lock (Gate)
        {
            if (aside == null)
            {
                var name = Path.Combine(AppPaths.Root, "Unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture));
                aside = name;
                for (var n = 2; Directory.Exists(aside); n++) aside = $"{name}-{n}";
            }
            Directory.CreateDirectory(aside);
            return aside;
        }
    }

    /// <summary>
    /// Reset Everything and Uninstall's "Also erase": vault.key and the TPM's key deleted, and the key forgotten — so what
    /// was sealed with it can never be opened again, wherever a copy went. Nothing is sealed after it by this copy of Simpl.
    /// </summary>
    public static void Erase()
    {
        byte[]? key = null;
        lock (Gate)
        {
            erased = true;
            generation++;
            if (opening is { IsCompletedSuccessfully: true } t) key = t.Result;
            opening = null;
            protection = VaultProtection.None;
        }
        lock (KeyFileGate)
        {
            try { File.Delete(AppPaths.VaultKey); } catch (Exception e) { Log.Write("vault: erasing vault.key: " + e.Message); }
            try { File.Delete(AppPaths.VaultKey + ".tmp"); } catch { }
        }
        DeleteTpmKey();
        if (key != null) CryptographicOperations.ZeroMemory(key);
        Log.Write("vault: erased");
    }

    /// <summary>A vault made afresh after <see cref="Erase"/> (Reset Everything, for the flashcards it keeps): a new key.</summary>
    public static void Renew()
    {
        lock (Gate)
        {
            if (!erased) return;
            erased = false;
            generation++;
            opening = null;
        }
        Preload();
    }

    // ---- the key: opened, or made ----

    /// <summary>The key from vault.key, or a new vault; never throws (at worst, a key for this run only).</summary>
    private static byte[] OpenOrMake(int gen)
    {
        var watch = Stopwatch.StartNew();
        var file = AppPaths.VaultKey;
        if (File.Exists(file))
        {
            try
            {
                var key = Unwrap(File.ReadAllBytes(file), out var tpm);
                protection = tpm ? VaultProtection.Tpm : VaultProtection.WindowsAccount;
                Log.Write($"vault: opened in {watch.ElapsedMilliseconds} ms, its key held by {Holder(protection)}");
                if (!tpm) AddTpmSoon(key, gen);
                return key;
            }
            catch (Exception e)
            {
                var moved = SetAsideAll(out var dir);
                Log.Write($"vault: vault.key could not be opened ({e.GetType().Name}: {e.Message.Trim()}): {string.Join(", ", moved)} moved to {dir ?? "(nowhere)"}, the answers kept deleted, and a new vault made");
            }
        }
        var made = RandomNumberGenerator.GetBytes(KeySize);
        try
        {
            var outer = ProtectedData.Protect(made, Entropy.ToArray(), DataProtectionScope.CurrentUser);
            if (!WriteKeyFile(0, outer, gen)) return made;
            protection = VaultProtection.WindowsAccount;
            Log.Write($"vault: made in {watch.ElapsedMilliseconds} ms, its key held by {Holder(protection)} until the TPM is asked");
            AddTpmSoon(made, gen);
        }
        catch (Exception e)
        {
            protection = VaultProtection.None;
            Log.Write($"vault: its key could not be kept ({e.GetType().Name}: {e.Message}): what is written now lasts while Simpl runs");
        }
        return made;
    }

    private static string Holder(VaultProtection p) => p switch
    {
        VaultProtection.Tpm => "this PC's TPM and Windows (DPAPI)",
        VaultProtection.WindowsAccount => "Windows (DPAPI)",
        _ => "nothing",
    };

    /// <summary>vault.key read: the DPAPI wrap opened, then the TPM's (when it has one).</summary>
    private static byte[] Unwrap(byte[] file, out bool tpm)
    {
        if (file.Length < KeyHeader || !file.AsSpan().StartsWith(KeyMagic) || file[8] != KeyVersion) throw new InvalidDataException("not a vault key");
        var flags = file[9];
        var length = BitConverter.ToInt32(file, 12);
        if (length <= 0 || length > file.Length - KeyHeader) throw new InvalidDataException("its length is wrong");
        var inner = ProtectedData.Unprotect(file.AsSpan(KeyHeader, length).ToArray(), Entropy.ToArray(), DataProtectionScope.CurrentUser);
        tpm = (flags & TpmLayer) != 0;
        if (!tpm)
        {
            if (inner.Length == KeySize) return inner;
            CryptographicOperations.ZeroMemory(inner);
            throw new InvalidDataException("its key is the wrong size");
        }
        try { return TpmUnwrap(inner, (flags & OaepSha1) != 0 ? RSAEncryptionPadding.OaepSHA1 : RSAEncryptionPadding.OaepSHA256); }
        finally { CryptographicOperations.ZeroMemory(inner); }
    }

    /// <summary>The TPM's wrap undone. Its key gone (the TPM cleared, another PC): at once. Anything else is tried thrice — a
    /// TPM can be slow to answer just after Windows starts.</summary>
    private static byte[] TpmUnwrap(byte[] wrapped, RSAEncryptionPadding padding)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var provider = new CngProvider(PlatformProvider);
                if (!CngKey.Exists(KeyName, provider, CngKeyOpenOptions.Silent)) throw new KeyGone();
                using var cng = CngKey.Open(KeyName, provider, CngKeyOpenOptions.Silent);
                using var rsa = new RSACng(cng);
                var key = rsa.Decrypt(wrapped, padding);
                if (key.Length == KeySize) return key;
                CryptographicOperations.ZeroMemory(key);
                throw new InvalidDataException("the TPM's key unwrapped something else");
            }
            catch (Exception e) when (attempt < 3 && e is not KeyGone and not InvalidDataException)
            {
                Thread.Sleep(attempt == 1 ? 250 : 1000);
            }
        }
    }

    private sealed class KeyGone : Exception
    {
        public KeyGone() : base("the TPM's key is not on this PC") { }
    }

    /// <summary>
    /// The TPM's name for the vault's key: SimplCourses.Simpl.Vault for Simpl's own folder; a --profile's (the screenshot
    /// suite's, a developer's) its own, so erasing one never takes another's key with it.
    /// </summary>
    private static string KeyName
    {
        get
        {
            const string name = "SimplCourses.Simpl.Vault";
            if (Launch.Get("profile") == null) return name;
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(AppPaths.Root).TrimEnd('\\', '/').ToLowerInvariant()));
            return name + "." + Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
        }
    }

    /// <summary>
    /// The TPM's wrap added under the DPAPI one, on a thread of its own: an RSA key made in the chip can take seconds (and
    /// a PC without one answers at once), so a new vault is kept by Windows alone meanwhile, and the student waits on
    /// nothing. Tried again at the next start if Simpl quits first.
    /// </summary>
    private static void AddTpmSoon(byte[] key, int gen)
    {
        var copy = (byte[])key.Clone();
        new Thread(() =>
        {
            try { AddTpm(copy, gen); }
            finally { CryptographicOperations.ZeroMemory(copy); }
        }) { IsBackground = true, Name = "Simpl vault", Priority = ThreadPriority.BelowNormal }.Start();
    }

    private static void AddTpm(byte[] key, int gen)
    {
        // (one at a time: a vault made again after an erase waits for the wrap begun for the one before, which finds it
        // was erased and takes its TPM key away with it)
        lock (TpmGate)
        {
            if (gen != Generation) return;
            var watch = Stopwatch.StartNew();
            try
            {
                using var cng = MakeTpmKey();
                using var rsa = new RSACng(cng);
                var (wrapped, sha1) = WrapChecked(rsa, key);
                var outer = ProtectedData.Protect(wrapped, Entropy.ToArray(), DataProtectionScope.CurrentUser);
                if (!WriteKeyFile((byte)(TpmLayer | (sha1 ? OaepSha1 : 0)), outer, gen))
                {
                    DeleteTpmKey(); // (erased meanwhile: the key just made goes too)
                    return;
                }
                protection = VaultProtection.Tpm;
                Log.Write($"vault: its key is held by this PC's TPM now (RSA-OAEP {(sha1 ? "SHA-1" : "SHA-256")}, {watch.ElapsedMilliseconds} ms)");
            }
            catch (Exception e)
            {
                // (no TPM, as in most VMs and older PCs, or none Windows will let Simpl use)
                Log.Write($"vault: the TPM did not take its key ({e.GetType().Name}: {e.Message.Trim()}): Windows (DPAPI) holds it");
            }
        }
    }

    private static int Generation
    {
        get { lock (Gate) return erased ? -1 : generation; }
    }

    /// <summary>
    /// A new RSA-2048 key in the TPM, this user's, never exportable, for decrypting only. One already there by that name is
    /// let go of first: nothing is wrapped by it (this vault is not yet), and a key half made by a Simpl that quit midway
    /// could not be trusted. No TPM (or no Platform Crypto Provider): it throws, and Windows alone keeps the key.
    /// </summary>
    private static CngKey MakeTpmKey()
    {
        var provider = new CngProvider(PlatformProvider);
        DeleteTpmKey();
        CngKeyCreationParameters Parameters(bool usage)
        {
            var p = new CngKeyCreationParameters
            {
                Provider = provider,
                ExportPolicy = CngExportPolicies.None,
                KeyCreationOptions = CngKeyCreationOptions.None,
            };
            if (usage) p.KeyUsage = CngKeyUsages.Decryption;
            p.Parameters.Add(new CngProperty("Length", BitConverter.GetBytes(2048), CngPropertyOptions.None));
            return p;
        }
        // (a TPM that will not be told the key's use makes it with its own default)
        try { return CngKey.Create(CngAlgorithm.Rsa, KeyName, Parameters(usage: true)); }
        catch (CryptographicException) when (!CngKey.Exists(KeyName, provider, CngKeyOpenOptions.Silent))
        {
            return CngKey.Create(CngAlgorithm.Rsa, KeyName, Parameters(usage: false));
        }
    }

    /// <summary>The key wrapped with OAEP SHA-256, and unwrapped by the TPM to be sure it can be; a TPM that refuses SHA-256
    /// (an older one) is asked for SHA-1, and vault.key says which.</summary>
    private static (byte[] Wrapped, bool Sha1) WrapChecked(RSACng rsa, byte[] key)
    {
        foreach (var sha1 in new[] { false, true })
        {
            var padding = sha1 ? RSAEncryptionPadding.OaepSHA1 : RSAEncryptionPadding.OaepSHA256;
            byte[]? back = null;
            try
            {
                var wrapped = rsa.Encrypt(key, padding);
                back = rsa.Decrypt(wrapped, padding);
                if (CryptographicOperations.FixedTimeEquals(back, key)) return (wrapped, sha1);
            }
            catch (Exception e) when (!sha1 && e is CryptographicException or NotSupportedException) { }
            finally { if (back != null) CryptographicOperations.ZeroMemory(back); }
        }
        throw new CryptographicException("the TPM could not unwrap what it wrapped");
    }

    private static void DeleteTpmKey()
    {
        try
        {
            var provider = new CngProvider(PlatformProvider);
            if (!CngKey.Exists(KeyName, provider, CngKeyOpenOptions.Silent)) return;
            using var cng = CngKey.Open(KeyName, provider, CngKeyOpenOptions.Silent);
            cng.Delete();
        }
        catch (Exception e) when (e is CryptographicException or PlatformNotSupportedException) { /* (no TPM: nothing to delete) */ }
        catch (Exception e) { Log.Write("vault: the TPM's key could not be deleted: " + e.Message); }
    }

    /// <summary>vault.key written whole (beside it, then moved over it) — unless the vault it is for was erased meanwhile.</summary>
    private static bool WriteKeyFile(byte flags, byte[] outer, int gen)
    {
        var file = new byte[KeyHeader + outer.Length];
        KeyMagic.CopyTo(file);
        file[8] = KeyVersion;
        file[9] = flags;
        BitConverter.GetBytes(outer.Length).CopyTo(file, 12);
        outer.CopyTo(file, KeyHeader);
        lock (KeyFileGate)
        {
            if (gen != Generation) return false;
            var tmp = AppPaths.VaultKey + ".tmp";
            File.WriteAllBytes(tmp, file);
            File.Move(tmp, AppPaths.VaultKey, true);
            return true;
        }
    }

    /// <summary>
    /// vault.key could not be opened: it and every file it sealed moved together into one Unreadable-… folder (plain ones
    /// from before the vault stay, to be sealed by the new one), and the answers kept — only a cache — deleted. The names
    /// of what moved.
    /// </summary>
    private static List<string> SetAsideAll(out string? dir)
    {
        var moved = new List<string>();
        dir = null;
        foreach (var f in new[] { AppPaths.PrefsFile, AppPaths.Storage, AppPaths.Flashcards, AppPaths.Login, AppPaths.VaultKey })
        {
            try
            {
                if (!File.Exists(f)) continue;
                if (f != AppPaths.VaultKey)
                {
                    var head = new byte[SealHeader];
                    using (var s = File.OpenRead(f)) s.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
                    if (!IsSealed(head)) continue;
                }
                if (SetAside(f) is { } d) { dir = d; moved.Add(Path.GetFileName(f)); }
            }
            catch (Exception e) { Log.Write($"vault: {Path.GetFileName(f)}: {e.Message}"); }
        }
        try { if (Directory.Exists(AppPaths.Answers)) Directory.Delete(AppPaths.Answers, true); } catch { }
        return moved;
    }
}
