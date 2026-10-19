using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using static TenLapse.CrashCatcher.Native;

namespace TenLapse.CrashCatcher;

/// <summary>
/// One debugged run of the app. Every exception goes back to the app as if no debugger were attached (first chance:
/// not handled), so the app behaves as it does alone, only a little slower. The child gets the normal heap
/// (<c>_NO_DEBUG_HEAP=1</c>), and stacks are walked only for a crash or a hang, so start-up timing stays close to a
/// plain launch (#90). Kept on the way:
/// <list type="bullet">
/// <item>the last WinRT error notifications (RoOriginateError and its transform raise them while a debugger is
/// attached: the HRESULT, the message XAML or Composition gave, and the thread), so the error behind a fail-fast is
/// in the report;</item>
/// <item>a STATUS_STOWED_EXCEPTION: each stowed exception's HRESULT and the stack captured when it was stowed, as
/// module+offset (symbols can be looked up later);</item>
/// <item>second-chance exceptions, and access violations and stack-buffer overruns at first chance.</item>
/// </list>
/// </summary>
internal static unsafe class DebugSession
{
    private const uint StowedException = 0xC000027B;
    private const uint OriginateError = 0x40080201;
    private const uint TransformError = 0x40080202;
    private const uint Breakpoint = 0x80000003;
    private const uint WowBreakpoint = 0x4000001F;
    private const uint AccessViolation = 0xC0000005;
    private const uint StackBufferOverrun = 0xC0000409;
    private const int KeptNotifications = 60;
    private const int WaitTimeout = 121;

    public static uint Run(string reportPath, string pidFile, string exe, string[] arguments)
    {
        var commandLine = new StringBuilder(Quote(exe));
        foreach (var argument in arguments)
        {
            commandLine.Append(' ').Append(Quote(argument));
        }

        var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>() };
        var environment = Marshal.StringToHGlobalUni(ChildEnvironment());
        ProcessInformation process;
        try
        {
            if (!CreateProcessW(exe, commandLine, 0, 0, false, DebugOnlyThisProcess | CreateUnicodeEnvironment, environment, Path.GetDirectoryName(exe), ref startup, out process))
            {
                throw new Win32Exception();
            }
        }
        finally
        {
            Marshal.FreeHGlobal(environment);
        }

        File.WriteAllText(pidFile, process.ProcessId.ToString(CultureInfo.InvariantCulture));
        var session = new Session(process.Process, Path.GetFileName(exe));
        var exitCode = session.Loop(pidFile + ".walk", reportPath);
        if (exitCode != 0 || session.HasNotes)
        {
            File.WriteAllText(reportPath, session.Report(exitCode));
        }

