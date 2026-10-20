using System.Globalization;
using System.Runtime.CompilerServices;

namespace TenLapse.Tests.Localization;

/// <summary>
/// Every test runs in en-US, for the UI language and for number and date formats (#113), whatever the machine says:
/// the dev PC runs en-CH formats, a tester's PC may run French. A test that needs another culture sets it itself and
/// puts it back (see <c>OutputPanelViewModelTests.TheEstimateIsWrittenInTheCurrentCulture</c>).
/// </summary>
internal static class TestCulture
{
    /// <summary>The culture the tests run in.</summary>
    public static CultureInfo English { get; } = CultureInfo.GetCultureInfo("en-US");

#pragma warning disable CA2255 // A module initializer in a test project is the one place that runs before every test.
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Pin()
    {
        // The defaults reach the thread-pool threads xUnit runs the tests on; the current ones this thread.
        CultureInfo.DefaultThreadCurrentCulture = English;
        CultureInfo.DefaultThreadCurrentUICulture = English;
        CultureInfo.CurrentCulture = English;
        CultureInfo.CurrentUICulture = English;
    }
}
