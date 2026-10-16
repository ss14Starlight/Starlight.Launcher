using Avalonia.Threading;
using Serilog;

namespace Starlight.Launcher.Services.Logging;

internal static class GlobalExceptionHandlers
{
    private static bool _installed;

    public static void Install()
    {
        if (_installed)
            return;
        _installed = true;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                Log.Fatal(ex, "Unhandled exception (terminating: {IsTerminating})", e.IsTerminating);
            else
                Log.Fatal("Unhandled non-CLR exception (terminating: {IsTerminating}): {Exception}", e.IsTerminating, e.ExceptionObject);

            if (e.IsTerminating)
                Log.CloseAndFlush();
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };
    }

    public static void InstallDispatcher() =>
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            if (e.Exception is OperationCanceledException &&
                e.Exception.StackTrace?.Contains("Avalonia.FreeDesktop.DBusTrayIconImpl.WatchAsync") == true)
            {
                e.Handled = true;
                return;
            }

            Log.Fatal(e.Exception, "Unhandled exception on the UI thread");
        };
}
