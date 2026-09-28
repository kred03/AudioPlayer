namespace AudioPlayer.UI.Services;

public interface IFileDialogService
{
    IReadOnlyList<string> ShowOpenAudioFilesDialog(string? initialDirectory = null);

    string? ShowOpenFolderDialog(string? initialDirectory = null);

    string? ShowOpenPlaylistDialog(string? initialDirectory = null);

    string? ShowSavePlaylistDialog(string? initialDirectory = null);
}
