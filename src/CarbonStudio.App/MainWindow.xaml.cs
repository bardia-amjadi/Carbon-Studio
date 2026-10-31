using System.ComponentModel;
using CarbonStudio.App.Controls;
using CarbonStudio.App.Services;
using CarbonStudio.App.ViewModels;
using CarbonStudio.Core.Commands;
using CarbonStudio.Core.Documents;
using CarbonStudio.Core.FileSystem;
using CarbonStudio.Core.Output;
using CarbonStudio.Core.Paths;
using CarbonStudio.Core.Settings;
using CarbonStudio.Core.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace CarbonStudio.App;

/// <summary>Shell window. Behaviour is split by concern across the MainWindow.*.cs partial files.</summary>
public sealed partial class MainWindow : Window
{
    private const double MinSidebarWidth = 160;
    private const double MinBottomHeight = 100;

    private readonly IServiceProvider _services;
    private readonly ISettingsService _settings;
    private readonly IWorkspaceService _workspace;
    private readonly IRecentProjectsStore _recents;
    private readonly IFileSystemService _fs;
    private readonly DocumentManager _documents;
    private readonly ICommandRegistry _registry;
    private readonly IOutputService _output;
    private readonly ILogger<MainWindow> _logger;
    private readonly DialogService _dialogs;
    private readonly PickerService _pickers;
    private readonly DispatcherQueue _dispatcher;
    private readonly CarbonStudio.Core.Editor.ShortcutMap _shortcutMap;

    private bool _sidebarVisible = true;
    private bool _bottomVisible = true;
    private double _sidebarWidth = 280;
    private double _bottomHeight = 220;
    private string _activeSidebarView = "explorer";
    private string _activeBottomPanel = "output";
    private double _dragStartSize;
    private bool _closeConfirmed;

    // False until the saved layout has been applied: XAML load raises selection events that must not persist defaults.
    private bool _layoutReady;

    public MainWindow(IServiceProvider services)
    {
        _services = services;
        _settings = services.GetRequiredService<ISettingsService>();
        _workspace = services.GetRequiredService<IWorkspaceService>();
        _recents = services.GetRequiredService<IRecentProjectsStore>();
        _fs = services.GetRequiredService<IFileSystemService>();
        _documents = services.GetRequiredService<DocumentManager>();
        _registry = services.GetRequiredService<ICommandRegistry>();
        _output = services.GetRequiredService<IOutputService>();
        _logger = services.GetRequiredService<ILogger<MainWindow>>();
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleDragRegion);
        TrySetIcon();

        _dialogs = new DialogService(() => RootGrid.XamlRoot);
        _pickers = new PickerService(WinRT.Interop.WindowNative.GetWindowHandle(this));
        ViewModel = new MainViewModel(_workspace, _documents);

        RegisterCommands();
        _shortcutMap = CarbonStudio.Core.Editor.ShortcutMap.FromCommands(_registry.All);
        WireEvents();

