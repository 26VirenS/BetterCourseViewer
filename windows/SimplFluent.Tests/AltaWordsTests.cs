using Simpl.Screens;
using Xunit;

namespace Simpl.Tests;

/// <summary>Knewton Alta's own words for a time (Screens/Alta/AltaLook.cs): written as every screen writes one —
/// "11:59 PM", never "11:59pm", and no time zone — and its due words read as a date where they can be; (0.3.5) its pop-ups'
/// buttons in sentence case.</summary>
public class AltaWordsTests
{
    [Fact]
    public void ItsTimesAreWrittenAsEveryScreenWritesThem()
    {
        Assert.Equal("Complete by 11:59 PM today", AltaLook.Times("Complete by 11:59pm today"));
        Assert.Equal("Last updated Oct 10 at 8:08 PM", AltaLook.Times("Last updated Oct 10 at 8:08pm"));
        Assert.Equal("Opens at 12:00 AM", AltaLook.Times("Opens at 12am"));
        Assert.Equal("Monday, Oct 12 11:59 PM", AltaLook.Times("Monday, Oct 12 11:59pm PDT"));
        // (what is written already, and words that only look like a time, are left as they are)
        Assert.Equal("Due Oct 12 at 11:59 PM", AltaLook.Times("Due Oct 12 at 11:59 PM"));
        Assert.Equal("10 amounts", AltaLook.Times("10 amounts"));
    }

    [Fact]
    public void APopUpsButtonsAreInSentenceCase()
    {
        // (0.3.5: a pop-up of Alta's shown as the app's own dialog — Screens/Alta/AltaPopup.cs, AltaDialogCard)
        Assert.Equal("Got it", AltaParts.ButtonWords("GOT IT"));
        Assert.Equal("View instruction", AltaParts.ButtonWords("VIEW INSTRUCTION"));
        Assert.Equal("Next", AltaParts.ButtonWords("NEXT"));
        Assert.Equal("Not now", AltaParts.ButtonWords("Not now"));
        Assert.Equal("OK", AltaParts.ButtonWords("OK"));
        Assert.Equal("×", AltaParts.ButtonWords("×"));
    }

    [Fact]
    public void ItsDueWordsAreReadAsADate()
    {
        Assert.Equal("Due Oct 12 at 11:59 PM", AltaLook.DueOf("Monday, Oct 12 11:59pm PDT"));
        Assert.Equal("Due Oct 19 at 9:00 AM", AltaLook.DueOf("Oct 19 at 9am"));
        Assert.Null(AltaLook.DueOf("whenever you like"));
    }
}
