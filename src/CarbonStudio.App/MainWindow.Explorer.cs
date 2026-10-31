using System.Diagnostics;
using CarbonStudio.App.Services;
using CarbonStudio.App.Models;
using CarbonStudio.Core.Paths;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace CarbonStudio.App;

// Explorer tree: lazy-loaded folders, file operations confined to the workspace root.
public sealed partial class MainWindow
{
    private readonly HashSet<string> _expandedPaths = new(PathGuard.Comparer);

    private string? WorkspaceRoot => _workspace.Current?.RootPath;

    private void RebuildExplorer()
    {
        ExplorerTree.RootNodes.Clear();
        var workspace = _workspace.Current;
        var hasWorkspace = workspace is not null;

        NoFolderPanel.Visibility = hasWorkspace ? Visibility.Collapsed : Visibility.Visible;
        ExplorerTree.Visibility = hasWorkspace ? Visibility.Visible : Visibility.Collapsed;
        NewFileButton.IsEnabled = hasWorkspace;
        NewFolderButton.IsEnabled = hasWorkspace;
        RefreshButton.IsEnabled = hasWorkspace;
        ExplorerTitle.Text = workspace is null ? "EXPLORER" : workspace.Name.ToUpperInvariant();

        _expandedPaths.Clear();
        if (workspace is null)
        {
            return;
        }

        var root = new ExplorerItem(workspace.Name, workspace.RootPath, isDirectory: true);
        var node = new TreeViewNode { Content = root, HasUnrealizedChildren = true };
        root.Node = node;
        ExplorerTree.RootNodes.Add(node);
        node.IsExpanded = true; // raises Expanding, which loads the first level
    }

    private async void OnExplorerExpanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (args.Node.Content is not ExplorerItem { IsDirectory: true } item)
        {
            return;
        }

