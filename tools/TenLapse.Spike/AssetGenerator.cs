using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Svg;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;
using Windows.UI;

namespace TenLapse.Spike;

/// <summary>
/// #37: renders every icon, tile, logo and splash asset of the app from the vector sources in
/// src/TenLapse.App/Assets/Source with Win2D's SVG renderer, and writes the .ico. Deterministic: the same
/// sources always give the same files. See Assets/Source/README.md.
/// </summary>
internal static partial class AssetGenerator
{
    // The Windows scale factors the manifest assets come in.
    private static readonly int[] _scales = [100, 125, 150, 200, 400];

    // Square44x44Logo target sizes. Each comes plain, altform-unplated and altform-lightunplated.
    private static readonly int[] _targetSizes = [16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256];

    // The sizes that have a drawing on their own pixel grid (TenLapse-NN.svg); every other size uses the 48 grid master.
    private static readonly int[] _gridSizes = [16, 20, 24, 32];

    private const string FullView = "0 0 48 48";    // The master with its 4 px padding: the plated forms.
    private const string TightView = "3 3 42 42";   // The tile fills 95%: the unplated forms and the .ico.
    private const string TileView = "4 4 40 40";    // The bare tile, for the tiles and the splash.

    // The splash's background colour is declared in the manifest (SplashScreen BackgroundColor); the PNGs are transparent.
    private const string SplashBackground = "#1F1E1D";

    [GeneratedRegex(@"<\?xml.*?\?>|<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Prolog();

    [GeneratedRegex("<svg [^>]*>")]
    private static partial Regex RootTag();

    // The 48 grid master, and the drawings of the sizes that have their own pixel grid, by size.
    private sealed record Sources(string Master, Dictionary<int, string> Small);

    private sealed record Asset(string File, int Width, int Height, string? View, double TileFraction);

    public static async Task<int> RunAsync(Options options)
    {
        var root = FindRepositoryRoot();
        var sourceDirectory = options.Text("source", Path.Combine(root, "src", "TenLapse.App", "Assets", "Source"));
        var outputDirectory = options.Text("out", Path.Combine(root, "src", "TenLapse.App", "Assets"));
        var master = await File.ReadAllTextAsync(Path.Combine(sourceDirectory, "TenLapse.svg"));
        var sources = new Sources(master, new Dictionary<int, string>
        {
            [16] = await File.ReadAllTextAsync(Path.Combine(sourceDirectory, "TenLapse-16.svg")),
            [20] = await File.ReadAllTextAsync(Path.Combine(sourceDirectory, "TenLapse-20.svg")),
            [24] = await File.ReadAllTextAsync(Path.Combine(sourceDirectory, "TenLapse-24.svg")),
            [32] = await File.ReadAllTextAsync(Path.Combine(sourceDirectory, "TenLapse-32.svg")),
        });

        var device = CanvasDevice.GetSharedDevice();
        var assets = Plan();
        foreach (var asset in assets)
        {
            using var image = Render(device, sources, asset);
            await image.SaveAsync(Path.Combine(outputDirectory, asset.File), CanvasBitmapFileFormat.Png);
        }

        var icon = Path.Combine(outputDirectory, "TenLapse.ico");
        await WriteIconAsync(device, sources, icon);
        Console.WriteLine($"assets: {assets.Count} PNGs and TenLapse.ico written to {outputDirectory}");

        var sheet = options.Text("contact-sheet", "");
        if (sheet.Length > 0)
        {
            await WriteContactSheetAsync(device, outputDirectory, sheet);
            Console.WriteLine($"assets: contact sheet {sheet}");
        }

        return 0;
    }

    private static List<Asset> Plan()
    {
        var assets = new List<Asset>();
        foreach (var scale in _scales)
        {
            assets.Add(Square($"Square44x44Logo.scale-{scale}.png", Px(44, scale), unplated: false));
            assets.Add(Tile($"Square71x71Logo.scale-{scale}.png", Px(71, scale), Px(71, scale), 0.62));
            assets.Add(Tile($"Square150x150Logo.scale-{scale}.png", Px(150, scale), Px(150, scale), 0.62));
            assets.Add(Tile($"Wide310x150Logo.scale-{scale}.png", Px(310, scale), Px(150, scale), 0.62));
            assets.Add(Tile($"Square310x310Logo.scale-{scale}.png", Px(310, scale), Px(310, scale), 0.62));
            assets.Add(Tile($"SplashScreen.scale-{scale}.png", Px(620, scale), Px(300, scale), 0.42));
            assets.Add(Square($"StoreLogo.scale-{scale}.png", Px(50, scale), unplated: false));
        }

        foreach (var size in _targetSizes)
        {
            assets.Add(Square($"Square44x44Logo.targetsize-{size}.png", size, unplated: false));
            assets.Add(Square($"Square44x44Logo.targetsize-{size}_altform-unplated.png", size, unplated: true));
            assets.Add(Square($"Square44x44Logo.targetsize-{size}_altform-lightunplated.png", size, unplated: true));
        }

        return assets;
    }

    // Round half up, like the Windows scale tables (71 at 125% is 89, at 150% 107).
    private static int Px(int size, int scale) => (int)Math.Floor((size * scale / 100.0) + 0.5);

    private static Asset Square(string file, int size, bool unplated) =>
        new(file, size, size, _gridSizes.Contains(size) ? null : unplated ? TightView : FullView, 1);

    private static Asset Tile(string file, int width, int height, double fraction) => new(file, width, height, TileView, fraction);

    private static CanvasRenderTarget Render(CanvasDevice device, Sources sources, Asset asset)
    {
        var target = new CanvasRenderTarget(device, asset.Width, asset.Height, 96);
        using var session = target.CreateDrawingSession();
        session.Clear(Color.FromArgb(0, 0, 0, 0));

        var side = (float)Math.Round(Math.Min(asset.Width, asset.Height) * asset.TileFraction);
        var source = asset.View is null ? sources.Small[asset.Width] : sources.Master;
        var view = asset.View ?? $"0 0 {asset.Width} {asset.Width}";
        using var document = Load(device, source, view, side);
        session.Transform = Matrix3x2.CreateTranslation((float)Math.Round((asset.Width - side) / 2), (float)Math.Round((asset.Height - side) / 2));
        session.DrawSvg(document, new Size(side, side));
        return target;
    }

    private static CanvasSvgDocument Load(CanvasDevice device, string source, string view, float side)
    {
        var size = side.ToString("0.###", CultureInfo.InvariantCulture);
        // The SVG parser of Direct2D takes neither the XML declaration nor comments.
        var xml = Prolog().Replace(source, string.Empty);
        xml = RootTag().Replace(xml,$"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="{view}" width="{size}" height="{size}">""", 1);
        // A leading blank line is a parse error, too.
        xml = xml.Trim();
        return CanvasSvgDocument.LoadFromXml(device, xml);
    }

    // A Vista-style .ico: a directory of PNG images, no BMP data.
    private static async Task WriteIconAsync(CanvasDevice device, Sources sources, string path)
    {
        int[] sizes = [16, 24, 32, 48, 256];
        var images = new List<byte[]>();
        foreach (var size in sizes)
        {
            using var image = Render(device, sources, Square("ico", size, unplated: true));
            using var stream = new MemoryStream();
            await image.SaveAsync(stream.AsRandomAccessStream(), CanvasBitmapFileFormat.Png);
            images.Add(stream.ToArray());
        }

        using var file = File.Create(path);
        using var writer = new BinaryWriter(file);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)sizes.Length);
        var offset = 6 + (16 * sizes.Length);
        for (var i = 0; i < sizes.Length; i++)
        {
            writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
            writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(images[i].Length);
            writer.Write(offset);
            offset += images[i].Length;
        }

        foreach (var image in images)
        {
            writer.Write(image);
        }
    }

