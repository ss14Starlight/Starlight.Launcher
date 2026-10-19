namespace Starlight.Launcher.WebUI.Services;

/// <summary>
/// Calendar of holiday themes. Every holiday is an anchor date (fixed or computed per year)
/// plus a window of days around it, so a theme can stay active for several days and
/// ranges may cross the new year.
/// </summary>
public static class HolidayThemes
{
    private sealed record Holiday(Func<int, DateOnly> Anchor, int DaysBefore, int DaysAfter, string[] Themes);

    private static readonly Holiday[] _holidays =
    [
        new(y => new DateOnly(y, 1, 1), 2, 1, ["newyear"]),                               // Dec 30 – Jan 2
        new(y => new DateOnly(y, 2, 14), 2, 0, ["valentine"]),                            // Feb 12 – 14
        new(y => new DateOnly(y, 3, 17), 2, 0, ["shamrock"]),                             // Mar 15 – 17
        new(EasterSunday, 2, 1, ["easter"]),                                              // Good Friday – Easter Monday
        new(y => new DateOnly(y, 7, 4), 3, 1, ["independence"]),                          // Jul 1 – 5
        new(y => new DateOnly(y, 8, 23), 0, 0, ["birthday"]),                             // Aug 23
        new(y => new DateOnly(y, 10, 31), 7, 0, ["pumpkin", "skeleton"]),                 // Oct 24 – 31
        new(y => NthWeekday(y, 11, DayOfWeek.Thursday, 4), 1, 3, ["thanksgiving"]),       // Wed – Sun of Thanksgiving
        new(y => new DateOnly(y, 12, 25), 7, 1, ["christmas"]),                           // Dec 18 – 26
    ];

    /// <summary>Returns all holiday themes active on <paramref name="date"/>, or an empty list.</summary>
    public static IReadOnlyList<string> For(DateOnly date)
    {
        var themes = new List<string>();

        foreach (var holiday in _holidays)
        {
            // Check neighbouring years too so windows that wrap around Jan 1 still match.
            for (var year = date.Year - 1; year <= date.Year + 1; year++)
            {
                var anchor = holiday.Anchor(year);
                if (date < anchor.AddDays(-holiday.DaysBefore) || date > anchor.AddDays(holiday.DaysAfter))
                    continue;

                themes.AddRange(holiday.Themes);
                break;
            }
        }

        return themes;
    }

    private static DateOnly NthWeekday(int year, int month, DayOfWeek day, int n)
    {
        var first = new DateOnly(year, month, 1);
        var offset = ((int)day - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(offset + ((n - 1) * 7));
    }

    // Anonymous Gregorian algorithm (Meeus/Jones/Butcher).
    private static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = ((19 * a) + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        var m = (a + (11 * h) + (22 * l)) / 451;
        var month = (h + l - (7 * m) + 114) / 31;
        var day = ((h + l - (7 * m) + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }
}
