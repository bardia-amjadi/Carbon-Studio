using CarbonStudio.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CarbonStudio.App.Services;

/// <summary>Builds the Settings dialog and the function that applies its values to a settings object.</summary>
internal static class SettingsDialog
{
    public static (ContentDialog Dialog, Func<AppSettings, AppSettings> Apply) Create(AppSettings current)
    {
        var theme = new ComboBox { Header = "Theme", HorizontalAlignment = HorizontalAlignment.Stretch };
        theme.Items.Add("Use system setting");
        theme.Items.Add("Light");
        theme.Items.Add("Dark");
        theme.SelectedIndex = current.Appearance.Theme switch { ThemeMode.System => 0, ThemeMode.Light => 1, _ => 2 };

        var accent = new TextBox
        {
            Header = "Accent colour (#RRGGBB)",
            Text = current.Appearance.AccentColor,
            MaxLength = 7,
        };

        var fontFamily = new TextBox { Header = "Editor font family", Text = current.Editor.FontFamily };
        var fontSize = new NumberBox
        {
            Header = "Editor font size",
            Value = current.Editor.FontSize,
            Minimum = 8,
            Maximum = 48,
            SmallChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
        };
        var wrap = new ToggleSwitch { Header = "Word wrap", IsOn = current.Editor.WordWrap };
        var tabSize = new NumberBox
        {
            Header = "Tab size",
            Value = current.Editor.TabSize,
            Minimum = 1,
            Maximum = 16,
            SmallChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
        };
        var insertSpaces = new ToggleSwitch { Header = "Insert spaces when pressing Tab", IsOn = current.Editor.InsertSpaces };
        var lineNumbers = new ToggleSwitch { Header = "Line numbers", IsOn = current.Editor.LineNumbers };
        var minimap = new ToggleSwitch { Header = "Minimap", IsOn = current.Editor.Minimap };
        var newFileEnding = new ComboBox { Header = "Line ending for new files", HorizontalAlignment = HorizontalAlignment.Stretch };
        newFileEnding.Items.Add("System default");
        newFileEnding.Items.Add("LF (Unix)");
        newFileEnding.Items.Add("CRLF (Windows)");
        newFileEnding.SelectedIndex = current.Editor.NewFileLineEnding switch { NewFileLineEnding.Lf => 1, NewFileLineEnding.CrLf => 2, _ => 0 };
        var maxFile = new NumberBox
        {
            Header = "Largest file the editor will open (MB)",
            Value = current.Files.MaxOpenFileSizeMb,
            Minimum = 1,
            Maximum = 200,
            SmallChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
        };

        var stack = new StackPanel { Spacing = 14, MinWidth = 380 };
        foreach (var element in new UIElement[] { theme, accent, fontFamily, fontSize, wrap, tabSize, insertSpaces, lineNumbers, minimap, newFileEnding, maxFile })
        {
            stack.Children.Add(element);
        }

        var dialog = new ContentDialog
        {
            Title = "Settings",
            Content = new ScrollViewer { Content = stack, MaxHeight = 520 },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        accent.TextChanged += (_, _) =>
        {
            var valid = SettingsValidator.TryParseHexColor(accent.Text, out _);
            dialog.IsPrimaryButtonEnabled = valid;
            accent.Header = valid ? "Accent colour (#RRGGBB)" : "Accent colour (#RRGGBB) — not a valid colour";
        };

        AppSettings Apply(AppSettings settings) => settings with
        {
            Appearance = settings.Appearance with
            {
                Theme = theme.SelectedIndex switch { 0 => ThemeMode.System, 1 => ThemeMode.Light, _ => ThemeMode.Dark },
                AccentColor = accent.Text.Trim(),
            },
            Editor = settings.Editor with
            {
                FontFamily = fontFamily.Text,
                FontSize = double.IsNaN(fontSize.Value) ? settings.Editor.FontSize : fontSize.Value,
                WordWrap = wrap.IsOn,
                TabSize = double.IsNaN(tabSize.Value) ? settings.Editor.TabSize : (int)Math.Round(tabSize.Value),
                InsertSpaces = insertSpaces.IsOn,
                LineNumbers = lineNumbers.IsOn,
                Minimap = minimap.IsOn,
                NewFileLineEnding = newFileEnding.SelectedIndex switch { 1 => NewFileLineEnding.Lf, 2 => NewFileLineEnding.CrLf, _ => NewFileLineEnding.System },
            },
            Files = settings.Files with
            {
                MaxOpenFileSizeMb = double.IsNaN(maxFile.Value) ? settings.Files.MaxOpenFileSizeMb : (int)Math.Round(maxFile.Value),
            },
        };

        return (dialog, Apply);
    }
}
