namespace AudioPlayer.Core.Services;

public sealed class PlaybackFailedEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception;
}
