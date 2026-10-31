namespace CarbonStudio.Core.Editor;

/// <summary>Editing actions the application can ask the editor surface to perform.</summary>
public enum EditorAction
{
    Undo,
    Redo,
    Cut,
    Copy,
    Paste,
    SelectAll,
    Find,
    Replace,
    GoToLine,
    FormatDocument,
    TrimTrailingWhitespace,
}

public static class EditorActionExtensions
{
    /// <summary>Name used on the wire; must match a key of ACTIONS in Editor/web/src/bridge.js (checked by a test).</summary>
    public static string WireName(this EditorAction action) => action switch
    {
        EditorAction.Undo => "undo",
        EditorAction.Redo => "redo",
        EditorAction.Cut => "cut",
        EditorAction.Copy => "copy",
        EditorAction.Paste => "paste",
        EditorAction.SelectAll => "selectAll",
        EditorAction.Find => "find",
        EditorAction.Replace => "replace",
        EditorAction.GoToLine => "goToLine",
        EditorAction.FormatDocument => "formatDocument",
        EditorAction.TrimTrailingWhitespace => "trimTrailingWhitespace",
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };
}
