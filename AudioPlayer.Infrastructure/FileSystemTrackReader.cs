using System.IO;
using AudioPlayer.Core.Models;
using AudioPlayer.Core.Services;

namespace AudioPlayer.Infrastructure;

public sealed class FileSystemTrackReader : ITrackReader
{
    public Task<IReadOnlyList<Track>> ReadFolderAsync(
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        return Task.Run<IReadOnlyList<Track>>(() =>
        {
            if (!Directory.Exists(folderPath))
            {
                throw new DirectoryNotFoundException(
                    $"Папка «{folderPath}» не найдена или недоступна.");
            }

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false,
                AttributesToSkip = FileAttributes.ReparsePoint
            };

            var tracks = new List<Track>();

            foreach (var filePath in Directory.EnumerateFiles(folderPath, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (SupportedAudioFormats.Contains(filePath))
                {
                    tracks.Add(CreateTrack(filePath));
                }
            }

            return tracks
                .OrderBy(track => track.DisplayTitle, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }, cancellationToken);
    }

    public Task<Track> ReadFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException(
                    $"Файл «{filePath}» не найден или недоступен.",
                    filePath);
            }

            if (!SupportedAudioFormats.Contains(filePath))
            {
                throw new NotSupportedException(
                    $"Формат «{Path.GetExtension(filePath)}» не поддерживается. Доступны MP3 и WAV.");
            }

            return CreateTrack(filePath);
        }, cancellationToken);
    }

    private static Track CreateTrack(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);

        try
        {
            using var mediaFile = TagLib.File.Create(fullPath);
            var tag = mediaFile.Tag;
            var picture = tag.Pictures.FirstOrDefault();

            return new Track
            {
                FilePath = fullPath,
                Title = string.IsNullOrWhiteSpace(tag.Title)
                    ? Path.GetFileNameWithoutExtension(fullPath)
                    : tag.Title,
                Artist = tag.Performers.FirstOrDefault() ?? string.Empty,
                Album = tag.Album ?? string.Empty,
                Year = tag.Year,
                Genre = tag.Genres.FirstOrDefault() ?? string.Empty,
                Duration = mediaFile.Properties.Duration,
                CoverImageData = picture?.Data.Data
            };
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new Track
            {
                FilePath = fullPath,
                Title = Path.GetFileNameWithoutExtension(fullPath)
            };
        }
    }
}
