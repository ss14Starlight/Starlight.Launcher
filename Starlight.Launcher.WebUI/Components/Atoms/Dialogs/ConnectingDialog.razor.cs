using System.ComponentModel;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Starlight.Launcher.Services;
using Starlight.Launcher.Services.Localization;
using static Starlight.Launcher.Services.Connector;
using static Starlight.Launcher.Services.Updater;

namespace Starlight.Launcher.Components.Atoms.Dialogs;

public sealed partial class ConnectingDialog : ComponentBase, IDisposable
{
    [Inject] private LocalizationManager _localization { get; set; } = default!;
    [Inject] private Connector _connector { get; set; } = default!;
    [Inject] private Updater _updater { get; set; } = default!;
    [CascadingParameter] private IMudDialogInstance _mudDialog { get; set; } = default!;

    [Parameter] public string Address { get; set; } = "";

    [Parameter] public FileResult? ContentBundle { get; set; }

    [Parameter] public string? Title { get; set; } = null;

    private readonly CancellationTokenSource _cts = new();
    private Timer? _pollTimer;

    // Speed measurement, derived from the SAME downloaded counter we display.
    private long _lastDownloaded;
    private DateTime _lastSampleUtc;
    private double _smoothedBytesPerSec;
    private bool _haveSpeed;
    private UpdateStatus _lastUpdateStatus;

    private string _title => ContentBundle is { } f
        ? $"Launching {f.FileName}"
        : $"Connecting to {Title ?? Address}";

    private string _titleIcon => ContentBundle is not null
        ? Icons.Material.Filled.Inventory2
        : Icons.Material.Filled.Dns;

    private double _percent =>
        _updater.Progress is { total: > 0 } p ? (double)p.downloaded / p.total * 100.0 : 0;

    private string _percentCss => _percent.ToString("0.##", CultureInfo.InvariantCulture);

    private bool _hasDeterminateProgress =>
        _connector.Status == ConnectionStatus.Updating && _updater.Progress is { total: > 0 };

    // Only these phases report progress as real bytes — gate byte/speed display on them.
    private bool _isByteProgress =>
        _connector.Status == ConnectionStatus.Updating && _updater.Status is
            UpdateStatus.DownloadingEngineVersion
            or UpdateStatus.DownloadingEngineModules
            or UpdateStatus.DownloadingClientUpdate;

    private string? _eta
    {
        get
        {
            if (!_haveSpeed || _smoothedBytesPerSec <= 1) return null;
            if (_updater.Progress is not { total: > 0 } p) return null;

            var remaining = p.total - p.downloaded;
            if (remaining <= 0) return null;

            var secs = remaining / _smoothedBytesPerSec;
            if (double.IsNaN(secs) || double.IsInfinity(secs) || secs > 86_400) return null;

            var ts = TimeSpan.FromSeconds(secs);
            return ts.TotalMinutes >= 1
                ? $"{(int)ts.TotalMinutes}m {ts.Seconds:00}s"
                : $"{Math.Max(1, (int)Math.Ceiling(ts.TotalSeconds))}s";
        }
    }

    private bool _isTerminal => _connector.Status is
        ConnectionStatus.ConnectionFailed
        or ConnectionStatus.UpdateError
        or ConnectionStatus.NotAContentBundle
        or ConnectionStatus.Cancelled
        or ConnectionStatus.ClientExited;

    private string _statusText => _connector.Status switch
    {
        ConnectionStatus.Connecting => "Contacting server…",
        ConnectionStatus.Updating => _updater.Status switch
        {
            UpdateStatus.DownloadingEngineVersion => "Downloading engine…",
            UpdateStatus.DownloadingEngineModules => "Downloading engine modules…",
            UpdateStatus.DownloadingClientUpdate => "Downloading game files…",
            UpdateStatus.LoadingIntoDb => "Installing…",
            UpdateStatus.LoadingContentBundle => "Loading content bundle…",
            UpdateStatus.Verifying => "Verifying…",
            UpdateStatus.CommittingDownload => "Finishing up…",
            _ => "Checking for updates…"
        },
        ConnectionStatus.StartingClient => "Starting the game…",
        ConnectionStatus.ClientRunning => "Game is running.",
        ConnectionStatus.ConnectionFailed => "Could not reach the server.",
        ConnectionStatus.UpdateError => "Update failed. See the logs for details.",
        ConnectionStatus.NotAContentBundle => "This file isn't a valid content bundle.",
        ConnectionStatus.Cancelled => "Cancelled.",
        _ => "Starting…"
    };

    protected override void OnInitialized()
    {
        _connector.PropertyChanged += OnConnectorChanged;

        _pollTimer = new Timer(_ =>
        {
            SampleSpeed();
            InvokeAsync(StateHasChanged);
        }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(250));

        // Fire-and-forget: Status drives the UI from here on.
        if (ContentBundle is { } bundle)
            _connector.LaunchContentBundle(bundle, _cts.Token);
        else
            _connector.Connect(Address, _cts.Token);
    }

    // Recompute speed from the displayed counter so the two can never disagree.
    private void SampleSpeed()
    {
        if (!_isByteProgress || _updater.Progress is not { total: > 0 } p)
        {
            _haveSpeed = false;
            _smoothedBytesPerSec = 0;
            _lastDownloaded = 0;
            return;
        }

        var now = DateTime.UtcNow;

        // Reset on phase change or when the counter restarts (new download).
        if (_updater.Status != _lastUpdateStatus || p.downloaded < _lastDownloaded)
        {
            _lastUpdateStatus = _updater.Status;
            _lastDownloaded = p.downloaded;
            _lastSampleUtc = now;
            _haveSpeed = false;
            _smoothedBytesPerSec = 0;
            return;
        }

        var dt = (now - _lastSampleUtc).TotalSeconds;
        if (dt >= 0.2) // ignore tiny intervals that produce spikes
        {
            var instant = (p.downloaded - _lastDownloaded) / dt;
            const double Alpha = 0.25; // EMA smoothing
            _smoothedBytesPerSec = _haveSpeed
                ? (Alpha * instant) + ((1 - Alpha) * _smoothedBytesPerSec)
                : instant;

            _haveSpeed = true;
            _lastDownloaded = p.downloaded;
            _lastSampleUtc = now;
        }
    }

    private void OnConnectorChanged(object? sender, PropertyChangedEventArgs e) =>
        InvokeAsync(() =>
        {
            if (_connector.Status == ConnectionStatus.ClientRunning)
                _mudDialog.Close();

            StateHasChanged();
        });

    private void Decide(PrivacyPolicyAcceptResult result) => _connector.ConfirmPrivacyPolicy(result);

    private void Cancel()
    {
        _cts.Cancel();
        _mudDialog.Cancel();
    }

    private void Close() => _mudDialog.Close();

    private static string Format(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double v = bytes;
        var i = 0;
        while (v >= 1024 && i < units.Length - 1)
        {
            v /= 1024;
            i++;
        }
        return $"{v:0.#} {units[i]}";
    }

    public void Dispose()
    {
        _connector.PropertyChanged -= OnConnectorChanged;
        _pollTimer?.Dispose();

        if (_connector.Status != ConnectionStatus.ClientRunning)
        {
            if (!_cts.IsCancellationRequested)
                _cts.Cancel();
            _cts.Dispose();
        }
    }
}
