using CarbonStudio.App.Editor;
using CarbonStudio.App.Services;
using CarbonStudio.Core.Documents;
using CarbonStudio.Core.Editor;
using CarbonStudio.Core.FileSystem;
using CarbonStudio.Core.Paths;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CarbonStudio.App;

// Editor tabs and document operations. Documents are displayed by a single IEditorSurface (Monaco in WebView2, or a
// plain TextBox fallback); the TabView is only the tab strip.
public sealed partial class MainWindow
{
    private readonly Dictionary<Guid, TabViewItem> _tabs = [];
    private IEditorSurface? _surface;
    private bool _syncingSelection;

    private bool IsDark => RootGrid.ActualTheme != ElementTheme.Light;

    private void WireDocumentEvents()
    {
        _documents.DocumentChanged += (_, e) => RunOnUi(() => OnDocumentChanged(e));
        _documents.ActiveDocumentChanged += (_, _) => RunOnUi(SyncActiveDocument);
    }

    // ------------------------------------------------------------------ surface lifecycle

    private async Task InitializeEditorAsync()
    {
        EditorStartingText.Visibility = Visibility.Visible;
        var bundle = Path.Combine(AppContext.BaseDirectory, "Assets", "editor");
        string? fallbackReason = null;

        if (!File.Exists(Path.Combine(bundle, "index.html")))
        {
            fallbackReason = "the editor bundle (Assets\\editor) is missing from this build";
        }
        else if (MonacoEditorSurface.RuntimeVersion is not { } runtimeVersion)
        {
            fallbackReason = "the Microsoft Edge WebView2 Runtime is not installed (get it from https://developer.microsoft.com/microsoft-edge/webview2/)";
        }
        else
        {
            var monaco = new MonacoEditorSurface(bundle, _services.GetRequiredService<AppPaths>().WebViewDataDirectory, _shortcutMap, _logger);
            EditorHost.Children.Insert(0, monaco.View);
            try
            {
                await monaco.InitializeAsync();
                monaco.Failed += (_, reason) => RunOnUi(() => _ = OnEditorFailedAsync(reason));
                AttachSurface(monaco);
                _logger.LogInformation("Editor: Monaco ready (WebView2 runtime {Version})", runtimeVersion);
                return;
            }
            catch (Exception ex)
            {
                // Any failure to start the web editor must leave the app usable, so everything falls back to plain text.
                _logger.LogError(ex, "The Monaco editor could not start");
                EditorHost.Children.Remove(monaco.View);
                fallbackReason = ex.Message;
            }
        }

        _logger.LogWarning("Editor: using the basic plain-text editor because {Reason}", fallbackReason);
        var plain = new PlainTextEditorSurface();
        EditorHost.Children.Insert(0, plain.View);
        AttachSurface(plain);
        SetMessage("Basic text editor in use (see Output)");
    }

    private void AttachSurface(IEditorSurface surface)
    {
        _surface = surface;
        EditorStartingText.Visibility = Visibility.Collapsed;
        surface.TextChanged += OnSurfaceTextChanged;
        surface.CaretChanged += (_, e) =>
        {
            if (_documents.Active?.Id == e.DocumentId)
            {
                ViewModel.SetPosition(e.Line, e.Column);
            }
        };
        surface.ShortcutPressed += async (_, chord) =>
        {
            var commandId = _shortcutMap.Resolve(chord) ?? (chord == "f1" ? CommandIds.ViewCommandPalette : null);
            if (commandId is not null)
            {
                await RunCommandAsync(commandId);
            }
        };

        ApplyEditorSettings();
        foreach (var document in _documents.Documents)
        {
            surface.Open(document);
        }

        surface.Activate(_documents.Active, focus: false);
        UpdateMenuState();
    }

