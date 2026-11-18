using System.Buffers.Binary;

namespace Hindsound.Linux.Window.Text;

/// <summary>
/// The height of a line of one font, by the rule of the Windows text (ICanvasText.LineHeight): the ascent and the descent are
/// each the larger of the font's own metrics (hhea, or the typographic ones of OS/2 for a font that sets USE_TYPO_METRICS)
/// and of usWinAscent and usWinDescent, then rounded at the em; a VDMX entry for that em, which GDI used, can only make them
/// larger. Read from the raw tables, so it is the same on every system.
/// </summary>
internal sealed class VerticalMetrics
{
    private const int UseTypoMetrics = 1 << 7;
    private readonly Dictionary<int, (int YMax, int YMin)> _vdmx = [];

    private VerticalMetrics(int unitsPerEm, int ascent, int descent)
    {
        UnitsPerEm = unitsPerEm;
        Ascent = ascent;
        Descent = descent;
    }

    public int UnitsPerEm { get; }

    /// <summary>In design units, above the baseline.</summary>
    public int Ascent { get; }

    /// <summary>In design units, below the baseline, positive.</summary>
    public int Descent { get; }

    /// <summary>The metrics from the head, hhea, OS/2 and VDMX tables; any of them may be empty or cut short.</summary>
    public static VerticalMetrics Read(ReadOnlySpan<byte> head, ReadOnlySpan<byte> hhea, ReadOnlySpan<byte> os2, ReadOnlySpan<byte> vdmx)
    {
        var unitsPerEm = head.Length >= 20 ? BinaryPrimitives.ReadUInt16BigEndian(head[18..]) : 0;
        if (unitsPerEm == 0)
        {
            unitsPerEm = 1000;
        }

        int? ascent = null, descent = null;
        if (os2.Length >= 72 && (BinaryPrimitives.ReadUInt16BigEndian(os2[62..]) & UseTypoMetrics) != 0)
        {
            ascent = BinaryPrimitives.ReadInt16BigEndian(os2[68..]);
            descent = -BinaryPrimitives.ReadInt16BigEndian(os2[70..]);
        }
        else if (hhea.Length >= 8)
        {
            ascent = BinaryPrimitives.ReadInt16BigEndian(hhea[4..]);
            descent = -BinaryPrimitives.ReadInt16BigEndian(hhea[6..]);
        }

        if (os2.Length >= 78)
        {
            var winAscent = BinaryPrimitives.ReadUInt16BigEndian(os2[74..]);
            var winDescent = BinaryPrimitives.ReadUInt16BigEndian(os2[76..]);
            ascent = Math.Max(ascent ?? 0, winAscent);
            descent = Math.Max(descent ?? 0, winDescent);
        }

        // With no metrics at all, those the Windows text gives a font it cannot find.
        var metrics = new VerticalMetrics(
            unitsPerEm,
            ascent ?? (int)MathF.Round(1.08f * unitsPerEm),
            descent ?? (int)MathF.Round(0.25f * unitsPerEm));
        metrics.ReadVdmx(vdmx);
        return metrics;
    }

    /// <summary>The ascent and the descent in whole pixels at this em, as the Windows text rounds them.</summary>
    public (int Ascent, int Descent) AtEm(int em)
    {
        var ascent = (int)MathF.Round((float)Ascent / UnitsPerEm * em);
        var descent = (int)MathF.Round((float)Descent / UnitsPerEm * em);
        if (_vdmx.TryGetValue(em, out var entry))
        {
            ascent = Math.Max(ascent, entry.YMax);
            descent = Math.Max(descent, -entry.YMin);
        }

        return (ascent, descent);
    }

    // VDMX: version, numRecs, numRatios, then numRatios RatioRange (charset, xRatio, yStartRatio, yEndRatio), numRatios
    // offsets to groups, and each group: recs, startsz, endsz, then recs entries (yPelHeight, yMax, yMin). The group of the
    // first ratio that holds 1:1 (or the default 0:0:0) is the one of a screen with square pixels.
    private void ReadVdmx(ReadOnlySpan<byte> vdmx)
    {
        if (vdmx.Length < 6)
        {
            return;
        }

        var ratios = BinaryPrimitives.ReadUInt16BigEndian(vdmx[4..]);
        for (var i = 0; i < ratios; i++)
        {
            var at = 6 + (4 * i);
            var offsetAt = 6 + (4 * ratios) + (2 * i);
            if (offsetAt + 2 > vdmx.Length)
            {
                return;
            }

            int x = vdmx[at + 1], yStart = vdmx[at + 2], yEnd = vdmx[at + 3];
            if (!(x == 0 && yStart == 0 && yEnd == 0) && !(yStart <= x && x <= yEnd))
            {
                continue;
            }

            var group = BinaryPrimitives.ReadUInt16BigEndian(vdmx[offsetAt..]);
            if (group + 4 > vdmx.Length)
            {
                return;
            }

            var records = BinaryPrimitives.ReadUInt16BigEndian(vdmx[group..]);
            for (var r = 0; r < records; r++)
            {
                var entry = group + 4 + (6 * r);
                if (entry + 6 > vdmx.Length)
                {
                    return;
                }

                _vdmx[BinaryPrimitives.ReadUInt16BigEndian(vdmx[entry..])] =
                    (BinaryPrimitives.ReadInt16BigEndian(vdmx[(entry + 2)..]), BinaryPrimitives.ReadInt16BigEndian(vdmx[(entry + 4)..]));
            }

            return;
        }
    }
}
