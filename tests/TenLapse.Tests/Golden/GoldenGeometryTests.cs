using System.Diagnostics;
using Microsoft.Graphics.Canvas;
using TenLapse.Core.Preview;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Preview;
using TenLapse.Media.Rendering;
using TenLapse.Tests.Media;
using Windows.UI;
using static TenLapse.Tests.Media.MediaSkips;

namespace TenLapse.Tests.Golden;

/// <summary>
/// The golden render test for geometry (#32, and #18's promise that preview and export match for every
/// combination): photos of mixed shapes exported at every frame shape and fit, each decoded frame compared with
/// what the preview draws for it: the preview's own bitmap (<see cref="PreviewImageSource"/>, through a
/// <see cref="ProxyCache"/>) drawn by <see cref="FrameRenderer.Draw(CanvasDrawingSession, CanvasBitmap, FrameSettings, PixelSize)"/>
/// with the snapshot's frame settings and video size into an offscreen target.
/// </summary>
/// <remarks>
/// The reference is the preview's path up to the swap chain: the proxy JPEG (at most
/// <see cref="ProxyCache.DefaultMaxSide"/>, quality 0.85, read back from the cache), scaled by the renderer. The
/// swap chain's transform and DPI scale that whole frame onto the screen afterwards and are not compared. The
/// export decodes each photo at the size the frame shows it. The two must agree within the codec's error: the mean
/// difference per channel, the frame's shape, the black bars within 1 px, and every edge on a few scan lines within
/// 1 px.
/// </remarks>
[Collection(nameof(RealEncoderCollection))]
public sealed class GoldenGeometryTests(GoldenGeometryTests.MixedPhotos photos) : IClassFixture<GoldenGeometryTests.MixedPhotos>
{
    private const string ClassName = "geometry";

    /// <summary>
    /// The most a decoded frame may differ from the reference, on average per channel, in levels. Measured on this
    /// PC: at most 1.8 with NVENC, 3.02 with Microsoft's software encoder. Small shifts are the edge check's to
    /// catch; this limit catches a wrong crop, a mirrored frame or wrong colours.
    /// </summary>
    private const double MeanErrorLimit = 4;

    /// <summary>
    /// How far an edge may move between the reference and the decoded frame, in pixels. Measured noise: at most
    /// 0.42 px with NVENC, 0.46 px with the software encoder; a 1 px shift or a 1 % crop moves some edge further.
    /// </summary>
    private const double EdgeTolerance = 1.0;

    /// <summary>
    /// How far a bar may differ, in whole pixels, between the reference and the geometry and between the export and
    /// the reference. Measured noise: 0 px with NVENC, 1 px with the software encoder (4:5 Fit).
    /// </summary>
    private const int BarTolerance = 1;

    /// <summary>
    /// <see cref="MeanErrorLimit"/> for an animated GIF (#51). Measured on this PC: at most 4.88 levels, on the square photo
    /// whose proxy JPEG's slight noise the 256-colour palette meets with error diffusion's grain; the edges stay within
    /// 0.37 px and the bars within 1 px, so the grain is not a shift. A wrong crop or wrong colours is far above it.
    /// </summary>
    private const double GifMeanErrorLimit = 6;

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
        var folder = GoldenExport.EvidenceFolder(ClassName, $"{aspect}-{fit}");
        var (sources, proxies) = await photos.GetAsync();

        // 2 s per photo: the rate does not matter here, and a slow one gives each frame many bits.
        var snapshot = GoldenExport.Snapshot(sources.Select(photo => GoldenExport.FrameOf(photo.Path, photo.Size)), TimingSettings.PerPhoto(new Fraction(2, 1)), aspect, fit);
        var size = GoldenExport.SmallSize(snapshot.VideoSize);
        var shape = Shape(aspect);
        var output = Path.Combine(photos.Folder, $"{aspect}-{fit}.mp4");
        var clock = Stopwatch.StartNew();
        var (result, _) = await GoldenExport.ExportSmallOrSkipAsync(snapshot, output);
        var (frames, _) = GoldenExport.ReadAllFrames(result.Path);

