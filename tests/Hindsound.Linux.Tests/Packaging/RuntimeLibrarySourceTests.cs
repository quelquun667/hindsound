using System.Text.RegularExpressions;

namespace Hindsound.Linux.Tests.Packaging;

/// <summary>
/// The runtime at the head of the AppImage is linked with libfuse, under the LGPL-2.1, and with squashfuse, Zstandard, zlib,
/// mimalloc and musl. fetch-runtime-sources.sh puts the source of those other libraries in
/// type2-runtime-20251108-libraries-source.tar.gz, from pins it alone holds. It only runs on a Linux runner, so these tests
/// keep its pins, the list of what the archive holds, and each of its refusals.
/// </summary>
public sealed class RuntimeLibrarySourceTests
{
    private const string Prefix = "type2-runtime-20251108-libraries-source/";

    private static readonly string Root = RepositoryRoot();

    private static readonly string ScriptPath = Path.Combine(Root, "tools", "release", "fetch-runtime-sources.sh");

    /// <summary>The Alpine 3.21 packages the runtime links, as the notices give them, read on 2026-11-12.</summary>
    private static readonly string[] AlpinePackages =
    [
        "main/musl 1.2.5-r9 efd4d5df7be0ea48d98512671f676fc5ceb14899 musl-1.2.5.tar.gz 7bb7f7833923cd69c7a1a9b8a5f1784bfd5289663eb6061dcd43d583e45987df8a68a1be05d75cc1c88a3f5b610653d1a70f4a9cff4d8f7fd41ae73ee058c17c https://musl.libc.org/releases/musl-1.2.5.tar.gz",
        "main/zstd 1.5.6-r2 5c2ddf18f193dafdd25ae7391d96c7e30766eecd zstd-1.5.6.tar.gz ca12dffd86618ca008e1ecc79056c1129cb4e61668bf13a3cd5b2fa5c93bc9c92c80f64c1870c68b9c20009d9b3a834eac70db72242d5106125a1c53cccf8de8 https://github.com/facebook/zstd/archive/v1.5.6.tar.gz",
        "main/zlib 1.3.1-r2 4c2722e7ecc1c5e3ba882429044a101497121223 zlib-1.3.1.tar.gz 580677aad97093829090d4b605ac81c50327e74a6c2de0b85dd2e8525553f3ddde17556ea46f8f007f89e435493c9a20bc997d1ef1c1c2c23274528e3c46b94f https://zlib.net/fossils/zlib-1.3.1.tar.gz",
        "community/mimalloc2 2.1.7-r0 5a1a5dc3216a69e3cd66a57ee07cb5ef01b8be39 mimalloc-2.1.7.tar.gz 4e30976758015c76a146acc1bfc8501e2e5c61b81db77d253de0d58a8edef987669243f232210667b32ef8da3a33286642acb56ba526fd24c4ba925b44403730 https://github.com/microsoft/mimalloc/archive/v2.1.7/mimalloc-2.1.7.tar.gz",
    ];

