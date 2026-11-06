namespace Hindsound.Ui;

/// <summary>
/// Work that can take as long as Windows likes, and so has no place on the thread of the message loop: while the loop waits,
/// no shortcut answers and no notification moves.
/// </summary>
internal static class OffLoop
{
    /// <summary>
    /// Runs the work on a thread of its own, in the single-threaded apartment the shell asks for. Returns at once. A failure
    /// goes to <paramref name="failed"/>: an exception out of a thread would end the process, and the buffer with it.
    /// </summary>
    public static Thread Start(Action work, Action<Exception> failed)
    {
        var thread = new Thread(() =>
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                failed(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Hindsound off the loop",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread;
    }
}
