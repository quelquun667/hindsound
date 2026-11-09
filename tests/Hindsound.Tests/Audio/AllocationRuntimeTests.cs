namespace Hindsound.Tests.Audio;

/// <summary>
/// The allocation counts of the capture tests must come from the code under test only. When a background GC ends, it empties
/// the allocation block of every thread, and GC.GetAllocatedBytesForCurrentThread takes what was left in it for an allocation.
/// Measured in the full suite, on the same millisecond: The_whole_callback_allocates_nothing jumped by 2,600 bytes, and a second
/// thread that allocates nothing by 7,976. The published app runs without background GC too.
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
