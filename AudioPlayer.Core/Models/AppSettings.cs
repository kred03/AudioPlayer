namespace AudioPlayer.Core.Models;

public sealed class AppSettings
{
    public string? DefaultFolder { get; set; }

    public double VolumePercent { get; set; } = 70;

    public string? LastTrackPath { get; set; }

    public string Theme { get; set; } = "Dark";

    public RepeatMode RepeatMode { get; set; }

    public bool IsShuffleEnabled { get; set; }
}
