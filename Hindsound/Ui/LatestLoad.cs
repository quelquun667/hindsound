namespace Hindsound.Ui;

/// <summary>
/// The latest of several slow loads for the loop: a load started later may finish first, and an older answer must not
/// replace a newer one. Everything but the work itself happens on the loop's thread. Pure: tested without any window.
/// </summary>
/// <typeparam name="T">What a load gives.</typeparam>
/// <param name="toLoop">Runs an action on the loop's thread.</param>
/// <param name="delivered">Takes the answer, on the loop's thread.</param>
/// <param name="failed">Hears of a load that failed, on the thread of the load. What is on screen stays as it is.</param>
internal sealed class LatestLoad<T>(Action<Action> toLoop, Action<T> delivered, Action<Exception> failed)
{
    private int _latest;

    /// <summary>Starts a load in the thread pool. Call it on the loop's thread.</summary>
    public void Start(Func<T> load)
    {
        var ticket = ++_latest;
        _ = Task.Run(() =>
        {
            try
            {
                var answer = load();
                toLoop(() =>
                {
                    if (ticket == _latest)
                    {
                        delivered(answer);
                    }
                });
            }
            catch (Exception ex)
            {
                failed(ex);
            }
        });
    }

    /// <summary>Nothing started so far is wanted any more. Call it on the loop's thread.</summary>
    public void Forget() => _latest++;
}
