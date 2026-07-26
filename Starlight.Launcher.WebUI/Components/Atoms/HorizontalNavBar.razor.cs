using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Starlight.Launcher.Services.Localization;

namespace Starlight.Launcher.Components.Atoms;

public sealed partial class HorizontalNavBar : ComponentBase, IDisposable
{
    [Inject] private LocalizationManager _localization { get; set; } = default!;
    [Inject] private NavigationManager _navigation { get; set; } = default!;

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

    protected override void OnInitialized()
        => _navigation.LocationChanged += OnLocationChanged;

    public void Dispose() => _navigation.LocationChanged -= OnLocationChanged;
}
