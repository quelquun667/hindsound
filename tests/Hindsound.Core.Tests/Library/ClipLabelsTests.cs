using Hindsound.Library;

namespace Hindsound.Tests.Library;

/// <summary>
/// <see cref="ClipLabels.Any"/> is what keeps a labeled clip in the library whatever its age: each kind of label must be enough
/// on its own.
/// </summary>
public sealed class ClipLabelsTests
{
    [Fact]
    public void AStarAlone_IsALabel() => Assert.True(new ClipLabels(true, []).Any);

    [Fact]
    public void ACollectionAlone_IsALabel() => Assert.True(new ClipLabels(false, ["c1"]).Any);

    [Fact]
    public void Both_AreALabel() => Assert.True(new ClipLabels(true, ["c1", "c2"]).Any);

    [Fact]
    public void Neither_IsNoLabel() => Assert.False(new ClipLabels(false, []).Any);
}
