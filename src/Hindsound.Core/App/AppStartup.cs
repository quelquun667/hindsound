namespace Hindsound.App;

/// <summary>
/// What every entry point does first, whatever the system: the Windows, Linux and Mac <c>Program.Main</c> each begin with
/// <see cref="Begin"/>, and a test reads their sources to see that none of them stopped. Without it a language chosen in the
/// settings would be read and journaled, and the interface would stay in English.
/// </summary>
internal static class AppStartup
{
    /// <summary>Lets the language setting change the whole process, once, before anything is shown.</summary>
    public static void Begin() => Tr.ForProcess();
}
