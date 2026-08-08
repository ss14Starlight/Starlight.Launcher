using System.Text;
using System.Text.RegularExpressions;
using Serilog;
using Starlight.Launcher.Models.Logging;
using Starlight.Launcher.Services.Settings;
using Starlight.Launcher.WebUI.Models.Logging;

namespace Starlight.Launcher.Services.Logging;

public sealed partial class ClientLogManager(SettingsService settings)
{
    private const string FilePrefix = "client";

    private readonly SemaphoreSlim _allocLock = new(1, 1);

    [GeneratedRegex(@"^client-launch-(\d+)\.", RegexOptions.IgnoreCase)]
    private static partial Regex LaunchNumberRegex { get; }

    public string ResolveDirectory()
    {
        var s = settings.GetSettings();
        var custom = s.ClientLogDirectory;

        if (string.IsNullOrWhiteSpace(custom))
            return s.DirClientLogsDefault;

        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(custom.Trim()));
    }

    public async Task<ClientLogSession> BeginSessionAsync(
        ClientLogContext context,
        CancellationToken cancel = default)
    {
        var s = settings.GetSettings();
        var dir = ResolveDirectory();
        var combine = s.ClientLogCombineStreams;

        string baseName;

        await _allocLock.WaitAsync(cancel);
        try
        {
            _ = Directory.CreateDirectory(dir);

            baseName = s.ClientLogSplitMode switch
            {
                ClientLogSplitMode.Date => $"{FilePrefix}-{DateTime.Now:yyyy-MM-dd}",
                ClientLogSplitMode.Launch => $"{FilePrefix}-launch-{NextLaunchNumber(dir)}",
                _ => FilePrefix,
            };

            TryCleanup(dir, s.ClientLogRetainFiles);
        }
        finally
        {
            _ = _allocLock.Release();
        }

        var stdoutPath = Path.Combine(dir, combine ? $"{baseName}.log" : $"{baseName}.stdout.log");
        var stdoutTarget = ClientLogTarget.OpenAppend(stdoutPath);

        ClientLogTarget? stderrTarget = null;
        if (!combine)
            stderrTarget = ClientLogTarget.OpenAppend(Path.Combine(dir, $"{baseName}.stderr.log"));

        await stdoutTarget.WriteTextAsync(BuildBanner(context, combine ? "stdout+stderr" : "stdout"), cancel);
        if (stderrTarget != null)
            await stderrTarget.WriteTextAsync(BuildBanner(context, "stderr"), cancel);

        Log.Debug("Client logs for pid {Pid}: {Path}", context.ProcessId, stdoutPath);

        return new ClientLogSession(stdoutTarget, stderrTarget);
    }

    private static int NextLaunchNumber(string dir)
    {
        var max = 0;

        foreach (var path in Directory.EnumerateFiles(dir, $"{FilePrefix}-launch-*.log"))
        {
            var m = LaunchNumberRegex.Match(Path.GetFileName(path));
            if (m.Success && int.TryParse(m.Groups[1].ValueSpan, out var n) && n > max)
                max = n;
        }

        return max + 1;
    }

    private static void TryCleanup(string dir, int retain)
    {
        if (retain <= 0)
            return;

        try
        {
            var stale = new DirectoryInfo(dir)
                .GetFiles($"{FilePrefix}*.log")
                .GroupBy(f => StripStreamSuffix(f.Name), StringComparer.OrdinalIgnoreCase)
                .Select(g => (Group: g, Touched: g.Max(f => f.LastWriteTimeUtc)))
                .OrderByDescending(x => x.Touched)
                .Skip(retain);

            foreach (var (group, _) in stale)
            {
                foreach (var file in group)
                {
                    try
                    {
                        file.Delete();
                        Log.Debug("Deleted old client log {Name}", file.Name);
                    }
                    catch (Exception e)
                    {
                        Log.Warning(e, "Failed to delete old client log {Name}", file.Name);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Log.Warning(e, "Client log cleanup failed");
        }
    }

    private static string StripStreamSuffix(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        if (name.EndsWith(".stdout", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".stderr", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^7];
        }

        return name;
    }

    private static string BuildBanner(ClientLogContext context, string streamName)
    {
        const int Width = 100;
        var line = new string('=', Width);
        var sb = new StringBuilder();

        _ = sb.Append("\n\n\n")
              .Append(line).Append('\n')
              .Append("=== NEW LAUNCH  ").Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz")).Append('\n')
              .Append("=== stream: ").Append(streamName).Append('\n');

        if (context.ProcessId is { } pid)
            _ = sb.Append("=== pid: ").Append(pid).Append('\n');

        if (!string.IsNullOrWhiteSpace(context.Target))
            _ = sb.Append("=== target: ").Append(context.Target).Append('\n');

        if (!string.IsNullOrWhiteSpace(context.EngineVersion))
            _ = sb.Append("=== engine: ").Append(context.EngineVersion).Append('\n');

        return sb.Append(line).Append("\n\n").ToString();
    }
}

public readonly record struct ClientLogContext(
    int? ProcessId = null,
    string? Target = null,
    string? EngineVersion = null);
