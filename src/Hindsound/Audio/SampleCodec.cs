namespace Hindsound.Audio;

/// <summary>
/// The conversion between float samples and the 16-bit integers the buffer holds. The scale is 32767 both ways,
/// so full scale survives (1 stays 1, -1 stays -1) and a stored value always decodes and encodes back to itself.
/// Louder than full scale is clipped. Neither way allocates.
/// </summary>
public static class SampleCodec
{
    private const float Scale = 32767f;

    public static short ToShort(float sample)
    {
        var clipped = sample >= 1f ? 1f : sample <= -1f ? -1f : sample;
        return (short)MathF.Round(clipped * Scale, MidpointRounding.AwayFromZero);
    }

    public static float ToFloat(short sample) => sample / Scale;

    public static void Encode(ReadOnlySpan<float> source, Span<short> destination)
    {
        for (var i = 0; i < source.Length; i++)
        {
            destination[i] = ToShort(source[i]);
        }
    }

    public static void Decode(ReadOnlySpan<short> source, Span<float> destination)
    {
        for (var i = 0; i < source.Length; i++)
        {
            destination[i] = source[i] / Scale;
        }
    }
}