        _expandedPaths.Add(item.FullPath);
        if (args.Node.HasUnrealizedChildren)
        {
            await LoadChildrenAsync(args.Node);
        }
    }

    private void OnExplorerCollapsed(TreeView sender, TreeViewCollapsedEventArgs args)
    {
        if (args.Node.Content is ExplorerItem item)
        {
            _expandedPaths.Remove(item.FullPath);
        }
    }

    private async Task LoadChildrenAsync(TreeViewNode node)
    {
        if (node.Content is not ExplorerItem { IsDirectory: true } item || item.IsLoading)
        {
            return;
        }

        item.IsLoading = true;
        try
        {
            var excluded = _settings.Effective.Explorer.ExcludedNames;
            var entries = await Task.Run(() => _fs.ListDirectory(item.FullPath, excluded));

            node.Children.Clear();
            foreach (var entry in entries)
            {
                var child = new ExplorerItem(entry.Name, entry.FullPath, entry.IsDirectory);
                var childNode = new TreeViewNode { Content = child, HasUnrealizedChildren = entry.IsDirectory };
                child.Node = childNode;
                node.Children.Add(childNode);
            }

            node.HasUnrealizedChildren = false;

            // Re-expand folders the user had open before a refresh.
            foreach (var childNode in node.Children.Where(n => n.Content is ExplorerItem { IsDirectory: true } c && _expandedPaths.Contains(c.FullPath)))
            {
                childNode.IsExpanded = true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not list {Path}", item.FullPath);
            node.Children.Clear();
            node.HasUnrealizedChildren = false;
            SetMessage($"Cannot read folder '{item.Name}': {ex.Message}");
        }
        finally
        {
            item.IsLoading = false;
        }
    }

    private async void OnExplorerItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        var item = args.InvokedItem as ExplorerItem ?? (args.InvokedItem as TreeViewNode)?.Content as ExplorerItem;
        if (item is null)
        {
            return;
        }

        if (item.IsDirectory)
        {
            if (item.Node is { } node)
            {
                node.IsExpanded = !node.IsExpanded;
            }
        }
        else
        {
            await OpenFileAsync(item.FullPath);
        }
    }

    // ------------------------------------------------------------------ toolbar and context menu

    private async void OnExplorerNewFileClick(object sender, RoutedEventArgs e) => await RunCommandAsync(CommandIds.ExplorerNewFile);

    private async void OnExplorerNewFolderClick(object sender, RoutedEventArgs e) => await RunCommandAsync(CommandIds.ExplorerNewFolder);

    private async void OnExplorerRefreshClick(object sender, RoutedEventArgs e) => await RunCommandAsync(CommandIds.ExplorerRefresh);

    private async void OnOpenFolderButtonClick(object sender, RoutedEventArgs e) => await RunCommandAsync(CommandIds.FileOpenFolder);

    private ExplorerItem? SelectedExplorerItem => (ExplorerTree.SelectedNode?.Content as ExplorerItem) ?? WorkspaceRootItem;

    private ExplorerItem? WorkspaceRootItem => ExplorerTree.RootNodes.FirstOrDefault()?.Content as ExplorerItem;

    private void OnExplorerRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var item = ItemFromElement(e.OriginalSource as DependencyObject);
        if (item is null)
        {
            return;
        }

        if (item.Node is { } node)
        {
            ExplorerTree.SelectedNode = node;
        }

        var flyout = new MenuFlyout();
        if (!item.IsDirectory)
        {
            flyout.Items.Add(MenuItem("Open", async () => await OpenFileAsync(item.FullPath)));
            flyout.Items.Add(new MenuFlyoutSeparator());
        }

        flyout.Items.Add(MenuItem("New File…", async () => await CreateEntryAsync(item, isFolder: false)));
        flyout.Items.Add(MenuItem("New Folder…", async () => await CreateEntryAsync(item, isFolder: true)));
        flyout.Items.Add(new MenuFlyoutSeparator());

        var isRoot = PathGuard.PathEquals(item.FullPath, WorkspaceRoot ?? string.Empty);
        if (!isRoot)
        {
            flyout.Items.Add(MenuItem("Rename…", async () => await RenameEntryAsync(item)));
            flyout.Items.Add(MenuItem("Delete…", async () => await DeleteEntryAsync(item)));
            flyout.Items.Add(new MenuFlyoutSeparator());
        }

        flyout.Items.Add(MenuItem("Copy Path", () =>
        {
            var package = new DataPackage();
            package.SetText(item.FullPath);
            Clipboard.SetContent(package);
            return Task.CompletedTask;
        }));
        flyout.Items.Add(MenuItem("Reveal in File Explorer", () =>
        {
            RevealInFileExplorer(item);
            return Task.CompletedTask;
        }));
        flyout.Items.Add(MenuItem("Refresh", async () => await RefreshExplorerAsync()));

        flyout.ShowAt(ExplorerTree, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = e.GetPosition(ExplorerTree) });
        e.Handled = true;
    }

    private MenuFlyoutItem MenuItem(string text, Func<Task> action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += async (_, _) =>
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Explorer action '{Action}' failed", text);
                await _dialogs.ShowMessageAsync(text.TrimEnd('…'), ex.Message);
            }
        };
        return item;
    }

    private ExplorerItem? ItemFromElement(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is TreeViewItem container)
            {
                return ExplorerTree.NodeFromContainer(container)?.Content as ExplorerItem;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    // ------------------------------------------------------------------ file operations

    /// <summary>The folder that new entries should be created in for the given item.</summary>
    private static TreeViewNode? FolderNodeFor(ExplorerItem item) =>
        item.IsDirectory ? item.Node : item.Node?.Parent;

    private async Task CreateEntryAsync(ExplorerItem? target, bool isFolder)
    {
        var root = WorkspaceRoot;
        var folderNode = target is null ? ExplorerTree.RootNodes.FirstOrDefault() : FolderNodeFor(target);
        if (root is null || folderNode?.Content is not ExplorerItem folder)
        {
            return;
        }

        var name = await _dialogs.PromptAsync(isFolder ? "New folder" : "New file", isFolder ? "Folder name" : "File name", confirmText: "Create");
        if (name is null)
        {
            return;
        }

        string created;
        try
        {
            created = isFolder ? _fs.CreateFolder(root, folder.FullPath, name) : _fs.CreateFile(root, folder.FullPath, name);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync(isFolder ? "Cannot create folder" : "Cannot create file", ex.Message);
            return;
        }

        _expandedPaths.Add(folder.FullPath);
        await LoadChildrenAsync(folderNode);
        folderNode.IsExpanded = true;

        if (!isFolder)
        {
            await OpenFileAsync(created);
        }
    }

    private async Task RenameEntryAsync(ExplorerItem item)
    {
        var root = WorkspaceRoot;
        if (root is null)
        {
            return;
        }

        var newName = await _dialogs.PromptAsync("Rename", "New name", item.Name, "Rename");
        if (newName is null || newName == item.Name)
        {
            return;
        }

        string newPath;
        try
        {
            newPath = _fs.Rename(root, item.FullPath, newName);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync("Cannot rename", ex.Message);
            return;
        }

        _documents.NotifyPathRenamed(item.FullPath, newPath);
        RemapExpandedPaths(item.FullPath, newPath);

        if (item.Node?.Parent is { } parent)
        {
            await LoadChildrenAsync(parent);
        }
    }

    private void RemapExpandedPaths(string oldPath, string newPath)
    {
        foreach (var path in _expandedPaths.Where(p => PathGuard.IsInside(oldPath, p)).ToList())
        {
            _expandedPaths.Remove(path);
            _expandedPaths.Add(Path.Combine(newPath, Path.GetRelativePath(oldPath, path)));
        }
    }

    private async Task DeleteEntryAsync(ExplorerItem item)
    {
        var root = WorkspaceRoot;
        if (root is null)
        {
            return;
        }

        var openCount = _documents.Documents.Count(d => d.Path is not null && PathGuard.IsInside(item.FullPath, d.Path));
        var message = $"Move '{item.Name}' to the Recycle Bin?";
        if (openCount > 0)
        {
            message += $"{Environment.NewLine}{Environment.NewLine}{openCount} open tab(s) belong to this item. They stay open; saving one would recreate the file.";
        }

        if (!await _dialogs.ConfirmAsync("Delete", message, "Delete"))
        {
            return;
        }

        try
        {
            await Task.Run(() => _fs.Delete(root, item.FullPath, sendToRecycleBin: true));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync("Cannot delete", ex.Message);
            return;
        }

        if (item.Node?.Parent is { } parent)
        {
            await LoadChildrenAsync(parent);
        }
    }

    private async Task RefreshExplorerAsync()
    {
        if (ExplorerTree.RootNodes.FirstOrDefault() is { } root)
        {
            await LoadChildrenAsync(root);
            SetMessage("Explorer refreshed");
        }
    }

    private void RevealInFileExplorer(ExplorerItem item)
    {
        // Paths come from the file system and cannot contain quotes on Windows, so quoting is safe.
        var arguments = item.IsDirectory ? $"\"{item.FullPath}\"" : $"/select,\"{item.FullPath}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = false });
    }
}
