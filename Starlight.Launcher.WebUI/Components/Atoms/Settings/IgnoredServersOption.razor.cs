using Microsoft.AspNetCore.Components;
using Starlight.Launcher.WebUI.Localization;
using Starlight.Launcher.WebUI.Models.Settings;

namespace Starlight.Launcher.WebUI.Components.Atoms.Settings;

public partial class IgnoredServersOption : LocalizedComponentBase
{
    [Parameter] public List<IgnoredServer> Value { get; set; } = [];
    [Parameter] public EventCallback<List<IgnoredServer>> ValueChanged { get; set; }

    [Parameter] public string Title { get; set; } = default!;
    [Parameter] public string Description { get; set; } = default!;
    [Parameter] public string Icon { get; set; } = default!;
    [Parameter] public string EmptyValuesString { get; set; } = "There's no ignored servers.";

    /// <summary>
    /// Means that this component will control value change by itself.
    /// </summary>
    [Parameter] public bool SelfValueControl { get; set; } = true;
    [Parameter] public Action<List<IgnoredServer>>? SelfValueControlAction { get; set; }
    [Parameter] public Func<Task<List<IgnoredServer>>>? SelfValueControlInitialization { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        if (SelfValueControlInitialization is null)
            return;
        Value = await SelfValueControlInitialization.Invoke();
    }

    private async Task RemoveAt(int index)
    {
        if (index < 0 || index >= Value.Count)
            return;

        Value.RemoveAt(index);
        await NotifyChanged();
    }

    private async Task NotifyChanged()
    {
        if (!SelfValueControl)
            await ValueChanged.InvokeAsync(Value);
        else
            SelfValueControlAction?.Invoke(Value);
    }
}
