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
            // A workflow of this repository, called by its path, runs at the commit of the run itself.
            var local = Regex.Match(reference, @"^\./\.github/workflows/([a-z-]+\.yml)$");
            if (local.Success)
            {
                Assert.True(File.Exists(Path.Combine(RepositoryRoot(), ".github", "workflows", local.Groups[1].Value)), reference);
                continue;
            }

            Assert.Matches("^[^@]+@[0-9a-f]{40}$", reference);
        }
    }

    /// <summary>The steps of the job mac that prove the app itself: the job mac-older runs them again, word for word.</summary>
    private static readonly string[] AppProofs =
    [
        "Draw the pages and prove the MP3 with the app",
        "Prove the loop, the icon, the shortcut and --save with the app",
        "Prove the shortcuts follow the keyboard layout with the app",
        "Show a notification with the app",
        "Prove the devices and the applications that play with the app",
        "Prove the capture with the app",
        "Play a clip with the app and hear it on BlackHole",
        "Run the whole app, save a clip of a 1 kHz tone and quit",
        "Start the app from --save when none is running",
        "Change the default output during a capture with the app",
    ];

    [Fact]
    public void The_older_system_runs_on_macOS_15_with_the_app_the_job_mac_built()
    {
        var older = JobOf("mac-older");

        Assert.Contains("\n    needs: mac\n", older, StringComparison.Ordinal);
        Assert.Contains("os: [macos-15]", older, StringComparison.Ordinal);
        Assert.Contains("\n    runs-on: ${{ matrix.os }}\n", older, StringComparison.Ordinal);
        Assert.Contains("fail-fast: false", older, StringComparison.Ordinal);
        Assert.Contains("timeout-minutes:", older, StringComparison.Ordinal);

        // The job mac hands over its bundle, its dylibs and its fonts; the older job takes them and builds nothing of the app.
        var pack = StepOf(JobOf("mac"), "Pack the app for the older systems");
        Assert.Contains("ditto -c -k", pack, StringComparison.Ordinal);
        var keep = StepOf(JobOf("mac"), "Keep the app for the older systems");
        Assert.Contains("name: mac-app\n", keep, StringComparison.Ordinal);
        var fetch = StepOf(older, "Fetch the app of the job mac");
        Assert.Contains("uses: actions/download-artifact@", fetch, StringComparison.Ordinal);
        Assert.Contains("name: mac-app\n", fetch, StringComparison.Ordinal);
        Assert.DoesNotContain("build-app.sh", older, StringComparison.Ordinal);
        Assert.DoesNotContain("natives-work", older, StringComparison.Ordinal);
    }

    [Fact]
    public void The_older_systems_say_their_version_and_refuse_to_prove_the_capture_under_14_4()
    {
        var older = JobOf("mac-older");
        var system = StepOf(older, "Say which macOS this is");

        Assert.Contains("sw_vers\n", system, StringComparison.Ordinal);
        Assert.Contains("::error::", system, StringComparison.Ordinal);
        Assert.Contains("under the 14.4 of the Info.plist", system, StringComparison.Ordinal);
        Assert.Contains("NOT proven", system, StringComparison.Ordinal);
        Assert.True(older.IndexOf("Say which macOS this is", StringComparison.Ordinal) < older.IndexOf("Fetch the app of the job mac", StringComparison.Ordinal));

        // Everything after it waits for it (the proofs of the app wait for the unpacking, which waits for it): no step proves
        // anything on a system it refused.
        foreach (var name in new[] { "Fetch the app of the job mac", "Unpack the app and check what came through", "Build" })
        {
            Assert.Contains("steps.system.outcome == 'success'", StepOf(older, name), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_older_systems_run_the_tests_of_the_Mac_and_the_proofs_of_the_job_mac_word_for_word()
    {
        var mac = JobOf("mac");
        var older = JobOf("mac-older");

        var tests = StepOf(older, "Test");
        Assert.Contains("tests/Hindsound.Mac.Tests/Hindsound.Mac.Tests.csproj", tests, StringComparison.Ordinal);
        Assert.Contains("--filter \"Category!=Device\"", tests, StringComparison.Ordinal);
        Assert.DoesNotContain("Category!=Mac", tests, StringComparison.Ordinal);
        Assert.Contains("HINDSOUND_FONT_DIR", tests, StringComparison.Ordinal);
        Assert.Equal(WithoutConditions(StepOf(mac, "Prove the desktop parts")), WithoutConditions(StepOf(older, "Prove the desktop parts")));

        foreach (var name in AppProofs)
        {
            Assert.Equal(WithoutConditions(StepOf(mac, name)), WithoutConditions(StepOf(older, name)));
            Assert.Contains("steps.unpack.outcome == 'success'", StepOf(older, name), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A step without its if: lines (the two jobs do not wait for the same step) and without the comments between the steps
    /// (the end of a step text), for the comparison of the two.
    /// </summary>
    private static string WithoutConditions(string step) =>
        string.Join('\n', step.Split('\n').Where(line => !line.StartsWith("        if:", StringComparison.Ordinal) && !line.StartsWith("      #", StringComparison.Ordinal)))
            .TrimEnd();

    private static string JobOf(string name)
    {
        var ci = Ci();
        var start = ci.IndexOf("\n  " + name + ":\n", StringComparison.Ordinal);
        Assert.True(start >= 0, "ci.yml has no job " + name);
        var next = Regex.Match(ci[(start + 1)..], "\n  [a-z-]+:\n");
        return next.Success ? ci.Substring(start, next.Index + 1) : ci[start..];
    }

    /// <summary>The text of one step of a job, from its name to the next step.</summary>
    private static string StepOf(string job, string name)
    {
        var start = job.IndexOf("- name: " + name + "\n", StringComparison.Ordinal);
        Assert.True(start >= 0, "the job has no step named " + name);
        var next = job.IndexOf("\n      - name:", start + 1, StringComparison.Ordinal);
        return next < 0 ? job[start..] : job[start..next];
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
