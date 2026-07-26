using Microsoft.AspNetCore.Components;
using Starlight.Launcher.Models.ServerStatus;
using Starlight.Launcher.Services.Localization;

namespace Starlight.Launcher.Components.Atoms.ServerList;

public partial class TagsBar : ComponentBase
{
    [Inject] private LocalizationManager _localization { get; set; } = default!;
    [Parameter, EditorRequired] public ServerListFilters Filters { get; set; } = null!;
    [Parameter] public IReadOnlyList<string> AvailableRPTags { get; set; } = Array.Empty<string>();
    [Parameter] public IReadOnlyList<string> AvailableLangTags { get; set; } = Array.Empty<string>();
    [Parameter] public IReadOnlyList<string> AvailableRegionTags { get; set; } = Array.Empty<string>();
    [Parameter] public bool IsBottom { get; set; }
    [Parameter] public bool HasSearchAbove { get; set; }
    [Parameter] public bool HasSearchBelow { get; set; }
    [Parameter] public bool AlwaysExpanded { get; set; } = false;

    private void OnRPTagsChanged(IReadOnlyCollection<string?>? strings)
    {
        Filters.SelectedRP = new (
            (strings ?? Enumerable.Empty<string?>()).Where(s => s is not null)!);
        Filters.NotifyChanged();
    }

    private void OnLangTagsChanged(IReadOnlyCollection<string?>? strings)
    {
        Filters.SelectedLang = new (
            (strings ?? Enumerable.Empty<string?>()).Where(s => s is not null)!);
        Filters.NotifyChanged();
    }

    private void OnRegionTagsChanged(IReadOnlyCollection<string?>? strings)
    {
        Filters.SelectedRegion = new (
            (strings ?? Enumerable.Empty<string?>()).Where(s => s is not null)!);
        Filters.NotifyChanged();
    }

    private bool _hasActiveTagFilters =>
        Filters.SelectedRP.Count > 0 ||
        Filters.SelectedLang.Count > 0 ||
        Filters.SelectedRegion.Count > 0;

    private void ClearTagFilters()
    {
        Filters.SelectedRP = new HashSet<string>();
        Filters.SelectedLang = new HashSet<string>();
        Filters.SelectedRegion = new HashSet<string>();
        Filters.NotifyChanged();
        StateHasChanged();
    }
}
