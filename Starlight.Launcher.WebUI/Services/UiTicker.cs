namespace Starlight.Launcher.WebUI.Services;

public sealed class UiTicker : IDisposable
{
    private readonly object _lock = new();
    private System.Timers.Timer? _timer;
    private Action? _tick;
    private int _subscribers;

    public event Action Tick
    {
        add
        {
            lock (_lock)
            {
                _tick += value;
                if (++_subscribers == 1)
                    Start();
            }
        }
        remove
        {
            lock (_lock)
            {
                _tick -= value;
                if (--_subscribers <= 0)
                {
                    _subscribers = 0;
                    Stop();
                }
            }
        }
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

    private void OnElapsed(object? sender, System.Timers.ElapsedEventArgs e)
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
        lock (_lock)
        {
            _tick = null;
            _subscribers = 0;
            Stop();
        }
    }
}
