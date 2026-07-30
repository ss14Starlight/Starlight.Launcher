using Starlight.Launcher.WebUI.Models.Settings;

namespace Starlight.Launcher.Services.Settings;

public sealed partial class SettingsService
{
    private readonly List<ISettingsSubscription> _settingsSubs = new();
    private readonly object _settingsSubsLock = new();

    public IDisposable Subscribe<T>(
        Func<AppSettings, T> selector,
        Action<T> onChanged,
        bool fireImmediately = false,
        IEqualityComparer<T>? comparer = null)
    {
        var sub = new SettingsSubscription<T>(selector, onChanged, comparer ?? EqualityComparer<T>.Default);

        lock (_settingsSubsLock)
            _settingsSubs.Add(sub);

        if (fireImmediately)
            onChanged(selector(GetSettings()));

        return new Unsubscriber(this, sub);
    }

    private void NotifySettingsChanged(AppSettings oldSettings, AppSettings newSettings)
    {
        if (ReferenceEquals(oldSettings, newSettings))
            return;

        ISettingsSubscription[] snapshot;
        lock (_settingsSubsLock)
            snapshot = _settingsSubs.ToArray();

        foreach (var sub in snapshot)
            sub.NotifyIfChanged(oldSettings, newSettings);
    }

    private interface ISettingsSubscription
    {
        void NotifyIfChanged(AppSettings oldSettings, AppSettings newSettings);
    }

    private sealed class SettingsSubscription<T>(
        Func<AppSettings, T> selector,
        Action<T> onChanged,
        IEqualityComparer<T> comparer) : ISettingsSubscription
    {
        public void NotifyIfChanged(AppSettings oldSettings, AppSettings newSettings)
        {
            var oldVal = selector(oldSettings);
            var newVal = selector(newSettings);
            if (!comparer.Equals(oldVal, newVal))
                onChanged(newVal);
        }
    }

    private sealed class Unsubscriber(SettingsService owner, ISettingsSubscription sub) : IDisposable
    {
        private ISettingsSubscription? _sub = sub;

        public void Dispose()
        {
            var s = Interlocked.Exchange(ref _sub, null);
            if (s is null) return;
            lock (owner._settingsSubsLock)
                _ = owner._settingsSubs.Remove(s);
        }
    }
}
