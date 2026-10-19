using System.Text.RegularExpressions;

namespace TenLapse.Tests.Code;

/// <summary>
/// The callbacks the window framework calls outside XAML's error routing (#90): the handlers of the
/// <c>AppWindow</c> events <c>Changed</c>, <c>Closing</c> and <c>Destroying</c> (an exception from a <c>Changed</c>
/// handler leaves no trace at all), named or lambdas, and a <c>SystemBackdrop</c>'s overrides (the first connection runs
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

        Assert.True(offenders.Count == 0, "AppWindow event handlers and SystemBackdrop overrides must be one RunLogged call:\n" + string.Join('\n', offenders));
    }

    [Theory]
    [InlineData("private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args) { ApplyMinimumSize(Scale); }")]
    [InlineData("private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args) => ApplyMinimumSize(Scale);")]
    [InlineData("protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot root) { base.OnTargetConnected(target, root); _controller.AddSystemBackdropTarget(target); }")]
    [InlineData("protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot root) { Tint(); }")]
    [InlineData("protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target) { _errors.RunLogged(Release, Context); Tint(); }")]
    [InlineData("private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args) { if (_busy) { args.Cancel = true; } }")]
    [InlineData("AppWindow.Changed += (sender, args) => ApplyMinimumSize(Scale);")]
    [InlineData("AppWindow.Closing += (_, args) => { args.Cancel = Busy; };")]
    [InlineData("window.AppWindow.Destroying += (_, _) => Release();")]
    [InlineData("AppWindow.Changed += args => Follow(args);")]
    [InlineData("AppWindow.Closing += delegate { Release(); };")]
    [InlineData("AppWindow.Destroying += OnDestroying; private void OnDestroying(AppWindow sender, object args) { Release(); }")]
    [InlineData("AppWindow.Destroying += OnDestroyingElsewhere;")]
    public void TheScannerFlagsAnUnguardedFrameworkCallback(string source) => Assert.NotEmpty(FrameworkCallbackViolations(source));

    [Theory]
    [InlineData("private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args) => _errors.RunLogged(() => Follow(sender, args), ProjectText.WindowChangeFailed);")]
    [InlineData("protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot root) { base.OnTargetConnected(target, root); _errors.RunLogged(() => Connect(target, root), ProjectText.BackdropFailed); }")]
    [InlineData("protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot root)\n{\n    // A comment.\n    base.OnDefaultSystemBackdropConfigurationChanged(target, root);\n    _errors.RunLogged(Tint, ProjectText.BackdropFailed);\n}")]
    [InlineData("private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args) =>\n    _errors.RunLogged(() => args.Cancel = !_gate.Closing(), ExportText.CloseCheckFailed);")]
    [InlineData("AppWindow.Changed += (sender, args) => _errors.RunLogged(() => ApplyMinimumSize(Scale), ProjectText.WindowChangeFailed);")]
    [InlineData("AppWindow.Destroying += (_, _) => { _errors.RunLogged(Release, Context); };")]
    [InlineData("AppWindow.Destroying += OnDestroying; private void OnDestroying(AppWindow sender, object args) => _errors.RunLogged(Release, Context);")]
    [InlineData("AppWindow.Changed -= OnAppWindowChanged; AppWindow.Closing -= (_, _) => Release();")]
    public void TheScannerAllowsARunLoggedFrameworkCallback(string source) => Assert.Empty(FrameworkCallbackViolations(source));

    [Fact]
    public void EveryAnimationCompletionRunsLogged()
    {
        // #35: a Composition batch's or a Storyboard's Completed is raised by the compositor, outside XAML's error routing
        // (and a batch's may come off the UI thread), so its handler is one RunLogged call or one TryEnqueueGuarded call
        // (which runs the work inside RunLogged on the UI thread).
        var offenders = Sources()
            .SelectMany(file => CompletionViolations(File.ReadAllText(file)).Select(hit => $"{Relative(file)}: {hit}"))
            .ToList();

        Assert.True(offenders.Count == 0, "Animation Completed handlers must be one RunLogged or TryEnqueueGuarded call:\n" + string.Join('\n', offenders));
    }

    [Theory]
    [InlineData("batch.Completed += (_, _) => Settle(leaving);")]
    [InlineData("batch.Completed += (_, _) => { _layer.Visibility = Visibility.Collapsed; };")]
    [InlineData("storyboard.Completed += OnArrived; private void OnArrived(object? sender, object e) { Settle(); }")]
    [InlineData("batch.Completed += OnFaded;")]
    [InlineData("batch.Completed += (_, _) => { _dispatcher.TryEnqueueGuarded(_log, Clear, Context); Clear(); };")]
    public void TheScannerFlagsAnUnguardedCompletion(string source) => Assert.NotEmpty(CompletionViolations(source));

    [Theory]
    [InlineData("batch.Completed += (_, _) => _dispatcher.TryEnqueueGuarded(_log, () => Clear(generation), ProjectText.MotionFailed);")]
    [InlineData("batch.Completed += (_, _) => _dispatcher.TryEnqueueGuarded(\n    _log,\n    () =>\n    {\n        Settle(leaving);\n    },\n    ExportText.PanelFailed);")]
    [InlineData("storyboard.Completed += (_, _) => _log.RunLogged(Settle, ProjectText.MotionFailed);")]
    [InlineData("storyboard.Completed += OnArrived; private void OnArrived(object? sender, object e) => _log.RunLogged(Settle, Context);")]
    [InlineData("batch.Completed -= OnFaded;")]
    public void TheScannerAllowsAGuardedCompletion(string source) => Assert.Empty(CompletionViolations(source));

    private static IEnumerable<string> CompletionViolations(string source)
    {
        var code = WithoutComments(source);
        foreach (Match subscription in CompletedSubscription().Matches(code))
        {
            var rest = code[(subscription.Index + subscription.Length)..];
            var lambda = LambdaHead().Match(rest);
            var handler = HandlerName().Match(rest);
            var declaration = handler.Success ? Regex.Match(code, $@"\bvoid\s+{handler.Groups[1].Value}\s*\([^)]*\)") : Match.Empty;
            var ok = lambda.Success
                ? IsOneGuardedCall(Body(rest, lambda.Length))
                : declaration.Success && IsOneGuardedCall(Body(code, declaration.Index + declaration.Length));
            if (!ok)
            {
                yield return Line(subscription.Value + rest[..Math.Min(rest.Length, 40)].Split('\n')[0]);
            }
        }
    }

    // A body that is one RunLogged or one TryEnqueueGuarded call.
    private static bool IsOneGuardedCall(string body)
    {
        body = body.Trim();
        var call = GuardedCallHead().Match(body);
        var close = call.Success ? Closing(body, call.Index + call.Length - 1) : -1;
        return close >= 0 && body[(close + 1)..].Trim() == ";";
    }

    // A handler added to an animation's (a batch's, a Storyboard's) Completed.
    [GeneratedRegex(@"\b\w+\s*\.\s*Completed\s*\+=\s*")]
    private static partial Regex CompletedSubscription();

    [GeneratedRegex(@"^(?:this\.)?\w+\.(?:RunLogged|TryEnqueueGuarded)\s*\(")]
    private static partial Regex GuardedCallHead();

    private static IEnumerable<string> FrameworkCallbackViolations(string source)
    {
        var code = WithoutComments(source);
        foreach (Match method in FrameworkCallback().Matches(code))
        {
            if (!IsOneRunLoggedCall(Body(code, method.Index + method.Length)))
            {
                yield return Line(method.Value);
            }
        }

        // A subscription: a lambda's body is checked here, a named handler's where it is declared in the same file (a
        // Destroying handler's arguments are a bare object, so no signature gives it away). Anything else is flagged.
        foreach (Match subscription in AppWindowSubscription().Matches(code))
        {
            var rest = code[(subscription.Index + subscription.Length)..];
            var lambda = LambdaHead().Match(rest);
            var handler = HandlerName().Match(rest);
            var declaration = handler.Success ? Regex.Match(code, $@"\bvoid\s+{handler.Groups[1].Value}\s*\([^)]*\)") : Match.Empty;
            var ok = lambda.Success
                ? IsOneRunLoggedCall(Body(rest, lambda.Length))
                : declaration.Success && IsOneRunLoggedCall(Body(code, declaration.Index + declaration.Length));
            if (!ok)
            {
                yield return Line(subscription.Value + rest[..Math.Min(rest.Length, 40)].Split('\n')[0]);
            }
        }
    }

    // A body that is one RunLogged call, after a base call (for an override) if there is one.
    private static bool IsOneRunLoggedCall(string body)
    {
        body = body.Trim();
        if (BaseCall().Match(body) is { Success: true } baseCall)
        {
            body = body[baseCall.Length..].Trim();
        }

        var call = RunLoggedCall().Match(body);
        var close = call.Success ? Closing(body, call.Index + call.Length - 1) : -1;
        return close >= 0 && body[(close + 1)..].Trim() == ";";
    }

    // A handler taking AppWindowChangedEventArgs or AppWindowClosingEventArgs, or one of the SystemBackdrop overrides.
    [GeneratedRegex(@"\bvoid\s+\w+\s*\([^)]*\bAppWindow(?:Changed|Closing)EventArgs\s+\w+\s*\)|\boverride\s+void\s+(?:OnTargetConnected|OnTargetDisconnected|OnDefaultSystemBackdropConfigurationChanged)\s*\([^)]*\)")]
    private static partial Regex FrameworkCallback();

    // A handler added to one of AppWindow's events (a removal is not a handler).
    [GeneratedRegex(@"\bAppWindow\s*\.\s*(?:Changed|Closing|Destroying)\s*\+=\s*")]
    private static partial Regex AppWindowSubscription();

    // A lambda's parameters, up to its arrow: "(sender, args) " or "args ".
    [GeneratedRegex(@"^(?:\([^()]*\)|\w+)\s*(?==>)")]
    private static partial Regex LambdaHead();

    // A method group: "OnClosing;".
    [GeneratedRegex(@"^(\w+)\s*;")]
    private static partial Regex HandlerName();

    [GeneratedRegex(@"^base\.\w+\s*\([^;]*\)\s*;")]
    private static partial Regex BaseCall();

    [GeneratedRegex(@"^(?:this\.)?\w+\.RunLogged\s*\(")]
    private static partial Regex RunLoggedCall();
}
