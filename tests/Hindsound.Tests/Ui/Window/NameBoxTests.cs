using Hindsound.Library;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

public sealed class NameBoxTests
{
    private const uint Accent = 0xD4F53C;

    private static readonly LibraryCollection[] Taken = [new LibraryCollection { Id = "c1", Name = "pets" }, new LibraryCollection { Id = "c2", Name = "Sam" }];

    private static NameBoxState Create() => NameBoxState.ForCreate(ChipLine.NewId, () => Taken);

    private static NameBoxState Rename() => NameBoxState.ForRename("c1", "pets", CollectionActions.RenameId, () => Taken);

    private static NameBoxState Delete() => NameBoxState.ForDelete("c2", "Sam", CollectionActions.DeleteId);

    /// <summary>Types in the field as the screen does: the field of the moment, then the widgets made again.</summary>
    private static void Type(NameBoxState state, string text)
    {
        var field = Assert.Single(NameBox.Build(new FakeCanvas(), state, () => { }, () => { }).OfType<TextField>());
        field.Typed(text, null);
    }

    private static IReadOnlyList<Widget> Widgets(NameBoxState state, Action? confirm = null, Action? cancel = null) =>
        NameBox.Build(new FakeCanvas(), state, confirm ?? (() => { }), cancel ?? (() => { }));

    private static Button ConfirmOf(NameBoxState state) => Assert.IsType<Button>(Widgets(state).Single(w => w.Id == NameBox.ConfirmId), exactMatch: false);

    private static ShapeLog PaintAll(IReadOnlyList<Widget> widgets, Widget? focus = null)
    {
        var log = new ShapeLog();
        var ui = new UiState(Accent, focus, null, null, -1, -1, 0, true, null);
        foreach (var widget in widgets)
        {
            widget.Paint(log, ui);
        }

        return log;
    }

    // ---------------------------------------------------------------- the size of the box

    public static TheoryData<string> States => new() { string.Empty, "memes", "pets", "   ", new string('a', 41) };

    [Theory]
    [MemberData(nameof(States))]
    public void The_box_of_a_name_is_190_tall_and_nothing_in_it_moves_whatever_is_typed(string typed)
    {
        foreach (var state in new[] { Create(), Rename() })
        {
            Type(state, typed);

            var widgets = Widgets(state);
            var frame = PaintAll(widgets).Frames[0].Box;

            Assert.Equal(new Box(200, 149, 360, 190), frame);
            Assert.Equal(new Box(224, 223, 312, 32), widgets.Single(w => w.Id == NameBox.NameId).Bounds);
            Assert.Equal(frame.Bottom - 16, widgets.Single(w => w.Id == NameBox.ConfirmId).Bounds.Bottom);
            Assert.Equal(frame.Bottom - 16, widgets.Single(w => w.Id == NameBox.CancelId).Bounds.Bottom);
        }
    }

    [Fact]
    public void The_box_of_a_deletion_is_that_of_its_picture_centred_in_the_window()
    {
        var frame = PaintAll(Widgets(Delete())).Frames[0].Box;

        Assert.Equal(new Box(200, 183, 360, 122), frame);
        Assert.Equal(NameBox.DeleteHeight, frame.H);
    }

    [Fact]
    public void A_message_under_the_field_stops_before_the_buttons()
    {
        var widgets = Widgets(Create());
        var field = widgets.Single(w => w.Id == NameBox.NameId);

        Assert.True(field.Bounds.Bottom + TextField.MessageHeight <= widgets.Single(w => w.Id == NameBox.CancelId).Bounds.Y);
    }

    [Fact]
    public void The_buttons_keep_the_order_of_the_picture_and_stay_in_the_box()
    {
        foreach (var state in new[] { Create(), Rename(), Delete() })
        {
            var widgets = Widgets(state);
            var frame = NameBox.Frame(state.Kind);
            var cancel = widgets.Single(w => w.Id == NameBox.CancelId).Bounds;
            var confirm = widgets.Single(w => w.Id == NameBox.ConfirmId).Bounds;

            Assert.True(cancel.Right < confirm.X);
            Assert.Equal(frame.Right - 24, confirm.Right);
            Assert.True(cancel.X > frame.X);
        }
    }

