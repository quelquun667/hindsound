using System.Text.RegularExpressions;

namespace Hindsound.Linux.Tests.Packaging;

/// <summary>
/// The rules of the release, read from the workflows: the release waits for the Linux and the macOS jobs, sends every file
/// of the release and checks each one downloaded back before it publishes, and the rehearsal of every pull request builds
/// the same files with the same workflow without any way to publish them, nor to read a secret. The certificate of the Mac
/// app is read by release.yml only, in one step of its job sign-mac.
/// </summary>
public sealed partial class ReleaseWorkflowTests
{
    private static readonly string Workflows = Path.Combine(RepositoryRoot(), ".github", "workflows");

    /// <summary>The files of a release, as release.yml names them.</summary>
    private static readonly string[] FileNames =
    [
        "Hindsound-$env:VERSION-Setup.exe",
        "Hindsound-$env:VERSION-win-x64-portable.zip",
        "hindsound-$env:VERSION-linux-x64.AppImage",
        "hindsound-$env:VERSION-linux-x64.tar.gz",
        "Hindsound-$env:VERSION-macos-arm64.zip",
        "hindsound-update-win-x64.json",
        "hindsound-update-win-x64-portable.json",
        "hindsound-update-linux-x64.json",
        "hindsound-update-macos-arm64.json",
        "lame-3.100.tar.gz",
        "fuse-3.15.0.tar.xz",
        "type2-runtime-20251108-source.tar.gz",
        "type2-runtime-20251108-libraries-source.tar.gz",
        "SHA256SUMS",
    ];

    public static TheoryData<string> ReleaseFiles() => new(FileNames);

