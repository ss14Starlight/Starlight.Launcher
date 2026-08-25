using Microsoft.AspNetCore.Components;
using MudBlazor;
using Starlight.Launcher.WebUI.Components.Atoms.Dialogs;
using Starlight.Launcher.WebUI.Localization;
using Starlight.Launcher.WebUI.Models.Settings;

namespace Starlight.Launcher.WebUI.Components.Atoms.Settings;

public partial class LocalServerSourceListOption : LocalizedComponentBase
{
    [Inject] private IDialogService _dialogService { get; set; } = default!;

    [Parameter] public List<LocalServerSourceConfig> Value { get; set; } = [];
    [Parameter] public EventCallback<List<LocalServerSourceConfig>> ValueChanged { get; set; }

    [Parameter] public string Title { get; set; } = default!;
    [Parameter] public string Description { get; set; } = default!;
    [Parameter] public string Icon { get; set; } = default!;
    [Parameter] public string EmptyValuesString { get; set; } = "No sources configured. Click + to add one.";
    [Parameter] public string AddButtonTooltip { get; set; } = "Add source";

    [Parameter] public bool SelfValueControl { get; set; } = true;
    [Parameter] public Action<List<LocalServerSourceConfig>>? SelfValueControlAction { get; set; }
    [Parameter] public Func<Task<List<LocalServerSourceConfig>?>>? SelfValueControlInitialization { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        if (SelfValueControlInitialization is not null)
            Value = (await SelfValueControlInitialization.Invoke()) ?? [];
    }

    private async Task<bool> ConfirmNewSourceAsync()
    {
        var options = new DialogOptions
        {
            BackdropClick = false,
            CloseOnEscapeKey = true,
            MaxWidth = MaxWidth.ExtraSmall,
            FullWidth = true
        };

        var dialog = await _dialogService.ShowAsync<ImportantLocalServerSourceWarningDialog>(
            L["local-server-source-warning-title"], options);

        var result = await dialog.Result;
        return result is not null && !result.Canceled;
    }

    private async Task AddSource()
    {
        if (!await ConfirmNewSourceAsync())
            return;

        Value.Add(new LocalServerSourceConfig { Enabled = true });
        await NotifyChanged();
    }

    private async Task RemoveAt(int index)
    {
        if (index < 0 || index >= Value.Count)
            return;

        Value.RemoveAt(index);
        await NotifyChanged();
    }

    private async Task ToggleEnabled(int index, bool enabled)
    {
        if (index < 0 || index >= Value.Count)
            return;

        Value[index] = Value[index] with { Enabled = enabled };
        await NotifyChanged();
    }

    private async Task UpdateSource(int index, Func<LocalServerSourceConfig, LocalServerSourceConfig> update)
    {
        if (index < 0 || index >= Value.Count)
            return;

        Value[index] = update(Value[index]);
        await NotifyChanged();
    }

    private async Task NotifyChanged()
    {
        if (!SelfValueControl)
            await ValueChanged.InvokeAsync(Value);
        else
            SelfValueControlAction?.Invoke(Value);
    }

    private static string? ValidateUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "URL is required";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return "Invalid URL";
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            return "Invalid URL scheme";
        return null;
    }
}
