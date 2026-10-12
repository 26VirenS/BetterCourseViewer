using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Simpl.Support;

/// <summary>
/// The school sign-in the student chose to keep ("Stay logged in?"): the username and password, for the one sign-in
/// page they were given on (the Mac's SavedLogin).
/// </summary>
public sealed record SavedLogin(string Host, string User, string Pass, DateTime SavedAt);

/// <summary>
/// Where the saved sign-in is kept (the Mac's LoginVault, its Keychain): Windows' own Credential Manager, which locks it
/// to this user on this PC — encrypted with the account's own secret, never roamed — where it can be seen and removed
/// under Control Panel ▸ Credential Manager ▸ Windows Credentials as "Simpl Courses/sign-in". The browser underneath never
/// sees it: WebView2's own password saving and autofill are off (Engine). A development run (the mock, a demo, a
/// picture) keeps nothing past the run.
/// </summary>
public static class LoginVault
{
    public const string Target = "Simpl Courses/sign-in";
    /// <summary>Raised when the saved sign-in is kept or forgotten (Settings shows it).</summary>
    public static event Action? Changed;
    private static SavedLogin? memory;

    public static SavedLogin? Load()
    {
        if (Launch.IsDevelopment) return memory;
        var ptr = IntPtr.Zero;
        try
        {
            if (!CredReadW(Target, CRED_TYPE_GENERIC, 0, out ptr) || ptr == IntPtr.Zero) return null;
            var c = Marshal.PtrToStructure<CREDENTIAL>(ptr);
            if (c.CredentialBlob == IntPtr.Zero || c.CredentialBlobSize == 0) return null;
            var bytes = new byte[c.CredentialBlobSize];
            Marshal.Copy(c.CredentialBlob, bytes, 0, bytes.Length);
            return JsonSerializer.Deserialize<SavedLogin>(Encoding.UTF8.GetString(bytes), Json.Options);
        }
        catch { return null; }
        finally { if (ptr != IntPtr.Zero) CredFree(ptr); }
    }

    public static bool Save(SavedLogin login)
    {
        bool ok;
        if (Launch.IsDevelopment) { memory = login; ok = true; }
        else
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(login, Json.Script));
            var blob = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, blob, bytes.Length);
                var c = new CREDENTIAL
                {
                    Type = CRED_TYPE_GENERIC,
                    TargetName = Target,
                    Comment = "Simpl's sign-in at " + login.Host,
                    CredentialBlobSize = (uint)bytes.Length,
                    CredentialBlob = blob,
                    Persist = CRED_PERSIST_LOCAL_MACHINE,
                    UserName = login.User,
                };
                ok = CredWriteW(ref c, 0);
                if (!ok) Log.Write($"vault: the credential could not be written ({Marshal.GetLastWin32Error()})");
            }
            catch (Exception e) { Log.Write("vault: " + e.Message); ok = false; }
            finally { Marshal.FreeHGlobal(blob); }
        }
        Changed?.Invoke();
        return ok;
    }

    public static void Clear()
    {
        memory = null;
        if (!Launch.IsDevelopment) { try { CredDeleteW(Target, CRED_TYPE_GENERIC, 0); } catch { } }
        Changed?.Invoke();
    }

    private const uint CRED_TYPE_GENERIC = 1, CRED_PERSIST_LOCAL_MACHINE = 2;

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

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CredWriteW(ref CREDENTIAL credential, uint flags);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CredReadW(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CredDeleteW(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr buffer);
}
