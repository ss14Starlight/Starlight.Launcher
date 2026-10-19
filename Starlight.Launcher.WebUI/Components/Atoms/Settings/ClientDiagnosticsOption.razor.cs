using System.Globalization;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Localization;
using Starlight.Launcher.WebUI.Models.Diagnostics;

namespace Starlight.Launcher.WebUI.Components.Atoms.Settings;

public partial class ClientDiagnosticsOption : LocalizedComponentBase
{
    private static readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(1);

    private static readonly (string Name, string LocKey, Func<double, string> Format)[] _shown =
    [
        ("cpu-usage", "client-diagnostics-cpu", v => $"{v:0.#} %"),
        ("working-set", "client-diagnostics-working-set", v => $"{v:0} MB"),
        ("gc-heap-size", "client-diagnostics-gc-heap", v => $"{v:0} MB"),
        ("alloc-rate", "client-diagnostics-alloc-rate", v => $"{v / (1024 * 1024):0.0} MB/s"),
        ("time-in-gc", "client-diagnostics-time-in-gc", v => $"{v:0.#} %"),
        ("gen-0-gc-count", "client-diagnostics-gen0", v => $"{v:0} /s"),
        ("gen-1-gc-count", "client-diagnostics-gen1", v => $"{v:0} /s"),
        ("gen-2-gc-count", "client-diagnostics-gen2", v => $"{v:0} /s"),
        ("threadpool-queue-length", "client-diagnostics-threadpool-queue", v => $"{v:0}"),
        ("threadpool-thread-count", "client-diagnostics-threadpool-threads", v => $"{v:0}"),
        ("monitor-lock-contention-count", "client-diagnostics-lock-contention", v => $"{v:0} /s"),
        ("exception-count", "client-diagnostics-exceptions", v => $"{v:0} /s"),
    ];

    [Inject] private IBridge _bridge { get; set; } = default!;
    [Inject] private ISnackbar _snackbar { get; set; } = default!;

    [Parameter] public string Icon { get; set; } = default!;

    private readonly CancellationTokenSource _cts = new();
    private ClientDiagnosticsSnapshot? _snapshot;
    private bool _dumping;

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _ = PollAsync(_cts.Token);
    }

    private async Task PollAsync(CancellationToken cancel)
    {
        try
        {
            using var timer = new PeriodicTimer(_pollInterval);
            do
            {
                _snapshot = await Task.Run(_bridge.GetClientDiagnostics, cancel);
                await InvokeAsync(StateHasChanged);
            } while (await timer.WaitForNextTickAsync(cancel).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private IEnumerable<(string Label, string Value)> Rows()
    {
        if (_snapshot is not { Counters.Count: > 0 } snapshot)
            yield break;

        var byName = snapshot.Counters.ToDictionary(c => c.Name);
        foreach (var (name, key, format) in _shown)
        {
            if (byName.TryGetValue(name, out var counter))
                yield return (L[key], string.Create(CultureInfo.InvariantCulture, $"{format(counter.Value)}"));
        }
    }

    private async Task DumpAsync()
    {
        if (_dumping)
            return;

        _dumping = true;
        try
        {
            var result = await _bridge.WriteClientDumpAsync();
            _ = result.Status switch
            {
                ClientDumpStatus.Success => _snackbar.Add(L.GetString("client-diagnostics-dump-success", ("path", result.Path ?? "")), Severity.Success),
                ClientDumpStatus.NoClient => _snackbar.Add(L["client-diagnostics-not-running"], Severity.Warning),
                _ => _snackbar.Add(L["client-diagnostics-dump-failed"], Severity.Error),
            };
        }
        finally
        {
            _dumping = false;
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        _cts.Cancel();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
