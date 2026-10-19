namespace Starlight.Launcher.WebUI.Models.Data;

public record FavoriteServer(string? Name, string Address, string HubAddress)
{
    public string? LastUsedName { get; set; }
    public string Address { get; set; } = Address;
    public string HubAddress { get; set; } = HubAddress;
}
