using TenLapse.Core.Projects;
using TenLapse.Core.Settings;
using TenLapse.Core.Timing;
using TenLapse.Presentation;
using TenLapse.Presentation.ProjectFiles;
using TenLapse.Presentation.Services;
using TenLapse.Tests.Import;
using TenLapse.Tests.Projects;

namespace TenLapse.Tests.Presentation;

/// <summary>
/// Save, Save as and Open (#52) on a stand-in UI thread: where files go, what the project becomes, what is asked before
/// unsaved changes are lost, what an unusable file says, and the recovery offer, the autosave and the recent list.
/// </summary>
public sealed class ProjectFilesViewModelTests : IDisposable
{
    private static readonly DateTime _written = new(2026, 5, 1, 18, 30, 0, DateTimeKind.Utc);

    private readonly UiThread _ui = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeErrorSink _errors = new();
    private readonly FakeInfoBarService _infoBars = new();
    private readonly FakeLog _log = new();
    private readonly MemoryDisk _disk = new();
    private readonly FakePicker _picker = new();
    private readonly FakeRecent _recent = new();
    private readonly FakeRecovery _recovery = new();

    public void Dispose() => _ui.Dispose();

    [Fact]
    public async Task SaveTheFirstTimeAsksWhereThenSavesThereWithoutAsking()
    {
        var (shell, files) = Create();
        _ui.Run(() => shell.CurrentProject.Model.AddFrames(Photos(2)));
        _picker.SavePath = @"C:\Projects\Harbor sunset.10lapse";

        await _ui.RunAsync(() => files.SaveCommand.ExecuteAsync(null));

        var project = shell.CurrentProject.Model;
        Assert.Equal(@"C:\Projects\Harbor sunset.10lapse", project.FilePath);
        Assert.Equal("Harbor sunset", project.Name);
        Assert.False(project.IsDirty);
        Assert.Equal(2, ProjectFileJson.Read(_disk.Texts[@"C:\Projects\Harbor sunset.10lapse"]).Content!.Frames.Length);
        Assert.Equal([@"C:\Projects\Harbor sunset.10lapse"], _recent.RecentProjects);

        _picker.SavePath = @"C:\Elsewhere\Other.10lapse";
        _ui.Run(() => project.Timing = TimingSettings.PerSecond(new Fraction(24, 1)));
        await _ui.RunAsync(() => files.SaveCommand.ExecuteAsync(null));

        Assert.Equal(1, _picker.SaveAsks);
        Assert.False(project.IsDirty);
        Assert.Equal(new Fraction(24, 1), ProjectFileJson.Read(_disk.Texts[@"C:\Projects\Harbor sunset.10lapse"]).Content!.Timing.FramesPerSecond);
    }

    [Fact]
    public async Task SaveAsAlwaysAsksAndMovesTheProjectToTheNewFile()
    {
        var (shell, files) = Create();
        _ui.Run(() => shell.CurrentProject.Model.AddFrames(Photos(1)));
        _picker.SavePath = @"C:\Projects\One.10lapse";
        await _ui.RunAsync(() => files.SaveCommand.ExecuteAsync(null));

        _picker.SavePath = @"C:\Projects\Two.10lapse";
        await _ui.RunAsync(() => files.SaveAsCommand.ExecuteAsync(null));

        Assert.Equal(2, _picker.SaveAsks);
        Assert.Equal("Two", shell.CurrentProject.Name);
        Assert.Equal([@"C:\Projects\Two.10lapse", @"C:\Projects\One.10lapse"], _recent.RecentProjects);
    }

    [Fact]
    public async Task AFailedSaveSaysSoAndLeavesTheProjectUnsaved()
    {
        var (shell, files) = Create();
        _ui.Run(() => shell.CurrentProject.Model.AddFrames(Photos(1)));
        _picker.SavePath = @"C:\ReadOnly\Harbor.10lapse";
        _disk.FailWrites = true;

        await _ui.RunAsync(() => files.SaveCommand.ExecuteAsync(null));

        Assert.True(shell.CurrentProject.Model.IsDirty);
        Assert.Null(shell.CurrentProject.Model.FilePath);
        var bar = Assert.Single(_infoBars.Shown);
        Assert.Equal(InfoBarSeverity.Critical, bar.Severity);
        Assert.Equal(ProjectFileText.SaveFailed(@"C:\ReadOnly\Harbor.10lapse"), bar.Message);
        Assert.Equal(ProjectFileText.SaveAs, bar.ActionLabel);
        Assert.Single(_errors.Reported);
    }