    /// <summary>Every file of the archive but README.txt, with the SHA-256 computed from the downloads on 2026-11-12.</summary>
    private static readonly string[] LibraryFiles =
    [
        "0eed39319f139afde8515010ff59baf24de9e47ea316a315398e8027d198202d  mimalloc-2.1.7.tar.gz",
        "a9a118bbe84d8764da0ea0d28b3ab3fae8477fc7e4085d90102b8596fc7c75e4  musl-1.2.5.tar.gz",
        "db0238c5981dabbd80ee09ae15387f390091668ca060a7bc38047912491443d3  squashfuse-0.5.2.tar.gz",
        "9a93b2b7dfdac77ceba5a558a580e74667dd6fede4585b91eefb60f03b72df23  zlib-1.3.1.tar.gz",
        "30f35f71c1203369dc979ecde0400ffea93c27391bfd2ac5a9715d2173d92ff7  zstd-1.5.6.tar.gz",
        "bb2618381e98ef508b41697d9177b7d1fef6f61bf38543aa055b9e0002c3c54b  aports/community/mimalloc2/APKBUILD",
        "bbf09f82b9a187399f33f6cc4136bd088f475d769f385aa1f08d0850473b04ae  aports/community/mimalloc2/cmake-add-insecure-suffix.patch",
        "5c31b8e5c25bcc4fe51e913dce8e057bebaf9c8980b88f76bc19d05d67a906ba  aports/main/musl/APKBUILD",
        "ab382da2bb5e1cc32d8904be28fb13f8ad3ce813967bd4bf7f5b1c6baee33e17  aports/main/musl/CVE-2025-26519.patch",
        "299a7d75a09de3e2e11e7fb4acc3182e4a14e868093d2f30938fce9bfcff13da  aports/main/musl/__stack_chk_fail_local.c",
        "d87d0cbb3690ae2c5d8cc218349fd8278b93855dd625deaf7ae50e320aad247c  aports/main/musl/getconf.c",
        "b25f32d306d542246e9358ef65cd67c12b3e3a77656bc467853d3b0c76b5fbbd  aports/main/musl/getent.c",
        "2a65e03b94217063d72fe998e5fb2e25cd01716078a3ba1b0ab574ab205111e6  aports/main/musl/handle-aux-at_base.patch",
        "f79a2930a2e5bb0624321589edf8b889d1e9b603e01e6b7ae214616605b3fdd7  aports/main/musl/iconv.c",
        "1321d542b40921a4c248bb2d15dbd03fcbbf008c8725eb2043d3cab62faeba48  aports/main/musl/isatty-glibc-align.patch",
        "b4a2c06db38742e8c42c3c9838b285a7d8cdac6c091ff3df5ff9a15f1e41b9c7  aports/main/musl/ldconfig",
        "7f5806124d4a89536e53a07bfe95fcd0a8f1b7a27fc0dfc89c78c724dea1c924  aports/main/musl/ldso-fix-non-functional-fix-to-early-dynamic-PAGE_SI.patch",
        "28efacd507c01b89e1725ed33723f3f9fdfe6cb83eb78ffea1e2acb7fda8a52c  aports/main/musl/loongarch64-Add-lsx-and-lasx-vector-structure-defini.patch",
        "1efb4604e0b28b84e10faf37d17bad448510e73f6a3db89c8be47d38b9e85138  aports/main/musl/loongarch64-add-TLSDESC-support.patch",
        "8849080b49dff046b1f29db24e54ffe6262b5884078091994db77efdafca2505  aports/main/musl/loongarch64-add-bits-hwcap.h.patch",
        "d73f85a90fe52930dd8b5d16cb7f3369a994918c0e41ae3c3dc9a25df4b45099  aports/main/musl/loongarch64-add-fpu-and-simd-context-to-signal.h.patch",
        "47ba2fb65734f65fbbfc980d261cf86ab99e8c5bad9d0977a59620cde18ffd53  aports/main/zlib/APKBUILD",
        "7344ccab938fb86eb3eb437f1caa11c99ceca58c475b31458d3ab04cce2e2388  aports/main/zstd/APKBUILD",
    ];

    [Fact]
    public void The_squashfuse_pins_are_those_of_the_runtime_build_script()
    {
        Assert.Equal("https://github.com/vasi/squashfuse/archive/0.5.2.tar.gz", Pin("SQUASHFUSE_URL"));
        Assert.Equal("squashfuse-0.5.2.tar.gz", Pin("SQUASHFUSE_NAME"));
        Assert.Equal("db0238c5981dabbd80ee09ae15387f390091668ca060a7bc38047912491443d3", Pin("SQUASHFUSE_SHA256"));
        Assert.Contains(Pin("SQUASHFUSE_SHA256") + "  " + Pin("SQUASHFUSE_NAME"), LibraryFiles);
    }

