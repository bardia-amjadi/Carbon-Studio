using CarbonStudio.Core.Workspace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CarbonStudio.App.Views;

public sealed partial class WelcomeView : UserControl
{
    public WelcomeView() => InitializeComponent();

    public event EventHandler? OpenFolderRequested;

    public event EventHandler? OpenFileRequested;

    public event EventHandler? NewFileRequested;

    public event EventHandler? PaletteRequested;

    public event EventHandler<string>? RecentOpenRequested;

    public event EventHandler<string>? RecentRemoveRequested;

    public void SetRecents(IReadOnlyList<RecentProject> recents)
    {
        RecentList.ItemsSource = recents;
        NoRecentText.Visibility = recents.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e) => OpenFolderRequested?.Invoke(this, EventArgs.Empty);

    private void OnOpenFileClick(object sender, RoutedEventArgs e) => OpenFileRequested?.Invoke(this, EventArgs.Empty);

    private void OnNewFileClick(object sender, RoutedEventArgs e) => NewFileRequested?.Invoke(this, EventArgs.Empty);

    private void OnPaletteClick(object sender, RoutedEventArgs e) => PaletteRequested?.Invoke(this, EventArgs.Empty);

    private void OnRecentClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path })
        {
            RecentOpenRequested?.Invoke(this, path);
        }
    }

    private void OnRecentRemoveClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path })
        {
            RecentRemoveRequested?.Invoke(this, path);
        }
    }
}
