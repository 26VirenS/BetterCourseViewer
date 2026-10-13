using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Simpl.Support;

/// <summary>
/// The school sign-in the student chose to keep ("Stay logged in?"): the username and password, for the one sign-in
/// page they were given on (the Mac's SavedLogin).
/// </summary>
public sealed record SavedLogin(string Host, string User, string Pass, DateTime SavedAt);

/// <summary>
/// Where the saved sign-in is kept (the Mac's LoginVault, its Keychain): inside Simpl, in login.bin, sealed by the vault
/// (Vault) — so it is encrypted with a key this PC's TPM and Windows hold for this user alone, never roamed. It is held
/// in memory only as sealed, and opened each time it is asked for. The browser underneath never sees it: WebView2's own
/// password saving and autofill are off (Engine). A development run (the mock, a demo, a picture) keeps nothing past the
/// run. An earlier Simpl kept it in Windows' Credential Manager as "Simpl Courses/sign-in": found there, it is moved
/// into the vault and deleted from there (and deleted again whenever the sign-in is forgotten, should it be back).
/// </summary>
public static class LoginVault
{
    /// <summary>Where an earlier Simpl kept it: Credential Manager's generic credential of this name.</summary>
    public const string Target = "Simpl Courses/sign-in";
    private const string Purpose = "login";
    /// <summary>Raised when the saved sign-in is kept or forgotten (Settings shows it).</summary>
    public static event Action? Changed;
    private static readonly object Gate = new();
    private static SavedLogin? memory;
    /// <summary>login.bin's bytes, sealed, once read (null: none kept).</summary>
    private static byte[]? kept;
    private static bool read;

    /// <summary>login.bin read (and a sign-in an earlier Simpl kept in Credential Manager moved in) on a worker as the app
    /// starts, once the vault is open, so asking for it later reads nothing from disk.</summary>
    public static void Preload()
    {
        if (Launch.IsDevelopment) return;
        _ = Task.Run(async () =>
        {
            await Vault.Ready;
            lock (Gate) Ensure();
        });
    }

    public static SavedLogin? Load()
    {
        if (Launch.IsDevelopment) return memory;
        byte[]? plain = null;
        try
        {
            lock (Gate)
            {
                Ensure();
                if (kept == null) return null;
                plain = Vault.Open(kept, Purpose);
            }
            return plain == null ? null : JsonSerializer.Deserialize<SavedLogin>(plain, Json.Options);
        }
        catch { return null; }
        finally { if (plain != null) CryptographicOperations.ZeroMemory(plain); }
    }

    public static bool Save(SavedLogin login)
    {
        bool ok;
        if (Launch.IsDevelopment) { memory = login; ok = true; }
        else lock (Gate)
        {
            read = true;
            ok = Keep(login);
            if (ok) ForgetOld();
        }
        Changed?.Invoke();
        return ok;
    }

    public static void Clear()
    {
        memory = null;
        if (!Launch.IsDevelopment)
        {
            lock (Gate)
            {
                read = true;
                kept = null;
                try { File.Delete(AppPaths.Login); } catch (Exception e) { Log.Write("sign-in: login.bin could not be deleted: " + e.Message); }
                try { File.Delete(AppPaths.Login + ".tmp"); } catch { }
            }
            ForgetOld();
        }
        Changed?.Invoke();
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
    private static bool Keep(SavedLogin login)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(login, Json.Script);
        try
        {
            kept = Vault.Write(AppPaths.Login, plain, Purpose);
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

    private static SavedLogin? ReadOld()
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
            return JsonSerializer.Deserialize<SavedLogin>(bytes, Json.Options);
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
