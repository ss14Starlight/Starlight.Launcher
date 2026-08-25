namespace Starlight.Launcher.WebUI.Models.LocalServer;

public enum ServerCVarType
{
    String,
    Int,
    Float,
    Bool
}

public sealed record ServerCVarOption(string Value, string Label);

public sealed record ServerCVarDefinition(
    string Group,
    string Name,
    ServerCVarType Type,
    string DefaultValue,
    IReadOnlyList<ServerCVarOption>? Options = null
)
{
    public string Key => $"{Group}.{Name}";
}

public sealed record ServerCVarValue(string Group, string Name, ServerCVarType Type, string Value)
{
    public string Key => $"{Group}.{Name}";
}

public static class ServerCVarCatalog
{
    public static readonly IReadOnlyList<ServerCVarDefinition> KnownCVars =
    [
        new ("log", "enabled", ServerCVarType.Bool, "false"),
        new ("log", "path", ServerCVarType.String, "logs"),
        new ("log", "format", ServerCVarType.String, "log_%(date)s-T%(time)s.txt"),
        new ("log", "level", ServerCVarType.Int, "1",
            Options:
            [
                new ("0", "Verbose"),
                new ("1", "Debug"),
                new ("2", "Info"),
                new ("3", "Warning"),
                new ("4", "Error"),
                new ("5", "Fatal"),
            ]),
        new ("log", "runtimelog", ServerCVarType.Bool, "true"),
        new ("net", "tickrate", ServerCVarType.Int, "30"),
        new ("net", "port", ServerCVarType.Int, "1212"),
        new ("net", "bindto", ServerCVarType.String, "0.0.0.0,::"),
        new ("net", "max_connections", ServerCVarType.Int, "256"),
        new ("net", "upnp", ServerCVarType.Bool, "false"),
        new ("status", "enabled", ServerCVarType.Bool, "true"),
        new ("status", "bind", ServerCVarType.String, ""),
        new ("status", "connectaddress", ServerCVarType.String, ""),
        new ("game", "hostname", ServerCVarType.String, "MyServer"),
        new ("console", "loginlocal", ServerCVarType.Bool, "true"),
        new ("hub", "advertise", ServerCVarType.Bool, "false"),
        new ("hub", "tags", ServerCVarType.String, ""),
        new ("hub", "server_url", ServerCVarType.String, ""),
        new ("hub", "hub_urls", ServerCVarType.String, "https://hub.spacestation14.com/"),
        new("auth", "mode", ServerCVarType.Int, "2",
            Options:
            [
                new("0", "Disabled"),
                new("1", "Optional"),
                new("2", "Required")
            ]),
        new ("auth", "allowlocal", ServerCVarType.Bool, "true")
    ];
}
