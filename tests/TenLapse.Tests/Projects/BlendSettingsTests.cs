using TenLapse.Core.Projects;

namespace TenLapse.Tests.Projects;

/// <summary>
/// The frame blending settings (#47): which photos a photo blends, their shares at each strength, the ends of the
/// project, and what a value out of range does.
/// </summary>
public sealed class BlendSettingsTests
{
    private static readonly BlendSettings _on = BlendSettings.Default with { Enabled = true };

    [Fact]
    public void ANewProjectDoesNotBlendAndRemembersFourPhotosAtFullStrength()
    {
        Assert.False(BlendSettings.Default.Enabled);
        Assert.False(BlendSettings.Default.Blends);
        Assert.Equal(4, BlendSettings.Default.Frames);
        Assert.Equal(100, BlendSettings.Default.Strength);
        Assert.Equal(BlendSettings.Default, new Project().Blend);
    }

    [Theory]
    [InlineData(2, 9, 10)]
    [InlineData(3, 9, 11)]
    [InlineData(4, 8, 11)]
    [InlineData(16, 2, 17)]
    public void APhotoBlendsTheCountAroundItWithOneMoreBeforeWhenItIsEven(int frames, int first, int last)
    {
        Assert.Equal((first, last), (_on with { Frames = frames }).Window(10, 100));
        Assert.Equal(frames, last - first + 1);
    }

    [Fact]
    public void AtTheEndsOfTheProjectAPhotoBlendsThePhotosThereAre()
    {
        var eight = _on with { Frames = 8 };

        Assert.Equal((0, 3), eight.Window(0, 100));
        Assert.Equal((0, 5), eight.Window(2, 100));
        Assert.Equal((95, 99), eight.Window(99, 100));
        Assert.Equal((0, 2), eight.Window(1, 3));
        Assert.Equal((0, 0), eight.Window(0, 1));
    }

    [Fact]
    public void AtFullStrengthEveryPhotoHasTheSameShare()
    {
        var layers = (_on with { Frames = 4 }).Layers(10, 100);

        Assert.Equal([8, 9, 10, 11], layers.Select(layer => layer.Photo));
        Assert.All(layers, layer => Assert.Equal(0.25, layer.Weight, 12));
    }

    [Fact]
    public void BelowFullStrengthThePhotoKeepsTheRestOfTheLight()
    {
        // Half: half the photo plus its quarter of the blended half.
        var layers = (_on with { Frames = 4, Strength = 50 }).Layers(10, 100);

        Assert.Equal(0.625, layers.Single(layer => layer.Photo == 10).Weight, 12);
        Assert.All(layers.Where(layer => layer.Photo != 10), layer => Assert.Equal(0.125, layer.Weight, 12));
        Assert.Equal(1, layers.Sum(layer => layer.Weight), 12);

        // At an end there are fewer photos to share with: 3 of them at 30 %.
        var start = (_on with { Frames = 6, Strength = 30 }).Layers(0, 100);
        Assert.Equal([0, 1, 2], start.Select(layer => layer.Photo));
        Assert.Equal(0.8, start[0].Weight, 12);
        Assert.Equal(1, start.Sum(layer => layer.Weight), 12);
    }

    [Fact]
    public void OffNoStrengthOrOnePhotoIsThePhotoAlone()
    {
        Assert.Equal([new BlendLayer(5, 1)], BlendSettings.Default.Layers(5, 10));
        Assert.Equal([new BlendLayer(5, 1)], (_on with { Strength = 0 }).Layers(5, 10));
        Assert.Equal([new BlendLayer(0, 1)], _on.Layers(0, 1));
        Assert.False((_on with { Strength = 0 }).Blends);
        Assert.True(_on.Blends);
    }

    [Fact]
    public void AValueOutOfRangeIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _on with { Frames = 1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => _on with { Frames = 17 });
        Assert.Throws<ArgumentOutOfRangeException>(() => _on with { Strength = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => _on with { Strength = 101 });
        Assert.Throws<ArgumentOutOfRangeException>(() => _on.Window(10, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => _on.Window(-1, 10));
    }

    [Fact]
    public void TurningItOffKeepsTheCountAndTheStrength()
    {
        var project = new Project();
        project.Blend = new BlendSettings(true, 9, 60);
        ProjectChanges changes = default;
        project.Changed += (_, e) => changes = e.Changes;

        project.Blend = project.Blend with { Enabled = false };

        Assert.True((changes & ProjectChanges.Blend) != 0);
        Assert.Equal(new BlendSettings(false, 9, 60), project.Blend);
        Assert.True(project.Snapshot.IsDirty);
    }
}
