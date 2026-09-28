using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AudioPlayer.Infrastructure;
using AudioPlayer.UI.Services;
using AudioPlayer.UI.ViewModels;

namespace AudioPlayer.UI;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "WPF Window disposes the view model from its Closed event.")]
public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private bool _isSavingBeforeClose;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();

        var themeService = new ThemeService();
        _viewModel = new MainWindowViewModel(
            new FileSystemTrackReader(),
            new WpfMediaPlaybackService(),
            new PlaylistStorageService(),
            new JsonSettingsService(),
            new FileDialogService(),
            themeService);

        DataContext = _viewModel;
        Loaded += OnWindowLoaded;
        Closing += OnWindowClosing;
        Closed += OnWindowClosed;
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnWindowLoaded;
        await _viewModel.InitializeAsync();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        var containsSupportedPath =
            e.Data.GetData(DataFormats.FileDrop) is string[] paths &&
            paths.Any(path =>
                Directory.Exists(path) || SupportedAudioFormats.Contains(path));

        e.Effects = containsSupportedPath
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            await _viewModel.AddDroppedPathsAsync(paths);
        }
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    private async void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var originalVolume = _viewModel.VolumePercent;
        var originalTheme = _viewModel.ThemeName;
        var settingsWindow = new SettingsWindow(
            _viewModel.DefaultFolder,
            _viewModel.VolumePercent,
            _viewModel.ThemeName)
        {
            Owner = this
        };
        settingsWindow.VolumePreviewChanged += volume =>
            _viewModel.VolumePercent = volume;
        settingsWindow.ThemePreviewChanged += themeName =>
            _viewModel.PreviewTheme(themeName);

        if (settingsWindow.ShowDialog() == true)
        {
            await _viewModel.ApplySettingsAsync(
                settingsWindow.DefaultFolder,
                settingsWindow.VolumePercent,
                settingsWindow.ThemeName);
        }
        else
        {
            _viewModel.VolumePercent = originalVolume;
            _viewModel.PreviewTheme(originalTheme);
        }
    }

    private async void OnThemeClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string themeName })
        {
            await _viewModel.ApplyThemeAsync(themeName);
        }
    }

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "AudioPlayer\nC# 14, .NET 10 и WPF\n\nПоддерживаются MP3, WAV, JSON и M3U.",
            "О программе",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void OnPlaylistDoubleClick(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not DataGrid dataGrid ||
            e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(dataGrid, source) is not DataGridRow)
        {
            return;
        }

        await _viewModel.PlaySelectedFromListAsync();
    }

    private async void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;

        if (_isSavingBeforeClose)
        {
            return;
        }

        _isSavingBeforeClose = true;
        await _viewModel.SaveSessionAsync();
        _allowClose = true;
        Close();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        Closing -= OnWindowClosing;
        Closed -= OnWindowClosed;
        _viewModel.Dispose();
    }
}
