using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace AudioPlayer.UI;

public partial class SettingsWindow : Window
{
    public SettingsWindow(
        string? defaultFolder,
        double volumePercent,
        string themeName)
    {
        InitializeComponent();
        DefaultFolderTextBox.Text = defaultFolder ?? string.Empty;
        VolumeSlider.Value = Math.Clamp(volumePercent, 0, 100);
        ThemeComboBox.SelectedValue = string.Equals(
            themeName,
            "Light",
            StringComparison.OrdinalIgnoreCase)
            ? "Light"
            : "Dark";
    }

    public string? DefaultFolder { get; private set; }

    public double VolumePercent { get; private set; }

    public string ThemeName { get; private set; } = "Dark";

    public event Action<double>? VolumePreviewChanged;

    public event Action<string>? ThemePreviewChanged;

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var currentFolder = DefaultFolderTextBox.Text.Trim();
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку с аудиофайлами",
            InitialDirectory = Directory.Exists(currentFolder)
                ? currentFolder
                : string.Empty
        };

        if (dialog.ShowDialog(this) == true)
        {
            DefaultFolderTextBox.Text = dialog.FolderName;
        }
    }

    private void OnClearFolderClick(object sender, RoutedEventArgs e) =>
        DefaultFolderTextBox.Clear();

    private void OnVolumeChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e) =>
        VolumePreviewChanged?.Invoke(e.NewValue);

    private void OnThemeSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (ThemeComboBox.SelectedValue is string themeName)
        {
            ThemePreviewChanged?.Invoke(themeName);
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var folder = DefaultFolderTextBox.Text.Trim();

        if (folder.Length > 0 && !Directory.Exists(folder))
        {
            MessageBox.Show(
                this,
                "Указанная папка не существует или недоступна.",
                "Настройки",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        DefaultFolder = folder.Length == 0 ? null : Path.GetFullPath(folder);
        VolumePercent = VolumeSlider.Value;
        ThemeName = ThemeComboBox.SelectedValue as string ?? "Dark";
        DialogResult = true;
    }
}
