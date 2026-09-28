using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using AudioPlayer.Core.Models;
using AudioPlayer.Core.Services;

namespace AudioPlayer.Infrastructure;

public sealed class PlaylistStorageService : IPlaylistStorageService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public Task SaveAsync(
        string filePath,
        IReadOnlyCollection<Track> tracks,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(tracks);

        return string.Equals(Path.GetExtension(filePath), ".m3u", StringComparison.OrdinalIgnoreCase)
            ? SaveM3uAsync(filePath, tracks, cancellationToken)
            : SaveJsonAsync(filePath, tracks, cancellationToken);
    }

    public Task<IReadOnlyList<string>> LoadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Файл плейлиста не найден.", filePath);
        }

        return string.Equals(Path.GetExtension(filePath), ".m3u", StringComparison.OrdinalIgnoreCase)
            ? LoadM3uAsync(filePath, cancellationToken)
            : LoadJsonAsync(filePath, cancellationToken);
    }

    private static async Task SaveJsonAsync(
        string filePath,
        IReadOnlyCollection<Track> tracks,
        CancellationToken cancellationToken)
    {
        var document = new PlaylistDocument
        {
            Tracks = tracks.Select(track => track.FilePath).ToList()
        };

        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(
            stream,
            document,
            SerializerOptions,
            cancellationToken);
    }

    private static async Task SaveM3uAsync(
        string filePath,
        IReadOnlyCollection<Track> tracks,
        CancellationToken cancellationToken)
    {
        var lines = new List<string> { "#EXTM3U" };

        foreach (var track in tracks)
        {
            var duration = (int)Math.Round(track.Duration.TotalSeconds);
            lines.Add($"#EXTINF:{duration.ToString(CultureInfo.InvariantCulture)},{track.DisplayTitle}");
            lines.Add(track.FilePath);
        }

        await File.WriteAllLinesAsync(
            filePath,
            lines,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
    }

    private static async Task<IReadOnlyList<string>> LoadJsonAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        var document = await JsonSerializer.DeserializeAsync<PlaylistDocument>(
            stream,
            SerializerOptions,
            cancellationToken);

        if (document?.Tracks is null)
        {
            throw new InvalidDataException("Некорректный JSON-плейлист.");
        }

        return ResolvePaths(filePath, document.Tracks);
    }

    private static async Task<IReadOnlyList<string>> LoadM3uAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
        var trackPaths = lines
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'));
        return ResolvePaths(filePath, trackPaths);
    }

    private static string[] ResolvePaths(
        string playlistPath,
        IEnumerable<string> trackPaths)
    {
        var playlistFolder = Path.GetDirectoryName(Path.GetFullPath(playlistPath))
                             ?? Environment.CurrentDirectory;

        return trackPaths
            .Select(path => Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(playlistFolder, path)))
            .ToArray();
    }

    private sealed class PlaylistDocument
    {
        public int Version { get; init; } = 1;

        public List<string> Tracks { get; init; } = [];
    }
}
