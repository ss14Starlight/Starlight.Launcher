namespace Starlight.Launcher.Services.LocalServer.Dotnet;

public readonly record struct RuntimeVersion(int Major, int Minor, int Patch, string? Prerelease = null)
    : IComparable<RuntimeVersion>
{
    public bool IsPrerelease => Prerelease is not null;

    public static bool TryParse(string? text, out RuntimeVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var span = text.AsSpan().Trim();

        var plus = span.IndexOf('+');
        if (plus >= 0)
            span = span[..plus];

        string? prerelease = null;
        var dash = span.IndexOf('-');
        if (dash >= 0)
        {
            prerelease = span[(dash + 1)..].ToString();
            if (prerelease.Length == 0)
                return false;
            span = span[..dash];
        }

        Span<Range> parts = stackalloc Range[4];
        var count = span.Split(parts, '.');
        if (count is < 2 or > 3)
            return false;

        if (!int.TryParse(span[parts[0]], out var major) || !int.TryParse(span[parts[1]], out var minor))
            return false;

        var patch = 0;
        if (count == 3 && !int.TryParse(span[parts[2]], out patch))
            return false;

        if (major < 0 || minor < 0 || patch < 0)
            return false;

        version = new RuntimeVersion(major, minor, patch, prerelease);
        return true;
    }

    public int CompareTo(RuntimeVersion other)
    {
        var c = Major.CompareTo(other.Major);
        if (c != 0)
            return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0)
            return c;
        c = Patch.CompareTo(other.Patch);
        return c != 0 ? c : ComparePrerelease(Prerelease, other.Prerelease);
    }

    private static int ComparePrerelease(string? a, string? b)
    {
        if (a == b)
            return 0;
        if (a is null)
            return 1;
        if (b is null)
            return -1;

        var aParts = a.Split('.');
        var bParts = b.Split('.');
        for (var i = 0; i < Math.Min(aParts.Length, bParts.Length); i++)
        {
            var aNumeric = int.TryParse(aParts[i], out var aNum);
            var bNumeric = int.TryParse(bParts[i], out var bNum);

            var c = (aNumeric, bNumeric) switch
            {
                (true, true) => aNum.CompareTo(bNum),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(aParts[i], bParts[i])
            };

            if (c != 0)
                return c;
        }

        return aParts.Length.CompareTo(bParts.Length);
    }

    public static bool operator <(RuntimeVersion a, RuntimeVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(RuntimeVersion a, RuntimeVersion b) => a.CompareTo(b) > 0;
    public static bool operator <=(RuntimeVersion a, RuntimeVersion b) => a.CompareTo(b) <= 0;
    public static bool operator >=(RuntimeVersion a, RuntimeVersion b) => a.CompareTo(b) >= 0;

    public override string ToString() =>
        Prerelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{Prerelease}";
}
