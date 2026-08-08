using Microsoft.AspNetCore.Components;
using MudBlazor;
using NSec.Cryptography;
using System.Text;
using Starlight.Launcher.WebUI.Components.Atoms.Dialogs;
using Starlight.Launcher.WebUI.Localization;
using Starlight.Launcher.WebUI.Models.Settings;

namespace Starlight.Launcher.WebUI.Components.Atoms.Settings;

public partial class RobustCdnListOption : LocalizedComponentBase
{
    [Inject] private IDialogService _dialogService { get; set; } = default!;

    [Parameter] public List<RobustCdnConfig> Value { get; set; } = [];
    [Parameter] public EventCallback<List<RobustCdnConfig>> ValueChanged { get; set; }

    [Parameter] public string Title { get; set; } = default!;
    [Parameter] public string Description { get; set; } = default!;
    [Parameter] public string Icon { get; set; } = default!;
    [Parameter] public string EmptyValuesString { get; set; } = "No CDNs configured. Click + to add one.";
    [Parameter] public string AddButtonTooltip { get; set; } = "Add CDN";

    [Parameter] public bool AllowKeyEditing { get; set; } = false;

    [Parameter] public bool SelfValueControl { get; set; } = true;
    [Parameter] public Action<List<RobustCdnConfig>>? SelfValueControlAction { get; set; }
    [Parameter] public Func<Task<List<RobustCdnConfig>?>>? SelfValueControlInitialization { get; set; }

    private bool _importantWarningAccepted;

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        if (SelfValueControlInitialization is not null)
            Value = (await SelfValueControlInitialization.Invoke()) ?? AppSettings.DefaultRobustCdns;
    }

    private async Task<bool> ConfirmImportantAsync(RobustCdnConfig cdn)
    {
        if (!cdn.Important || _importantWarningAccepted)
            return true;

        var parameters = new DialogParameters<ImportantCdnWarningDialog>
        {
            { x => x.CdnName, string.IsNullOrWhiteSpace(cdn.Name) ? cdn.Urls.FirstOrDefault() ?? "" : cdn.Name }
        };

        var options = new DialogOptions
        {
            BackdropClick = false,
            CloseOnEscapeKey = true,
            MaxWidth = MaxWidth.ExtraSmall,
            FullWidth = true
        };

        var dialog = await _dialogService.ShowAsync<ImportantCdnWarningDialog>(
            L["settings-cdns-option-important-warning-title"], parameters, options);

        var result = await dialog.Result;
        if (result is null || result.Canceled)
            return false;

        _importantWarningAccepted = true;
        return true;
    }

    private async Task AddCdn()
    {
        Value.Add(new RobustCdnConfig { Urls = [""], Enabled = true });
        await NotifyChanged();
    }

    private async Task RemoveAt(int index)
    {
        if (index < 0 || index >= Value.Count)
            return;
        if (!await ConfirmImportantAsync(Value[index]))
            return;

        Value.RemoveAt(index);
        await NotifyChanged();
    }

    private async Task ToggleEnabled(int index, bool enabled)
    {
        if (index < 0 || index >= Value.Count)
            return;

        if (!enabled && !await ConfirmImportantAsync(Value[index]))
        {
            StateHasChanged();
            return;
        }

        Value[index] = Value[index] with { Enabled = enabled };
        await NotifyChanged();
    }

    private async Task MoveUp(int index) => await Move(index, -1);
    private async Task MoveDown(int index) => await Move(index, +1);

    private async Task Move(int index, int delta)
    {
        var target = index + delta;
        if (index < 0 || index >= Value.Count || target < 0 || target >= Value.Count)
            return;

        if (!await ConfirmImportantAsync(Value[index]) || !await ConfirmImportantAsync(Value[target]))
            return;

        (Value[target], Value[index]) = (Value[index], Value[target]);
        await NotifyChanged();
    }

    private async Task UpdateCdn(int index, Func<RobustCdnConfig, RobustCdnConfig> update)
    {
        if (index < 0 || index >= Value.Count)
            return;

        Value[index] = update(Value[index]);
        await NotifyChanged();
    }

    private async Task UpdateUrl(int cdnIndex, int urlIndex, string url)
    {
        if (cdnIndex < 0 || cdnIndex >= Value.Count)
            return;

        var urls = new List<string>(Value[cdnIndex].Urls);
        if (urlIndex < 0 || urlIndex >= urls.Count)
            return;

        urls[urlIndex] = url;
        Value[cdnIndex] = Value[cdnIndex] with { Urls = urls };
        await NotifyChanged();
    }

    private async Task AddUrl(int cdnIndex)
        => await UpdateCdn(cdnIndex, c => c with { Urls = [.. c.Urls, ""] });

    private async Task RemoveUrl(int cdnIndex, int urlIndex)
    {
        if (cdnIndex < 0 || cdnIndex >= Value.Count)
            return;

        var urls = new List<string>(Value[cdnIndex].Urls);
        if (urlIndex < 0 || urlIndex >= urls.Count || urls.Count <= 1)
            return;

        urls.RemoveAt(urlIndex);
        Value[cdnIndex] = Value[cdnIndex] with { Urls = urls };
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
        if (uri.Scheme != Uri.UriSchemeHttps)
            return "Only HTTPS allowed";
        return null;
    }

    private static string? ValidateKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Public key is required";

        try
        {
            _ = PublicKey.Import(
                SignatureAlgorithm.Ed25519,
                Encoding.UTF8.GetBytes(value),
                KeyBlobFormat.PkixPublicKeyText);
            return null;
        }
        catch
        {
            return "Not a valid Ed25519 PKIX public key";
        }
    }
}
