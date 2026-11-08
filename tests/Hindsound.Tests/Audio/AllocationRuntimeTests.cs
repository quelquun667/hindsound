namespace Hindsound.Tests.Audio;

/// <summary>
/// The allocation counts of the capture tests must come from the code under test only. With tiered compilation the runtime
/// itself allocates, once, when it swaps a hot method for its optimised copy: in a Release run that landed inside the loop of
/// The_whole_callback_allocates_nothing, long after its warm-up, and only in the full suite.
/// </summary>
public class AllocationRuntimeTests
{
    [Fact]
    public void The_test_run_compiles_each_method_once_and_does_not_swap_it_while_it_runs()
    {
        Assert.Equal("false", AppContext.GetData("System.Runtime.TieredCompilation")?.ToString()?.ToLowerInvariant());
    }
}
