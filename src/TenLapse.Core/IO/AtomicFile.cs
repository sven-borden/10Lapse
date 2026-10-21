using System.Text;

namespace TenLapse.Core.IO;

/// <summary>
/// Writes a whole text file so that a crash, a kill or a full disk never leaves half of it (#21's settings, #52's project
/// and recovery files): the text goes to a temporary file beside the target first, is flushed to the disk, and then
/// replaces the target in one rename. The old file stays intact until the new one is complete.
/// </summary>
public static class AtomicFile
{
    private static readonly UTF8Encoding _utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Writes <paramref name="text"/> as UTF-8 (no byte order mark) to <paramref name="path"/>, creating its folder when it
    /// is missing. Throws when the write fails (a locked file, a full or read-only disk); the target is then unchanged and
    /// no temporary file is left behind.
    /// </summary>
    public static void WriteAllText(string path, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(text);
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        // A name of its own, so two running copies of the app never write the same temporary file.
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(_utf8.GetBytes(text));
                stream.Flush(flushToDisk: true);
            }

            // The data is on disk already (Flush above); the rename swaps it in whole or not at all.
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            // After a good write the temporary file is gone already; after a failed one it must not stay behind.
            try
            {
                File.Delete(temporary);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Nothing more to do for a leftover temporary file; the write's own error is what matters.
            }
        }
    }
}
