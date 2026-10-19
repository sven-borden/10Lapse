using System.Runtime.InteropServices;
using System.Text;

namespace TenLapse.CrashCatcher;

/// <summary>The Win32 debugging API the catcher uses (x64 layouts).</summary>
internal static unsafe partial class Native
{
    public const uint DebugOnlyThisProcess = 0x2;
    public const uint DbgContinue = 0x00010002;
    public const uint DbgExceptionNotHandled = 0x80010001;
    public const uint ExceptionDebugEvent = 1;
    public const uint CreateThreadDebugEvent = 2;
    public const uint CreateProcessDebugEvent = 3;
    public const uint ExitThreadDebugEvent = 4;
    public const uint ExitProcessDebugEvent = 5;
    public const uint LoadDllDebugEvent = 6;
    public const uint UnloadDllDebugEvent = 7;
    public const uint ContextFull = 0x10000B;
    public const uint ImageFileMachineAmd64 = 0x8664;
    public const uint AddrModeFlat = 3;

    public delegate nint FunctionTableAccess(nint process, ulong address);

    public delegate ulong ModuleBase(nint process, ulong address);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CreateProcessW(string application, StringBuilder commandLine, nint processAttributes, nint threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, nint environment, string? directory, ref StartupInfo startup, out ProcessInformation information);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WaitForDebugEvent(out DebugEvent debugEvent, uint milliseconds);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ContinueDebugEvent(uint processId, uint threadId, uint status);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DebugBreakProcess(nint process);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ReadProcessMemory(nint process, nint address, void* buffer, nint size, out nint read);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern uint GetFinalPathNameByHandleW(nint file, char* buffer, uint size, uint flags);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetThreadContext(nint thread, Context* context);

    [DllImport("dbghelp.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SymInitializeW(nint process, string? searchPath, [MarshalAs(UnmanagedType.Bool)] bool invadeProcess);

    [DllImport("dbghelp.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool StackWalk64(uint machine, nint process, nint thread, ref StackFrame64 frame, Context* context, nint readMemory, FunctionTableAccess functionTableAccess, ModuleBase moduleBase, nint translateAddress);

    [DllImport("dbghelp.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern nint SymFunctionTableAccess64(nint process, ulong address);

    [DllImport("dbghelp.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern ulong SymGetModuleBase64(nint process, ulong address);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct StartupInfo
    {
        public int Size;
        public nint Reserved;
        public nint Desktop;
        public nint Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public short ShowWindow, Reserved2Size;
        public nint Reserved2, StdInput, StdOutput, StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessInformation
    {
        public nint Process;
        public nint Thread;
        public uint ProcessId;
        public uint ThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ExceptionRecord64
    {
        public uint Code;
        public uint Flags;
        public ulong Record;
        public ulong Address;
        public uint ParameterCount;
        public uint Alignment;
        public fixed ulong Information[15];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ExceptionDebugInfo
    {
        public ExceptionRecord64 Record;
        public uint FirstChance;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CreateThreadInfo
    {
        public nint Thread;
        public nint ThreadLocalBase;
        public nint StartAddress;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CreateProcessInfo
    {
        public nint File;
        public nint Process;
        public nint Thread;
        public nint BaseOfImage;
        public uint DebugInfoFileOffset;
        public uint DebugInfoSize;
        public nint ThreadLocalBase;
        public nint StartAddress;
        public nint ImageName;
        public ushort Unicode;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ExitProcessInfo
    {
        public uint ExitCode;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LoadDllInfo
    {
        public nint File;
        public nint BaseOfDll;
        public uint DebugInfoFileOffset;
        public uint DebugInfoSize;
        public nint ImageName;
        public ushort Unicode;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct UnloadDllInfo
    {
        public nint BaseOfDll;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct DebugEventInfo
    {
        [FieldOffset(0)]
        public ExceptionDebugInfo Exception;

        [FieldOffset(0)]
        public CreateThreadInfo CreateThread;

        [FieldOffset(0)]
        public CreateProcessInfo CreateProcess;

        [FieldOffset(0)]
        public ExitProcessInfo ExitProcess;

        [FieldOffset(0)]
        public LoadDllInfo LoadDll;

        [FieldOffset(0)]
        public UnloadDllInfo UnloadDll;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DebugEvent
    {
        public uint Code;
        public uint ProcessId;
        public uint ThreadId;
        public DebugEventInfo Info;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Address64
    {
        public ulong Offset;
        public ushort Segment;
        public uint Mode;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct StackFrame64
    {
        public Address64 Pc;
        public Address64 Return;
        public Address64 Frame;
        public Address64 Stack;
        public Address64 BStore;
        public nint FunctionTableEntry;
        public fixed ulong Params[4];
        public int Far;
        public int Virtual;
        public fixed ulong Reserved[3];
        public fixed byte KdHelp[0x70];
    }

    // The AMD64 CONTEXT (1232 bytes); only the fields the stack walk starts from are named.
    [StructLayout(LayoutKind.Explicit, Size = 1232)]
    public struct Context
    {
        [FieldOffset(0x30)]
        public uint ContextFlags;

        [FieldOffset(0x98)]
        public ulong Rsp;

        [FieldOffset(0xA0)]
        public ulong Rbp;

        [FieldOffset(0xF8)]
        public ulong Rip;
    }
}
