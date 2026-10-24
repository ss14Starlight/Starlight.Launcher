using System.Globalization;
using Microsoft.AspNetCore.Components;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Localization;
using Starlight.Launcher.WebUI.Models.Data;

namespace Starlight.Launcher.WebUI.Components.Atoms.Settings;

public partial class StorageUsageOption : LocalizedComponentBase
{
    [Inject] private IBridge _bridge { get; set; } = default!;

    [Parameter] public string Title { get; set; } = default!;
    [Parameter] public string Description { get; set; } = default!;
    [Parameter] public string Icon { get; set; } = default!;

    private StorageUsage? _usage;
    private bool _loading;

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (_loading)
            return;

        _loading = true;
        try
        {
            _usage = await _bridge.GetStorageUsageAsync();
        }
        finally
        {
            _loading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double v = bytes;
        var i = 0;
        while (v >= 1024 && i < units.Length - 1)
        {
            v /= 1024;
            i++;
        }

        return $"{v.ToString("0.#", CultureInfo.CurrentCulture)} {units[i]}";
    }

    private static string BarWidth(long value, long max)
        => $"width: {Math.Max(value > 0 ? 2 : 0, value * 100d / max).ToString("0.#", CultureInfo.InvariantCulture)}%";
}