    [Fact]
    public void The_alpine_packages_are_pinned_to_their_version_their_commit_and_their_upstream_archive()
    {
        Assert.Equal("https://raw.githubusercontent.com/alpinelinux/aports", Pin("APORTS_FILES"));
        Assert.Equal(AlpinePackages, Lines(MultiLinePin("ALPINE_PACKAGES")));
    }

    [Fact]
    public void The_archive_holds_these_files_with_these_sha256()
    {
        Assert.Equal(LibraryFiles, Lines(MultiLinePin("LIBRARY_FILES")));
    }

    [Fact]
    public void The_archive_holds_the_readme_the_squashfuse_archive_and_for_each_package_its_archive_and_its_recipe_folder()
    {
        var paths = LibraryFiles.Select(line => line[66..]).ToList();
        var expected = new List<string> { "README.txt", "squashfuse-0.5.2.tar.gz" };
        foreach (var fields in AlpinePackages.Select(line => line.Split(' ')))
        {
            expected.Add(fields[3]);
            Assert.Contains("aports/" + fields[0] + "/APKBUILD", paths);
        }

        Assert.All(paths, path => Assert.True(
            expected.Contains(path) || AlpinePackages.Any(line => path.StartsWith("aports/" + line.Split(' ')[0] + "/", StringComparison.Ordinal)),
            path + " is no file of squashfuse or of a package"));
        Assert.Equal(paths.Count + 1, paths.Concat(["README.txt"]).Distinct(StringComparer.Ordinal).Count());
        Assert.All(expected, name => Assert.True(name == "README.txt" || paths.Contains(name), name + " is not in the archive"));

        var entries = paths.Concat(["README.txt"]).Select(path => Prefix + path).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(24, entries.Count);
        Assert.Equal(Prefix + "README.txt", entries[0]);
        Assert.Equal(Prefix + "zstd-1.5.6.tar.gz", entries[^1]);
    }

