namespace Hindsound.Ui;

/// <summary>Where exceptions caught at the window procedures go.</summary>
internal static class UiErrors
{
    public static Action<Exception>? Handler { get; set; }

    public static void Report(Exception ex)
    {
        try
        {
            Handler?.Invoke(ex);
        }
        catch (Exception)
        {
            // Nothing left to tell.
        }
    }
}
