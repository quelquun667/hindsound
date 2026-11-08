using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hindsound.Library;
using Hindsound.Settings;
using Hindsound.Tests.Export;

namespace Hindsound.Tests.Library;

public sealed class KnownClipFoldersTests : IDisposable
{
    private readonly TempFolder _root = new();

    public void Dispose() => _root.Dispose();

    // ---------------------------------------------------------------- what the output folder change gives

    [Fact]
    public void The_folder_left_comes_first()
    {
        var known = ClipFolders.AfterOutputChange([@"D:\older"], @"D:\left", @"D:\new");

        Assert.Equal([@"D:\left", @"D:\older"], known);
    }

    [Fact]
    public void No_earlier_list_is_an_empty_list()
    {
        Assert.Equal([@"D:\left"], ClipFolders.AfterOutputChange(null, @"D:\left", @"D:\new"));
    }

    [Fact]
    public void The_same_folder_written_otherwise_is_no_change()
    {
        Assert.Equal([@"D:\older"], ClipFolders.AfterOutputChange([@"D:\older"], @"D:\same", @"d:\SAME\"));
    }

    [Fact]
    public void Going_back_to_a_folder_takes_it_out_of_the_list()
    {
        var known = ClipFolders.AfterOutputChange([@"D:\a", @"D:\b\"], @"D:\c", @"d:\B");

        Assert.Equal([@"D:\c", @"D:\a"], known);
    }

    [Fact]
    public void A_folder_already_listed_moves_to_the_front_once()
    {
        var known = ClipFolders.AfterOutputChange([@"D:\a", @"D:\b", @"d:\C\"], @"D:\c", @"D:\new");

        Assert.Equal([@"D:\c", @"D:\a", @"D:\b"], known);
    }

    [Fact]
    public void Past_eight_folders_the_oldest_goes()
    {
        var eight = Enumerable.Range(1, 8).Select(i => $@"D:\{i}").ToArray();

        var known = ClipFolders.AfterOutputChange(eight, @"D:\left", @"D:\new");

        Assert.Equal(ExportSettings.MaxKnownClipFolders, known.Count);
        Assert.Equal(8, ExportSettings.MaxKnownClipFolders);
        Assert.Equal(@"D:\left", known[0]);
        Assert.Equal(@"D:\7", known[^1]);
    }

    [Fact]
    public void The_text_is_kept_as_it_was_stored()
    {
        const string variable = "HINDSOUND_TEST_KNOWN_FOLDER";
        Environment.SetEnvironmentVariable(variable, @"D:\media");
        try
        {
            var known = ClipFolders.AfterOutputChange([$@"  %{variable}%\a  "], $@"%{variable}%\b", @"D:\new");

            Assert.Equal([$@"%{variable}%\b", $@"%{variable}%\a"], known);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void A_variable_and_its_value_are_one_folder()
    {
        const string variable = "HINDSOUND_TEST_KNOWN_SAME";
        Environment.SetEnvironmentVariable(variable, @"D:\media");
        try
        {
            var known = ClipFolders.AfterOutputChange([$@"%{variable}%\a"], @"D:\media\a", @"D:\new");

            Assert.Equal([@"D:\media\a"], known);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void A_folder_left_empty_was_the_default_folder()
    {
        Assert.Equal([ExportSettings.DefaultFolder], ClipFolders.AfterOutputChange(null, "  ", @"D:\new"));
        Assert.Equal([ExportSettings.DefaultFolder], ClipFolders.AfterOutputChange(null, null, @"D:\new"));
    }

    [Fact]
    public void Leaving_the_default_folder_for_the_default_folder_is_no_change()
    {
        Assert.Empty(ClipFolders.AfterOutputChange(null, ExportSettings.DefaultFolder, ""));
    }

    [Fact]
    public void A_folder_no_clip_could_go_to_is_not_kept()
    {
        Assert.Empty(ClipFolders.AfterOutputChange(null, @"relative\folder", @"D:\new"));
        Assert.Equal([@"D:\left"], ClipFolders.AfterOutputChange(["", "  ", @"relative\folder"], @"D:\left", @"D:\new"));
    }

    [Fact]
    public void A_new_folder_no_clip_can_go_to_still_keeps_the_folder_left()
    {
        Assert.Equal([@"D:\left"], ClipFolders.AfterOutputChange(null, @"D:\left", @"relative\folder"));
    }

    // ---------------------------------------------------------------- in the model

    [Fact]
    public void Normalizing_trims_drops_blanks_and_duplicates_and_keeps_the_first_eight()
    {
        var settings = new AppSettings();
        settings.Export.KnownClipFolders = ["  D:\\a  ", "", null!, "   ", "d:\\A", .. Enumerable.Range(1, 12).Select(i => $@"D:\{i}")];

        settings.Normalize();

        Assert.Equal(["D:\\a", .. Enumerable.Range(1, 7).Select(i => $@"D:\{i}")], settings.Export.KnownClipFolders);
    }

    [Fact]
    public void An_empty_list_is_no_list()
    {
        var settings = new AppSettings();
        settings.Export.KnownClipFolders = ["", " "];

        settings.Normalize();

        Assert.Null(settings.Export.KnownClipFolders);
    }

    [Fact]
    public void The_key_only_enters_the_file_once_a_folder_was_left()
    {
        var store = new SettingsStore(_root.Combine("settings.json"));

        store.Save(new AppSettings());
        Assert.DoesNotContain("knownClipFolders", File.ReadAllText(store.Path), StringComparison.OrdinalIgnoreCase);

        var settings = store.Load();
        Assert.Equal(SettingsLoadStatus.Loaded, store.LastStatus);
        Assert.Null(settings.Export.KnownClipFolders);
    }

    [Fact]
    public void The_list_survives_a_save_and_a_load()
    {
        var store = new SettingsStore(_root.Combine("settings.json"));
        var settings = new AppSettings();
        settings.Export.KnownClipFolders = [@"%USERPROFILE%\Music\old", @"D:\old"];

        store.Save(settings);

        using (var document = JsonDocument.Parse(File.ReadAllText(store.Path)))
        {
            Assert.Equal(2, document.RootElement.GetProperty("export").GetProperty("knownClipFolders").GetArrayLength());
        }

        Assert.Equal([@"%USERPROFILE%\Music\old", @"D:\old"], store.Load().Export.KnownClipFolders);
        Assert.Equal(SettingsLoadStatus.Loaded, store.LastStatus);
    }

    [Fact]
    public void A_file_with_the_list_stays_readable_by_a_version_that_does_not_know_it()
    {
        var store = new SettingsStore(_root.Combine("settings.json"));
        var settings = new AppSettings { ClipSeconds = 45 };
        settings.Export.OutputFolder = @"D:\new";
        settings.Export.KnownClipFolders = [@"D:\old"];
        store.Save(settings);
        Assert.Contains("knownClipFolders", File.ReadAllText(store.Path));

        // A model without the key, generated with the options of SettingsStore, is what the versions before it read with.
        var older = JsonSerializer.Deserialize(File.ReadAllText(store.Path), OlderSettingsJson.Default.OlderSettings);

        Assert.NotNull(older);
        Assert.Equal(45, older.ClipSeconds);
        Assert.Equal(@"D:\new", older.Export.OutputFolder);
    }

    [Fact]
    public void A_key_this_version_does_not_know_is_no_error_either()
    {
        var store = new SettingsStore(_root.Combine("settings.json"));
        File.WriteAllText(store.Path, """{ "clipSeconds": 45, "keyOfANewerVersion": ["a", 1], "export": { "another": { "deep": true } } }""");

        Assert.Equal(45, store.Load().ClipSeconds);
        Assert.Equal(SettingsLoadStatus.Loaded, store.LastStatus);
    }
}

public sealed class OlderSettings
{
    public int ClipSeconds { get; set; }

    public OlderExportSettings Export { get; set; } = new();
}

public sealed class OlderExportSettings
{
    public string OutputFolder { get; set; } = "";
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(OlderSettings))]
internal sealed partial class OlderSettingsJson : JsonSerializerContext;