    [Fact]
    public async Task SavingToAFolderThatIsGoneSaysSoAndOffersSaveAs()
    {
        var (shell, files) = Create();
        _ui.Run(() => shell.CurrentProject.Model.AddFrames(Photos(1)));
        _picker.SavePath = @"E:\Shoots\Harbor.10lapse";
        await _ui.RunAsync(() => files.SaveCommand.ExecuteAsync(null));
        _ui.Run(() => shell.CurrentProject.Model.Timing = TimingSettings.PerSecond(new Fraction(24, 1)));
        _disk.FolderGone = true;

        await _ui.RunAsync(() => files.SaveCommand.ExecuteAsync(null));

        var bar = Assert.Single(_infoBars.Shown);
        Assert.Equal(ProjectFileText.SaveFolderGone(@"E:\Shoots\Harbor.10lapse"), bar.Message);
        Assert.True(shell.CurrentProject.Model.IsDirty);

        _disk.FolderGone = false;
        _picker.SavePath = @"D:\Elsewhere\Harbor.10lapse";
        await _ui.RunAsync(bar.Action!);

        Assert.Equal(@"D:\Elsewhere\Harbor.10lapse", shell.CurrentProject.Model.FilePath);
        Assert.False(shell.CurrentProject.Model.IsDirty);
    }

    [Fact]
    public async Task NothingToSaveWithoutPhotos()
    {
        var (_, files) = Create();

        await _ui.RunAsync(() => files.SaveCommand.ExecuteAsync(null));

        Assert.Equal(0, _picker.SaveAsks);
        Assert.False(files.CanSave);
    }

    [Fact]
    public async Task OpenReplacesTheProjectWithTheFilesSettingsAndThePhotosThatAreThere()
    {
        var (shell, files) = Create();
        _disk.Facts[@"D:\Shoot\1.jpg"] = new FileFacts(10, _written);
        _disk.Facts[@"D:\Shoot\3.jpg"] = new FileFacts(30, _written);
        _disk.Texts[@"D:\Shoot.10lapse"] = ProjectFileJson.Write(new ProjectFileContent
        {
            Frames = [new(@"D:\Shoot\3.jpg"), new(@"D:\Shoot\2.jpg"), new(@"D:\Shoot\1.jpg")],
            Frame = new FrameSettings(FrameAspect.Ratio1x1, FitMode.Fit),
            SortMode = SortMode.Manual,
        });
        var first = shell.CurrentProject;

        Assert.True(await OnUi(() => files.OpenPathAsync(@"D:\Shoot.10lapse")));

        var project = shell.CurrentProject.Model;
        Assert.NotSame(first, shell.CurrentProject);
        Assert.True(first.IsDisposed);
        Assert.Equal([@"D:\Shoot\3.jpg", @"D:\Shoot\1.jpg"], project.Frames.Select(frame => frame.SourcePath));
        Assert.Equal(new FrameSettings(FrameAspect.Ratio1x1, FitMode.Fit), project.FrameSettings);
        Assert.Equal(SortMode.Manual, project.SortMode);
        Assert.Equal("Shoot", project.Name);
        Assert.False(project.IsDirty);
        Assert.Equal([@"D:\Shoot.10lapse"], _recent.RecentProjects);

        var missing = Assert.Single(_infoBars.Shown);
        Assert.Equal(ProjectFileText.Missing(1), missing.Message);
        Assert.Equal("2.jpg", Assert.Single(missing.Details).Text);
    }

