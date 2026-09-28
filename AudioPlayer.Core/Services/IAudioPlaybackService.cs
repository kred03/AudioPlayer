using AudioPlayer.Core.Models;

namespace AudioPlayer.Core.Services;

public interface IAudioPlaybackService : IDisposable
{
    event EventHandler? PlaybackEnded;

    event EventHandler? PositionChanged;

    event EventHandler<PlaybackFailedEventArgs>? PlaybackFailed;

    Track? CurrentTrack { get; }

    TimeSpan Position { get; }

    TimeSpan Duration { get; }

    bool IsPlaying { get; }

    double Volume { get; set; }

    bool IsMuted { get; set; }

    Task OpenAsync(Track track, CancellationToken cancellationToken = default);

    void Play();

    void Pause();

    void Stop();

    void Seek(TimeSpan position);
}
