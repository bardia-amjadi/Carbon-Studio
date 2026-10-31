using CarbonStudio.Core.Editor;
using CarbonStudio.Core.FileSystem;

namespace CarbonStudio.Core.Documents;

/// <summary>An open text document. Mutated only through <see cref="DocumentManager"/>.</summary>
public sealed class DocumentSession
{
    internal DocumentSession(string? path, string displayName, string text, TextFileFormat format, bool hadMixedLineEndings)
    {
        Path = path;
        DisplayName = displayName;
        Text = text;
        Format = format;
        HadMixedLineEndings = hadMixedLineEndings;
    }

    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>Full path, or null for an untitled document that has never been saved.</summary>
    public string? Path { get; internal set; }

    public string DisplayName { get; internal set; }

    /// <summary>Document text with '\n' line breaks. Line endings are re-applied from <see cref="Format"/> on save.</summary>
    public string Text { get; internal set; }

    public TextFileFormat Format { get; internal set; }

    public bool IsDirty { get; internal set; }

    public bool IsUntitled => Path is null;

    /// <summary>Editor language derived from the current file name.</summary>
    public LanguageInfo Language => LanguageCatalog.Detect(DisplayName);

    /// <summary>True when the file on disk mixed line-ending styles; saving normalises them.</summary>
    public bool HadMixedLineEndings { get; internal set; }
}

public enum DocumentChangeKind
{
    Opened,
    Closed,
    TextChanged,
    DirtyChanged,
    Saved,
    Renamed,
    FormatChanged,
}

public sealed class DocumentEventArgs(DocumentSession document, DocumentChangeKind kind) : EventArgs
{
    public DocumentSession Document { get; } = document;

    public DocumentChangeKind Kind { get; } = kind;
}
