using Microsoft.AspNetCore.Components;
using MudBlazor;
using Starlight.Launcher.WebUI.Models.Diagnostics;
using Starlight.Launcher.WebUI.Models.Settings;
using Starlight.Launcher.WebUI.Services;

namespace Starlight.Launcher.WebUI.Components.Pages;

/// <summary>
/// Launch options and debugging tools, shown in the navigation while developer mode is on.
/// </summary>
public partial class Development : SettingsPageBase
{
    [Inject] private IFileDialogService _fileDialog { get; set; } = default!;
    [Inject] private ISnackbar _snackbar { get; set; } = default!;

    // Robust LogLevel names, passed as --loglevel root=<level>.
    private static readonly List<string> _logLevels = ["Verbose", "Debug", "Info", "Warning", "Error"];

    private bool _loaded;
    private bool _developerMode;
    private RenderDocInfo? _renderDoc;

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        var settings = await Bridge.GetSettingsAsync();
        _developerMode = settings.DeveloperMode;
        _renderDoc = Bridge.GetRenderDocInfo();
        _loaded = true;
    }

    private async Task OpenFolder(Func<AppSettings, string> dir)
    {
        var path = dir(await Bridge.GetSettingsAsync());
        _ = Directory.CreateDirectory(path);
        Bridge.OpenPath(path);
    }

    private async Task OpenClientLogs()
    {
        var s = await Bridge.GetSettingsAsync();
        var dir = string.IsNullOrWhiteSpace(s.ClientLogDirectory) ? s.DirClientLogsDefault : s.ClientLogDirectory;
        _ = Directory.CreateDirectory(dir);
        Bridge.OpenPath(dir);
    }

    private async Task OnOpenRenderDoc()
    {
        if (!await Bridge.OpenRenderDocAsync())
            _ = _snackbar.Add(L["development-renderdoc-open-failed"], Severity.Error);

        _renderDoc = Bridge.GetRenderDocInfo();
    }
}
