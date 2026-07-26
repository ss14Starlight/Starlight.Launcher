using Microsoft.AspNetCore.Components;
using Starlight.Launcher.Services.Localization;

namespace Starlight.Launcher.Components.Atoms.Settings;

public partial class AlternativeListOption : ComponentBase
{
    [Inject] private LocalizationManager _localization { get; set; } = default!;

    [Parameter]public List<(string Value, long Priority)> Value { get; set; } = [];
    [Parameter] public EventCallback<List<(string Value, long Priority)>> ValueChanged { get; set; }
    [Parameter] public string Title { get; set; } = default!;
    [Parameter] public string Description { get; set; } = default!;
    [Parameter] public string Icon { get; set; } = default!;
    [Parameter] public string EmptyValuesString { get; set; } = "There's no values added up. Click + to add one.";
    [Parameter] public string AddButtonTooltip { get; set; } = "Add Value";
    [Parameter] public string TextFieldLabel { get; set; } = "Value";
    /// <summary>
    /// Means that this component will control value change by itself.
    /// </summary>
    [Parameter] public bool SelfValueControl { get; set; } = true;
    [Parameter] public Action<List<(string Value, long Priority)>>? SelfValueControlAction { get; set; }
    [Parameter] public Func<Task<List<(string Value, long Priority)>>>? SelfValueControlInitialization { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        if (SelfValueControlInitialization is null)
            return;
        Value = await SelfValueControlInitialization.Invoke();
    }

    private async Task OnAddButtonPressed()
    {
        // 0 = highest priority; new item goes to the end (lowest priority).
        Value.Add((string.Empty, Value.Count));
        await NotifyChanged();
    }

    private async Task RemoveAt(int index)
    {
        if (index < 0 || index >= Value.Count)
            return;

        Value.RemoveAt(index);
        ReassignPriorities();
        await NotifyChanged();
    }

    private async Task MoveUp(int index)
    {
        if (index <= 0 || index >= Value.Count)
            return;

        (Value[index - 1], Value[index]) = (Value[index], Value[index - 1]);
        ReassignPriorities();
        await NotifyChanged();
    }

    private async Task MoveDown(int index)
    {
        if (index < 0 || index >= Value.Count - 1)
            return;

        (Value[index + 1], Value[index]) = (Value[index], Value[index + 1]);
        ReassignPriorities();
        await NotifyChanged();
    }

    private async Task OnValueChanged(string value, int index)
    {
        if (index < 0 || index >= Value.Count)
            return;

        Value[index] = (value, Value[index].Priority);
        await NotifyChanged();
    }

    private void ReassignPriorities()
    {
        for (var i = 0; i < Value.Count; i++)
            Value[i] = (Value[i].Value, i);
    }

    private async Task NotifyChanged()
    {
        if (!SelfValueControl)
            await ValueChanged.InvokeAsync(Value);
        else
        {
            SelfValueControlAction?.Invoke(Value);
        }
    }

    private static string? ValidateUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "URL is required";

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return "Invalid URL";

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return "Only HTTP/HTTPS allowed";

        return null;
    }
}
