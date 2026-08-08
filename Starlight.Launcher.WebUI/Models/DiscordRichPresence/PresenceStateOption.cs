namespace Starlight.Launcher.WebUI.Models.DiscordRichPresence;

public sealed record PresenceStateOption(PresenceState State, bool Enabled = true);

public static class PresenceStates
{
    public static readonly PresenceState[] DefaultOrder =
    [
        PresenceState.InGame,
        PresenceState.Reconnecting,
        PresenceState.LaunchingGame,
        PresenceState.DownloadingContent,
        PresenceState.UpdatingLauncher,
        PresenceState.ViewingServer,
        PresenceState.ManagesLogins,
        PresenceState.SettingUp,
        PresenceState.SearchingServers,
        PresenceState.Idle
    ];

    public static readonly IEqualityComparer<List<PresenceStateOption>> ListComparer = new OrderedListComparer();

    public static List<PresenceStateOption> CreateDefault() =>
        DefaultOrder.Select(s => new PresenceStateOption(s)).ToList();

    public static List<PresenceStateOption> Normalize(IEnumerable<PresenceStateOption>? saved)
    {
        if (saved is null)
            return CreateDefault();

        var known = Enum.GetValues<PresenceState>().ToHashSet();
        var seen = new HashSet<PresenceState>();
        var result = new List<PresenceStateOption>(known.Count);

        foreach (var option in saved)
        {
            if (!known.Contains(option.State) || !seen.Add(option.State))
                continue;

            result.Add(option);
        }

        for (var i = 0; i < DefaultOrder.Length; i++)
        {
            var state = DefaultOrder[i];
            if (!seen.Add(state))
                continue;

            result.Insert(Math.Min(i, result.Count), new PresenceStateOption(state));
        }

        var idle = result.FirstOrDefault(o => o.State == PresenceState.Idle)
                   ?? new PresenceStateOption(PresenceState.Idle);

        _ = result.RemoveAll(o => o.State == PresenceState.Idle);
        result.Add(idle);

        return result;
    }

    public static bool IsMovable(PresenceState state) => state != PresenceState.Idle;

    private sealed class OrderedListComparer : IEqualityComparer<List<PresenceStateOption>>
    {
        public bool Equals(List<PresenceStateOption>? x, List<PresenceStateOption>? y)
        {
            if (ReferenceEquals(x, y))
                return true;

            if (x is null || y is null || x.Count != y.Count)
                return false;

            for (var i = 0; i < x.Count; i++)
            {
                if (!x[i].Equals(y[i]))
                    return false;
            }

            return true;
        }

        public int GetHashCode(List<PresenceStateOption> obj)
        {
            var hash = new HashCode();

            foreach (var option in obj)
                hash.Add(option);

            return hash.ToHashCode();
        }
    }
}
