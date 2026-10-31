using CarbonStudio.Core.Documents;
using CarbonStudio.Core.Editor;
using CarbonStudio.Core.Settings;
using Microsoft.UI.Xaml;

namespace CarbonStudio.App.Editor;

internal sealed class EditorTextEventArgs(Guid documentId, string text) : EventArgs
{
    public Guid DocumentId { get; } = documentId;

    /// <summary>Full document text with '\n' line breaks.</summary>
    public string Text { get; } = text;
}

internal sealed class EditorCaretEventArgs(Guid documentId, int line, int column) : EventArgs
{
    public Guid DocumentId { get; } = documentId;

    public int Line { get; } = line;

    public int Column { get; } = column;
}

/// <summary>
/// The control that displays and edits the open documents. One surface hosts every document (tab) so that only one
/// web view or text box exists. Two implementations: Monaco in WebView2, and a plain TextBox fallback used when the
/// WebView2 runtime or the editor bundle is unavailable.
/// </summary>
internal interface IEditorSurface
{
    string Name { get; }

    FrameworkElement View { get; }

    event EventHandler<EditorTextEventArgs>? TextChanged;

    event EventHandler<EditorCaretEventArgs>? CaretChanged;

    /// <summary>A key chord (for example "ctrl+s") that the application bound to a command while the editor had focus.</summary>
    event EventHandler<string>? ShortcutPressed;

    bool Supports(EditorAction action);

    void Open(DocumentSession document);

    void Close(DocumentSession document);

    /// <summary>Shows the document; null shows nothing.</summary>
    void Activate(DocumentSession? document, bool focus);

    void UpdateLanguage(DocumentSession document);

    void ApplySettings(AppSettings settings, bool dark);

    /// <summary>Makes the host application's copy of every document current. False means the editor did not respond.</summary>
    Task<bool> FlushAsync();

    void Execute(EditorAction action);

    void Focus();
}
