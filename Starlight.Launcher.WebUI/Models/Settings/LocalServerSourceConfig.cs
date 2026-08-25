using Starlight.Launcher.WebUI.Models.LocalServer;

namespace Starlight.Launcher.WebUI.Models.Settings;

public sealed record LocalServerSourceConfig
{
    public string Name { get; init; } = "";

    public string Url { get; init; } = "";

    public bool Enabled { get; init; } = true;

    public List<ServerCVarValue> CVarOverrides { get; init; } = [];
}
