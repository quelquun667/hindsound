using System.Text.RegularExpressions;

namespace Hindsound.Linux.Tests.Packaging;

/// <summary>
/// The shape of the core-on-linux job that the first runs of the CI showed to matter: a failing project must not hide the
/// other one, the log of the sound server must survive a failure, and every action is pinned by the commit it names.
/// </summary>
public sealed partial class CiWorkflowTests
{
    [Fact]
    public void A_failing_project_does_not_stop_the_other_one_from_running()
    {
        var step = CoreOnLinuxStep("Test the portable code and the Linux app");

        Assert.Contains("Hindsound.Core.Tests", step, StringComparison.Ordinal);
        Assert.Contains("Hindsound.Linux.Tests", step, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"bash\s+-\w*e\w*\b", step);
        Assert.DoesNotContain("set -e", step, StringComparison.Ordinal);
        Assert.Contains("|| failed=1", step, StringComparison.Ordinal);
        Assert.Contains("exit $failed", step, StringComparison.Ordinal);
    }

    [Fact]
    public void The_log_of_the_sound_server_goes_to_a_file_and_is_kept_when_the_tests_fail()
    {
        var job = CoreOnLinuxJob();
        var tests = CoreOnLinuxStep("Test the portable code and the Linux app");

        Assert.Contains("--log-target=\"file:$PULSE_LOG\"", tests, StringComparison.Ordinal);

        var keep = CoreOnLinuxStep("Keep the log of the sound server");
        Assert.Contains("if: failure()", keep, StringComparison.Ordinal);
        Assert.Contains("uses: actions/upload-artifact@", keep, StringComparison.Ordinal);
        Assert.Contains("pulseaudio.log", keep, StringComparison.Ordinal);
        Assert.True(job.IndexOf("Keep the log of the sound server", StringComparison.Ordinal) > job.IndexOf("Test the portable code", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_action_of_the_workflow_is_pinned_by_the_sha_of_a_commit()
    {
        var uses = UsesLine().Matches(Ci()).Select(match => match.Groups[1].Value).ToList();

        Assert.NotEmpty(uses);
        foreach (var reference in uses)
        {
            Assert.Matches("^[^@]+@[0-9a-f]{40}$", reference);
        }
    }

    private static string CoreOnLinuxJob()
    {
        var ci = Ci();
        var start = ci.IndexOf("\n  core-on-linux:\n", StringComparison.Ordinal);
        Assert.True(start >= 0, "ci.yml has no core-on-linux job");
        var next = Regex.Match(ci[(start + 1)..], "\n  [a-z-]+:\n");
        return next.Success ? ci.Substring(start, next.Index + 1) : ci[start..];
    }

    /// <summary>The text of one step of the core-on-linux job, from its name to the next step.</summary>
    private static string CoreOnLinuxStep(string name)
    {
        var job = CoreOnLinuxJob();
        var start = job.IndexOf("- name: " + name + "\n", StringComparison.Ordinal);
        Assert.True(start >= 0, "the core-on-linux job has no step named " + name);
        var next = job.IndexOf("\n      - name:", start + 1, StringComparison.Ordinal);
        return next < 0 ? job[start..] : job[start..next];
    }

    private static string Ci() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", "ci.yml")).Replace("\r\n", "\n", StringComparison.Ordinal);

    [GeneratedRegex(@"^\s*(?:- )?uses:\s*(\S+)", RegexOptions.Multiline)]
    private static partial Regex UsesLine();

    private static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Hindsound.slnx")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("Hindsound.slnx is not above the test assembly");
    }
}
