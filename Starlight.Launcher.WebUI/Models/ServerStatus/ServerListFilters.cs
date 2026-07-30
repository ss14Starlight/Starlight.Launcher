namespace Starlight.Launcher.WebUI.Models.ServerStatus;

public sealed class ServerListFilters
{
    public string SearchQuery { get; set; } = "";
    public HashSet<string> SelectedRP { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SelectedLang { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SelectedRegion { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool TagsExpanded { get; set; } = true;
    public bool HideAdult { get; set; }
    public bool OnlyAdult { get; set; }
    public bool HideEmpty { get; set; }
    public bool HideFull { get; set; }
    public ServerSortMode SortBy { get; set; } = ServerSortMode.Players;

    public event Action? Changed;
    public void NotifyChanged() => Changed?.Invoke();
}

public enum ServerSortMode
{
    Players,
    Name,
    Ping,
}
