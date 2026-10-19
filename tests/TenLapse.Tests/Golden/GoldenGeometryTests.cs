using System.Diagnostics;
using Microsoft.Graphics.Canvas;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Decoding;
using TenLapse.Media.Preview;
using TenLapse.Media.Rendering;
using TenLapse.Tests.Media;
using Windows.UI;
using static TenLapse.Tests.Media.MediaSkips;

namespace TenLapse.Tests.Golden;

/// <summary>
/// The golden render test for geometry (#32, and #18's promise that preview and export match for every
/// combination): photos of mixed shapes exported at every frame shape and fit, each decoded frame compared with
/// what the preview's call draws for it, <see cref="FrameRenderer.Draw(CanvasDrawingSession, CanvasBitmap, FrameSettings, PixelSize)"/>
/// with the snapshot's frame settings and video size into an offscreen target.
/// </summary>
/// <remarks>
/// The reference decodes each photo as the preview does (whole, at most <see cref="ProxyCache.DefaultMaxSide"/>, and
/// lets the renderer scale it); the export decodes it at the size the frame shows it. The two must agree within the
/// codec's error: the mean difference per channel, the black bars and every edge on a few scan lines within 2 px.
/// </remarks>
[Collection(nameof(RealEncoderCollection))]
public sealed class GoldenGeometryTests(GoldenGeometryTests.MixedPhotos photos) : IClassFixture<GoldenGeometryTests.MixedPhotos>
{
    /// <summary>
    /// The most a decoded frame may differ from the reference, on average per channel, in levels. Measured on this
    /// PC (NVENC): at most 1.8. Small shifts are the edge check's to catch (a frame drawn 3 px off measured 1 to 6
    /// here); this limit catches a wrong crop, a mirrored frame or wrong colours.
    /// </summary>
    private const double MeanErrorLimit = 4;

    /// <summary>How far a bar or an edge may move between the reference and the decoded frame, in pixels.</summary>
    private const double EdgeTolerance = 2;