    [Fact]
    public async Task AProjectNoneOfWhosePhotosAreThereDoesNotOpenAndItsFileStaysAsItIs()
    {
        var (shell, files) = Create();
        var text = ProjectFileJson.Write(new ProjectFileContent
        {
            Frames = [new(@"E:\Alps\1.jpg"), new(@"E:\Alps\2.jpg")],
            SortMode = SortMode.Manual,
        });
        _disk.Texts[@"D:\Alps.10lapse"] = text;
        var before = shell.CurrentProject;

        Assert.False(await OnUi(() => files.OpenPathAsync(@"D:\Alps.10lapse")));

        Assert.Same(before, shell.CurrentProject);
        Assert.Null(shell.CurrentProject.Model.FilePath);
        Assert.Empty(_recent.RecentProjects);
        Assert.Empty(_dialogs.Chosen);
        var bar = Assert.Single(_infoBars.Shown);
        Assert.Equal(InfoBarSeverity.Caution, bar.Severity);
        Assert.Equal(ProjectFileText.NoneFound(2, "Alps"), bar.Message);
        Assert.Equal([(@"1.jpg", @"E:\Alps"), (@"2.jpg", @"E:\Alps")], bar.Details.Select(detail => (detail.Text, detail.Note)));
        Assert.Equal(text, _disk.Texts[@"D:\Alps.10lapse"]);
    }

    [Fact]
    public async Task PhotosThatWereNotFoundStayInTheFileWhenItIsSavedAgain()
    {
        var (shell, files) = Create();
        _disk.Facts[@"D:\Shoot\1.jpg"] = new FileFacts(10, _written);
        _disk.Texts[@"D:\Shoot.10lapse"] = ProjectFileJson.Write(new ProjectFileContent
        {
            Frames = [new(@"D:\Shoot\1.jpg"), new(@"E:\Card\2.jpg"), new(@"D:\Shoot\3.jpg")],
        });
        Assert.True(await OnUi(() => files.OpenPathAsync(@"D:\Shoot.10lapse")));
        _ui.Run(() => shell.CurrentProject.Model.Timing = TimingSettings.PerSecond(new Fraction(24, 1)));

        await _ui.RunAsync(() => files.SaveCommand.ExecuteAsync(null));

        var saved = ProjectFileJson.Read(_disk.Texts[@"D:\Shoot.10lapse"]).Content!;
        Assert.Equal([@"D:\Shoot\1.jpg", @"E:\Card\2.jpg", @"D:\Shoot\3.jpg"], saved.Frames.Select(frame => frame.Path));
        Assert.Equal(new Fraction(24, 1), saved.Timing.FramesPerSecond);
        Assert.Equal(0, _picker.SaveAsks);

        // The recovery copy carries them too.
        var copy = _ui.Run(() => files.RecoveryContent(shell.CurrentProject.Model, shell.CurrentProject.Model.Snapshot));
        Assert.Equal(3, copy.Frames.Length);
    }

    [Fact]
    public async Task SaveNeverOverwritesAFileFromANewerVersion()
    {
        var (shell, files) = Create();
        _disk.Facts[@"D:\Shoot\1.jpg"] = new FileFacts(10, _written);
        var newer = """{ "format": "10Lapse project", "version": 9, "deflicker": { "strength": 3 }, "frames": [ { "path": "D:\\Shoot\\1.jpg" } ] }""";
        _disk.Texts[@"D:\Newer.10lapse"] = newer;
        Assert.True(await OnUi(() => files.OpenPathAsync(@"D:\Newer.10lapse")));
        _infoBars.Shown.Clear();
        _ui.Run(() => shell.CurrentProject.Model.Timing = TimingSettings.PerSecond(new Fraction(24, 1)));
        _picker.SavePath = @"D:\Copy.10lapse";

        await _ui.RunAsync(() => files.SaveCommand.ExecuteAsync(null));

        Assert.Equal(1, _picker.SaveAsks);
        Assert.Equal(newer, _disk.Texts[@"D:\Newer.10lapse"]);
        Assert.True(_disk.Texts.ContainsKey(@"D:\Copy.10lapse"));
        Assert.Equal(ProjectFileText.NewerSaveAs(@"D:\Newer.10lapse"), Assert.Single(_infoBars.Shown).Message);

        // Tied to the copy now, its own file: Save writes it without asking.
        _ui.Run(() => shell.CurrentProject.Model.Timing = TimingSettings.PerSecond(new Fraction(25, 1)));
        await _ui.RunAsync(() => files.SaveCommand.ExecuteAsync(null));
        Assert.Equal(1, _picker.SaveAsks);
    }

