using TenLapse.Media.Decoding;

namespace TenLapse.Tests.Media;

/// <summary>sRGB is told by what a profile does (primaries and tone curves), not by its name.</summary>
public sealed class IccProfileTests
{
    [Fact]
    public void SrgbPrimariesAndCurveAreSrgbWhateverTheName() => Assert.True(IccProfile.IsSrgb(IccProfiles.Srgb()));

    [Fact]
    public void WindowsOwnSrgbProfileIsSrgb()
    {
        Assert.SkipUnless(File.Exists(IccProfiles.WindowsSrgbPath), "This PC has no sRGB Color Space Profile.icm.");

        Assert.True(IccProfile.IsSrgb(File.ReadAllBytes(IccProfiles.WindowsSrgbPath)));
    }

    [Fact]
    public void SwappedPrimariesNamedSrgbAreNot()
    {
        var swapped = IccProfiles.SwappedRedAndBlue();

        Assert.False(IccProfile.IsSrgb(swapped));
        Assert.True(IccProfile.IsRgb(swapped));
    }

    [Fact]
    public void APlainGammaIsNotSrgb() => Assert.False(IccProfile.IsSrgb(IccProfiles.Gamma22()));

    [Fact]
    public void AnythingElseIsNeither()
    {
        var cmyk = IccProfiles.Srgb();
        "CMYK"u8.CopyTo(cmyk.AsSpan(16));

        Assert.False(IccProfile.IsSrgb(cmyk));
        Assert.False(IccProfile.IsRgb(cmyk));
        Assert.False(IccProfile.IsSrgb([]));
        Assert.False(IccProfile.IsSrgb(new byte[200]));
        Assert.False(IccProfile.IsSrgb(IccProfiles.Srgb()[..140]));
    }
}
