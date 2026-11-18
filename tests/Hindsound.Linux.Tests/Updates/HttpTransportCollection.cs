namespace Hindsound.Linux.Tests.Updates;

/// <summary>
/// The tests of the HTTP transport wait on a local server for two seconds, and the delay ends through a timer, the socket and
/// the server, all on threads of the pool. xUnit runs the tests that run beside them on threads of that pool too, as many as
/// the machine has cores: on the four cores of the Windows runner of the CI, the long tests of the window (the dump of every
/// page, a whole preview) took them all, and a wait meant to end after 2 s ended after 7 and 8 s. They run one
/// at a time, apart from the others.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HttpTransportCollection
{
    public const string Name = "HTTP transport";
}
