using System.Globalization;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Starlight.Launcher.Services;

namespace Starlight.Launcher.Components.Atoms.Dialogs;

public sealed partial class LauncherUpdateDialog : ComponentBase, IDisposable
{
    [Inject] private LauncherUpdater _launcherUpdater { get; set; } = default!;
    [CascadingParameter] private IMudDialogInstance _mudDialog { get; set; } = default!;

    [Parameter] public LauncherUpdater.ReleaseAsset Asset { get; set; } = default!;

    private enum Phase { Downloading, Done, Failed }
    private Phase _phase = Phase.Downloading;
    private string? _error;

    private readonly CancellationTokenSource _cts = new();
    private Timer? _pollTimer;

    private long _downloaded;
    private long _total;

    // Speed measurement, same approach as ConnectingDialog.
    private long _lastDownloaded;
    private DateTime _lastSampleUtc;
    private double _smoothedBytesPerSec;
    private bool _haveSpeed;

    private bool _hasDeterminateProgress => _total > 0;
    private double _percent => _total > 0 ? (double)_downloaded / _total * 100.0 : 0;
    private string _percentCss => _percent.ToString("0.##", CultureInfo.InvariantCulture);

    private string? _eta
    {
        get
        {
            if (!_haveSpeed || _smoothedBytesPerSec <= 1 || _total <= 0) return null;

            var remaining = _total - _downloaded;
            if (remaining <= 0) return null;

            var secs = remaining / _smoothedBytesPerSec;
            if (double.IsNaN(secs) || double.IsInfinity(secs) || secs > 86_400) return null;

            var ts = TimeSpan.FromSeconds(secs);
            return ts.TotalMinutes >= 1
                ? $"{(int)ts.TotalMinutes}m {ts.Seconds:00}s"
                : $"{Math.Max(1, (int)Math.Ceiling(ts.TotalSeconds))}s";
        }
    }

    protected override void OnInitialized()
    {
        _launcherUpdater.DownloadProgress += OnProgress;

        _pollTimer = new Timer(_ =>
        {
            SampleSpeed();
            InvokeAsync(StateHasChanged);
        }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(250));

        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            var path = await _launcherUpdater.DownloadAsset(Asset, _cts.Token);

            _phase = Phase.Done;
            await InvokeAsync(StateHasChanged);

            // Small pause so the user sees "complete" before we vanish.
            await Task.Delay(800, _cts.Token);

            LauncherUpdater.RunInstallerAndExit(path);
        }
        catch (OperationCanceledException)
        {
            // User cancelled — dialog already closing.
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            _phase = Phase.Failed;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void OnProgress((long downloaded, long total) p)
    {
        _downloaded = p.downloaded;
        _total = p.total;
        // StateHasChanged is driven by the poll timer to avoid flooding the UI thread.
    }

    private void SampleSpeed()
    {
        if (_phase != Phase.Downloading || _total <= 0)
        {
            _haveSpeed = false;
            _smoothedBytesPerSec = 0;
            _lastDownloaded = 0;
            return;
        }

        var now = DateTime.UtcNow;

        if (_downloaded < _lastDownloaded)
        {
            _lastDownloaded = _downloaded;
            _lastSampleUtc = now;
            _haveSpeed = false;
            _smoothedBytesPerSec = 0;
            return;
        }

        if (_lastSampleUtc == default)
        {
            _lastDownloaded = _downloaded;
            _lastSampleUtc = now;
            return;
        }

        var dt = (now - _lastSampleUtc).TotalSeconds;
        if (dt >= 0.2)
        {
            var instant = (_downloaded - _lastDownloaded) / dt;
            const double Alpha = 0.25;
            _smoothedBytesPerSec = _haveSpeed
                ? (Alpha * instant) + ((1 - Alpha) * _smoothedBytesPerSec)
                : instant;

            _haveSpeed = true;
            _lastDownloaded = _downloaded;
            _lastSampleUtc = now;
        }
    }

    private void Cancel()
    {
        if (!_cts.IsCancellationRequested)
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
        _launcherUpdater.DownloadProgress -= OnProgress;
        _pollTimer?.Dispose();

        if (!_cts.IsCancellationRequested)
            _cts.Cancel();
        _cts.Dispose();
    }
}
