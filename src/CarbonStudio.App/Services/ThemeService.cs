using CarbonStudio.Core.Settings;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace CarbonStudio.App.Services;

/// <summary>Applies the theme mode and accent colour from settings to the window.</summary>
internal static class ThemeService
{
    public static void Apply(Window window, FrameworkElement root, AppearanceSettings appearance)
    {
        root.RequestedTheme = appearance.Theme switch
        {
            ThemeMode.Light => ElementTheme.Light,
            ThemeMode.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        if (SettingsValidator.TryParseHexColor(appearance.AccentColor, out var rgb) &&
            Application.Current.Resources["CarbonAccentBrush"] is SolidColorBrush accent)
        {
            accent.Color = Color.FromArgb(255, rgb.R, rgb.G, rgb.B);
        }

        UpdateTitleBarButtons(window, root);
    }

    /// <summary>Caption buttons are drawn by the shell; they need colours that match the active theme.</summary>
    public static void UpdateTitleBarButtons(Window window, FrameworkElement root)
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        var dark = root.ActualTheme != ElementTheme.Light;
        var titleBar = window.AppWindow.TitleBar;
        var foreground = dark ? Color.FromArgb(255, 0xE6, 0xED, 0xF3) : Color.FromArgb(255, 0x1B, 0x23, 0x2B);
        var hover = dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0);

        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 0x8B, 0x98, 0xA5);
        titleBar.ButtonHoverBackgroundColor = hover;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = hover;
        titleBar.ButtonPressedForegroundColor = foreground;
    }
}
