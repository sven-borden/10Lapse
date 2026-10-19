using System.Text.RegularExpressions;

namespace TenLapse.Tests.Code;

/// <summary>
/// The callbacks the window framework calls outside XAML's error routing (#90): an <c>AppWindow.Changed</c> handler
/// (an exception from it leaves no trace at all) and a <c>SystemBackdrop</c>'s overrides (the first connection runs
/// inside the window's <c>SystemBackdrop</c> setter, so a throw fails the start-up; the later calls have no managed
/// caller). Their bodies must be one <c>RunLogged</c> call, after the <c>base</c> call for an override.
/// </summary>
public sealed partial class ErrorPathRuleTests
{
    [Fact]
    public void EveryWindowFrameworkCallbackRunsLogged()
    {
        var offenders = Sources()
            .SelectMany(file => FrameworkCallbackViolations(File.ReadAllText(file)).Select(hit => $"{Relative(file)}: {hit}"))
            .ToList();

        Assert.True(offenders.Count == 0, "AppWindow.Changed handlers and SystemBackdrop overrides must be one RunLogged call:\n" + string.Join('\n', offenders));
    }

    [Theory]
    [InlineData("private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args) { ApplyMinimumSize(Scale); }")]
    [InlineData("private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args) => ApplyMinimumSize(Scale);")]
    [InlineData("protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot root) { base.OnTargetConnected(target, root); _controller.AddSystemBackdropTarget(target); }")]
    [InlineData("protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot root) { Tint(); }")]
    [InlineData("protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target) { _errors.RunLogged(Release, Context); Tint(); }")]
    public void TheScannerFlagsAnUnguardedFrameworkCallback(string source) => Assert.NotEmpty(FrameworkCallbackViolations(source));

    [Theory]
    [InlineData("private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args) => _errors.RunLogged(() => Follow(sender, args), ProjectText.WindowChangeFailed);")]
    [InlineData("protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot root) { base.OnTargetConnected(target, root); _errors.RunLogged(() => Connect(target, root), ProjectText.BackdropFailed); }")]
    [InlineData("protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot root)\n{\n    // A comment.\n    base.OnDefaultSystemBackdropConfigurationChanged(target, root);\n    _errors.RunLogged(Tint, ProjectText.BackdropFailed);\n}")]
    public void TheScannerAllowsARunLoggedFrameworkCallback(string source) => Assert.Empty(FrameworkCallbackViolations(source));

    private static IEnumerable<string> FrameworkCallbackViolations(string source)
    {
        var code = WithoutComments(source);
        foreach (Match method in FrameworkCallback().Matches(code))
        {
            var body = Body(code, method.Index + method.Length).Trim();
            if (BaseCall().Match(body) is { Success: true } baseCall)
            {
                body = body[baseCall.Length..].Trim();
            }

            var call = RunLoggedCall().Match(body);
            var close = call.Success ? Closing(body, call.Index + call.Length - 1) : -1;
            if (close < 0 || body[(close + 1)..].Trim() != ";")
            {
                yield return Line(method.Value);
            }
        }
    }

    // A handler taking AppWindowChangedEventArgs, or one of the SystemBackdrop overrides.
    [GeneratedRegex(@"\bvoid\s+\w+\s*\([^)]*\bAppWindowChangedEventArgs\s+\w+\s*\)|\boverride\s+void\s+(?:OnTargetConnected|OnTargetDisconnected|OnDefaultSystemBackdropConfigurationChanged)\s*\([^)]*\)")]
    private static partial Regex FrameworkCallback();

    [GeneratedRegex(@"^base\.\w+\s*\([^;]*\)\s*;")]
    private static partial Regex BaseCall();

    [GeneratedRegex(@"^(?:this\.)?\w+\.RunLogged\s*\(")]
    private static partial Regex RunLoggedCall();
}
