using Microsoft.AspNetCore.Components;

namespace Starlight.Launcher.Components.Atoms.Settings;

public partial class FieldOption : ComponentBase 
{
    [Parameter] public string Value { get; set; } = "";
    [Parameter] public EventCallback<string> ValueChanged { get; set; }
    [Parameter] public string Title { get; set; } = default!;
    [Parameter] public string Description { get; set; } = default!;
    [Parameter] public string Icon { get; set; } = default!;
    [Parameter] public string Label { get; set; } = "";
    [Parameter] public string Helper { get; set; } = "";
    [Parameter] public bool HelperOnFocus { get; set; } = false;
    /// <summary>
    /// Means that this component will control value change by itself.
    /// </summary>
    [Parameter] public bool SelfValueControl { get; set; } = true;
    [Parameter] public Action<string>? SelfValueControlAction { get; set; }
    [Parameter] public Func<Task<string>>? SelfValueControlInitialization { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        if (SelfValueControlInitialization is null)
            return;
        Value = await SelfValueControlInitialization.Invoke();
    }

    private async Task OnValueChanged(string value)
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
