namespace Hindsound.Linux.Tests.App;

/// <summary>The tests that set <c>UiErrors.Handler</c>, which the whole process shares: one at a time, apart from the others.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class UiErrorsCollection
{
    public const string Name = "Ui errors";
}
