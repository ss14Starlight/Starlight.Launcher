using Microsoft.AspNetCore.Components;

namespace Starlight.Launcher.Components.Atoms.Settings;

public partial class BoolOption : ComponentBase
{
    [Parameter] public bool Value { get; set; } = true;
    [Parameter] public EventCallback<bool> ValueChanged { get; set; }
    [Parameter] public string Title { get; set; } = default!;
    [Parameter] public string Description { get; set; } = default!;
    [Parameter] public string Icon { get; set; } = default!;
    /// <summary>
    /// Means that this component will control value change by itself.
    /// </summary>
    [Parameter] public bool SelfValueControl { get; set; } = true;
    [Parameter] public Action<bool>? SelfValueControlAction { get; set; }
    [Parameter] public Func<Task<bool>>? SelfValueControlInitialization { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        if (SelfValueControlInitialization is null)
            return;
        Value = await SelfValueControlInitialization.Invoke();
    }

    private async Task OnValueChanged(bool value)
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