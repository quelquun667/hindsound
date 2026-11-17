using System.Collections.Concurrent;
using Hindsound.Library;

namespace Hindsound.Tests.Library;

/// <summary>
/// The decisions of the sink of IFileOperation, without COM, and what the bin refuses before it reaches the system. The real bin
/// is reached only by the test of the Device category, on a file of one byte in a folder of the test.
/// </summary>
public sealed class WindowsRecycleBinTests : IDisposable
{
    private const uint TSF_DELETE_RECYCLE_IF_POSSIBLE = 0x80, TSF_USE_FULL_ACCESS = 0x40;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "HindsoundTests-" + Guid.NewGuid().ToString("N"));
    private readonly ConcurrentQueue<string> _journal = new();

    public WindowsRecycleBinTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void A_deletion_the_system_would_make_for_good_is_refused()
    {
        var tally = new RecycleTally();

        Assert.Equal(RecycleTally.Cancelled, tally.BeforeDelete(0));
        Assert.Equal(RecycleTally.Cancelled, tally.BeforeDelete(TSF_USE_FULL_ACCESS));
        Assert.Equal(2, tally.Refused);
    }

    [Fact]
    public void A_deletion_to_the_bin_goes_through()
    {
        var tally = new RecycleTally();

        Assert.Equal(0, tally.BeforeDelete(TSF_DELETE_RECYCLE_IF_POSSIBLE));
        Assert.Equal(0, tally.BeforeDelete(TSF_DELETE_RECYCLE_IF_POSSIBLE | TSF_USE_FULL_ACCESS));
        Assert.Equal(0, tally.Refused);
    }

    [Fact]
    public void The_refusal_is_the_cancel_of_the_system()
    {
        // HRESULT_FROM_WIN32(ERROR_CANCELLED).
        Assert.Equal(unchecked((int)0x800704C7), RecycleTally.Cancelled);
        Assert.Equal(TSF_DELETE_RECYCLE_IF_POSSIBLE, RecycleTally.RecycleIfPossible);
    }

    [Fact]
    public void Every_file_in_the_bin_is_recycled()
    {
        var tally = new RecycleTally();
        tally.BeforeDelete(TSF_DELETE_RECYCLE_IF_POSSIBLE);
        tally.AfterDelete(0, inBin: true);
        tally.BeforeDelete(TSF_DELETE_RECYCLE_IF_POSSIBLE);
        tally.AfterDelete(0, inBin: true);

        Assert.Equal(RecycleOutcome.Recycled, tally.Outcome(2));
    }

    [Fact]
    public void A_first_file_refused_leaves_everything_where_it_was()
    {
        var tally = new RecycleTally();
        tally.BeforeDelete(0);
        tally.AfterDelete(RecycleTally.Cancelled, inBin: false);

        Assert.Equal(RecycleOutcome.CannotRecycle, tally.Outcome(2));
    }

    [Fact]
    public void A_file_refused_after_one_went_to_the_bin_is_a_failure()
    {
        var tally = new RecycleTally();
        tally.BeforeDelete(TSF_DELETE_RECYCLE_IF_POSSIBLE);
        tally.AfterDelete(0, inBin: true);
        tally.BeforeDelete(0);

        Assert.Equal(RecycleOutcome.Failed, tally.Outcome(2));
    }

    [Fact]
    public void A_deletion_that_made_nothing_in_the_bin_is_a_failure_whatever_else_went_well()
    {
        var tally = new RecycleTally();
        tally.AfterDelete(0, inBin: true);
        tally.AfterDelete(0, inBin: false);

        Assert.Equal(1, tally.DeletedForGood);
        Assert.Equal(RecycleOutcome.Failed, tally.Outcome(1));
    }

    [Fact]
    public void A_deletion_the_system_could_not_make_is_a_failure()
    {
        var tally = new RecycleTally();
        tally.BeforeDelete(TSF_DELETE_RECYCLE_IF_POSSIBLE);
        tally.AfterDelete(unchecked((int)0x80070020), inBin: false);

        Assert.Equal(0, tally.DeletedForGood);
        Assert.Equal(RecycleOutcome.Failed, tally.Outcome(1));
    }

    [Fact]
    public void The_operation_asks_for_the_bin_and_never_for_a_window()
    {
        const uint FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400;
        const uint FOFX_RECYCLEONDELETE = 0x00080000, FOFX_EARLYFAILURE = 0x00100000;

        Assert.Equal(FOF_ALLOWUNDO | FOFX_RECYCLEONDELETE | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOFX_EARLYFAILURE, RecycleBinNative.Flags);
    }

    [Fact]
    public void A_file_that_is_not_there_is_refused_before_the_system_is_asked()
    {
        var bin = new WindowsRecycleBin(_journal.Enqueue);
        var there = Path.Combine(_root, "there.mp3");
        File.WriteAllBytes(there, [1]);

        Assert.Equal(RecycleOutcome.CannotRecycle, bin.Recycle([there, Path.Combine(_root, "gone.mp3")]));
        Assert.True(File.Exists(there));
        Assert.Empty(_journal);
    }

    [Fact]
    [Trait("Category", "Device")]
    public void A_file_of_one_byte_goes_to_the_real_bin()
    {
        var bin = new WindowsRecycleBin(_journal.Enqueue);
        var path = Path.Combine(_root, "HindsoundTests-recycle-" + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllBytes(path, [1]);

        Assert.Equal(RecycleOutcome.Recycled, bin.Recycle([path]));
        Assert.False(File.Exists(path));
        Assert.Empty(_journal);
    }
}
