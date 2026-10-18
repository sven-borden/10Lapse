namespace TenLapse.Shell;

/// <summary>Small functions for x:Bind in the shell's XAML.</summary>
public static class Bind
{
    /// <summary>The opposite of <paramref name="value"/>, for a state trigger that is active while something is false.</summary>
    public static bool Not(bool value) => !value;
}
