using AudioPlayer.Core.Models;

namespace AudioPlayer.Core.Services;

public interface IPlaylistStorageService
{
    Task SaveAsync(
        string filePath,
        IReadOnlyCollection<Track> tracks,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> LoadAsync(
        string filePath,
        CancellationToken cancellationToken = default);
}
