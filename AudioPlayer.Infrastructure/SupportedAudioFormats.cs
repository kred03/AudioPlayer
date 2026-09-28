using System.IO;

namespace AudioPlayer.Infrastructure;

public static class SupportedAudioFormats
{
    public static IReadOnlySet<string> Extensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3",
            ".wav"
        };

    public static bool Contains(string filePath) =>
        Extensions.Contains(Path.GetExtension(filePath));
}