    [Fact]
    public async Task OnlineOnlyPhotosOpenAndSaySoOnce()
    {
        var (shell, files) = Create();
        _disk.Facts[@"D:\Shoot\1.jpg"] = new FileFacts(4_000_000, _written, IsCloudOnly: true);
        _disk.Facts[@"D:\Shoot\2.jpg"] = new FileFacts(6_000_000, _written, IsCloudOnly: true);
        _disk.Texts[@"D:\Shoot.10lapse"] = ProjectFileJson.Write(new ProjectFileContent { Frames = [new(@"D:\Shoot\1.jpg"), new(@"D:\Shoot\2.jpg")] });

        Assert.True(await OnUi(() => files.OpenPathAsync(@"D:\Shoot.10lapse")));

        Assert.Equal(2, shell.CurrentProject.Model.Frames.Length);
        Assert.Equal(ProjectFileText.CloudOnly(2, 10_000_000), Assert.Single(_infoBars.Shown).Message);
    }

    [Fact]
    public async Task DuringAnExportOpenAndRestoreAskNothing()
    {
        var (shell, files) = Create();
        _ui.Run(() => shell.CurrentProject.Model.AddFrames(Photos(2)));
        _disk.Texts[@"D:\Other.10lapse"] = ProjectFileJson.Write(new ProjectFileContent());
        _picker.OpenPath = @"D:\Other.10lapse";
        _ui.Run(() => { shell.Edits.Close(); return 0; });

        await _ui.RunAsync(() => files.OpenCommand.ExecuteAsync(null));
        Assert.False(await OnUi(() => files.OpenPathAsync(@"D:\Other.10lapse")));

        Assert.Equal(0, _picker.OpenAsks);
        Assert.Empty(_dialogs.Chosen);
    }

    [Theory]
    [InlineData(ChoiceResult.Primary, true)]
    [InlineData(ChoiceResult.Secondary, true)]
    [InlineData(ChoiceResult.Cancel, false)]
    public async Task NewProjectWithUnsavedChangesAsksToSaveFirst(ChoiceResult answer, bool replaced)
    {
        var (shell, files) = Create();
        _ui.Run(() => shell.CurrentProject.Model.AddFrames(Photos(2)));
        var before = shell.CurrentProject;
        var name = before.Name;
        _dialogs.Choice = answer;
        _picker.SavePath = @"C:\Projects\Harbor.10lapse";

        await _ui.RunAsync(() => shell.NewProjectCommand.ExecuteAsync(null));

        Assert.Equal(ProjectFileText.UnsavedQuestion(name).Title, Assert.Single(_dialogs.Chosen).Title);
        Assert.Empty(_dialogs.Asked);
        Assert.Equal(replaced, !ReferenceEquals(before, shell.CurrentProject));
        Assert.Equal(answer == ChoiceResult.Primary, _disk.Texts.ContainsKey(@"C:\Projects\Harbor.10lapse"));
        GC.KeepAlive(files);
    }

    [Fact]
    public async Task OpeningOverUnsavedChangesAsksAndCancelKeepsThem()
    {
        var (shell, files) = Create();
        _ui.Run(() => shell.CurrentProject.Model.AddFrames(Photos(3)));
        _disk.Texts[@"D:\Other.10lapse"] = ProjectFileJson.Write(new ProjectFileContent());
        _dialogs.Choice = ChoiceResult.Cancel;
        var before = shell.CurrentProject;

        Assert.False(await OnUi(() => files.OpenPathAsync(@"D:\Other.10lapse")));

        Assert.Same(before, shell.CurrentProject);
        Assert.Equal(ProjectFileText.UnsavedQuestion(before.Name).Title, Assert.Single(_dialogs.Chosen).Title);

        _dialogs.Choice = ChoiceResult.Secondary;
        Assert.True(await OnUi(() => files.OpenPathAsync(@"D:\Other.10lapse")));
        Assert.NotSame(before, shell.CurrentProject);
    }

