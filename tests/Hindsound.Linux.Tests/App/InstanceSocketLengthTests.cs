using System.Text;
using Hindsound.Linux.App;

namespace Hindsound.Linux.Tests.App;

/// <summary>
/// The socket path fits sun_path of struct sockaddr_un on Linux: 108 bytes, the last one a NUL. Written out here, not taken
/// from the code, so a wrong limit there shows.
/// </summary>
public class InstanceSocketLengthTests
{
    private const int SunPathBytes = 108;

    private static Func<string, string?> Runtime(string folder) => name => name == InstancePlace.RuntimeVariable ? folder : null;

    // A runtime folder that gives a socket path of exactly that many bytes, without the NUL.
    private static string FolderFor(int socketBytes) =>
        "/" + new string('a', socketBytes - "/".Length - "/".Length - InstancePlace.FileNameFor("k").Length - ".sock".Length);

    [Fact]
    public void A_socket_path_of_107_bytes_fits()
    {
        var place = InstancePlace.For(Runtime(FolderFor(SunPathBytes - 1)), null, "k");

        Assert.NotNull(place);
        Assert.Equal(SunPathBytes - 1, Encoding.UTF8.GetByteCount(place.Socket));
    }

    [Fact]
    public void A_socket_path_of_108_bytes_leaves_no_room_for_the_nul_and_gives_no_place()
    {
        Assert.Null(InstancePlace.For(Runtime(FolderFor(SunPathBytes)), null, "k"));
    }
}
