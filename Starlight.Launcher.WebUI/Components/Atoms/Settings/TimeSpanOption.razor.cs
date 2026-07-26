using Microsoft.AspNetCore.Components;

namespace Starlight.Launcher.Components.Atoms.Settings;

public partial class TimeSpanOption : ComponentBase
{
    [Parameter] public string? Icon { get; set; }
    [Parameter] public string? Title { get; set; }
    [Parameter] public string? Description { get; set; }
    [Parameter] public string DaysLabel { get; set; } = "Days";
    [Parameter] public string HoursLabel { get; set; } = "Hours";
    [Parameter] public string MinutesLabel { get; set; } = "Min";
    [Parameter] public string SecondsLabel { get; set; } = "Sec";
    [Parameter] public TimeSpan Value { get; set; }
    [Parameter] public EventCallback<TimeSpan> ValueChanged { get; set; }
    /// <summary>
    /// Means that this component will control value change by itself.
    /// </summary>
    [Parameter] public bool SelfValueControl { get; set; } = true;
    [Parameter] public Action<TimeSpan>? SelfValueControlAction { get; set; }
    [Parameter] public Func<Task<TimeSpan>>? SelfValueControlInitialization { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        if (SelfValueControlInitialization is null)
            return;
        Value = await SelfValueControlInitialization.Invoke();
    }

    private async Task OnValueChanged(TimeSpan value)
    {
        if (!SelfValueControl)
            await ValueChanged.InvokeAsync(value);
        else
        {
            Value = value;
            SelfValueControlAction?.Invoke(value);
        }
    }

    private int Days => Value.Days;
    private int Hours => Value.Hours;
    private int Minutes => Value.Minutes;
    private int Seconds => Value.Seconds;

    private Task SetDays(int v) => OnValueChanged(new TimeSpan(Math.Max(0, v), Hours, Minutes, Seconds));
    private Task SetHours(int v) => OnValueChanged(new TimeSpan(Days, v, Minutes, Seconds));
    private Task SetMinutes(int v) => OnValueChanged(new TimeSpan(Days, Hours, v, Seconds));
    private Task SetSeconds(int v) => OnValueChanged(new TimeSpan(Days, Hours, Minutes, v));
}
