using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using TenLapse.Core.Diagnostics;
using TenLapse.Core.Settings;
using TenLapse.Media.Import;
using TenLapse.Presentation;
using TenLapse.Presentation.Rating;
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
    // Every file and folder the app writes or deletes in. Debug can move each one (--log-folder=PATH, --settings-folder=PATH,
    // --encoder-cache=FILE, --cache-folder=PATH) and the journal of unfinished exports follows the log folder, so a test
    // run leaves the user's own folder alone (#90, #110). Without the switches, and always in Release, these are
    // AppEnvironment's defaults. Declared before _log: static fields initialise in order.
    private static readonly AppFolders _folders = AppFolders.Resolve(
        AppEnvironment.LogFolder(),
        AppEnvironment.ProxyCacheFolder(),
#if DEBUG
        new AppFolderOverrides(
            Debugging.DebugLaunch.LogFolder,
            Debugging.DebugLaunch.SettingsFolder,
            Debugging.DebugLaunch.EncoderCacheFile,
            Debugging.DebugLaunch.CacheFolder));
#else
        null);
#endif

    // The first thing that exists, so a failure anywhere after it (InitializeComponent included) reaches the log.
    // Opening it never throws: an unwritable folder makes it keep its lines in memory.
    private static readonly RollingFileLog _log = new(_folders.LogFolder);

    // The start-up phases in the log (#90), timed from the process start: the last line of a log that stops at
    // start-up names the phase.
    private readonly StartupTrace _startup = StartupTrace.ForThisProcess(_log);
    private ErrorPresenter? _errors;
    private Window? _window;

    public App()
    {
        // First of all, before anything reads text (#113): the app's language, as the .NET UI culture of every thread, so
        // the resource files speak the language WinUI and Windows chose for the app. Labels read once and kept
        // (ShellLayout, the Frame and Timing panels) follow it, so a language change needs a restart.
#if DEBUG
        // Debug --lang=<tag> wins (WinUI's own strings follow it below, once the log and the hooks exist).
        var language = AppLanguage.Apply(Debugging.DebugLaunch.Language);
#else
        var language = AppLanguage.Apply(null);
#endif

        // Logs synchronously before a dying process goes (a crash on a background thread, a fatal UI exception).
        AppDomain.CurrentDomain.UnhandledException += OnProcessUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        UnhandledException += OnUnhandledException;

#if DEBUG
        // Before any text is read: a few lists of labels are read once and kept.
        if (Debugging.DebugLaunch.PseudoLocalize)
        {
            Core.Text.PseudoLocalization.Enable();
        }
#endif

        _log.Info($"10Lapse {AppDiagnostics.AppVersion()} starting ({(AppEnvironment.HasPackageIdentity ? "packaged" : "unpackaged")}); log in {_log.Folder}.");
        _log.Info(language);
#if DEBUG
        // --lang also sets WinUI's own strings (MRT), for this process only. Before InitializeComponent, which loads the
        // first WinUI resources. A setter that throws leaves WinUI in the display language: logged, not fatal.
        try
        {
            Debugging.DebugLaunch.ApplyLanguage(AppLanguage.Culture);
        }
        catch (Exception exception)
        {
            _log.Warning($"--lang could not set WinUI's language to {AppLanguage.Tag}; its own strings stay in the display language.", exception);
        }

        // --region sets the regional format (numbers, dates) for this process only, so a screenshot in French shows "16,0 s"
        // (#129). A tag .NET does not know is logged and the PC's own format stays.
        try
        {
            if (Debugging.DebugLaunch.ApplyRegion() is { } note)
            {
                _log.Info(note);
            }
        }
        catch (Exception exception)
        {
            _log.Warning($"--region={Debugging.DebugLaunch.Region} is not a regional format .NET knows; this PC's own stays.", exception);
        }
#endif
        ExportCleanupLog.Start(_log, _folders.UnfinishedExportsFile);

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
            _startup.Phase("activating the window");
            _window.Activate();
            _startup.Phase("waiting for the shell to load");
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

        // The encoder probe (#9) runs in the background once the window's first frame is on screen (#90); the Output
        // panel (#23) takes its answer when it comes and nothing waits for it.
        var encoders = new EncoderStartup(_folders.EncoderCacheFile, _startup);

        // Copy diagnostics. The encoder section reads this session's probe result, else the cache #9's probe writes,
        // and never probes itself.
        var diagnostics = new DiagnosticsCopier(
            new DiagnosticsCollector(
                [new AppDiagnostics(), new GraphicsDiagnostics(), new EncoderDiagnostics(_folders.EncoderCacheFile, () => encoders.Loaded)],
                _log,
                redaction: DiagnosticsRedaction.ForCurrentUser(AppEnvironment.ShortProfileFolder())),
            new ClipboardService(),
            infoBars);

        // The error path: every command goes through it. Mappers word the failures of libraries Presentation
        // cannot see.
        var errors = new ErrorPresenter(_log, infoBars, [new VideoExportErrorMapper()], diagnostics.CopyAsync);

        // From here an exception XAML routes is shown and handled (its InfoBar waits for the window), not fatal (#90).
        _errors = errors;
        // The remembered settings (#21): a small file read now, on this thread, before the window; a new project starts
        // from them, the pickers open in the last folder, and the file is written a moment after each change.
        var remembered = new RememberedSettings(new JsonFileSettingsStore(_folders.SettingsFile), errors, _log);
        _startup.Phase("building the view models");
        var shell = new ShellViewModel(dialogs, errors, remembered.CreateProject);
        var settings = new SettingsViewModel(errors, diagnostics, remembered, infoBars, new LauncherService());

        // Import (#12): Media's PhotoImporter does the file work behind Core's IPhotoImportSource; the batches reach
        // the project on this (the UI) thread's queue. The pickers remember the last folder across launches (#21).
        var ui = new DispatcherQueueUiDispatcher(DispatcherQueue.GetForCurrentThread(), logOnly);
        ICodecExtensions codecs = new WindowsCodecExtensions();
#if DEBUG
        if (Debugging.DebugMissingCodecs.FromSwitches(Environment.GetCommandLineArgs()) is { } missingCodecs)
        {
            codecs = new Debugging.DebugMissingCodecs(codecs, missingCodecs);
        }
#endif
        var import = new ImportViewModel(
            shell,
            new PhotoImporter { Log = _log },
            new PhotoPickerService(() => window!.AppWindow.Id),
            remembered,
            ui,
            dialogs,
            infoBars,
            codecs,
            errors);
        // Saving and opening projects (#52): the File menu, the recent list on the first-launch screen, the question before
        // unsaved changes would be lost, and the crash safety net. The recovery copies sit beside the settings file, in
        // the app's own folder, never beside the user's project.
        var recovery = new Core.Projects.RecoveryStore(_folders.RecoveryFolder);
        var files = new Presentation.ProjectFiles.ProjectFilesViewModel(
            shell,
            new ProjectFilePicker(() => window!.AppWindow.Id),
            Core.Projects.ProjectFileSystem.Instance,
            remembered,
            recovery,
            dialogs,
            infoBars,
            errors,
            _log);
        var recent = new Presentation.ProjectFiles.RecentProjectsViewModel(remembered, files, Core.Projects.ProjectFileSystem.Instance, infoBars, errors);
        var autosave = new Presentation.ProjectFiles.ProjectAutosave(shell, recovery, errors);

        // The Output panel (#23) offers only what the probe confirms; until then it says it is checking. A probe
        // that is slow or fails leaves it with H.264 up to 1080p (OutputCapabilitiesDelivery).
        var output = new OutputPanelViewModel(shell, errors);
        var probe = encoders.Probe;
#if DEBUG
        probe = Debugging.DebugOutputCapabilities.FromSwitches(Environment.GetCommandLineArgs()) ?? probe;
#endif
        _ = errors.GuardAsync(
            async () =>
            {
                await output.DeliverAsync(probe, ui, errors, OutputCapabilitiesDelivery.DefaultPatience, encoders.Token).ConfigureAwait(false);
                _startup.ProbeApplied("encoder answer applied to the Output panel");
            },
            OutputText.ProbeFailed);

        // The preview (#19): the proxy cache in LocalCache (beside the log unpackaged), the frame images, the player.
        _startup.Phase("creating the preview session");
        var preview = new Preview.PreviewSession(shell, errors, infoBars, _log, _folders.ProxyCacheFolder);

        // The export (#24): Media's exporter behind Presentation's interface, the Save dialog in the folder of the last
        // export (#21), Show in folder, and the window's taskbar progress and notification. The notifications register
        // now, before the window, as the Windows App SDK asks: packaged always, unpackaged Debug only with --notifications.
        _startup.Phase("registering the export notifications");
        var notifications = new ExportNotifications(logOnly);
        notifications.Register();
        Presentation.Export.ISaveVideoPicker savePicker = new SaveVideoPicker(() => window!.AppWindow.Id);
#if DEBUG
        savePicker = Debugging.DebugExport.Picker(savePicker);
#endif
        var exporter = new ProjectVideoExporter();
        _startup.Phase("creating the window");
        window = new MainWindow(
            shell,
            settings,
            import,
            output,
            infoBars,
            logOnly,
            errors,
            preview,
            notifications,
            exportWindow => new Presentation.Export.ExportServices(exporter, savePicker, remembered, dialogs, infoBars, new ExplorerFileReveal(), exportWindow, new PowerRequestKeepAwake(logOnly)),
            _startup,
            files,
            recent);
        _startup.Phase("attaching the window's services");
        window.Closed += (_, _) => notifications.Dispose();
        window.FirstFrameRendered += (_, _) => encoders.Start();

        // Closing the window stops the probe (it ends within one test encode and removes that file) and the panel.
        window.Closed += (_, _) =>
        {
            encoders.Cancel();
            output.Dispose();
        };

        // And the preview: its render thread, its loads and the frame images.
        window.Closed += (_, _) => logOnly.RunLogged(preview.Dispose, ProjectText.PreviewProjectFailed);

        // The rating prompt (#31): after the second successful export, once per version, if the window is in front and
        // nothing is open. Unpackaged it does nothing; Debug --fake-rating logs the ask instead of calling the Store.
        IRatingService ratingService = new StoreRatingService(() => window!.Handle, _log);
#if DEBUG
        if (Debugging.DebugRating.Requested)
        {
            ratingService = new Debugging.DebugRating(_log);
        }
#endif
        var rating = new RatingPrompt(remembered, ratingService, () => window!.IsInForeground && !dialogs.HasOpenDialog && window.Export.IsFinished, AppDiagnostics.AppVersion(), _log, logOnly);
        rating.Attach(window.Export);
        window.Closed += (_, _) => rating.Dispose();
#if DEBUG
        errors.Guard(() => Debugging.DebugLaunch.ApplyPreview(preview, window, _log), "Applying the preview's Debug switches failed.");
        errors.Guard(() => Debugging.DebugLaunch.Apply(shell, infoBars), "Applying the Debug switches failed.");
        errors.Guard(() => Debugging.DebugLaunch.ApplyImport(import, shell, errors), "Applying the import bench failed.");
#endif
        // After the Debug switches, so only the user's own changes are remembered. The last change is written as the window closes.
        errors.Guard(() => remembered.Attach(shell), ProjectText.SettingsAttachFailed);
        window.Closed += (_, _) => remembered.Dispose();

        // The recovery copy follows the user's own changes from here (#52). A clean close removes it; the window closes only
        // after the question about unsaved changes (WindowCloseGate).
        errors.Guard(autosave.Attach, Presentation.ProjectFiles.ProjectFileText.AutosaveFailedContext);
        window.Closed += (_, _) =>
        {
            autosave.Dispose();
            recent.Dispose();
            files.Dispose();
        };

        // Once the window is on screen: the project 10Lapse was started with (a double click on a .10lapse file, or the
        // command line), then the offer to restore a project a 10Lapse that ended without closing could not save.
        var launchFile = LaunchFile.Find();
        window.FirstFrameRendered += (_, _) => _ = errors.GuardAsync(
            async () =>
            {
                if (launchFile is not null)
                {
                    await errors.GuardAsync(() => files.OpenPathAsync(launchFile), Presentation.ProjectFiles.ProjectFileText.OpenAtLaunchFailedContext);
                }

                await files.OfferRecoveryAsync();
            },
            Presentation.ProjectFiles.ProjectFileText.RecoveryLookFailedContext);
#if DEBUG
        if (Debugging.DebugMissingCodecs.FromSwitches(Environment.GetCommandLineArgs()) is { } fakedMissing)
        {
            // --missing-codec: the InfoBars an import of 12 HEIC and 3 RAW photos would raise with those extensions missing.
            errors.Guard(() => { var prompts = new CodecExtensionPrompts(codecs, infoBars, errors); prompts.Show(prompts.Plan(Debugging.DebugMissingCodecs.SampleSkips(fakedMissing))); }, "Showing the faked missing-codec InfoBars failed.");
        }
#endif
        return window;
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
