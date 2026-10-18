using System.Text.RegularExpressions;
using TenLapse.Tests.Design;

namespace TenLapse.Tests.Code;

/// <summary>
/// The code rules behind "never silent" (CLAUDE.md): no <c>async void</c> except an event handler that awaits one
/// guarded call, and every command built by <c>ErrorPath</c>, so no failure can reach WinUI unhandled.
/// </summary>
public sealed partial class ErrorPathRuleTests
{
    // Calls that never throw, so awaiting one is all an async void event handler may do: ErrorPath.GuardAsync, and
    // InfoBarQueue.RunActionAsync (it reports its action's failure itself; InfoBarPresenter forwards to it).
    private static readonly string[] _guards = ["GuardAsync", "RunActionAsync"];

    // The one file that may construct commands.
    private const string CommandFactory = "ErrorPath.cs";

    [Fact]
    public void NoAsyncVoidExceptGuardedEventHandlers()
    {
        var offenders = Sources()
            .SelectMany(file => AsyncVoidViolations(File.ReadAllText(file)).Select(hit => $"{Relative(file)}: {hit}"))
            .ToList();

        Assert.True(offenders.Count == 0, "async void outside a guarded event handler:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void EveryCommandIsBuiltByTheErrorPath()
    {
        var offenders = Sources()
            .Where(file => !Path.GetFileName(file).Equals(CommandFactory, StringComparison.OrdinalIgnoreCase))
            .SelectMany(file => CommandViolations(File.ReadAllText(file)).Select(hit => $"{Relative(file)}: {hit}"))
            .ToList();

        Assert.True(offenders.Count == 0, "Commands must come from ErrorPath (errors.AsyncCommand / errors.Command):\n" + string.Join('\n', offenders));
    }

    [Theory]
    [InlineData("private async void OnClick(object? sender, RoutedEventArgs e) => await _errors.GuardAsync(() => SaveAsync(), Context);")]
    [InlineData("private async void OnDrop(object sender, DragEventArgs e)\n{\n    await _errors.GuardAsync(() => ImportAsync(e), ProjectText.ImportFailed);\n}")]
    [InlineData("button.Click += async (_, _) => await RunActionAsync(ticket);")]
    [InlineData("button.Click += async (_, _) =>\n{\n    await _errors.GuardAsync(() => SaveAsync(), Context);\n};")]
    [InlineData("// private async void Old(object sender, EventArgs e) { await LoadAsync(); }")]
    [InlineData("/// <code>private async void OnDrop(object? sender, DragEventArgs e) => await LoadAsync();</code>")]
    public void TheScannerAllowsAGuardedEventHandler(string source) => Assert.Empty(AsyncVoidViolations(source));

    [Theory]
    [InlineData("private async void LoadAsync() { await Task.Delay(1); }")]
    [InlineData("public async void Start(int count) => await RunAsync(count);")]
    [InlineData("private async void OnClick(object? sender, RoutedEventArgs e) => await SaveAsync();")]
    [InlineData("private async void OnClick(object? sender, RoutedEventArgs e)\n{\n    await _errors.GuardAsync(() => SaveAsync(), Context);\n    Refresh();\n}")]
    [InlineData("private async void OnClick(object? sender, RoutedEventArgs e)\n{\n    Prepare();\n    await _errors.GuardAsync(() => SaveAsync(), Context);\n}")]
    [InlineData("button.Click += async (_, _) => await SaveAsync();")]
    [InlineData("button.Click += async (_, _) =>\n{\n    await SaveAsync();\n};")]
    [InlineData("private static async void Tick(object? state) => await _errors.GuardAsync(Work, Context);")]
    public void TheScannerFlagsEveryOtherAsyncVoid(string source) => Assert.NotEmpty(AsyncVoidViolations(source));

    [Theory]
    [InlineData("[RelayCommand]\nprivate async Task SaveAsync() { }")]
    [InlineData("SaveCommand = new AsyncRelayCommand(SaveAsync);")]
    [InlineData("SaveCommand = new RelayCommand(Save);")]
    [InlineData("SaveCommand = new AsyncRelayCommand<string>(SaveAsync);")]
    public void TheScannerFlagsACommandBuiltElsewhere(string source) => Assert.NotEmpty(CommandViolations(source));

    [Fact]
    public void TheScannerAllowsACommandFromTheErrorPath() =>
        Assert.Empty(CommandViolations("SaveCommand = errors.AsyncCommand(SaveAsync, ProjectText.SaveFailed);"));

    // Every C# file under src, build output excluded.
    private static IEnumerable<string> Sources()
    {
        var files = Directory.EnumerateFiles(Path.Combine(DesignFiles.Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.NotEmpty(files);
        return files;
    }

    private static string Relative(string file) => Path.GetRelativePath(DesignFiles.Root, file);

    private static IEnumerable<string> AsyncVoidViolations(string source)
    {
        var code = WithoutComments(source);
        foreach (Match method in AsyncVoidMethod().Matches(code))
        {
            var parameters = method.Groups["params"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var isEventHandler = parameters.Length == 2
                && SenderParameter().IsMatch(parameters[0])
                && parameters[1].Split(' ', StringSplitOptions.RemoveEmptyEntries) is [_, _];
            if (!isEventHandler || !IsOneGuardedAwait(Body(code, method.Index + method.Length)))
            {
                yield return Line(method.Value);
            }
        }

        foreach (Match lambda in AsyncLambdaHandler().Matches(code))
        {
            if (!IsOneGuardedAwait(Body(code, lambda.Index + lambda.Length)))
            {
                yield return Line(lambda.Value);
            }
        }
    }

    private static IEnumerable<string> CommandViolations(string source)
    {
        var code = WithoutComments(source);
        return CommandConstruction().Matches(code).Select(match => match.Value);
    }

    // The body after a signature: an expression body up to its semicolon, or a block up to its closing brace.
    private static string Body(string code, int start)
    {
        var rest = code[start..].TrimStart();
        if (rest.StartsWith("=>", StringComparison.Ordinal))
        {
            rest = rest[2..].TrimStart();
            if (!rest.StartsWith('{'))
            {
                var end = rest.IndexOf(';', StringComparison.Ordinal);
                return rest[..(end < 0 ? rest.Length : end + 1)].Trim();
            }
        }

        if (!rest.StartsWith('{'))
        {
            return rest;
        }

        var depth = 0;
        for (var i = 0; i < rest.Length; i++)
        {
            depth += rest[i] switch { '{' => 1, '}' => -1, _ => 0 };
            if (depth == 0)
            {
                return rest[1..i].Trim();
            }
        }

        return rest;
    }

    // Exactly one statement, "await <receiver>.Guard(...);" or "await Guard(...);", with nothing after it.
    private static bool IsOneGuardedAwait(string body)
    {
        var match = GuardedAwait().Match(body.Trim());
        return match.Success && _guards.Contains(match.Groups["guard"].Value, StringComparer.Ordinal);
    }

    private static string WithoutComments(string source) => LineComment().Replace(BlockComment().Replace(source, string.Empty), string.Empty);

    private static string Line(string text) => text.ReplaceLineEndings(" ").Trim();

    [GeneratedRegex(@"\basync\s+void\s+\w+\s*\((?<params>[^)]*)\)")]
    private static partial Regex AsyncVoidMethod();

    [GeneratedRegex(@"^(object\??)\s+\w+$")]
    private static partial Regex SenderParameter();

    [GeneratedRegex(@"\+=\s*async\s*(\([^)]*\)|\w+)\s*(?==>)")]
    private static partial Regex AsyncLambdaHandler();

    [GeneratedRegex(@"^await\s+(?:[\w.]+\.)?(?<guard>\w+)\s*\((?:[^;]|;(?!\s*\S))*\)\s*;$", RegexOptions.Singleline)]
    private static partial Regex GuardedAwait();

    [GeneratedRegex(@"\[RelayCommand\b[^\]]*\]|\bnew\s+(Async)?RelayCommand\b(<[^>]*>)?\s*\(")]
    private static partial Regex CommandConstruction();

    [GeneratedRegex(@"//[^\n]*")]
    private static partial Regex LineComment();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockComment();
}
