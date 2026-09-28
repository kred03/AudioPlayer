using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AudioPlayer.Core.Models;
using AudioPlayer.Core.Services;
using AudioPlayer.Infrastructure;
using AudioPlayer.UI.Services;

namespace AudioPlayer.UI.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly ITrackReader _trackReader;
    private readonly IAudioPlaybackService _playbackService;
    private readonly IPlaylistStorageService _playlistStorageService;
    private readonly ISettingsService _settingsService;
    private readonly IFileDialogService _fileDialogService;
    private readonly IThemeService _themeService;
    private readonly Dispatcher _dispatcher;
    private readonly object _folderWatcherLock = new();
    private readonly HashSet<string> _knownPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Track> _shufflePool = [];
    private readonly List<Track> _playHistory = [];
    private Track? _selectedTrack;
    private Track? _currentTrack;
    private ImageSource? _currentCoverImage;
    private string _statusMessage = "Перетащите сюда MP3/WAV или откройте файл";
    private string? _defaultFolder;
    private string _themeName = "Dark";
    private double _positionSeconds;
    private double _durationSeconds;
    private double _volumePercent = 70;
    private bool _isPlaying;
    private bool _isMuted;
    private RepeatMode _repeatMode;
    private bool _isShuffleEnabled;
    private bool _isUpdatingPosition;
    private bool _isInitialized;
    private bool _isDisposed;
    private FileSystemWatcher? _folderWatcher;
    private CancellationTokenSource? _folderRescanCancellation;
    private CancellationTokenSource? _playbackRequestCancellation;
    private int _playbackRequestVersion;

    public MainWindowViewModel(
        ITrackReader trackReader,
        IAudioPlaybackService playbackService,
        IPlaylistStorageService playlistStorageService,
        ISettingsService settingsService,
        IFileDialogService fileDialogService,
        IThemeService themeService)
    {
        _trackReader = trackReader;
        _playbackService = playbackService;
        _playlistStorageService = playlistStorageService;
        _settingsService = settingsService;
        _fileDialogService = fileDialogService;
        _themeService = themeService;
        _dispatcher = Dispatcher.CurrentDispatcher;

        OpenFilesCommand = new AsyncRelayCommand(OpenFilesAsync);
        OpenFolderCommand = new AsyncRelayCommand(OpenFolderAsync);
        LoadPlaylistCommand = new AsyncRelayCommand(LoadPlaylistAsync);
        SavePlaylistCommand = new AsyncRelayCommand(
            SavePlaylistAsync,
            () => Tracks.Count > 0);
        RemoveSelectedCommand = new RelayCommand(
            RemoveSelected,
            () => SelectedTrack is not null);
        ClearCommand = new RelayCommand(
            Clear,
            () => Tracks.Count > 0);
        PlayCommand = new AsyncRelayCommand(
            PlaySelectedAsync,
            () => Tracks.Count > 0);
        TogglePlayPauseCommand = new AsyncRelayCommand(
            TogglePlayPauseAsync,
            () => Tracks.Count > 0);
        PauseCommand = new RelayCommand(Pause, () => IsPlaying);
        StopCommand = new RelayCommand(Stop, () => CurrentTrack is not null);
        PreviousCommand = new AsyncRelayCommand(
            PlayPreviousAsync,
            () => Tracks.Count > 0);
        NextCommand = new AsyncRelayCommand(
            PlayNextAsync,
            () => Tracks.Count > 0);
        ToggleMuteCommand = new RelayCommand(ToggleMute);
        DisableRepeatCommand = new RelayCommand(
            () => RepeatMode = AudioPlayer.Core.Models.RepeatMode.Off);
        RepeatOneCommand = new RelayCommand(
            () => RepeatMode = AudioPlayer.Core.Models.RepeatMode.One);
        RepeatAllCommand = new RelayCommand(
            () => RepeatMode = AudioPlayer.Core.Models.RepeatMode.All);
        ToggleShuffleCommand = new RelayCommand(ToggleShuffle);

        Tracks.CollectionChanged += OnTracksCollectionChanged;
        _playbackService.PositionChanged += OnPlaybackPositionChanged;
        _playbackService.PlaybackEnded += OnPlaybackEnded;
        _playbackService.PlaybackFailed += OnPlaybackFailed;
        _playbackService.Volume = _volumePercent / 100;
    }

    public ObservableCollection<Track> Tracks { get; } = [];

    public AsyncRelayCommand OpenFilesCommand { get; }

    public AsyncRelayCommand OpenFolderCommand { get; }

    public AsyncRelayCommand LoadPlaylistCommand { get; }

    public AsyncRelayCommand SavePlaylistCommand { get; }

    public RelayCommand RemoveSelectedCommand { get; }

    public RelayCommand ClearCommand { get; }

    public AsyncRelayCommand PlayCommand { get; }

    public AsyncRelayCommand TogglePlayPauseCommand { get; }

    public RelayCommand PauseCommand { get; }

    public RelayCommand StopCommand { get; }

    public AsyncRelayCommand PreviousCommand { get; }

    public AsyncRelayCommand NextCommand { get; }

    public RelayCommand ToggleMuteCommand { get; }

    public RelayCommand DisableRepeatCommand { get; }

    public RelayCommand RepeatOneCommand { get; }

    public RelayCommand RepeatAllCommand { get; }

    public RelayCommand ToggleShuffleCommand { get; }

    public Track? SelectedTrack
    {
        get => _selectedTrack;
        set
        {
            if (SetProperty(ref _selectedTrack, value))
            {
                RemoveSelectedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public Track? CurrentTrack
    {
        get => _currentTrack;
        private set
        {
            if (SetProperty(ref _currentTrack, value))
            {
                CurrentCoverImage = CreateCoverImage(value?.CoverImageData);
                RaisePropertyChanged(nameof(CurrentTitle));
                RaisePropertyChanged(nameof(CurrentSubtitle));
                RaisePropertyChanged(nameof(CurrentMetadataDetails));
                StopCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public ImageSource? CurrentCoverImage
    {
        get => _currentCoverImage;
        private set => SetProperty(ref _currentCoverImage, value);
    }

    public string CurrentTitle => CurrentTrack?.DisplayTitle ?? "Трек не выбран";

    public string CurrentSubtitle
    {
        get
        {
            if (CurrentTrack is null)
            {
                return "Исполнитель — Альбом";
            }

            var artist = string.IsNullOrWhiteSpace(CurrentTrack.Artist)
                ? "Неизвестный исполнитель"
                : CurrentTrack.Artist;
            var album = string.IsNullOrWhiteSpace(CurrentTrack.Album)
                ? "Неизвестный альбом"
                : CurrentTrack.Album;
            return $"{artist} — {album}";
        }
    }

    public string CurrentMetadataDetails
    {
        get
        {
            if (CurrentTrack is null)
            {
                return "Год и жанр появятся после выбора трека";
            }

            var details = new List<string>();

            if (CurrentTrack.Year > 0)
            {
                details.Add(CurrentTrack.Year.ToString(CultureInfo.CurrentCulture));
            }

            if (!string.IsNullOrWhiteSpace(CurrentTrack.Genre))
            {
                details.Add(CurrentTrack.Genre);
            }

            return details.Count > 0
                ? string.Join(" • ", details)
                : "Метаданные отсутствуют";
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string? DefaultFolder
    {
        get => _defaultFolder;
        private set => SetProperty(ref _defaultFolder, value);
    }

    public string ThemeName
    {
        get => _themeName;
        private set
        {
            if (SetProperty(ref _themeName, NormalizeTheme(value)))
            {
                RaisePropertyChanged(nameof(IsDarkTheme));
                RaisePropertyChanged(nameof(IsLightTheme));
            }
        }
    }

    public bool IsDarkTheme => ThemeName == "Dark";

    public bool IsLightTheme => ThemeName == "Light";

    public double PositionSeconds
    {
        get => _positionSeconds;
        set
        {
            var clampedValue = Math.Clamp(value, 0, DurationSeconds);

            if (SetProperty(ref _positionSeconds, clampedValue))
            {
                RaisePropertyChanged(nameof(PlaybackTimeText));

                if (!_isUpdatingPosition && CurrentTrack is not null)
                {
                    _playbackService.Seek(TimeSpan.FromSeconds(clampedValue));
                }
            }
        }
    }

    public double DurationSeconds
    {
        get => _durationSeconds;
        private set
        {
            if (SetProperty(ref _durationSeconds, Math.Max(0, value)))
            {
                RaisePropertyChanged(nameof(PlaybackTimeText));
            }
        }
    }

    public string PlaybackTimeText =>
        $"{FormatTime(TimeSpan.FromSeconds(PositionSeconds))} / " +
        $"{FormatTime(TimeSpan.FromSeconds(DurationSeconds))}";

    public double VolumePercent
    {
        get => _volumePercent;
        set
        {
            if (SetProperty(ref _volumePercent, Math.Clamp(value, 0, 100)))
            {
                _playbackService.Volume = _volumePercent / 100;
                RaisePropertyChanged(nameof(VolumeText));
            }
        }
    }

    public string VolumeText => $"Громкость: {VolumePercent:0}%";

    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (SetProperty(ref _isPlaying, value))
            {
                PauseCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        private set
        {
            if (SetProperty(ref _isMuted, value))
            {
                RaisePropertyChanged(nameof(MuteButtonText));
            }
        }
    }

    public string MuteButtonText => IsMuted ? "Включить звук" : "Выключить звук";

    public RepeatMode RepeatMode
    {
        get => _repeatMode;
        set
        {
            if (SetProperty(ref _repeatMode, value))
            {
                RaisePropertyChanged(nameof(IsRepeatOff));
                RaisePropertyChanged(nameof(IsRepeatOne));
                RaisePropertyChanged(nameof(IsRepeatAll));
                StatusMessage = value switch
                {
                    AudioPlayer.Core.Models.RepeatMode.One => "Будет повторяться текущий трек",
                    AudioPlayer.Core.Models.RepeatMode.All => "Будет повторяться весь плейлист",
                    _ => "Повтор выключен"
                };
            }
        }
    }

    public bool IsRepeatOff => RepeatMode == AudioPlayer.Core.Models.RepeatMode.Off;

    public bool IsRepeatOne => RepeatMode == AudioPlayer.Core.Models.RepeatMode.One;

    public bool IsRepeatAll => RepeatMode == AudioPlayer.Core.Models.RepeatMode.All;

    public bool IsShuffleEnabled
    {
        get => _isShuffleEnabled;
        private set
        {
            if (SetProperty(ref _isShuffleEnabled, value))
            {
                RaisePropertyChanged(nameof(ShuffleButtonText));
            }
        }
    }

    public string ShuffleButtonText => IsShuffleEnabled
        ? "Случайно: вкл."
        : "Случайно: выкл.";

    public string TrackCountText => $"Треков: {Tracks.Count}";

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        var settings = await _settingsService.LoadAsync();

        DefaultFolder = Directory.Exists(settings.DefaultFolder)
            ? settings.DefaultFolder
            : null;
        ThemeName = settings.Theme;
        _themeService.ApplyTheme(ThemeName);
        VolumePercent = settings.VolumePercent;
        RepeatMode = Enum.IsDefined(settings.RepeatMode)
            ? settings.RepeatMode
            : AudioPlayer.Core.Models.RepeatMode.Off;
        IsShuffleEnabled = settings.IsShuffleEnabled;

        ImportResult? folderImportResult = null;

        if (DefaultFolder is not null)
        {
            StatusMessage = $"Сканирование папки: {DefaultFolder}…";
            folderImportResult = await ImportPathsAsync([DefaultFolder]);

            if (folderImportResult.Errors.Count == 0)
            {
                StartWatchingFolder(DefaultFolder);
            }
        }

        Track? restoredTrack = null;

        if (!string.IsNullOrWhiteSpace(settings.LastTrackPath) &&
            File.Exists(settings.LastTrackPath))
        {
            try
            {
                restoredTrack = Tracks.FirstOrDefault(track => string.Equals(
                    track.FilePath,
                    settings.LastTrackPath,
                    StringComparison.OrdinalIgnoreCase));

                if (restoredTrack is null)
                {
                    restoredTrack = await _trackReader.ReadFileAsync(settings.LastTrackPath);
                    AddUniqueTracks([restoredTrack]);
                }

                CurrentTrack = restoredTrack;
                SelectedTrack = restoredTrack;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                StatusMessage = $"Не удалось восстановить последний трек: {exception.Message}";
            }
        }

        if (folderImportResult is not null)
        {
            StatusMessage = folderImportResult.Errors.Count == 0
                ? restoredTrack is null
                    ? $"Папка загружена и отслеживается. Треков: {Tracks.Count}"
                    : $"Папка загружена и отслеживается. Треков: {Tracks.Count}. " +
                      $"Выбран последний трек: {restoredTrack.DisplayTitle}"
                : $"Не удалось загрузить папку: {folderImportResult.Errors[0]}";
        }
        else if (restoredTrack is not null)
        {
            StatusMessage = $"Восстановлен последний трек: {restoredTrack.DisplayTitle}";
        }

        ResetShufflePool();
    }

    public async Task<bool> SaveSessionAsync()
    {
        try
        {
            await _settingsService.SaveAsync(CreateSettings());
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            StatusMessage = $"Не удалось сохранить настройки: {exception.Message}";
            return false;
        }
    }

    public async Task ApplyDefaultFolderAsync(string? defaultFolder)
    {
        DefaultFolder = string.IsNullOrWhiteSpace(defaultFolder)
            ? null
            : Path.GetFullPath(defaultFolder);
        if (await SaveSessionAsync())
        {
            StatusMessage = DefaultFolder is null
                ? "Папка по умолчанию отключена"
                : $"Папка по умолчанию: {DefaultFolder}";
        }
    }

    public async Task ApplySettingsAsync(
        string? defaultFolder,
        double volumePercent,
        string themeName)
    {
        DefaultFolder = string.IsNullOrWhiteSpace(defaultFolder)
            ? null
            : Path.GetFullPath(defaultFolder);
        VolumePercent = volumePercent;
        ThemeName = themeName;
        _themeService.ApplyTheme(ThemeName);

        if (await SaveSessionAsync())
        {
            StatusMessage = "Настройки сохранены";
        }
    }

    public void PreviewTheme(string themeName)
    {
        ThemeName = themeName;
        _themeService.ApplyTheme(ThemeName);
    }

    public async Task ApplyThemeAsync(string themeName)
    {
        ThemeName = themeName;
        _themeService.ApplyTheme(ThemeName);
        if (await SaveSessionAsync())
        {
            StatusMessage = ThemeName == "Light"
                ? "Включена светлая тема"
                : "Включена тёмная тема";
        }
    }

    public async Task AddDroppedPathsAsync(IEnumerable<string> paths)
    {
        var result = await ImportPathsAsync(paths);
        SetImportStatus(result.AddedCount, result.Errors);
    }

    private async Task<ImportResult> ImportPathsAsync(IEnumerable<string> paths)
    {
        var addedCount = 0;
        var errorMessages = new List<string>();

        foreach (var path in paths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    var folderTracks = await _trackReader.ReadFolderAsync(path);
                    addedCount += AddUniqueTracks(folderTracks);
                }
                else
                {
                    var track = await _trackReader.ReadFileAsync(path);
                    addedCount += AddUniqueTracks([track]);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or
                    NotSupportedException or ArgumentException)
            {
                errorMessages.Add(exception.Message);
            }
        }

        return new ImportResult(addedCount, errorMessages);
    }

    public Task PlaySelectedFromListAsync() => PlaySelectedAsync();

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        CancelPendingPlaybackRequest();
        StopWatchingFolder();
        Tracks.CollectionChanged -= OnTracksCollectionChanged;
        _playbackService.PositionChanged -= OnPlaybackPositionChanged;
        _playbackService.PlaybackEnded -= OnPlaybackEnded;
        _playbackService.PlaybackFailed -= OnPlaybackFailed;
        _playbackService.Dispose();
    }

    private async Task OpenFilesAsync()
    {
        var filePaths = _fileDialogService.ShowOpenAudioFilesDialog(DefaultFolder);

        if (filePaths.Count > 0)
        {
            await AddDroppedPathsAsync(filePaths);
        }
    }

    private async Task OpenFolderAsync()
    {
        var folderPath = _fileDialogService.ShowOpenFolderDialog(DefaultFolder);

        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            StopWatchingFolder();
            DefaultFolder = folderPath;
            StatusMessage = "Сканирование папки…";
            var result = await ImportPathsAsync([folderPath]);
            SetImportStatus(result.AddedCount, result.Errors);

            if (result.Errors.Count == 0)
            {
                StartWatchingFolder(folderPath);
            }
        }
    }

    private async Task LoadPlaylistAsync()
    {
        var playlistPath = _fileDialogService.ShowOpenPlaylistDialog(DefaultFolder);

        if (string.IsNullOrWhiteSpace(playlistPath))
        {
            return;
        }

        try
        {
            StatusMessage = "Загрузка плейлиста…";
            var paths = await _playlistStorageService.LoadAsync(playlistPath);
            StopWatchingFolder();
            ResetPlaylist();
            var result = await ImportPathsAsync(paths);

            StatusMessage = result.Errors.Count == 0
                ? $"Плейлист загружен. Треков: {Tracks.Count}"
                : $"Плейлист загружен частично. Треков: {Tracks.Count}, " +
                  $"пропущено: {result.Errors.Count}. {result.Errors[0]}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Загрузка плейлиста отменена";
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            StatusMessage = $"Не удалось загрузить плейлист: {exception.Message}";
        }
    }

    private async Task SavePlaylistAsync()
    {
        var playlistPath = _fileDialogService.ShowSavePlaylistDialog(DefaultFolder);

        if (string.IsNullOrWhiteSpace(playlistPath))
        {
            return;
        }

        try
        {
            await _playlistStorageService.SaveAsync(playlistPath, Tracks);
            StatusMessage = $"Плейлист сохранён: {Path.GetFileName(playlistPath)}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Сохранение плейлиста отменено";
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            StatusMessage = $"Не удалось сохранить плейлист: {exception.Message}";
        }
    }

    private async Task TogglePlayPauseAsync()
    {
        if (IsPlaying)
        {
            Pause();
        }
        else
        {
            await PlaySelectedAsync();
        }
    }

    private async Task PlaySelectedAsync()
    {
        var track = SelectedTrack ?? CurrentTrack ?? Tracks.FirstOrDefault();

        if (track is null)
        {
            return;
        }

        if (_playbackService.CurrentTrack is not null &&
            string.Equals(
                _playbackService.CurrentTrack.FilePath,
                track.FilePath,
                StringComparison.OrdinalIgnoreCase))
        {
            if (_playbackService.Duration > TimeSpan.Zero &&
                _playbackService.Position >= _playbackService.Duration)
            {
                _playbackService.Seek(TimeSpan.Zero);
            }

            CurrentTrack = track;
            SelectedTrack = track;
            UpdatePlaybackPosition();
            _playbackService.Play();
            IsPlaying = true;
            StatusMessage = $"Воспроизводится: {track.DisplayTitle}";
            return;
        }

        await OpenAndPlayAsync(track);
    }

    private async Task OpenAndPlayAsync(Track track, bool addToHistory = true)
    {
        var (requestVersion, cancellation) = BeginPlaybackRequest();

        try
        {
            var previousTrack = CurrentTrack;
            StatusMessage = $"Открытие: {track.DisplayTitle}…";
            await _playbackService.OpenAsync(track, cancellation.Token);

            if (requestVersion != _playbackRequestVersion)
            {
                return;
            }

            if (addToHistory &&
                previousTrack is not null &&
                !ReferenceEquals(previousTrack, track))
            {
                _playHistory.Add(previousTrack);
            }

            CurrentTrack = track;
            SelectedTrack = track;
            _shufflePool.Remove(track);
            UpdatePlaybackPosition();
            _playbackService.Play();
            IsPlaying = true;
            StatusMessage = $"Воспроизводится: {track.DisplayTitle}";
        }
        catch (OperationCanceledException)
        {
            if (requestVersion == _playbackRequestVersion)
            {
                IsPlaying = false;
                StatusMessage = "Открытие трека отменено";
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (requestVersion == _playbackRequestVersion)
            {
                IsPlaying = false;
                StatusMessage = $"Ошибка воспроизведения: {exception.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(_playbackRequestCancellation, cancellation))
            {
                _playbackRequestCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private (int Version, CancellationTokenSource Cancellation) BeginPlaybackRequest()
    {
        _playbackRequestCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _playbackRequestCancellation = cancellation;
        return (++_playbackRequestVersion, cancellation);
    }

    private void CancelPendingPlaybackRequest()
    {
        _playbackRequestVersion++;
        _playbackRequestCancellation?.Cancel();
        _playbackRequestCancellation = null;
    }

    private void Pause()
    {
        _playbackService.Pause();
        IsPlaying = false;
        StatusMessage = CurrentTrack is null
            ? "Воспроизведение приостановлено"
            : $"Пауза: {CurrentTrack.DisplayTitle}";
    }

    private void Stop()
    {
        _playbackService.Stop();
        IsPlaying = false;
        UpdatePlaybackPosition();
        StatusMessage = "Воспроизведение остановлено";
    }

    private async Task PlayPreviousAsync()
    {
        if (Tracks.Count == 0)
        {
            return;
        }

        if (IsShuffleEnabled)
        {
            if (_playHistory.Count == 0)
            {
                StatusMessage = "Предыдущих треков в истории нет";
                return;
            }

            var historyIndex = _playHistory.Count - 1;
            var previousTrack = _playHistory[historyIndex];
            _playHistory.RemoveAt(historyIndex);
            await OpenAndPlayAsync(previousTrack, addToHistory: false);
            return;
        }

        var playbackOrder = GetPlaybackOrder();
        var currentIndex = CurrentTrack is null
            ? -1
            : playbackOrder.IndexOf(CurrentTrack);
        var previousIndex = currentIndex <= 0
            ? playbackOrder.Count - 1
            : currentIndex - 1;
        await OpenAndPlayAsync(playbackOrder[previousIndex]);
    }

    private async Task PlayNextAsync()
    {
        var nextTrack = GetNextTrack(manualNavigation: true);

        if (nextTrack is not null)
        {
            await OpenAndPlayAsync(nextTrack);
        }
    }

    private void ToggleMute()
    {
        IsMuted = !IsMuted;
        _playbackService.IsMuted = IsMuted;
        StatusMessage = IsMuted ? "Звук выключен" : "Звук включён";
    }

    private void ToggleShuffle()
    {
        IsShuffleEnabled = !IsShuffleEnabled;
        _playHistory.Clear();
        ResetShufflePool();
        StatusMessage = IsShuffleEnabled
            ? "Случайный порядок включён"
            : "Случайный порядок выключен";
    }

    private int AddUniqueTracks(IEnumerable<Track> tracks)
    {
        var addedCount = 0;

        foreach (var track in tracks)
        {
            var normalizedPath = Path.GetFullPath(track.FilePath);

            if (_knownPaths.Add(normalizedPath))
            {
                Tracks.Add(track);
                SelectedTrack ??= track;
                addedCount++;
            }
        }

        return addedCount;
    }

    private void StartWatchingFolder(string folderPath)
    {
        StopWatchingFolder();

        try
        {
            var watcher = new FileSystemWatcher(Path.GetFullPath(folderPath))
            {
                IncludeSubdirectories = true,
                Filter = "*.*",
                NotifyFilter = NotifyFilters.FileName |
                               NotifyFilters.DirectoryName |
                               NotifyFilters.LastWrite |
                               NotifyFilters.Size
            };

            watcher.Created += OnWatchedFolderChanged;
            watcher.Changed += OnWatchedFolderChanged;
            watcher.Renamed += OnWatchedFolderRenamed;
            watcher.Error += OnWatchedFolderError;

            lock (_folderWatcherLock)
            {
                _folderWatcher = watcher;
            }

            watcher.EnableRaisingEvents = true;
            StatusMessage = $"Папка отслеживается: {folderPath}";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            StopWatchingFolder();
            StatusMessage = $"Треки добавлены, но следить за папкой не удалось: {exception.Message}";
        }
    }

    private void StopWatchingFolder()
    {
        FileSystemWatcher? watcher;

        lock (_folderWatcherLock)
        {
            _folderRescanCancellation?.Cancel();
            _folderRescanCancellation = null;
            watcher = _folderWatcher;
            _folderWatcher = null;
        }

        if (watcher is null)
        {
            return;
        }

        watcher.EnableRaisingEvents = false;
        watcher.Created -= OnWatchedFolderChanged;
        watcher.Changed -= OnWatchedFolderChanged;
        watcher.Renamed -= OnWatchedFolderRenamed;
        watcher.Error -= OnWatchedFolderError;
        watcher.Dispose();
    }

    private void OnWatchedFolderChanged(object sender, FileSystemEventArgs e)
    {
        if (Directory.Exists(e.FullPath) || SupportedAudioFormats.Contains(e.FullPath))
        {
            ScheduleFolderRescan();
        }
    }

    private void OnWatchedFolderRenamed(object sender, RenamedEventArgs e) =>
        OnWatchedFolderChanged(sender, e);

    private void OnWatchedFolderError(object sender, ErrorEventArgs e) =>
        ScheduleFolderRescan();

    private void ScheduleFolderRescan()
    {
        string folderPath;
        CancellationTokenSource cancellation;

        lock (_folderWatcherLock)
        {
            if (_isDisposed || _folderWatcher is null)
            {
                return;
            }

            var previousCancellation = _folderRescanCancellation;
            cancellation = new CancellationTokenSource();
            _folderRescanCancellation = cancellation;
            folderPath = _folderWatcher.Path;
            previousCancellation?.Cancel();
        }

        _ = RescanWatchedFolderAsync(folderPath, cancellation);
    }

    private async Task RescanWatchedFolderAsync(
        string folderPath,
        CancellationTokenSource cancellation)
    {
        var cancellationToken = cancellation.Token;

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(900), cancellationToken);
            var tracks = await _trackReader.ReadFolderAsync(folderPath, cancellationToken);

            await _dispatcher.InvokeAsync(() =>
            {
                var addedCount = AddUniqueTracks(tracks);

                if (addedCount > 0)
                {
                    StatusMessage = $"Автоматически добавлено треков: {addedCount}";
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Новое событие папки перезапустило отложенное сканирование.
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            if (!_isDisposed)
            {
                await _dispatcher.InvokeAsync(() =>
                    StatusMessage = $"Не удалось обновить папку: {exception.Message}");
            }
        }
        finally
        {
            lock (_folderWatcherLock)
            {
                if (ReferenceEquals(_folderRescanCancellation, cancellation))
                {
                    _folderRescanCancellation = null;
                }
            }

            cancellation.Dispose();
        }
    }

    private void RemoveSelected()
    {
        if (SelectedTrack is null)
        {
            return;
        }

        CancelPendingPlaybackRequest();
        var trackToRemove = SelectedTrack;

        if (ReferenceEquals(trackToRemove, CurrentTrack))
        {
            _playbackService.Stop();
            IsPlaying = false;
            CurrentTrack = null;
            UpdatePlaybackPosition();
        }

        Tracks.Remove(trackToRemove);
        _knownPaths.Remove(Path.GetFullPath(trackToRemove.FilePath));
        _playHistory.RemoveAll(track => ReferenceEquals(track, trackToRemove));
        _shufflePool.RemoveAll(track => ReferenceEquals(track, trackToRemove));
        SelectedTrack = Tracks.FirstOrDefault();
        StatusMessage = $"Удалён трек «{trackToRemove.DisplayTitle}»";
    }

    private void Clear()
    {
        StopWatchingFolder();
        ResetPlaylist();
        StatusMessage = "Плейлист очищен";
    }

    private void ResetPlaylist()
    {
        CancelPendingPlaybackRequest();
        _playbackService.Stop();
        Tracks.Clear();
        _knownPaths.Clear();
        _shufflePool.Clear();
        _playHistory.Clear();
        SelectedTrack = null;
        CurrentTrack = null;
        IsPlaying = false;
        UpdatePlaybackPosition();
    }

    private void SetImportStatus(int addedCount, IReadOnlyList<string> errors)
    {
        if (errors.Count > 0)
        {
            StatusMessage = addedCount > 0
                ? $"Добавлено: {addedCount}. Ошибка: {errors[0]}"
                : $"Не удалось добавить файлы: {errors[0]}";
            return;
        }

        StatusMessage = addedCount > 0
            ? $"Добавлено треков: {addedCount}"
            : "Новых поддерживаемых файлов не найдено";
    }

    private void OnTracksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ResetShufflePool();
        ClearCommand.NotifyCanExecuteChanged();
        SavePlaylistCommand.NotifyCanExecuteChanged();
        PlayCommand.NotifyCanExecuteChanged();
        TogglePlayPauseCommand.NotifyCanExecuteChanged();
        PreviousCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
        RaisePropertyChanged(nameof(TrackCountText));
    }

    private void OnPlaybackPositionChanged(object? sender, EventArgs e) =>
        UpdatePlaybackPosition();

    private async void OnPlaybackEnded(object? sender, EventArgs e)
    {
        IsPlaying = false;

        if (CurrentTrack is null)
        {
            return;
        }

        if (RepeatMode == AudioPlayer.Core.Models.RepeatMode.One)
        {
            await OpenAndPlayAsync(CurrentTrack, addToHistory: false);
            return;
        }

        var nextTrack = GetNextTrack(manualNavigation: false);

        if (nextTrack is not null)
        {
            await OpenAndPlayAsync(nextTrack);
        }
        else
        {
            StatusMessage = "Плейлист завершён";
            UpdatePlaybackPosition();
        }
    }

    private void OnPlaybackFailed(object? sender, PlaybackFailedEventArgs e)
    {
        IsPlaying = false;
        StatusMessage = $"Ошибка воспроизведения: {e.Exception.Message}";
    }

    private Track? GetNextTrack(bool manualNavigation)
    {
        if (Tracks.Count == 0)
        {
            return null;
        }

        if (!IsShuffleEnabled)
        {
            var playbackOrder = GetPlaybackOrder();
            var currentIndex = CurrentTrack is null
                ? -1
                : playbackOrder.IndexOf(CurrentTrack);

            if (currentIndex < 0)
            {
                return playbackOrder[0];
            }

            if (currentIndex < playbackOrder.Count - 1)
            {
                return playbackOrder[currentIndex + 1];
            }

            return manualNavigation || RepeatMode == AudioPlayer.Core.Models.RepeatMode.All
                ? playbackOrder[0]
                : null;
        }

        if (_shufflePool.Count == 0)
        {
            if (!manualNavigation && RepeatMode != AudioPlayer.Core.Models.RepeatMode.All)
            {
                return null;
            }

            ResetShufflePool();
        }

        if (_shufflePool.Count == 0)
        {
            return manualNavigation || RepeatMode == AudioPlayer.Core.Models.RepeatMode.All
                ? CurrentTrack ?? Tracks[0]
                : null;
        }

        var nextTrack = _shufflePool[0];
        _shufflePool.RemoveAt(0);
        return nextTrack;
    }

    private List<Track> GetPlaybackOrder() =>
        CollectionViewSource.GetDefaultView(Tracks).Cast<Track>().ToList();

    private void ResetShufflePool()
    {
        _shufflePool.Clear();

        foreach (var track in Tracks)
        {
            if (!ReferenceEquals(track, CurrentTrack))
            {
                _shufflePool.Add(track);
            }
        }

        for (var index = _shufflePool.Count - 1; index > 0; index--)
        {
            var swapIndex = Random.Shared.Next(index + 1);
            (_shufflePool[index], _shufflePool[swapIndex]) =
                (_shufflePool[swapIndex], _shufflePool[index]);
        }
    }

    private void UpdatePlaybackPosition()
    {
        _isUpdatingPosition = true;

        try
        {
            if (CurrentTrack is null)
            {
                DurationSeconds = 0;
                PositionSeconds = 0;
            }
            else
            {
                DurationSeconds = _playbackService.Duration.TotalSeconds;
                PositionSeconds = _playbackService.Position.TotalSeconds;
            }

            RaisePropertyChanged(nameof(PlaybackTimeText));
        }
        finally
        {
            _isUpdatingPosition = false;
        }
    }

    private AppSettings CreateSettings() => new()
    {
        DefaultFolder = DefaultFolder,
        VolumePercent = VolumePercent,
        LastTrackPath = CurrentTrack?.FilePath ?? SelectedTrack?.FilePath,
        Theme = ThemeName,
        RepeatMode = RepeatMode,
        IsShuffleEnabled = IsShuffleEnabled
    };

    private static BitmapImage? CreateCoverImage(byte[]? imageData)
    {
        if (imageData is null || imageData.Length == 0)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(imageData, writable: false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static string NormalizeTheme(string? themeName) => string.Equals(
        themeName,
        "Light",
        StringComparison.OrdinalIgnoreCase)
        ? "Light"
        : "Dark";

    private static string FormatTime(TimeSpan value) => value.TotalHours >= 1
        ? value.ToString(@"h\:mm\:ss", CultureInfo.CurrentCulture)
        : value.ToString(@"m\:ss", CultureInfo.CurrentCulture);

    private sealed record ImportResult(
        int AddedCount,
        IReadOnlyList<string> Errors);
}
