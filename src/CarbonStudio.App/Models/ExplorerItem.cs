using Microsoft.UI.Xaml.Controls;

namespace CarbonStudio.App.Models;

/// <summary>Content object attached to each explorer <see cref="TreeViewNode"/>.</summary>
public sealed class ExplorerItem
{
    public ExplorerItem(string name, string fullPath, bool isDirectory)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
    }

    public string Name { get; }

    public string FullPath { get; }

    public bool IsDirectory { get; }

    public TreeViewNode? Node { get; set; }

    /// <summary>True while children are being read so that quick repeated expansion does not load twice.</summary>
    public bool IsLoading { get; set; }

    // Segoe Fluent Icons / Segoe MDL2 Assets: FolderOpen and Document.
    public string Glyph => IsDirectory ? "\uE838" : "\uE8A5";

    public override string ToString() => Name;
}
