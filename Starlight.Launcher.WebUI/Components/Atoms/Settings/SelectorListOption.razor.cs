using Microsoft.AspNetCore.Components;

namespace Starlight.Launcher.Components.Atoms.Settings;

public partial class SelectorListOption : ComponentBase
{
    [Parameter, EditorRequired] public List<string> Values { get; set; }
    [Parameter] public string? SelectedValue { get; set; } = null;
    [Parameter] public EventCallback<string?> ValueChanged { get; set; }
    [Parameter] public string Title { get; set; } = default!;
    [Parameter] public string Description { get; set; } = default!;
    [Parameter] public string Icon { get; set; } = default!;
    [Parameter] public bool EmptyOption { get; set; } = true;
    [Parameter] public string EmptyOptionName { get; set; } = "Nothing Selected";
    /// <summary>
    /// Means that this component will control value change by itself.
    /// </summary>
    [Parameter] public bool SelfValueControl { get; set; } = true;
    [Parameter] public Action<string?>? SelfValueControlAction { get; set; }
    [Parameter] public Func<Task<string?>>? SelfValueControlInitialization { get; set; }
    [Parameter] public Func<string, string>? DisplayTextSelector { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        if (SelfValueControlInitialization is null)
            return;
        SelectedValue = await SelfValueControlInitialization.Invoke();
    }

    private async Task OnValueChanged(string? value)
    {
        if (!SelfValueControl)
            await ValueChanged.InvokeAsync(value);
        else
        {
            SelectedValue = value;
            SelfValueControlAction?.Invoke(value);
        }
    }
}