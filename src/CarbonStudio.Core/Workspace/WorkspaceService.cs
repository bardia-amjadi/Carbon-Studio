using CarbonStudio.Core.Paths;
using CarbonStudio.Core.Settings;
using Microsoft.Extensions.Logging;

namespace CarbonStudio.Core.Workspace;

public sealed record WorkspaceInfo(string RootPath, string Name);

public sealed class WorkspaceChangedEventArgs(WorkspaceInfo? previous, WorkspaceInfo? current) : EventArgs
{
    public WorkspaceInfo? Previous { get; } = previous;

    public WorkspaceInfo? Current { get; } = current;
}

public interface IWorkspaceService
{
    WorkspaceInfo? Current { get; }

    IReadOnlyList<DetectedProject> Projects { get; }

    event EventHandler<WorkspaceChangedEventArgs>? Changed;

    /// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder cannot be read.</exception>
    Task<WorkspaceInfo> OpenFolderAsync(string path, CancellationToken cancellationToken = default);

    Task CloseAsync(CancellationToken cancellationToken = default);
}

/// <summary>Holds the single currently open folder workspace (multi-workspace support is planned for a later phase).</summary>
public sealed class WorkspaceService : IWorkspaceService
{
    private readonly ISettingsService _settings;
    private readonly IRecentProjectsStore _recents;
    private readonly IProjectDetector _detector;
    private readonly ILogger<WorkspaceService> _logger;

    public WorkspaceService(
        ISettingsService settings,
        IRecentProjectsStore recents,
        IProjectDetector detector,
        ILogger<WorkspaceService> logger)
    {
        _settings = settings;
        _recents = recents;
        _detector = detector;
        _logger = logger;
    }

    public event EventHandler<WorkspaceChangedEventArgs>? Changed;

    public WorkspaceInfo? Current { get; private set; }

    public IReadOnlyList<DetectedProject> Projects { get; private set; } = [];

    public async Task<WorkspaceInfo> OpenFolderAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var full = PathGuard.Normalize(path);
        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException($"The folder '{full}' does not exist.");
        }

        // Fail early with a clear error if the folder cannot be listed at all.
        _ = Directory.EnumerateFileSystemEntries(full).FirstOrDefault();

        var projects = await Task.Run(() => _detector.Detect(full, cancellationToken), cancellationToken).ConfigureAwait(false);
        await _settings.SetWorkspaceAsync(full, cancellationToken).ConfigureAwait(false);

        try
        {
            await _recents.AddAsync(full, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A failure to record history must not prevent the folder from opening.
            _logger.LogWarning(ex, "Could not update the recent projects list");
        }

        var previous = Current;
        Current = new WorkspaceInfo(full, RecentProjectsStore.NameOf(full));
        Projects = projects;

        _logger.LogInformation("Opened workspace {Path} ({Count} project marker(s))", full, projects.Count);
        Changed?.Invoke(this, new WorkspaceChangedEventArgs(previous, Current));
        return Current;
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (Current is null)
        {
            return;
        }

        var previous = Current;
        await _settings.SetWorkspaceAsync(null, cancellationToken).ConfigureAwait(false);
        Current = null;
        Projects = [];
        Changed?.Invoke(this, new WorkspaceChangedEventArgs(previous, null));
    }
}