        var problems = new List<string>();
        var evidence = new List<(int Index, GoldenImage Reference, GoldenImage Exported)>();
        if (frames.Count != sources.Count)
        {
            problems.Add($"frames decoded: expected {sources.Count}, got {frames.Count}");
        }

        // The frame's shape from the aspect alone, not through OutputSize: 320 px on the short side, the long side
        // from the nominal ratio, within SmallSize's rounding to even.
        var shaped = GoldenExport.SmallSize(new PixelSize(shape.Width, shape.Height));
        if (frames.Count > 0 && (Math.Abs(frames[0].Width - shaped.Width) > 2 || Math.Abs(frames[0].Height - shaped.Height) > 2))
        {
            problems.Add($"shape: {aspect} is {shape.Width}:{shape.Height}, so about {shaped.Width}x{shaped.Height}; the export is {frames[0].Width}x{frames[0].Height}");
        }

        var (worstError, worstEdge, worstBar) = (0.0, 0.0, 0);
        using (var device = new CanvasDevice())
        {
            var preview = new PreviewImageSource(proxies, () => device);
            for (var index = 0; index < Math.Min(frames.Count, sources.Count); index++)
            {
                var exported = frames[index];
                var where = $"frame {index} ({sources[index].Name})";
                if (exported.Width != size.Width || exported.Height != size.Height)
                {
                    problems.Add($"{where}: size expected {size.Width}x{size.Height} (the preview's), got {exported.Width}x{exported.Height}");
                    continue;
                }

                var reference = await PreviewDrawAsync(device, preview, snapshot.Frames[index], snapshot.FrameSettings, size);
                var comparison = Compare(reference, exported, sources[index].Size, size, fit);
                var found = comparison.Problems;
                var error = GoldenImage.MeanAbsoluteError(reference, exported);
                var channel = Math.Max(error.B, Math.Max(error.G, error.R));
                (worstError, worstEdge, worstBar) = (Math.Max(worstError, channel), Math.Max(worstEdge, comparison.EdgeShift), Math.Max(worstBar, comparison.BarShift));
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
            $"{label}: {size.Width}x{size.Height}, {frames.Count} frames, worst mean error {worstError:0.00} levels, worst matched edge shift {worstEdge:0.00} px, " +
            $"worst bar shift {worstBar} px, {clock.ElapsedMilliseconds} ms, encoder {result.Encoder?.Name ?? "unknown"}");

        if (problems.Count > 0)
        {
            Directory.CreateDirectory(folder);
            File.Copy(result.Path, Path.Combine(folder, "export.mp4"), overwrite: true);
            foreach (var (index, reference, exported) in evidence)
            {
                await reference.SavePngAsync(Path.Combine(folder, $"frame{index}-preview.png"));
                await exported.SavePngAsync(Path.Combine(folder, $"frame{index}-export.png"));
            }

            Assert.Fail(GoldenExport.Report($"{label} at {size.Width}x{size.Height}, preview against export:", problems, $"{folder} (the video, and the preview and export PNG of each failing frame)"));
        }

        File.Delete(result.Path);
    }

