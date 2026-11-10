using Hindsound.Audio;
using Hindsound.Linux.Audio;

namespace Hindsound.Linux.Tests.Audio;

/// <summary>The process list, read from /proc, or from a folder made like it.</summary>
public sealed class ProcProcessSnapshotTests
{
    [Fact]
    public void A_stat_line_gives_the_id_the_name_and_the_parent()
    {
        var entry = ProcProcessSnapshot.ParseStat("1234 (discord) S 1200 1234 1234 0 -1 4194560 2000 0 0 0 10 5 0 0 20 0 30 0\n");

        Assert.Equal(new ProcessEntry(1234, 1200, "discord"), entry);
    }

    [Fact]
    public void A_name_with_spaces_and_parentheses_runs_to_the_last_closing_one()
    {
        var entry = ProcProcessSnapshot.ParseStat("77 (Web (Content) x) R 76 77 77 0 -1 0");

        Assert.Equal(new ProcessEntry(77, 76, "Web (Content) x"), entry);
    }

    [Fact]
    public void The_first_process_has_parent_zero()
    {
        Assert.Equal(new ProcessEntry(1, 0, "systemd"), ProcProcessSnapshot.ParseStat("1 (systemd) S 0 1 1 0 -1"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("12 (name")]
    [InlineData("x (name) S 1")]
    [InlineData("12 (name) S")]
    [InlineData("12 (name) S parent")]
    [InlineData("-3 (name) S 1")]
    public void A_line_that_is_not_a_stat_line_gives_nothing(string line)
    {
        Assert.Null(ProcProcessSnapshot.ParseStat(line));
    }

    [Fact]
    public void The_list_has_each_process_folder_lowest_id_first_and_skips_the_rest()
    {
        var root = Directory.CreateTempSubdirectory("hindsound-proc-").FullName;
        try
        {
            void Process(string folder, string? stat)
            {
                var path = Directory.CreateDirectory(Path.Combine(root, folder)).FullName;
                if (stat is not null)
                {
                    File.WriteAllText(Path.Combine(path, "stat"), stat);
                }
            }

            Process("1200", "1200 (bash) S 1 1200 1200 0");
            Process("88", "88 (pipewire) S 1 88 88 0");
            Process("1", "1 (systemd) S 0 1 1 0");
            Process("self", "1200 (bash) S 1 1200 1200 0");
            Process("sys", null);
            Process("4242", null);
            Process("500", "501 (other) S 1 501 501 0");
            Process("600", "not a stat line");

            var entries = new ProcProcessSnapshot(root).Take();

            Assert.Equal(
                [new ProcessEntry(1, 0, "systemd"), new ProcessEntry(88, 1, "pipewire"), new ProcessEntry(1200, 1, "bash")],
                entries);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Without_a_proc_folder_the_list_is_empty()
    {
        var missing = Path.Combine(Path.GetTempPath(), "hindsound-no-proc-" + Guid.NewGuid().ToString("N"));

        Assert.Empty(new ProcProcessSnapshot(missing).Take());
    }
}
