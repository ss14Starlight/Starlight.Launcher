using Microsoft.AspNetCore.Components;
using MudBlazor;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Models.Settings;

namespace Starlight.Launcher.WebUI.Components.Atoms.Dialogs;

public partial class DefaultsChangedDialog
{
    [CascadingParameter] private IMudDialogInstance _mudDialog { get; set; } = default!;
    [Inject] private IBridge _bridge { get; set; } = default!;

    [Parameter, EditorRequired] public IReadOnlyList<DefaultSettingChange> Changes { get; set; } = [];

    private static readonly Dictionary<string, string> _labels = new()
    {
        [nameof(AppSettings.Hubs)] = "settings-menu-hub-list-option",
        [nameof(AppSettings.AuthServerUrls)] = "settings-menu-auth-list-option",
        [nameof(AppSettings.RobustCdns)] = "settings-cdns-option",
        [nameof(AppSettings.LocalServerSources)] = "local-server-sources-option-title",
        [nameof(AppSettings.Theme)] = "settings-menu-theme-option",
        [nameof(AppSettings.Navigation)] = "settings-menu-navigation-bar-option",
        [nameof(AppSettings.AnimatedBackground)] = "settings-menu-animated-background-option",
        [nameof(AppSettings.HolidayThemes)] = "settings-menu-holiday-themes-option",
        [nameof(AppSettings.PreventMultipleClients)] = "settings-menu-multiple-clients-option",
        [nameof(AppSettings.ReceivePrereleases)] = "settings-menu-prereleases-option",
        [nameof(AppSettings.PresenceStates)] = "settings-menu-discord-presence-states-option",
        [nameof(AppSettings.DownloadSpeedLimitKb)] = "settings-menu-download-speed-limit-option",
        [nameof(AppSettings.PreloadFavoriteContent)] = "settings-menu-preload-favorites-option",
    };

    private enum ItemState
    {
        Pending,
        Accepted,
        Kept,
    }

    private enum DiffKind
    {
        Same,
        Removed,
        Added,
    }

    private sealed record DiffLine(DiffKind Kind, string Text);

    private sealed class Item(DefaultSettingChange change)
    {
        public DefaultSettingChange Change { get; } = change;
        public IReadOnlyList<DiffLine> Diff { get; } = LineDiff(change.CurrentValue, change.MergedValue);
        public ItemState State { get; set; }
        public bool DiffOpen { get; set; }
    }

    private List<Item> _items = [];

    private bool _hasPending => _items.Any(i => i.State == ItemState.Pending);

    protected override void OnParametersSet()
    {
        _items = [.. Changes.Select(c => new Item(c))];
        if (_items.Count == 1)
            _items[0].DiffOpen = true;
    }

    private string Label(string key) => _labels.TryGetValue(key, out var loc) ? L[loc] : key;

    private void Accept(Item item)
    {
        _bridge.AcceptDefaultChanges([item.Change.Key]);
        item.State = ItemState.Accepted;
        CloseIfDone();
    }

    private void Keep(Item item)
    {
        _bridge.KeepCurrentSettings([item.Change.Key]);
        item.State = ItemState.Kept;
        CloseIfDone();
    }

    private void AcceptAll() => ResolveAll(ItemState.Accepted, _bridge.AcceptDefaultChanges);

    private void KeepAll() => ResolveAll(ItemState.Kept, _bridge.KeepCurrentSettings);

    private void ResolveAll(ItemState state, Action<IReadOnlyCollection<string>> resolve)
    {
        var pending = _items.Where(i => i.State == ItemState.Pending).ToList();
        resolve([.. pending.Select(i => i.Change.Key)]);
        foreach (var item in pending)
            item.State = state;

        _mudDialog.Close(DialogResult.Ok(true));
    }

    private void CloseIfDone()
    {
        if (!_hasPending)
            _mudDialog.Close(DialogResult.Ok(true));
    }

    private void Later() => _mudDialog.Cancel();

    private static List<DiffLine> LineDiff(string from, string to)
    {
        var a = from.ReplaceLineEndings("\n").Split('\n');
        var b = to.ReplaceLineEndings("\n").Split('\n');

        var lcs = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
        {
            for (var j = b.Length - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
        }

        var result = new List<DiffLine>(a.Length + b.Length);
        int x = 0, y = 0;
        while (x < a.Length && y < b.Length)
        {
            if (a[x] == b[y])
            {
                result.Add(new(DiffKind.Same, a[x++]));
                y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                result.Add(new(DiffKind.Removed, a[x++]));
            }
            else
            {
                result.Add(new(DiffKind.Added, b[y++]));
            }
        }

        while (x < a.Length)
            result.Add(new(DiffKind.Removed, a[x++]));

        while (y < b.Length)
            result.Add(new(DiffKind.Added, b[y++]));

        return result;
    }
}
