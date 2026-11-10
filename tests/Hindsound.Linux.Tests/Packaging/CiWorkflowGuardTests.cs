using System.Text.RegularExpressions;

namespace Hindsound.Linux.Tests.Packaging;

/// <summary>
/// The two guards of the core-on-linux job that a looser reading of the step would miss: the result of each test run is
/// what fails the step, and the log the sound server writes is the very file the artifact step keeps.
/// </summary>
public sealed partial class CiWorkflowTests
{
    [Fact]
    public void Every_test_run_of_the_job_marks_the_step_as_failed_when_it_fails()
    {
        // The step has no "set -e": a dotnet test without "|| failed=1" would fail without changing what the step exits
        // with. The sound server lines carry the same guard, so the test lines are checked one by one.
        var step = CoreOnLinuxStep("Test the portable code and the Linux app");
        var runs = step.Split('\n').Where(line => line.Contains("dotnet test", StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(runs);
        Assert.All(runs, line => Assert.Matches(@"\|\|\s*failed=1\s*$", line));
    }

    [Fact]
    public void The_artifact_step_keeps_the_file_the_sound_server_writes()
    {
        var written = PulseLog().Match(CoreOnLinuxStep("Test the portable code and the Linux app"));
        var kept = UploadPath().Match(CoreOnLinuxStep("Keep the log of the sound server"));

        Assert.True(written.Success, "the test step does not set PULSE_LOG");
        Assert.True(kept.Success, "the artifact step names no path");
        Assert.Equal(written.Groups[1].Value, kept.Groups[1].Value);
    }

    [GeneratedRegex(@"^\s*PULSE_LOG:\s*(\S.*?)\s*$", RegexOptions.Multiline)]
    private static partial Regex PulseLog();

    [GeneratedRegex(@"^\s*path:\s*(\S.*?)\s*$", RegexOptions.Multiline)]
    private static partial Regex UploadPath();
}