    private async Task OnEditorFailedAsync(string reason)
    {
        _logger.LogError("Monaco stopped ({Reason}); switching to the basic text editor", reason);
        if (_surface is { } broken)
        {
            EditorHost.Children.Remove(broken.View);
        }

        var plain = new PlainTextEditorSurface();
        EditorHost.Children.Insert(0, plain.View);
        AttachSurface(plain);
        await _dialogs.ShowMessageAsync(
            "Editor stopped",
            $"The editor engine stopped unexpectedly ({reason}). Carbon Studio switched to the basic text editor. " +
            "Changes made in the last second before the failure may be missing; please check your open files.");
    }

    private void OnSurfaceTextChanged(object? sender, EditorTextEventArgs e)
    {
        var document = _documents.Documents.FirstOrDefault(d => d.Id == e.DocumentId);
        if (document is not null)
        {
            _documents.UpdateText(document, e.Text);
        }
    }

    private void ApplyEditorSettings()
    {
        _surface?.ApplySettings(_settings.Effective, IsDark);
        WordWrapItem.IsChecked = _settings.Effective.Editor.WordWrap;
    }

    // ------------------------------------------------------------------ tabs

    private void OnDocumentChanged(DocumentEventArgs e)
    {
        switch (e.Kind)
        {
            case DocumentChangeKind.Opened:
                AddTab(e.Document);
                _surface?.Open(e.Document);
                break;
            case DocumentChangeKind.Closed:
                RemoveTab(e.Document);
                _surface?.Close(e.Document);
                break;
            case DocumentChangeKind.Renamed:
                UpdateTabHeader(e.Document);
                _surface?.UpdateLanguage(e.Document);
                break;
            case DocumentChangeKind.DirtyChanged:
            case DocumentChangeKind.Saved:
            case DocumentChangeKind.FormatChanged:
                UpdateTabHeader(e.Document);
                break;
        }

        UpdateMenuState();
    }

    private void AddTab(DocumentSession document)
    {
        var tab = new TabViewItem { Tag = document };
        _tabs[document.Id] = tab;
        UpdateTabHeader(document);
        EditorTabs.TabItems.Add(tab);
        Welcome.Visibility = Visibility.Collapsed;

        if (document.HadMixedLineEndings)
        {
            SetMessage($"{document.DisplayName} mixes line endings; saving will normalise them.");
        }
    }

    private void RemoveTab(DocumentSession document)
    {
        if (_tabs.Remove(document.Id, out var tab))
        {
            EditorTabs.TabItems.Remove(tab);
        }

        Welcome.Visibility = _documents.Documents.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateTabHeader(DocumentSession document)
    {
        if (_tabs.TryGetValue(document.Id, out var tab))
        {
            tab.Header = (document.IsDirty ? "● " : string.Empty) + document.DisplayName;
            ToolTipService.SetToolTip(tab, document.Path ?? "Unsaved document");
        }
    }

    private void SyncActiveDocument()
    {
        var active = _documents.Active;
        _syncingSelection = true;
        try
        {
            EditorTabs.SelectedItem = active is not null && _tabs.TryGetValue(active.Id, out var tab) ? tab : null;
        }
        finally
        {
            _syncingSelection = false;
        }

        _surface?.Activate(active, focus: true);
        if (active is null)
        {
            ViewModel.SetPosition(1, 1);
            ViewModel.PositionStatus = string.Empty;
        }

        UpdateMenuState();
    }

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection)
        {
            return;
        }

