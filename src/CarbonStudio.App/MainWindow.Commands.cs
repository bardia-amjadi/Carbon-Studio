using System.Diagnostics;
using System.Runtime.InteropServices;
using CarbonStudio.App.Services;
using CarbonStudio.Core.Commands;
using CarbonStudio.Core.Editor;
using CarbonStudio.Core.FileSystem;
using CarbonStudio.Core.Paths;
using CarbonStudio.Core.Settings;
using CarbonStudio.Core.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace CarbonStudio.App;

/// <summary>A row in the command palette list.</summary>
public sealed class PaletteItem
{
    public PaletteItem(CommandDefinition command)
    {
        Command = command;
        Category = command.Category;
        Title = command.Title;
        Shortcut = command.Shortcut ?? string.Empty;
        Note = command.UnavailableReason ?? string.Empty;
        Opacity = command.IsAvailable ? 1.0 : 0.5;
    }

    public CommandDefinition Command { get; }

    public string Category { get; }

    public string Title { get; }

    public string Shortcut { get; }

    public string Note { get; }

    public double Opacity { get; }
}

// Command registration, menu wiring and the command palette.
public sealed partial class MainWindow
{
    private void RegisterCommands()
    {
        void Add(string id, string title, string category, Func<Task> run, string? shortcut = null, Func<bool>? can = null, string? unavailable = null) =>
            _registry.Register(new CommandDefinition(id, title, category, _ => run())
            {
                Shortcut = shortcut,
                CanExecute = can,
                UnavailableReason = unavailable,
            });

        bool HasDocument() => _documents.Active is not null;
        bool CanDo(EditorAction action) => _surface is not null && HasDocument() && _surface.Supports(action);
        bool HasWorkspace() => _workspace.Current is not null;

        // File
        Add(CommandIds.FileNew, "New File", "File", () =>
        {
            _documents.CreateUntitled();
            return Task.CompletedTask;
        }, "Ctrl+N");
        Add(CommandIds.FileOpen, "Open File…", "File", OpenFileDialogAsync, "Ctrl+O");
        Add(CommandIds.FileOpenFolder, "Open Folder…", "File", OpenFolderDialogAsync, "Ctrl+Shift+O");
        Add(CommandIds.FileCloseFolder, "Close Folder", "File", async () => await _workspace.CloseAsync(), can: HasWorkspace);
        Add(CommandIds.FileSave, "Save", "File", async () => await SaveDocumentAsync(_documents.Active!), "Ctrl+S", HasDocument);
        Add(CommandIds.FileSaveAs, "Save As…", "File", async () => await SaveDocumentAsAsync(_documents.Active!), "Ctrl+Shift+S", HasDocument);
        Add(CommandIds.FileSaveAll, "Save All", "File", SaveAllAsync, "Ctrl+Alt+S", () => _documents.HasUnsavedChanges);
        Add(CommandIds.FileCloseTab, "Close Tab", "File", async () => await CloseDocumentAsync(_documents.Active!), "Ctrl+W", HasDocument);
        Add(CommandIds.FileExit, "Exit", "File", () =>
        {
            Close();
            return Task.CompletedTask;
        });

        // Edit (executed by the active editor surface; the basic fallback editor supports only the first six)
        void AddEdit(string id, string title, EditorAction action) =>
            Add(id, title, "Edit", () => Run(() => _surface!.Execute(action)), can: () => CanDo(action));

        AddEdit(CommandIds.EditUndo, "Undo", EditorAction.Undo);
        AddEdit(CommandIds.EditRedo, "Redo", EditorAction.Redo);
        AddEdit(CommandIds.EditCut, "Cut", EditorAction.Cut);
        AddEdit(CommandIds.EditCopy, "Copy", EditorAction.Copy);
        AddEdit(CommandIds.EditPaste, "Paste", EditorAction.Paste);
        AddEdit(CommandIds.EditSelectAll, "Select All", EditorAction.SelectAll);
        AddEdit(CommandIds.EditFind, "Find in File", EditorAction.Find);
        AddEdit(CommandIds.EditReplace, "Replace in File", EditorAction.Replace);
        AddEdit(CommandIds.EditGoToLine, "Go to Line", EditorAction.GoToLine);
        AddEdit(CommandIds.EditFormatDocument, "Format Document", EditorAction.FormatDocument);
        AddEdit(CommandIds.EditTrimWhitespace, "Trim Trailing Whitespace", EditorAction.TrimTrailingWhitespace);

        // Document format (applied when the file is saved)
        Add(CommandIds.DocLineEndingLf, "Set Line Endings: LF", "Document", () => SetLineEndingAsync(LineEnding.Lf), can: HasDocument);
        Add(CommandIds.DocLineEndingCrLf, "Set Line Endings: CRLF", "Document", () => SetLineEndingAsync(LineEnding.CrLf), can: HasDocument);
        Add(CommandIds.DocEncodingUtf8, "Save with Encoding: UTF-8", "Document", () => SetEncodingAsync(TextEncodingKind.Utf8, withBom: false), can: HasDocument);
        Add(CommandIds.DocEncodingUtf8Bom, "Save with Encoding: UTF-8 with BOM", "Document", () => SetEncodingAsync(TextEncodingKind.Utf8, withBom: true), can: HasDocument);
        Add(CommandIds.DocEncodingUtf16Le, "Save with Encoding: UTF-16 LE", "Document", () => SetEncodingAsync(TextEncodingKind.Utf16Le, withBom: true), can: HasDocument);
        Add(CommandIds.DocEncodingUtf16Be, "Save with Encoding: UTF-16 BE", "Document", () => SetEncodingAsync(TextEncodingKind.Utf16Be, withBom: true), can: HasDocument);
        Add(CommandIds.DocEncodingLatin1, "Save with Encoding: Latin-1", "Document", () => SetEncodingAsync(TextEncodingKind.Latin1, withBom: false), can: HasDocument);

        // View
        Add(CommandIds.ViewCommandPalette, "Command Palette", "View", () =>
        {
            OpenPalette();
            return Task.CompletedTask;
        }, "Ctrl+Shift+P");
        Add(CommandIds.ViewToggleSidebar, "Toggle Side Bar", "View", () => Run(ToggleSidebar), "Ctrl+B");
        Add(CommandIds.ViewToggleBottomPanel, "Toggle Bottom Panel", "View", () => Run(ToggleBottomPanel), "Ctrl+J");
        Add(CommandIds.ViewExplorer, "Show Explorer", "View", () => Run(() =>
        {
            _sidebarVisible = true;
            UpdateSidebarSize();
            ShowSidebarView("explorer");
            _ = SaveLayoutAsync();
        }), "Ctrl+Shift+E");
        Add(CommandIds.ViewOutput, "Show Output", "View", () => Run(() =>
        {
            _bottomVisible = true;
            UpdateBottomSize();
            ShowBottomPanel("output", updateSelector: true);
            _ = SaveLayoutAsync();
        }));
        Add(CommandIds.ViewThemeDark, "Theme: Dark", "View", () => SetThemeAsync(ThemeMode.Dark));
        Add(CommandIds.ViewThemeLight, "Theme: Light", "View", () => SetThemeAsync(ThemeMode.Light));
        Add(CommandIds.ViewThemeSystem, "Theme: Use System Setting", "View", () => SetThemeAsync(ThemeMode.System));
        Add(CommandIds.ViewWordWrap, "Toggle Word Wrap", "View", async () =>
            await _settings.UpdateUserAsync(s => s with { Editor = s.Editor with { WordWrap = !s.Editor.WordWrap } }));
        Add(CommandIds.ViewResetLayout, "Reset Layout", "View", ResetLayoutAsync);

        // Explorer
        Add(CommandIds.ExplorerRefresh, "Refresh Explorer", "Explorer", RefreshExplorerAsync, can: HasWorkspace);
        Add(CommandIds.ExplorerNewFile, "New File in Workspace…", "Explorer", () => CreateEntryAsync(SelectedExplorerItem, isFolder: false), can: HasWorkspace);
        Add(CommandIds.ExplorerNewFolder, "New Folder in Workspace…", "Explorer", () => CreateEntryAsync(SelectedExplorerItem, isFolder: true), can: HasWorkspace);

        // Settings and help
        Add(CommandIds.SettingsOpen, "Open Settings", "Preferences", OpenSettingsAsync, "Ctrl+,");
        Add(CommandIds.SettingsOpenFile, "Open Settings File (JSON)", "Preferences", OpenSettingsFileAsync);
        Add(CommandIds.HelpOpenLogs, "Open Logs Folder", "Help", () =>
        {
            OpenLogsFolder();
            return Task.CompletedTask;
        });
        Add(CommandIds.HelpAbout, "About Carbon Studio", "Help", ShowAboutAsync);

        // Registered so they are discoverable, but explicitly unavailable until their phase ships.
        Add(CommandIds.SearchWorkspace, "Search in Workspace", "Search", () => Task.CompletedTask, unavailable: "Planned for Phase 5");
        Add(CommandIds.ProjectNew, "New Project…", "Project", () => Task.CompletedTask, unavailable: "Planned for Phase 3");
        Add(CommandIds.BuildBuild, "Build", "Build", () => Task.CompletedTask, unavailable: "Planned for Phase 4");
        Add(CommandIds.BuildRun, "Run", "Build", () => Task.CompletedTask, unavailable: "Planned for Phase 4");
        Add(CommandIds.TerminalNew, "New Terminal", "Terminal", () => Task.CompletedTask, unavailable: "Planned for Phase 4");
        Add(CommandIds.GitCommit, "Git: Commit", "Git", () => Task.CompletedTask, unavailable: "Planned for Phase 5");
        Add(CommandIds.DebugStart, "Start Debugging", "Debug", () => Task.CompletedTask, unavailable: "Planned for Phase 6");
        Add(CommandIds.TestRunAll, "Run All Tests", "Test", () => Task.CompletedTask, unavailable: "Planned for Phase 6");
        Add(CommandIds.ExtensionsOpen, "Extensions", "Extensions", () => Task.CompletedTask, unavailable: "Planned for Phase 7");
    }

