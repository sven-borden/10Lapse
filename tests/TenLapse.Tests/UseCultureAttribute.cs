using System.Globalization;
using System.Reflection;
using Xunit.v3;

namespace TenLapse.Tests;

/// <summary>
/// Runs each test of a class or method with a fixed regional format and UI language (#114), and puts the thread's own
/// back after it. The text classes write numbers in the current culture, and this PC runs en-CH regional formats
/// ("5’000"), so a test that pins English text with grouped numbers sets en-US. Tests that need another culture pass it
/// explicitly or set their own.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class UseCultureAttribute(string culture) : BeforeAfterTestAttribute
{
    private readonly CultureInfo _culture = CultureInfo.GetCultureInfo(culture);
    private CultureInfo? _culturesBefore;
    private CultureInfo? _uiCultureBefore;

    /// <summary>The culture the tests run in.</summary>
    public string Culture => _culture.Name;

    /// <inheritdoc/>
    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        _culturesBefore = CultureInfo.CurrentCulture;
        _uiCultureBefore = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _culture;
    }

    /// <inheritdoc/>
    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        CultureInfo.CurrentCulture = _culturesBefore ?? CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = _uiCultureBefore ?? CultureInfo.InvariantCulture;
    }
}
