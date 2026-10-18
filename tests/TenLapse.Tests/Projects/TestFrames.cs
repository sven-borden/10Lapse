using TenLapse.Core.Projects;

namespace TenLapse.Tests.Projects;

/// <summary>Frames for tests. Paths are made up; nothing touches the disk unless a test says so.</summary>
internal static class TestFrames
{
    private static readonly DateTime _written = new(2026, 5, 1, 18, 30, 0, DateTimeKind.Utc);

    public static Frame At(string path) => new(path, 1_000, _written);

    /// <summary>"C:\Photos\{folder}\IMG_0001.jpg" and on, <paramref name="count"/> of them.</summary>
    public static Frame[] Folder(string folder, int count) =>
        [.. Enumerable.Range(1, count).Select(i => At($@"C:\Photos\{folder}\IMG_{i:0000}.jpg"))];
}