    private static Task Run(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    /// <summary>Executes a command by id with uniform error reporting.</summary>
    private async Task RunCommandAsync(string id)
    {
        var command = _registry.Find(id);
        if (command is null)
        {
            return;
        }

        if (!command.IsAvailable)
        {
            if (command.UnavailableReason is { } reason)
            {
                await _dialogs.ShowMessageAsync(command.Title, $"{reason}. It is registered so you can find it, but it is not implemented yet.");
            }

            return;
        }

        try
        {
            await _registry.ExecuteAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command {CommandId} failed", id);
            await _dialogs.ShowMessageAsync(command.Title, $"The command failed: {ex.Message}");
        }
        finally
        {
            UpdateMenuState();
        }
    }

    private async void OnMenuCommand(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id })
        {
            await RunCommandAsync(id);
        }
    }

    private async void OnPaletteButtonClick(object sender, RoutedEventArgs e) => await RunCommandAsync(CommandIds.ViewCommandPalette);

    /// <summary>Enables or disables menu entries from the registry (called whenever relevant state changes).</summary>
    private void UpdateMenuState()
    {
        void Visit(IEnumerable<MenuFlyoutItemBase> items)
        {
            foreach (var item in items)
            {
                switch (item)
                {
                    case MenuFlyoutSubItem sub:
                        Visit(sub.Items);
                        break;
                    case MenuFlyoutItem { Tag: string id } entry:
                        entry.IsEnabled = _registry.Find(id)?.IsAvailable ?? false;
                        break;
                }
            }
        }

        foreach (var bar in MainMenu.Items)
        {
            Visit(bar.Items);
        }
    }

    // ------------------------------------------------------------------ command implementations

    private async Task OpenFileDialogAsync()
    {
        var path = await _pickers.PickFileAsync();
        if (path is not null)
        {
            await OpenFileAsync(path);
        }
    }

    private async Task OpenFolderDialogAsync()
    {
        var path = await _pickers.PickFolderAsync();
        if (path is not null)
        {
            await OpenFolderAsync(path);
        }
    }

    private async Task OpenFolderAsync(string path)
    {
        try
        {
            await _workspace.OpenFolderAsync(path);
            SetMessage($"Opened {_workspace.Current?.Name}");
        }
        catch (DirectoryNotFoundException ex)
        {
            if (await _dialogs.ConfirmAsync("Folder not found", $"{ex.Message}{Environment.NewLine}{Environment.NewLine}Remove it from the recent list?", "Remove"))
            {
                await _recents.RemoveAsync(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not open folder {Path}", path);
            await _dialogs.ShowMessageAsync("Cannot open folder", ex.Message);
        }

        await RefreshRecentsAsync();
    }

    private async Task SetThemeAsync(ThemeMode mode) =>
        await _settings.UpdateUserAsync(s => s with { Appearance = s.Appearance with { Theme = mode } });

    private async Task OpenSettingsAsync()
    {
        var (dialog, apply) = SettingsDialog.Create(_settings.User);
        if (await _dialogs.ShowAsync(dialog) == ContentDialogResult.Primary)
        {
            try
            {
                await _settings.UpdateUserAsync(apply);
                SetMessage("Settings saved");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await _dialogs.ShowMessageAsync("Settings were not saved", ex.Message);
            }
        }
    }

    private async Task OpenSettingsFileAsync()
    {
        // Saving the current values guarantees the file exists before it is opened.
        await _settings.UpdateUserAsync(s => s);
        await OpenFileAsync(_services.GetRequiredService<AppPaths>().SettingsFile);
    }

    private void OpenLogsFolder()
    {
        var logs = _services.GetRequiredService<AppPaths>().LogsDirectory;
        Directory.CreateDirectory(logs);
        Process.Start(new ProcessStartInfo(logs) { UseShellExecute = true });
    }

    private async Task ShowAboutAsync()
    {
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
        await _dialogs.ShowMessageAsync(
            "About Carbon Studio",
            $"Carbon Studio {version} (Phase 1 foundation)\n\n" +
            $"Runtime: {RuntimeInformation.FrameworkDescription}\n" +
            $"OS: {RuntimeInformation.OSDescription}\n\n" +
            "Implemented: folder workspaces, explorer, tabbed plain-text editing, themes, settings, command palette.\n" +
            "Not yet implemented: code editor features, templates, build/run, terminal, Git, language services, debugger.");
    }

    // ------------------------------------------------------------------ welcome screen

    private void WireWelcomeEvents()
    {
        Welcome.OpenFolderRequested += async (_, _) => await RunCommandAsync(CommandIds.FileOpenFolder);
        Welcome.OpenFileRequested += async (_, _) => await RunCommandAsync(CommandIds.FileOpen);
        Welcome.NewFileRequested += async (_, _) => await RunCommandAsync(CommandIds.FileNew);
        Welcome.PaletteRequested += async (_, _) => await RunCommandAsync(CommandIds.ViewCommandPalette);
        Welcome.RecentOpenRequested += async (_, path) => await OpenFolderAsync(path);
        Welcome.RecentRemoveRequested += async (_, path) =>
        {
            await _recents.RemoveAsync(path);
            await RefreshRecentsAsync();
        };
    }

    private async Task RefreshRecentsAsync()
    {
        try
        {
            Welcome.SetRecents(await _recents.GetAsync());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not read the recent projects list");
        }
    }

    // ------------------------------------------------------------------ command palette

    private void OpenPalette()
    {
        PaletteOverlay.Visibility = Visibility.Visible;
        PaletteInput.Text = string.Empty;
        RefreshPalette(string.Empty);
        _dispatcher.TryEnqueue(() => PaletteInput.Focus(FocusState.Programmatic));
    }

    private void ClosePalette()
    {
        PaletteOverlay.Visibility = Visibility.Collapsed;
        if (_documents.Active is not null)
        {
            _surface?.Focus();
        }
    }

    private void RefreshPalette(string query)
    {
        var matches = CommandPaletteSearch.Search(_registry.All, query);
        PaletteList.ItemsSource = matches.Select(c => new PaletteItem(c)).ToList();
        if (matches.Count > 0)
        {
            PaletteList.SelectedIndex = 0;
        }
    }

    private void OnPaletteTextChanged(object sender, TextChangedEventArgs e) => RefreshPalette(PaletteInput.Text);

    private async void OnPaletteKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Escape:
                ClosePalette();
                e.Handled = true;
                break;
            case VirtualKey.Down:
                PaletteList.SelectedIndex = Math.Min(PaletteList.SelectedIndex + 1, PaletteList.Items.Count - 1);
                PaletteList.ScrollIntoView(PaletteList.SelectedItem);
                e.Handled = true;
                break;
            case VirtualKey.Up:
                PaletteList.SelectedIndex = Math.Max(PaletteList.SelectedIndex - 1, 0);
                PaletteList.ScrollIntoView(PaletteList.SelectedItem);
                e.Handled = true;
                break;
            case VirtualKey.Enter:
                e.Handled = true;
                if (PaletteList.SelectedItem is PaletteItem selected)
                {
                    await ExecutePaletteItemAsync(selected);
                }

                break;
        }
    }

    private async void OnPaletteItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PaletteItem item)
        {
            await ExecutePaletteItemAsync(item);
        }
    }

    private async Task ExecutePaletteItemAsync(PaletteItem item)
    {
        ClosePalette();
        await RunCommandAsync(item.Command.Id);
    }

    private void OnPaletteScrimTapped(object sender, TappedRoutedEventArgs e) => ClosePalette();

    private void OnPaletteCardTapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;
}