        if (EditorTabs.SelectedItem is TabViewItem { Tag: DocumentSession document } && _documents.Documents.Contains(document))
        {
            _documents.Active = document;
        }
    }

    private async void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Tab.Tag is DocumentSession document)
        {
            await CloseDocumentAsync(document);
        }
    }

    // ------------------------------------------------------------------ document operations

    /// <summary>
    /// Asks the editor to hand over any text it is still holding back. Returns false (after telling the user) when it does not
    /// answer, so that nothing is ever saved from a stale copy.
    /// </summary>
    private async Task<bool> FlushEditorAsync()
    {
        if (_surface is null || await _surface.FlushAsync())
        {
            return true;
        }

        await _dialogs.ShowMessageAsync(
            "Editor not responding",
            "The editor did not confirm its latest changes, so the operation was cancelled to avoid using stale text. Please try again in a moment.");
        return false;
    }

    private async Task OpenFileAsync(string path)
    {
        try
        {
            await _documents.OpenAsync(path);
        }
        catch (BinaryFileException ex)
        {
            await _dialogs.ShowMessageAsync("Cannot open file", ex.Message);
        }
        catch (FileTooLargeException ex)
        {
            await _dialogs.ShowMessageAsync("File too large", ex.Message + " The limit can be changed in Settings.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not open {Path}", path);
            await _dialogs.ShowMessageAsync("Cannot open file", ex.Message);
        }
    }

    /// <summary>Saves a document (prompting for a path when untitled). Returns false when cancelled or failed.</summary>
    private async Task<bool> SaveDocumentAsync(DocumentSession document)
    {
        if (!await FlushEditorAsync())
        {
            return false;
        }

        try
        {
            if (document.IsUntitled)
            {
                return await SaveDocumentAsAsync(document);
            }

            await _documents.SaveAsync(document);
            SetMessage($"Saved {document.DisplayName}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to save {Path}", document.Path);
            await _dialogs.ShowMessageAsync("Save failed", ex.Message);
            return false;
        }
    }

    private async Task<bool> SaveDocumentAsAsync(DocumentSession document)
    {
        if (!await FlushEditorAsync())
        {
            return false;
        }

        var path = await _pickers.PickSavePathAsync(document.DisplayName);
        if (path is null)
        {
            return false;
        }

        try
        {
            await _documents.SaveAsAsync(document, path);
            SetMessage($"Saved {document.DisplayName}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to save {Path}", path);
            await _dialogs.ShowMessageAsync("Save failed", ex.Message);
            return false;
        }
    }

    private async Task SaveAllAsync()
    {
        if (!await FlushEditorAsync())
        {
            return;
        }

        var failures = await _documents.SaveAllAsync();
        if (failures.Count > 0)
        {
            var names = string.Join(Environment.NewLine, failures.Select(f => $"{f.Document.DisplayName}: {f.Error.Message}"));
            await _dialogs.ShowMessageAsync("Some files could not be saved", names);
        }
        else
        {
            SetMessage("Saved all files");
        }
    }

    private async Task<bool> CloseDocumentAsync(DocumentSession document)
    {
        // Pending edits must reach the document first, otherwise a just-typed change would not count as unsaved.
        if (_surface is not null && !await _surface.FlushAsync())
        {
            if (!await _dialogs.ConfirmAsync("Editor not responding", $"Close {document.DisplayName} without confirming its latest edits?", "Close anyway"))
            {
                return false;
            }
        }

        if (document.IsDirty)
        {
            _documents.Active = document;
            var choice = await _dialogs.AskSaveAsync($"Do you want to save the changes you made to {document.DisplayName}?");
            if (choice == SaveChoice.Cancel || (choice == SaveChoice.Save && !await SaveDocumentAsync(document)))
            {
                return false;
            }
        }

        _documents.Close(document);
        return true;
    }

    // ------------------------------------------------------------------ format commands

    private async Task SetLineEndingAsync(LineEnding lineEnding)
    {
        if (_documents.Active is { } document)
        {
            _documents.SetLineEnding(document, lineEnding);
            SetMessage($"Line endings set to {document.Format.LineEndingDisplayName}; save to apply");
        }

        await Task.CompletedTask;
    }

    private async Task SetEncodingAsync(TextEncodingKind encoding, bool withBom)
    {
        if (_documents.Active is not { } document || !await FlushEditorAsync())
        {
            return;
        }

        try
        {
            _documents.SetEncoding(document, encoding, withBom);
            SetMessage($"Encoding set to {document.Format.EncodingDisplayName}; save to apply");
        }
        catch (UnrepresentableTextException ex)
        {
            await _dialogs.ShowMessageAsync("Cannot change encoding", ex.Message);
        }
    }
}
