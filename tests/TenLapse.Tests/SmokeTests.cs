using TenLapse.Core;
using TenLapse.Media;

namespace TenLapse.Tests;

/// <summary>Proves the harness loads each referenced project, native Win2D included.</summary>
public sealed class SmokeTests
{
    [Fact]
    public void CoreIsReferenced()
    {
        Assert.Equal("10Lapse", Product.Name);
    }

    [Fact]
    public void MediaCreatesTheWin2DDevice()
    {
        var device = GraphicsDevice.Shared;

        Assert.NotNull(device);
        Assert.Same(device, GraphicsDevice.Shared);
    }
}