    /// <summary>
    /// The same agreement for an animated GIF (#51), at the GIF's own size (Medium, 480 px on the long side): the GIF's
    /// frames come through the same <see cref="FrameRenderer.Draw(CanvasDrawingSession, CanvasBitmap, FrameSettings, PixelSize)"/>,
    /// then a 256-colour palette and error diffusion, which add grain but no shift.
    /// </summary>
    [Theory]
    [MemberData(nameof(Combinations))]
    public async Task TheGifFrameMatchesWhatThePreviewDraws(FrameAspect aspect, FitMode fit)
    {
        SkipWithoutGraphicsDevice();
        var label = $"GIF {aspect} {fit}";
        var folder = GoldenExport.EvidenceFolder(ClassName, $"gif-{aspect}-{fit}");
        var (sources, proxies) = await photos.GetAsync();
        var snapshot = GoldenExport.Snapshot(sources.Select(photo => GoldenExport.FrameOf(photo.Path, photo.Size)), TimingSettings.PerPhoto(new Fraction(2, 1)), aspect, fit, VideoFormat.Gif);
        var size = snapshot.VideoSize;
        // The shape from the aspect alone: 480 px on the long side, the short side from the nominal ratio, within 2 px.
        var shape = Shape(aspect);
        var (longRatio, shortRatio) = (Math.Max(shape.Width, shape.Height), Math.Min(shape.Width, shape.Height));
        Assert.Equal(480, Math.Max(size.Width, size.Height));
        Assert.InRange(Math.Min(size.Width, size.Height), (480.0 * shortRatio / longRatio) - 2, (480.0 * shortRatio / longRatio) + 2);
        Assert.Equal(shape.Width >= shape.Height, size.Width >= size.Height);

        var output = Path.Combine(photos.Folder, $"gif-{aspect}-{fit}.gif");
        var clock = Stopwatch.StartNew();
        var result = await ExportOrSkipAsync(snapshot, output);
        var frames = new List<GoldenImage>();
        TenLapse.Media.Inspection.GifFileReader.ReadFrames(result.Path, frame => frames.Add(GoldenImage.Copy(frame.Pixels, frame.Width, frame.Height, frame.Stride)));

        var problems = new List<string>();
        var evidence = new List<(int Index, GoldenImage Reference, GoldenImage Exported)>();
        if (frames.Count != sources.Count)
        {
            problems.Add($"frames decoded: expected {sources.Count} (0.5 fps is slower than the preset, so every photo), got {frames.Count}");
        }

        var (worstError, worstEdge, worstBar) = (0.0, 0.0, 0);
        using (var device = new CanvasDevice())
        {
            var preview = new PreviewImageSource(proxies, () => device);
            for (var index = 0; index < Math.Min(frames.Count, sources.Count); index++)
            {
                var exported = frames[index];
                var where = $"frame {index} ({sources[index].Name})";
                if (exported.Width != size.Width || exported.Height != size.Height)
                {
                    problems.Add($"{where}: size expected {size.Width}x{size.Height} (the preview's), got {exported.Width}x{exported.Height}");
                    continue;
                }

                var reference = await PreviewDrawAsync(device, preview, snapshot.Frames[index], snapshot.FrameSettings, size);
                var comparison = Compare(reference, exported, sources[index].Size, size, fit);
                var found = comparison.Problems;
                var error = GoldenImage.MeanAbsoluteError(reference, exported);
                var channel = Math.Max(error.B, Math.Max(error.G, error.R));
                (worstError, worstEdge, worstBar) = (Math.Max(worstError, channel), Math.Max(worstEdge, comparison.EdgeShift), Math.Max(worstBar, comparison.BarShift));
                if (channel > GifMeanErrorLimit)
                {
                    found.Add($"mean error per channel (B, G, R) expected at most {GifMeanErrorLimit}, got ({error.B:0.00}, {error.G:0.00}, {error.R:0.00})");
                }

                if (found.Count > 0)
                {
                    problems.AddRange(found.Select(problem => $"{where}: {problem}"));
                    evidence.Add((index, reference, exported));
                }
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{label}: {size.Width}x{size.Height}, {frames.Count} frames, worst mean error {worstError:0.00} levels, worst matched edge shift {worstEdge:0.00} px, " +
            $"worst bar shift {worstBar} px, {clock.ElapsedMilliseconds} ms");

        if (problems.Count > 0)
        {
            Directory.CreateDirectory(folder);
            File.Copy(result.Path, Path.Combine(folder, "export.gif"), overwrite: true);
            foreach (var (index, reference, exported) in evidence)
            {
                await reference.SavePngAsync(Path.Combine(folder, $"frame{index}-preview.png"));
                await exported.SavePngAsync(Path.Combine(folder, $"frame{index}-export.png"));
            }

            Assert.Fail(GoldenExport.Report($"{label} at {size.Width}x{size.Height}, preview against GIF:", problems, $"{folder} (the GIF, and the preview and export PNG of each failing frame)"));
        }

        File.Delete(result.Path);
    }

    // The nominal width:height of each shape; Match photos is 3:2 for this set (the 960x640 photo is the most common).
    private static (int Width, int Height) Shape(FrameAspect aspect) => aspect switch
    {
        FrameAspect.MatchPhotos => (3, 2),
        FrameAspect.Ratio16x9 => (16, 9),
        FrameAspect.Ratio9x16 => (9, 16),
        FrameAspect.Ratio1x1 => (1, 1),
        FrameAspect.Ratio4x5 => (4, 5),
        FrameAspect.Ratio4x3 => (4, 3),
        _ => throw new ArgumentOutOfRangeException(nameof(aspect), aspect, "A new frame shape: add its ratio here."),
    };

    // The preview's drawing: the frame's proxy as the preview loads it (built once, then read back from the cache's
    // JPEG), drawn by FrameRenderer.Draw with the snapshot's settings at the video size, onto a target first cleared
    // to grey so the bars are the renderer's.
    private static async Task<GoldenImage> PreviewDrawAsync(CanvasDevice device, PreviewImageSource preview, Frame frame, FrameSettings settings, PixelSize size)
    {
        var token = TestContext.Current.CancellationToken;
        var ensured = await preview.EnsureProxyAsync(frame, token);
        Assert.True(ensured == FrameProxyResult.Ready, $"{frame.SourcePath}: the preview could not build its proxy ({ensured}).");
        var loaded = await preview.LoadAsync(frame, FrameImageQuality.Proxy, token);
        using var bitmap = loaded.Image ?? throw new InvalidOperationException($"{frame.SourcePath}: the preview could not load it ({loaded.Failure}).");
        Assert.Equal(FrameImageQuality.Proxy, loaded.Quality);
        using var target = new CanvasRenderTarget(device, size.Width, size.Height, 96f);
        using (var session = target.CreateDrawingSession())
        {
            session.Clear(Color.FromArgb(255, 128, 128, 128));
            FrameRenderer.Draw(session, bitmap, settings, size);
        }

        return new GoldenImage(target.GetPixelBytes(), size.Width, size.Height);
    }

    // The bars on the middle row and column against the geometry (FrameRenderer.Layout) and against each other, and
    // every edge on three quiet rows and three quiet columns, matched within the tolerance; with the largest edge
    // and bar difference that was still within it, the noise the tolerances must stay well above.
    private static (List<string> Problems, double EdgeShift, int BarShift) Compare(GoldenImage reference, GoldenImage exported, PixelSize photo, PixelSize size, FitMode fit)
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
            return (problems, 0, 0);
        }

