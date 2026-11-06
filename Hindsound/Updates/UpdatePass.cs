namespace Hindsound.Updates;

/// <summary>A thread of low priority, born and gone with each pass: between two checks, nothing of the updater is alive.</summary>
internal static class UpdatePass
{
    // Enough for WinHTTP and the JSON reader, and far from the 1.5 MB a default thread reserves.
    private const int StackBytes = 256 * 1024;

    public static Task<T> Run<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(
            () =>
            {
                try
                {
                    completion.SetResult(work());
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            },
            StackBytes)
        {
            IsBackground = true,
            Name = "Hindsound update pass",
            Priority = ThreadPriority.Lowest,
        };
        thread.Start();
        return completion.Task;
    }
}