        return exitCode;
    }

    // One argument as CommandLineToArgvW reads it back: quoted, with each quote escaped and the backslashes before a
    // quote (or before the closing quote) doubled.
    internal static string Quote(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
        {
            return argument;
        }

        var text = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            text.Append('\\', c == '"' ? (backslashes * 2) + 1 : backslashes).Append(c);
            backslashes = 0;
        }

        return text.Append('\\', backslashes * 2).Append('"').ToString();
    }

    // This process's environment plus _NO_DEBUG_HEAP=1: a process started under a debugger otherwise gets the NT debug
    // heap, which is slower and lays memory out differently. Sorted, as CreateProcess expects, and double-NUL ended.
    private static string ChildEnvironment()
    {
        var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            variables[(string)entry.Key] = (string?)entry.Value ?? string.Empty;
        }

        variables["_NO_DEBUG_HEAP"] = "1";
        var block = new StringBuilder();
        foreach (var (name, value) in variables)
        {
            block.Append(name).Append('=').Append(value).Append('\0');
        }

        return block.Append('\0').ToString();
    }

    private sealed class Session(nint process, string exeName)
    {
        private readonly SortedDictionary<ulong, string> _modules = [];
        private readonly Dictionary<uint, nint> _threads = [];
        private readonly Queue<string> _notifications = new();
        private readonly List<string> _notes = [];
        private readonly long _started = Environment.TickCount64;
        private bool _symbols;

        public bool HasNotes => _notes.Count > 0;

        public uint Loop(string walkFlag, string reportPath)
        {
            var walkRequested = false;
            var walked = false;
            while (true)
            {
                if (!WaitForDebugEvent(out var debugEvent, 500))
                {
                    if (Marshal.GetLastWin32Error() != WaitTimeout)
                    {
                        throw new Win32Exception();
                    }

                    if (!walkRequested && File.Exists(walkFlag))
                    {
                        walkRequested = true;
                        DebugBreakProcess(process);
                    }

                    continue;
                }

                var status = DbgContinue;
                try
                {
                    switch (debugEvent.Code)
                    {
                        case CreateProcessDebugEvent:
                            _modules[(ulong)debugEvent.Info.CreateProcess.BaseOfImage] = exeName;
                            _threads[debugEvent.ThreadId] = debugEvent.Info.CreateProcess.Thread;
                            CloseIfOpen(debugEvent.Info.CreateProcess.File);
                            break;
                        case CreateThreadDebugEvent:
                            _threads[debugEvent.ThreadId] = debugEvent.Info.CreateThread.Thread;
                            break;
                        case ExitThreadDebugEvent:
                            _threads.Remove(debugEvent.ThreadId);
                            break;
                        case LoadDllDebugEvent:
                            _modules[(ulong)debugEvent.Info.LoadDll.BaseOfDll] = ModuleName(debugEvent.Info.LoadDll.File, (ulong)debugEvent.Info.LoadDll.BaseOfDll);
                            CloseIfOpen(debugEvent.Info.LoadDll.File);
                            break;
                        case UnloadDllDebugEvent:
                            _modules.Remove((ulong)debugEvent.Info.UnloadDll.BaseOfDll);
                            break;
                        case ExceptionDebugEvent:
                            if (debugEvent.Info.Exception.Record.Code == Breakpoint && walkRequested && !walked)
                            {
                                walked = true;
                                WalkAllThreads();
                                File.WriteAllText(reportPath, Report(exitCode: null));
                            }

                            status = OnException(ref debugEvent.Info.Exception, debugEvent.ThreadId);
                            break;
                        case ExitProcessDebugEvent:
                            ContinueDebugEvent(debugEvent.ProcessId, debugEvent.ThreadId, DbgContinue);
                            return debugEvent.Info.ExitProcess.ExitCode;
                    }
                }
                catch (Exception exception)
                {
                    // The catcher must never be what ends the app.
                    _notes.Add($"[{Elapsed} ms] catcher failed on debug event {debugEvent.Code}: {exception.GetType().Name} {exception.Message}");
                }

                ContinueDebugEvent(debugEvent.ProcessId, debugEvent.ThreadId, status);
            }
        }

        public string Report(uint? exitCode)
        {
            var text = new StringBuilder(exitCode is { } code ? $"exit 0x{code:X8}\n" : "hang\n");
            foreach (var note in _notes)
            {
                text.AppendLine(note);
            }

            text.AppendLine("--- the last WinRT error notifications:");
            foreach (var notification in _notifications)
            {
                text.AppendLine(notification);
            }

            return text.ToString();
        }

        private long Elapsed => Environment.TickCount64 - _started;

        private uint OnException(ref ExceptionDebugInfo info, uint threadId)
        {
            ref var record = ref info.Record;
            var firstChance = info.FirstChance != 0;
            if (record.Code is Breakpoint or WowBreakpoint)
            {
                return DbgContinue;
            }

            if (record.Code is OriginateError or TransformError)
            {
                var hr = (uint)record.Information[0];
                var length = record.ParameterCount > 1 && record.Information[1] < 4096 ? (int)record.Information[1] + 1 : 512;
                var message = record.ParameterCount > 2 ? ReadString(record.Information[2], length) : string.Empty;
                var kind = record.Code == OriginateError ? "originate" : "transform";
                // No stack walk here: it would freeze the app on every notification and change start-up timing. The
                // stowed exception of a crash carries its own stack.
                _notifications.Enqueue($"[{Elapsed} ms] {kind} 0x{hr:X8} thread {threadId} \"{message.Trim()}\"");
                if (_notifications.Count > KeptNotifications)
                {
                    _notifications.Dequeue();
                }
            }
            else if (record.Code == StowedException)
            {
                _notes.Add($"[{Elapsed} ms] STOWED EXCEPTION thread {threadId} at {Where((ulong)record.Address)}, first chance {firstChance}");
                _notes.Add("    thread stack: " + Walk(threadId));
                if (record.ParameterCount >= 2)
                {
                    DescribeStowed(record.Information[0], (int)Math.Min(record.Information[1], 16));
                }
            }
            else if (!firstChance || record.Code is AccessViolation or StackBufferOverrun)
            {
                _notes.Add($"[{Elapsed} ms] exception 0x{record.Code:X8} first chance {firstChance} thread {threadId} at {Where((ulong)record.Address)}\n    {Walk(threadId)}");
            }

            return DbgExceptionNotHandled;
        }

        // STOWED_EXCEPTION_INFORMATION_V2 (x64): Size, Signature, ResultCode, ExceptionForm:2 + ThreadId:30, then for
        // form 1 the exception address, the stack word size and count and the stack pointer, for form 2 the error text;
        // NestedExceptionType at 40.
        private void DescribeStowed(ulong array, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var info = Read<ulong>(array + (ulong)(i * 8));
                var signature = Encoding.ASCII.GetString(BitConverter.GetBytes(Read<uint>(info + 4)));
                var hr = Read<uint>(info + 8);
                var formAndThread = Read<uint>(info + 12);
                var text = new StringBuilder($"    stowed[{i}] {signature} hr 0x{hr:X8} form {formAndThread & 3} thread {formAndThread >> 2}");
                if ((formAndThread & 3) == 1)
                {
                    var wordSize = Read<uint>(info + 24);
                    var words = Math.Min(Read<uint>(info + 28), 64u);
                    var stack = Read<ulong>(info + 32);
                    text.Append($" at {Where(Read<ulong>(info + 16))}\n      stack:");
                    for (var word = 0u; word < words; word++)
                    {
                        var address = wordSize == 8 ? Read<ulong>(stack + (word * 8)) : Read<uint>(stack + (word * 4));
                        text.Append(' ').Append(Where(address));
                    }
                }
                else if ((formAndThread & 3) == 2)
                {
                    text.Append(" text \"").Append(ReadString(Read<ulong>(info + 16), 512)).Append('"');
                }

                var nested = Read<uint>(info + 40);
                if (nested != 0)
                {
                    text.Append("\n      nested ").Append(Encoding.ASCII.GetString(BitConverter.GetBytes(nested)));
                }

                _notes.Add(text.ToString());
            }
        }

        private void WalkAllThreads()
        {
            _notes.Add($"[{Elapsed} ms] HANG: every thread's stack ({_threads.Count} threads)");
            foreach (var threadId in _threads.Keys.ToArray())
            {
                _notes.Add($"  thread {threadId}: {Walk(threadId)}");
            }
        }

        // The thread's native frames as module+offset; JIT-compiled managed frames show as bare addresses.
        private string Walk(uint threadId)
        {
            if (!_threads.TryGetValue(threadId, out var thread))
            {
                return "(thread not known)";
            }

            if (!_symbols)
            {
                _symbols = SymInitializeW(process, null, true);
            }

            var context = (Context*)NativeMemory.AlignedAlloc((nuint)sizeof(Context), 16);
            try
            {
                *context = default;
                context->ContextFlags = ContextFull;
                if (!GetThreadContext(thread, context))
                {
                    return "(no thread context)";
                }

                var frame = new StackFrame64();
                frame.Pc.Offset = context->Rip;
                frame.Pc.Mode = AddrModeFlat;
                frame.Frame.Offset = context->Rbp;
                frame.Frame.Mode = AddrModeFlat;
                frame.Stack.Offset = context->Rsp;
                frame.Stack.Mode = AddrModeFlat;
                var text = new StringBuilder();
                for (var i = 0; i < 48 && StackWalk64(ImageFileMachineAmd64, process, thread, ref frame, context, 0, SymFunctionTableAccess64, SymGetModuleBase64, 0) && frame.Pc.Offset != 0; i++)
                {
                    text.Append(Where(frame.Pc.Offset)).Append(' ');
                }

                return text.ToString();
            }
            finally
            {
                NativeMemory.AlignedFree(context);
            }
        }

        private string Where(ulong address)
        {
            string? name = null;
            ulong moduleBase = 0;
            foreach (var (start, module) in _modules)
            {
                if (start > address)
                {
                    break;
                }

                moduleBase = start;
                name = module;
            }

            return name is null ? $"0x{address:X}" : $"{name}+0x{address - moduleBase:X}";
        }

        private T Read<T>(ulong address)
            where T : unmanaged
        {
            T value = default;
            ReadProcessMemory(process, (nint)address, &value, sizeof(T), out _);
            return value;
        }

        private string ReadString(ulong address, int maxChars)
        {
            if (address is 0 or > 0x7FFF_FFFF_FFFF)
            {
                return string.Empty;
            }

            maxChars = maxChars is <= 0 or > 4096 ? 512 : maxChars;
            var buffer = new char[maxChars];
            fixed (char* chars = buffer)
            {
                ReadProcessMemory(process, (nint)address, chars, maxChars * 2, out var read);
                var text = new string(chars, 0, (int)(read / 2));
                var end = text.IndexOf('\0', StringComparison.Ordinal);
                return end >= 0 ? text[..end] : text;
            }
        }

        private static string ModuleName(nint file, ulong moduleBase)
        {
            if (file == 0)
            {
                return $"module@{moduleBase:X}";
            }

            var buffer = new char[1024];
            fixed (char* chars = buffer)
            {
                var length = GetFinalPathNameByHandleW(file, chars, 1024, 0);
                return length > 0 ? Path.GetFileName(new string(chars, 0, (int)length)) : $"module@{moduleBase:X}";
            }
        }

        private static void CloseIfOpen(nint handle)
        {
            if (handle != 0)
            {
                CloseHandle(handle);
            }
        }
    }
}
