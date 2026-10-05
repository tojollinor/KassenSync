using System.Windows.Media;

namespace KassenSync.App.Services;

public static class ThemeService
{
    public const string DefaultBackgroundColor = "#EAF6FF";

    public static void ApplyBackground(string? hexColor)
    {
        var normalized = Normalize(hexColor);

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(normalized);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            System.Windows.Application.Current.Resources["AppBackgroundBrush"] = brush;
        }
        catch
        {
            var fallback = (Color)ColorConverter.ConvertFromString(DefaultBackgroundColor);
            var brush = new SolidColorBrush(fallback);
            brush.Freeze();
            System.Windows.Application.Current.Resources["AppBackgroundBrush"] = brush;
        }
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DefaultBackgroundColor;

        var trimmed = value.Trim().ToUpperInvariant();
        return KassenSync.Core.Services.SettingsMigration.IsValidRgbHex(trimmed)
            ? trimmed
            : DefaultBackgroundColor;
    }
}
