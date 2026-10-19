using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using TenLapse.Core.Diagnostics;
using TenLapse.Media.Import;
using TenLapse.Presentation;
using TenLapse.Presentation.Services;
using TenLapse.Services;

namespace TenLapse;

/// <summary>
/// The application entry point and composition root: builds the services and the shell view model by hand
/// and gives them to the main window. One window and a handful of services do not need a DI container.
/// It also holds the last line of the error path (#26): the three global handlers, and the message box for a
/// window that cannot be created.
/// </summary>
public partial class App : Application
{
    // The first thing that exists, so a failure anywhere after it (InitializeComponent included) reaches the log.
    // Opening it never throws: an unwritable folder makes it keep its lines in memory.
    private static readonly RollingFileLog _log = new(AppEnvironment.LogFolder());

    private ErrorPresenter? _errors;
    private Window? _window;

    public App()
    {
        // Logs synchronously before a dying process goes (a crash on a background thread, a fatal UI exception).
        AppDomain.CurrentDomain.UnhandledException += OnProcessUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        UnhandledException += OnUnhandledException;

        _log.Info($"10Lapse {AppDiagnostics.AppVersion()} starting ({(AppEnvironment.HasPackageIdentity ? "packaged" : "unpackaged")}); log in {_log.Folder}.");
        ExportCleanupLog.Start(_log);

        // App.xaml (the token dictionaries, the styles) that fails to load would escape the constructor with no
        // message and maybe no log: as in OnLaunched, log it, say so in a plain message box and close. The process
        // ends here, because Application.Exit would still run OnLaunched against the broken resources.
        try
        {
            InitializeComponent();
        }
        catch (Exception exception)
        {
            _log.Critical(ProjectText.StartupResourcesFailed, exception);
            StartupFailure.Show(_log.Folder);
            _log.Dispose();
            Environment.Exit(StartupFailure.ExitCode);
        }

#if DEBUG
        Gallery.GalleryLaunch.ApplyTheme(this);
#endif
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Before the window exists nothing can show an InfoBar: a failure here is logged and told in a plain
        // message box, and the app closes.
        try
        {
#if DEBUG
            // Debug only: --gallery opens the token gallery instead of the main window.
            _window = Gallery.GalleryLaunch.Requested
                ? new Gallery.GalleryWindow(Gallery.GalleryLaunch.Scroll)
                : CreateMainWindow();
#else
            _window = CreateMainWindow();
#endif
            _window.Activate();
        }
        catch (Exception exception)
        {
            _log.Critical(ProjectText.StartupWindowFailed, exception);
            StartupFailure.Show(_log.Folder);
            _log.Dispose();
            Exit();
            return;
        }

#if DEBUG
        if (_errors is { } errors)
        {
            Debugging.DebugLaunch.Crash(errors, _window.DispatcherQueue);
        }
#endif
    }