    // One PNG to look at: the small sizes 1:1 and magnified on a light and a dark taskbar, then the tiles and the splash.
    private static async Task WriteContactSheetAsync(CanvasDevice device, string assets, string path)
    {
        int[] sizes = [16, 20, 24, 30, 32, 36, 40, 48, 64, 96];
        const int Width = 1500;
        const int Height = 1650;
        using var sheet = new CanvasRenderTarget(device, Width, Height, 96);
        using var session = sheet.CreateDrawingSession();
        session.Clear(Color.FromArgb(255, 80, 80, 80));
        using var label = new CanvasTextFormat { FontSize = 12, FontFamily = "Segoe UI" };
        var white = Color.FromArgb(255, 235, 235, 235);

        async Task<CanvasBitmap> Open(string file) => await CanvasBitmap.LoadAsync(device, Path.Combine(assets, file));

        float y = 20;
        foreach (var (name, background) in new[] { ("light taskbar", Color.FromArgb(255, 238, 238, 236)), ("dark taskbar", Color.FromArgb(255, 28, 27, 26)) })
        {
            session.FillRectangle(20, y, Width - 40, 430, background);
            session.DrawText($"{name}: plated targetsize files at 1:1, then 16, 24 and 32 at 8x (nearest neighbour)", 30, y + 8, name.StartsWith('l') ? Color.FromArgb(255, 28, 27, 26) : white, label);
            var x = 40f;
            foreach (var size in sizes)
            {
                using var bitmap = await Open($"Square44x44Logo.targetsize-{size}.png");
                session.DrawImage(bitmap, x, y + 40);
                x += size + 24;
            }

            x = 40;
            foreach (var size in new[] { 16, 24, 32 })
            {
                using var bitmap = await Open($"Square44x44Logo.targetsize-{size}.png");
                var scaled = size * 8;
                session.DrawImage(bitmap, new Rect(x, y + 150, scaled, scaled), new Rect(0, 0, size, size), 1f, CanvasImageInterpolation.NearestNeighbor);
                x += scaled + 30;
            }

            y += 450;
        }

        session.DrawText("Square71, Square150, Wide310, Square310 and StoreLogo at scale-100 on a mid grey; the SplashScreen at scale-100 on its manifest background", 30, y, white, label);
        y += 24;
        var cursor = 30f;
        foreach (var file in new[] { "Square71x71Logo.scale-100.png", "Square150x150Logo.scale-100.png", "Wide310x150Logo.scale-100.png", "StoreLogo.scale-100.png" })
        {
            using var bitmap = await Open(file);
            session.DrawImage(bitmap, cursor, y);
            cursor += (float)bitmap.Size.Width + 24;
        }

        using (var tile = await Open("Square310x310Logo.scale-100.png"))
        {
            session.DrawImage(tile, cursor, y);
        }

        y += 330;
        using var splash = await Open("SplashScreen.scale-100.png");
        session.FillRectangle(30, y, (float)splash.Size.Width, (float)splash.Size.Height, ParseColor(SplashBackground));
        session.DrawImage(splash, 30, y);

        session.Dispose();
        await sheet.SaveAsync(path, CanvasBitmapFileFormat.Png);
    }

    private static Color ParseColor(string hex) =>
        Color.FromArgb(255, Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TenLapse.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new ArgumentException("Run it from inside the repository, or pass --source and --out.");
    }
}
