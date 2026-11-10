namespace Hindsound.Tests.Audio;

/// <summary>
/// The allocation counts of the capture tests must come from the code under test only. Measured in the full suite: as a
/// background GC ended, The_whole_callback_allocates_nothing jumped by 2,600 bytes and a second thread that allocates nothing
/// by 7,976, within the same millisecond. That was 2 of the 3 background GCs inside a measure, and none of the 57 measures
/// without one. With no background GC: no jump in 30 full runs, against 5 in 31. The published app runs without it too.
/// </summary>
public class AllocationRuntimeTests
{
    [Fact]
    public void The_test_run_has_no_background_GC_as_the_published_app()
    {
        Assert.Equal("false", AppContext.GetData("System.GC.Concurrent")?.ToString()?.ToLowerInvariant());
        Assert.Equal(0, GC.GetGCMemoryInfo(GCKind.Background).Index);
    }
}
