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

    private bool _running;

    private async Task RunAsync()
    {
        if (Action is null || _running)
            return;

        _running = true;
        try
        {
            await Action();
        }
        finally
        {
            _running = false;
        }
    }
}
