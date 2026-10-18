using TenLapse.Tests.Media;
using Windows.Graphics.Imaging;

namespace TenLapse.Tests.Fixtures;

/// <summary>
/// The awkward files too big or too stateful to commit, made in a temp folder per instance: a locked file, a file
/// deleted after it was listed, a path past MAX_PATH with non-Latin characters, a read-only file, and (once per test
/// run, shared) a 100 MP JPEG.
/// </summary>
internal sealed class RuntimeFixtures : IDisposable
{
    public const int HugeSide = 10_000;

    private static readonly Lazy<Task<string>> _huge = new(MakeHugeAsync);

    private readonly PhotoFixtures _files = new();
    private readonly List<IDisposable> _locks = [];
    private readonly List<string> _readOnly = [];

    /// <summary>The folder the files of this instance live in.</summary>
    public string Directory => _files.Directory;

    /// <summary>A 10000x10000 JPEG, red top-left and blue elsewhere, written on first use and deleted when the test run ends.</summary>
    public static Task<string> HugeJpegAsync() => _huge.Value;

    /// <summary>A small JPEG that another stream holds open with <see cref="FileShare.None"/> until this object is disposed.</summary>
    public async Task<string> LockedAsync(string name = "locked.jpg")
    {
        var path = await _files.WriteAsync(name, BitmapEncoder.JpegEncoderId);
        _locks.Add(new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
        return path;
    }

    /// <summary>The path of a JPEG that existed (a listing found it) and was deleted before anything read it.</summary>
    public async Task<string> DeletedAfterListingAsync(string name = "deleted.jpg")
    {
        var path = await _files.WriteAsync(name, BitmapEncoder.JpegEncoderId);
        Assert.Contains(path, System.IO.Directory.EnumerateFiles(Directory));
        File.Delete(path);
        return path;
    }

    /// <summary>A JPEG in a folder tree past 260 characters, with accents, CJK and an emoji in the names.</summary>
    public async Task<string> LongUnicodeAsync()
    {
        var directory = Directory;
        while (directory.Length < 300)
        {
            directory = Path.Combine(directory, "Fotos für den Zeitraffer – 夕焼け");
        }

        System.IO.Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Été 2026 – 日の出 🌅.jpg");
        await PhotoFixtures.WriteToAsync(path, BitmapEncoder.JpegEncoderId);
        Assert.True(path.Length > 260);
        return path;
    }

    /// <summary>A small JPEG with the read-only attribute.</summary>
    public async Task<string> ReadOnlyAsync(string name = "read-only.jpg")
    {
        var path = await _files.WriteAsync(name, BitmapEncoder.JpegEncoderId);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        _readOnly.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var held in _locks)
        {
            held.Dispose();
        }

        foreach (var path in _readOnly)
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }

        _files.Dispose();
    }

    private static async Task<string> MakeHugeAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TenLapse.Tests", "huge-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                System.IO.Directory.Delete(directory, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Windows cleans the temp folder eventually.
            }
        };

        var path = Path.Combine(directory, "100mp.jpg");
        await PhotoFixtures.WriteToAsync(path, BitmapEncoder.JpegEncoderId, HugeSide, HugeSide).ConfigureAwait(false);
        return path;
    }
}
