// Crash catcher for #90: runs the app as a debuggee and keeps what a crash with a stowed exception (0xC000027B)
// hides from the app's own log. Usage:
//   TenLapse.CrashCatcher <report.txt> <pid-file> <app.exe> [app arguments...]
// It writes the app's process id to <pid-file> (the launch test finds the window by it) and exits with the app's
// exit code. The report is written when the app exits with a non-zero code or a hang is asked for: create
// "<pid-file>.walk" and it breaks in and writes every thread's stack. No admin rights and no registry changes:
// debugging a child process of your own needs neither. See scripts/launch-test.ps1.
using TenLapse.CrashCatcher;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: TenLapse.CrashCatcher <report.txt> <pid-file> <app.exe> [app arguments...]");
    return 2;
}

return (int)DebugSession.Run(args[0], args[1], args[2], args[3..]);
