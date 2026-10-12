namespace Starlight.Launcher.WebUI.Services;

/// <summary>
///     Whether anyone can see the UI. Goes inactive while the window is minimized or hidden in the tray,
///     so UI-only work (tickers, polling, re-renders) can stop instead of feeding a frozen WebView.
/// </summary>
public sealed class UiActivity
{
    private readonly object _lock = new();
    private TaskCompletionSource _activated = CompletedSource();

    public bool IsActive { get; private set; } = true;

    /// <summary>
    ///     Raised with the new state, on whatever thread changed it.
    /// </summary>
    public event Action<bool>? Changed;

    public void SetActive(bool active)
    {
        TaskCompletionSource? toComplete = null;

        lock (_lock)
        {
            if (IsActive == active)
                return;

            IsActive = active;

            if (active)
                toComplete = _activated;
            else
                _activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        _ = toComplete?.TrySetResult();
        Changed?.Invoke(active);
    }

    /// <summary>
    ///     Completes right away while active, otherwise once the UI becomes visible again.
    /// </summary>
    public Task WaitUntilActiveAsync(CancellationToken cancel = default)
    {
        Task task;
        lock (_lock)
            task = _activated.Task;

        return task.IsCompleted ? Task.CompletedTask : task.WaitAsync(cancel);
    }

    private static TaskCompletionSource CompletedSource()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tcs.SetResult();
        return tcs;
    }
}
