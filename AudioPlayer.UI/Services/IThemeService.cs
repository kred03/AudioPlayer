namespace AudioPlayer.UI.Services;

public interface IThemeService
{
    string CurrentTheme { get; }

    void ApplyTheme(string themeName);
}
