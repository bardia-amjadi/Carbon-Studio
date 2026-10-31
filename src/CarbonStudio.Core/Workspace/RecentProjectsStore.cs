using System.Text.Json;
using CarbonStudio.Core.Paths;
using Microsoft.Extensions.Logging;

namespace CarbonStudio.Core.Workspace;

public sealed record RecentProject(string Path, string Name, DateTimeOffset LastOpened);

public interface IRecentProjectsStore
{
    /// <summary>Most recent first.</summary>
    Task<IReadOnlyList<RecentProject>> GetAsync(CancellationToken cancellationToken = default);

    Task AddAsync(string path, CancellationToken cancellationToken = default);

    Task RemoveAsync(string path, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>JSON-file-backed recent project list (deduplicated, capped, most recent first).</summary>
public sealed class RecentProjectsStore : IRecentProjectsStore
{
    public const int MaxEntries = 20;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly AppPaths _paths;
    private readonly ILogger<RecentProjectsStore> _logger;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RecentProjectsStore(AppPaths paths, ILogger<RecentProjectsStore> logger, TimeProvider? time = null)
    {
        _paths = paths;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<RecentProject>> GetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task AddAsync(string path, CancellationToken cancellationToken = default) =>
        MutateAsync(
            items =>
            {
                var full = PathGuard.Normalize(path);
                items.RemoveAll(i => PathGuard.PathEquals(i.Path, full));
                items.Insert(0, new RecentProject(full, NameOf(full), _time.GetUtcNow()));
                if (items.Count > MaxEntries)
                {
                    items.RemoveRange(MaxEntries, items.Count - MaxEntries);
                }
            },
            cancellationToken);

    public Task RemoveAsync(string path, CancellationToken cancellationToken = default) =>
        MutateAsync(items => items.RemoveAll(i => PathGuard.PathEquals(i.Path, path)), cancellationToken);

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        MutateAsync(items => items.Clear(), cancellationToken);

    internal static string NameOf(string fullPath)
    {
        var name = System.IO.Path.GetFileName(fullPath);
        return string.IsNullOrEmpty(name) ? fullPath : name;
    }

    private async Task MutateAsync(Action<List<RecentProject>> mutate, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var items = (await LoadAsync(cancellationToken).ConfigureAwait(false)).ToList();
            mutate(items);
            var json = JsonSerializer.Serialize(new Document(1, items), JsonOptions);
            await AtomicFile.WriteAllTextAsync(_paths.RecentProjectsFile, json, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<RecentProject>> LoadAsync(CancellationToken cancellationToken)
    {
        var file = _paths.RecentProjectsFile;
        if (!File.Exists(file))
        {
            return [];
        }

        try
        {
            var text = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            var doc = JsonSerializer.Deserialize<Document>(text, JsonOptions);
            return (doc?.Items ?? [])
                .Where(i => !string.IsNullOrWhiteSpace(i.Path))
                .ToList();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Recent projects file is invalid; it was moved aside");
            var backup = $"{file}.corrupt-{_time.GetUtcNow():yyyyMMddHHmmss}";
            try
            {
                File.Move(file, backup);
            }
            catch (Exception moveError) when (moveError is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(moveError, "Could not move invalid recent projects file aside");
            }

            return [];
        }
    }

    private sealed record Document(int Version, List<RecentProject> Items);
}
