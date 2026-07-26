using Robust.Launcher.Api.Api;

namespace Starlight.Launcher.WebUI.Models.HubServerFetcher;

public sealed record HubServerListEntry(string Address, string HubAddress, ServerApi.ServerStatus StatusData);
