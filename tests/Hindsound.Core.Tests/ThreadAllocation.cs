using System.Runtime;

namespace Hindsound.Tests;

/// <summary>
/// The bytes a piece of work allocates on the current thread, for the tests that hold it to a bound. The count of a thread
/// can move with nothing allocated by the work: once in the Windows CI it rose by 32,664 bytes early in the run, while the
/// runtime was still compiling and collecting for the tests beside it. So a measure over the bound is taken again only when
/// a collection or a compilation (in the whole process) is seen during it; one over the bound with neither is given back at
/// once, and so is the fifth, for the assertion of the test to fail. This file is compiled into every test project that
/// measures allocations.
/// </summary>
internal static class ThreadAllocation
{
    public const int Tries = 5;

    /// <summary>For the work that must allocate nothing.</summary>
    public static long Measure(Action work) => Measure(work, bytes => bytes == 0);

    /// <param name="fits">The bound of the test: a measure that fits it is never taken again.</param>
    public static long Measure(Action work, Func<long, bool> fits)
    {
        for (var attempt = 1; ; attempt++)
        {
            var collections = GC.CollectionCount(0);
            var compiled = JitInfo.GetCompiledMethodCount();
            var before = GC.GetAllocatedBytesForCurrentThread();
            work();
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            var disturbed = GC.CollectionCount(0) != collections || JitInfo.GetCompiledMethodCount() != compiled;
            if (fits(bytes) || !disturbed || attempt == Tries)
            {
                return bytes;
            }
        }
    }
}
