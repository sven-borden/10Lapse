using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Presentation;
using TenLapse.Tests.Projects;

namespace TenLapse.Tests.Presentation;

public sealed class ProjectViewModelTests
{
    [Fact]
    public void FollowsTheModelAndRaisesOnlyWhatChanged()
    {
        var viewModel = new ProjectViewModel(new Project(), new FakeErrorSink());
        var raised = new List<string?>();
        viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        viewModel.Model.AddFrames(TestFrames.Folder("a", 742));

        Assert.Equal(742, viewModel.FrameCount);
        Assert.True(viewModel.HasFrames);
        Assert.True(viewModel.IsDirty);
        Assert.Equal("742 photos → 24.7 s at 30 fps", viewModel.TimingReadout);
        Assert.Equal(
            [nameof(ProjectViewModel.FrameCount), nameof(ProjectViewModel.HasFrames), nameof(ProjectViewModel.IsDirty), nameof(ProjectViewModel.TimingReadout)],
            raised);

        raised.Clear();
        viewModel.Model.Timing = TimingSettings.PerSecond(new Fraction(24, 1));

        Assert.Equal([nameof(ProjectViewModel.TimingReadout)], raised);
        Assert.Equal("742 photos → 30.9 s at 24 fps", viewModel.TimingReadout);
    }

    [Fact]
    public void DisposeStopsFollowingTheModelAndDisposesResourcesNewestFirst()
    {
        var log = new List<string>();
        var viewModel = new ProjectViewModel(new Project(), new FakeErrorSink());
        viewModel.AddResource(new RecordingResource("cache", log));
        viewModel.AddResource(new RecordingResource("preview", log));

        viewModel.Dispose();
        viewModel.Dispose();

        Assert.Equal(["preview", "cache"], log);
        Assert.True(viewModel.Model.IsClosed);
        Assert.Throws<ObjectDisposedException>(() => viewModel.Model.AddFrames(TestFrames.Folder("a", 1)));
        Assert.Equal(0, viewModel.FrameCount);
    }

    [Fact]
    public void AResourceThatArrivesAfterDisposeIsDisposedAtOnce()
    {
        var viewModel = new ProjectViewModel(new Project(), new FakeErrorSink());
        viewModel.Dispose();
        var late = new FakeProxyCache { Count = 12 };

        viewModel.AddResource(late);

        Assert.True(late.IsDisposed);
    }

    [Fact]
    public void OneFailingResourceDoesNotKeepTheOthersAlive()
    {
        var log = new List<string>();
        var errors = new FakeErrorSink();
        var viewModel = new ProjectViewModel(new Project(), errors);
        viewModel.AddResource(new RecordingResource("cache", log));
        viewModel.AddResource(new RecordingResource("preview", log, throws: true));
        viewModel.AddResource(new RecordingResource("import", log, throws: true));

        var failure = Record.Exception(viewModel.Dispose);

        Assert.Null(failure);
        Assert.Equal(["import", "preview", "cache"], log);
        Assert.Equal(["import", "preview"], errors.Reported.Select(report => report.Exception.Message));
        Assert.All(errors.Reported, report => Assert.Equal(ProjectText.ClosingProjectFailed, report.Context));
        Assert.True(viewModel.IsDisposed);
    }

    [Fact]
    public void ALateResourceThatThrowsIsReportedNotThrown()
    {
        var log = new List<string>();
        var errors = new FakeErrorSink();
        var viewModel = new ProjectViewModel(new Project(), errors);
        viewModel.Dispose();

        var failure = Record.Exception(() => viewModel.AddResource(new RecordingResource("late", log, throws: true)));

        Assert.Null(failure);
        Assert.Equal(["late"], log);
        Assert.Equal("late", Assert.Single(errors.Reported).Exception.Message);
    }
}
