using Microsoft.AspNetCore.Components;
using Starlight.Launcher.WebUI.Localization;
using Starlight.Launcher.WebUI.Models.ServerStatus;

namespace Starlight.Launcher.WebUI.Components.Atoms.ServerList;

public partial class TagsBar : LocalizedComponentBase
{
    [Parameter, EditorRequired] public ServerListFilters Filters { get; set; } = null!;
    [Parameter] public IReadOnlyList<string> AvailableRPTags { get; set; } = [];
    [Parameter] public IReadOnlyList<string> AvailableLangTags { get; set; } = [];
    [Parameter] public IReadOnlyList<string> AvailableRegionTags { get; set; } = [];
    [Parameter] public bool IsBottom { get; set; }
    [Parameter] public bool HasSearchAbove { get; set; }
    [Parameter] public bool HasSearchBelow { get; set; }
    [Parameter] public bool AlwaysExpanded { get; set; }

    private void OnRPTagsChanged(IReadOnlyCollection<string?>? strings)
    {
        Filters.SelectedRP = ToTagSet(strings);
        Filters.NotifyChanged();
    }

    private void OnLangTagsChanged(IReadOnlyCollection<string?>? strings)
    {
        Filters.SelectedLang = ToTagSet(strings);
        Filters.NotifyChanged();
    }

    private void OnRegionTagsChanged(IReadOnlyCollection<string?>? strings)
    {
        Filters.SelectedRegion = ToTagSet(strings);
        Filters.NotifyChanged();
    }

    // Same comparer as ServerListFilters' defaults, tags are matched case-insensitively.
    private static HashSet<string> ToTagSet(IReadOnlyCollection<string?>? strings)
        => new((strings ?? []).OfType<string>(), StringComparer.OrdinalIgnoreCase);

    private bool _hasActiveTagFilters
        => Filters.SelectedRP.Count > 0
            || Filters.SelectedLang.Count > 0
            || Filters.SelectedRegion.Count > 0;

    private void ClearTagFilters()
    {
        Filters.SelectedRP = ToTagSet(null);
        Filters.SelectedLang = ToTagSet(null);
        Filters.SelectedRegion = ToTagSet(null);
        Filters.NotifyChanged();
    }
}
