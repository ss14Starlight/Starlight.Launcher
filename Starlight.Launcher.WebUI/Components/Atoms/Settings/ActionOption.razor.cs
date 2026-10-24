using Microsoft.AspNetCore.Components;

namespace Starlight.Launcher.WebUI.Components.Atoms.Settings;

public partial class ActionOption : ComponentBase
{
    [Parameter] public string Title { get; set; } = default!;
    [Parameter] public string Description { get; set; } = default!;
    [Parameter] public string Icon { get; set; } = default!;
    [Parameter] public string ButtonText { get; set; } = default!;
    [Parameter] public string? ButtonIcon { get; set; }
    [Parameter] public bool Danger { get; set; }
    [Parameter] public Func<Task>? Action { get; set; }

    [Parameter] public Func<CancellationToken, Task>? CancellableAction { get; set; }
    [Parameter] public string? CancelText { get; set; }

    private bool _running;
    private CancellationTokenSource? _cts;

    private bool CanCancel => _running && _cts is { IsCancellationRequested: false };

    private async Task RunAsync()
    {
        if (_running)
        {
            _cts?.Cancel();
            return;
        }

        if (Action is null && CancellableAction is null)
            return;

        _running = true;
        try
        {
            if (CancellableAction is not null)
            {
                _cts = new CancellationTokenSource();
                await CancellableAction(_cts.Token);
            }
            else
            {
                await Action!();
            }
        }
        finally
        {
            _running = false;
            _cts?.Dispose();
            _cts = null;
        }
    }
}