    [Theory]
    [InlineData("{ not a project")]
    [InlineData("""{ "settings": 1 }""")]
    [InlineData("""{ "format": "10Lapse project", "format": "10Lapse project", "version": 1 }""")]
    public async Task AFileThatIsNotAProjectSaysSoAndChangesNothing(string text)
    {
        var (shell, files) = Create();
        _disk.Texts[@"D:\Broken.10lapse"] = text;
        var before = shell.CurrentProject;

        Assert.False(await OnUi(() => files.OpenPathAsync(@"D:\Broken.10lapse")));

        Assert.Same(before, shell.CurrentProject);
        var bar = Assert.Single(_infoBars.Shown);
        Assert.Equal(InfoBarSeverity.Critical, bar.Severity);
        Assert.Equal(ProjectFileText.OpenNotAProject(@"D:\Broken.10lapse"), bar.Message);
    }

    [Fact]
    public async Task AFileThatIsGoneSaysSoAndLeavesTheRecentList()
    {
        var (_, files) = Create();
        _recent.AddRecentProject(@"D:\Gone.10lapse");

        Assert.False(await OnUi(() => files.OpenPathAsync(@"D:\Gone.10lapse")));

        Assert.Equal(ProjectFileText.OpenUnreadable(@"D:\Gone.10lapse"), Assert.Single(_infoBars.Shown).Message);
        Assert.Empty(_recent.RecentProjects);
    }

    [Fact]
    public async Task ANewerFileOpensAndSaysWhatItLeftOut()
    {
        var (_, files) = Create();
        _disk.Texts[@"D:\Newer.10lapse"] = """{ "format": "10Lapse project", "version": 9 }""";

        Assert.True(await OnUi(() => files.OpenPathAsync(@"D:\Newer.10lapse")));

        var bar = Assert.Single(_infoBars.Shown);
        Assert.Equal(InfoBarSeverity.Caution, bar.Severity);
        Assert.Equal(ProjectFileText.OpenNewerVersion(@"D:\Newer.10lapse"), bar.Message);
    }

    [Theory]
    [InlineData(ChoiceResult.Secondary, true, false)]
    [InlineData(ChoiceResult.Cancel, false, false)]
    [InlineData(ChoiceResult.Primary, true, true)]
    public async Task ClosingWithUnsavedChangesAsksToSave(ChoiceResult answer, bool closes, bool saved)
    {
        var (shell, files) = Create();
        _ui.Run(() => shell.CurrentProject.Model.AddFrames(Photos(2)));
        _dialogs.Choice = answer;
        _picker.SavePath = @"C:\Projects\Harbor.10lapse";

        Assert.True(files.HasUnsavedWork);
        Assert.Equal(closes, await OnUi(files.ConfirmCloseAsync));
        Assert.Equal(saved, _disk.Texts.ContainsKey(@"C:\Projects\Harbor.10lapse"));
    }

    [Fact]
    public async Task ASavedProjectClosesWithoutAQuestion()
    {
        var (_, files) = Create();

        Assert.True(await OnUi(files.ConfirmCloseAsync));
        Assert.Empty(_dialogs.Chosen);
    }

