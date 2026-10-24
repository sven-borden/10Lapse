using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Decoding;
using TenLapse.Media.Export;
using TenLapse.Media.Rendering;
using TenLapse.Tests.Golden;
using Windows.Foundation;

namespace TenLapse.Tests.Media;

/// <summary>
/// Crop and pan's geometry in the shared renderer (#46): the part of the photo a framing shows and where it lands, the
/// whole photo's placement for the reframe, the larger decode of a zoomed frame, and each exported frame's framing.
/// </summary>
public sealed class CropLayoutTests
{
    private static readonly PixelSize _photo = new(3000, 2000);
    private static readonly PixelSize _frame = new(1920, 1080);

    [Fact]
    public void TheWholeFrameIsTheLayoutWithoutAView()
    {
        foreach (var fit in Enum.GetValues<FitMode>())
        {
            Assert.Equal(FrameRenderer.Layout(_photo, _frame, fit), FrameRenderer.Layout(_photo, _frame, fit, Framing.Whole));
        }
    }

    [Fact]
    public void AFillViewShowsItsShareOfTheCoverCropEnlargedToTheWholeFrame()
    {
        // Fill covers 1920x1080 with 3000x1687.5 of the photo, from row 156.25. The top left quarter shows half of that.
        var (cover, _) = FrameRenderer.Layout(_photo, _frame, FitMode.Fill);
        var (source, destination) = FrameRenderer.Layout(_photo, _frame, FitMode.Fill, new Framing(0.5, 0.25, 0.25));

        Assert.Equal(new Rect(0, 0, 1920, 1080), destination);
        Assert.Equal(cover.X, source.X, 9);
        Assert.Equal(cover.Y, source.Y, 9);
        Assert.Equal(cover.Width / 2, source.Width, 9);
        Assert.Equal(cover.Height / 2, source.Height, 9);
    }

    [Fact]
    public void AFitViewThatTakesInABarShowsTheBarEnlargedToo()
    {
        // Fit puts the 3:2 photo 1620 px wide in the middle of 1920, bars of 150 px. The left half of the frame, from the top:
        // 150 px of bar and 810 px of photo, enlarged twice.
        var (_, placed) = FrameRenderer.Layout(_photo, _frame, FitMode.Fit);
        Assert.Equal(new Rect(150, 0, 1620, 1080), placed);

        var (source, destination) = FrameRenderer.Layout(_photo, _frame, FitMode.Fit, new Framing(0.5, 0.25, 0.25));
        Assert.Equal(new Rect(300, 0, 1620, 1080), destination);
        Assert.Equal(new Rect(0, 0, 1500, 1000), source);
    }

    [Fact]
    public void AFitViewInsideABarShowsNoPhoto()
    {
        // A 10:1 strip in a 1:1 frame leaves bars above and below; a small view in the top bar shows only bar.
        var (source, destination) = FrameRenderer.Layout(new PixelSize(1000, 100), new PixelSize(1000, 1000), FitMode.Fit, new Framing(0.25, 0.5, 0.125));

        Assert.Equal(0, destination.Width);
        Assert.Equal(0, source.Width);
    }

    [Fact]
    public void ThePlacementIsTheWholePhotoEnlargedWithTheView()
    {
        // Fill: the whole photo is 1920 x 1280 at y -100; through the top left quarter, enlarged twice from the frame's corner.
        Assert.Equal(new Rect(0, -100, 1920, 1280), FrameRenderer.Placement(_photo, _frame, FitMode.Fill, Framing.Whole));
        Assert.Equal(new Rect(0, -200, 3840, 2560), FrameRenderer.Placement(_photo, _frame, FitMode.Fill, new Framing(0.5, 0.25, 0.25)));
    }

    [Fact]
    public void AZoomedFrameIsDecodedForAFrameThatMuchLarger()
    {
        Assert.Equal(FrameRenderer.DecodeOptions(FitMode.Fill, _frame), FrameRenderer.DecodeOptions(FitMode.Fill, _frame, 1));
        Assert.Equal(PhotoDecodeOptions.Cover(new PixelSize(3840, 2160)), FrameRenderer.DecodeOptions(FitMode.Fill, _frame, 2));
        Assert.Equal(PhotoDecodeOptions.Contain(new PixelSize(2880, 1620)), FrameRenderer.DecodeOptions(FitMode.Fit, _frame, 1.5));
    }

    [Fact]
    public void AZoomedExportDecodesFewerFramesAhead()
    {
        var settings = new VideoExportSettings(1920, 1080, new Fraction(30, 1));

        Assert.Equal(8, settings.DecodeFor(tightMemory: false).Ahead);
        Assert.Equal(3, (settings with { DecodeZoom = 1.5 }).DecodeFor(tightMemory: false).Ahead);
        Assert.Equal(2, (settings with { DecodeZoom = 4 }).DecodeFor(tightMemory: false).Ahead);
        Assert.Equal(2, (settings with { DecodeZoom = 2 }).DecodeFor(tightMemory: true).Ahead);
        Assert.Equal(6, (settings with { DecodeZoom = 4, DecodeAhead = 6 }).DecodeFor(tightMemory: true).Ahead);
        Assert.Throws<ArgumentOutOfRangeException>(() => settings with { DecodeZoom = 0.5 });
    }

    [Fact]
    public void EachExportedFrameTakesTheFramingOfItsPlaceInTheVideo()
    {
        // 3 photos held 2 frames at each end at 2 fps (1 s holds), from the whole frame to the top left quarter, linearly.
        var frames = Enumerable.Range(0, 3).Select(i => GoldenExport.FrameOf($@"C:\nowhere\{i}.jpg")).ToArray();
        var corner = new Framing(0.5, 0.25, 0.25);
        var snapshot = GoldenExport.Snapshot(
            frames,
            TimingSettings.PerSecond(new Fraction(2, 1)),
            FrameAspect.Ratio16x9,
            FitMode.Fill,
            sequence: new SequenceSettings(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 1, false),
            crop: new CropSettings(true, Framing.Whole, corner, CropEasing.Linear));
        var settings = ProjectExporter.SettingsFor(snapshot);
        var positions = ProjectExporter.PositionsFor(snapshot, settings);
        var photos = ProjectExporter.FramesFor(snapshot, settings);

        var looks = ProjectExporter.LooksFor(snapshot, photos, positions)!;

        Assert.Equal(7, looks.Length);
        Assert.Equal(new[] { 1.0, 1, 1, Math.Round(Math.Sqrt(0.5), 12), 0.5, 0.5, 0.5 }, looks.Select(look => Math.Round(look.View.Size, 12)));
        Assert.Equal(2, settings.DecodeZoom);
        Assert.Equal(FrameLook.For(snapshot, snapshot.Frames[1].Id, 3), looks[3]);
    }
}
