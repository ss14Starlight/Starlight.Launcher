using System.Diagnostics;
using Microsoft.AspNetCore.Components;
using Robust.Launcher.Api.Models.Data;
using Starlight.Launcher.Services;
using Starlight.Launcher.Services.Localization;

namespace Starlight.Launcher.Components.Atoms.Settings;

public partial class FilePathOption : ComponentBase
{
    [Inject] private IFileDialogService _fileDialog { get; set; } = default!;
    [Inject] private LocalizationManager _localization { get; set; } = default!;

    [Parameter] public string Value { get; set; } = "";
    [Parameter] public EventCallback<string> ValueChanged { get; set; }
    [Parameter] public string Title { get; set; } = default!;
    [Parameter] public string Description { get; set; } = default!;
    [Parameter] public string Icon { get; set; } = default!;
    [Parameter] public string Label { get; set; } = "";
    [Parameter] public string Helper { get; set; } = "";
    [Parameter] public bool HelperOnFocus { get; set; } = false;

    /// <summary>
    /// Determines should we show open folder/file button.
    /// </summary>
    [Parameter] public bool ShowOpenButton { get; set; } = false;

    /// <summary>
    /// Allows typing the path manually. If false, the value can only be
    /// changed through the picker dialog (field is read-only).
    /// </summary>
    [Parameter] public bool AllowManualInput { get; set; } = false;

    /// <summary>
    /// Means that this component will control value change by itself.
    /// </summary>
    [Parameter] public bool SelfValueControl { get; set; } = true;
    [Parameter] public Action<string>? SelfValueControlAction { get; set; }
    [Parameter] public Func<Task<string>>? SelfValueControlInitialization { get; set; }

    /// <summary>
    /// Custom picker. When null, falls back to the injected
    /// <see cref="IFileDialogService"/>'s default file dialog.
    /// </summary>
    [Parameter] public Func<Task<FileResult?>>? PickAction { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        if (SelfValueControlInitialization is null)
            return;
        Value = await SelfValueControlInitialization.Invoke();
    }

    private async Task OpenFolderAsync() => Process.Start(new ProcessStartInfo
    {
        FileName = Value,
        UseShellExecute = true
    });

    private Task OnValueChanged(string value) => ApplyValueAsync(value);

    private async Task BrowseAsync()
    {
        var result = PickAction is not null
            ? await PickAction.Invoke()
            : await _fileDialog.PickFileAsync();

        if (result is null || string.IsNullOrEmpty(result.FullPath))
            return;

        await ApplyValueAsync(result.FullPath);
    }

    private async Task ApplyValueAsync(string value)
    {
        if (!SelfValueControl)
        {
            await ValueChanged.InvokeAsync(value);
            return;
        }

        Value = value;
        SelfValueControlAction?.Invoke(value);
    }
}
