namespace Starlight.Launcher.WebUI.Models.Logging;

public enum ClientLogSplitMode
{
    /// <summary>
    /// Old format as vanilla SS14 launcher does it: client.stdout.log and client.stderr.log.
    /// </summary>
    Single = 0,

    /// <summary>
    /// Splitting logs by date, e.g. client-2024-06-01.stdout.log and client-2024-06-01.stderr.log.
    /// </summary>
    Date = 1,

    /// <summary>
    /// Splitting logs by launch, e.g. client-launch-1.stdout.log and client-launch-2.stderr.log for example.
    /// </summary>
    Launch = 2,
}