    [Fact]
    public async Task ACopyLeftBehindIsOfferedAndRestoredUnsavedWithItsName()
    {
        var (shell, files) = Create();
        _disk.Facts[@"D:\Shoot\1.jpg"] = new FileFacts(10, _written);
        _recovery.Orphan = new RecoveryCopy(
            ProjectFileJson.Write(new ProjectFileContent { Frames = [new(@"D:\Shoot\1.jpg")], RecoveredName = "Harbor sunset" }),
            _written);

        await _ui.RunAsync(files.OfferRecoveryAsync);
        var offer = Assert.Single(_infoBars.Shown);
        Assert.Equal(ProjectFileText.RecoveryOffer("Harbor sunset"), offer.Message);
        Assert.Equal(ProjectFileText.RecoveryRestore, offer.ActionLabel);

        await _ui.RunAsync(offer.Action!);

        var project = shell.CurrentProject.Model;
        Assert.Equal("Harbor sunset", project.Name);
        Assert.Single(project.Frames);
        Assert.True(project.IsDirty);
        Assert.Null(project.FilePath);
        Assert.True(_recovery.OfferRestored);
    }

    [Fact]
    public async Task NoCopyNoOffer()
    {
        var (_, files) = Create();

        await _ui.RunAsync(files.OfferRecoveryAsync);

        Assert.Empty(_infoBars.Shown);
    }

    [Fact]
    public void TheAutosaveKeepsACopyOfUnsavedWorkOnlyAndClearsOnClose()
    {
        var (shell, _) = Create();
        var autosave = _ui.Run(() =>
        {
            var created = new ProjectAutosave(shell, _recovery, _errors, debounce: TimeSpan.FromDays(1));
            created.Attach();
            return created;
        });

        autosave.WriteNow();
        Assert.Null(_recovery.Saved);

        _ui.Run(() => shell.CurrentProject.Model.AddFrames(Photos(2)));
        autosave.WriteNow();
        Assert.Equal(2, ProjectFileJson.Read(_recovery.Saved).Content!.Frames.Length);

        _ui.Run(() =>
        {
            var project = shell.CurrentProject.Model;
            project.SetFile(@"C:\Projects\Harbor.10lapse");
            return project.MarkSaved(project.Version);
        });
        autosave.WriteNow();
        Assert.Null(_recovery.Saved);

        _ui.Run(() => { autosave.Dispose(); return 0; });
        Assert.True(_recovery.Disposed);
    }

    [Fact]
    public void AfterAFailedCloseQuestionTheCopyIsWrittenAndKept()
    {
        var (shell, _) = Create();
        var autosave = _ui.Run(() =>
        {
            var created = new ProjectAutosave(shell, _recovery, _errors, debounce: TimeSpan.FromDays(1));
            created.Attach();
            shell.CurrentProject.Model.AddFrames(Photos(2));
            return created;
        });

        _ui.Run(() => { autosave.KeepForNextLaunch(); autosave.Dispose(); return 0; });

        Assert.NotNull(_recovery.Saved);
        Assert.True(_recovery.Left);
        Assert.False(_recovery.Disposed);
    }

    [Fact]
    public async Task TheRecentListShowsMissingFilesAndAClickOnOneTakesItOff()
    {
        var (_, files) = Create();
        _disk.Texts[@"D:\Here.10lapse"] = ProjectFileJson.Write(new ProjectFileContent());
        _disk.Facts[@"D:\Here.10lapse"] = new FileFacts(1, _written);
        _recent.AddRecentProject(@"D:\Gone.10lapse");
        _recent.AddRecentProject(@"D:\Here.10lapse");
        var recent = _ui.Run(() => new RecentProjectsViewModel(_recent, files, _disk, _infoBars, _errors));

        await _ui.RunAsync(recent.RefreshAsync);

        Assert.Equal(["Here", "Gone"], recent.Items.Select(item => item.Name));
        Assert.False(recent.Items[0].IsMissing);
        Assert.True(recent.Items[1].IsMissing);
        Assert.Equal(ProjectFileText.RecentNotFound, recent.Items[1].Detail);

        await _ui.RunAsync(() => recent.Items[1].OpenCommand.ExecuteAsync(null));

        Assert.Equal([@"D:\Here.10lapse"], _recent.RecentProjects);
        Assert.Equal(ProjectFileText.RecentGone("Gone"), Assert.Single(_infoBars.Shown).Message);
        Assert.Single(recent.Items);

        _ui.Run(() => { recent.ClearCommand.Execute(null); return 0; });
        Assert.False(recent.HasItems);
    }

