using System.Collections.Concurrent;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Localization;
using Starlight.Launcher.WebUI.Models.Diagnostics;
using Starlight.Launcher.WebUI.Models.Helpers;

namespace Starlight.Launcher.WebUI.Components.Atoms.Development;

/// <summary>
/// Live stdout/stderr of the game, the same text the client log gets.
/// </summary>
public partial class GameConsole : LocalizedComponentBase
{
    private const int MaxLines = 5000;

    private static readonly TimeSpan _flushInterval = TimeSpan.FromMilliseconds(150);

    [Inject] private IBridge _bridge { get; set; } = default!;
    [Inject] private IJSRuntime _js { get; set; } = default!;
    [Inject] private ISnackbar _snackbar { get; set; } = default!;

    [Parameter] public Func<Task>? OpenLogs { get; set; }

    private readonly RingBuffer<ConsoleLine> _lines = new(MaxLines);
    private readonly ConcurrentQueue<ConsoleLine> _pending = new();
    private int _flushScheduled;
    private bool _autoScroll = true;
    private bool _scrollPending;
    private string? _filter;
    private bool _errorsOnly;

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _lines.AddRange(_bridge.GetClientConsoleBuffer());
        _scrollPending = true;
        _bridge.ClientConsoleOutput += OnOutput;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_scrollPending || !_autoScroll)
            return;

        _scrollPending = false;
        try
        {
            await _js.InvokeVoidAsync("eval", "var el = document.getElementById('game-console-log'); if (el) el.scrollTop = el.scrollHeight;");
        }
        catch (JSDisconnectedException)
        {
            // Page is going away.
        }
    }

    private IEnumerable<ConsoleLine> Visible()
    {
        IEnumerable<ConsoleLine> lines = _lines;
        if (_errorsOnly)
            lines = lines.Where(l => l.IsError);

        if (!string.IsNullOrWhiteSpace(_filter))
            lines = lines.Where(l => l.Text.Contains(_filter, StringComparison.OrdinalIgnoreCase));

        return lines;
    }

    // Reader threads only enqueue, the buffer is touched on the renderer thread.
    private void OnOutput(ConsoleLine line)
    {
        _pending.Enqueue(line);
        if (Interlocked.Exchange(ref _flushScheduled, 1) == 0)
            _ = FlushAsync();
    }

    private async Task FlushAsync()
    {
        await Task.Delay(_flushInterval);
        await InvokeAsync(() =>
        {
            _ = Interlocked.Exchange(ref _flushScheduled, 0);
            while (_pending.TryDequeue(out var line))
                _lines.Add(line);

            _scrollPending = true;
            StateHasChanged();
        });
    }

    private async Task CopyAsync()
    {
        var text = string.Join('\n', Visible().Select(l => $"{l.Timestamp:HH:mm:ss} {l.Text}"));
        try
        {
            await _js.InvokeVoidAsync("navigator.clipboard.writeText", text);
            _ = _snackbar.Add(L["development-console-copied"], Severity.Success);
        }
        catch (JSException)
        {
            _ = _snackbar.Add(L["development-console-copy-failed"], Severity.Warning);
        }
    }

    private async Task OpenLogsAsync()
    {
        if (OpenLogs != null)
            await OpenLogs();
    }

    private void Clear()
    {
        _bridge.ClearClientConsole();
        _lines.Clear();
    }

    public override void Dispose()
    {
        base.Dispose();
        _bridge.ClientConsoleOutput -= OnOutput;
        GC.SuppressFinalize(this);
    }
}
