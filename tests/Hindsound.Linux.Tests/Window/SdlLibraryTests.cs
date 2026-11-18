using Hindsound.Linux.Window;

namespace Hindsound.Linux.Tests.Window;

/// <summary>Where libSDL3.so.0 is looked for: the copy next to the program first, then the system, as libmp3lame.so.0.</summary>
public sealed class SdlLibraryTests
{
    private static readonly string Program = Path.Combine(Path.GetTempPath(), "hindsound-sdl-test-program");

    [Fact]
    public void The_copy_next_to_the_program_comes_before_the_system()
    {
        var candidates = SdlLibrary.Candidates(Program);

        Assert.Equal([Path.Combine(Program, "libSDL3.so.0"), "libSDL3.so.0"], candidates);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "libSDL3.so.0"), SdlLibrary.ProgramCandidates[0]);
    }

    [Fact]
    public void The_copy_next_to_the_program_is_taken_when_it_loads()
    {
        var asked = new List<string>();
        var handle = SdlLibrary.Open(SdlLibrary.Candidates(Program), _ => true, (string candidate, out nint h) =>
        {
            asked.Add(candidate);
            h = 42;
            return true;
        }, out var from, out var error);

        Assert.Equal(42, handle);
        Assert.Equal(Path.Combine(Program, "libSDL3.so.0"), from);
        Assert.Null(error);
        Assert.Single(asked);
    }

    [Fact]
    public void A_missing_copy_is_not_handed_to_the_loader_and_the_system_one_is_taken()
    {
        var asked = new List<string>();
        var handle = SdlLibrary.Open(SdlLibrary.Candidates(Program), _ => false, (string candidate, out nint h) =>
        {
            asked.Add(candidate);
            h = 7;
            return true;
        }, out var from, out _);

        Assert.Equal(7, handle);
        Assert.Equal("libSDL3.so.0", from);
        Assert.Equal(["libSDL3.so.0"], asked);
    }

    [Fact]
    public void A_candidate_the_loader_refuses_gives_the_reason_the_loader_gave_and_the_next_one_is_tried()
    {
        var asked = new List<string>();
        var handle = SdlLibrary.Open(SdlLibrary.Candidates(Program), _ => true, (string candidate, out nint h) =>
        {
            asked.Add(candidate);
            if (asked.Count == 1)
            {
                throw new DllNotFoundException("libSDL3.so.0: version `GLIBC_2.34' not found");
            }

            h = 9;
            return true;
        }, out var from, out var error);

        Assert.Equal(9, handle);
        Assert.Equal("libSDL3.so.0", from);
        Assert.Null(error);
        Assert.Equal(2, asked.Count);
    }

    [Theory]
    [InlineData(typeof(DllNotFoundException), "version `GLIBC_2.34' not found")]
    [InlineData(typeof(BadImageFormatException), "wrong ELF class: ELFCLASS32")]
    public void When_the_loader_refuses_every_candidate_the_error_carries_its_reasons(Type exception, string reason)
    {
        var handle = SdlLibrary.Open(SdlLibrary.Candidates(Program), _ => true, (string candidate, out nint h) =>
            throw (Exception)Activator.CreateInstance(exception, reason + " in " + candidate)!, out var from, out var error);

        Assert.Equal(0, handle);
        Assert.Null(from);
        Assert.Contains(reason + " in " + Path.Combine(Program, "libSDL3.so.0"), error, StringComparison.Ordinal);
        Assert.Contains(reason + " in libSDL3.so.0)", error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_error_that_is_not_the_loaders_is_not_swallowed()
    {
        Assert.Throws<InvalidOperationException>(() => SdlLibrary.Open(SdlLibrary.Candidates(Program), _ => true, (string candidate, out nint h) =>
            throw new InvalidOperationException("a bug"), out _, out _));
    }

    [Fact]
    public void When_nothing_loads_the_error_says_what_was_tried()
    {
        var handle = SdlLibrary.Open(SdlLibrary.Candidates(Program), path => true, (string candidate, out nint h) =>
        {
            h = 0;
            return false;
        }, out var from, out var error);

        Assert.Equal(0, handle);
        Assert.Null(from);
        Assert.Equal(
            "libSDL3.so.0 was not found: " + Path.Combine(Program, "libSDL3.so.0") + " (cannot be loaded); libSDL3.so.0 (cannot be loaded)",
            error);
    }
}
