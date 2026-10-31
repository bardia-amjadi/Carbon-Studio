using CarbonStudio.Core.Documents;
using CarbonStudio.Core.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CarbonStudio.App.ViewModels;

/// <summary>Observable shell state: status bar text, window title and which panels are visible.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IWorkspaceService _workspace;
    private readonly DocumentManager _documents;

    public MainViewModel(IWorkspaceService workspace, DocumentManager documents)
    {
        _workspace = workspace;
        _documents = documents;
        _workspace.Changed += (_, _) => RefreshWorkspace();
        _documents.ActiveDocumentChanged += (_, _) => RefreshDocument();
        _documents.DocumentChanged += (_, e) =>
        {
            if (ReferenceEquals(e.Document, _documents.Active) &&
                e.Kind is DocumentChangeKind.Renamed or DocumentChangeKind.Saved or DocumentChangeKind.DirtyChanged or DocumentChangeKind.FormatChanged)
            {
                RefreshDocument();
            }
        };

        // Defaults are assigned here rather than as property initializers to keep the source generator simple.
        PositionStatus = string.Empty;
        EncodingStatus = string.Empty;
        LineEndingStatus = string.Empty;
        LanguageStatus = string.Empty;
        MessageStatus = "Ready";
        RefreshWorkspace();
        RefreshDocument();
    }

    [ObservableProperty]
    public partial string WindowTitle { get; set; }

    [ObservableProperty]
    public partial string WorkspaceStatus { get; set; }

    [ObservableProperty]
    public partial string PositionStatus { get; set; }

    [ObservableProperty]
    public partial string EncodingStatus { get; set; }

    [ObservableProperty]
    public partial string LineEndingStatus { get; set; }

    [ObservableProperty]
    public partial string LanguageStatus { get; set; }

    [ObservableProperty]
    public partial string MessageStatus { get; set; }

    public bool HasWorkspace => _workspace.Current is not null;

    /// <summary>Updates the caret position shown in the status bar (reported by the editor for the active document).</summary>
    public void SetPosition(int line, int column) => PositionStatus = new TextPosition(line, column).ToString();

    private void RefreshWorkspace()
    {
        var current = _workspace.Current;
        WorkspaceStatus = current is null
            ? "No folder open"
            : $"{current.Name}  ·  {WorkspaceSummary.Describe(_workspace.Projects)}";
        OnPropertyChanged(nameof(HasWorkspace));
        UpdateTitle();
    }

    private void RefreshDocument()
    {
        var active = _documents.Active;
        EncodingStatus = active?.Format.EncodingDisplayName ?? string.Empty;
        LineEndingStatus = active?.Format.LineEndingDisplayName ?? string.Empty;
        LanguageStatus = active?.Language.DisplayName ?? string.Empty;
        if (active is null)
        {
            PositionStatus = string.Empty;
        }

        UpdateTitle();
    }

    private void UpdateTitle()
    {
        var parts = new List<string>();
        var active = _documents.Active;
        if (active is not null)
        {
            parts.Add((active.IsDirty ? "● " : string.Empty) + active.DisplayName);
        }

        if (_workspace.Current is { } workspace)
        {
            parts.Add(workspace.Name);
        }

        parts.Add("Carbon Studio");
        WindowTitle = string.Join(" — ", parts);
    }
}
