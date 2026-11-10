using Hindsound.Library;
using Hindsound.Settings;

namespace Hindsound.Tests.Library;

/// <summary>
/// The verbs of the preview, as MCI gets them: the strings the preview sent before its player had verbs, the same alias for
/// every command of a track. No command reaches MCI here.
/// </summary>
public class MciPreviewVerbsTests
{
    [Fact]
    public void Each_verb_is_the_MCI_string_of_before()
    {
        var sent = new List<string>();
        var player = new MciPreviewPlayer(command =>
        {
            sent.Add(command);
            return 0;
        });

        player.Open(@"C:\clips\a.mp3", "hindsound_preview_7");
        player.Play("hindsound_preview_7");
        player.Stop("hindsound_preview_7");
        player.Close("hindsound_preview_7");

        Assert.Equal(
            [
                "open \"C:\\clips\\a.mp3\" type mpegvideo alias hindsound_preview_7",
                "play hindsound_preview_7",
                "stop hindsound_preview_7",
                "close hindsound_preview_7",
            ],
            sent);
    }

    [Fact]
    public void A_path_with_a_space_is_quoted_whole()
    {
        var sent = new List<string>();
        new MciPreviewPlayer(command =>
        {
            sent.Add(command);
            return 0;
        }).Open(@"C:\clips\a b.mp3", "hindsound_preview_9");

        Assert.Equal(["open \"C:\\clips\\a b.mp3\" type mpegvideo alias hindsound_preview_9"], sent);
    }

    [Fact]
    public void The_MCI_error_comes_back_as_it_is()
    {
        var player = new MciPreviewPlayer(_ => 263);

        Assert.Equal(263, player.Open(@"C:\clips\gone.mp3", "hindsound_preview_8"));
        Assert.Equal(263, player.Play("hindsound_preview_8"));
    }

    [Theory]
    [InlineData(526, 0, (int)PreviewTrackState.Playing)]
    [InlineData(525, 0, (int)PreviewTrackState.Stopped)]
    [InlineData(524, 0, (int)PreviewTrackState.Other)]
    [InlineData(0, 263, (int)PreviewTrackState.Other)]
    public void The_state_of_a_track_is_its_mode_read_through_the_player(int mode, int code, int state)
    {
        var player = new MciPreviewPlayer(_ => 0, (string alias, out int m) =>
        {
            Assert.Equal("hindsound_preview_3", alias);
            m = mode;
            return code;
        });

        Assert.Equal(code, player.StateOf("hindsound_preview_3", out var result));
        Assert.Equal((PreviewTrackState)state, result);
    }

    [Theory]
    [InlineData(526, (int)PreviewTrackState.Playing)]
    [InlineData(525, (int)PreviewTrackState.Stopped)]
    [InlineData(524, (int)PreviewTrackState.Other)]
    [InlineData(529, (int)PreviewTrackState.Other)]
    [InlineData(0, (int)PreviewTrackState.Other)]
    public void An_MCI_mode_is_read_as_a_state(int mode, int state)
    {
        Assert.Equal((PreviewTrackState)state, MciPreviewPlayer.StateOfMode(mode));
    }
}

/// <summary>The Run key behind the startup model: one value, the quoted exe, compared as Windows compares paths.</summary>
public class RegistryRunKeyTests
{
    private sealed class Values : IRunKeyValues
    {
        public Dictionary<string, string> Held { get; } = [];

        public string? GetValue(string name) => Held.GetValueOrDefault(name);

        public void SetValue(string name, string value) => Held[name] = value;

        public void DeleteValue(string name) => Held.Remove(name);
    }

    [Fact]
    public void The_entry_reads_none_this_program_or_another_one()
    {
        var values = new Values();
        var key = new RegistryRunKey(values);

        Assert.Equal(RunEntry.None, key.State(@"C:\Apps\Hindsound.exe"));

        key.Enable(@"C:\Apps\Hindsound.exe");
        Assert.Equal("\"C:\\Apps\\Hindsound.exe\"", values.Held["Hindsound"]);
        Assert.Equal(RunEntry.ThisProgram, key.State(@"c:\apps\HINDSOUND.exe"));
        Assert.Equal(RunEntry.OtherProgram, key.State(@"D:\Moved\Hindsound.exe"));
        Assert.Equal(RunEntry.OtherProgram, key.State(null));

        key.Disable();
        Assert.Empty(values.Held);
    }
}
