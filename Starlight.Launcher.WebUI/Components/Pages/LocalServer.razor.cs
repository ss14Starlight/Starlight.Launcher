using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MudBlazor;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Components.Atoms.Dialogs;
using Starlight.Launcher.WebUI.Components.Atoms.Settings;
using Starlight.Launcher.WebUI.Localization;
using Starlight.Launcher.WebUI.Models.LocalServer;
using Starlight.Launcher.WebUI.Models.Settings;

namespace Starlight.Launcher.WebUI.Components.Pages;

public partial class LocalServer : LocalizedComponentBase, IDisposable
{
    [Inject] private IBridge _bridge { get; set; } = default!;
    [Inject] private IDialogService _dialog { get; set; } = default!;
    [Inject] private NavigationManager _navigation { get; set; } = default!;
    [Inject] private ISnackbar _snackbar { get; set; } = default!;
    [Inject] private IJSRuntime _jS { get; set; } = default!;

    private List<LocalServerSourceConfig> _sources = [];
    private int _selectedIndex = -1;
    private LocalServerSourceConfig? SelectedSource =>
        _selectedIndex >= 0 && _selectedIndex < _sources.Count ? _sources[_selectedIndex] : null;

    private LocalServerLatestBuildResult? _latestBuild;
    private bool _refreshing;
    private bool _clearing;

    private readonly Dictionary<string, string> _knownCVarValues = new();
    private List<ServerCVarValue> _customCVars = [];

    private LocalServerState _state = new(LocalServerPhase.Idle);
    private readonly List<LocalServerLogLine> _console = [];
    private bool _autoScrollPending;
    private bool _autoScrollEnabled = true;
    private string _commandInput = "";

    private bool _policyChecked;
    private bool _policyAccepted;

    private const string LocalServerConnectAddress = "ss14://localhost";

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();

        var settings = await _bridge.GetSettingsAsync();
        _policyAccepted = settings.LocalServerPolicyAccepted;

        _state = _bridge.GetLocalServerState();
        _console.AddRange(_bridge.GetLocalServerConsoleBuffer());

