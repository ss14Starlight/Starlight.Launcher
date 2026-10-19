using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Localization;
using Starlight.Launcher.WebUI.Services;

namespace Starlight.Launcher.WebUI.Components.Atoms;

public sealed partial class HorizontalNavBar : LocalizedComponentBase, IDisposable
{
    [Inject] private NavigationManager _navigation { get; set; } = default!;
    [Inject] private IBridge _bridge { get; set; } = default!;
    [Inject] private AppState _state { get; set; } = default!;

    private bool _developerMode;

    private bool IsActive(string href, NavLinkMatch match = NavLinkMatch.Prefix)
    {
        var current = _navigation.ToBaseRelativePath(_navigation.Uri).Split('?')[0];
        current = "/" + current.TrimEnd('/');
        var target = href.TrimEnd('/');
        if (string.IsNullOrEmpty(target)) target = "/";

        return match == NavLinkMatch.All
            ? string.Equals(current, target, StringComparison.OrdinalIgnoreCase)
            : current.StartsWith(target, StringComparison.OrdinalIgnoreCase);
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
        => InvokeAsync(StateHasChanged);

    private void OnAppStateChanged()
    {
        _developerMode = _bridge.GetSettings().DeveloperMode;
        _ = InvokeAsync(StateHasChanged);
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _developerMode = _bridge.GetSettings().DeveloperMode;
        _navigation.LocationChanged += OnLocationChanged;
        _state.OnChange += OnAppStateChanged;
    }

    public override void Dispose()
    {
        base.Dispose();

        _navigation.LocationChanged -= OnLocationChanged;
        _state.OnChange -= OnAppStateChanged;
    }
}
