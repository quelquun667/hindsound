using System.Diagnostics;
using System.Globalization;
using Hindsound.Library;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>
/// The pieces of the line of collections, painted together on the real canvas over a plain page, in the places of the screen that
/// will host them. Every run paints them. Set HINDSOUND_SCENE_FOLDER to keep the pictures as PNG files, to look at them.
/// </summary>
public sealed class CollectionScenesTests
{
    private const uint Accent = 0xD4F53C;
    private const float Left = SettingsScreen.Left, Right = SettingsScreen.Right;

    private static readonly Box Filter = new(Left, 82, SettingsScreen.FieldWidth, SettingsScreen.RowHeight);

    private static readonly string[] Clips =
    [
        "Hindsound_Discord_2026-11-08_21-14-03.mp3",
        "Hindsound_Discord_2026-11-08_20-47-51.mp3",
        "Hindsound_2026-11-08_19-02-16.mp3",
        "Hindsound_Discord_2026-11-07_23-31-40.mp3",
        "Hindsound_2026-11-07_22-10-08.mp3",
        "Hindsound_Discord_2026-11-06_18-55-27.mp3",
    ];

    private static readonly LibraryCollection[] Taken =
    [
        new LibraryCollection { Id = "c1", Name = "pets" },
        new LibraryCollection { Id = "c2", Name = "Sam" },
        new LibraryCollection { Id = "c3", Name = "Alex" },
    ];

    private static readonly ChipEntry[] Three = [new("c1", "pets", true), new("c2", "Sam", false), new("c3", "Alex", false)];

    private static readonly ChipEntry[] Eight =
    [
        new("c1", "memes", true),
        new("c2", "Best of the green room", true),
        new("c3", "pets", false),
        new("c4", "Sam", false),
        new("c5", "Alex", false),
        new("c6", "Friday night", false),
        new("c7", "dogs", false),
        new("c8", new string('W', 40), false),
    ];

    private sealed record Scene(string Name, IReadOnlyList<ChipEntry> Chips)
    {
        public string Filter { get; init; } = LibraryFilter.AllClips;

        public bool Actions { get; init; }

        public bool NoClips { get; init; }

        public string? Notice { get; init; }

        public string? Note { get; init; }

        public bool OpenPlus { get; init; }

        public NameBoxState? Box { get; init; }

        public string? Typed { get; init; }

        public string? Focus { get; init; }
    }

    private static IEnumerable<Scene> AllScenes() =>
    [
        new("chips-three", Three),
        new("chips-eight", Eight),
        new("chips-focus-plus", Eight) { Focus = ChipLine.ChipsId },
        new("plus-open", Eight) { OpenPlus = true },
        new("no-collection", []),
        new("file-damaged", Three) { Notice = ChipLine.ReadOnlyMessage(LibraryStatus.Corrupt) },
        new("file-unreadable", Three) { Notice = ChipLine.ReadOnlyMessage(LibraryStatus.Unreadable) },
        new("filter-collection-actions", Three) { Filter = "pets", Actions = true, Focus = CollectionActions.RenameId },
        new("filter-empty-collection", []) { Filter = "Alex", Actions = true, NoClips = true },
        new("filter-actions-with-note", Three) { Filter = "pets", Actions = true, Note = "Playback can end up in your next clip on this setup." },
        new("box-new", Three) { Box = NameBoxState.ForCreate(ChipLine.NewId, () => Taken), Typed = "memes", Focus = NameBox.NameId },
        new("box-empty", Three) { Box = NameBoxState.ForCreate(ChipLine.NewId, () => Taken), Focus = NameBox.NameId },
        new("box-taken", Three) { Box = NameBoxState.ForCreate(ChipLine.NewId, () => Taken), Typed = "pets", Focus = NameBox.NameId },
        new("box-too-long", Three) { Box = NameBoxState.ForCreate(ChipLine.NewId, () => Taken), Typed = new string('a', 41), Focus = NameBox.NameId },
        new("box-rename", Three) { Box = NameBoxState.ForRename("c1", "pets", CollectionActions.RenameId, () => Taken), Focus = NameBox.NameId },
        new("box-delete", Three) { Box = NameBoxState.ForDelete("c2", "Sam", CollectionActions.DeleteId), Focus = NameBox.CancelId },
        new("box-delete-long-name", Three) { Box = NameBoxState.ForDelete("c2", new string('W', 40), CollectionActions.DeleteId), Focus = NameBox.CancelId },
    ];

