namespace Hindsound.Ui.Window;

/// <summary>
/// The colours of one column of a wave: the bar, and the halo behind it where there is one. The halo is a colour mixed in advance
/// (see <see cref="Theme.Mix"/>), never an alpha, so a lane is only opaque rectangles.
/// </summary>
internal readonly struct BarColumn
{
    public BarColumn(uint color)
    {
        Color = color;
    }

    public BarColumn(uint color, uint halo)
    {
        Color = color;
        Halo = halo;
        HasHalo = true;
    }

    public uint Color { get; }

    public uint Halo { get; }

    public bool HasHalo { get; }
}

/// <summary>
/// Where the columns of a wave lane fall, in physical pixels: the bar is 3 design units wide rounded to a whole pixel, the pitch is
/// twice the bar, and the lane keeps 4 units free at each side. Counting in physical pixels, never in design units, gives every bar
/// the same width and every gap the same width, so the picture does not shimmer: 3 on a pitch of 6 at 100 %, 4 on 8 at 125 %,
/// 5 on 10 at 150 %, 6 on 12 at 200 %.
/// <para>
/// For the 527-unit lane of the Now page that is 87 columns at 100 %, 81 at 125 %, 78 at 150 % and 87 at 200 %.
/// </para>
/// </summary>
/// <param name="Left">The left edge of the lane.</param>
/// <param name="Right">The right edge of the lane.</param>
/// <param name="Bar">The width of a bar.</param>
/// <param name="Pitch">From the left of a bar to the left of the next one.</param>
/// <param name="Columns">How many columns fit.</param>
/// <param name="Start">The left of the first (oldest) column: the row of columns is centred in the lane.</param>
/// <param name="Mid">The row of the base line, which the bars mirror around.</param>
/// <param name="Half">The longest a bar goes from the base line, up or down.</param>
/// <param name="Line">The thickness of the base line.</param>
/// <param name="Edge">How far the halo goes beyond the bar, at each side.</param>
internal readonly record struct WaveGrid(int Left, int Right, int Bar, int Pitch, int Columns, int Start, int Mid, int Half, int Line, int Edge)
{
    /// <summary>The free space at the left and right of the columns, and above and below them, in design units.</summary>
    public const float Margin = 4;

    /// <summary>The width of a bar at 100 %, in design units.</summary>
    public const float BarUnits = 3;

    public int Gap => Pitch - Bar;

    /// <summary>The right edge of the last column.</summary>
    public int End => Start + Columns * Pitch - Gap;

    /// <summary>The left of column <paramref name="column"/>, counted from the oldest on the left.</summary>
    public int X(int column) => Start + column * Pitch;

    /// <summary>Design units to whole physical pixels, half away from zero: what the canvas does for every edge.</summary>
    public static int Px(float units, double scale) => (int)Math.Round(units * scale, MidpointRounding.AwayFromZero);

    /// <summary>The grid of a lane, drawn at this scale (1.0 is 100 %).</summary>
    public static WaveGrid Of(Box lane, double scale)
    {
        var left = Px(lane.X, scale);
        var right = Px(lane.Right, scale);
        var bar = Math.Max(1, Px(BarUnits, scale));
        var pitch = 2 * bar;
        var gap = pitch - bar;
        var width = Math.Max(0, right - left);
        var columns = Math.Max(0, (width - 2 * Px(Margin, scale) + gap) / pitch);
        var used = columns == 0 ? 0 : columns * pitch - gap;
        var half = Math.Max(0, (Px(lane.H, scale) - 2 * Px(Margin, scale)) / 2);
        return new WaveGrid(left, right, bar, pitch, columns, left + (width - used) / 2, Px(lane.MidY, scale), half,
            Math.Max(1, (int)scale), Math.Max(1, Px(1, scale)));
    }
}
