using System.IO;
using Simpl.Support;

namespace Simpl.Tests;

/// <summary>
/// The tests' own profile: Simpl's folder (AppPaths.Root: the vault's key, the log) a new temporary one, as --profile
/// gives the screenshot suite — never this PC's own %LOCALAPPDATA%\Simpl. Each test that reaches Simpl's files asks for
/// it first (in its class's constructor), before anything has read where the folder is.
/// </summary>
internal static class Profile
{
    private static readonly Lazy<string> Made = new(() =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "simpl-tests-" + Guid.NewGuid().ToString("N"));
        Launch.Parse(new[] { "--profile", folder });
        return folder;
    });

    public static string Folder => Made.Value;
}
