using AudioPlayer.Core.Models;

namespace AudioPlayer.Core.Services;

public interface ITrackReader
{
    Task<IReadOnlyList<Track>> ReadFolderAsync(
        string folderPath,
        CancellationToken cancellationToken = default);

    Task<Track> ReadFileAsync(
        string filePath,
        CancellationToken cancellationToken = default);
}
