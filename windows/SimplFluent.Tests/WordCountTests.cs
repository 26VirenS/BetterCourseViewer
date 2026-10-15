using Simpl.Tools;
using Xunit;

namespace Simpl.Tests;

/// <summary>The Word Counter's counting (Tools/Everyday/Model/WordCount.cs): words, characters, sentences, paragraphs,
/// the times, the longest word and the words used most — the same sums as the Mac's.</summary>
public class WordCountTests
{
    [Fact]
    public void TheSpecsSentenceCounts()
    {
        // (the shared spec's vector: 5 words, 28 characters, 2 sentences; without its four spaces, 24 characters)
        var s = WordCount.Count("Hello, world! This is Simpl.");
        Assert.Equal(5, s.Words);
        Assert.Equal(28, s.Characters);
        Assert.Equal(24, s.CharactersNoSpaces);
        Assert.Equal(2, s.Sentences);
        Assert.Equal(1, s.Paragraphs);
        Assert.Equal("Hello", s.LongestWord);
        Assert.Equal(4.2, s.AverageWordLength, 6);
    }

    [Fact]
    public void NothingCountsAsNothing()
    {
        Assert.Same(WordStats.Empty, WordCount.Count(""));
        Assert.Same(WordStats.Empty, WordCount.Count(null));
        var blank = WordCount.Count("   \n\n  ");
        Assert.Equal(0, blank.Words);
        Assert.Equal(0, blank.Sentences);
        Assert.Equal(0, blank.Paragraphs);
    }

    [Fact]
    public void PunctuationAloneIsNoWord()
    {
        var s = WordCount.Count("one - two — three ... (four)");
        Assert.Equal(4, s.Words);
        Assert.Equal("three", s.LongestWord);
    }

    [Theory]
    [InlineData("A decimal like 3.14 does not end a sentence. Nor does a version like 2.0 here.", 2)]
    [InlineData("Really?! Yes. \"Quoted.\" Then nothing", 4)]
    [InlineData("No mark at the end", 1)]
    [InlineData("Dots... and more... and the end.", 3)]
    public void SentencesEndAtAMarkBeforeASpace(string text, int sentences) => Assert.Equal(sentences, WordCount.Sentences(text));

    [Fact]
    public void ParagraphsAreBlocksBetweenBlankLines()
    {
        Assert.Equal(2, WordCount.Paragraphs("First.\n\nSecond, two lines\nlong."));
        Assert.Equal(2, WordCount.Paragraphs("First.\r\n\r\n   \r\nSecond."));
        Assert.Equal(1, WordCount.Paragraphs("One\nline after another\nwith no gap"));
    }

    [Fact]
    public void LineBreaksCountOnceHoweverWindowsWritesThem()
    {
        Assert.Equal(WordCount.Count("a\nb").Characters, WordCount.Count("a\r\nb").Characters);
        Assert.Equal(3, WordCount.Count("a\r\nb").Characters);
        Assert.Equal(2, WordCount.Count("a\r\nb").CharactersNoSpaces);
    }

    [Fact]
    public void TheTimesAreAtTheUsualPaces()
    {
        var s = WordCount.Count(string.Join(' ', Enumerable.Repeat("word", 476)));
        Assert.Equal(TimeSpan.FromSeconds(120), s.ReadingTime);   // 476 words at 238 a minute
        Assert.Equal(TimeSpan.FromSeconds(190), s.SpeakingTime);  // at 150 a minute: 190.4 s
        Assert.Equal("2 min", WordCount.Clock(s.ReadingTime));
        Assert.Equal("3 min 10 s", WordCount.Clock(s.SpeakingTime));
        Assert.Equal("12 s", WordCount.Clock(TimeSpan.FromSeconds(12)));
        Assert.Equal("1 h 2 min", WordCount.Clock(TimeSpan.FromMinutes(62)));
    }

    [Fact]
    public void TheMostUsedWordsLeaveTheStopWordsOut()
    {
        var s = WordCount.Count("The cell makes ATP in the cell, and the cell divides. ATP powers the cell!");
        Assert.Equal(("cell", 4), s.TopWords[0]);
        Assert.Equal(("atp", 2), s.TopWords[1]);
        Assert.DoesNotContain(s.TopWords, t => t.Word is "the" or "and" or "in");
        // (ties in alphabetical order, at most ten)
        var tied = WordCount.Count("zebra apple mango");
        Assert.Equal(new[] { "apple", "mango", "zebra" }, tied.TopWords.Select(t => t.Word));
        Assert.True(WordCount.Count(string.Join(' ', Enumerable.Range(0, 30).Select(i => $"w{i}"))).TopWords.Count <= 10);
    }

    [Theory]
    [InlineData(380, 500, "120 to go")]
    [InlineData(515, 500, "15 over")]
    [InlineData(500, 500, "On target")]
    [InlineData(10, 0, "")]
    public void TheTargetSaysWhatIsLeft(int words, int target, string said) => Assert.Equal(said, WordCount.ToTarget(words, target));

    [Fact]
    public void TheReportIsPlainText()
    {
        var r = WordCount.Report(WordCount.Count("Hello, world! This is Simpl."));
        Assert.StartsWith("Words: 5\nCharacters: 28 (24 without spaces)\nSentences: 2\nParagraphs: 1\n", r);
        Assert.EndsWith("Longest word: Hello", r);
    }
}
