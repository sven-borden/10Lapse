using TenLapse.Core.Projects;
using TenLapse.Media.Decoding;

namespace TenLapse.Tests.Fixtures;

/// <summary>An expected colour, as a pixel of the decoded BGRA8 premultiplied bitmap reads (alpha 255 unless the file has some).</summary>
internal readonly record struct Rgba(byte R, byte G, byte B, byte A = 255);

/// <summary>
/// What the decoder must do with one file of the fixture folder. <see cref="Reason"/> is null for a file that decodes.
/// <see cref="Quadrants"/> are the colours at the middle of the top-left, top-right, bottom-left and bottom-right
/// quarters of the picture as shown, within <see cref="Tolerance"/> levels.
/// </summary>
/// <param name="Name">The file's name in <c>Fixtures/Photos</c>.</param>
/// <param name="Reason">Why the decoder refuses the file, or null.</param>
/// <param name="Size">The oriented size of a file that decodes (a cover decode never enlarges, so it is the decoded size too).</param>
/// <param name="Orientation">The EXIF orientation stored in the file.</param>
/// <param name="Frames">The number of frames in the file.</param>
/// <param name="Quadrants">The colours shown, or null when the file does not decode.</param>
/// <param name="Tolerance">How far each channel may be off: JPEG compression, chroma subsampling, noise.</param>
/// <param name="Skip">When set, the test is skipped with this reason: a decoder bug this fixture found.</param>
internal sealed record PhotoFixture(
    string Name,
    SkipReason? Reason = null,
    PixelSize Size = default,
    int Orientation = 1,
    int Frames = 1,
    Rgba[]? Quadrants = null,
    int Tolerance = 0,
    string? Skip = null)
{
    public string Path => System.IO.Path.Combine(PhotoFixtureCatalogue.Directory, Name);

    public bool Decodes => Reason is null;

    public override string ToString() => Name;
}

/// <summary>
/// The committed fixture folder (<c>Fixtures/Photos</c>, written by <c>make_fixtures.py</c> and copied next to the
/// test host) and what the decoder does with each file. <c>Fixtures/README.md</c> says the same in prose.
/// </summary>
internal static class PhotoFixtureCatalogue
{
    /// <summary>The scene every generated picture shows: red top-left (the orientation marker), blue, green, yellow.</summary>
    public static readonly Rgba[] Scene = [new(240, 20, 20), new(30, 60, 220), new(30, 200, 60), new(240, 220, 30)];

    private const int JpegTolerance = 24;
    private const int NoisyTolerance = 55;

    public static string Directory { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "Photos");

    public static string Readme { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "README.md");

    public static IReadOnlyList<PhotoFixture> All { get; } =
    [
        // ---- refused, each with its reason ----
        new("empty.jpg", SkipReason.EmptyFile),
        new("truncated.jpg", SkipReason.Truncated),
        new("no-eoi.jpg", SkipReason.Truncated),
        new("text.heic", SkipReason.UnsupportedFormat),

        // ---- awkward and fine ----
        new("png-named.jpg", Size: new(96, 64), Quadrants: Scene, Tolerance: 2),
        new("trailing-data.jpg", Size: new(256, 160), Quadrants: Scene, Tolerance: NoisyTolerance),
        new("progressive.jpg", Size: new(256, 160), Quadrants: Scene, Tolerance: NoisyTolerance),
        new("two-frames.gif", Size: new(96, 64), Frames: 2, Quadrants: Scene, Tolerance: 2),
        new("rgb16.png", Size: new(96, 64), Quadrants: Scene, Tolerance: 2),
        new("palette-alpha.png", Size: new(96, 64), Quadrants: [new(240, 20, 20), new(30, 60, 220), new(15, 100, 30, 128), new(0, 0, 0, 0)], Tolerance: 3),
        new("gray.jpg", Size: new(96, 64), Quadrants: [Gray(230), Gray(40), Gray(120), Gray(190)], Tolerance: JpegTolerance),
        // Pillow stored C = 255 - R and so on, with no K and no profile. Windows converts through its default CMYK
        // profile (ink-like: blue turns dull), so these are the colours it shows, not the scene's.
        new("cmyk.jpg", Size: new(96, 64), Quadrants: [new(218, 64, 51), new(77, 75, 124), new(36, 133, 91), new(240, 203, 65)], Tolerance: 16),

        // ---- colour profiles: sRGB is left alone, the wide-gamut ones are converted to sRGB (clipped where they must be) ----
        new("profile-srgb.jpg", Size: new(96, 64), Quadrants: Scene, Tolerance: JpegTolerance),
        new("profile-adobergb.jpg", Size: new(96, 64), Quadrants: [new(255, 12, 12), new(0, 57, 225), new(0, 201, 37), new(248, 221, 0)], Tolerance: 16),
        new("profile-displayp3.jpg", Size: new(96, 64), Quadrants: [new(255, 0, 0), new(17, 61, 229), new(0, 204, 13), new(244, 219, 0)], Tolerance: 16),

        // ---- EXIF ----
        Oriented(1, 96, 64),
        Oriented(3, 96, 64),
        Oriented(6, 64, 96),
        Oriented(8, 64, 96),
        new("exif-date.jpg", Size: new(96, 64), Quadrants: Scene, Tolerance: JpegTolerance),
    ];

    public static TheoryData<string> Names() => new(All.Select(fixture => fixture.Name));

    public static TheoryData<string> GoodNames() => new(All.Where(fixture => fixture.Decodes).Select(fixture => fixture.Name));

    public static PhotoFixture Get(string name) => All.Single(fixture => fixture.Name == name);

    /// <summary>
    /// The quadrant colours of the stored scene as they appear after EXIF <paramref name="orientation"/>
    /// (orientation 6 turns the picture clockwise: the stored top-left ends at the top-right).
    /// </summary>
    public static Rgba[] Displayed(Rgba[] stored, int orientation) => orientation switch
    {
        1 => stored,
        3 => [stored[3], stored[2], stored[1], stored[0]],
        6 => [stored[2], stored[0], stored[3], stored[1]],
        8 => [stored[1], stored[3], stored[0], stored[2]],
        _ => throw new ArgumentOutOfRangeException(nameof(orientation)),
    };

    private static PhotoFixture Oriented(int orientation, int width, int height) =>
        new($"orientation-{orientation}.jpg", Size: new(width, height), Orientation: orientation, Quadrants: Displayed(Scene, orientation), Tolerance: JpegTolerance);

    private static Rgba Gray(byte level) => new(level, level, level);
}
