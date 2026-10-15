using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfSharp.Pdf.IO;
using Simpl.Screens;
using Simpl.Shell;
using Simpl.Tools;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// Compress files' sums (Tools/Files/Shrink.cs): a heavy photo comes out at least a third smaller at Balanced and still
/// reads; a PNG with see-through parts keeps them (but at Smallest); Fit under lands under its limit or says the
/// smallest it could reach; a PDF's pictures are written again smaller and the file still opens; the presets' words, the
/// sizes' words and the copies' names; and the tool's place among the tools (Screens/More/ToolsScreen.cs) and in the
/// search box (Shell/Search/PaletteCommand.cs).
/// </summary>
public class ShrinkTests
{
    public ShrinkTests() => _ = Profile.Folder;

    /// <summary>A picture with the grain of a camera's sensor: every pixel its own, so a JPEG of it weighs what a photo weighs.</summary>
    private static byte[] Photo(int w, int h, int quality = 97)
    {
        var rnd = new Random(11);
        var px = new byte[w * h * 3];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var i = (y * w + x) * 3;
                px[i] = (byte)Math.Clamp(60 + x * 150 / w + rnd.Next(-30, 31), 0, 255);
                px[i + 1] = (byte)Math.Clamp(90 + y * 120 / h + rnd.Next(-30, 31), 0, 255);
                px[i + 2] = (byte)Math.Clamp(140 - x * 60 / w + rnd.Next(-30, 31), 0, 255);
            }
        var b = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr24, null, px, w * 3);
        var e = new JpegBitmapEncoder { QualityLevel = quality };
        e.Frames.Add(BitmapFrame.Create(b));
        var ms = new MemoryStream();
        e.Save(ms);
        return ms.ToArray();
    }

    /// <summary>A PNG whose left half is see-through.</summary>
    private static byte[] Png(int w, int h, bool alpha)
    {
        var px = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var i = (y * w + x) * 4;
                px[i] = (byte)(x * 255 / w);
                px[i + 1] = (byte)(y * 255 / h);
                px[i + 2] = 200;
                px[i + 3] = (byte)(alpha && x < w / 2 ? 0 : 255);
            }
        var b = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        var e = new PngBitmapEncoder();
        e.Frames.Add(BitmapFrame.Create(b));
        var ms = new MemoryStream();
        e.Save(ms);
        return ms.ToArray();
    }

    private static BitmapFrame Decoded(byte[] bytes) => BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];

    [Fact]
    public void AHeavyPhotoComesOutAtLeastAThirdSmallerAtBalancedAndStillReads()
    {
        var photo = Photo(1800, 1200);
        Assert.True(photo.Length > 1_000_000, $"the sample photo weighs {photo.Length} bytes; the test wants one over a megabyte");
        var r = Shrink.Image(photo, ".jpg", ShrinkPreset.Balanced);
        Assert.Equal("jpg", r.Ext);
        Assert.True(r.Bytes.Length <= photo.Length * 0.7, $"{photo.Length} came out as {r.Bytes.Length}");
        var d = Decoded(r.Bytes);
        Assert.Equal(1800, d.PixelWidth); // (under 2048 on its long edge: not made smaller)
        Assert.Equal(1200, d.PixelHeight);
    }

    [Fact]
    public void SmallestBringsALongEdgeDownTo1280()
    {
        var r = Shrink.Image(Photo(1800, 1200), ".jpg", ShrinkPreset.Smallest);
        Assert.Equal(1280, r.Width);
        Assert.Equal(853, r.Height);
        Assert.Equal(1280, Decoded(r.Bytes).PixelWidth);
    }

    [Fact]
    public void APngWithSeeThroughPartsKeepsThemUnlessSmallest()
    {
        var png = Png(600, 400, alpha: true);
        var kept = Shrink.Image(png, ".png", ShrinkPreset.Balanced);
        Assert.Equal("png", kept.Ext);
        Assert.True(Shrink.HasAlpha(Decoded(kept.Bytes)));
        var flat = Shrink.Image(png, ".png", ShrinkPreset.Smallest);
        Assert.Equal("jpg", flat.Ext);
        Assert.False(Shrink.HasAlpha(Decoded(flat.Bytes)));
        // (a PNG with nothing see-through is a JPEG at every preset)
        Assert.Equal("jpg", Shrink.Image(Png(600, 400, alpha: false), ".png", ShrinkPreset.Max).Ext);
    }

    [Fact]
    public void FitUnderLandsUnderTheLimitOrSaysTheSmallestItCouldReach()
    {
        var photo = Photo(1800, 1200);
        var r = Shrink.Image(photo, ".jpg", ShrinkPreset.Fit, 200_000);
        Assert.True(r.Reached, r.Note);
        Assert.True(r.Bytes.Length <= 200_000, $"{r.Bytes.Length} is over 0.2 MB");
        // (an impossible limit: the smallest it could reach, said as such)
        var tiny = Shrink.Image(photo, ".jpg", ShrinkPreset.Fit, 500);
        Assert.False(tiny.Reached);
        Assert.Contains("smallest it could reach", tiny.Note);
        Assert.True(tiny.Bytes.Length < photo.Length);
    }

    [Fact]
    public void APictureAlreadySmallIsLeftAsItWas()
    {
        var light = Photo(400, 300, quality: 60);
        var r = Shrink.Image(light, ".jpg", ShrinkPreset.Max);
        Assert.Same(light, r.Bytes);
        Assert.Contains("left as it was", r.Note);
    }

    [Fact]
    public void APdfsPicturesAreWrittenSmallerAndItStillOpens()
    {
        var photo = Photo(1800, 1200);
        var path = Path.Combine(Profile.Folder, "shrink-test.pdf");
        PdfWork.Pictures(new[] { new PdfWork.Picture(photo, 612, 408), new PdfWork.Picture(Png(500, 300, alpha: false), 500, 300) }, path);
        var pdf = File.ReadAllBytes(path);
        Assert.Equal(2, Shrink.PictureCount(pdf));
        var r = Shrink.Pdf(pdf, ShrinkPreset.Smallest);
        Assert.Equal("pdf", r.Ext);
        Assert.True(r.Bytes.Length <= pdf.Length * 0.7, $"{pdf.Length} came out as {r.Bytes.Length}");
        Assert.Contains("written smaller", r.Note);
        using var doc = PdfReader.Open(new MemoryStream(r.Bytes), PdfDocumentOpenMode.Import);
        Assert.Equal(2, doc.PageCount);
    }

    [Theory]
    [InlineData(900, "900 bytes")]
    [InlineData(41_000, "41 KB")]
    [InlineData(1_234_000, "1.23 MB")]
    [InlineData(12_345_000, "12.3 MB")]
    public void ASizeReadsAsFileExplorerWritesIt(long bytes, string text) => Assert.Equal(text, Shrink.Size(bytes));

    [Fact]
    public void WhatWasSavedIsAWholePercentage()
    {
        Assert.Equal(72, Shrink.Saved(1_900_000, 540_000));
        Assert.Equal(0, Shrink.Saved(100, 100));
        Assert.Equal(0, Shrink.Saved(100, 120));
    }

    [Theory]
    [InlineData("balanced", "balanced")]
    [InlineData("Max quality", "max")]
    [InlineData("smallest", "smallest")]
    [InlineData("Fit under", "fit")]
    public void APresetIsKnownByItsKeyOrItsLabel(string word, string key)
    {
        var preset = ShrinkPresets.Parse(word);
        Assert.NotNull(preset);
        Assert.Equal(key, ShrinkPresets.Key(preset!.Value));
        Assert.Null(ShrinkPresets.Parse("huge"));
    }

    [Fact]
    public void ACopyIsNamedSmallerAndACopyOfACopyStaysSo()
    {
        Assert.Equal("Lab bench-smaller", Shrink.OutputName(@"C:\Users\me\Pictures\Lab bench.jpg"));
        Assert.Equal("Lab bench-smaller", Shrink.OutputName(@"C:\Users\me\Downloads\Lab bench-smaller.jpg"));
        Assert.Equal("Lab bench-smaller", Shrink.OutputName(@"C:\Users\me\Downloads\Lab bench-smaller 2.jpg"));
        Assert.Equal(200_000, Shrink.Megabytes("0.2"));
        Assert.Null(Shrink.Megabytes("none"));
    }

    [Fact]
    public void CompressFilesIsAFilesToolWithTheMacsWords()
    {
        var kind = ToolsCenter.KindOf("shrink");
        Assert.NotNull(kind);
        Assert.Equal("Shrink images and PDFs to fit.", kind!.Note);
        Assert.Equal("#ff9500", kind.Hex);
        Assert.Equal("Compress", ToolsCenter.ShortName("shrink"));
        Assert.Equal("shrink", ToolsScreen.Groups.First(g => g.Title == "Files").Keys.Last());
        Assert.True((int)kind.Symbol <= 0xFFFF);
        foreach (var word in new[] { "compress", "shrink", "upload limit" })
        {
            var command = PaletteCommand.Named(PaletteCommand.NewTools().ToList(), word);
            Assert.NotNull(command);
            Assert.Equal(PaletteDo.Tool, command!.Action.Do);
            Assert.Equal("shrink", command.Action.Text);
        }
    }
}