    [Fact]
    public void The_release_waits_for_the_files_built_on_Linux()
    {
        var release = Read("release.yml");
        var files = Read("release-files.yml");

        Assert.Contains("\n  release:\n    needs: [check, files, sign-mac, assemble-signed]\n", release, StringComparison.Ordinal);
        Assert.Contains("\n  files:\n    needs: check\n    permissions:\n      contents: read\n    uses: ./.github/workflows/release-files.yml\n", release, StringComparison.Ordinal);
        Assert.Contains("\n  assemble:\n    needs: [windows, linux, macos]\n", files, StringComparison.Ordinal);

        var linux = Job(files, "linux");
        Assert.Contains("\n    runs-on: ubuntu-22.04\n", linux, StringComparison.Ordinal);
        Assert.Contains("bash packaging/linux/build-packages.sh ", linux, StringComparison.Ordinal);
        Assert.Contains("bash packaging/linux/check-packages.sh ", linux, StringComparison.Ordinal);
        Assert.Contains("name: hindsound-linux-x64\n", linux, StringComparison.Ordinal);
        Assert.Contains("name: hindsound-linux-x64\n", Job(files, "assemble"), StringComparison.Ordinal);

        var macos = Job(files, "macos");
        Assert.Contains("\n    runs-on: macos-26\n", macos, StringComparison.Ordinal);
        Assert.Contains("bash packaging/mac/build-app.sh ", macos, StringComparison.Ordinal);
        Assert.Contains("bash packaging/mac/check-zip.sh ", macos, StringComparison.Ordinal);
        Assert.Contains("name: hindsound-macos-arm64\n", macos, StringComparison.Ordinal);
        Assert.Contains("name: hindsound-macos-arm64\n", Job(files, "assemble"), StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_release_signs_the_Mac_app_with_the_certificate_and_sends_that_zip()
    {
        var release = Read("release.yml");
        var signMac = Job(release, "sign-mac");
        var signing = Step(signMac, "Sign the Mac app with the certificate");
        var assembleSigned = Job(release, "assemble-signed");

        // The two secrets of the certificate, in the env of that one step, and nowhere else: not passed to release-files.yml.
        // Every way to read a secret counts: secrets.NAME, secrets['NAME'], toJSON(secrets), secrets: inherit.
        Assert.Equal(2, Regex.Matches(release, @"secrets\s*[.\[:}]|[(,]\s*secrets\b").Count);
        Assert.Contains("        env:\n          MACOS_SIGNING_P12: ${{ secrets.MACOS_SIGNING_P12 }}\n          MACOS_SIGNING_P12_PASSWORD: ${{ secrets.MACOS_SIGNING_P12_PASSWORD }}\n", signing, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets", Job(release, "files"), StringComparison.Ordinal);
        Assert.DoesNotContain("secrets: inherit", release, StringComparison.Ordinal);

        // sign-mac signs again the zip of the job macos, and only reads.
        Assert.Contains("\n    needs: [check, files]\n    runs-on: macos-26\n", signMac, StringComparison.Ordinal);
        Assert.Contains("\n    permissions:\n      contents: read\n", signMac, StringComparison.Ordinal);
        Assert.Contains("name: hindsound-macos-arm64\n          path: ${{ runner.temp }}/adhoc\n", Step(signMac, "Take the Mac app signed ad hoc"), StringComparison.Ordinal);
        Assert.Contains("bash packaging/mac/resign-zip.sh \"$RUNNER_TEMP/adhoc/$zip\" \"$RUNNER_TEMP/signed/$zip\" \"$RUNNER_TEMP/resign-work\" \"$(tr -d '[:space:]' < packaging/mac/signing-certificate.sha1)\"\n", signing, StringComparison.Ordinal);

        // The secrets live in the environment mac-signing, which only the tags v* reach: no workflow of a branch reads them.
        Assert.Contains("\n    timeout-minutes: 30\n    environment: mac-signing\n", signMac, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(release, @"(?m)^\s*environment:"));

        // With the SHA-1 of the certificate committed, a missing secret stops the release; the .p12 goes whatever happens.
        Assert.Contains("if [ -s packaging/mac/signing-certificate.sha1 ]; then\n            [ -n \"$MACOS_SIGNING_P12\" ] && [ -n \"$MACOS_SIGNING_P12_PASSWORD\" ] || { echo \"::error ", signing, StringComparison.Ordinal);
        Assert.Contains("signing=\"$(mktemp -d)\"\n            trap 'rm -rf \"$signing\"' EXIT\n", signing, StringComparison.Ordinal);

        // The app is started in the next step only, which has no secret, once the .p12 and the keychain are gone.
        var check = Step(signMac, "Check the signed Mac app");
        Assert.DoesNotContain("check-zip", signing, StringComparison.Ordinal);
        Assert.Contains("bash packaging/mac/check-zip.sh \"$RUNNER_TEMP/signed/$zip\" \"$VERSION\" \"$RUNNER_TEMP/zip-check\" \"$certificate\"\n", check, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets", check, StringComparison.Ordinal);
        Assert.DoesNotContain("env:", check, StringComparison.Ordinal);
        Assert.True(signMac.IndexOf("- name: Sign the Mac app", StringComparison.Ordinal) < signMac.IndexOf("- name: Check the signed Mac app", StringComparison.Ordinal));
        Assert.Contains("name: hindsound-macos-arm64-signed\n          path: ${{ runner.temp }}/signed/\n", Step(signMac, "Keep the signed Mac app"), StringComparison.Ordinal);

        // assemble-signed gathers the files around that zip, checks them, and the release sends what it kept.
        Assert.Contains("\n    needs: [check, sign-mac]\n", assembleSigned, StringComparison.Ordinal);
        Assert.Contains("\n    permissions:\n      contents: read\n", assembleSigned, StringComparison.Ordinal);
        Assert.Contains("name: hindsound-macos-arm64-signed\n          path: ${{ runner.temp }}/macos\n", Step(assembleSigned, "Take the signed Mac app"), StringComparison.Ordinal);
        Assert.Contains("-MacFolder \"$env:RUNNER_TEMP/macos\" -OutFolder \"$env:RUNNER_TEMP/release\"", Step(assembleSigned, "Gather the files of the release"), StringComparison.Ordinal);
        Assert.Contains("./tools/release/check-release-files.ps1 -Folder \"$env:RUNNER_TEMP/release\" ", Step(assembleSigned, "Check the files of the release"), StringComparison.Ordinal);
        Assert.Contains("name: release-files-signed\n          path: ${{ runner.temp }}/release/\n", Step(assembleSigned, "Keep the files of the release"), StringComparison.Ordinal);
        Assert.Contains("name: release-files-signed\n          path: ${{ runner.temp }}/out\n", Step(Job(release, "release"), "Take the files of the release"), StringComparison.Ordinal);
        foreach (var job in new[] { signMac, assembleSigned })
        {
            Assert.DoesNotContain("GH_TOKEN", job, StringComparison.Ordinal);
            Assert.DoesNotContain("github.token", job, StringComparison.Ordinal);
            Assert.DoesNotContain("gh release", job, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_proof_of_the_encoding_runs_tests_built_before_it()
    {
        var windows = Job(Read("release-files.yml"), "windows");
        var build = windows.IndexOf("run: dotnet build tests/Hindsound.Tests/Hindsound.Tests.csproj -c Release\n", StringComparison.Ordinal);
        var proof = windows.IndexOf("run: ./tools/release/prove-encoding.ps1 ", StringComparison.Ordinal);

        Assert.True(build >= 0 && proof > build, "prove-encoding.ps1 runs its tests with --no-build: the windows job must build them first");
    }

    [Theory]
    [MemberData(nameof(ReleaseFiles))]
    public void Every_file_of_the_release_is_sent_then_checked_against_the_file_sent(string name)
    {
        var attach = Step(Job(Read("release.yml"), "release"), "Attach the files");
        var upload = attach.Split('\n').Single(line => line.Contains("gh release upload ", StringComparison.Ordinal));

        Assert.Contains("\"$env:RUNNER_TEMP/out/" + name + "\"", upload, StringComparison.Ordinal);
        var sent = attach.IndexOf("gh release upload ", StringComparison.Ordinal);
        var downloaded = attach.IndexOf("gh release download $env:TAG --dir \"$env:RUNNER_TEMP/verify\"", StringComparison.Ordinal);
        var checkedBack = attach.IndexOf("./tools/release/check-release-files.ps1 -Folder \"$env:RUNNER_TEMP/verify\" -Version $env:VERSION -Repository $env:GITHUB_REPOSITORY -Expected \"$env:RUNNER_TEMP/out\"", StringComparison.Ordinal);
        Assert.True(sent >= 0 && downloaded > sent && checkedBack > downloaded, "the files are not sent, downloaded back, then checked");
    }

    [Fact]
    public void The_check_of_the_files_knows_every_file_of_the_release()
    {
        var check = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "release", "check-release-files.ps1"));

        foreach (var name in FileNames)
        {
            Assert.Contains(name.Replace("$env:VERSION", "$Version", StringComparison.Ordinal), check, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_release_is_published_only_after_the_files_downloaded_back_are_checked()
    {
        var release = Read("release.yml");
        var job = Job(release, "release");
        var steps = StepNames().Matches(job).Select(match => match.Groups[1].Value).ToList();

        Assert.Equal(new[] { "Check out", "Write the release notes", "Take the files of the release", "Check the files of the release", "Create the draft release", "Attach the files", "Publish the release" }, steps);
        Assert.Single(Regex.Matches(release, "--draft=false"));
        Assert.Contains("--draft=false", Step(job, "Publish the release"), StringComparison.Ordinal);
        Assert.Contains("gh release create $env:TAG --draft ", Step(job, "Create the draft release"), StringComparison.Ordinal);
        Assert.DoesNotContain("continue-on-error", release, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failure_is_reported_by_a_job_that_cannot_publish()
    {
        var release = Read("release.yml");
        var report = Job(release, "report-failure");

        Assert.Equal(new[] { "    if: failure()" }, release.Split('\n').Where(line => line.TrimStart().StartsWith("if:", StringComparison.Ordinal)));
        Assert.Contains("\n    needs: [check, files, sign-mac, assemble-signed, release]\n", report, StringComparison.Ordinal);
        Assert.Contains("\n    permissions: {}\n", report, StringComparison.Ordinal);
        Assert.Contains("::error::", report, StringComparison.Ordinal);
        Assert.DoesNotContain("GH_TOKEN", report, StringComparison.Ordinal);
        Assert.DoesNotContain("gh ", report, StringComparison.Ordinal);
        Assert.DoesNotContain("uses:", report, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ci.yml")]
    [InlineData("release-files.yml")]
    public void A_pull_request_never_reaches_a_release(string workflow)
    {
        var text = Read(workflow);

        Assert.DoesNotContain("gh release", text, StringComparison.Ordinal);
        Assert.DoesNotContain("GH_TOKEN", text, StringComparison.Ordinal);
        Assert.DoesNotContain("github.token", text, StringComparison.Ordinal);
        Assert.DoesNotContain("contents: write", text, StringComparison.Ordinal);
        // Not even the word: neither secrets.NAME nor secrets['NAME'], toJSON(secrets) or secrets: inherit.
        Assert.DoesNotContain("secrets", text, StringComparison.Ordinal);
        Assert.DoesNotContain("environment:", text, StringComparison.Ordinal);
        Assert.Contains("\npermissions:\n  contents: read\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_CI_rehearses_the_release_with_the_workflow_of_the_tag()
    {
        var ci = Read("ci.yml");
        var files = Read("release-files.yml");

        Assert.Contains("\n  release-files:\n    needs: release-version\n    uses: ./.github/workflows/release-files.yml\n", ci, StringComparison.Ordinal);
        Assert.Contains("\non:\n  workflow_call:\n", files, StringComparison.Ordinal);
        Assert.DoesNotContain("\n  push:", files, StringComparison.Ordinal);
        Assert.DoesNotContain("\n  pull_request:", files, StringComparison.Ordinal);
        Assert.Contains("name: release-files\n", Step(Job(files, "assemble"), "Keep the files of the release"), StringComparison.Ordinal);
        Assert.Contains("./tools/release/check-release-files.ps1 -Folder \"$env:RUNNER_TEMP/release\" ", Step(Job(files, "assemble"), "Check the files of the release"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("release.yml")]
    [InlineData("release-files.yml")]
    public void Every_action_of_the_release_is_pinned_by_the_sha_of_a_commit(string workflow)
    {
        var uses = UsesLine().Matches(Read(workflow)).Select(match => match.Groups[1].Value).ToList();

        Assert.NotEmpty(uses);
        foreach (var reference in uses)
        {
            var local = LocalWorkflow().Match(reference);
            if (local.Success)
            {
                Assert.True(File.Exists(Path.Combine(Workflows, local.Groups[1].Value)), reference);
                continue;
            }

            Assert.Matches("^[^@]+@[0-9a-f]{40}$", reference);
        }
    }

    private static string Read(string workflow) =>
        File.ReadAllText(Path.Combine(Workflows, workflow)).Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>The text of one job, up to the next job or the end of the file.</summary>
    private static string Job(string workflow, string name)
    {
        var start = workflow.IndexOf("\n  " + name + ":\n", StringComparison.Ordinal);
        Assert.True(start >= 0, "no job named " + name);
        var next = NextJob().Match(workflow[(start + 1)..]);
        return next.Success ? workflow.Substring(start, next.Index + 1) : workflow[start..];
    }

    /// <summary>The text of one step of a job, from its name to the next step.</summary>
    private static string Step(string job, string name)
    {
        var start = job.IndexOf("- name: " + name + "\n", StringComparison.Ordinal);
        Assert.True(start >= 0, "no step named " + name);
        var next = job.IndexOf("\n      - name:", start + 1, StringComparison.Ordinal);
        return next < 0 ? job[start..] : job[start..next];
    }

    [GeneratedRegex("\n  [a-z-]+:\n")]
    private static partial Regex NextJob();

    [GeneratedRegex(@"^      - name: (.+)$", RegexOptions.Multiline)]
    private static partial Regex StepNames();

    [GeneratedRegex(@"^\s*(?:- )?uses:\s*(\S+)", RegexOptions.Multiline)]
    private static partial Regex UsesLine();

    [GeneratedRegex(@"^\./\.github/workflows/([a-z-]+\.yml)$")]
    private static partial Regex LocalWorkflow();

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
