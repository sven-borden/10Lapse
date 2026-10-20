using System.Diagnostics;
using System.Text.RegularExpressions;
using TenLapse.Tests.Design;

namespace TenLapse.Tests.Code;

/// <summary>
/// The version bump of <c>scripts/pack.ps1 -Version</c> (#40): every flight or Store upload needs a version above the
/// last one and a last digit of 0. The script runs with <c>-BumpOnly</c> on a copy of the real manifest, so no build
/// starts and the repository's manifest is never touched.
/// </summary>
public sealed partial class PackVersionTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "TenLapse.PackVersionTests", Guid.NewGuid().ToString("N"));
    private readonly string _manifest;
    private readonly string _original;

    public PackVersionTests()
    {
        Directory.CreateDirectory(_folder);
        _manifest = Path.Combine(_folder, "Package.appxmanifest");
        File.Copy(Path.Combine(DesignFiles.Root, "src", "TenLapse.App", "Package.appxmanifest"), _manifest);
        _original = File.ReadAllText(_manifest);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task ABumpAboveTheManifestWritesOnlyTheVersion()
    {
        var (exit, _) = await BumpAsync("99.0.1.0");

        Assert.Equal(0, exit);
        var written = File.ReadAllText(_manifest);
        Assert.Equal("99.0.1.0", IdentityVersion(written));
        Assert.Equal(_original.Replace("Version=\"" + IdentityVersion(_original) + "\"", "Version=\"99.0.1.0\"", StringComparison.Ordinal), written);
    }

    [Theory]
    [InlineData("3.00.1.0")]
    [InlineData("00003.0.1.0")]
    public async Task LeadingZerosAreWrittenWithoutThem(string version)
    {
        var (exit, _) = await BumpAsync(version);

        Assert.Equal(0, exit);
        Assert.Equal("3.0.1.0", IdentityVersion(File.ReadAllText(_manifest)));
    }

    [Fact]
    public void TheManifestVersionEndsInZeroAndIsAboveTheLegacyStoreVersion()
    {
        // The shipped manifest is what the next flight uploads: it must already be a valid Store version.
        var version = System.Version.Parse(IdentityVersion(_original));

        Assert.Equal(0, version.Revision);
        Assert.True(version > new System.Version(2, 1, 1, 0));
    }

    [Theory]
    [InlineData("3.0.0.0", "not above")]
    [InlineData("2.9.9.0", "not above")]
    [InlineData("99.0.1.1", "must end in .0")]
    [InlineData("99.0.1", "four numbers")]
    [InlineData("99.0.1.0.0", "four numbers")]
    [InlineData("v99.0.1.0", "four numbers")]
    [InlineData("99.0.-1.0", "four numbers")]
    [InlineData("99.0.70000.0", "above 65535")]
    public async Task ABadVersionIsRefusedAndLeavesTheManifestAlone(string version, string reason)
    {
        var (exit, output) = await BumpAsync(version);

        Assert.Equal(1, exit);
        Assert.Contains("pack: FAILED", output, StringComparison.Ordinal);
        Assert.Contains(reason, output, StringComparison.Ordinal);
        Assert.Equal(_original, File.ReadAllText(_manifest));
    }

    private async Task<(int Exit, string Output)> BumpAsync(string version)
    {
        var script = Path.Combine(DesignFiles.Root, "scripts", "pack.ps1");
        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", script, "-Version", version, "-BumpOnly", "-ManifestPath", _manifest })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("scripts/pack.ps1 -BumpOnly did not finish in 60 s.");
        }

        return (process.ExitCode, await output + await error);
    }

    private static string IdentityVersion(string manifest) =>
        IdentityVersionPattern().Match(manifest) is { Success: true } match
            ? match.Groups[1].Value
            : throw new InvalidOperationException("The manifest has no Identity Version.");

    [GeneratedRegex("<Identity\\b[^>]*?\\sVersion=\"([^\"]+)\"", RegexOptions.Singleline)]
    private static partial Regex IdentityVersionPattern();
}
