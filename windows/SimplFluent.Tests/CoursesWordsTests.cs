using System.Windows.Media;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Screens;
using Simpl.Shell;
using Xunit;

namespace Simpl.Tests;

/// <summary>What the course screens work out themselves (the UI pass's courses findings): the words on a course's
/// colour, a course named once, the line under a title without what the breadcrumb says, a file's kind, and the chips a
/// list of work leaves out.</summary>
public class CoursesWordsTests
{
    public CoursesWordsTests() => _ = Profile.Folder;

    [Theory]
    // (the mock Canvas's course colours — white reads 2.2:1 on MATH's green, 2.6:1 on PHYS's teal, 2.2:1 on SPRK's
    //  orange, 1.7:1 on BIO's sky blue: near-black there; 3.65:1 on PHYS HL's pink and CSE's blue, 5.4:1 on ASRS's deep
    //  green: white — WCAG's 3:1 for large text)
    [InlineData("#34c759", false)]
    [InlineData("#30b0c7", false)]
    [InlineData("#ff9500", false)]
    [InlineData("#64d2ff", false)]
    [InlineData("#c8901c", false)]
    [InlineData("#ff2d55", true)]
    [InlineData("#0a84ff", true)]
    [InlineData("#1e7a37", true)]
    [InlineData("#5856d6", true)]
    [InlineData("#000000", true)]
    [InlineData("#ffffff", false)]
    public void WhiteOnlyWhereItReads(string hex, bool white)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        Assert.Equal(white, K.WhiteReads(c));
        Assert.Equal(white ? Colors.White : Color.FromRgb(0x1c, 0x1c, 0x1e), K.InkOn(c));
    }

    [Fact]
    public void LuminanceIsWcags()
    {
        Assert.Equal(0, K.Luminance(Colors.Black), 6);
        Assert.Equal(1, K.Luminance(Colors.White), 6);
        Assert.Equal(0.2126, K.Luminance(Color.FromRgb(255, 0, 0)), 4);
    }

    [Theory]
    // (the engine's saysMore: the same name once case, spaces, dashes, dots and a term prefix are set aside, or a part
    //  of one shown, says nothing new)
    //  (the lines shown, "|" apart)
    [InlineData("MATH-021-20", false, "F26-MATH 021 20")]
    [InlineData("PHYS-008HL", false, "F26-PHYS 008HL 01/PHYS 008L 01")]
    [InlineData("SP26 BIO 2", false, "bio-2")]
    [InlineData("ASRS", true, "Academic Success Resource Site (2026-27)")]
    [InlineData("Calculus of a Single Variable", true, "F26-MATH 021 20|Fall 2026")]
    [InlineData("Fall 2026", false, "F26-MATH 021 20|Fall 2026")]
    [InlineData("", false, "F26-MATH 021 20")]
    [InlineData(null, false, "F26-MATH 021 20")]
    public void ACourseIsNamedOnce(string? name, bool more, string shown) =>
        Assert.Equal(more, CourseKit.SaysMore(name, shown.Split('|')));

    [Theory]
    // (the breadcrumb's parts before the title, "|" apart)
    [InlineData("Discussion · F26-MATH 021 20", "Discussion", "F26-MATH 021 20")]
    [InlineData("F26-MATH 021 20 · Edited Sunday by Yue Lei", "Edited Sunday by Yue Lei", "F26-MATH 021 20")]
    [InlineData("Discussion · F26-MATH 021 20", "Discussion · F26-MATH 021 20", "Dashboard")]
    [InlineData("F26-MATH 021 20", null, "F26-MATH 021 20")]
    [InlineData("F26-MATH 021 20", "F26-MATH 021 20", "")]
    public void TheLineUnderATitleSaysWhatTheBreadcrumbDoesNot(string subtitle, string? left, string crumbs) =>
        Assert.Equal(left, AppShell.WithoutCrumbs(subtitle, crumbs.Split('|')));

    [Theory]
    // (the engine's typeText first; an answer from before works out the same vocabulary)
    [InlineData("Lecture 3 whiteboard.png", "image", "Image", "Image")]
    [InlineData("Lecture 3 whiteboard.png", "", null, "Image")]
    [InlineData("reading-list.txt", "text", null, "Text")]
    [InlineData("math21-F26 planned lecture schedule.xlsx", "", null, "Spreadsheet")]
    [InlineData("Course Syllabus.pdf", "pdf", null, "PDF")]
    [InlineData("Week 2 slides.pptx", "", null, "Slides")]
    [InlineData("notes", "", null, "File")]
    [InlineData("anything.bin", "", "Archive", "Archive")]
    public void AFileSaysWhatItIsInOneVocabulary(string name, string kind, string? typeText, string said) =>
        Assert.Equal(said, CourseFiles.Kind(name, kind, typeText));

    [Fact]
    public void PlainWorkStillToDoWearsNoChip()
    {
        Assert.True(CourseKit.IsPlainToDo(new Status { Word = "Not submitted", Kind = "muted" }));
        Assert.False(CourseKit.IsPlainToDo(new Status { Word = "Missing", Kind = "bad" }));
        Assert.False(CourseKit.IsPlainToDo(new Status { Word = "Submitted", Kind = "good" }));
        Assert.False(CourseKit.IsPlainToDo(new Status { Word = "19/19", Kind = "good" }));
    }
}
