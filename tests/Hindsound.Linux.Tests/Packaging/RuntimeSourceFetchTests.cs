using System.Text.RegularExpressions;

namespace Hindsound.Linux.Tests.Packaging;

/// <summary>
/// fetch-runtime-sources.sh puts the source of libfuse and of the AppImage runtime next to the packages, for the LGPL-2.1 of
/// libfuse. It only runs on a Linux runner of GitHub Actions, and fails there only while its checks are still in it: these
/// tests keep the checks, keep its pins equal to the notices and to the runtime build-packages.sh downloads, and keep those
/// pins in the script alone.
/// </summary>
public sealed partial class RuntimeSourceFetchTests
{
    private static readonly string Root = RepositoryRoot();

    private static readonly string ScriptPath = Path.Combine(Root, "tools", "release", "fetch-runtime-sources.sh");

    [Fact]
    public void The_pins_are_those_of_the_notices()
    {
        var notices = Read(Path.Combine(Root, "THIRD-PARTY-NOTICES.md"));
        var lgpl = Section(notices, "### libfuse and the LGPL-2.1");
        var built = Section(notices, "### What is built into it");
        var runtimeRow = notices.Split('\n').Single(line => line.StartsWith("| AppImage type2 runtime ", StringComparison.Ordinal));

        Assert.Contains("<" + Pin("FUSE_URL") + ">", lgpl, StringComparison.Ordinal);
        Assert.Contains("`" + Pin("FUSE_SHA256") + "`", lgpl, StringComparison.Ordinal);
        Assert.Contains("`" + Path.GetFileName(Pin("FUSE_URL")) + "`", built, StringComparison.Ordinal);
        Assert.Contains("(`" + Pin("FUSE_SHA256") + "`)", built, StringComparison.Ordinal);
        Assert.Contains("<https://github.com/AppImage/type2-runtime/tree/" + Pin("RUNTIME_TAG") + ">", lgpl, StringComparison.Ordinal);
        Assert.Contains("at the tag `" + Pin("RUNTIME_TAG") + "` (commit `" + Pin("RUNTIME_COMMIT") + "`)", runtimeRow, StringComparison.Ordinal);
        Assert.Contains("[AppImage/type2-runtime](" + Pin("RUNTIME_REPOSITORY")[..^".git".Length] + ")", runtimeRow, StringComparison.Ordinal);
    }

    [Fact]
    public void The_runtime_build_packages_downloads_is_the_one_of_the_pinned_tag()
    {
        var build = Read(Path.Combine(Root, "packaging", "linux", "build-packages.sh"));
        var url = Assert.Single(RuntimeUrl().Matches(build)).Groups[1].Value;

        Assert.Equal("https://github.com/AppImage/type2-runtime.git", Pin("RUNTIME_REPOSITORY"));
        Assert.Equal("https://github.com/AppImage/type2-runtime/releases/download/" + Pin("RUNTIME_TAG") + "/runtime-x86_64", url);
    }

