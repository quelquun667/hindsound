using Hindsound.Linux.Export;

namespace Hindsound.Linux.Tests.Export;

/// <summary>Where the app looks for libmp3lame. Pure: no library is loaded, so these run on every system.</summary>
public sealed class LameLibraryProgramFolderTests
{
    [Fact]
    public void The_app_looks_in_the_folder_of_the_program_first_then_in_the_system()
    {
        // AppContext.BaseDirectory is the folder of the published single file; Environment.ProcessPath would be the host
        // (dotnet) under a test runner, and the library is not shipped there.
        Assert.Equal(
            [Path.Combine(AppContext.BaseDirectory, LameLibrary.SystemName), LameLibrary.SystemName],
            LameLibrary.ProgramCandidates);
    }
}