        var barShift = Math.Max(
            CheckBars(problems, $"left and right bars on row {rows[0]}", expectedSides, reference.BarsAlongRow(rows[0]), exported.BarsAlongRow(rows[0])),
            CheckBars(problems, $"top and bottom bars on column {columns[0]}", expectedEnds, reference.BarsAlongColumn(columns[0]), exported.BarsAlongColumn(columns[0])));
        var edgeShift = 0.0;
        foreach (var row in rows)
        {
            edgeShift = Math.Max(edgeShift, CheckEdges(problems, $"edges along row {row}", reference.EdgesAlongRow(row), exported.EdgesAlongRow(row)));
        }

        foreach (var column in columns)
        {
            edgeShift = Math.Max(edgeShift, CheckEdges(problems, $"edges down column {column}", reference.EdgesAlongColumn(column), exported.EdgesAlongColumn(column)));
        }

        return (problems, edgeShift, barShift);
    }

    // Returns the largest difference between export and preview bars, in pixels.
    private static int CheckBars(List<string> problems, string what, (double Start, double End) geometry, (int Start, int End) reference, (int Start, int End) exported)
    {
        static bool Near(double a, double b) => Math.Abs(a - b) <= BarTolerance;
        if (!Near(reference.Start, geometry.Start) || !Near(reference.End, geometry.End)
            || !Near(exported.Start, reference.Start) || !Near(exported.End, reference.End))
        {
            problems.Add($"{what} differ by more than {BarTolerance} px: geometry {geometry.Start:0.##} and {geometry.End:0.##} px, preview {reference.Start} and {reference.End} px, export {exported.Start} and {exported.End} px");
        }

        return Math.Max(Math.Abs(exported.Start - reference.Start), Math.Abs(exported.End - reference.End));
    }

    // Returns the largest distance between an edge and its match in the other image, among the matched ones.
    private static double CheckEdges(List<string> problems, string what, IReadOnlyList<Edge> reference, IReadOnlyList<Edge> exported)
    {
        static double? Nearest(Edge edge, IReadOnlyList<Edge> others) =>
            others.Where(other => other.Sign == edge.Sign).Select(other => (double?)Math.Abs(other.Position - edge.Position)).Min();

        var missing = reference.Where(edge => !(Nearest(edge, exported) <= EdgeTolerance)).ToList();
        var extra = exported.Where(edge => !(Nearest(edge, reference) <= EdgeTolerance)).ToList();
        if (missing.Count > 0 || extra.Count > 0)
        {
            problems.Add(
                $"{what} differ by more than {EdgeTolerance} px: preview [{string.Join(", ", reference)}], export [{string.Join(", ", exported)}]" +
                $" (not in the export: [{string.Join(", ", missing)}]; not in the preview: [{string.Join(", ", extra)}])");
        }

        return reference.Select(edge => Nearest(edge, exported))
            .Concat(exported.Select(edge => Nearest(edge, reference)))
            .OfType<double>()
            .Where(distance => distance <= EdgeTolerance)
            .DefaultIfEmpty()
            .Max();
    }

    /// <summary>
    /// Six photos of mixed shapes, written once per class as PNG: landscape 3:2 (twice, so Match photos takes 3:2),
    /// portrait 2:3, a square declared at 72 DPI, a 3:1 panorama, and a 2000x1333 landscape, larger than the proxy's
    /// 1280 px so the preview's copy is a non-integral downscale. Each is a grid of large blocks, bright and dark in
    /// a checkerboard (luma about 200 against about 105, never near the bars' black), with hues that do not repeat
    /// with the grid, so a crop off by a block period still shows. The preview's proxies go to a cache of their own.
    /// </summary>
    public sealed class MixedPhotos : IDisposable
    {
        private static readonly (byte R, byte G, byte B)[] _bright = [(250, 230, 90), (140, 240, 255), (255, 180, 220)];
        private static readonly (byte R, byte G, byte B)[] _dark = [(90, 90, 200), (200, 70, 70), (60, 140, 60)];

        private static readonly (string Name, int Width, int Height, int Columns, int Rows, double Dpi)[] _shapes =
        [
            ("landscape 3:2", 960, 640, 6, 4, 96),
            ("portrait 2:3", 640, 960, 4, 6, 96),
            ("square at 72 DPI", 720, 720, 5, 5, 72),
            ("panorama 3:1", 1200, 400, 9, 3, 96),
            ("landscape 3:2 again", 960, 640, 6, 4, 96),
            ("large landscape 2000x1333", 2000, 1333, 6, 4, 96),
        ];

        private readonly SemaphoreSlim _gate = new(1, 1);
        private (IReadOnlyList<(string Name, string Path, PixelSize Size)> Photos, ProxyCache Proxies)? _written;

        public string Folder { get; } = GoldenExport.StartClass(ClassName);

        /// <summary>The photos and the preview's proxy cache, written and opened on first use.</summary>
        public async Task<(IReadOnlyList<(string Name, string Path, PixelSize Size)> Photos, ProxyCache Proxies)> GetAsync()
        {
            await _gate.WaitAsync(TestContext.Current.CancellationToken);
            try
            {
                return _written ??= await WriteAsync();
            }
            finally
            {
                _gate.Release();
            }
        }

        public void Dispose()
        {
            _gate.Dispose();
            GoldenExport.EndClass(Folder);
        }

        private async Task<(IReadOnlyList<(string Name, string Path, PixelSize Size)>, ProxyCache)> WriteAsync()
        {
            var photos = new List<(string Name, string Path, PixelSize Size)>();
            for (var n = 0; n < _shapes.Length; n++)
            {
                var (name, width, height, columns, rows, dpi) = _shapes[n];
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
                await new GoldenImage(pixels, width, height).SavePngAsync(path, dpi);
                photos.Add((name, path, new PixelSize(width, height)));
            }

            var proxies = new ProxyCache(System.IO.Path.Combine(Folder, "proxies"));
            await proxies.OpenAsync();
            return (photos, proxies);
        }
    }
}
