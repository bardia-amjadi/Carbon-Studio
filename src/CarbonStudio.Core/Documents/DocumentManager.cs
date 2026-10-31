using CarbonStudio.Core.FileSystem;
using CarbonStudio.Core.Paths;
using CarbonStudio.Core.Settings;
using Microsoft.Extensions.Logging;

namespace CarbonStudio.Core.Documents;

/// <summary>
/// Tracks open documents and performs load/save. Not thread-safe: call from a single (UI) thread.
/// Awaits deliberately resume on the caller's context (no ConfigureAwait(false)) so that events are raised on that same thread.
/// </summary>
public sealed class DocumentManager
{
    private readonly ISettingsService _settings;
    private readonly ILogger<DocumentManager> _logger;
    private readonly List<DocumentSession> _documents = [];
    private int _untitledCounter;
    private DocumentSession? _active;

    public DocumentManager(ISettingsService settings, ILogger<DocumentManager> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public event EventHandler<DocumentEventArgs>? DocumentChanged;

    public event EventHandler? ActiveDocumentChanged;

    public IReadOnlyList<DocumentSession> Documents => _documents;

    public DocumentSession? Active
    {
        get => _active;
        set
        {
            if (value is not null && !_documents.Contains(value))
            {
                throw new ArgumentException("The document is not open.", nameof(value));
            }

            if (!ReferenceEquals(_active, value))
            {
                _active = value;
                ActiveDocumentChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool HasUnsavedChanges => _documents.Any(d => d.IsDirty);

    public DocumentSession? FindByPath(string path) =>
        _documents.FirstOrDefault(d => d.Path is not null && PathGuard.PathEquals(d.Path, path));

    /// <summary>Opens a file (or activates it when already open).</summary>
    /// <exception cref="BinaryFileException"/>
    /// <exception cref="FileTooLargeException"/>
    public async Task<DocumentSession> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        var full = Path.GetFullPath(path);
        var existing = FindByPath(full);
        if (existing is not null)
        {
            Active = existing;
            return existing;
        }

        var limit = (long)_settings.Effective.Files.MaxOpenFileSizeMb * 1024 * 1024;
        var content = await TextFile.ReadAsync(full, limit, cancellationToken);

        // The same file may have been opened concurrently while we were reading.
        existing = FindByPath(full);
        if (existing is not null)
        {
            Active = existing;
            return existing;
        }

        var session = new DocumentSession(full, Path.GetFileName(full), content.Text, content.Format, content.HadMixedLineEndings);
        _documents.Add(session);
        _logger.LogInformation("Opened {Path} as {Encoding}/{LineEnding}", full, content.Format.EncodingDisplayName, content.Format.LineEndingDisplayName);
        DocumentChanged?.Invoke(this, new DocumentEventArgs(session, DocumentChangeKind.Opened));
        Active = session;
        return session;
    }

    public DocumentSession CreateUntitled()
    {
        _untitledCounter++;
        var lineEnding = _settings.Effective.Editor.NewFileLineEnding switch
        {
            NewFileLineEnding.Lf => LineEnding.Lf,
            NewFileLineEnding.CrLf => LineEnding.CrLf,
            _ => TextFileFormat.Default.LineEnding,
        };
        var format = TextFileFormat.Default with { LineEnding = lineEnding };
        var session = new DocumentSession(null, $"Untitled-{_untitledCounter}", string.Empty, format, hadMixedLineEndings: false);
        _documents.Add(session);
        DocumentChanged?.Invoke(this, new DocumentEventArgs(session, DocumentChangeKind.Opened));
        Active = session;
        return session;
    }

    /// <summary>Changes the line ending used when the document is saved. The file counts as modified.</summary>
    public void SetLineEnding(DocumentSession document, LineEnding lineEnding)
    {
        EnsureOpen(document);
        ApplyFormat(document, document.Format with { LineEnding = lineEnding });
        document.HadMixedLineEndings = false;
    }

    /// <summary>
    /// Changes the encoding used when the document is saved. UTF-16 and UTF-32 always carry a byte-order mark (they cannot be
    /// detected without one), Latin-1 never does.
    /// </summary>
    /// <exception cref="UnrepresentableTextException">The text contains characters the encoding cannot store.</exception>
    public void SetEncoding(DocumentSession document, TextEncodingKind encoding, bool withBom = false)
    {
        EnsureOpen(document);
        var bom = encoding switch
        {
            TextEncodingKind.Utf8 => withBom,
            TextEncodingKind.Latin1 => false,
            _ => true,
        };

        var format = document.Format with { Encoding = encoding, HasBom = bom };
        if (!TextFile.CanRepresent(document.Text, format))
        {
            throw new UnrepresentableTextException(format);
        }

        ApplyFormat(document, format);
    }

    private void ApplyFormat(DocumentSession document, TextFileFormat format)
    {
        if (document.Format == format)
        {
            return;
        }

        document.Format = format;
        DocumentChanged?.Invoke(this, new DocumentEventArgs(document, DocumentChangeKind.FormatChanged));

        if (!document.IsDirty)
        {
            document.IsDirty = true;
            DocumentChanged?.Invoke(this, new DocumentEventArgs(document, DocumentChangeKind.DirtyChanged));
        }
    }

    /// <summary>Records an edit. Line breaks in <paramref name="newText"/> must already be '\n'.</summary>
    public void UpdateText(DocumentSession document, string newText)
    {
        EnsureOpen(document);
        if (string.Equals(document.Text, newText, StringComparison.Ordinal))
        {
            return;
        }

        document.Text = newText;
        DocumentChanged?.Invoke(this, new DocumentEventArgs(document, DocumentChangeKind.TextChanged));

        if (!document.IsDirty)
        {
            document.IsDirty = true;
            DocumentChanged?.Invoke(this, new DocumentEventArgs(document, DocumentChangeKind.DirtyChanged));
        }
    }

    /// <exception cref="InvalidOperationException">The document has no path; use <see cref="SaveAsAsync"/>.</exception>
    public async Task SaveAsync(DocumentSession document, CancellationToken cancellationToken = default)
    {
        EnsureOpen(document);
        if (document.Path is null)
        {
            throw new InvalidOperationException("An untitled document must be saved with Save As.");
        }

        await TextFile.WriteAsync(document.Path, document.Text, document.Format, cancellationToken);
        MarkSaved(document);
    }

    public async Task SaveAsAsync(DocumentSession document, string path, CancellationToken cancellationToken = default)
    {
        EnsureOpen(document);
        var full = Path.GetFullPath(path);

        var other = FindByPath(full);
        if (other is not null && !ReferenceEquals(other, document))
        {
            throw new IOException($"'{Path.GetFileName(full)}' is already open in another tab. Close it first.");
        }

        await TextFile.WriteAsync(full, document.Text, document.Format, cancellationToken);

        var renamed = document.Path is null || !PathGuard.PathEquals(document.Path, full);
        document.Path = full;
        document.DisplayName = Path.GetFileName(full);
        document.HadMixedLineEndings = false;
        MarkSaved(document);

        if (renamed)
        {
            DocumentChanged?.Invoke(this, new DocumentEventArgs(document, DocumentChangeKind.Renamed));
        }
    }

    /// <summary>Saves every dirty document that already has a path; returns the documents that failed with their errors.</summary>
    public async Task<IReadOnlyList<(DocumentSession Document, Exception Error)>> SaveAllAsync(CancellationToken cancellationToken = default)
    {
        var failures = new List<(DocumentSession, Exception)>();
        foreach (var document in _documents.Where(d => d.IsDirty && !d.IsUntitled).ToList())
        {
            try
            {
                await SaveAsync(document, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "Failed to save {Path}", document.Path);
                failures.Add((document, ex));
            }
        }

        return failures;
    }

    /// <summary>Closes without saving; callers must have confirmed discarding unsaved changes.</summary>
    public void Close(DocumentSession document)
    {
        var index = _documents.IndexOf(document);
        if (index < 0)
        {
            return;
        }

        _documents.RemoveAt(index);
        DocumentChanged?.Invoke(this, new DocumentEventArgs(document, DocumentChangeKind.Closed));

        if (ReferenceEquals(_active, document))
        {
            Active = _documents.Count == 0 ? null : _documents[Math.Min(index, _documents.Count - 1)];
        }
    }

    /// <summary>Updates open documents after a file or folder was renamed on disk.</summary>
    public void NotifyPathRenamed(string oldPath, string newPath)
    {
        var oldFull = PathGuard.Normalize(oldPath);
        var newFull = PathGuard.Normalize(newPath);

        foreach (var document in _documents.Where(d => d.Path is not null).ToList())
        {
            var current = document.Path!;
            string? updated = null;

            if (PathGuard.PathEquals(current, oldFull))
            {
                updated = newFull;
            }
            else if (PathGuard.IsInside(oldFull, current, allowRoot: false))
            {
                updated = Path.Combine(newFull, Path.GetRelativePath(oldFull, current));
            }

            if (updated is not null)
            {
                document.Path = updated;
                document.DisplayName = Path.GetFileName(updated);
                DocumentChanged?.Invoke(this, new DocumentEventArgs(document, DocumentChangeKind.Renamed));
            }
        }
    }

    private void MarkSaved(DocumentSession document)
    {
        var wasDirty = document.IsDirty;
        document.IsDirty = false;
        DocumentChanged?.Invoke(this, new DocumentEventArgs(document, DocumentChangeKind.Saved));
        if (wasDirty)
        {
            DocumentChanged?.Invoke(this, new DocumentEventArgs(document, DocumentChangeKind.DirtyChanged));
        }
    }

    private void EnsureOpen(DocumentSession document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!_documents.Contains(document))
        {
            throw new InvalidOperationException("The document is not open.");
        }
    }
}
