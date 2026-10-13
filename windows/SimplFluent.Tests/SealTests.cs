using System.IO;
using System.Security.Cryptography;
using System.Text;
using Simpl.Support;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// What keeps Simpl's files and the saved sign-in closed (Support/Vault.cs, Support/LoginRecord.cs): sealed and opened
/// again, never opened for another purpose, with another key, or once changed. The vault here is the tests' own
/// (Profile): its key held as this PC holds one, the TPM's wrap when there is a TPM, DPAPI's when not.
/// </summary>
public class SealTests
{
    public SealTests() => _ = Profile.Folder;

    [Fact]
    public void TheVaultOpensWhatItSealed()
    {
        var sealedBytes = Vault.SealText("{\"theme\":\"Dusk\"}", "prefs");
        Assert.True(Vault.IsSealed(sealedBytes));
        Assert.Equal("SIMPLENC", Encoding.ASCII.GetString(sealedBytes, 0, 8));
        Assert.DoesNotContain("Dusk", Encoding.UTF8.GetString(sealedBytes));
        Assert.Equal("{\"theme\":\"Dusk\"}", Vault.OpenText(sealedBytes, "prefs"));
    }

    [Fact]
    public void NeverForAnotherPurposeNorOnceChanged()
    {
        var sealedBytes = Vault.SealText("kept", "storage");
        Assert.Null(Vault.OpenText(sealedBytes, "prefs"));
        var changed = (byte[])sealedBytes.Clone();
        changed[^1] ^= 0x01;
        Assert.Null(Vault.OpenText(changed, "storage"));
        Assert.Null(Vault.Open(Encoding.UTF8.GetBytes("plain text from before the vault"), "storage"));
        // (the same text sealed twice is two different seals: a new nonce each time)
        Assert.NotEqual(sealedBytes, Vault.SealText("kept", "storage"));
    }

    [Fact]
    public void AFileWrittenSealedIsReadBack()
    {
        var path = Path.Combine(Profile.Folder, "test-" + Guid.NewGuid().ToString("N") + ".json");
        Vault.Write(path, "[1,2,3]", "flashcards");
        Assert.True(Vault.IsSealed(File.ReadAllBytes(path)));
        var read = Vault.Read(path, "flashcards");
        Assert.Equal(VaultState.Sealed, read.State);
        Assert.Equal("[1,2,3]", read.Text);
        File.WriteAllText(path, "[4,5]"); // (an earlier Simpl's plain file: read as it is, for the caller to seal)
        Assert.Equal(VaultState.Legacy, Vault.Read(path, "flashcards").State);
        Assert.Equal(VaultState.Missing, Vault.Read(path + ".gone", "flashcards").State);
    }

    [Fact]
    public void NamesSayNothingOfWhatTheyName()
    {
        var a = Vault.FileName("answers:canvas.example.edu");
        Assert.Matches("^[0-9a-f]{32}$", a);
        Assert.Equal(a, Vault.FileName("answers:canvas.example.edu"));
        Assert.NotEqual(a, Vault.FileName("answers:other.example.edu"));
    }

    private static readonly DateTime When = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ASignInKeptInTheVaultRoundTrips()
    {
        var r = LoginRecord.InVault("canvas.example.edu", "jdoe", "correct horse", When);
        var back = LoginRecord.Parse(r.ToJson());
        Assert.NotNull(back);
        Assert.Equal(("canvas.example.edu", "jdoe", "correct horse"), (back!.Host, back.User, back.Pass));
        Assert.False(back.LockedWithHello);
        Assert.True(back.LockAtUse(helloSupported: true));
        Assert.False(back.LockAtUse(helloSupported: false));
        Assert.False(back.NotLocked(LoginRecord.Declined).LockAtUse(helloSupported: true));
    }

    [Fact]
    public void ASignInLockedWithWindowsHelloOpensOnlyWithItsKey()
    {
        var challenge = RandomNumberGenerator.GetBytes(LoginRecord.ChallengeSize);
        var key = LoginRecord.KeyFrom(Encoding.UTF8.GetBytes("a signature of the challenge"));
        var r = LoginRecord.WithHello("canvas.example.edu", "jdoe", "correct horse", When, challenge, key);
        Assert.True(r.LockedWithHello);
        Assert.Null(r.Pass);
        var json = r.ToJson();
        Assert.DoesNotContain("correct horse", Encoding.UTF8.GetString(json));
        var back = LoginRecord.Parse(json)!;
        Assert.Equal(challenge, back.ChallengeBytes);
        Assert.Equal("correct horse", back.OpenPass(key));
        Assert.Null(back.OpenPass(LoginRecord.KeyFrom(Encoding.UTF8.GetBytes("another signature"))));
        // (bound to its host and username: moved to another, it no longer opens)
        back.User = "someone-else";
        Assert.Null(back.OpenPass(key));
    }

    [Fact]
    public void OnlyAWholeRecordIsRead()
    {
        Assert.Null(LoginRecord.Parse("not json"u8));
        Assert.Null(LoginRecord.Parse("{\"host\":\"canvas.example.edu\",\"user\":\"jdoe\"}"u8)); // (no password, not locked)
        Assert.Null(LoginRecord.Parse("{\"host\":\"\",\"user\":\"jdoe\",\"pass\":\"x\"}"u8));
        Assert.Null(LoginRecord.Parse("{\"host\":\"h\",\"user\":\"u\",\"lock\":\"hello\",\"challenge\":\"AAAA\",\"sealed\":\"AAAA\"}"u8));
        Assert.True(LoginRecord.SameSignature(new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 3 }));
        Assert.False(LoginRecord.SameSignature(new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 4 }));
        Assert.False(LoginRecord.SameSignature(Array.Empty<byte>(), Array.Empty<byte>()));
    }

    [Theory]
    [InlineData(HelloResult.Success, null, UnlockStep.Open)]
    [InlineData(HelloResult.Canceled, LoginRecord.Declined, UnlockStep.Keep)]
    [InlineData(HelloResult.Gone, LoginRecord.Unusable, UnlockStep.Forget)]
    public void WhatWindowsHellosAnswersLeadTo(HelloResult result, string? notLocked, UnlockStep after)
    {
        Assert.Equal(notLocked, LoginRecord.NotLockedBecause(result));
        Assert.Equal(after, LoginRecord.AfterUnlock(result));
    }
}
