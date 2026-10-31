using CarbonStudio.Core.Documents;
using CarbonStudio.Core.Editor;
using CarbonStudio.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace CarbonStudio.App.Editor;

/// <summary>
/// Fallback editor: one TextBox per document. It supports the basics (undo/redo, clipboard, word wrap, fonts, tab key)
/// and nothing else, and says so through <see cref="Supports"/>. Used only when the Monaco editor cannot start.
/// </summary>
internal sealed class PlainTextEditorSurface : IEditorSurface
{
    private readonly ContentControl _host = new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch,
    };

    private readonly Dictionary<Guid, TextBox> _boxes = [];
    private EditorSettings _settings = new();
    private Guid? _activeId;
    private bool _suppress;

    public string Name => "Plain text (fallback)";

    public FrameworkElement View => _host;

    public event EventHandler<EditorTextEventArgs>? TextChanged;

    public event EventHandler<EditorCaretEventArgs>? CaretChanged;

    public event EventHandler<string>? ShortcutPressed
    {
        add { }
        remove { }
    }

    public bool Supports(EditorAction action) =>
        action is EditorAction.Undo or EditorAction.Redo or EditorAction.Cut or EditorAction.Copy or EditorAction.Paste or EditorAction.SelectAll;

    public void Open(DocumentSession document)
    {
        var box = new TextBox
        {
            AcceptsReturn = true,
            IsSpellCheckEnabled = false,
            IsTextPredictionEnabled = false,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            Padding = new Thickness(12, 8, 12, 8),
            Background = (Brush)Application.Current.Resources["CarbonEditorBrush"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(box, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, $"Editor for {document.DisplayName}");
        ApplyTo(box);

        _suppress = true;
        box.Text = document.Text.Replace('\n', '\r'); // TextBox separates lines with '\r'
        _suppress = false;

        var id = document.Id;
        box.TextChanged += (_, _) =>
        {
            if (!_suppress)
            {
                TextChanged?.Invoke(this, new EditorTextEventArgs(id, ToDocumentText(box.Text)));
                RaiseCaret(id, box);
            }
        };
        box.SelectionChanged += (_, _) => RaiseCaret(id, box);
        box.KeyDown += (_, e) =>
        {
            // TextBox moves focus on Tab; an editor should insert indentation instead.
            if (e.Key == VirtualKey.Tab && !IsDown(VirtualKey.Control) && !IsDown(VirtualKey.Shift))
            {
                var start = box.SelectionStart;
                var indent = _settings.InsertSpaces ? new string(' ', _settings.TabSize) : "\t";
                box.SelectedText = indent;
                box.SelectionStart = start + indent.Length;
                box.SelectionLength = 0;
                e.Handled = true;
            }
        };
        _boxes[id] = box;
    }

    public void Close(DocumentSession document)
    {
        _boxes.Remove(document.Id);
        if (_activeId == document.Id)
        {
            _activeId = null;
            _host.Content = null;
        }
    }

    public void Activate(DocumentSession? document, bool focus)
    {
        _activeId = document?.Id;
        if (document is null || !_boxes.TryGetValue(document.Id, out var box))
        {
            _host.Content = null;
            return;
        }

        _host.Content = box;
        RaiseCaret(document.Id, box);
        if (focus)
        {
            Focus();
        }
    }

    public void UpdateLanguage(DocumentSession document)
    {
        // Plain text has no languages.
    }

    public void ApplySettings(AppSettings settings, bool dark)
    {
        _settings = settings.Editor;
        foreach (var box in _boxes.Values)
        {
            ApplyTo(box);
        }
    }

    public Task<bool> FlushAsync() => Task.FromResult(true); // every keystroke is reported immediately

    public void Execute(EditorAction action)
    {
        if (_activeId is not { } id || !_boxes.TryGetValue(id, out var box))
        {
            return;
        }

        switch (action)
        {
            case EditorAction.Undo:
                box.Undo();
                break;
            case EditorAction.Redo:
                box.Redo();
                break;
            case EditorAction.Cut:
                box.CutSelectionToClipboard();
                break;
            case EditorAction.Copy:
                box.CopySelectionToClipboard();
                break;
            case EditorAction.Paste:
                box.PasteFromClipboard();
                break;
            case EditorAction.SelectAll:
                box.SelectAll();
                break;
        }
    }

    public void Focus()
    {
        if (_host.Content is TextBox box)
        {
            box.Focus(FocusState.Programmatic);
        }
    }

    private void ApplyTo(TextBox box)
    {
        box.FontFamily = new FontFamily(_settings.FontFamily);
        box.FontSize = _settings.FontSize;
        box.TextWrapping = _settings.WordWrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
    }

    private void RaiseCaret(Guid id, TextBox box)
    {
        var position = TextPosition.FromOffset(ToDocumentText(box.Text), box.SelectionStart);
        CaretChanged?.Invoke(this, new EditorCaretEventArgs(id, position.Line, position.Column));
    }

    private static string ToDocumentText(string editorText) =>
        editorText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static bool IsDown(VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
}
