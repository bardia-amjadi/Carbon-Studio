using System.Globalization;

namespace CarbonStudio.Core.Settings;

/// <summary>Clamps and repairs settings values so the UI never receives nonsense from a hand-edited file.</summary>
public static class SettingsValidator
{
    public static AppSettings Normalize(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var appearance = settings.Appearance with
        {
            AccentColor = TryParseHexColor(settings.Appearance.AccentColor, out _)
                ? NormalizeHex(settings.Appearance.AccentColor)
                : AppearanceSettings.DefaultAccentColor,
        };

        var editor = settings.Editor with
        {
            FontSize = Clamp(settings.Editor.FontSize, 8, 48, 14),
            TabSize = Math.Clamp(settings.Editor.TabSize, 1, 16),
            FontFamily = string.IsNullOrWhiteSpace(settings.Editor.FontFamily)
                ? new EditorSettings().FontFamily
                : settings.Editor.FontFamily.Trim(),
        };

        var explorer = settings.Explorer with
        {
            ExcludedNames = (settings.Explorer.ExcludedNames ?? [])
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
        };

        var files = settings.Files with
        {
            MaxOpenFileSizeMb = Math.Clamp(settings.Files.MaxOpenFileSizeMb, 1, 200),
        };

        var layout = settings.Layout with
        {
            SidebarWidth = Clamp(settings.Layout.SidebarWidth, 160, 700, 280),
            BottomPanelHeight = Clamp(settings.Layout.BottomPanelHeight, 100, 900, 220),
            ActiveSidebarView = string.IsNullOrWhiteSpace(settings.Layout.ActiveSidebarView) ? "explorer" : settings.Layout.ActiveSidebarView,
            ActiveBottomPanel = string.IsNullOrWhiteSpace(settings.Layout.ActiveBottomPanel) ? "output" : settings.Layout.ActiveBottomPanel,
            WindowWidth = settings.Layout.WindowWidth is { } w ? Math.Clamp(w, 640, 16384) : null,
            WindowHeight = settings.Layout.WindowHeight is { } h ? Math.Clamp(h, 480, 16384) : null,
        };

        return settings with
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            Appearance = appearance,
            Editor = editor,
            Explorer = explorer,
            Files = files,
            Layout = layout,
        };
    }

    /// <summary>Accepts "#RRGGBB" (case-insensitive).</summary>
    public static bool TryParseHexColor(string? value, out (byte R, byte G, byte B) color)
    {
        color = default;
        if (value is not { Length: 7 } || value[0] != '#')
        {
            return false;
        }

        if (!byte.TryParse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !byte.TryParse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !byte.TryParse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        color = (r, g, b);
        return true;
    }

    private static string NormalizeHex(string value) => value.ToUpperInvariant();

    private static double Clamp(double value, double min, double max, double fallback) =>
        double.IsNaN(value) || double.IsInfinity(value) ? fallback : Math.Clamp(value, min, max);
}
