using Microsoft.AspNetCore.Components;
using Starlight.Launcher.Services.Localization;

namespace Starlight.Launcher.Components.Atoms.Settings;

public partial class AlternativeSelectorListOption : ComponentBase
{
    [Inject] private LocalizationManager _localization { get; set; } = default!;

    [Parameter] public List<string> Values { get; set; } = [];
    [Parameter] public EventCallback<List<string>> ValuesChanged { get; set; }

    [Parameter] public string? SelectedValue { get; set; }
    [Parameter] public EventCallback<string?> SelectedValueChanged { get; set; }

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
    [Parameter] public Action<List<string>, string?>? SelfValueControlAction { get; set; }
    [Parameter] public Func<Task<(List<string> Values, string? Selected)>>? SelfValueControlInitialization { get; set; }

    private int _selectedIndex = -1;

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();

        if (SelfValueControlInitialization is not null)
        {
            var (values, selected) = await SelfValueControlInitialization.Invoke();
            Values = values;
            SelectedValue = selected;
        }

        _selectedIndex = SelectedValue is null ? -1 : Values.IndexOf(SelectedValue);
    }

    private async Task OnAddButtonPressed()
    {
        Values.Add(string.Empty);
        await NotifyChanged();
    }

    private async Task RemoveAt(int index)
    {
        if (index < 0 || index >= Values.Count)
            return;

        Values.RemoveAt(index);

        // Корректируем индекс выбранного элемента после удаления.
        if (_selectedIndex == index)
            _selectedIndex = -1;
        else if (_selectedIndex > index)
            _selectedIndex--;

        SyncSelectedValue();
        await NotifyChanged();
    }

    private async Task Select(int index)
    {
        if (index < 0 || index >= Values.Count)
            return;

        _selectedIndex = index;
        SyncSelectedValue();
        await NotifyChanged();
    }

    private async Task OnValueChanged(string value, int index)
    {
        if (index < 0 || index >= Values.Count)
            return;

        Values[index] = value;

        // Если редактируем активное значение — обновляем SelectedValue.
        if (_selectedIndex == index)
            SyncSelectedValue();

        await NotifyChanged();
    }

    private void SyncSelectedValue()
        => SelectedValue = _selectedIndex >= 0 && _selectedIndex < Values.Count
            ? Values[_selectedIndex]
            : null;

    private async Task NotifyChanged()
    {
        if (!SelfValueControl)
        {
            await ValuesChanged.InvokeAsync(Values);
            await SelectedValueChanged.InvokeAsync(SelectedValue);
        }
        else
        {
            SelfValueControlAction?.Invoke(Values, SelectedValue);
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
