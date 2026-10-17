using System.Globalization;
using Microsoft.AspNetCore.Components;
using Starlight.Launcher.WebUI.Localization;
using Starlight.Launcher.WebUI.Models.Journal;

namespace Starlight.Launcher.WebUI.Components.Atoms.Journal;

public partial class ActivityHeatmap : LocalizedComponentBase
{
    [Parameter] public ServerActivity? Activity { get; set; }

    private static readonly DayOfWeek[] _days =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    ];

    private double _max;
    private (DayOfWeek Day, int Hour, double Players)? _peak;

    protected override void OnParametersSet()
    {
        _max = 0;
        _peak = null;
        if (Activity is null)
            return;

        foreach (var day in _days)
        {
            for (var hour = 0; hour < 24; hour++)
            {
                if (Activity.Average(day, hour) is { } avg && avg > _max)
                {
                    _max = avg;
                    _peak = (day, hour, avg);
                }
            }
        }
    }

    private string DayName(DayOfWeek day) => L[$"journal-day-{day.ToString().ToLowerInvariant()[..3]}"];

    private string? CellStyle(double? avg)
    {
        if (avg is not { } value || _max <= 0)
            return null;

        var strength = 0.12 + (0.88 * (value / _max));
        return $"opacity: {strength.ToString("0.###", CultureInfo.InvariantCulture)}";
    }

    private string CellTitle(DayOfWeek day, int hour, double? avg)
        => avg is { } value
            ? L.GetString("activity-heatmap-cell", ("day", DayName(day)), ("hour", hour.ToString("00")), ("players", Math.Round(value)))
            : L.GetString("activity-heatmap-cell-empty", ("day", DayName(day)), ("hour", hour.ToString("00")));
}
