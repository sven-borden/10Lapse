using TenLapse.Presentation.Services;

namespace TenLapse.Tests.Presentation;

public sealed class DialogQueueTests
{
    [Fact]
    public async Task ASecondQuestionWaitsForTheFirstInsteadOfReadingAsDeclined()
    {
        var queue = new DialogQueue();
        var firstAnswer = new TaskCompletionSource<bool>();
        var secondAnswer = new TaskCompletionSource<bool>();
        var shown = new List<string>();
        var secondShown = new TaskCompletionSource();

        var first = queue.RunAsync(Free, () => Show("first", firstAnswer));
        var second = queue.RunAsync(Free, () =>
        {
            secondShown.SetResult();
            return Show("second", secondAnswer);
        });

        Assert.Equal(["first"], shown);
        Assert.False(second.IsCompleted);
        firstAnswer.SetResult(true);
        Assert.True(await first);
        await secondShown.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(["first", "second"], shown);
        secondAnswer.SetResult(false);
        Assert.False(await second);

        Task<bool> Show(string name, TaskCompletionSource<bool> answer)
        {
            lock (shown)
            {
                shown.Add(name);
            }

            return answer.Task;
        }
    }

    [Fact]
    public async Task AForeignDialogIsWaitedForBeforeTheQuestionShows()
    {
        var queue = new DialogQueue();
        var foreignClosed = new TaskCompletionSource();
        var shown = false;

        var asking = queue.RunAsync(() => foreignClosed.Task, () =>
        {
            shown = true;
            return Task.FromResult(true);
        });

        Assert.False(shown);
        foreignClosed.SetResult();
        Assert.True(await asking);
        Assert.True(shown);
    }

    [Fact]
    public async Task AQuestionThatFailsToShowPassesTheTurnOn()
    {
        var queue = new DialogQueue();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => queue.RunAsync<bool>(Free, () => throw new InvalidOperationException("No XAML root.")));
        var next = await queue.RunAsync(Free, () => Task.FromResult(true));

        Assert.True(next);
    }

    private static Task Free() => Task.CompletedTask;
}
