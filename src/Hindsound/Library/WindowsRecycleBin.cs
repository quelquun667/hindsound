using System.Globalization;
using System.IO;
using Hindsound.Ui;

namespace Hindsound.Library;

/// <summary>
/// The Recycle Bin of Windows, through IFileOperation. Never deletes a file for good: a file the bin can't take stays where it
/// is, and the answer says so. SHFileOperation is not used: with FOF_ALLOWUNDO it deletes for good, without a word, a file the bin
/// can't take, and nothing lets it be refused file by file. IFileOperation asks its sink before each deletion, which refuses.
/// </summary>
internal sealed class WindowsRecycleBin : IRecycleBin
{
    private readonly Action<string> _journal;

    /// <param name="journal">Told of what went wrong, by code only. Without it, the errors of the interface take it.</param>
    public WindowsRecycleBin(Action<string>? journal = null)
    {
        _journal = journal ?? (line => UiErrors.Report(new RecycleBinNotice(line)));
    }

    public RecycleOutcome Recycle(IReadOnlyList<string> paths)
    {
        try
        {
            if (paths.Count == 0)
            {
                return RecycleOutcome.Recycled;
            }

            // Asked before anything moves. A drive with no bin (a network share, some removable drives) takes nothing.
            foreach (var path in paths)
            {
                if (!File.Exists(path) || Path.GetPathRoot(Path.GetFullPath(path)) is not { Length: > 0 } root || !RecycleBinNative.HasBin(root))
                {
                    return RecycleOutcome.CannotRecycle;
                }
            }

            // IFileOperation wants a single-threaded apartment: a thread of its own, which the caller, already off the loop, waits for.
            var outcome = RecycleOutcome.Failed;
            var thread = new Thread(() => outcome = OnOwnThread(paths)) { IsBackground = true, Name = "Hindsound recycle bin" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            return outcome;
        }
        catch (Exception ex)
        {
            _journal($"recycle bin: failed ({Describe(ex)})");
            return RecycleOutcome.Failed;
        }
    }

    private RecycleOutcome OnOwnThread(IReadOnlyList<string> paths)
    {
        try
        {
            var tally = new RecycleTally();
            var code = RecycleBinNative.Recycle(paths, tally);
            var outcome = tally.Outcome(paths.Count);
            if (tally.DeletedForGood > 0)
            {
                _journal("recycle bin: deleted for good");
            }
            else if (outcome != RecycleOutcome.Recycled && code < 0)
            {
                _journal(string.Create(CultureInfo.InvariantCulture, $"recycle bin: not done (0x{code:X8})"));
            }

            return outcome;
        }
        catch (Exception ex)
        {
            _journal($"recycle bin: failed ({Describe(ex)})");
            return RecycleOutcome.Failed;
        }
    }

    private static string Describe(Exception ex) => string.Create(CultureInfo.InvariantCulture, $"{ex.GetType().Name}, 0x{ex.HResult:X8}");

    /// <summary>A line of the log with nothing else in it: no path, no message from the system.</summary>
    private sealed class RecycleBinNotice(string line) : Exception(line);
}

/// <summary>
/// What the sink of IFileOperation decides and counts, apart from COM so that a test can drive it. Before each deletion, the
/// system says in its flags whether the file will go to the bin: without <see cref="RecycleIfPossible"/> it would be deleted for
/// good, so it is refused. After each, a deletion that succeeded with no item made in the bin was a deletion for good.
/// </summary>
internal sealed class RecycleTally
{
    /// <summary>TSF_DELETE_RECYCLE_IF_POSSIBLE, in the flags given to PreDeleteItem (ShObjIdl_core.h).</summary>
    internal const uint RecycleIfPossible = 0x80;

    /// <summary>HRESULT_FROM_WIN32(ERROR_CANCELLED): the file is not deleted, and the operation stops.</summary>
    internal const int Cancelled = unchecked((int)0x800704C7);

    /// <summary>Files the sink kept from a deletion for good.</summary>
    public int Refused { get; private set; }

    /// <summary>Files now in the bin.</summary>
    public int Recycled { get; private set; }

    /// <summary>Files deleted for good all the same. Never expected: the sink refuses them before.</summary>
    public int DeletedForGood { get; private set; }

    /// <summary>The answer of the sink to PreDeleteItem.</summary>
    public int BeforeDelete(uint flags)
    {
        if ((flags & RecycleIfPossible) == 0)
        {
            Refused++;
            return Cancelled;
        }

        return 0;
    }

    /// <summary>Told by PostDeleteItem.</summary>
    /// <param name="result">How the deletion ended.</param>
    /// <param name="inBin">Whether the system gave the item it made in the bin.</param>
    public void AfterDelete(int result, bool inBin)
    {
        if (result < 0)
        {
            return;
        }

        if (inBin)
        {
            Recycled++;
        }
        else
        {
            DeletedForGood++;
        }
    }

    /// <summary>What became of <paramref name="count"/> files once the operation is over.</summary>
    public RecycleOutcome Outcome(int count)
    {
        if (DeletedForGood > 0)
        {
            return RecycleOutcome.Failed;
        }

        if (Recycled == count)
        {
            return RecycleOutcome.Recycled;
        }

        // Refused before anything moved: every file is where it was. Any other end leaves the disk to be looked at again.
        return Recycled == 0 && Refused > 0 ? RecycleOutcome.CannotRecycle : RecycleOutcome.Failed;
    }
}
