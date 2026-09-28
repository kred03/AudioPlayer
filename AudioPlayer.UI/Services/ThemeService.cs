using System.Windows;

namespace AudioPlayer.UI.Services;

public sealed class ThemeService : IThemeService
{
    public string CurrentTheme { get; private set; } = "Dark";

    public void ApplyTheme(string themeName)
    {
        var normalizedTheme = string.Equals(
            themeName,
            "Light",
            StringComparison.OrdinalIgnoreCase)
            ? "Light"
            : "Dark";

        var dictionary = new ResourceDictionary
        {
            Source = new Uri(
                $"pack://application:,,,/AudioPlayer;component/Themes/{normalizedTheme}Theme.xaml",
                UriKind.Absolute)
        };

        var mergedDictionaries = Application.Current.Resources.MergedDictionaries;
        var currentTheme = mergedDictionaries.FirstOrDefault(resource =>
            resource.Source?.OriginalString.EndsWith("Theme.xaml", StringComparison.OrdinalIgnoreCase)
            == true);

        if (currentTheme is not null)
        {
            mergedDictionaries.Remove(currentTheme);
        }

        mergedDictionaries.Add(dictionary);
        CurrentTheme = normalizedTheme;
    }
}
