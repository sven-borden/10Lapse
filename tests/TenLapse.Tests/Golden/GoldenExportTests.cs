using System.Reflection;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Export;

namespace TenLapse.Tests.Golden;

/// <summary>The golden tests' own helpers, which need no GPU.</summary>
public sealed class GoldenExportTests
{
    [Fact]
    public void ResizedCopiesEverySettingButTheSize()
    {
        // Every settable property gets a value other than its default, so a property Resized forgets shows as a
        // difference. A new property type needs a value here; the test says which.
        var settings = new VideoExportSettings(1280, 720, new Fraction(30000, 1001));
        foreach (var property in typeof(VideoExportSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(property => property.SetMethod is not null))
        {
            property.SetValue(settings, Other(property, property.GetValue(settings)));
        }

        var resized = GoldenExport.Resized(settings, new PixelSize(568, 320));

        Assert.Equal((568, 320), (resized.Width, resized.Height));
        var forgotten = typeof(VideoExportSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.Name is not (nameof(VideoExportSettings.Width) or nameof(VideoExportSettings.Height)))
            .Where(property => !Equals(property.GetValue(settings), property.GetValue(resized)))
            .Select(property => $"{property.Name} ({property.GetValue(settings)} became {property.GetValue(resized)})")
            .ToList();
        Assert.True(forgotten.Count == 0, $"GoldenExport.Resized does not copy: {string.Join(", ", forgotten)}. Copy it there, or the golden tests export with its default.");
    }

    private static object Other(PropertyInfo property, object? current)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        return type switch
        {
            _ when type.IsEnum => Enum.GetValues(type).Cast<object>().Last(value => !value.Equals(current)),
            _ when type == typeof(bool) => !(bool)current!,
            _ when type == typeof(int) => (current is int number ? number : 0) + 3,
            _ when type == typeof(uint) => (current is uint number ? number : 0u) + 12_345u,
            _ when type == typeof(TimeSpan) => (current is TimeSpan span ? span : TimeSpan.Zero) + TimeSpan.FromSeconds(7),
            _ when type == typeof(double) => (current is double number ? number : 0) + 1.5,
            _ when type == typeof(TenLapse.Media.Audio.MusicTrack) => new TenLapse.Media.Audio.MusicTrack(MusicSettings.Default with { Path = @"C:\Music\Sunrise.mp3" }, 20_000_000),
            _ => throw new InvalidOperationException($"VideoExportSettings.{property.Name} is a {type.Name}: give it a value other than its default in {nameof(ResizedCopiesEverySettingButTheSize)}."),
        };
    }
}
