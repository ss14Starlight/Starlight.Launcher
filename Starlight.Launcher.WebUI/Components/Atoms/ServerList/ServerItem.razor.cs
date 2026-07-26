using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using MudBlazor;
using Robust.Launcher.Api.Models.ServerStatus;
using Starlight.Launcher.Components.Atoms.Dialogs;
using Starlight.Launcher.Components.Pages;
using Starlight.Launcher.Services;
using Starlight.Launcher.Services.Auth;
using Starlight.Launcher.Services.Localization;
using Starlight.Launcher.Services.ServerStatus;

namespace Starlight.Launcher.Components.Atoms.ServerList;

public partial class ServerItem : ComponentBase, IDisposable
{
    [Inject] private LocalizationManager _localization { get; set; } = default!;
    [Inject] private ServerInfoLoader _infoLoader { get; set; } = default!;
    [Inject] private IDialogService _dialogService { get; set; } = default!;
    [Inject] private LoginManager _loginManager { get; set; } = default!;
    [Inject] private UiTicker _ticker { get; set; } = default!;
    [Parameter, EditorRequired] public ServerStatusData Data { get; set; } = default!;
    [Parameter] public EventCallback<ServerStatusData> OnInfoNeeded { get; set; }
    [Parameter] public EventCallback<ServerStatusData> OnFavorites { get; set; }
    [Parameter] public bool IsInFavorites { get; set; } = false;
    [Inject] private ILogger<ServerItem> _logger { get; set; } = default!;

    private bool _expanded = false;

    private List<string>? _displayTags;

    protected override void OnParametersSet()
        => _displayTags = Data.Tags?
            .Select(ParseTag)
            .Where(t => !string.IsNullOrEmpty(t))
            .Take(3)
            .ToList();

    private static string ParseTag(string tag)
    {
        if (tag.StartsWith("region:", StringComparison.OrdinalIgnoreCase))
            return Servers.ParseRegionTag(tag);
        if (tag.StartsWith("lang:", StringComparison.OrdinalIgnoreCase))
            return Servers.ParseLangTag(tag);
        if (tag.StartsWith("rp:", StringComparison.OrdinalIgnoreCase))
            return Servers.ParseRPTag(tag);
        return "";
    }

    protected override void OnInitialized()
    {
        Data.Changed += OnDataChanged;
        _ticker.Tick += OnTick;
    }

    private async void OnTick()
    {
        if (Data.RoundStartTime is null)
            return;

        try { await InvokeAsync(StateHasChanged); }
        catch (ObjectDisposedException) { }
    }

    protected override void OnAfterRender(bool firstRender)
    {
        base.OnAfterRender(firstRender);
        if (firstRender)
            _infoLoader.Request(Data);
    }

    private async void OnDataChanged()
    {
        try { await InvokeAsync(StateHasChanged); }
        catch (ObjectDisposedException) { }
    }

    private async Task HandleClick()
    {
        if (string.IsNullOrEmpty(Data.Description) && Data.StatusInfo == ServerStatusInfoCode.Fetched)
            _infoLoader.Request(Data);

        _expanded = !_expanded;
        await Task.CompletedTask;
    }

    private async Task HandleFavorites() => await OnFavorites.InvokeAsync(Data);

    private async Task OnInfoClick(string Url)
    {
        try
        {
            await Browser.OpenAsync(Url, BrowserLaunchMode.SystemPreferred);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to open URL {Url}");
        }
    }

    private async Task Play()
    {
        if (_loginManager.ActiveAccount == null)
        {
            var noaccountParams = new DialogParameters<NoAccountDialog>
            {
                { x => x.Address, Data.Address },
                { x => x.Title, Data.Name }
            };

            var noaccountOptions = new DialogOptions
            {
                BackdropClick = false,
                CloseOnEscapeKey = false,
                CloseButton = false,
                MaxWidth = MaxWidth.ExtraSmall,
                FullWidth = true
            };

            await _dialogService.ShowAsync<NoAccountDialog>(_localization["no-account-dialog-title"], noaccountParams, noaccountOptions);
        }
        else
        {
            var parameters = new DialogParameters<ConnectingDialog>
            {
                { x => x.Address, Data.Address },
                { x => x.Title, Data.Name }
            };

            var options = new DialogOptions
            {
                BackdropClick = false,
                CloseOnEscapeKey = false,
                CloseButton = false,
                MaxWidth = MaxWidth.ExtraSmall,
                FullWidth = true
            };

            await _dialogService.ShowAsync<ConnectingDialog>("Connecting", parameters, options);
        }
    }

    private int? _pingMs => Data.Ping is { } p ? (int)Math.Round(p.TotalMilliseconds) : null;

    private string _pingClass => _pingMs switch
    {
        null => "server-card__ping--unknown",
        <= 100 => "server-card__ping--good",
        <= 250 => "server-card__ping--ok",
        _ => "server-card__ping--bad",
    };

    private string? _roundTime
    {
        get
        {
            if (Data.RoundStartTime is not { } startRaw)
                return null;

            var start = startRaw.Kind == DateTimeKind.Utc ? startRaw : startRaw.ToUniversalTime();
            var elapsed = DateTime.UtcNow - start;
            if (elapsed < TimeSpan.Zero)
                elapsed = TimeSpan.Zero;

            return elapsed.TotalHours >= 1
                ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}"
                : $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
        }
    }

    private string? _roundStatusText => Data.RoundStatus switch
    {
        GameRoundStatus.InLobby => _localization["servers-list-item-round-lobby"],
        GameRoundStatus.InRound => _localization["servers-list-item-round-in-round"],
        _ => null,
    };

    private string? ParseIcon(string? icon)
    {
        if (icon == null)
            return null;

        if (icon == "discord")
            return Icons.Custom.Brands.Discord;

        if (icon == "telegram")
            return Icons.Custom.Brands.Telegram;

        if (icon == "github")
            return Icons.Custom.Brands.GitHub;

        if (icon == "web")
            return Icons.Material.Outlined.Web;

        if (icon == "forum")
            return Icons.Material.Outlined.Forum;

        if (icon == "wiki")
            return Icons.Material.Outlined.Book;

        return icon;
    }

    public void Dispose()
    {
        _ticker.Tick -= OnTick;
        Data.Changed -= OnDataChanged;
    }
}
