# TenLapse.CrashCatcher

A small debugger for one start-up crash hunt (#90). It starts the Debug app as its own child process and keeps
what a crash with a stowed exception (`0xC000027B`) hides from the app's log: each stowed exception's HRESULT and
its captured stack (module+offset), and the last 60 WinRT error notifications (HRESULT, message, thread). Windows
keeps the same in `Report.wer`, which is readable by administrators only.

```
TenLapse.CrashCatcher <report.txt> <pid-file> <app.exe> [app arguments...]
```

- It writes the app's process ID to `<pid-file>` and exits with the app's exit code.
- The report is written when the app exits with a non-zero code. Create `<pid-file>.walk` to have it break in and
  write every thread's stack (a hang).
- No admin rights and no registry change: debugging a child process of your own needs neither.

What it changes in the app's run, kept small on purpose:

- Every exception goes back to the app unhandled (first chance), so the app behaves as it does alone.
- The child gets the normal heap (`_NO_DEBUG_HEAP=1`), not the NT debug heap.
- Stacks are walked (and symbols loaded) only for a crash or a hang.
- It is still a debugger: `IsDebuggerPresent` is true, and WinRT raises its error notifications only then. Start-up is
  a little slower, so compare with plain launches (`launch-test.ps1 -Plain`).

x64 only (the stack walk reads the AMD64 thread context). Not shipped: not referenced by the App, not in the MSIX.
Driven by `scripts/launch-test.ps1`.
