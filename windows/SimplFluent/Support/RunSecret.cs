using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Simpl.Support;

/// <summary>
/// A secret Simpl holds in memory for the rest of a run — the saved sign-in's password once Windows Hello has given it
/// (UnlockGate) — never kept as a plain string. Its UTF-8 bytes are written straight into an array the garbage collector
/// never moves (so no stray copies are left behind) and encrypted there at once by Windows for this process alone
/// (CryptProtectMemory, SAME_PROCESS: under a key Windows keeps for this boot, not in Simpl's memory), so a dump of
/// Simpl's memory or the page file does not show it. Opened for the moment it is used, its bytes zeroed when let go.
/// Where Windows will not protect it (not Windows: the tests also run elsewhere), it is sealed instead with AES-256-GCM
/// under a random key made for this run and never written anywhere.
/// </summary>
public sealed class RunSecret : IDisposable
{
    /// <summary>CryptProtectMemory's block (CRYPTPROTECTMEMORY_BLOCK_SIZE): what it encrypts is a whole number of them.</summary>
    private const int Block = 16;
    /// <summary>CRYPTPROTECTMEMORY_SAME_PROCESS: only this process can decrypt it.</summary>
    private const uint SameProcess = 0;
    private const int NonceSize = 12, TagSize = 16;

    /// <summary>The run's own key, for a secret Windows would not protect: made on first use, never written anywhere.</summary>
    private static readonly Lazy<byte[]> RunKey = new(() =>
    {
        var key = GC.AllocateArray<byte>(32, pinned: true);
        RandomNumberGenerator.Fill(key);
        return key;
    });

    private readonly object sync = new();
    private readonly bool byWindows;
    /// <summary>What is held, encrypted: its length and its UTF-8 bytes, padded to a block (null once let go).</summary>
    private byte[]? data;

    private RunSecret(byte[] data, bool byWindows)
    {
        this.data = data;
        this.byWindows = byWindows;
    }

    /// <summary><paramref name="text"/> held: its bytes encrypted as soon as they are written.</summary>
    public static RunSecret Hold(string text)
    {
        var count = Encoding.UTF8.GetByteCount(text);
        byte[]? plain = GC.AllocateArray<byte>((4 + count + Block - 1) / Block * Block, pinned: true);
        try
        {
            BinaryPrimitives.WriteInt32LittleEndian(plain, count);
            Encoding.UTF8.GetBytes(text, plain.AsSpan(4));
            if (Protect(plain, true))
            {
                var held = plain;
                plain = null; // (encrypted where it lies: that array is what is held)
                return new RunSecret(held, true);
            }
            var box = GC.AllocateArray<byte>(NonceSize + TagSize + plain.Length, pinned: true);
            RandomNumberGenerator.Fill(box.AsSpan(0, NonceSize));
            using var gcm = new AesGcm(RunKey.Value, TagSize);
            gcm.Encrypt(box.AsSpan(0, NonceSize), plain, box.AsSpan(NonceSize + TagSize), box.AsSpan(NonceSize, TagSize));
            return new RunSecret(box, false);
        }
        finally { if (plain != null) CryptographicOperations.ZeroMemory(plain); }
    }

    /// <summary>The secret, for the moment it is used; null once let go (or should it not open). Never throws.</summary>
    public string? Open()
    {
        lock (sync)
        {
            if (data == null) return null;
            var plain = GC.AllocateArray<byte>(byWindows ? data.Length : data.Length - NonceSize - TagSize, pinned: true);
            try
            {
                if (byWindows)
                {
                    data.CopyTo(plain, 0);
                    if (!Protect(plain, false)) return null;
                }
                else
                {
                    using var gcm = new AesGcm(RunKey.Value, TagSize);
                    gcm.Decrypt(data.AsSpan(0, NonceSize), data.AsSpan(NonceSize + TagSize), data.AsSpan(NonceSize, TagSize), plain);
                }
                var count = BinaryPrimitives.ReadInt32LittleEndian(plain);
                return count >= 0 && count <= plain.Length - 4 ? Encoding.UTF8.GetString(plain, 4, count) : null;
            }
            catch (CryptographicException) { return null; }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
    }

    /// <summary>Let go: what was held zeroed, so it can never be opened again.</summary>
    public void Dispose()
    {
        lock (sync)
        {
            if (data != null) CryptographicOperations.ZeroMemory(data);
            data = null;
        }
    }

    /// <summary>What is held, as it lies in memory (the tests look for the secret in it).</summary>
    internal byte[]? InMemory()
    {
        lock (sync) return data == null ? null : (byte[])data.Clone();
    }

    /// <summary>Encrypted (or decrypted) in place by Windows for this process; false where Windows will not.</summary>
    private static bool Protect(byte[] block, bool protect)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try { return protect ? CryptProtectMemory(block, (uint)block.Length, SameProcess) : CryptUnprotectMemory(block, (uint)block.Length, SameProcess); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectMemory([In, Out] byte[] data, uint size, uint flags);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectMemory([In, Out] byte[] data, uint size, uint flags);
}
