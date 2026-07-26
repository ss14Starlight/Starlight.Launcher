using Microsoft.AspNetCore.Components;

namespace Starlight.Launcher.Components.Atoms.Settings;

public partial class NumericFieldOption : ComponentBase
{
    [Parameter] public int Value { get; set; } = 0;
    [Parameter] public EventCallback<int> ValueChanged { get; set; }
    [Parameter] public string Title { get; set; } = default!;
    [Parameter] public string Description { get; set; } = default!;
    [Parameter] public string Icon { get; set; } = default!;
    [Parameter] public string Label { get; set; } = "";
    /// <summary>
    /// Means that this component will control value change by itself.
    /// </summary>
    [Parameter] public bool SelfValueControl { get; set; } = true;
    [Parameter] public Action<int>? SelfValueControlAction { get; set; }
    [Parameter] public Func<Task<int>>? SelfValueControlInitialization { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        if (SelfValueControlInitialization is null)
            return;
        Value = await SelfValueControlInitialization.Invoke();
    }

    private async Task OnValueChanged(int value)
    {
        if (!SelfValueControl)
            await ValueChanged.InvokeAsync(value);
        else
        {
            Value = value;
            SelfValueControlAction?.Invoke(value);
        }
    }
}
