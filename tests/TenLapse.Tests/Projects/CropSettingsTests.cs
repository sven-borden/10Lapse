using TenLapse.Core.Projects;
using TenLapse.Core.Timing;

namespace TenLapse.Tests.Projects;

/// <summary>Crop and pan's settings (#46): a framing stays inside the frame, the move between two, and its timeline.</summary>
public sealed class CropSettingsTests
{
    private static readonly Framing _corner = new(0.5, 0.25, 0.25);

    [Fact]
    public void TheDefaultFramingIsTheWholeFrame()
    {
        Assert.Equal(Framing.Whole, default);
        Assert.True(Framing.Whole.IsWhole);
        Assert.Equal((1.0, 0.5, 0.5, 1.0), (Framing.Whole.Size, Framing.Whole.CenterX, Framing.Whole.CenterY, Framing.Whole.Zoom));
        Assert.Equal(Framing.Whole, new Framing(1, 0.5, 0.5));
        Assert.False(CropSettings.Default.Enabled);
        Assert.False(CropSettings.Default.IsActive);
        Assert.Equal(1, CropSettings.Default.MaxZoom);
    }

    [Theory]
    [InlineData(0.2, 0.5, 0.5)]
    [InlineData(1.01, 0.5, 0.5)]
    [InlineData(1, 0.4, 0.5)]
    [InlineData(0.5, 0.2, 0.5)]
    [InlineData(0.5, 0.5, 0.8)]
    [InlineData(double.NaN, 0.5, 0.5)]
    [InlineData(0.5, double.PositiveInfinity, 0.5)]
    public void AFramingThatWouldLeaveTheFrameIsRefused(double size, double x, double y) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Framing(size, x, y));

    [Fact]
    public void ClampedMovesAFramingInsideTheFrame()
    {
        Assert.Equal(new Framing(0.25, 0.125, 0.875), Framing.Clamped(0.1, -3, 9));
        Assert.Equal(Framing.Whole, Framing.Clamped(4, 0.1, 0.9));
        Assert.Equal(new Framing(0.5, 0.75, 0.3), Framing.Clamped(0.5, 0.9, 0.3));
        Assert.Equal(Framing.Whole, Framing.Clamped(double.NaN, double.NaN, double.NaN));
    }

    [Fact]
    public void AZoomIntoACornerClosesInOnThatCornerAtASteadyRate()
    {
        var steps = Enumerable.Range(0, 11).Select(i => Framing.Between(Framing.Whole, new Framing(0.25, 0.125, 0.125), i / 10.0)).ToArray();

        // The same factor every step: from 1 to a quarter in ten steps is 4^(-1/10) a step.
        for (var i = 1; i < steps.Length; i++)
        {
            Assert.Equal(Math.Pow(0.25, 0.1), steps[i].Size / steps[i - 1].Size, 9);
        }

        // The top left corner is the point both show in the same place: every view keeps it.
        Assert.All(steps, step => Assert.Equal((0.0, 0.0), (Math.Round(step.Left, 12), Math.Round(step.Top, 12))));
    }

    [Fact]
    public void APanWithoutAZoomMovesEvenly()
    {
        var from = new Framing(0.5, 0.25, 0.5);
        var to = new Framing(0.5, 0.75, 0.5);

        Assert.Equal(new Framing(0.5, 0.5, 0.5), Framing.Between(from, to, 0.5));
        Assert.Equal(0.375, Framing.Between(from, to, 0.25).CenterX, 12);
    }

    [Fact]
    public void EveryFramingOnTheWayLiesInsideTheFrame()
    {
        // From one corner, zoomed in, to the opposite corner, zoomed in less: the view never leaves the frame.
        var from = new Framing(0.3, 0.15, 0.85);
        var to = new Framing(0.6, 0.7, 0.3);
        for (var i = 0; i <= 100; i++)
        {
            var at = Framing.Between(from, to, i / 100.0);
            Assert.InRange(at.Left, -1e-12, 1 - at.Size + 1e-12);
            Assert.InRange(at.Top, -1e-12, 1 - at.Size + 1e-12);
        }

        Assert.Equal(from, Framing.Between(from, to, 0));
        Assert.Equal(to, Framing.Between(from, to, 1));
    }

    [Fact]
    public void SmoothStartsAndEndsGentlyAndLinearKeepsOneSpeed()
    {
        var smooth = new CropSettings(true, Framing.Whole, new Framing(0.5, 0.5, 0.5), CropEasing.Smooth);
        var linear = smooth with { Easing = CropEasing.Linear };

        // Halfway is halfway either way (a steady zoom from 1 to 0.5 is at √0.5 there).
        Assert.Equal(Math.Sqrt(0.5), smooth.At(0.5).Size, 12);
        Assert.Equal(Math.Sqrt(0.5), linear.At(0.5).Size, 12);

        // The first tenth: smoothstep has covered 2.8 % of the way, linear 10 %.
        Assert.Equal(Math.Pow(0.5, 0.028), smooth.At(0.1).Size, 12);
        Assert.Equal(Math.Pow(0.5, 0.1), linear.At(0.1).Size, 12);
        Assert.Equal(Framing.Whole, smooth.At(-1));
        Assert.Equal(new Framing(0.5, 0.5, 0.5), smooth.At(2));
    }

    [Fact]
    public void OffOrWithTheWholeFrameAtBothEndsItChangesNothing()
    {
        var off = new CropSettings(false, _corner, Framing.Whole, CropEasing.Linear);
        var whole = new CropSettings(true, Framing.Whole, Framing.Whole, CropEasing.Linear);

        Assert.False(off.IsActive);
        Assert.False(whole.IsActive);
        Assert.Equal(Framing.Whole, off.At(0));
        Assert.Equal(1, off.MaxZoom);
        Assert.True((off with { Enabled = true }).IsActive);
        Assert.Equal(2, (off with { Enabled = true }).MaxZoom);
    }

    [Fact]
    public void TheMoveRunsOverThePhotosFramesAndTheHoldsStandStill()
    {
        // 5 photos, a 3-frame start hold and a 2-frame end hold: frames 3 to 7 move, 0 to 3 show the start, 7 to 9 the end.
        var sequence = new FrameSequence(5, 3, 2, 1, false);
        var progress = Enumerable.Range(0, sequence.Length).Select(position => CropSettings.Progress(sequence, position)).ToArray();

        Assert.Equal(new double[] { 0, 0, 0, 0, 0.25, 0.5, 0.75, 1, 1, 1 }, progress);
    }

    [Fact]
    public void ABoomerangAndItsPlaysAreOneMove()
    {
        // 4 photos as a boomerang played twice: 2 × 6 frames, one move from the first to the last.
        var sequence = new FrameSequence(4, 0, 0, 2, true);
        var crop = new CropSettings(true, Framing.Whole, _corner, CropEasing.Linear);

        Assert.Equal(12, sequence.CoreLength);
        Assert.Equal(Framing.Whole, crop.At(sequence, 0));
        Assert.Equal(_corner, crop.At(sequence, 11));

        // Photo 1 shows at frames 1 and 5: framed where the video is each time.
        Assert.Equal(1, sequence.PhotoAt(1));
        Assert.Equal(1, sequence.PhotoAt(5));
        Assert.NotEqual(crop.At(sequence, 1), crop.At(sequence, 5));
    }

    [Fact]
    public void AVideoOfOneFrameShowsTheStart()
    {
        var crop = new CropSettings(true, _corner, Framing.Whole, CropEasing.Smooth);

        Assert.Equal(_corner, crop.At(FrameSequence.Plain(1), 0));
        Assert.Equal(0, CropSettings.Progress(FrameSequence.Plain(0), 0));
    }

    [Fact]
    public void TheEditIsOneChangeAndTheSnapshotHasIt()
    {
        var project = new Project();
        var changes = new List<ProjectChanges>();
        project.Changed += (_, e) => changes.Add(e.Changes);
        var crop = new CropSettings(true, Framing.Whole, _corner, CropEasing.Linear);

        project.Crop = crop;
        project.Crop = crop;

        Assert.Equal(crop, project.Snapshot.Crop);
        Assert.Single(changes);
        Assert.Equal(ProjectChanges.Crop, changes[0] & ~ProjectChanges.Dirty);
        Assert.True(project.Snapshot.IsDirty);
        Assert.Throws<ArgumentNullException>(() => project.Crop = null!);
        Assert.Throws<ArgumentOutOfRangeException>(() => crop with { Easing = (CropEasing)7 });
    }
}