        _bridge.LocalServerStateChanged += OnStateChanged;
        _bridge.LocalServerOutput += OnOutput;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);

        if (_autoScrollPending)
        {
            _autoScrollPending = false;
            await _jS.InvokeVoidAsync("eval",
                "var el = document.getElementById('local-server-console-log'); if (el) el.scrollTop = el.scrollHeight;");
        }

        if (!firstRender || _policyChecked)
            return;

        _policyChecked = true;
        if (_policyAccepted)
            return;

        await ShowPolicyDialogAsync();
    }

    private async Task ShowPolicyDialogAsync()
    {
        var options = new DialogOptions
        {
            CloseButton = false,
            MaxWidth = MaxWidth.Small,
            FullWidth = true,
            BackdropClick = false
        };

        var parameters = new DialogParameters<AlertDialog>
        {
            { x => x.DescriptionKey, "local-server-policy-alert-description" }
        };

        var dialog = await _dialog.ShowAsync<AlertDialog>(L["local-server-policy-alert-title"], parameters, options);
        if (dialog.Dialog is AlertDialog alert)
        {
            alert.OnSuccess += async () =>
            {
                _policyAccepted = true;
                var settings = await _bridge.GetSettingsAsync();
                await _bridge.WriteSettingsAsync(settings with { LocalServerPolicyAccepted = true });
                await InvokeAsync(StateHasChanged);
            };
            alert.OnCancel += () => _navigation.NavigateTo("/");
        }
    }

    private async Task<List<LocalServerSourceConfig>?> FetchSources()
    {
        var settings = await _bridge.GetSettingsAsync();
        _sources = settings.LocalServerSources;
        if (_selectedIndex < 0 && _sources.Count > 0)
            _selectedIndex = _sources.FindIndex(s => s.Enabled);
        LoadCVarState();
        return _sources;
    }

    private async Task OnSourcesChanged(List<LocalServerSourceConfig> sources)
    {
        _sources = sources;
        if (_selectedIndex >= _sources.Count)
            _selectedIndex = _sources.Count > 0 ? 0 : -1;

        var settings = await _bridge.GetSettingsAsync();
        await _bridge.WriteSettingsAsync(settings with { LocalServerSources = sources });
        LoadCVarState();
        await InvokeAsync(StateHasChanged);
    }

    private Task OnSourceIndexSelected(int index)
    {
        _selectedIndex = index;
        _latestBuild = null;
        LoadCVarState();
        return Task.CompletedTask;
    }

    private static IEnumerable<IGrouping<string, ServerCVarDefinition>> GroupedKnownCVars =>
        ServerCVarCatalog.KnownCVars.GroupBy(d => d.Group);

    private void LoadCVarState()
    {
        _knownCVarValues.Clear();
        _customCVars = [];

        if (SelectedSource is not { } source)
            return;

        var overridesByKey = source.CVarOverrides.ToDictionary(v => v.Key);

        foreach (var def in ServerCVarCatalog.KnownCVars)
            _knownCVarValues[def.Key] = overridesByKey.TryGetValue(def.Key, out var v) ? v.Value : def.DefaultValue;

        var knownKeys = ServerCVarCatalog.KnownCVars.Select(d => d.Key).ToHashSet();
        _customCVars = [.. source.CVarOverrides.Where(v => !knownKeys.Contains(v.Key))];
    }

    private string GetKnownValue(ServerCVarDefinition def) =>
        _knownCVarValues.TryGetValue(def.Key, out var v) ? v : def.DefaultValue;

    private void SetKnownValue(string key, string value) => _knownCVarValues[key] = value;

    private bool GetKnownBool(string key) => _knownCVarValues.TryGetValue(key, out var v) && v == "true";

    private void SetKnownBool(string key, bool value) => _knownCVarValues[key] = value ? "true" : "false";

    private void AddCustomCVar() => _customCVars.Add(new ServerCVarValue("", "", ServerCVarType.String, ""));

    private void RemoveCustomCVar(int index) => _customCVars.RemoveAt(index);

    private void SetCustomCVarGroup(int index, string value) => _customCVars[index] = _customCVars[index] with { Group = value };

    private void SetCustomCVarName(int index, string value) => _customCVars[index] = _customCVars[index] with { Name = value };

    private void SetCustomCVarType(int index, ServerCVarType value) => _customCVars[index] = _customCVars[index] with { Type = value };

    private void SetCustomCVarValue(int index, string value) => _customCVars[index] = _customCVars[index] with { Value = value };

    private void SetCustomCVarBool(int index, bool value) => _customCVars[index] = _customCVars[index] with { Value = value ? "true" : "false" };

    private bool GetCustomCVarBool(int index) => _customCVars[index].Value == "true";

    private async Task SaveServerConfig()
    {
        if (SelectedSource is not { } source)
            return;

        var overrides = new List<ServerCVarValue>();
        foreach (var def in ServerCVarCatalog.KnownCVars)
            overrides.Add(new ServerCVarValue(def.Group, def.Name, def.Type, GetKnownValue(def)));

        foreach (var cvar in _customCVars)
        {
            if (!string.IsNullOrWhiteSpace(cvar.Group) && !string.IsNullOrWhiteSpace(cvar.Name))
                overrides.Add(cvar);
        }

        _sources[_selectedIndex] = source with { CVarOverrides = overrides };

        var settings = await _bridge.GetSettingsAsync();
        await _bridge.WriteSettingsAsync(settings with { LocalServerSources = _sources });

        _ = _snackbar.Add(L["local-server-config-saved"], Severity.Success);
    }

    private async Task RefreshLatestBuild()
    {
        if (SelectedSource is not { } source)
            return;

        _refreshing = true;
        try
        {
            _latestBuild = await _bridge.FetchLocalServerLatestBuildAsync(source.Url);
        }
        catch (Exception e)
        {
            _ = _snackbar.Add(e.Message, Severity.Error);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private async Task StartServer()
    {
        if (SelectedSource is not { } source)
            return;

        try
        {
            await _bridge.StartLocalServerAsync(source.Name, source.Url, source.CVarOverrides);
        }
        catch (Exception e)
        {
            _ = _snackbar.Add(e.Message, Severity.Error);
        }
    }

    private void StopServer() => _bridge.StopLocalServer();

    private bool CanConnect() => _state.Phase == LocalServerPhase.Running;

    private async Task ConnectToServer()
    {
        var options = new DialogOptions
        {
            CloseOnEscapeKey = false,
            BackdropClick = false,
            CloseButton = false
        };

        var parameters = new DialogParameters<ConnectingDialog>
        {
            { x => x.Address, LocalServerConnectAddress },
            { x => x.Title, null }
        };

        _ = await _dialog.ShowAsync<ConnectingDialog>(L["local-server-connecting-title"], parameters, options);
    }

    private bool CanClearInstalledServers() => !_clearing && !CanStop();

    private async Task ClearInstalledServers()
    {
        var confirmed = await _dialog.ShowMessageBoxAsync(
            L["local-server-clear-confirm-title"],
            L["local-server-clear-confirm-text"],
            yesText: L["local-server-clear-confirm-yes"],
            cancelText: L["local-server-clear-confirm-cancel"]);

        if (confirmed != true)
            return;

        _clearing = true;
        try
        {
            await _bridge.ClearLocalServerInstallsAsync();
            _latestBuild = null;
            _ = _snackbar.Add(L["local-server-clear-done"], Severity.Success);
        }
        catch (Exception e)
        {
            _ = _snackbar.Add(e.Message, Severity.Error);
        }
        finally
        {
            _state = _bridge.GetLocalServerState();
            _clearing = false;
        }
    }

    private void ToggleAutoScroll() => _autoScrollEnabled = !_autoScrollEnabled;

    private bool CanSendCommand() => _state.Phase == LocalServerPhase.Running;

    private void SendCommand()
    {
        if (string.IsNullOrWhiteSpace(_commandInput))
            return;

        if (!_bridge.SendLocalServerCommand(_commandInput))
            _ = _snackbar.Add(L["local-server-console-send-failed"], Severity.Warning);

        _commandInput = "";
    }

    private void OnCommandKeyDown(KeyboardEventArgs e)
    {
        if (e.Key is "Enter" or "NumpadEnter")
            SendCommand();
    }

    private void ClearConsole() => _console.Clear();

    private void OnStateChanged(LocalServerState state)
    {
        _state = state;
        _ = InvokeAsync(StateHasChanged);
    }

    private void OnOutput(LocalServerLogLine line)
    {
        _console.Add(line);
        if (_console.Count > 5000)
            _console.RemoveAt(0);

        if (_autoScrollEnabled)
            _autoScrollPending = true;
        _ = InvokeAsync(StateHasChanged);
    }

    private bool CanStart() => SelectedSource is not null
        && _state.Phase is LocalServerPhase.Idle or LocalServerPhase.Stopped or LocalServerPhase.Error;

    private bool CanStop() => _state.Phase is LocalServerPhase.FetchingManifest or LocalServerPhase.Downloading
        or LocalServerPhase.Extracting or LocalServerPhase.Starting or LocalServerPhase.Running;

    private Color GetStatusColor() => _state.Phase switch
    {
        LocalServerPhase.Running => Color.Success,
        LocalServerPhase.Error => Color.Error,
        LocalServerPhase.Idle or LocalServerPhase.Stopped => Color.Default,
        _ => Color.Info
    };

    private string GetStatusText() => _state.Phase switch
    {
        LocalServerPhase.Idle => L["local-server-status-idle"],
        LocalServerPhase.FetchingManifest => L["local-server-status-fetching"],
        LocalServerPhase.Downloading => L.GetString("local-server-status-downloading", ("percent", FormatPercent())),
        LocalServerPhase.Extracting => L["local-server-status-extracting"],
        LocalServerPhase.Starting => L["local-server-status-starting"],
        LocalServerPhase.Running => L["local-server-status-running"],
        LocalServerPhase.Stopping => L["local-server-status-stopping"],
        LocalServerPhase.Stopped => L["local-server-status-stopped"],
        LocalServerPhase.Error => L["local-server-status-error"],
        _ => ""
    };

    private string FormatPercent()
    {
        if (_state is { TotalBytes: > 0, DownloadedBytes: not null })
            return $"{(int)(_state.DownloadedBytes.Value * 100 / _state.TotalBytes.Value)}%";
        return "";
    }

    private static string ShortHash(string? hash) =>
        string.IsNullOrEmpty(hash) ? "" : hash.Length > 10 ? hash[..10] : hash;

    private static string FormatSize(long? bytes)
    {
        if (bytes is null)
            return "?";

        double size = bytes.Value;
        string[] units = ["B", "KB", "MB", "GB"];
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.#} {units[unit]}";
    }

    public override void Dispose()
    {
        _bridge.LocalServerStateChanged -= OnStateChanged;
        _bridge.LocalServerOutput -= OnOutput;
        base.Dispose();
    }
}