        var settings = _settings.Effective;
        ApplyWindowSize(settings.Layout);
        ApplyLayout(settings.Layout);
        ApplyTheme();
        ApplyEditorSettings();
        RebuildExplorer();
        UpdateStatus();
        UpdateMenuState();
        Title = ViewModel.WindowTitle;
        _layoutReady = true;
    }

    public MainViewModel ViewModel { get; }

    private void TrySetIcon()
    {
        try
        {
            AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "CarbonStudio.ico"));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            _logger.LogWarning(ex, "Could not set the window icon");
        }
    }

    private void WireEvents()
    {
        RootGrid.Loaded += OnRootLoaded;
        RootGrid.ActualThemeChanged += (_, _) =>
        {
            ThemeService.UpdateTitleBarButtons(this, RootGrid);
            ApplyEditorSettings();
        };
        AppWindow.Closing += OnAppWindowClosing;

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        _settings.Changed += (_, _) => RunOnUi(() =>
        {
            ApplyTheme();
            ApplyEditorSettings();
        });
        _workspace.Changed += (_, _) => RunOnUi(() =>
        {
            RebuildExplorer();
            UpdateMenuState();
        });
        _output.LineWritten += (_, line) => RunOnUi(() => AppendOutput(line));

        WireDocumentEvents();
        WireLayoutEvents();
        WireWelcomeEvents();
    }

    private async void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        // Reserve room for the system caption buttons (minimise / maximise / close).
        var scale = RootGrid.XamlRoot.RasterizationScale;
        TitleBarRightInset.Width = new GridLength(AppWindow.TitleBar.RightInset / scale);
        ThemeService.UpdateTitleBarButtons(this, RootGrid);

        SettingsMenuItem.KeyboardAccelerators.Add(new Microsoft.UI.Xaml.Input.KeyboardAccelerator
        {
            Key = (Windows.System.VirtualKey)188, // OEM comma has no named VirtualKey member
            Modifiers = Windows.System.VirtualKeyModifiers.Control,
        });

        foreach (var line in _output.Snapshot(OutputLoggerProvider.ChannelName))
        {
            AppendOutput(line);
        }

        await RefreshRecentsAsync();
        await InitializeEditorAsync();
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcher.HasThreadAccess)
        {
            action();
        }
        else
        {
            _dispatcher.TryEnqueue(() => action());
        }
    }

    // ------------------------------------------------------------------ status bar / title

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) => RunOnUi(UpdateStatus);

    private void UpdateStatus()
    {
        WorkspaceStatusText.Text = ViewModel.WorkspaceStatus;
        MessageStatusText.Text = ViewModel.MessageStatus;
        PositionStatusText.Text = ViewModel.PositionStatus;
        EncodingStatusText.Text = ViewModel.EncodingStatus;
        LineEndingStatusText.Text = ViewModel.LineEndingStatus;
        LanguageStatusText.Text = ViewModel.LanguageStatus;
        Title = ViewModel.WindowTitle;
    }

    private void SetMessage(string message) => ViewModel.MessageStatus = message;

    // ------------------------------------------------------------------ theme / editor settings

    private void ApplyTheme()
    {
        var appearance = _settings.Effective.Appearance;
        ThemeService.Apply(this, RootGrid, appearance);
        ThemeDarkItem.IsChecked = appearance.Theme == ThemeMode.Dark;
        ThemeLightItem.IsChecked = appearance.Theme == ThemeMode.Light;
        ThemeSystemItem.IsChecked = appearance.Theme == ThemeMode.System;
    }

    // ------------------------------------------------------------------ window size & layout

    private void ApplyWindowSize(LayoutSettings layout)
    {
        var width = layout.WindowWidth ?? 1360;
        var height = layout.WindowHeight ?? 840;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height));

        if (layout.WindowMaximized && AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }
    }

    private void ApplyLayout(LayoutSettings layout)
    {
        _sidebarVisible = layout.SidebarVisible;
        _bottomVisible = layout.BottomPanelVisible;
        _sidebarWidth = layout.SidebarWidth;
        _bottomHeight = layout.BottomPanelHeight;
        _activeSidebarView = layout.ActiveSidebarView;
        _activeBottomPanel = layout.ActiveBottomPanel;

        BottomSplitter.BarOrientation = Orientation.Horizontal;
        UpdateSidebarSize();
        UpdateBottomSize();
        ShowSidebarView(_activeSidebarView);
        ShowBottomPanel(_activeBottomPanel, updateSelector: true);
    }

    private void WireLayoutEvents()
    {
        SidebarSplitter.DragStarted += (_, _) => _dragStartSize = _sidebarWidth;
        SidebarSplitter.DragDelta += (_, delta) =>
        {
            var max = Math.Max(MinSidebarWidth, RootGrid.ActualWidth - 400);
            _sidebarWidth = Math.Clamp(_dragStartSize + delta, MinSidebarWidth, max);
            UpdateSidebarSize();
        };
        SidebarSplitter.DragCompleted += (_, _) => _ = SaveLayoutAsync();

        BottomSplitter.DragStarted += (_, _) => _dragStartSize = _bottomHeight;
        BottomSplitter.DragDelta += (_, delta) =>
        {
            var max = Math.Max(MinBottomHeight, EditorAreaGrid.ActualHeight - 160);
            _bottomHeight = Math.Clamp(_dragStartSize - delta, MinBottomHeight, max);
            UpdateBottomSize();
        };
        BottomSplitter.DragCompleted += (_, _) => _ = SaveLayoutAsync();
    }

    private void UpdateSidebarSize()
    {
        SidebarColumn.Width = new GridLength(_sidebarVisible ? _sidebarWidth : 0);
        SidebarHost.Visibility = _sidebarVisible ? Visibility.Visible : Visibility.Collapsed;
        SidebarSplitter.Visibility = _sidebarVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateBottomSize()
    {
        BottomPanelRow.Height = new GridLength(_bottomVisible ? _bottomHeight : 0);
        BottomPanel.Visibility = _bottomVisible ? Visibility.Visible : Visibility.Collapsed;
        BottomSplitter.Visibility = _bottomVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ToggleSidebar()
    {
        _sidebarVisible = !_sidebarVisible;
        UpdateSidebarSize();
        _ = SaveLayoutAsync();
    }

    private void ToggleBottomPanel()
    {
        _bottomVisible = !_bottomVisible;
        UpdateBottomSize();
        _ = SaveLayoutAsync();
    }

    private async Task ResetLayoutAsync()
    {
        var defaults = new LayoutSettings
        {
            WindowWidth = _settings.Effective.Layout.WindowWidth,
            WindowHeight = _settings.Effective.Layout.WindowHeight,
            WindowMaximized = _settings.Effective.Layout.WindowMaximized,
        };
        ApplyLayout(defaults);
        await SaveLayoutAsync();
        SetMessage("Layout reset");
    }

    private async Task SaveLayoutAsync(bool includeWindow = false)
    {
        if (!_layoutReady)
        {
            return;
        }

        try
        {
            var maximized = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
            var size = AppWindow.Size;

            await _settings.UpdateUserAsync(s => s with
            {
                Layout = s.Layout with
                {
                    SidebarVisible = _sidebarVisible,
                    SidebarWidth = _sidebarWidth,
                    BottomPanelVisible = _bottomVisible,
                    BottomPanelHeight = _bottomHeight,
                    ActiveSidebarView = _activeSidebarView,
                    ActiveBottomPanel = _activeBottomPanel,
                    WindowMaximized = includeWindow ? maximized : s.Layout.WindowMaximized,
                    WindowWidth = includeWindow && !maximized ? size.Width : s.Layout.WindowWidth,
                    WindowHeight = includeWindow && !maximized ? size.Height : s.Layout.WindowHeight,
                },
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A failed layout save must never interrupt the user; it is logged and the app carries on.
            _logger.LogWarning(ex, "Could not save the layout");
        }
    }

    // ------------------------------------------------------------------ activity bar / sidebar views

    private (Button Button, Rectangle Indicator)[] ActivityItems() =>
    [
        (ExplorerActivity, ExplorerIndicator),
        (SearchActivity, SearchIndicator),
        (GitActivity, GitIndicator),
        (RunActivity, RunIndicator),
        (ExtensionsActivity, ExtensionsIndicator),
    ];

    private static readonly Dictionary<string, (string Title, string Message, int Phase)> SidebarPhases = new()
    {
        ["search"] = ("Search", "Workspace-wide search and replace with previews and cancellation.", 5),
        ["git"] = ("Source Control", "Git status, staging, commits, diffs and history.", 5),
        ["run"] = ("Run and Debug", "Build and launch profiles first; the debugger follows later.", 4),
        ["extensions"] = ("Extensions", "VSIX inspection and the extension manager.", 7),
    };

    private static readonly Dictionary<string, (string Title, string Message, int Phase)> BottomPhases = new()
    {
        ["terminal"] = ("Integrated Terminal", "Real shell sessions through a pseudo-console. Phase 1 only records the technology decision.", 4),
        ["problems"] = ("Problems", "Build errors and language-server diagnostics will be listed here.", 4),
        ["debug"] = ("Debug Console", "Available together with the debugger integration.", 6),
        ["tests"] = ("Test Explorer", "Discovery and execution of .NET tests.", 6),
    };

    private void OnActivityClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id })
        {
            return;
        }

        // Clicking the active view toggles the side bar, as in most IDEs.
        if (_sidebarVisible && id == _activeSidebarView)
        {
            ToggleSidebar();
            return;
        }

        _sidebarVisible = true;
        UpdateSidebarSize();
        ShowSidebarView(id);
        _ = SaveLayoutAsync();
    }

    private async void OnSettingsActivityClick(object sender, RoutedEventArgs e) => await RunCommandAsync(CommandIds.SettingsOpen);

    private void ShowSidebarView(string id)
    {
        _activeSidebarView = SidebarPhases.ContainsKey(id) || id == "explorer" ? id : "explorer";

        foreach (var (button, indicator) in ActivityItems())
        {
            var selected = (string)button.Tag == _activeSidebarView;
            indicator.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            button.Opacity = selected ? 1.0 : 0.6;
        }

        var explorer = _activeSidebarView == "explorer";
        ExplorerView.Visibility = explorer ? Visibility.Visible : Visibility.Collapsed;
        PlaceholderView.Visibility = explorer ? Visibility.Collapsed : Visibility.Visible;
        PlaceholderView.Children.Clear();
        if (!explorer)
        {
            var (title, message, phase) = SidebarPhases[_activeSidebarView];
            PlaceholderView.Children.Add(new PhaseNotice(title, message, phase));
        }
    }

    // ------------------------------------------------------------------ bottom panel

    private void OnBottomTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_layoutReady && sender.SelectedItem?.Tag is string id)
        {
            ShowBottomPanel(id, updateSelector: false);
            _ = SaveLayoutAsync();
        }
    }

    private void ShowBottomPanel(string id, bool updateSelector)
    {
        _activeBottomPanel = BottomPhases.ContainsKey(id) || id == "output" ? id : "output";

        if (updateSelector)
        {
            BottomTabs.SelectedItem = _activeBottomPanel switch
            {
                "terminal" => TerminalTab,
                "problems" => ProblemsTab,
                "debug" => DebugTab,
                "tests" => TestsTab,
                _ => OutputTab,
            };
        }

        var output = _activeBottomPanel == "output";
        OutputScroll.Visibility = output ? Visibility.Visible : Visibility.Collapsed;
        BottomPlaceholderHost.Visibility = output ? Visibility.Collapsed : Visibility.Visible;
        if (BottomPlaceholderHost is Grid host)
        {
            host.Children.Clear();
            if (!output)
            {
                var (title, message, phase) = BottomPhases[_activeBottomPanel];
                host.Children.Add(new PhaseNotice(title, message, phase));
            }
        }
    }

    private void AppendOutput(OutputLine line)
    {
        if (!string.Equals(line.Channel, OutputLoggerProvider.ChannelName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var text = OutputText.Text;
        if (text.Length > 200_000)
        {
            text = text[^100_000..];
        }

        OutputText.Text = text + $"{line.Timestamp:HH:mm:ss}  {line.Text}\n";
        OutputScroll.UpdateLayout();
        OutputScroll.ChangeView(null, OutputScroll.ScrollableHeight, null);
    }

    // ------------------------------------------------------------------ closing

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeConfirmed)
        {
            return;
        }

        args.Cancel = true;
        _ = ConfirmCloseAsync();
    }

    private async Task ConfirmCloseAsync()
    {
        if (_surface is not null && !await _surface.FlushAsync() &&
            !await _dialogs.ConfirmAsync("Editor not responding", "The editor did not confirm its latest edits. Close anyway? Changes made in the last moments may be lost.", "Close anyway"))
        {
            return;
        }

        foreach (var document in _documents.Documents.Where(d => d.IsDirty).ToList())
        {
            _documents.Active = document;
            var choice = await _dialogs.AskSaveAsync($"Do you want to save the changes you made to {document.DisplayName}?");
            if (choice == SaveChoice.Cancel || (choice == SaveChoice.Save && !await SaveDocumentAsync(document)))
            {
                return;
            }
        }

        await SaveLayoutAsync(includeWindow: true);
        _closeConfirmed = true;
        Close();
    }
}
