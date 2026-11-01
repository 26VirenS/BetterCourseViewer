using System.IO;
using System.Text.RegularExpressions;
using Simpl.Screens;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// The app's words in Windows 11's sentence case (SentenceCase.cs): every tool's name and every section of the Tools
/// page ("Word counter", "Matrix calculator", "Periodic table"), and — read from the sources — every button, link, menu
/// item, dialog title, settings row and section heading the app writes ("Find appointment…", "Make my Simpl calendar",
/// "Exit Silent mode"). A tool keeps its name's capital where a label names it ("Open in Molecule builder").
/// </summary>
public class UiWordsTests
{
    public UiWordsTests() => _ = Profile.Folder;

    [Fact]
    public void TheToolsPageIsInSentenceCase()
    {
        var words = ToolsScreen.Kinds.Select(k => k.Name).Concat(ToolsScreen.Groups.Select(g => g.Title));
        var bad = words.Select(w => (w, Off: SentenceCase.Offenders(w))).Where(x => x.Off.Count > 0).Select(x => $"“{x.w}”: {string.Join(", ", x.Off)}").ToList();
        Assert.True(bad.Count == 0, "Title Case on the Tools page: " + string.Join("; ", bad));
    }

    /// <summary>Where a label is written in the sources: a button, a link, a menu item, a sheet's title or button, a row
    /// of Settings, a heading, an icon button's tooltip.</summary>
    private static readonly Regex[] Labels =
    {
        new(@"K\.Button\(\$?""([^""]+)"""), new(@"K\.Link\(\$?""([^""]+)"""), new(@"\bItem\(\$?""([^""]+)"""),
        new(@"Header = \$?""([^""]+)"""), new(@"\bTitle = \$?""([^""]+)"""), new(@"\.Add\(\$?""([^""]+)"", SheetButtonKind"),
        new(@"\(\$?""([^""]+)"", SymbolRegular\."), new(@"K\.Row\((?:SymbolRegular\.\w+|null), \$?""([^""]+)"""),
        new(@"ChemKit\.Wide\(\$?""([^""]+)"""), new(@"BioUI\.OpenButton\(\$?""([^""]+)"""), new(@"SectionHeader\(\$?""([^""]+)"""),
        new(@"K\.IconButton\([^,]+, [^,]+, \$?""([^""]+)"""),
    };

    /// <summary>The popups being reworked apart (their headers, footers and the update's prompt): read by their own change.</summary>
    private static readonly string[] Apart =
    {
        "Shell/FloatPopup.cs", "Kit/Sheets.cs", "Screens/Alta/", "Screens/Look/", "Screens/Quiz/QuizPopup.cs", "Screens/Assignment/RubricPopup.cs", "Shell/WebWindow.cs",
    };

    /// <summary>Labels that keep capitals for a reason of their own.</summary>
    private static readonly Dictionary<string, string> Kept = new()
    {
        ["Simpl Needs to Update"] = "the update's prompt, reworked with the popups",
        ["Update Now"] = "the update's prompt, reworked with the popups",
        ["Save Page Code"] = "Knewton Alta's popup's own button, as it is labelled there",
    };

    /// <summary>Words that keep their capitals in a label: a key, a mode, a page or a button it names, a Windows app, a
    /// person a law is named after.</summary>
    private static readonly HashSet<string> LabelNames = new(StringComparer.Ordinal)
    {
        "Ctrl", "Shift", "Alt", "Enter", "Esc", "Space", "Tab", "Delete",
        "Silent", "Sync", "Calendars", "Cross", "Know", "Edit", "Save", "Start", "Submit", "Allow", "Download", "Stay", "General", "Data",
        "File", "Task", "Manager", "Welch", "Beer", "Lambert",
    };

    [Fact]
    public void EveryLabelIsInSentenceCase()
    {
        var root = Sources();
        // (a tool's name keeps its first capital wherever a label names it: "Open in Molecule builder")
        var names = new HashSet<string>(SentenceCase.Names.Concat(LabelNames).Concat(ToolsScreen.Kinds.Select(k => k.Name.Split(' ')[0])), StringComparer.Ordinal);
        var bad = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (rel.StartsWith("bin/") || rel.StartsWith("obj/") || Apart.Any(rel.StartsWith)) continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("//")) continue;
                foreach (var re in Labels)
                    foreach (Match m in re.Matches(lines[i]))
                    {
                        var label = m.Groups[1].Value;
                        if (Kept.ContainsKey(label)) continue;
                        // (what is filled in is not the label's own words: "{name}" read as "x"; a fill with words of its
                        //  own inside, cut where it starts)
                        var words = Regex.Replace(label, @"\{[^{}""]*\}", "x");
                        if (words.IndexOf('{') is >= 0 and var cut) words = words[..cut];
                        var off = Offenders(words, names);
                        if (off.Count > 0) bad.Add($"{rel}:{i + 1} “{label}”: {string.Join(", ", off)}");
                    }
            }
        }
        Assert.True(bad.Count == 0, "Labels in Title Case:\n" + string.Join("\n", bad));
    }

    /// <summary>SentenceCase.Offenders with these names, and no word of one or two letters (a symbol, a variable, a
    /// genotype: "3D", "Aa").</summary>
    private static List<string> Offenders(string text, HashSet<string> names)
    {
        var off = SentenceCase.Offenders(text).Where(w => w.Length > 2).ToList();
        return off.Where(w => !names.Contains(w.TrimEnd('\'', '’').Replace("’s", "").Replace("'s", ""))).ToList();
    }

    /// <summary>The app's sources (windows/SimplFluent), found up from where the tests run.</summary>
    private static string Sources()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var app = Path.Combine(dir.FullName, "SimplFluent");
            if (File.Exists(Path.Combine(app, "SimplFluent.csproj"))) return app;
            var nested = Path.Combine(dir.FullName, "windows", "SimplFluent");
            if (File.Exists(Path.Combine(nested, "SimplFluent.csproj"))) return nested;
        }
        throw new DirectoryNotFoundException("windows/SimplFluent was not found above " + AppContext.BaseDirectory);
    }
}
