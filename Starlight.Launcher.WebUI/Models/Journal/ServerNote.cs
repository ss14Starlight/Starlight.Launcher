namespace Starlight.Launcher.WebUI.Models.Journal;

public sealed record ServerNote(string Address)
{
    public string? Name { get; init; }

    public string Note { get; init; } = "";

    public int Rating { get; init; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Note) && Rating == 0;
}
