using System.Collections;
using System.Collections.Immutable;

namespace TenLapse.Core.Settings;

/// <summary>
/// An ordered list of file paths that compares by its items (ignoring case, as Windows does), so a record that holds one
/// (<see cref="UserSettings.RecentProjects"/>) keeps value equality. Immutable.
/// </summary>
public sealed class PathList : IReadOnlyList<string>, IEquatable<PathList>
{
    private readonly ImmutableArray<string> _paths;

    private PathList(ImmutableArray<string> paths) => _paths = paths;

    /// <summary>No paths.</summary>
    public static PathList Empty { get; } = new([]);

    /// <inheritdoc />
    public int Count => _paths.Length;

    /// <inheritdoc />
    public string this[int index] => _paths[index];

    /// <summary>The list of <paramref name="paths"/> in order, each once (the first spelling of a path wins).</summary>
    public static PathList Of(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = ImmutableArray.CreateBuilder<string>();
        foreach (var path in paths)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path, nameof(paths));
            if (seen.Add(path))
            {
                kept.Add(path);
            }
        }

        return kept.Count == 0 ? Empty : new PathList(kept.ToImmutable());
    }

    /// <summary>
    /// This list with <paramref name="path"/> first (moved there when it was already in the list), cut to
    /// <paramref name="limit"/> paths: the most recently used first.
    /// </summary>
    public PathList WithFirst(string path, int limit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        return Of([path, .. _paths.Where(item => !string.Equals(item, path, StringComparison.OrdinalIgnoreCase))]).Take(limit);
    }

    /// <summary>This list without <paramref name="path"/> (compared ignoring case).</summary>
    public PathList Without(string path) =>
        Of(_paths.Where(item => !string.Equals(item, path, StringComparison.OrdinalIgnoreCase)));

    /// <summary>The first <paramref name="count"/> paths.</summary>
    public PathList Take(int count) => count >= Count ? this : Of(_paths.Take(count));

    /// <inheritdoc />
    public IEnumerator<string> GetEnumerator() => ((IEnumerable<string>)_paths).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public bool Equals(PathList? other) =>
        other is not null && _paths.SequenceEqual(other._paths, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as PathList);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var path in _paths)
        {
            hash.Add(path, StringComparer.OrdinalIgnoreCase);
        }

        return hash.ToHashCode();
    }
}
