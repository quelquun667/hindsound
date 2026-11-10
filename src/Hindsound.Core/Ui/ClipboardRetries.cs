namespace Hindsound.Ui;

/// <summary>How a copy to the clipboard waits for another app to let go of it. The copy itself is the platform's.</summary>
internal static class ClipboardRetries
{
    public const int Attempts = 5;
    public const int DelayMs = 50;

    /// <summary>
    /// Tries, and tries again through <paramref name="after"/> while another app holds the clipboard, then reports.
    /// The waiting goes through the message loop, which stays free in between.
    /// </summary>
    public static void CopyWithRetries(Func<bool> attempt, Action<int, Action> after, Action<bool> done, int attempts = Attempts, int delayMs = DelayMs)
    {
        var left = attempts;
        Step();

        void Step()
        {
            left--;
            if (attempt())
            {
                done(true);
            }
            else if (left <= 0)
            {
                done(false);
            }
            else
            {
                after(delayMs, Step);
            }
        }
    }
}