    // ---------------------------------------------------------------- what it says

    [Fact]
    public void Each_kind_says_its_own_words()
    {
        var create = PaintAll(Widgets(Create())).Texts;
        Assert.Contains("New collection", create);
        Assert.Contains("Collection name", create);
        Assert.Contains("Cancel", create);
        Assert.Contains("Create", create);

        var rename = PaintAll(Widgets(Rename())).Texts;
        Assert.Contains("Rename collection", rename);
        Assert.Contains("Rename", rename);
        Assert.Contains("pets", rename);

        var delete = PaintAll(Widgets(Delete())).Texts;
        Assert.DoesNotContain("Collection name", delete);
        Assert.Contains("Delete", delete);
        Assert.Contains("Cancel", delete);
        Assert.Equal("Delete collection \"Sam\"? Your clips stay.", NameBox.DeleteQuestion(new FakeCanvas(), "Sam"));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void The_question_of_a_deletion_never_passes_two_lines(double scale)
    {
        using var canvas = new GdiCanvas((int)(760 * scale), (int)(488 * scale), scale);
        var frame = NameBox.Frame(NameBoxKind.Delete);
        var names = new[] { "Sam", new string('W', 40), string.Join(' ', Enumerable.Repeat("WWW", 10)), "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "iiiiiiii WWWWWWWWWW iiiiiiii WWWWWWWWWW" };
        foreach (var name in names)
        {
            var used = canvas.Wrapped(NameBox.DeleteQuestion(canvas, name), Face.Body, 14, frame.X + 24, frame.Y + 22, NameBox.Width - 48, 0);

            Assert.True(used <= 2 * canvas.LineHeight(Face.Body, 14) + 0.01f, $"{name} takes {used}");
            Assert.True(frame.Y + 22 + used <= frame.Bottom - 16 - 34, $"{name} runs into the buttons");
        }
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void The_messages_of_the_field_fit_its_width(double scale)
    {
        using var canvas = new GdiCanvas((int)(760 * scale), (int)(488 * scale), scale);

        Assert.True(canvas.Measure(NameBoxState.TakenText, Face.Body, 13) + 18 <= NameBox.Width - 48);
        Assert.True(canvas.Measure(NameBoxState.TooLongText, Face.Body, 13) + 18 <= NameBox.Width - 48);
    }

    [Fact]
    public void The_box_darkens_what_is_behind_it_once_and_has_no_glow_of_its_own()
    {
        var create = PaintAll(Widgets(Create()));
        Assert.Equal(1, create.Dims);
        Assert.Equal(0, create.Glows);

        var delete = PaintAll(Widgets(Delete()));
        Assert.Equal(1, delete.Dims);
        Assert.Equal(0, delete.Glows);
    }

    [Fact]
    public void The_only_light_with_a_good_name_is_that_of_the_button_like_any_accented_button()
    {
        var state = Create();
        Type(state, "memes");

        Assert.Equal(1, PaintAll(Widgets(state)).Glows);
    }

    [Fact]
    public void The_field_has_the_focus_outline_of_a_real_text_field()
    {
        var widgets = Widgets(Create());
        var field = widgets.Single(w => w.Id == NameBox.NameId);

        var log = PaintAll(widgets, focus: field);

        Assert.Equal(field.Bounds.Inflate(3), Assert.Single(log.Rings));
    }

    // ---------------------------------------------------------------- the rule of the name

    [Theory]
    [InlineData("memes", true, null)]
    [InlineData("  memes  ", true, null)]
    [InlineData("", false, null)]
    [InlineData("    ", false, null)]
    [InlineData("pets", false, NameBoxState.TakenText)]
    [InlineData("PETS", false, NameBoxState.TakenText)]
    [InlineData("All clips", false, NameBoxState.TakenText)]
    [InlineData("favorites", false, NameBoxState.TakenText)]
    public void The_field_judges_at_each_key(string typed, bool ok, string? message)
    {
        Assert.Equal(new FieldCheck(ok, message), Create().Check(typed));
    }

    [Fact]
    public void Too_long_a_name_is_not_called_taken()
    {
        var check = Create().Check(new string('a', 41));

        Assert.False(check.Ok);
        Assert.Equal(NameBoxState.TooLongText, check.Message);
        Assert.NotEqual(NameBoxState.TakenText, check.Message);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("pets", false)]
    [InlineData("memes", true)]
    public void The_button_acts_only_on_a_good_name(string typed, bool ready)
    {
        var state = Create();
        Type(state, typed);

        var button = ConfirmOf(state);

        Assert.Equal(ready, button.Enabled);
        Assert.Equal(ready, button.Accented);
        Assert.Equal(ready, button.CanFocus);
    }

    [Fact]
    public void The_message_shows_under_the_field_when_typed()
    {
        var state = Create();

        Type(state, "pets");

        Assert.Equal("pets", state.Field.Draft);
        Assert.Equal(NameBoxState.TakenText, state.Field.Message);
        Assert.Contains(NameBoxState.TakenText, PaintAll(Widgets(state)).Texts);
    }

    [Fact]
    public void A_rename_starts_with_the_name_and_may_keep_it()
    {
        var state = Rename();

        Assert.Equal("pets", state.Field.Draft);
        Assert.True(state.CanConfirm);
        Assert.True(state.Check("PETS").Ok);
        Assert.Equal(NameBoxState.TakenText, state.Check("Sam").Message);
    }

    [Fact]
    public void A_creation_starts_empty()
    {
        Assert.Equal(string.Empty, Create().Field.Draft);
        Assert.False(Create().CanConfirm);
    }

    [Fact]
    public void The_name_is_judged_against_the_collections_as_of_now()
    {
        var collections = new List<LibraryCollection>();
        var state = NameBoxState.ForCreate(ChipLine.NewId, () => collections);
        Type(state, "memes");
        Assert.True(state.CanConfirm);

        collections.Add(new LibraryCollection { Id = "c1", Name = "memes" });

        Assert.False(state.CanConfirm);
        Assert.False(ConfirmOf(state).Enabled);
    }

    [Fact]
    public void A_deletion_asks_for_no_name()
    {
        var state = Delete();

        Assert.True(state.CanConfirm);
        Assert.Empty(Widgets(state).OfType<TextField>());
        Assert.True(ConfirmOf(state).Enabled);
        Assert.False(ConfirmOf(state).Accented);
    }

    // ---------------------------------------------------------------- the keyboard

    [Fact]
    public void The_focus_arrives_on_the_first_control()
    {
        Assert.Equal(NameBox.NameId, Create().EntryId);
        Assert.Equal(NameBox.NameId, Rename().EntryId);
        Assert.Equal(NameBox.CancelId, Delete().EntryId);
    }

    [Fact]
    public void Where_the_focus_arrives_is_the_first_stop_of_the_box()
    {
        foreach (var state in new[] { Create(), Rename(), Delete() })
        {
            Assert.Equal(state.EntryId, Widgets(state).First(w => w.CanFocus).Id);
        }
    }

    private static List<string> Walk(IReadOnlyList<Widget> widgets, string start, bool forward, int steps)
    {
        var seen = new List<string>();
        var at = start;
        for (var i = 0; i < steps; i++)
        {
            at = NameBox.NextStop(widgets, at, forward)!;
            seen.Add(at);
        }

        return seen;
    }

    [Fact]
    public void Tab_goes_round_the_box_in_the_order_of_the_eye()
    {
        var state = Create();
        Type(state, "memes");

        Assert.Equal([NameBox.CancelId, NameBox.ConfirmId, NameBox.NameId, NameBox.CancelId], Walk(Widgets(state), NameBox.NameId, forward: true, 4));
        Assert.Equal([NameBox.ConfirmId, NameBox.CancelId, NameBox.NameId], Walk(Widgets(state), NameBox.NameId, forward: false, 3));
    }

    [Fact]
    public void Tab_skips_a_button_that_cannot_act()
    {
        Assert.Equal([NameBox.CancelId, NameBox.NameId, NameBox.CancelId], Walk(Widgets(Create()), NameBox.NameId, forward: true, 3));
    }

    [Fact]
    public void Tab_stays_in_the_box_whatever_else_is_on_the_page()
    {
        var state = Delete();
        var page = new List<Widget>
        {
            new Button("Copy", () => { }) { Id = "foot.copy" },
            new Button("Pause", () => { }) { Id = "band.pause" },
        };
        page.InsertRange(1, Widgets(state));
        page.Add(new Toggle(() => true, _ => { }) { Id = "lib.boxed" });

        var seen = Walk(page, state.EntryId, forward: true, 6);

        Assert.All(seen, id => Assert.StartsWith(NameBox.Prefix, id));
        Assert.Equal([NameBox.ConfirmId, NameBox.CancelId], seen.Take(2));
    }

    [Theory]
    [InlineData(NameBox.NameId)]
    [InlineData(NameBox.CancelId)]
    [InlineData(NameBox.ConfirmId)]
    public void Escape_leaves_the_box_from_any_control(string focus)
    {
        Assert.Equal(NameBoxKey.Cancel, Create().Key(Vk.Escape, plain: true, focus));
    }

    [Fact]
    public void Enter_in_the_field_asks_for_the_name_and_on_a_button_is_left_to_the_button()
    {
        var state = Create();

        Assert.Equal(NameBoxKey.Confirm, state.Key(Vk.Enter, plain: true, NameBox.NameId));
        Assert.Equal(NameBoxKey.PassOn, state.Key(Vk.Enter, plain: true, NameBox.CancelId));
        Assert.Equal(NameBoxKey.PassOn, state.Key(Vk.Enter, plain: true, NameBox.ConfirmId));
    }

    [Fact]
    public void Enter_on_arrival_at_a_deletion_cancels_it()
    {
        var state = Delete();
        var cancelled = 0;
        var confirmed = 0;
        var widgets = Widgets(state, () => confirmed++, () => cancelled++);

        Assert.Equal(NameBoxKey.PassOn, state.Key(Vk.Enter, plain: true, state.EntryId));
        widgets.Single(w => w.Id == state.EntryId).OnKey(Vk.Enter);

        Assert.Equal(1, cancelled);
        Assert.Equal(0, confirmed);
    }

    [Fact]
    public void Keys_with_a_modifier_and_other_keys_are_not_for_the_box()
    {
        var state = Create();

        Assert.Equal(NameBoxKey.PassOn, state.Key(Vk.Escape, plain: false, NameBox.NameId));
        Assert.Equal(NameBoxKey.PassOn, state.Key(Vk.Enter, plain: false, NameBox.NameId));
        Assert.Equal(NameBoxKey.PassOn, state.Key(Vk.Left, plain: true, NameBox.NameId));
    }

    // ---------------------------------------------------------------- asking, and the answer

    [Fact]
    public void The_button_asks_once_until_the_answer_comes()
    {
        var state = Create();
        Type(state, "memes");
        var asked = 0;
        var button = Widgets(state, () => asked++).Single(w => w.Id == NameBox.ConfirmId);

        button.OnRelease(inside: true);
        button.OnRelease(inside: true);

        Assert.Equal(1, asked);
        Assert.True(state.Waiting);

        state.Answered(LibraryOutcome.NameTaken);
        Assert.False(state.Waiting);
    }

    [Fact]
    public void A_bad_name_never_leaves()
    {
        var state = Create();
        Type(state, "pets");

        Assert.False(state.TryConfirm());
        Assert.False(state.Waiting);
    }

    [Theory]
    [InlineData(LibraryOutcome.Done, true, null)]
    [InlineData(LibraryOutcome.NotWritten, true, null)]
    [InlineData(LibraryOutcome.ReadOnly, true, null)]
    [InlineData(LibraryOutcome.UnknownCollection, true, null)]
    [InlineData(LibraryOutcome.NameEmpty, false, null)]
    [InlineData(LibraryOutcome.NameTaken, false, NameBoxState.TakenText)]
    [InlineData(LibraryOutcome.NameTooLong, false, NameBoxState.TooLongText)]
    public void The_answer_closes_the_box_or_says_why_in_it(LibraryOutcome outcome, bool closes, string? message)
    {
        var state = Create();
        Type(state, "memes");
        state.TryConfirm();

        Assert.Equal(closes, state.Answered(outcome));
        Assert.Equal(message, state.Field.Message);
        Assert.Equal("memes", state.Field.Draft);
    }

    [Fact]
    public void Cancelling_goes_back_to_the_stop_that_opened_the_box()
    {
        Assert.Equal(ChipLine.NewId, Create().FocusAfter(null));
        Assert.Equal(CollectionActions.RenameId, Rename().FocusAfter(null));
        Assert.Equal(CollectionActions.DeleteId, Delete().FocusAfter(null));
    }

    [Fact]
    public void After_the_action_the_focus_goes_where_a_stop_still_is()
    {
        Assert.Equal(ChipLine.NewId, Create().FocusAfter(LibraryOutcome.Done));
        Assert.Equal(CollectionActions.RenameId, Rename().FocusAfter(LibraryOutcome.Done));
        Assert.Equal(NameBox.AfterDeleteId, Delete().FocusAfter(LibraryOutcome.Done));
        Assert.Equal(CollectionActions.DeleteId, Delete().FocusAfter(LibraryOutcome.NotWritten));
        Assert.Equal(NameBox.AfterDeleteId, Delete().FocusAfter(LibraryOutcome.UnknownCollection));
        Assert.Equal(NameBox.AfterDeleteId, Rename().FocusAfter(LibraryOutcome.UnknownCollection));
    }

    [Fact]
    public void What_was_typed_survives_the_widgets_being_made_again()
    {
        var state = Create();
        Type(state, "mem");

        var field = Assert.Single(Widgets(state).OfType<TextField>());

        Assert.Equal("mem", field.State.Draft);
    }

    // ---------------------------------------------------------------- Rename and Delete, next to the menu

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Rename_and_Delete_sit_on_the_line_of_the_menu_and_stay_in_the_page(double scale)
    {
        using var canvas = new GdiCanvas((int)(760 * scale), (int)(488 * scale), scale);
        var filter = new Box(200, 82, 220, 30);

        var buttons = CollectionActions.Build(canvas, filter, () => { }, () => { });

        Assert.Equal([CollectionActions.RenameId, CollectionActions.DeleteId], buttons.Select(b => b.Id));
        Assert.All(buttons, b =>
        {
            Assert.Equal(filter.Y, b.Bounds.Y);
            Assert.Equal(filter.H, b.Bounds.H);
        });
        Assert.True(buttons[0].Bounds.X > filter.Right);
        Assert.True(buttons[1].Bounds.X > buttons[0].Bounds.Right);
        Assert.True(buttons[1].Bounds.Right <= 728);
        Assert.True(canvas.Measure("Rename", Face.Medium, 13) < buttons[0].Bounds.W);
        Assert.True(canvas.Measure("Delete", Face.Medium, 13) < buttons[1].Bounds.W);
    }

    [Fact]
    public void Rename_and_Delete_do_what_they_say()
    {
        var renamed = 0;
        var deleted = 0;

        var buttons = CollectionActions.Build(new FakeCanvas(), new Box(200, 82, 220, 30), () => renamed++, () => deleted++);
        buttons[0].OnKey(Vk.Enter);
        buttons[1].OnRelease(inside: true);
        buttons[1].OnRelease(inside: false);

        Assert.Equal(1, renamed);
        Assert.Equal(1, deleted);
    }

    // ---------------------------------------------------------------- what lies behind

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public unsafe void Dimming_darkens_every_pixel_by_its_share(double scale)
    {
        using var canvas = new GdiCanvas((int)(100 * scale), (int)(60 * scale), scale, null);
        canvas.FillRect(new Box(0, 0, 100, 60), 0xFFFFFF);

        canvas.Dim(0.45f);

        var dimmed = canvas.Bits[0] & 0xFF;
        Assert.InRange(dimmed, 138u, 142u);
        Assert.Equal(canvas.Bits[0], canvas.Bits[(canvas.Height * canvas.Width) - 1]);
        Assert.Equal(0xFF000000u, canvas.Bits[0] & 0xFF000000u);
    }

    [Fact]
    public void A_canvas_that_does_not_care_for_it_can_be_asked_to_dim()
    {
        ICanvas canvas = new FakeCanvas();

        canvas.Dim(0.45f);
    }
}
