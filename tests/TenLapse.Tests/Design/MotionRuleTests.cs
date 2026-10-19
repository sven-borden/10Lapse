using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TenLapse.Tests.Design;

/// <summary>
/// Every animation's duration and curve comes from the motion tokens (#35), as every color comes from the color tokens
/// (<see cref="AmberRuleTests"/>, <see cref="LiteralColorTests"/>). The token files are Themes/Motion.xaml,
/// Themes/MotionTokens.cs and Presentation/Motion/MotionSpec.cs; anywhere else this test fails on a literal Duration,
/// KeyTime, BeginTime or KeySpline in XAML, an easing built in code, or a literal TimeSpan in a file that animates.
/// </summary>
public sealed partial class MotionRuleTests
{
    private static readonly string[] _tokenFiles =
    [
        Path.Combine("src", "TenLapse.App", "Themes", "Motion.xaml"),
        Path.Combine("src", "TenLapse.App", "Themes", "MotionTokens.cs"),
        Path.Combine("src", "TenLapse.Presentation", "Motion", "MotionSpec.cs"),
    ];

    [Fact]
    public void NoXamlOutsideTheTokenFilesWritesATimingOrACurve()
    {
        var offenders = Files("*.xaml")
            .SelectMany(file => XamlViolations(File.ReadAllText(file)).Select(hit => $"{Relative(file)}: {hit}"))
            .ToList();

        Assert.True(offenders.Count == 0, "Use the Motion*Duration and Motion*KeySpline resources (Themes/Motion.xaml):\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void NoCodeOutsideTheTokenFilesWritesATimingOrACurve()
    {
        var offenders = Files("*.cs")
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}Debugging{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(file => CodeViolations(File.ReadAllText(file)).Select(hit => $"{Relative(file)}: {hit}"))
            .ToList();

        Assert.True(offenders.Count == 0, "Plan the animation with MotionTokens.Plan(MotionCatalogue...) and take its Duration, Delay and Easing:\n" + string.Join('\n', offenders));
    }

    [Theory]
    [InlineData("<DoubleAnimation Duration=\"0:0:0.25\" />")]
    [InlineData("<SplineDoubleKeyFrame KeyTime=\"0:0:0.4\" Value=\"1\" KeySpline=\"0.16,1 0.3,1\" />")]
    [InlineData("<Storyboard BeginTime=\"0:0:0.2\" />")]
    [InlineData("<BrushTransition Duration=\"0:0:0.083\" />")]
    [InlineData("<DoubleAnimation><DoubleAnimation.EasingFunction><CubicEase /></DoubleAnimation.EasingFunction></DoubleAnimation>")]
    [InlineData("<SplineDoubleKeyFrame><SplineDoubleKeyFrame.KeySpline><KeySpline ControlPoint1=\"0,0\" ControlPoint2=\"1,1\" /></SplineDoubleKeyFrame.KeySpline></SplineDoubleKeyFrame>")]
    public void TheXamlScannerFlagsALiteral(string element) => Assert.NotEmpty(XamlViolations(Wrap(element)));

    [Theory]
    [InlineData("<DoubleAnimation Duration=\"{StaticResource MotionRevealDuration}\" />")]
    [InlineData("<SplineDoubleKeyFrame KeyTime=\"{StaticResource MotionSceneDuration}\" KeySpline=\"{StaticResource MotionEnterKeySpline}\" />")]
    [InlineData("<DiscreteDoubleKeyFrame KeyTime=\"0:0:0\" Value=\"0\" />")]
    [InlineData("<ContentPresenter BackgroundTransition=\"{StaticResource MotionTapBrushTransition}\" />")]
    public void TheXamlScannerAllowsTokens(string element) => Assert.Empty(XamlViolations(Wrap(element)));

    [Theory]
    [InlineData("visual.StartAnimation(\"Opacity\", fade); fade.Duration = TimeSpan.FromMilliseconds(250);")]
    [InlineData("var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation); await Task.Delay(TimeSpan.FromMilliseconds(170));")]
    [InlineData("var storyboard = new Storyboard(); animation.BeginTime = TimeSpan.FromSeconds(0.2);")]
    [InlineData("animation.KeyFrames.Add(new SplineDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(500)) });")]
    [InlineData("var spline = new KeySpline { ControlPoint1 = new(0.16, 1) };")]
    [InlineData("var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));")]
    [InlineData("var easing = compositor.CreateLinearEasingFunction();")]
    [InlineData("animation.EasingFunction = new CubicEase();")]
    [InlineData("button.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(167) };")]
    [InlineData("var wait = new TimeSpan(0, 0, 1); element.StartAnimation(group);")]
    public void TheCodeScannerFlagsALiteral(string source) => Assert.NotEmpty(CodeViolations(source));

    [Theory]
    [InlineData("var step = MotionTokens.Plan(MotionCatalogue.PanelIn); fade.Duration = step.Duration; visual.StartAnimation(\"Opacity\", fade);")]
    [InlineData("var easing = MotionTokens.CreateEasing(compositor, step.Easing); visual.StartAnimation(\"Opacity\", fade);")]
    [InlineData("_timer.Interval = TimeSpan.FromMilliseconds(150); visual.StartAnimation(\"Opacity\", fade);")]
    [InlineData("private static readonly TimeSpan _retry = TimeSpan.FromSeconds(30);")]
    [InlineData("// fade.Duration = TimeSpan.FromMilliseconds(250); in a comment\nvisual.StartAnimation(\"Opacity\", fade);")]
    public void TheCodeScannerAllowsTokensAndTimersThatDoNotAnimate(string source) => Assert.Empty(CodeViolations(source));

    private static IEnumerable<string> XamlViolations(string xaml)
    {
        var document = XDocument.Parse(xaml);
        foreach (var element in document.Descendants())
        {
            foreach (var attribute in element.Attributes().Where(attribute => _timingAttributes.Contains(attribute.Name.LocalName)))
            {
                var value = attribute.Value.Trim();
                if (!value.StartsWith('{') && !IsZero(value))
                {
                    yield return $"<{element.Name.LocalName} {attribute.Name.LocalName}=\"{value}\">";
                }
            }

            var name = element.Name.LocalName;
            if (name == "KeySpline" || (name.EndsWith("Ease", StringComparison.Ordinal) && !name.Contains('.', StringComparison.Ordinal)))
            {
                yield return $"<{name}>";
            }
        }
    }

    private static readonly HashSet<string> _timingAttributes = new(StringComparer.Ordinal) { "Duration", "KeyTime", "BeginTime", "KeySpline", "Delay" };

    private static bool IsZero(string value) => value is "0" or "0:0:0" or "00:00:00";

    private static IEnumerable<string> CodeViolations(string source)
    {
        var code = WithoutComments(source);
        foreach (Match match in BuiltCurve().Matches(code))
        {
            yield return match.Value;
        }

        // A file that animates may not write a time of its own; a timer's pace (Interval) is not an animation's timing.
        var animates = Animates().IsMatch(code);
        foreach (var line in code.Split('\n'))
        {
            if (line.Contains(".Interval", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in LiteralTime().Matches(line))
            {
                if (animates || AnimationTimingAssignment().IsMatch(line))
                {
                    yield return line.Trim();
                }
            }
        }
    }

    private static IEnumerable<string> Files(string pattern) =>
        Directory.EnumerateFiles(Path.Combine(DesignFiles.Root, "src"), pattern, SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file => !_tokenFiles.Contains(Relative(file), StringComparer.OrdinalIgnoreCase));

    private static string Relative(string file) => Path.GetRelativePath(DesignFiles.Root, file);

    private static string Wrap(string element) =>
        $"<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">{element}</Grid>";

    private static string WithoutComments(string source) => LineComment().Replace(BlockComment().Replace(source, string.Empty), string.Empty);

    // An easing made in code: a Composition easing function, a Storyboard KeySpline, or a XAML easing class.
    [GeneratedRegex(@"\bCreate\w*EasingFunction\s*\(|\bnew\s+KeySpline\b|\bnew\s+(?:Cubic|Exponential|Sine|Quadratic|Quartic|Quintic|Back|Bounce|Elastic|Circle|Power)Ease\b")]
    private static partial Regex BuiltCurve();

    // What makes a file one that animates.
    [GeneratedRegex(@"\bStartAnimation\b|\bStoryboard\b|KeyFrameAnimation\b|\bKeyFrames\.Add\b|\b(?:Scalar|Vector3|Brush)Transition\b|\bCreateScopedBatch\b|\bSetImplicit(?:Show|Hide)Animation\b|\bConnectedAnimation\b|\bOpacityTransition\b")]
    private static partial Regex Animates();

    // A time written as a number.
    [GeneratedRegex(@"\bTimeSpan\.From(?:Milliseconds|Seconds|Minutes|Ticks)\s*\(\s*[0-9]|\bnew\s+TimeSpan\s*\(")]
    private static partial Regex LiteralTime();

    // An animation's own timing property, wherever it is.
    [GeneratedRegex(@"\b(?:Duration|DelayTime|BeginTime|KeyTime)\s*=")]
    private static partial Regex AnimationTimingAssignment();

    [GeneratedRegex(@"//[^\n]*")]
    private static partial Regex LineComment();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockComment();
}
