using Microsoft.AspNetCore.Components;
using Starlight.Launcher.WebUI.Localization;
using Starlight.Launcher.WebUI.Models.DiscordRichPresence;

namespace Starlight.Launcher.WebUI.Components.Atoms.Settings;

public partial class PresenceStateListOption : LocalizedComponentBase
{
    [Parameter] public List<PresenceStateOption> Value { get; set; } = [];
    [Parameter] public EventCallback<List<PresenceStateOption>> ValueChanged { get; set; }

    [Parameter] public string Title { get; set; } = default!;
    [Parameter] public string Description { get; set; } = default!;
    [Parameter] public string Icon { get; set; } = default!;
    [Parameter] public string ResetButtonTooltip { get; set; } = "";
    [Parameter] public string PriorityTooltip { get; set; } = "";
    [Parameter] public string PinnedTooltip { get; set; } = "";

    /// <summary>
    /// Means that this component will control value change by itself.
    /// </summary>
    [Parameter] public bool SelfValueControl { get; set; } = true;
    [Parameter] public Action<List<PresenceStateOption>>? SelfValueControlAction { get; set; }
    [Parameter] public Func<Task<List<PresenceStateOption>>>? SelfValueControlInitialization { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();

        if (SelfValueControlInitialization is not null)
            Value = await SelfValueControlInitialization.Invoke();

        Value = PresenceStates.Normalize(Value);

        if (string.IsNullOrEmpty(ResetButtonTooltip))
            ResetButtonTooltip = L["settings-presence-state-list-option-reset"];
        if (string.IsNullOrEmpty(PriorityTooltip))
            PriorityTooltip = L["settings-presence-state-list-option-priority-tooltip"];
        if (string.IsNullOrEmpty(PinnedTooltip))
            PinnedTooltip = L["settings-presence-state-list-option-idle-tooltip"];
    }

    private string StateTitle(PresenceState state) =>
        L[$"presence-state-{state.ToString().ToLowerInvariant()}"];

    private string StateDescription(PresenceState state) =>
        L[$"presence-state-{state.ToString().ToLowerInvariant()}-description"];

    private bool CanMoveDown(int index)
    {
        for (var i = index + 1; i < Value.Count; i++)
        {
            if (PresenceStates.IsMovable(Value[i].State))
                return true;
        }

        return false;
    }

    private async Task MoveUp(int index)
    {
        if (index <= 0 || index >= Value.Count || !PresenceStates.IsMovable(Value[index].State))
            return;

        (Value[index - 1], Value[index]) = (Value[index], Value[index - 1]);
        await NotifyChanged();
    }

    private async Task MoveDown(int index)
    {
        if (index < 0 || index >= Value.Count - 1 || !CanMoveDown(index))
            return;

        (Value[index + 1], Value[index]) = (Value[index], Value[index + 1]);
        await NotifyChanged();
    }

    private async Task SetEnabled(int index, bool enabled)
    {
        if (index < 0 || index >= Value.Count)
            return;

        Value[index] = Value[index] with { Enabled = enabled };
        await NotifyChanged();
    }

    private async Task ResetToDefault()
    {
        Value = PresenceStates.CreateDefault();
        await NotifyChanged();
    }

    private async Task NotifyChanged()
    {
        Value = PresenceStates.Normalize(Value);

        if (!SelfValueControl)
            await ValueChanged.InvokeAsync(Value);
        else
            SelfValueControlAction?.Invoke(Value);
    }
}
