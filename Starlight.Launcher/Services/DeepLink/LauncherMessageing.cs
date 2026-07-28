using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Serilog;

namespace Starlight.Launcher.Services;

public class LauncherMessaging
{
    private LauncherActivationMessage[] _initialMessages = Array.Empty<LauncherActivationMessage>();
    private NamedPipeServerStream? _pipeServer;
    private readonly CancellationTokenSource _pipeServerSelfDestruct = new();
    private Task? _serverTask;

    public bool SendMessagesOrClaim(LauncherActivationMessage[] messages, bool sendAnyway = true)
    {
        var actualPipeName = "Starlight.Launcher.CommandPipe";

        if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { } runtimeDir && !string.IsNullOrEmpty(runtimeDir))
            actualPipeName = Path.Combine(runtimeDir, actualPipeName);
        else if (!OperatingSystem.IsMacOS())
            actualPipeName += "_" + Convert.ToHexString(Encoding.UTF8.GetBytes(Environment.UserName));

        try
        {
            using (var client = new NamedPipeClientStream(".", actualPipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly))
            {
                client.Connect(150);

                using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
                foreach (var message in messages)
                {
                    var json = JsonSerializer.Serialize(message);
                    Console.WriteLine($"IPC: relaying {json} to existing instance");
                    writer.WriteLine(json);
                }
            }
            Console.WriteLine($"IPC: relayed {messages.Length} message(s) to the existing instance");
            return true;
        }
        catch (Exception ex)
        {
            // Must use Console since Serilog isn't wired up yet in pre-init context.
            Console.WriteLine($"IPC: no existing instance reachable ({ex.GetType().Name}), becoming primary");
        }

        try
        {
            _pipeServer = new NamedPipeServerStream(actualPipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        }
        catch (Exception e)
        {
            Console.WriteLine($"IPC: pipe server could not be created: {e}");
        }

        if (sendAnyway)
            _initialMessages = messages;

        return false;
    }

    public void StartServerTask(LauncherCommands lc) => _serverTask = ServerTask(lc);

    public void StopAndWait()
    {
        _pipeServerSelfDestruct.Cancel();
        try { _ = _serverTask?.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { }
    }

    private async Task ServerTask(LauncherCommands lc)
    {
        var token = _pipeServerSelfDestruct.Token;

        foreach (var message in _initialMessages)
        {
            Log.Information("IPC: queueing initial activation message {@Message}", message);
            await lc.QueueMessage(message);
        }

        if (_pipeServer == null) return;

        var reader = new StreamReader(_pipeServer, Encoding.UTF8);
        try
        {
            while (true)
            {
                await _pipeServer.WaitForConnectionAsync(token).ConfigureAwait(false);
                if (token.IsCancellationRequested) break;

                try
                {
                    while (true)
                    {
                        var line = await reader.ReadLineAsync().WaitAsync(token).ConfigureAwait(false);
                        if (line is null) break;

                        Log.Information("IPC: received raw line: {line}", line);

                        LauncherActivationMessage? message;
                        try
                        {
                            message = JsonSerializer.Deserialize<LauncherActivationMessage>(line);
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "IPC: failed to deserialize line, ignoring");
                            continue;
                        }

                        if (message is not null)
                            await lc.QueueMessage(message);
                    }

                    _pipeServer.Disconnect();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    Log.Warning(e, "IPC: exception during a connection");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // fine, we're shutting down
        }
        finally
        {
            await _pipeServer.DisposeAsync();
        }
    }
}
