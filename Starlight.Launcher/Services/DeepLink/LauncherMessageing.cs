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
    private static readonly Encoding _noBomUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private Task? _serverTask;
    private string _pipeName = "";
    private const int ConnectAttempts = 5;
    private const int ConnectTimeoutMs = 500;

    public bool SendMessagesOrClaim(LauncherActivationMessage[] messages, bool sendAnyway = true)
    {
        var actualPipeName = "Starlight.Launcher.CommandPipe";

        if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { } runtimeDir && !string.IsNullOrEmpty(runtimeDir))
            actualPipeName = Path.Combine(runtimeDir, actualPipeName);
        else if (!OperatingSystem.IsMacOS())
            actualPipeName += "_" + Convert.ToHexString(Encoding.UTF8.GetBytes(Environment.UserName));

        _pipeName = actualPipeName;

        for (var attempt = 1; attempt <= ConnectAttempts; attempt++)
        {
            if (TrySend(actualPipeName, messages))
                return true;

            if (TryCreateServer(out var error))
                break;

            Console.WriteLine($"IPC: an instance owns the pipe but did not answer (attempt {attempt}): {error?.GetType().Name}");
            if (attempt == ConnectAttempts)
                Console.WriteLine("IPC: giving up on the existing instance, becoming primary without a pipe");
        }

        if (sendAnyway)
            _initialMessages = messages;

        return false;
    }

    private static bool TrySend(string pipeName, LauncherActivationMessage[] messages)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            client.Connect(ConnectTimeoutMs);

            using var writer = new StreamWriter(client, _noBomUtf8, leaveOpen: true) { AutoFlush = true };
            foreach (var message in messages)
            {
                var json = JsonSerializer.Serialize(message);
                Console.WriteLine($"IPC: relaying {json} to existing instance");
                writer.WriteLine(json);
            }

            Console.WriteLine($"IPC: relayed {messages.Length} message(s) to the existing instance");
            return true;
        }
        catch (Exception ex)
        {
            // Must use Console since Serilog isn't wired up yet in pre-init context.
            Console.WriteLine($"IPC: no existing instance reachable ({ex.GetType().Name})");
            return false;
        }
    }

    private bool TryCreateServer(out Exception? error)
    {
        try
        {
            _pipeServer = CreateServer(_pipeName);
            error = null;
            return true;
        }
        catch (Exception e)
        {
            error = e;
            return false;
        }
    }

    private static NamedPipeServerStream CreateServer(string pipeName)
        => new(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    public void StartServerTask(LauncherCommands lc) => _serverTask = ServerTask(lc);

    public void StopAndWait()
    {
        _pipeServerSelfDestruct.Cancel();

        _pipeServer?.Dispose();

        if (_serverTask == null)
            return;

        try
        {
            _serverTask.Wait();
        }
        catch (OperationCanceledException)
        {
        }
        catch (AggregateException ex) when (
            ex.InnerExceptions.All(e => e is OperationCanceledException or ObjectDisposedException))
        {
        }
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

        try
        {
            while (true)
            {
                try
                {
                    await _pipeServer.WaitForConnectionAsync(token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is IOException or InvalidOperationException or ObjectDisposedException && !token.IsCancellationRequested)
                {
                    Log.Warning(e, "IPC: pipe server broke, recreating it");
                    await RecreateServerAsync(token).ConfigureAwait(false);
                    continue;
                }

                if (token.IsCancellationRequested) break;

                var reader = new StreamReader(_pipeServer, _noBomUtf8, detectEncodingFromByteOrderMarks: true);

                try
                {
                    while (true)
                    {
                        var line = await reader.ReadLineAsync().WaitAsync(token).ConfigureAwait(false);
                        if (line is null) break;

                        line = line.TrimStart('\uFEFF');

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
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    Log.Warning(e, "IPC: exception during a connection");
                }

                // always free the single pipe instance, or the next WaitForConnectionAsync throws
                try
                {
                    if (_pipeServer.IsConnected)
                        _pipeServer.Disconnect();
                }
                catch (Exception e) when (e is IOException or InvalidOperationException or ObjectDisposedException)
                {
                    Log.Warning(e, "IPC: could not disconnect the pipe, recreating it");
                    await RecreateServerAsync(token).ConfigureAwait(false);
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

    private async Task RecreateServerAsync(CancellationToken token)
    {
        if (_pipeServer != null)
            await _pipeServer.DisposeAsync().ConfigureAwait(false);

        while (true)
        {
            try
            {
                _pipeServer = CreateServer(_pipeName);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Warning(e, "IPC: could not recreate the pipe server, retrying");
                await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
            }
        }
    }
}
