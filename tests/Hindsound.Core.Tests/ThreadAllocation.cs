using System.Runtime;

namespace Hindsound.Tests;

/// <summary>
/// The bytes a piece of work allocates on the current thread, for the few tests that hold it to nothing and have seen the
/// count of a thread move with nothing allocated: the mixer of the Linux apps (once in the Windows CI it rose by 32,664
/// bytes early in the run, while the runtime was still compiling and collecting for the tests beside it) and the reads of
/// the wave. A measure is taken again only when it is small, no more than <see cref="Jump"/> over nothing, and a
/// collection of generation 0 or a compilation (in the whole process, as the runtime can compile a tier on another thread)
/// was seen during it; at most <see cref="Tries"/> times. A bigger measure, or one with neither, is given back at once for
/// the assertion of the test to fail. The other allocation tests do not use this: they take one measure after their own
/// warm-up, because a retry would hide an allocation made once, which that single measure catches.
/// This file is compiled into each test project that has such a test.
/// </summary>
internal static class ThreadAllocation
{
    public const int Tries = 5;

    /// <summary>The most a measure can be over nothing and still be taken for a jump of the count.</summary>
    public const long Jump = 64 * 1024;

    /// <summary>The bytes allocated by the work, which must allocate nothing.</summary>
    public static long Measure(Action work)
    {
        for (var attempt = 1; ; attempt++)
        {
            var collections = GC.CollectionCount(0);
            var compiled = JitInfo.GetCompiledMethodCount();
            var before = GC.GetAllocatedBytesForCurrentThread();
            work();
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            var disturbed = GC.CollectionCount(0) != collections || JitInfo.GetCompiledMethodCount() != compiled;
            if (bytes == 0 || bytes > Jump || !disturbed || attempt == Tries)
            {
                return bytes;
            }
        }
    }
}
