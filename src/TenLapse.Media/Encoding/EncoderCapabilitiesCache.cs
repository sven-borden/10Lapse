using System.Text.Json;
using System.Text.Json.Serialization;

namespace TenLapse.Media.Encoding;

/// <summary>
/// The probe result on disk: one JSON file at a path the caller chooses (the app's local data folder). It is
/// used only while its <see cref="EncoderCacheKey"/> matches this PC; a file that is missing, unreadable,
/// corrupt, from another schema or for another key is ignored, and the next probe rewrites it.
/// </summary>
public static class EncoderCapabilitiesCache
{
    /// <summary>The cached capabilities when the file holds a result for <paramref name="key"/>; otherwise null. Never throws for a bad file.</summary>
    public static EncoderCapabilities? TryRead(string path, EncoderCacheKey key)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(key);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var file = JsonSerializer.Deserialize(stream, CacheJson.Default.CacheFile);
            if (file is null || file.Key != key)
            {
                return null;
            }

            return new EncoderCapabilities(file.Encoders, file.Probes, TimeSpan.FromTicks(file.ProbeTicks), decoders: file.Decoders);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Corrupt, truncated, locked or from a build with other types: probe again and overwrite it.
            return null;
        }
    }

    /// <summary>
    /// Writes the result for <paramref name="key"/>, creating the folder. The file is replaced whole (written
    /// beside it, then moved), so a crash mid-write leaves the old file or none, never half a file.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="capabilities"/> is provisional: it must not be cached.</exception>
    /// <exception cref="IOException">The file cannot be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder is not writable.</exception>
    public static void Write(string path, EncoderCacheKey key, EncoderCapabilities capabilities)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(capabilities);
        if (capabilities.IsProvisional)
        {
            throw new ArgumentException("A provisional result (no device, or a transient failure) is not cached.", nameof(capabilities));
        }

        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temporary = $"{full}.{Guid.NewGuid():N}.tmp";
        try
        {
            var file = new CacheFile(key, capabilities.Encoders, capabilities.Decoders, capabilities.Probes, capabilities.ProbeDuration.Ticks);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, file, CacheJson.Default.CacheFile);
            }

            File.Move(temporary, full, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}

/// <summary>The cache file's layout.</summary>
internal sealed record CacheFile(
    EncoderCacheKey Key,
    IReadOnlyList<EncoderMft> Encoders,
    IReadOnlyList<DecoderMft> Decoders,
    IReadOnlyList<ProbedSize> Probes,
    long ProbeTicks);

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(CacheFile))]
internal sealed partial class CacheJson : JsonSerializerContext;