    private MainWindow CreateMainWindow()
    {
        MainWindow? window = null;
        var dialogs = new ContentDialogService(() => window?.Content?.XamlRoot);

        // The one InfoBar queue: the error path and the import and export issues raise InfoBars through it (as
        // IInfoBarService); the window shows them. It shows its own bar when an InfoBar action fails, so it only
        // needs the log.
        // The window's dispatcher callbacks log their failures through the same log-only sink (never an InfoBar,
        // so a broken InfoBar presenter cannot loop).
        var logOnly = new LogErrorSink(_log);
        var infoBars = new InfoBarQueue(logOnly);

        // The encoder probe (#9) starts now, in the background; the Output panel (#23) takes its answer when it comes
        // and nothing waits for it.
        var encoders = new EncoderStartup(AppEnvironment.EncoderCacheFile());

        // Copy diagnostics. The encoder section reads this session's probe result, else the cache #9's probe writes,
        // and never probes itself.
        var diagnostics = new DiagnosticsCopier(
            new DiagnosticsCollector(
                [new AppDiagnostics(), new GraphicsDiagnostics(), new EncoderDiagnostics(AppEnvironment.EncoderCacheFile(), () => encoders.Loaded)],
                _log,
                redaction: DiagnosticsRedaction.ForCurrentUser(AppEnvironment.ShortProfileFolder())),
            new ClipboardService(),
            infoBars);

        // The error path: every command goes through it. Mappers word the failures of libraries Presentation
        // cannot see.
        var errors = new ErrorPresenter(_log, infoBars, [new VideoExportErrorMapper()], diagnostics.CopyAsync);
        var shell = new ShellViewModel(dialogs, errors);
        var settings = new SettingsViewModel(errors, diagnostics);

        // Import (#12): Media's PhotoImporter does the file work behind Core's IPhotoImportSource; the batches reach
        // the project on this (the UI) thread's queue. The pickers remember the last folder for the session; #21
        // passes an IImportFolderMemory that lasts across launches instead of SessionImportFolders.
        var ui = new DispatcherQueueUiDispatcher(DispatcherQueue.GetForCurrentThread(), logOnly);
        var import = new ImportViewModel(
            shell,
            new PhotoImporter { Log = _log },
            new PhotoPickerService(() => window!.AppWindow.Id),
            new SessionImportFolders(),
            ui,
            dialogs,
            infoBars,
            errors);
        // The Output panel (#23) offers only what the probe confirms; until then it says it is checking. A probe
        // that is slow or fails leaves it with H.264 up to 1080p (OutputCapabilitiesDelivery).
        var output = new OutputPanelViewModel(shell, errors);
        var probe = encoders.Probe;
#if DEBUG
        probe = Debugging.DebugOutputCapabilities.FromSwitches(Environment.GetCommandLineArgs()) ?? probe;
#endif
        _ = errors.GuardAsync(() => output.DeliverAsync(probe, ui, errors, OutputCapabilitiesDelivery.DefaultPatience, encoders.Token), OutputText.ProbeFailed);

        // The preview (#19): the proxy cache in LocalCache (beside the log unpackaged), the frame images, the player.
        var preview = new Preview.PreviewSession(shell, errors, infoBars, logOnly, PreviewCacheFolder());
        window = new MainWindow(shell, settings, import, output, infoBars, logOnly, errors, preview);

        // Closing the window stops the probe (it ends within one test encode and removes that file) and the panel.
        window.Closed += (_, _) =>
        {
            encoders.Cancel();
            output.Dispose();
        };

        // And the preview: its render thread, its loads and the frame images.
        window.Closed += (_, _) => logOnly.RunLogged(preview.Dispose, ProjectText.PreviewProjectFailed);
        _errors = errors;
#if DEBUG
        errors.Guard(() => Debugging.DebugLaunch.ApplyPreview(preview, window, _log), "Applying the preview's Debug switches failed.");
        errors.Guard(() => Debugging.DebugLaunch.Apply(shell, infoBars), "Applying the Debug switches failed.");
#endif
        return window;
    }

    // Debug can point the proxy cache elsewhere (--cache-folder=PATH), so a test run leaves the user's cache alone.
    private static string PreviewCacheFolder()
    {
#if DEBUG
        if (Debugging.DebugLaunch.CacheFolder is { } folder)
        {
            return folder;
        }
#endif
        return AppEnvironment.ProxyCacheFolder();
    }

    // An exception that reached the UI thread unhandled: an event handler, a binding, a continuation. Unless it
    // means corrupted state, the action failed and the app goes on: mark it handled and say so.
    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        var context = $"{ProjectText.UnhandledUiFailure} {e.Message}";
        if (_errors is not { } errors || ErrorMessages.IsFatal(e.Exception))
        {
            _log.Critical(context + " " + ProjectText.ProcessFailure, e.Exception);
            return;
        }

        e.Handled = true;
        errors.ShowUnhandled(e.Exception, context);
    }

    // A faulted task nobody awaited, found by the finalizer: observe it so it can never end the process, and tell
    // the user, because something they started did not finish.
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        if (_errors is { } errors)
        {
            errors.ShowUnhandled(e.Exception, ProjectText.UnobservedTaskFailure);
        }
        else
        {
            _log.Error(ProjectText.UnobservedTaskFailure, e.Exception);
        }
    }

    // The process is going (an exception on a thread of its own, or one WinUI could not hand back): write it down
    // while it still can. The log writes and flushes synchronously.
    private static void OnProcessUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        _log.Critical(ProjectText.ProcessFailure, e.ExceptionObject as Exception);
        if (e.IsTerminating)
        {
            _log.Dispose();
        }
    }
}
