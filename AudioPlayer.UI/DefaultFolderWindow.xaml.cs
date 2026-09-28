using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace AudioPlayer.UI;

public partial class DefaultFolderWindow : Window
{
    public DefaultFolderWindow(string? defaultFolder)
    {
        InitializeComponent();
        DefaultFolderTextBox.Text = defaultFolder ?? string.Empty;
    }

    public string? DefaultFolder { get; private set; }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку с аудиофайлами",
            InitialDirectory = Directory.Exists(DefaultFolderTextBox.Text)
                ? DefaultFolderTextBox.Text
                : string.Empty
        };

        if (dialog.ShowDialog(this) == true)
        {
            DefaultFolderTextBox.Text = dialog.FolderName;
        }
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        DefaultFolder = null;
        DialogResult = true;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var folder = DefaultFolderTextBox.Text.Trim();

        if (folder.Length > 0 && !Directory.Exists(folder))
        {
            MessageBox.Show(
                this,
                "Указанная папка не существует или недоступна.",
                "Папка по умолчанию",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        DefaultFolder = folder.Length == 0 ? null : Path.GetFullPath(folder);
        DialogResult = true;
    }
}