    [Theory]
    [InlineData(new[] { "10Lapse.exe", @"C:\Projects\Harbor.10lapse" }, @"C:\Projects\Harbor.10lapse")]
    [InlineData(new[] { "10Lapse.exe", "--lang=fr", @"C:\Projects\Harbor.10LAPSE" }, @"C:\Projects\Harbor.10LAPSE")]
    [InlineData(new[] { "10Lapse.exe", "----AppNotificationActivated:" }, null)]
    [InlineData(new[] { "10Lapse.exe", @"C:\Photos\IMG_0001.jpg" }, null)]
    [InlineData(new[] { @"C:\Projects\Harbor.10lapse" }, null)]
    public void TheLaunchFileIsTheFirstProjectArgument(string[] args, string? expected) =>
        Assert.Equal(expected, LaunchArguments.ProjectFile(args));

    private Task<T> OnUi<T>(Func<Task<T>> work) => _ui.InvokeAsync(work).Unwrap();

    private static Frame[] Photos(int count) =>
        [.. Enumerable.Range(1, count).Select(i => new Frame($@"C:\Photos\IMG_{i:0000}.jpg", 10, _written))];

    private (ShellViewModel Shell, ProjectFilesViewModel Files) Create() => _ui.Run(() =>
    {
        var shell = new ShellViewModel(_dialogs, _errors);
        return (shell, new ProjectFilesViewModel(shell, _picker, _disk, _recent, _recovery, _dialogs, _infoBars, _errors, _log));
    });

    private sealed class MemoryDisk : IProjectFileSystem
    {
        public Dictionary<string, string> Texts { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, FileFacts> Facts { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool FailWrites { get; set; }

        public bool FolderGone { get; set; }

        public string ReadText(string path) =>
            Texts.TryGetValue(path, out var text) ? text : throw new FileNotFoundException("Not there.", path);

        public void WriteText(string path, string text)
        {
            if (FailWrites)
            {
                throw new UnauthorizedAccessException("Read only.");
            }

            if (FolderGone)
            {
                throw new DirectoryNotFoundException("No folder.");
            }

            Texts[path] = text;
        }

        public FileFacts? Probe(string path) => Facts.TryGetValue(path, out var facts) ? facts : null;
    }

    private sealed class FakePicker : IProjectFilePicker
    {
        public string? OpenPath { get; set; }

        public string? SavePath { get; set; }

        public int SaveAsks { get; private set; }

        public int OpenAsks { get; private set; }

        public Task<string?> PickOpenAsync(string? startFolder)
        {
            OpenAsks++;
            return Task.FromResult(OpenPath);
        }

        public Task<string?> PickSaveAsync(string suggestedName, string? startFolder)
        {
            SaveAsks++;
            return Task.FromResult(SavePath);
        }
    }

    private sealed class FakeRecent : IRecentProjects
    {
        public PathList RecentProjects { get; private set; } = PathList.Empty;

        public event EventHandler? RecentProjectsChanged;

        public void AddRecentProject(string path) => Set(RecentProjects.WithFirst(path, UserSettings.MaxRecentProjects));

        public void RemoveRecentProject(string path) => Set(RecentProjects.Without(path));

        public void ClearRecentProjects() => Set(PathList.Empty);

        public Task RefreshRecentProjectsAsync() => Task.CompletedTask;

        private void Set(PathList list)
        {
            RecentProjects = list;
            RecentProjectsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FakeRecovery : IRecoveryStore
    {
        public RecoveryCopy? Orphan { get; set; }

        public string? Saved { get; private set; }

        public bool OfferRestored { get; private set; }

        public bool Disposed { get; private set; }

        public void Save(string text) => Saved = text;

        public void Clear() => Saved = null;

        public RecoveryCopy? TakeOrphan()
        {
            var orphan = Orphan;
            Orphan = null;
            return orphan;
        }

        public void MarkOfferRestored() => OfferRestored = true;

        public bool Left { get; private set; }

        public void Leave() => Left = true;

        public void Dispose() => Disposed = true;
    }
}
