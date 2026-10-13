using Simpl.Support;
using Xunit;

namespace Simpl.Tests;

/// <summary>JSON as the page speaks it and as JavaScript source (Support/Json.cs): what the bridge reads and writes.</summary>
public class JsonTests
{
    public JsonTests() => _ = Profile.Folder;

    private sealed record Course(int Id, string? Name, double? Score, List<string>? Tags);

    [Fact]
    public void ThePagesAnswersAreReadForgivingly()
    {
        // (names in any case, numbers given as strings, comments and a trailing comma, a field missing)
        var c = Json.Read<Course>("{\"ID\": \"101\", \"name\": \"Physics\", /* the page's */ \"score\": \"92.5\",}");
        Assert.NotNull(c);
        Assert.Equal(101, c!.Id);
        Assert.Equal("Physics", c.Name);
        Assert.Equal(92.5, c.Score);
        Assert.Null(c.Tags);
    }

    [Fact]
    public void WrittenCamelCasedWithoutTheNulls()
    {
        Assert.Equal("{\"id\":7,\"name\":\"Lab <1> & \\\"2\\\"\",\"tags\":[\"a\"]}", Json.Write(new Course(7, "Lab <1> & \"2\"", null, new() { "a" })));
        var back = Json.Read<Course>(Json.Write(new Course(7, "Ünïcödé — ok", 1.5, null)));
        Assert.Equal(new Course(7, "Ünïcödé — ok", 1.5, null), back);
    }

    [Fact]
    public void ALiteralIsValidJavaScriptToo()
    {
        // (the line and paragraph separators end a line in JavaScript source before ES2019: escaped, so a page's text
        //  never breaks the script it is put in)
        char ls = (char)0x2028, ps = (char)0x2029;
        const string slash = "\\";
        Assert.Equal("\"a" + slash + "u2028b" + slash + "u2029c\"", Json.Str($"a{ls}b{ps}c"));
        Assert.Equal("\"</script>\"", Json.Str("</script>"));
        Assert.Equal("null", Json.Literal(null));
    }
}
