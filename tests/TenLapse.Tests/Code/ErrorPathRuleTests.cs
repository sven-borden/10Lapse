using System.Text.RegularExpressions;
using TenLapse.Tests.Design;

namespace TenLapse.Tests.Code;

/// <summary>
/// The code rules behind "never silent" (CLAUDE.md): no <c>async void</c> except an event handler that awaits one
/// guarded call, every command built by <c>ErrorPath</c>, and every dispatcher callback guarded, so no failure can
/// reach WinUI unhandled.
/// </summary>
public sealed partial class ErrorPathRuleTests
{
    // The one file that may construct commands.
    private const string CommandFactory = "ErrorPath.cs";

    // The one file that may call DispatcherQueue.TryEnqueue: its TryEnqueueGuarded runs every callback through
    // ErrorPath.RunLogged, because a dispatcher callback that throws ends the process and reaches no handler.
    private const string DispatcherHelper = "DispatcherQueueExtensions.cs";

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

    [Fact]
    public void EveryDispatcherCallbackGoesThroughTheGuardedHelper()
    {
        var offenders = Sources()
            .Where(file => !Path.GetFileName(file).Equals(DispatcherHelper, StringComparison.OrdinalIgnoreCase))
            .SelectMany(file => DispatcherViolations(File.ReadAllText(file)).Select(hit => $"{Relative(file)}: {hit}"))
            .ToList();

        Assert.True(offenders.Count == 0, "Enqueue with dispatcher.TryEnqueueGuarded(errors, action, context), never TryEnqueue:\n" + string.Join('\n', offenders));
    }

    [Theory]
    [InlineData("_dispatcher.TryEnqueue(Sync);")]
    [InlineData("DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => Refresh());")]
    [InlineData("queue.TryEnqueue (() =>\n{\n    Refresh();\n});")]
    public void TheScannerFlagsARawTryEnqueue(string source) => Assert.NotEmpty(DispatcherViolations(source));

    [Theory]
    [InlineData("_dispatcher.TryEnqueueGuarded(_errors, Sync, ProjectText.InfoBarSyncFailed);")]
    [InlineData("// _dispatcher.TryEnqueue(Sync);")]
    public void TheScannerAllowsTheGuardedHelper(string source) => Assert.Empty(DispatcherViolations(source));

    [Theory]
    [InlineData("private async void OnClick(object? sender, RoutedEventArgs e) => await _errors.GuardAsync(() => SaveAsync(), Context);")]
    [InlineData("private async void OnDrop(object sender, DragEventArgs e)\n{\n    await _errors.GuardAsync(() => ImportAsync(e), ProjectText.ImportFailed);\n}")]
    [InlineData("button.Click += async (_, _) => await _queue.RunActionAsync(ticket);")]
    [InlineData("button.Click += async (_, _) =>\n{\n    await _errors.GuardAsync(() => SaveAsync(), Context);\n};")]
    [InlineData("// private async void Old(object sender, EventArgs e) { await LoadAsync(); }")]
    [InlineData("/// <code>private async void OnDrop(object? sender, DragEventArgs e) => await LoadAsync();</code>")]
    // A typed sender: any type for the first of the two parameters.
    [InlineData("private async void OnPrimary(ContentDialog sender, ContentDialogButtonClickEventArgs args) => await _errors.GuardAsync(() => SaveAsync(), Context);")]
    [InlineData("private async void OnLoaded(FrameworkElement? sender, RoutedEventArgs e) => await this._errors.GuardAsync(LoadAsync, Context);")]
    // A block lambda inside the guarded call, in a block body and in an expression body.
    [InlineData("private async void OnDrop(object? sender, DragEventArgs e)\n{\n    await _errors.GuardAsync(async () =>\n    {\n        var items = await e.DataView.GetStorageItemsAsync();\n        await ImportAsync(items);\n    }, ProjectText.ImportFailed);\n}")]
    [InlineData("private async void OnDrop(object? sender, DragEventArgs e) => await _errors.GuardAsync(async () => { await PrepareAsync(); Refresh(); }, Context);")]
    [InlineData("private async void OnClick(object sender, RoutedEventArgs e) => await _errors.GuardAsync(() => Log(\"a; b) {\"), Context);")]
    [InlineData("private async void OnClick(object sender, RoutedEventArgs e) => await ErrorPath.GuardAsync(_errors, SaveAsync, Context);")]
    // Not async void: Func<Task> lambdas, and an Action that only starts a guarded task.
    [InlineData("_ = Task.Run(async () => await LoadAsync());")]
    [InlineData("dispatcher.TryEnqueueGuarded(_errors, () => _ = _errors.GuardAsync(async () => await LoadAsync(), Context), Context);")]
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
    // Generic methods and explicit interface methods.
    [InlineData("private async void Load<T>(T item) => await SaveAsync(item);")]
    [InlineData("private async void OnChanged<T>(object sender, ValueChangedEventArgs<T> e) => await SaveAsync();")]
    [InlineData("async void IHandler.Handle(int count) => await SaveAsync(count);")]
    // The guarded call must go through the error path (or the InfoBar queue), nothing else of that name.
    [InlineData("private async void OnClick(object sender, RoutedEventArgs e) => await _helper.GuardAsync(SaveAsync, Context);")]
    [InlineData("private async void OnClick(object sender, RoutedEventArgs e) => await GuardAsync(SaveAsync, Context);")]
    [InlineData("button.Click += async (_, _) => await RunActionAsync(ticket);")]
    [InlineData("private async void OnClick(object sender, RoutedEventArgs e) => await _errors.GuardAsync(SaveAsync, Context).ConfigureAwait(true);")]
    [InlineData("private async void OnClick(object sender, RoutedEventArgs e) => await _errors.RunActionAsync(ticket);")]
    // Async lambdas and anonymous methods that become an Action: no event and no guard make them safe.
    [InlineData("_dispatcher.TryEnqueue(async () => await LoadAsync());")]
    [InlineData("dispatcher.TryEnqueueGuarded(_errors, async () => await _errors.GuardAsync(LoadAsync, Context), Context);")]
    [InlineData("dispatcher.TryEnqueue(DispatcherQueuePriority.Low, async () =>\n{\n    await LoadAsync();\n});")]
    [InlineData("new Thread(async () => await LoadAsync()).Start();")]
    [InlineData("_timer = new System.Threading.Timer(async _ => await TickAsync(), null, 0, 1000);")]
    [InlineData("ThreadPool.QueueUserWorkItem(async _ => await TickAsync());")]
    [InlineData("Action refresh = async () => await LoadAsync();")]
    [InlineData("private readonly Action<int> _refresh = async count => await LoadAsync(count);")]
    [InlineData("var refresh = new Action(async () => await LoadAsync());")]
    [InlineData("button.Click += async delegate { await _errors.GuardAsync(SaveAsync, Context); };")]
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
            if (!IsEventHandler(method.Groups["params"].Value) || !IsOneGuardedAwait(Body(code, method.Index + method.Length)))
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