    public static TheoryData<FrameAspect, FitMode> Combinations()
    {
        var data = new TheoryData<FrameAspect, FitMode>();
        foreach (var aspect in Enum.GetValues<FrameAspect>())
        {
            foreach (var fit in Enum.GetValues<FitMode>())
            {
                data.Add(aspect, fit);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public async Task TheExportedFrameMatchesWhatThePreviewDraws(FrameAspect aspect, FitMode fit)
    {
        SkipWithoutH264Encoder();
        var label = $"{aspect} {fit}";
        var folder = GoldenExport.EvidenceFolder($"{aspect}-{fit}");
        var sources = await photos.GetAsync();

        // 2 s per photo: the rate does not matter here, and a slow one gives each frame many bits.
        var snapshot = GoldenExport.Snapshot(sources.Select(photo => (photo.Path, photo.Size)), TimingSettings.PerPhoto(new Fraction(2, 1)), aspect, fit);
        var size = GoldenExport.SmallSize(snapshot.VideoSize);
        var output = Path.Combine(photos.Folder, $"{aspect}-{fit}.mp4");
        var clock = Stopwatch.StartNew();
        var result = await GoldenExport.ExportOrSkipAsync(snapshot, output);
        var (frames, _) = GoldenExport.ReadAllFrames(result.Path);

        var problems = new List<string>();
        var evidence = new List<(int Index, GoldenImage Reference, GoldenImage Exported)>();
        if (frames.Count != sources.Count)
        {
            problems.Add($"frames decoded: expected {sources.Count}, got {frames.Count}");
        }

        var worst = 0.0;
        using (var device = new CanvasDevice())
        {
            for (var index = 0; index < Math.Min(frames.Count, sources.Count); index++)
            {
                var exported = frames[index];
                var where = $"frame {index} ({sources[index].Name})";
                if (exported.Width != size.Width || exported.Height != size.Height)
                {
                    problems.Add($"{where}: size expected {size.Width}x{size.Height}, got {exported.Width}x{exported.Height}");
                    continue;
                }

                var reference = await PreviewDrawAsync(device, sources[index].Path, snapshot.FrameSettings, size);
                var found = Compare(reference, exported, sources[index].Size, size, fit);
                var error = GoldenImage.MeanAbsoluteError(reference, exported);
                var channel = Math.Max(error.B, Math.Max(error.G, error.R));
                worst = Math.Max(worst, channel);
                if (channel > MeanErrorLimit)
                {
                    found.Add($"mean error per channel (B, G, R) expected at most {MeanErrorLimit}, got ({error.B:0.00}, {error.G:0.00}, {error.R:0.00})");
                }

                if (found.Count > 0)
                {
                    problems.AddRange(found.Select(problem => $"{where}: {problem}"));
                    evidence.Add((index, reference, exported));
                }
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{label}: {size.Width}x{size.Height}, {frames.Count} frames, worst mean error {worst:0.00} levels, {clock.ElapsedMilliseconds} ms, encoder {result.Encoder?.Name ?? "unknown"}");

        if (problems.Count > 0)
        {
            Directory.CreateDirectory(folder);
            File.Copy(result.Path, Path.Combine(folder, "export.mp4"));
            foreach (var (index, reference, exported) in evidence)
            {
                await reference.SavePngAsync(Path.Combine(folder, $"frame{index}-preview.png"));
                await exported.SavePngAsync(Path.Combine(folder, $"frame{index}-export.png"));
            }

            Assert.Fail(GoldenExport.Report($"{label} at {size.Width}x{size.Height}, preview against export:", problems, $"{folder} (the video, and the preview and export PNG of each failing frame)"));
        }

        File.Delete(result.Path);
    }

    // The preview's drawing: the photo decoded whole (as a proxy is, at most 1280 px), drawn by FrameRenderer.Draw with
    // the snapshot's settings at the video size, onto a target first cleared to grey so the bars are the renderer's.
    private static async Task<GoldenImage> PreviewDrawAsync(CanvasDevice device, string path, FrameSettings settings, PixelSize size)
    {
        var decoded = await PhotoDecoder.DecodeAsync(path, new PhotoDecodeOptions { MaxSide = ProxyCache.DefaultMaxSide }, TestContext.Current.CancellationToken);
        Assert.True(decoded.Succeeded, $"{path} did not decode: {decoded.Failure}");
        using var photo = decoded.Value;
        using var bitmap = CanvasBitmap.CreateFromSoftwareBitmap(device, photo.Pixels);
        using var target = new CanvasRenderTarget(device, size.Width, size.Height, 96f);
        using (var session = target.CreateDrawingSession())
        {
            session.Clear(Color.FromArgb(255, 128, 128, 128));
            FrameRenderer.Draw(session, bitmap, settings, size);
        }

        return new GoldenImage(target.GetPixelBytes(), size.Width, size.Height);
    }

    // The bars on the middle row and column against the geometry (FrameRenderer.Layout) and against each other, and
    // every edge on three quiet rows and three quiet columns, matched within the tolerance.
    private static List<string> Compare(GoldenImage reference, GoldenImage exported, PixelSize photo, PixelSize size, FitMode fit)
    {
        var problems = new List<string>();
        var destination = FrameRenderer.Layout(photo, size, fit).Destination;
        var expectedSides = (destination.X, size.Width - destination.Right);
        var expectedEnds = (destination.Y, size.Height - destination.Bottom);

        var rows = reference.QuietRows([0.5, 0.3, 0.7]);
        var columns = reference.QuietColumns([0.5, 0.3, 0.7]);
        if (rows.Count == 0 || columns.Count == 0)
        {
            problems.Add($"no scan line clear of edges in the reference ({rows.Count} rows, {columns.Count} columns): the test photo's blocks are too small");
            return problems;
        }

        CheckBars(problems, $"left and right bars on row {rows[0]}", expectedSides, reference.BarsAlongRow(rows[0]), exported.BarsAlongRow(rows[0]));
        CheckBars(problems, $"top and bottom bars on column {columns[0]}", expectedEnds, reference.BarsAlongColumn(columns[0]), exported.BarsAlongColumn(columns[0]));
        foreach (var row in rows)
        {
            CheckEdges(problems, $"edges along row {row}", reference.EdgesAlongRow(row), exported.EdgesAlongRow(row));
        }

        foreach (var column in columns)
        {
            CheckEdges(problems, $"edges down column {column}", reference.EdgesAlongColumn(column), exported.EdgesAlongColumn(column));
        }

        return problems;
    }

    private static void CheckBars(List<string> problems, string what, (double Start, double End) geometry, (int Start, int End) reference, (int Start, int End) exported)
    {
        bool Near(double a, double b) => Math.Abs(a - b) <= EdgeTolerance;
        if (!Near(reference.Start, geometry.Start) || !Near(reference.End, geometry.End)
            || !Near(exported.Start, reference.Start) || !Near(exported.End, reference.End))
        {
            problems.Add($"{what}: geometry {geometry.Start:0.#} and {geometry.End:0.#} px, preview {reference.Start} and {reference.End} px, export {exported.Start} and {exported.End} px");
        }
    }

    private static void CheckEdges(List<string> problems, string what, IReadOnlyList<Edge> reference, IReadOnlyList<Edge> exported)
    {
        static List<Edge> Unmatched(IReadOnlyList<Edge> edges, IReadOnlyList<Edge> others) =>
            [.. edges.Where(edge => !others.Any(other => other.Sign == edge.Sign && Math.Abs(other.Position - edge.Position) <= EdgeTolerance))];

        var missing = Unmatched(reference, exported);
        var extra = Unmatched(exported, reference);
        if (missing.Count > 0 || extra.Count > 0)
        {
            problems.Add(
                $"{what} differ by more than {EdgeTolerance} px: preview [{string.Join(", ", reference)}], export [{string.Join(", ", exported)}]" +
                $" (not in the export: [{string.Join(", ", missing)}]; not in the preview: [{string.Join(", ", extra)}])");
        }
    }

    /// <summary>
    /// Five photos of mixed shapes, written once per class as PNG: landscape 3:2 (twice, so Match photos takes 3:2),
    /// portrait 2:3, square and a 3:1 panorama. Each is a grid of large blocks, bright and dark in a checkerboard
    /// (luma about 200 against about 105, never near the bars' black), with hues that do not repeat with the grid,
    /// so a crop off by a block period still shows.
    /// </summary>
    public sealed class MixedPhotos : IDisposable
    {
        private static readonly (byte R, byte G, byte B)[] _bright = [(250, 230, 90), (140, 240, 255), (255, 180, 220)];
        private static readonly (byte R, byte G, byte B)[] _dark = [(90, 90, 200), (200, 70, 70), (60, 140, 60)];

        private static readonly (string Name, int Width, int Height, int Columns, int Rows)[] _shapes =
        [
            ("landscape 3:2", 960, 640, 6, 4),
            ("portrait 2:3", 640, 960, 4, 6),
            ("square", 720, 720, 5, 5),
            ("panorama 3:1", 1200, 400, 9, 3),
            ("landscape 3:2 again", 960, 640, 6, 4),
        ];

        private readonly SemaphoreSlim _gate = new(1, 1);
        private IReadOnlyList<(string Name, string Path, PixelSize Size)>? _photos;

        public string Folder { get; } = GoldenExport.NewScratchFolder("geometry");

        public async Task<IReadOnlyList<(string Name, string Path, PixelSize Size)>> GetAsync()
        {
            await _gate.WaitAsync(TestContext.Current.CancellationToken);
            try
            {
                return _photos ??= await WriteAsync();
            }
            finally
            {
                _gate.Release();
            }
        }

        public void Dispose()
        {
            _gate.Dispose();
            GoldenExport.DeleteFolder(Folder);
        }

        private async Task<IReadOnlyList<(string Name, string Path, PixelSize Size)>> WriteAsync()
        {
            var photos = new List<(string Name, string Path, PixelSize Size)>();
            for (var n = 0; n < _shapes.Length; n++)
            {
                var (name, width, height, columns, rows) = _shapes[n];
                var pixels = new byte[width * height * 4];
                for (var y = 0; y < height; y++)
                {
                    var by = y * rows / height;
                    for (var x = 0; x < width; x++)
                    {
                        var bx = x * columns / width;
                        var palette = (bx + by) % 2 == 0 ? _bright : _dark;
                        var (r, g, b) = palette[(bx + (2 * by) + n) % palette.Length];
                        var i = ((y * width) + x) * 4;
                        (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (b, g, r, 255);
                    }
                }

                var path = System.IO.Path.Combine(Folder, $"photo{n}.png");
                await new GoldenImage(pixels, width, height).SavePngAsync(path);
                photos.Add((name, path, new PixelSize(width, height)));
            }

            return photos;
        }
    }
}
