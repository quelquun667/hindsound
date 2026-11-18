namespace Hindsound.Linux.Tests.Window;

/// <summary>
/// The tests that start SDL: its subsystems and hints are shared by the whole process, so they run one at a time, apart
/// from the others.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SdlCollection
{
    public const string Name = "SDL";
}
