using Microsoft.AspNetCore.Components;

namespace Starlight.Launcher.WebUI.Localization;

/// <summary>
/// Base for components that localize text. Use cL[id]c in markup; the component
///
/// re-renders automatically when localization reloads.
/// </summary>
public abstract class LocalizedComponentBase :  ComponentBase, IDisposable
{
    [Inject] protected ILocalizationManager L { get; set; } = default!;

    protected override void OnInitialized() => L.Changed += OnLocalizationChanged;

    private void OnLocalizationChanged() => InvokeAsync(StateHasChanged);

    public virtual void Dispose() => L.Changed -= OnLocalizationChanged;
}
