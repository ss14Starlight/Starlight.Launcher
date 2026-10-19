using Microsoft.AspNetCore.Components;

namespace Starlight.Launcher.WebUI.Localization;

/// <summary>
/// <para>Base for components that localize text. Use cL[id]c in markup; the component</para>
/// <para>re-renders automatically when localization reloads.</para>
/// </summary>
public abstract class LocalizedComponentBase : ComponentBase, IDisposable
{
    [Inject] protected ILocalizationManager L { get; set; } = default!;

    protected override void OnInitialized() => L.Changed += OnLocalizationChanged;

    private void OnLocalizationChanged() => InvokeAsync(StateHasChanged);

    public virtual void Dispose()
    {
        L.Changed -= OnLocalizationChanged;
        GC.SuppressFinalize(this);
    }
}
