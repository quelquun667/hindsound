namespace Hindsound.Linux.Tests;

/// <summary>The tests that set <c>PlatformDefaults</c>, which the whole process shares: one at a time, apart from the others.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PlatformDefaultsCollection
{
    public const string Name = "Platform defaults";
}
