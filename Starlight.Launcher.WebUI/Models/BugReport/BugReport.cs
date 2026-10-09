namespace Starlight.Launcher.WebUI.Models.BugReport;

public enum BugReportTarget
{
    Launcher,

    Game
}

public sealed record BugReportTargets(bool Launcher, string[] Projects);

public sealed record BugReportDraft(
    BugReportTarget Target,
    string? Project,
    string Title,
    string Description,
    string? ServerAddress,
    bool IncludeSystemInfo);

public enum BugReportStatus
{
    Sent,

    NotLinked,

    Unauthorized,

    RateLimited,

    Invalid,

    Unavailable
}

public sealed record BugReportResult(BugReportStatus Status, TimeSpan? RetryAfter = null, string? Error = null);

public static class BugReportLimits
{
    public const int MinTitleLength = 5;
    public const int MaxTitleLength = 100;
    public const int MinDescriptionLength = 10;
    public const int MaxDescriptionLength = 4000;
}
