using System.Drawing;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace TenLapse.Tests.Design;

/// <summary>Colors come only from the token dictionaries: no literal color in any XAML outside Themes.</summary>
public sealed partial class LiteralColorTests
{
    private static readonly HashSet<string> _namedColors = Enum.GetNames<KnownColor>()
        .Where(name => !name.Equals("Transparent", StringComparison.OrdinalIgnoreCase))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void NoXamlOutsideThemesHasALiteralColor()
    {
        var themes = DesignFiles.ThemesFolder + Path.DirectorySeparatorChar;
        var files = Directory.EnumerateFiles(Path.Combine(DesignFiles.Root, "src"), "*.xaml", SearchOption.AllDirectories)
            .Where(file => !file.StartsWith(themes, StringComparison.OrdinalIgnoreCase))
            .Where(file => !IsBuildOutput(file))
            .ToList();

        Assert.NotEmpty(files);
        var offenders = files
            .SelectMany(file => FindLiteralColors(File.ReadAllText(file)).Select(hit => $"{Path.GetRelativePath(DesignFiles.Root, file)}: {hit}"))
            .ToList();

        Assert.True(offenders.Count == 0, "Literal colors outside src/TenLapse.App/Themes:\n" + string.Join('\n', offenders));
    }

    [Theory]
    [InlineData("<Border Background=\"#FF0000\" />")]
    [InlineData("<Border Background=\"#abc\" />")]
    [InlineData("<Border Background=\"#80FFFFFF\" />")]
    [InlineData("<SolidColorBrush Color=\"White\" />")]
    [InlineData("<Border BorderBrush=\"red\" />")]
    [InlineData("<Color x:Key=\"X\" xmlns:x=\"x\">#112233</Color>")]
    [InlineData("<Rectangle Fill=\"Black\" />")]
    public void TheScannerFlagsALiteralColor(string xaml)
    {
        Assert.NotEmpty(FindLiteralColors(xaml));
    }

    [Theory]
    [InlineData("<Border Background=\"{ThemeResource LayerBrush}\" />")]
    [InlineData("<Border Background=\"Transparent\" />")]
    [InlineData("<Border><!-- #FF0000 in a comment --></Border>")]
    [InlineData("<TextBlock Text=\"Red\" />")]
    [InlineData("<TextBlock Text=\"Item #1\" />")]
    public void TheScannerAllowsTokensAndPlainText(string xaml)
    {
        Assert.Empty(FindLiteralColors(xaml));
    }

    /// <summary>Hex colors in any attribute or element text, and named colors in color-typed attributes. Comments are ignored; Transparent is allowed.</summary>
    internal static List<string> FindLiteralColors(string xaml)
    {
        var document = XDocument.Parse(xaml, LoadOptions.SetLineInfo);
        var hits = new List<string>();
        foreach (var element in document.Descendants())
        {
            foreach (var attribute in element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration))
            {
                var value = attribute.Value.Trim();
                if (HexColor().IsMatch(value) || (ColorProperty().IsMatch(attribute.Name.LocalName) && _namedColors.Contains(value)))
                {
                    hits.Add($"{element.Name.LocalName}.{attribute.Name.LocalName}=\"{value}\"{Line(element)}");
                }
            }

            var text = element.Nodes().OfType<XText>().Select(node => node.Value.Trim()).FirstOrDefault(value => value.Length > 0);
            if (text is not null && HexColor().IsMatch(text))
            {
                hits.Add($"{element.Name.LocalName} text \"{text}\"{Line(element)}");
            }
        }

        return hits;
    }

    private static string Line(XElement element) =>
        element is IXmlLineInfo info && info.HasLineInfo() ? $" (line {info.LineNumber})" : string.Empty;

    private static bool IsBuildOutput(string file) =>
        file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("^#([0-9A-Fa-f]{3,4}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")]
    private static partial Regex HexColor();

    [GeneratedRegex("(Color|Background|Foreground|Fill|Stroke|Brush)$")]
    private static partial Regex ColorProperty();
}
