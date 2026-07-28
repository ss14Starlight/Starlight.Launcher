using Microsoft.AspNetCore.Components;

namespace Starlight.Launcher.WebUI.Localization;

/// <summary>
/// Same as <seealso cref="LocalizedComponentBase"/> just for layouts.
/// </summary>
public abstract class LocalizedLayoutBase : LayoutComponentBase, IDisposable
{
    [Inject] protected ILocalizationManager L { get; set; } = default!;

    protected override void OnInitialized() => L.Changed += OnLocalizationChanged;

    private void OnLocalizationChanged() => InvokeAsync(StateHasChanged);

    public virtual void Dispose() => L.Changed -= OnLocalizationChanged;
}
