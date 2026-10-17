namespace Starlight.Launcher.WebUI.Models.Journal;

public sealed class PlaySession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Address { get; set; } = "";

    public string? ServerName { get; set; }

    public DateTime StartUtc { get; set; }

    public DateTime? EndUtc { get; set; }

    public List<PlayedRound> Rounds { get; set; } = [];

    public TimeSpan Duration => (EndUtc ?? DateTime.UtcNow) - StartUtc;
}

public sealed record PlayedRound(int? RoundId, string? Map, string? Preset, DateTime SeenUtc);
