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
