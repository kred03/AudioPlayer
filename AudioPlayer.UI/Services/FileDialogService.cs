using System.IO;
using Microsoft.Win32;

namespace AudioPlayer.UI.Services;

public sealed class FileDialogService : IFileDialogService
{
    private const string AudioFilter =
        "Поддерживаемые аудиофайлы (*.mp3;*.wav)|*.mp3;*.wav|" +
        "MP3 (*.mp3)|*.mp3|WAV (*.wav)|*.wav";

    private const string PlaylistFilter =
        "Плейлисты (*.json;*.m3u)|*.json;*.m3u|" +
        "JSON (*.json)|*.json|M3U (*.m3u)|*.m3u";

    private const string PlaylistSaveFilter =
        "JSON (*.json)|*.json|M3U (*.m3u)|*.m3u";

    public IReadOnlyList<string> ShowOpenAudioFilesDialog(string? initialDirectory = null)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите аудиофайлы",
            Filter = AudioFilter,
            Multiselect = true,
            CheckFileExists = true,
            InitialDirectory = GetExistingDirectory(initialDirectory)
        };

        return dialog.ShowDialog() == true
            ? dialog.FileNames
            : Array.Empty<string>();
    }

    public string? ShowOpenFolderDialog(string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку с аудиофайлами",
            Multiselect = false,
            InitialDirectory = GetExistingDirectory(initialDirectory)
        };

        return dialog.ShowDialog() == true
            ? dialog.FolderName
            : null;
    }

    public string? ShowOpenPlaylistDialog(string? initialDirectory = null)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Открыть плейлист",
            Filter = PlaylistFilter,
            CheckFileExists = true,
            InitialDirectory = GetExistingDirectory(initialDirectory)
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ShowSavePlaylistDialog(string? initialDirectory = null)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Сохранить плейлист",
            Filter = PlaylistSaveFilter,
            DefaultExt = ".json",
            AddExtension = true,
            FileName = "playlist",
            InitialDirectory = GetExistingDirectory(initialDirectory)
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static string GetExistingDirectory(string? directoryPath) =>
        !string.IsNullOrWhiteSpace(directoryPath) && Directory.Exists(directoryPath)
            ? directoryPath
            : string.Empty;
}
