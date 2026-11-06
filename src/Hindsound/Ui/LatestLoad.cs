namespace Hindsound.Ui;

/// <summary>
/// The latest of several slow loads for the loop: a load started later may finish first, and an older answer must not
/// replace a newer one. At most <see cref="MostAtOnce"/> run together, so repeated starts don't pile up against a slow
/// driver: the newest of those that had to wait starts when one ends. Two and not one, so that a single load stuck in a
/// driver doesn't stop the lists. Everything but the work itself happens on the loop's thread. Pure: tested without any window.
/// </summary>
/// <typeparam name="T">What a load gives.</typeparam>
/// <param name="toLoop">Runs an action on the loop's thread.</param>
/// <param name="delivered">Takes the answer, on the loop's thread.</param>
/// <param name="failed">Hears of a load that failed, on the thread of the load. What is on screen stays as it is.</param>
internal sealed class LatestLoad<T>(Action<Action> toLoop, Action<T> delivered, Action<Exception> failed)
{
    public const int MostAtOnce = 2;

    private readonly object _gate = new();
    private int _latest;
    private int _running;
    private Func<T>? _waiting;

    /// <summary>Starts a load in the thread pool, or keeps it for when one ends. Call it on the loop's thread.</summary>
    public void Start(Func<T> load)
    {
        lock (_gate)
        {
            if (_running >= MostAtOnce)
            {
                _waiting = load;
                return;
            }

            _running++;
        }

        var ticket = ++_latest;
        _ = Task.Run(() =>
        {
            try
            {
                var answer = load();
                toLoop(() =>
                {
                    try
                    {
                        if (ticket == _latest)
                        {
                            delivered(answer);
                        }
                    }
                    finally
                    {
                        StartWaiting(Ended());
                    }
                });
            }
            catch (Exception ex)
            {
                failed(ex);
                if (Ended() is { } waiting)
                {
                    toLoop(() => Start(waiting));
                }
            }
        });
    }

    /// <summary>Nothing started so far is wanted any more. Call it on the loop's thread.</summary>
    public void Forget()
    {
        _latest++;
        lock (_gate)
        {
            _waiting = null;
        }
    }

    // One load has ended: its place is free, and this is what takes it.
    private Func<T>? Ended()
    {
        lock (_gate)
        {
            _running--;
            var waiting = _waiting;
            _waiting = null;
            return waiting;
        }
    }

    private void StartWaiting(Func<T>? waiting)
    {
        if (waiting is not null)
        {
            Start(waiting);
        }
    }
}
