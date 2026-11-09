using Hindsound.Library;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>A name written by hand can break the rules of the box: the box must say so from the start, as the button is grey.</summary>
public sealed class NameBoxRenameTests
{
    private static readonly string TooLong = new('a', 41);

    private static NameBoxState Rename(string name) =>
        NameBoxState.ForRename("c1", name, CollectionActions.RenameId, () => [new LibraryCollection { Id = "c1", Name = name }]);

    [Theory]
    [InlineData("Favorites", NameBoxState.TakenText)]
    [InlineData("All clips", NameBoxState.TakenText)]
    [InlineData("favorites", NameBoxState.TakenText)]
    public void A_name_the_rules_refuse_says_why_as_the_box_opens(string name, string message)
    {
        var state = Rename(name);

        Assert.False(state.CanConfirm);
        Assert.Equal(message, state.Field.Message);
        Assert.Contains(message, NameBox.Build(new FakeCanvas(), state, () => { }, () => { }).OfType<TextField>().Single().State.Message);
    }

    [Fact]
    public void A_name_too_long_says_so_as_the_box_opens()
    {
        var state = Rename(TooLong);

        Assert.False(state.CanConfirm);
        Assert.Equal(NameBoxState.TooLongText, state.Field.Message);
    }

    [Theory]
    [InlineData("pets")]
    [InlineData("Sam")]
    public void A_good_name_opens_with_nothing_said(string name)
    {
        var state = Rename(name);

        Assert.True(state.CanConfirm);
        Assert.Null(state.Field.Message);
    }

    [Fact]
    public void The_message_is_painted_under_the_field()
    {
        var state = Rename("Favorites");
        var field = NameBox.Build(new FakeCanvas(), state, () => { }, () => { }).OfType<TextField>().Single();
        var log = new ShapeLog();

        field.Paint(log, new UiState(0xD4F53C, null, null, null, -1, -1, 0, true, null));

        Assert.Contains(NameBoxState.TakenText, log.Texts);
    }

    [Fact]
    public void A_creation_and_a_deletion_open_with_nothing_said()
    {
        Assert.Null(NameBoxState.ForCreate(ChipLine.NewId, () => []).Field.Message);
        Assert.Null(NameBoxState.ForDelete("c1", "Favorites", CollectionActions.DeleteId).Field.Message);
    }
}
