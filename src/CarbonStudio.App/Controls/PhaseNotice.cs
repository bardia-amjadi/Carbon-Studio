using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CarbonStudio.App.Controls;

/// <summary>
/// Honest placeholder for functionality that belongs to a later phase. It states what is coming and when,
/// and contains no controls that pretend to work.
/// </summary>
public sealed partial class PhaseNotice : StackPanel
{
    public PhaseNotice(string title, string message, int phase)
    {
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        Spacing = 6;
        Padding = new Thickness(16);
        MaxWidth = 360;

        Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CarbonTextBrush"],
        });
        Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CarbonMutedTextBrush"],
        });
        Children.Add(new TextBlock
        {
            Text = $"Planned for Phase {phase}",
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CarbonAccentBrush"],
        });
    }
}
