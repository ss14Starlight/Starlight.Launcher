namespace Starlight.Launcher.WebUI.Services;

/// <summary>
///     Once-a-second tick for live UI values (round timers etc.). Runs only while something listens
///     and the UI is visible.
/// </summary>
public sealed class UiTicker : IDisposable
{
    private readonly object _lock = new();
    private readonly UiActivity _activity;
    private System.Timers.Timer? _timer;
    private Action? _tick;
    private int _subscribers;

    public UiTicker(UiActivity activity)
    {
        _activity = activity;
        _activity.Changed += OnActivityChanged;
    }

    public event Action Tick
    {
        add
        {
            lock (_lock)
            {
                _tick += value;
                _subscribers++;
                UpdateTimer();
            }
        }
        remove
        {
            lock (_lock)
            {
                _tick -= value;
                if (--_subscribers < 0)
                    _subscribers = 0;
                UpdateTimer();
            }
        }
    }

    private void OnActivityChanged(bool active)
    {
        lock (_lock)
            UpdateTimer();

        if (active)
            OnElapsed(null, null);
    }

    private void UpdateTimer()
    {
        var shouldRun = _subscribers > 0 && _activity.IsActive;

        if (shouldRun && _timer is null)
            Start();
        else if (!shouldRun && _timer is not null)
            Stop();
    }

    private void Start()
    {
        _timer = new System.Timers.Timer(1000) { AutoReset = true };
        _timer.Elapsed += OnElapsed;
        _timer.Start();
    }

    private void Stop()
    {
        if (_timer is null)
            return;

        _timer.Elapsed -= OnElapsed;
        _timer.Stop();
        _timer.Dispose();
        _timer = null;
    }

    private void OnElapsed(object? sender, System.Timers.ElapsedEventArgs? e)
    {
        var handlers = _tick;
        if (handlers is null)
            return;

        foreach (var handler in handlers.GetInvocationList())
        {
            try { ((Action)handler)(); }
            catch { }
        }
    }

    public void Dispose()
    {
        _activity.Changed -= OnActivityChanged;

        lock (_lock)
        {
            _tick = null;
            _subscribers = 0;
            Stop();
        }
    }
}
