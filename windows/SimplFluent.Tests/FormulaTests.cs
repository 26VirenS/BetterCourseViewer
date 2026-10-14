using Simpl.Kit;
using Simpl.Screens;
using Xunit;

namespace Simpl.Tests;

/// <summary>Canvas's formulas (Kit/Formula.cs): found in its equation pictures, and — where they cannot be typeset —
/// written out as plain maths, never as TeX; a quiz's blanks marked in its question's words (QuizUi.BlankMarks); and the
/// page's Title Case words said in Windows' sentence case (QuizUi.Sentence).</summary>
public class FormulaTests
{
    [Fact]
    public void TheTexOfCanvasEquationPicturesIsFound()
    {
        Assert.Equal(@"\vec{v}", Formula.TexOf(new Dictionary<string, string> { ["class"] = "equation_image", ["data-equation-content"] = @"\vec{v}", ["src"] = "/equation_images/x" }));
        Assert.Equal(@"x^2", Formula.TexOf(new Dictionary<string, string> { ["class"] = "equation_image", ["alt"] = "LaTeX: x^2" }));
        // (only the picture's address left: the TeX in it, encoded twice)
        Assert.Equal(@"\vec{v}", Formula.TexOf(new Dictionary<string, string> { ["src"] = "https://school.edu/equation_images/%255Cvec%257Bv%257D?scale=1" }));
        Assert.Null(Formula.TexOf(new Dictionary<string, string> { ["src"] = "https://school.edu/files/12/preview", ["alt"] = "A graph" }));
    }

    [Fact]
    public void TexIsWrittenOutAsMaths()
    {
        Assert.Equal("a⃗ = dv⃗/dt", Formula.Readable(@"\vec{a}=\frac{d\vec{v}}{dt}"));
        Assert.Equal("v(5) = dx/dt = 0", Formula.Readable(@"v(5)=\frac{dx}{dt}=0"));
        Assert.Equal("x²+y² = r²", Formula.Readable(@"x^2+y^2=r^2"));
        Assert.Equal("Δx/Δt", Formula.Readable(@"\frac{\Delta x}{\Delta t}"));
        Assert.Equal("(a+b)/2", Formula.Readable(@"\frac{a+b}{2}"));
        Assert.Equal("√(2gh)", Formula.Readable(@"\sqrt{2gh}"));
        Assert.Equal("F = m · a", Formula.Readable(@"F = m \cdot a"));
        Assert.Equal("90°", Formula.Readable(@"90^\circ"));
        Assert.Equal("v₀ + at", Formula.Readable(@"v_0 + at"));
        Assert.Equal("x^(n+1/2)", Formula.Readable(@"x^{n+1/2}"));
    }

    [Fact]
    public void ABlanksMarkCarriesItsNumberOrItsPick()
    {
        const string html = "<p>Velocity is the <span style=\"display:inline-block;min-width:1.5em;padding:0 .35em\">1</span> of <span style=\"display:inline-block;min-width:1.5em;padding:0 .35em\">2</span>.</p>";
        Assert.Equal("<p>Velocity is the <span data-blank=\"1\">1</span> of <span data-blank=\"2\">2</span>.</p>", QuizUi.BlankMarks(html));
        Assert.Equal("<p>Velocity is the <span data-blank=\"1\" data-picked=\"1\">rate &amp; change</span> of <span data-blank=\"2\">2</span>.</p>",
            QuizUi.BlankMarks(html, new Dictionary<int, string> { [1] = "rate & change" }));
        Assert.Equal("<p>No blanks</p>", QuizUi.BlankMarks("<p>No blanks</p>"));
    }

    [Fact]
    public void ThePagesTitleCaseWordsAreSaidInSentenceCase()
    {
        Assert.Equal("Begin attempt", QuizUi.Sentence("Begin Attempt"));
        Assert.Equal("Continue survey", QuizUi.Sentence("Continue Survey"));
        Assert.Equal("No attempts left", QuizUi.Sentence("No Attempts Left"));
        Assert.Equal("Partly right", QuizUi.Sentence("Partly Right"));
        Assert.Equal("Needs LockDown Browser", QuizUi.Sentence("Needs LockDown Browser"));
        Assert.Equal("Open in Canvas", QuizUi.Sentence("Open In Canvas"));
        Assert.Equal("Locked", QuizUi.Sentence("Locked"));
        Assert.Equal("", QuizUi.Sentence(null));
    }
}
