using Microsoft.AspNetCore.Components;
using Starlight.Launcher.Models.ServerStatus;
using Starlight.Launcher.Services.Localization;
using Starlight.Launcher.Services.ServerStatus;

namespace Starlight.Launcher.Components.Atoms.ServerList;

public partial class SearchBar : ComponentBase
{
    [Inject] private LocalizationManager _localization { get; set; } = default!;
    [Parameter, EditorRequired] public ServerListFilters Filters { get; set; } = null!;
    [Parameter] public int TotalCount { get; set; }
    [Parameter] public int FilteredCount { get; set; }
    [Parameter] public RefreshListStatus Status { get; set; }
    [Parameter] public EventCallback OnRefresh { get; set; }
    [Parameter] public bool TagsExpandButton { get; set; } = true;

    private bool _hasActiveTagFilters =>
        Filters.SelectedRP.Count > 0 ||
        Filters.SelectedLang.Count > 0 ||
        Filters.SelectedRegion.Count > 0;

    private void ToggleTagsExpanded()
    {
        Filters.TagsExpanded = !Filters.TagsExpanded;
        Filters.NotifyChanged();
    }

    private bool _isRefreshing => Status == RefreshListStatus.UpdatingMaster;

    private void OnSearchChanged(string value)
    {
        Filters.SearchQuery = value ?? "";
        Filters.NotifyChanged();
    }

    private void OnSortChanged(ServerSortMode mode)
    {
        Filters.SortBy = mode;
        Filters.NotifyChanged();
    }

    private void ToggleHideEmpty()
    {
        Filters.HideEmpty = !Filters.HideEmpty;
        Filters.NotifyChanged();
    }

    private void ToggleHideFull()
    {
        Filters.HideFull = !Filters.HideFull;
        Filters.NotifyChanged();
    }

    private void ToggleHideAdult()
    {
        Filters.HideAdult = !Filters.HideAdult;
        if (Filters.HideAdult)
            Filters.OnlyAdult = false;
        Filters.NotifyChanged();
    }

    private void ToggleOnlyAdult()
    {
        Filters.OnlyAdult = !Filters.OnlyAdult;
        if (Filters.OnlyAdult)
            Filters.HideAdult = false;
        Filters.NotifyChanged();
    }

    private (string Text, MudBlazor.Color Color) GetStatusDisplay() => Status switch
    {
        RefreshListStatus.NotUpdated => ("Idle", MudBlazor.Color.Default),
        RefreshListStatus.UpdatingMaster => ("Updating…", MudBlazor.Color.Info),
        RefreshListStatus.Updated => ("Up to date", MudBlazor.Color.Success),
        RefreshListStatus.PartialError => ("Some hubs failed", MudBlazor.Color.Warning),
        RefreshListStatus.Error => ("Error", MudBlazor.Color.Error),
        _ => ("", MudBlazor.Color.Default),
    };
}
