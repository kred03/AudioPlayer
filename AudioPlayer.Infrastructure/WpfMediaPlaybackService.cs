using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using AudioPlayer.Core.Models;
using AudioPlayer.Core.Services;

namespace AudioPlayer.Infrastructure;

public sealed class WpfMediaPlaybackService : IAudioPlaybackService
{
    private readonly MediaPlayer _mediaPlayer = new();
    private readonly DispatcherTimer _positionTimer;
    private TaskCompletionSource? _openCompletionSource;
    private Track? _currentTrack;
    private TimeSpan _duration;
    private bool _isPlaying;
    private bool _isDisposed;

    public WpfMediaPlaybackService()
    {
        _mediaPlayer.ScrubbingEnabled = true;
        _mediaPlayer.Volume = 0.7;
        _mediaPlayer.MediaOpened += OnMediaOpened;
        _mediaPlayer.MediaEnded += OnMediaEnded;
        _mediaPlayer.MediaFailed += OnMediaFailed;

        _positionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _positionTimer.Tick += OnPositionTimerTick;
    }

    public event EventHandler? PlaybackEnded;

    public event EventHandler? PositionChanged;

    public event EventHandler<PlaybackFailedEventArgs>? PlaybackFailed;

    public Track? CurrentTrack => _currentTrack;

    public TimeSpan Position => _currentTrack is null
        ? TimeSpan.Zero
        : _mediaPlayer.Position;

    public TimeSpan Duration => _duration;

    public bool IsPlaying => _isPlaying;

    public double Volume
    {
        get => _mediaPlayer.Volume;
        set => _mediaPlayer.Volume = Math.Clamp(value, 0, 1);
    }

    public bool IsMuted
    {
        get => _mediaPlayer.IsMuted;
        set => _mediaPlayer.IsMuted = value;
    }

    public async Task OpenAsync(
        Track track,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(track);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(track.FilePath))
        {
            throw new FileNotFoundException(
                $"Файл «{track.FilePath}» не найден или недоступен.",
                track.FilePath);
        }

        _openCompletionSource?.TrySetCanceled(CancellationToken.None);
        _mediaPlayer.Stop();
        _mediaPlayer.Close();
        _positionTimer.Stop();

        _currentTrack = track;
        _duration = TimeSpan.Zero;
        _isPlaying = false;
        var completionSource = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _openCompletionSource = completionSource;

        using var registration = cancellationToken.Register(() =>
            completionSource.TrySetCanceled(cancellationToken));

        try
        {
            _mediaPlayer.Open(new Uri(track.FilePath, UriKind.Absolute));
            await completionSource.Task;
        }
        finally
        {
            if (ReferenceEquals(_openCompletionSource, completionSource))
            {
                _openCompletionSource = null;
            }
        }
    }

    public void Play()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_currentTrack is null)
        {
            return;
        }

        _mediaPlayer.Play();
        _isPlaying = true;
        _positionTimer.Start();
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Pause()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _mediaPlayer.Pause();
        _isPlaying = false;
        _positionTimer.Stop();
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _mediaPlayer.Stop();
        _isPlaying = false;
        _positionTimer.Stop();
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Seek(TimeSpan position)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_currentTrack is null)
        {
            return;
        }

        var upperBound = _duration > TimeSpan.Zero
            ? _duration
            : position;
        _mediaPlayer.Position = position.Clamp(TimeSpan.Zero, upperBound);
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _positionTimer.Stop();
        _positionTimer.Tick -= OnPositionTimerTick;
        _mediaPlayer.MediaOpened -= OnMediaOpened;
        _mediaPlayer.MediaEnded -= OnMediaEnded;
        _mediaPlayer.MediaFailed -= OnMediaFailed;
        _mediaPlayer.Close();
        _openCompletionSource?.TrySetCanceled();
    }

    private void OnMediaOpened(object? sender, EventArgs e)
    {
        _duration = _mediaPlayer.NaturalDuration.HasTimeSpan
            ? _mediaPlayer.NaturalDuration.TimeSpan
            : TimeSpan.Zero;

        var completionSource = _openCompletionSource;
        _openCompletionSource = null;
        completionSource?.TrySetResult();
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnMediaEnded(object? sender, EventArgs e)
    {
        _isPlaying = false;
        _positionTimer.Stop();
        PositionChanged?.Invoke(this, EventArgs.Empty);
        PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    private void OnMediaFailed(object? sender, ExceptionEventArgs e)
    {
        _isPlaying = false;
        _positionTimer.Stop();

        var exception = e.ErrorException
            ?? new InvalidOperationException("Не удалось воспроизвести аудиофайл.");
        var completionSource = _openCompletionSource;
        _openCompletionSource = null;

        if (completionSource is not null)
        {
            completionSource.TrySetException(exception);
        }
        else
        {
            PlaybackFailed?.Invoke(this, new PlaybackFailedEventArgs(exception));
        }
    }

    private void OnPositionTimerTick(object? sender, EventArgs e) =>
        PositionChanged?.Invoke(this, EventArgs.Empty);
}

internal static class TimeSpanExtensions
{
    public static TimeSpan Clamp(this TimeSpan value, TimeSpan minimum, TimeSpan maximum)
    {
        if (value < minimum)
        {
            return minimum;
        }

        return value > maximum ? maximum : value;
    }
}