    [Fact]
    public void The_archive_is_named_and_prefixed_after_the_runtime_tag_and_copied_with_the_two_others()
    {
        var script = Read(ScriptPath);

        Assert.Contains("libraries_prefix=\"type2-runtime-$RUNTIME_TAG-libraries-source/\"\n", script, StringComparison.Ordinal);
        Assert.Contains("libraries_name=\"type2-runtime-$RUNTIME_TAG-libraries-source.tar.gz\"\n", script, StringComparison.Ordinal);
        Assert.Contains("for name in \"$fuse_name\" \"$runtime_name\" \"$libraries_name\"; do\n", script, StringComparison.Ordinal);
        Assert.Contains("cp -- \"$libraries\" \"$out/$libraries_name\"\n", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_script_refuses_any_file_without_its_pinned_sha256()
    {
        var script = Read(ScriptPath);

        Assert.Contains(
            "  curl --fail --silent --show-error --location --retry 3 --connect-timeout 30 --max-time 300 --output \"$staging/$path\" \"$url\"\n  actual=\"$(sha256sum \"$staging/$path\" | cut -d' ' -f1)\"\n  if [ \"$actual\" != \"$sha256\" ]; then\n    echo \"$path has the SHA-256 $actual, expected $sha256. Nothing was copied.\" >&2\n    exit 1\n  fi\ndone <<< \"$LIBRARY_FILES\"\n",
            script,
            StringComparison.Ordinal);
        Assert.Contains("if [ \"$squashfuse_sha256\" != \"$SQUASHFUSE_SHA256\" ]; then\n", script, StringComparison.Ordinal);
        Assert.Contains("      echo \"$APORTS_FILES/$commit/$folder/${1##*/}\"\n", script, StringComparison.Ordinal);
        Assert.Contains("  echo \"LIBRARY_FILES names $1, which is no file of squashfuse or of ALPINE_PACKAGES. Nothing was copied.\" >&2\n  return 1\n", script, StringComparison.Ordinal);
        AssertBeforeCopies(script, "done <<< \"$LIBRARY_FILES\"\n");
    }

    [Fact]
    public void The_script_refuses_a_recipe_of_another_version_an_archive_without_the_sha512_of_its_recipe_and_a_folder_with_a_patch_more_or_less()
    {
        var script = Read(ScriptPath);

        Assert.Contains("  if ! [[ \"$commit\" =~ ^[0-9a-f]{40}$ ]]; then\n", script, StringComparison.Ordinal);
        Assert.Contains("  if [ ! -f \"$apkbuild\" ] || ! grep -qxF \"pkgver=${version%-r*}\" \"$apkbuild\" || ! grep -qxF \"pkgrel=${version##*-r}\" \"$apkbuild\"; then\n", script, StringComparison.Ordinal);
        Assert.Contains("  actual=\"$(sha512sum \"$staging/$archive\" | cut -d' ' -f1)\"\n  if [ \"$actual\" != \"$sha512\" ]; then\n", script, StringComparison.Ordinal);
        Assert.Contains("  listed=\"$(sed -n '/^sha512sums=\"/,/^\"/p' \"$apkbuild\" | grep -E '^[0-9a-f]{128}  ' | LC_ALL=C sort)\"\n", script, StringComparison.Ordinal);
        Assert.Contains("  if [ \"$listed\" != \"$held\" ]; then\n", script, StringComparison.Ordinal);
        AssertBeforeCopies(script, "  if [ \"$listed\" != \"$held\" ]; then\n");
    }

    [Fact]
    public void The_archive_is_the_same_bytes_for_the_same_files_and_is_read_back_before_it_is_copied()
    {
        var script = Read(ScriptPath);

        Assert.Contains("date=\"$(git -C \"$repository\" show -s --format=%ct \"$RUNTIME_COMMIT\")\"\n", script, StringComparison.Ordinal);
        Assert.Contains("(cd \"$tree\" && find \"${libraries_prefix%/}\" -type f | LC_ALL=C sort) > \"$work/libraries-files\"\n", script, StringComparison.Ordinal);
        Assert.Contains(
            "tar --create --format=ustar --no-recursion --owner=0 --group=0 --numeric-owner --mode=a=r,u+w --mtime=\"@$date\" \\\n  --directory=\"$tree\" --files-from=\"$work/libraries-files\" --file=- | gzip -9 -n > \"$libraries\"\n",
            script,
            StringComparison.Ordinal);
        Assert.Contains("if [ \"$(tar -tzf \"$libraries\")\" != \"$expected\" ]; then\n", script, StringComparison.Ordinal);
        Assert.Contains("  actual=\"$(tar -xzOf \"$libraries\" \"$libraries_prefix$path\" | sha256sum | cut -d' ' -f1)\"\n", script, StringComparison.Ordinal);
        AssertBeforeCopies(script, "if [ \"$(tar -tzf \"$libraries\")\" != \"$expected\" ]; then\n");
        AssertBeforeCopies(script, "  actual=\"$(tar -xzOf \"$libraries\" \"$libraries_prefix$path\" | sha256sum | cut -d' ' -f1)\"\n");
    }

    [Fact]
    public void A_second_run_in_the_same_work_folder_is_refused()
    {
        var script = Read(ScriptPath);

        Assert.Contains("tree=\"$work/libraries\"\nif [ -e \"$tree\" ]; then\n  echo \"$tree already exists: give an empty WORK_DIR.\" >&2\n  exit 1\nfi\n", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_notices_name_each_library_its_version_its_commit_and_the_third_file()
    {
        var notices = Read(Path.Combine(Root, "THIRD-PARTY-NOTICES.md"));
        var built = Section(notices, "### What is built into it");
        var lgpl = Section(notices, "### libfuse and the LGPL-2.1");

        foreach (var fields in AlpinePackages.Select(line => line.Split(' ')))
        {
            Assert.Contains("`" + fields[0] + "` " + fields[1], built, StringComparison.Ordinal);
            Assert.Contains("`" + fields[2] + "` for `" + fields[0] + "`", built, StringComparison.Ordinal);
            Assert.Contains("<https://github.com/alpinelinux/aports/tree/" + fields[2] + "/" + fields[0] + ">", lgpl, StringComparison.Ordinal);
        }

        Assert.Contains("<" + Pin("SQUASHFUSE_URL") + ">, SHA-256 `" + Pin("SQUASHFUSE_SHA256") + "`", lgpl, StringComparison.Ordinal);
        Assert.Contains("- `type2-runtime-20251108-libraries-source.tar.gz`, ", lgpl, StringComparison.Ordinal);
        Assert.Contains("With these three, the release meets point (d)", lgpl, StringComparison.Ordinal);
        Assert.DoesNotContain("Every release that hands out the AppImage meets point (d)", lgpl, StringComparison.Ordinal);
    }

    [Fact]
    public void The_check_of_the_release_reads_the_files_from_the_script_and_no_workflow_overrides_them()
    {
        var check = Read(Path.Combine(Root, "tools", "release", "check-release-files.ps1"));

        Assert.Contains("-cnotmatch '(?ms)^readonly LIBRARY_FILES=''([^'']*)''\\r?$'", check, StringComparison.Ordinal);
        Assert.Contains("[string]$LibraryFiles = '',\n", check, StringComparison.Ordinal);
        Assert.Contains("$librariesPrefix = '" + Prefix + "'\n", check, StringComparison.Ordinal);
        foreach (var workflow in Directory.GetFiles(Path.Combine(Root, ".github", "workflows"), "*.yml"))
        {
            Assert.DoesNotContain("-LibraryFiles", File.ReadAllText(workflow), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_new_pins_are_written_in_the_script_alone()
    {
        var pinned = AlpinePackages.Select(line => line.Split(' ')).SelectMany(fields => new[] { fields[2], fields[4] })
            .Concat(LibraryFiles.Select(line => line[..64]).Where(sha256 => sha256 != Pin("SQUASHFUSE_SHA256")))
            .ToList();
        var others = new[] { Path.Combine(Root, ".github"), Path.Combine(Root, "tools"), Path.Combine(Root, "packaging"), Path.Combine(Root, "installer") }
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            .Where(path => !string.Equals(Path.GetFullPath(path), Path.GetFullPath(ScriptPath), StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Equal(4 * 2 + LibraryFiles.Length - 1, pinned.Count);
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

    private static void AssertBeforeCopies(string script, string check)
    {
        var at = script.IndexOf(check, StringComparison.Ordinal);
        var firstCopy = script.IndexOf("cp -- \"$fuse\" \"$out/$fuse_name\"", StringComparison.Ordinal);
        Assert.True(at >= 0, "the script has no " + check);
        Assert.True(firstCopy > at, "a file is copied into OUT_DIR before " + check.Trim());
    }

    private static string[] Lines(string value) =>
        value.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>The value of a <c>readonly NAME='value'</c> line of the script, which must be there exactly once.</summary>
    private static string Pin(string name)
    {
        var matches = Regex.Matches(Read(ScriptPath), "^readonly " + name + "='([^'\\n]+)'$", RegexOptions.Multiline);
        return Assert.Single(matches).Groups[1].Value;
    }

    /// <summary>The value of a <c>readonly NAME='...'</c> that spans lines, which must be there exactly once.</summary>
    private static string MultiLinePin(string name)
    {
        var matches = Regex.Matches(Read(ScriptPath), "^readonly " + name + "='([^']*)'$", RegexOptions.Multiline);
        var value = Assert.Single(matches).Groups[1].Value;
        Assert.EndsWith("\n", value, StringComparison.Ordinal);
        return value;
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

    private static string Read(string path) => File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);

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
