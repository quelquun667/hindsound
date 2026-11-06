namespace Hindsound.Updates;

/// <summary>A release version, X.Y.Z and nothing else: no prefix, no suffix, no leading zero, ASCII digits only.</summary>
internal readonly record struct UpdateVersion(int Major, int Minor, int Patch) : IComparable<UpdateVersion>
{
    // Nine digits always fit an int, so parsing cannot overflow.
    private const int MaxDigits = 9;

    public static bool TryParse(string? text, out UpdateVersion version)
    {
        version = default;
        if (text is null)
        {
            return false;
        }

        var parts = text.Split('.');
        if (parts.Length != 3
            || !TryParsePart(parts[0], out var major)
            || !TryParsePart(parts[1], out var minor)
            || !TryParsePart(parts[2], out var patch))
        {
            return false;
        }

        version = new UpdateVersion(major, minor, patch);
        return true;
    }

    public int CompareTo(UpdateVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0)
        {
            return major;
        }

        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public static bool operator >(UpdateVersion left, UpdateVersion right) => left.CompareTo(right) > 0;

    public static bool operator <(UpdateVersion left, UpdateVersion right) => left.CompareTo(right) < 0;

    public static bool operator >=(UpdateVersion left, UpdateVersion right) => left.CompareTo(right) >= 0;

    public static bool operator <=(UpdateVersion left, UpdateVersion right) => left.CompareTo(right) <= 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    private static bool TryParsePart(string part, out int value)
    {
        value = 0;
        if (part.Length is 0 or > MaxDigits || (part.Length > 1 && part[0] == '0'))
        {
            return false;
        }

        foreach (var c in part)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }

            value = (value * 10) + (c - '0');
        }

        return true;
    }
}
