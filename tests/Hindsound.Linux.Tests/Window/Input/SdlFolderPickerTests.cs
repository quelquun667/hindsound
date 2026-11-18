using System.Runtime.InteropServices;
using Hindsound.Linux.Window.Input;

namespace Hindsound.Linux.Tests.Window.Input;

/// <summary>How the answer of SDL's folder dialog is read: the list SDL hands its callback, built here by hand.</summary>
public unsafe class SdlFolderPickerTests
{
    [Fact]
    public void A_folder_chosen_is_the_answer()
    {
        var folder = Marshal.StringToCoTaskMemUTF8("/home/someone/Clips été");
        try
        {
            var list = stackalloc byte*[] { (byte*)folder, null };
            var answer = new SdlFolderPicker.Answer();
            answer.Take(list, () => "unused");
            Assert.True(answer.Done);
            Assert.Equal("/home/someone/Clips été", answer.Folder);
            Assert.Null(answer.Error);
        }
        finally
        {
            Marshal.FreeCoTaskMem(folder);
        }
    }

    [Fact]
    public void An_empty_list_is_the_user_backing_out()
    {
        var list = stackalloc byte*[] { null };
        var answer = new SdlFolderPicker.Answer();
        answer.Take(list, () => "unused");
        Assert.True(answer.Done);
        Assert.Null(answer.Folder);
        Assert.Null(answer.Error);
    }

    [Fact]
    public void No_list_is_an_error_with_sdls_message()
    {
        var answer = new SdlFolderPicker.Answer();
        answer.Take(null, () => "File dialog driver unsupported");
        Assert.True(answer.Done);
        Assert.Null(answer.Folder);
        Assert.Equal("File dialog driver unsupported", answer.Error);
    }

    [Fact]
    public void The_wait_ends_even_when_reading_the_answer_fails()
    {
        var answer = new SdlFolderPicker.Answer();
        Assert.Throws<InvalidOperationException>(() => answer.Take(null, () => throw new InvalidOperationException()));
        Assert.True(answer.Done);
    }
}