        // An async lambda or anonymous method that becomes an Action (a dispatcher, thread pool, thread or timer
        // callback, an Action variable) is async void with no event behind it: never allowed.
        foreach (Match call in CallbackCall().Matches(code))
        {
            if (Arguments(code, call.Index + call.Length - 1).Any(argument => AsyncStart().IsMatch(argument)))
            {
                yield return Line(call.Value);
            }
        }

        foreach (Match action in AsyncAction().Matches(code))
        {
            yield return Line(action.Value);
        }
    }

    private static IEnumerable<string> DispatcherViolations(string source) =>
        RawTryEnqueue().Matches(WithoutComments(source)).Select(match => match.Value);

    private static IEnumerable<string> CommandViolations(string source)
    {
        var code = WithoutComments(source);
        return CommandConstruction().Matches(code).Select(match => match.Value);
    }

    // Two parameters, each a type (any type: object, ContentDialog, a generic) and a name.
    private static bool IsEventHandler(string parameters)
    {
        var list = TopLevelSplit(parameters, angles: true);
        return list.Count == 2 && list.All(parameter => Parameter().IsMatch(parameter));
    }

    // The body after a signature: an expression body up to the semicolon that ends it, or a block's inside.
    private static string Body(string code, int start)
    {
        var rest = code[start..].TrimStart();
        if (rest.StartsWith("=>", StringComparison.Ordinal))
        {
            rest = rest[2..].TrimStart();
            if (!rest.StartsWith('{'))
            {
                return Statement(rest);
            }
        }

        if (!rest.StartsWith('{'))
        {
            return rest;
        }

        var close = Closing(rest, 0);
        return close < 0 ? rest : rest[1..close].Trim();
    }

    // Exactly one statement: "await <the error path>.GuardAsync(...);" (or the InfoBar queue's RunActionAsync), its
    // arguments matched by balanced parentheses, nothing after it.
    private static bool IsOneGuardedAwait(string body)
    {
        body = body.Trim();
        var head = GuardedCall().Match(body);
        if (!head.Success)
        {
            return false;
        }

        var close = Closing(body, head.Index + head.Length - 1);
        return close >= 0 && body[(close + 1)..].Trim() == ";";
    }

    // The arguments of the call whose opening parenthesis is at open, split at its own commas.
    private static List<string> Arguments(string code, int open)
    {
        var close = Closing(code, open);
        return close < 0 ? [] : TopLevelSplit(code[(open + 1)..close], angles: false);
    }

    // Splits at the commas outside brackets and literals (and generic angle brackets, for parameter lists).
    private static List<string> TopLevelSplit(string text, bool angles)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '"' or '\'')
            {
                i = EndOfLiteral(text, i);
            }
            else if (c is '(' or '[' or '{' || (angles && c == '<'))
            {
                depth++;
            }
            else if (c is ')' or ']' or '}' || (angles && c == '>'))
            {
                depth--;
            }
            else if (c == ',' && depth == 0)
            {
                parts.Add(text[start..i].Trim());
                start = i + 1;
            }
        }

        var last = text[start..].Trim();
        if (last.Length > 0)
        {
            parts.Add(last);
        }

        return parts;
    }

    // The text up to and including the semicolon that ends the statement it starts.
    private static string Statement(string text)
    {
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '"' or '\'')
            {
                i = EndOfLiteral(text, i);
            }
            else if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                depth--;
            }
            else if (c == ';' && depth == 0)
            {
                return text[..(i + 1)].Trim();
            }
        }

        return text.Trim();
    }

    // The index of the bracket that closes the one at open, skipping string and character literals; -1 if none.
    private static int Closing(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '"' or '\'')
            {
                i = EndOfLiteral(text, i);
            }
            else if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}' && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    // The index of the quote that ends the literal starting at start (verbatim strings double their quotes).
    private static int EndOfLiteral(string text, int start)
    {
        var quote = text[start];
        var verbatim = quote == '"' && start > 0 && (text[start - 1] == '@' || (start > 1 && text[start - 2] == '@'));
        for (var i = start + 1; i < text.Length; i++)
        {
            if (!verbatim && text[i] == '\\')
            {
                i++;
            }
            else if (text[i] == quote)
            {
                if (verbatim && i + 1 < text.Length && text[i + 1] == '"')
                {
                    i++;
                    continue;
                }

                return i;
            }
        }

        return text.Length;
    }

    // Strips comments with regexes: a "//" inside a string literal also truncates that line (simple on purpose).
    private static string WithoutComments(string source) => LineComment().Replace(BlockComment().Replace(source, string.Empty), string.Empty);

    private static string Line(string text) => text.ReplaceLineEndings(" ").Trim();

    // async void Name(...), async void Name<T>(...), async void IInterface.Name(...).
    [GeneratedRegex(@"\basync\s+void\s+[\w.]+(?:<[^<>()]*>)?\s*\((?<params>[^)]*)\)")]
    private static partial Regex AsyncVoidMethod();

    [GeneratedRegex(@"^[\w.]+(?:<[^<>]*>)?\??\s+\w+$")]
    private static partial Regex Parameter();

    [GeneratedRegex(@"\+=\s*async\s*(\([^)]*\)|\w+)\s*(?==>)")]
    private static partial Regex AsyncLambdaHandler();

    // The guarded call's head, up to its opening parenthesis: the error path's GuardAsync (through the presenter, or
    // ErrorPath.GuardAsync(errors, ...)), or the InfoBar queue's RunActionAsync, which reports its action's failure.
    [GeneratedRegex(@"^await\s+(?:(?:this\.)?(?:_errors|errors|Errors)\.GuardAsync|ErrorPath\.GuardAsync|(?:this\.)?_queue\.RunActionAsync)\s*\(")]
    private static partial Regex GuardedCall();

    // Calls whose delegate argument returns void: dispatcher, thread pool, thread and timer callbacks.
    [GeneratedRegex(@"\b(?:TryEnqueue\w*|QueueUserWorkItem|UnsafeQueueUserWorkItem|new\s+(?:[\w.]+\.)?(?:Thread|Timer))\s*\(")]
    private static partial Regex CallbackCall();

    [GeneratedRegex(@"^async\b")]
    private static partial Regex AsyncStart();

    // An async lambda assigned to an Action or wrapped in one, and any async anonymous method.
    [GeneratedRegex(@"\bAction(?:<[^;=()]*>)?\s+\w+\s*=\s*async\b|\bnew\s+Action(?:<[^;()]*>)?\s*\(\s*async\b|\basync\s+delegate\b")]
    private static partial Regex AsyncAction();

    [GeneratedRegex(@"\[RelayCommand\b[^\]]*\]|\bnew\s+(Async)?RelayCommand\b(<[^>]*>)?\s*\(")]
    private static partial Regex CommandConstruction();

    [GeneratedRegex(@"\bTryEnqueue\s*\(")]
    private static partial Regex RawTryEnqueue();

    [GeneratedRegex(@"//[^\n]*")]
    private static partial Regex LineComment();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockComment();
}