    [Fact]
    public void The_files_it_writes_are_those_the_release_carries()
    {
        var script = Read(ScriptPath);

        Assert.Equal("fuse-3.15.0.tar.xz", Path.GetFileName(Pin("FUSE_URL")));
        Assert.Equal("20251108", Pin("RUNTIME_TAG"));
        Assert.Contains("fuse_name=\"${FUSE_URL##*/}\"\n", script, StringComparison.Ordinal);
        Assert.Contains("prefix=\"type2-runtime-$RUNTIME_TAG/\"\n", script, StringComparison.Ordinal);
        Assert.Contains("runtime_name=\"type2-runtime-$RUNTIME_TAG-source.tar.gz\"\n", script, StringComparison.Ordinal);
        Assert.Contains("set -euo pipefail\n", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_script_refuses_a_libfuse_archive_with_another_sha256()
    {
        var script = Read(ScriptPath);

        Assert.Contains(
            "curl --fail --silent --show-error --location --retry 3 --connect-timeout 30 --max-time 300 --output \"$fuse\" \"$FUSE_URL\"\nfuse_sha256=\"$(sha256sum \"$fuse\" | cut -d' ' -f1)\"\nif [ \"$fuse_sha256\" != \"$FUSE_SHA256\" ]; then\n  echo \"$fuse_name has the SHA-256 $fuse_sha256, expected $FUSE_SHA256. Nothing was copied.\" >&2\n  exit 1\nfi\n",
            script,
            StringComparison.Ordinal);
        var check = script.IndexOf("if [ \"$fuse_sha256\" != \"$FUSE_SHA256\" ]; then\n", StringComparison.Ordinal);
        Assert.True(check < script.IndexOf("cp -- \"$fuse\" \"$out/$fuse_name\"", StringComparison.Ordinal), "the archive is copied before its SHA-256 is checked");
    }

    [Fact]
    public void The_script_refuses_any_commit_but_the_pinned_one()
    {
        var script = Read(ScriptPath);

        Assert.Contains("git -C \"$repository\" fetch --quiet --depth 1 \"$RUNTIME_REPOSITORY\" \"$RUNTIME_COMMIT\"\n", script, StringComparison.Ordinal);
        Assert.Contains("fetched=\"$(git -C \"$repository\" rev-parse --verify 'FETCH_HEAD^{commit}')\"\nif [ \"$fetched\" != \"$RUNTIME_COMMIT\" ]; then\n  echo \"The fetch gave the commit $fetched, expected $RUNTIME_COMMIT. Nothing was written.\" >&2\n  exit 1\nfi\n", script, StringComparison.Ordinal);
        Assert.Contains("git -C \"$repository\" archive --format=tar.gz --prefix=\"$prefix\" --output=\"$runtime\" \"$RUNTIME_COMMIT\"\n", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_script_refuses_an_archive_without_the_licence_the_runtime_and_the_patch()
    {
        var script = Read(ScriptPath);

        Assert.Contains(
            "for needed in LICENSE src/runtime/runtime.c patches/libfuse/mount.c.diff; do\n  if ! printf '%s\\n' \"$listing\" | grep -qxF -- \"$prefix$needed\"; then\n    echo \"$runtime_name has no $prefix$needed. Nothing was copied.\" >&2\n    exit 1\n  fi\ndone\n",
            script,
            StringComparison.Ordinal);
        Assert.True(
            script.IndexOf("for needed in ", StringComparison.Ordinal) < script.IndexOf("cp -- \"$runtime\" \"$out/$runtime_name\"", StringComparison.Ordinal),
            "the archive is copied before what it holds is checked");
    }

    [Fact]
    public void The_linux_job_puts_both_files_in_its_artifact_after_checking_the_packages()
    {
        var job = LinuxJob();
        var step = job.IndexOf("- name: Add the source of the AppImage runtime\n        run: bash tools/release/fetch-runtime-sources.sh \"$RUNNER_TEMP/runtime-sources-work\" \"$RUNNER_TEMP/packages\"\n", StringComparison.Ordinal);
        var checkPackages = job.IndexOf("run: bash packaging/linux/check-packages.sh ", StringComparison.Ordinal);
        var upload = job.IndexOf("uses: actions/upload-artifact@", StringComparison.Ordinal);

        Assert.True(step >= 0, "the linux job does not run fetch-runtime-sources.sh into its packages folder");
        Assert.True(checkPackages >= 0 && checkPackages < step, "the sources are added before check-packages.sh, which checks the packages folder");
        Assert.True(upload > step, "the sources are added after the upload of the artifact");
        Assert.Contains("name: hindsound-linux-x64\n          path: ${{ runner.temp }}/packages/\n", job[upload..], StringComparison.Ordinal);
    }

    [Fact]
    public void The_pins_are_written_in_the_script_alone()
    {
        var pinned = new[] { Pin("FUSE_SHA256"), Pin("RUNTIME_COMMIT") };
        var others = new[] { Path.Combine(Root, ".github"), Path.Combine(Root, "tools"), Path.Combine(Root, "packaging"), Path.Combine(Root, "installer") }
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            .Where(path => !string.Equals(Path.GetFullPath(path), Path.GetFullPath(ScriptPath), StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(others);
        foreach (var path in others)
        {
            var text = File.ReadAllText(path);
            foreach (var pin in pinned)
            {
                Assert.False(text.Contains(pin, StringComparison.Ordinal), Path.GetRelativePath(Root, path) + " repeats the pin " + pin + " of fetch-runtime-sources.sh");
            }
        }
    }

    [Fact]
    public void The_check_of_the_release_reads_the_libfuse_sha256_from_the_script_and_no_workflow_overrides_it()
    {
        var check = Read(Path.Combine(Root, "tools", "release", "check-release-files.ps1"));

        Assert.Contains("[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'fetch-runtime-sources.sh'))", check, StringComparison.Ordinal);
        Assert.Contains("-cnotmatch '(?m)^readonly FUSE_SHA256=''([0-9a-f]{64})''\\r?$'", check, StringComparison.Ordinal);
        Assert.Contains("[string]$FuseSha256 = ''\n", check, StringComparison.Ordinal);
        foreach (var workflow in Directory.GetFiles(Path.Combine(Root, ".github", "workflows"), "*.yml"))
        {
            Assert.DoesNotContain("-FuseSha256", File.ReadAllText(workflow), StringComparison.Ordinal);
        }
    }

    /// <summary>The value of a <c>readonly NAME='value'</c> line of the script, which must be there exactly once.</summary>
    private static string Pin(string name)
    {
        var matches = Regex.Matches(Read(ScriptPath), "^readonly " + name + "='([^'\\n]+)'$", RegexOptions.Multiline);
        return Assert.Single(matches).Groups[1].Value;
    }

    /// <summary>The text of a section, up to the next heading of the same or a higher level.</summary>
    private static string Section(string text, string heading)
    {
        var start = text.IndexOf("\n" + heading + "\n", StringComparison.Ordinal);
        Assert.True(start >= 0, "no section " + heading);
        var level = heading.IndexOf(' ', StringComparison.Ordinal);
        var next = Regex.Match(text[(start + heading.Length + 2)..], "\n#{1," + level + "} ");
        return next.Success ? text.Substring(start, heading.Length + 2 + next.Index) : text[start..];
    }

    /// <summary>The linux job of release-files.yml, up to the next job.</summary>
    private static string LinuxJob()
    {
        var files = Read(Path.Combine(Root, ".github", "workflows", "release-files.yml"));
        var start = files.IndexOf("\n  linux:\n", StringComparison.Ordinal);
        Assert.True(start >= 0, "release-files.yml has no linux job");
        var next = Regex.Match(files[(start + 1)..], "\n  [a-z-]+:\n");
        return next.Success ? files.Substring(start, next.Index + 1) : files[start..];
    }

    private static string Read(string path) => File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);

    [GeneratedRegex(@"^readonly RUNTIME_URL='([^'\n]+)'$", RegexOptions.Multiline)]
    private static partial Regex RuntimeUrl();

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
