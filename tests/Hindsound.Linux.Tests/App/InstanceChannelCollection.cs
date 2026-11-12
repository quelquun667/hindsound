namespace Hindsound.Linux.Tests.App;

/// <summary>
/// The tests of the instance channel wait on real sockets for a few seconds, and the running instance reads and answers on
/// threads of the pool. Run beside the other tests on the busy Windows runner of the CI, three of them ran past their waits
/// in one day: they run one at a time, apart from the others.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class InstanceChannelCollection
{
    public const string Name = "Instance channel";
}
