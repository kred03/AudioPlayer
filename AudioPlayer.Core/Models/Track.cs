namespace AudioPlayer.Core.Models;

public sealed class Track
{
    public required string FilePath { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Artist { get; init; } = string.Empty;

    public string Album { get; init; } = string.Empty;

    public uint Year { get; init; }

    public string Genre { get; init; } = string.Empty;

    public TimeSpan Duration { get; init; }

    public byte[]? CoverImageData { get; init; }

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title)
        ? Path.GetFileNameWithoutExtension(FilePath)
        : Title;

    public string DurationText => Duration <= TimeSpan.Zero
        ? "—"
        : Duration.TotalHours >= 1
            ? Duration.ToString(@"h\:mm\:ss")
            : Duration.ToString(@"m\:ss");

    public string YearText => Year == 0 ? "—" : Year.ToString();
}
