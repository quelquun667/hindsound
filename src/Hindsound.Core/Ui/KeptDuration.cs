namespace Hindsound.Ui;

/// <summary>Where the memory stands: every text about what is kept starts from this.</summary>
public enum KeptState
{
    /// <summary>A few seconds at most: a figure would only make the sentence look like a promise.</summary>
    Starting,

    /// <summary>Part of the memory is there, and the text says how much.</summary>
    Filling,

    /// <summary>All of it is there.</summary>
    Full,
}

/// <summary>
/// The one rule behind every text about how much is kept. A text that names the setting alone promises what the memory may
/// not hold yet: 5:00 is what it can reach, not what it has, for the first 5 minutes. Pure: tested without any engine.
/// </summary>
internal static class KeptDuration
{
    /// <summary>Under this, nothing is worth a figure.</summary>
    public const int StartingBelowSeconds = 5;

    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);

    /// <param name="kept">What the memory holds, in whole seconds.</param>
    /// <param name="cap">What the setting lets it hold, in seconds.</param>
    public static KeptState StateOf(int kept, int cap) =>
        // A second of margin: the measure of what is held can stay a hair under the setting for good.
        kept >= Math.Max(1, cap - 1) ? KeptState.Full
        : kept < StartingBelowSeconds ? KeptState.Starting
        : KeptState.Filling;

    /// <summary>The figure to show: a full memory says the setting, not the second it may be short of it.</summary>
    public static int Shown(int kept, int cap) => StateOf(kept, cap) == KeptState.Full ? cap : kept;

    /// <summary>The same whole seconds as the title of a clip.</summary>
    public static int SecondsOf(TimeSpan buffered) => NotificationStates.WholeSeconds(buffered);

    /// <summary>A clip is worth asking for: under a second, it would be empty or nearly so.</summary>
    public static bool CanSave(TimeSpan buffered) => buffered >= OneSecond;
}