    public static TheoryData<string, double> SceneNames()
    {
        var data = new TheoryData<string, double>();
        foreach (var scene in AllScenes())
        {
            data.Add(scene.Name, 1.0);
            data.Add(scene.Name, 1.5);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SceneNames))]
    public void A_scene_paints_on_the_real_canvas(string name, double scale)
    {
        var scene = AllScenes().Single(s => s.Name == name);
        using var canvas = new GdiCanvas((int)(760 * scale), (int)(488 * scale), scale);

        Paint(canvas, scene);

        if (Environment.GetEnvironmentVariable("HINDSOUND_SCENE_FOLDER") is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{name}-{(int)(scale * 100)}.png"));
            Save(canvas, file);
        }
    }

    [Fact]
    public void How_long_the_pieces_take_is_written_down_when_asked()
    {
        if (Environment.GetEnvironmentVariable("HINDSOUND_SCENE_FOLDER") is not { Length: > 0 } folder)
        {
            return;
        }

        var lines = new List<string>();
        foreach (var scale in new[] { 1.0, 1.5, 2.0 })
        {
            using var canvas = new GdiCanvas((int)(760 * scale), (int)(488 * scale), scale);
            canvas.FillRect(new Box(0, 0, 760, 488), Theme.Base);
            var dim = Timed(() => canvas.Dim(0.45f));
            var fit = Timed(() => ChipLine.Lay(canvas, Eight, Left, ChipLine.ChipsLimit(canvas, Right)));
            var state = new ChipLineState();
            var entries = state.Arrange("clip", Eight);
            var line = new CollectionChips(canvas, state, entries, Left, ChipLine.ChipsLimit(canvas, Right), (_, _) => { }, _ => { });
            var paint = Timed(() => line.Paint(canvas, new UiState(Accent, null, null, null, -1, -1, 0, true, null)));
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"{scale * 100}%: Dim {dim:F2} ms, Lay {fit:F2} ms, line painted {paint:F2} ms (median of 21)"));
        }

        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "measures.txt"), lines);
    }

    private static double Timed(Action action)
    {
        var samples = new double[21];
        for (var i = 0; i < samples.Length; i++)
        {
            var clock = Stopwatch.StartNew();
            action();
            samples[i] = clock.Elapsed.TotalMilliseconds;
        }

        Array.Sort(samples);
        return samples[samples.Length / 2];
    }

    private static unsafe void Save(GdiCanvas canvas, string file) => PngFile.Write(file, canvas.Bits, canvas.Width, canvas.Height);

    private static void Paint(ICanvas c, Scene scene)
    {
        Page(c, scene);
        var widgets = new List<Widget>();
        var filter = new Dropdown(
            () => [new DropItem("x", scene.Filter)],
            () => "x",
            _ => { },
            _ => { })
        {
            Id = NameBox.AfterDeleteId,
            Bounds = Filter,
        };
        widgets.Add(filter);
        if (scene.Actions)
        {
            widgets.AddRange(CollectionActions.Build(c, Filter, () => { }, () => { }));
        }

        if (scene.Note is { } note)
        {
            var x = (scene.Actions ? widgets[^1].Bounds.Right : Filter.Right) + 16;
            widgets.Add(new Painted((canvas, _) => canvas.Wrapped(note, Face.Body, 12, x, 82, Right - x, Theme.Muted)));
        }

        CollectionChips? line = null;
        if (scene.Notice is { } notice)
        {
            widgets.Add(ChipLine.Notice(notice, Left, Right - Left));
        }
        else if (!scene.NoClips)
        {
            var state = new ChipLineState();
            if (scene.Name == "chips-focus-plus")
            {
                state.Cursor = ChipStop.Plus;
            }

            line = new CollectionChips(c, state, state.Arrange("clip", scene.Chips), Left, ChipLine.ChipsLimit(c, Right), (_, _) => { }, _ => { });
            widgets.Add(line);
            widgets.Add(ChipLine.NewButton(c, Right, () => { }));
        }

        Foot(c, scene);

        if (scene.Box is { } box)
        {
            if (scene.Typed is { } typed)
            {
                var field = NameBox.Build(c, box, () => { }, () => { }).OfType<TextField>().Single();
                field.Typed(typed, null);
            }

            widgets.AddRange(NameBox.Build(c, box, () => { }, () => { }));
        }

        var focus = scene.Focus is { } id ? widgets.Find(w => w.Id == id) : null;
        var ui = new UiState(Accent, focus, null, null, -1, -1, 0, true, null);
        foreach (var widget in widgets)
        {
            widget.Paint(c, ui);
        }

        if (scene.OpenPlus && line is { Fit.Plus: { } plus })
        {
            var ticked = new HashSet<string> { "c6" };
            var list = new CheckedListPopup(line.Fit.Hidden, plus, Right, ticked.Contains, (_, _) => { }, () => { });
            list.Move(1);
            list.Paint(c, Accent);
        }
    }

    /// <summary>What lies around the pieces: the band, the side bar and the list, plain, to judge them in their place.</summary>
    private static void Page(ICanvas c, Scene scene)
    {
        const float band = SettingsScreen.BandHeight, nav = SettingsScreen.NavWidth, bottom = SettingsScreen.ViewBottom;
        c.FillRect(new Box(0, 0, 760, 488), Theme.Base);
        c.FillRect(new Box(0, 0, 760, band), Theme.Panel);
        c.FillRect(new Box(0, band - 1, 760, 1), Theme.Line);
        c.FillRect(new Box(0, 0, 4, band), Accent);
        c.TextMid("Capturing", Face.Title, 22, 48, 36, Theme.Text);
        c.FillRect(new Box(0, band, nav, bottom - band), Theme.Panel);
        c.FillRect(new Box(nav - 1, band, 1, bottom - band), Theme.Line);
        var y = band + 16f;
        foreach (var item in new[] { "Now", "Library", "Hotkeys", "Durations", "Audio", "Export", "General" })
        {
            c.TextMid(item, Face.Medium, 14, 26, y + 18, item == "Library" ? Theme.Text : Theme.Muted);
            y += item == "Library" ? 64 : 40;
        }

        var state = new RowListState();
        if (scene.NoClips)
        {
            new RowList(state, new Box(Left, 118, 560, 240), _ => default, () => { }, () => { })
            {
                EmptyTitle = LibraryFilter.EmptyText(LibraryChoice.Of("c3")).Title,
                EmptyArea = new Box(Left, band, Right - Left, bottom - band),
            }.Paint(c, new UiState(Accent, null, null, null, -1, -1, 0, false, null));
            return;
        }

        state.SetRows(Clips.Length, i => Clips[i]);
        state.Select(1);
        new RowList(state, new Box(Left, 118, 560, 240), i => new ListRow(Clips[i], "0:30"), () => { }, () => { })
            .Paint(c, new UiState(Accent, null, null, null, -1, -1, 0, false, null));
    }

    private static void Foot(ICanvas c, Scene scene)
    {
        var top = SettingsScreen.WindowHeight - SettingsScreen.FootHeight;
        c.FillRect(new Box(0, top, 760, SettingsScreen.FootHeight), Theme.Panel);
        c.FillRect(new Box(0, top, 760, 1), Theme.Line);
        if (scene.NoClips)
        {
            c.TextMid("Selected clip", Face.Title, 18, 24, top + 38, Theme.Muted);
            c.TextMid("Nothing selected", Face.Body, 13, 130, top + 38, Theme.Muted);
            return;
        }

        c.TextMid("Selected clip", Face.Title, 18, 24, top + 26, Theme.Muted);
        c.TextMid(Clips[1], Face.Body, 12, 24, top + 51, Theme.Muted);
    }
}
