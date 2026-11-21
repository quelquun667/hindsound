using System.Buffers.Binary;
using Hindsound.Linux.Window.Text;
using Hindsound.Unix.Window.Text;

namespace Hindsound.Linux.Tests.Window.Text;

/// <summary>The height of a line from the font's tables, by the rule of the Windows text.</summary>
public sealed class VerticalMetricsTests
{
    private static byte[] Head(int unitsPerEm)
    {
        var head = new byte[54];
        BinaryPrimitives.WriteUInt16BigEndian(head.AsSpan(18), (ushort)unitsPerEm);
        return head;
    }

    private static byte[] Hhea(int ascender, int descender)
    {
        var hhea = new byte[36];
        BinaryPrimitives.WriteInt16BigEndian(hhea.AsSpan(4), (short)ascender);
        BinaryPrimitives.WriteInt16BigEndian(hhea.AsSpan(6), (short)descender);
        return hhea;
    }

    private static byte[] Os2(int winAscent, int winDescent, bool useTypo = false, int typoAscender = 0, int typoDescender = 0)
    {
        var os2 = new byte[96];
        BinaryPrimitives.WriteUInt16BigEndian(os2.AsSpan(62), (ushort)(useTypo ? 1 << 7 : 0));
        BinaryPrimitives.WriteInt16BigEndian(os2.AsSpan(68), (short)typoAscender);
        BinaryPrimitives.WriteInt16BigEndian(os2.AsSpan(70), (short)typoDescender);
        BinaryPrimitives.WriteUInt16BigEndian(os2.AsSpan(74), (ushort)winAscent);
        BinaryPrimitives.WriteUInt16BigEndian(os2.AsSpan(76), (ushort)winDescent);
        return os2;
    }

    // One ratio, one group with the given entries (ppem, yMax, yMin).
    private static byte[] Vdmx(byte xRatio, byte yStart, byte yEnd, params (int Ppem, int YMax, int YMin)[] entries)
    {
        var vdmx = new byte[6 + 4 + 2 + 4 + (6 * entries.Length)];
        BinaryPrimitives.WriteUInt16BigEndian(vdmx.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(vdmx.AsSpan(4), 1);
        vdmx[7] = xRatio;
        vdmx[8] = yStart;
        vdmx[9] = yEnd;
        BinaryPrimitives.WriteUInt16BigEndian(vdmx.AsSpan(10), 12);
        BinaryPrimitives.WriteUInt16BigEndian(vdmx.AsSpan(12), (ushort)entries.Length);
        for (var i = 0; i < entries.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(vdmx.AsSpan(16 + (6 * i)), (ushort)entries[i].Ppem);
            BinaryPrimitives.WriteInt16BigEndian(vdmx.AsSpan(18 + (6 * i)), (short)entries[i].YMax);
            BinaryPrimitives.WriteInt16BigEndian(vdmx.AsSpan(20 + (6 * i)), (short)entries[i].YMin);
        }

        return vdmx;
    }

    [Theory]
    [InlineData(12, 16)]
    [InlineData(13, 17)]
    [InlineData(14, 19)]
    [InlineData(16, 21)]
    public void Segoe_UIs_metrics_give_the_line_the_Windows_text_measured(int em, int lineHeight)
    {
        // Segoe UI and Selawik: 2048 units per em, ascent 2210 and descent 514 in hhea and in usWin.
        var metrics = VerticalMetrics.Read(Head(2048), Hhea(2210, -514), Os2(2210, 514), []);

        var (ascent, descent) = metrics.AtEm(em);

        Assert.Equal(lineHeight, ascent + descent);
    }

    [Fact]
    public void The_typographic_metrics_count_only_for_a_font_that_asks_for_them()
    {
        var asks = VerticalMetrics.Read(Head(1000), Hhea(700, -200), Os2(800, 250, useTypo: true, 900, -300), []);
        var doesNot = VerticalMetrics.Read(Head(1000), Hhea(700, -200), Os2(800, 250, useTypo: false, 900, -300), []);

        Assert.Equal((900, 300), (asks.Ascent, asks.Descent));
        Assert.Equal((800, 250), (doesNot.Ascent, doesNot.Descent));
    }

    [Fact]
    public void Each_of_ascent_and_descent_takes_the_larger_of_the_fonts_own_and_of_usWin()
    {
        var metrics = VerticalMetrics.Read(Head(1000), Hhea(940, -100), Os2(800, 260), []);

        Assert.Equal((940, 260), (metrics.Ascent, metrics.Descent));
        Assert.Equal((9, 3), metrics.AtEm(10));
    }

    [Fact]
    public void A_font_with_no_metrics_takes_those_of_a_font_the_Windows_text_cannot_find()
    {
        var metrics = VerticalMetrics.Read([], [], [], []);

        Assert.Equal(1000, metrics.UnitsPerEm);
        Assert.Equal((1080, 250), (metrics.Ascent, metrics.Descent));
    }

    [Fact]
    public void A_VDMX_entry_of_square_pixels_can_only_make_the_line_taller_at_its_own_size()
    {
        var vdmx = Vdmx(1, 1, 1, (20, 23, -6), (26, 20, -2));
        var metrics = VerticalMetrics.Read(Head(2048), Hhea(2210, -514), Os2(2210, 514), vdmx);

        Assert.Equal((23, 6), metrics.AtEm(20));
        Assert.Equal((28, 7), metrics.AtEm(26));
        Assert.Equal((13, 3), metrics.AtEm(12));
    }

    [Fact]
    public void A_VDMX_ratio_of_other_pixels_or_a_table_cut_short_is_left_alone()
    {
        var other = VerticalMetrics.Read(Head(2048), Hhea(2210, -514), Os2(2210, 514), Vdmx(2, 1, 1, (20, 23, -6)));
        var cut = VerticalMetrics.Read(Head(2048), Hhea(2210, -514), Os2(2210, 514), Vdmx(0, 0, 0, (20, 23, -6))[..18]);

        Assert.Equal((22, 5), other.AtEm(20));
        Assert.Equal((22, 5), cut.AtEm(20));
    }
}
