using System.Text.Json;
using System.Text.Json.Nodes;
using CarbonStudio.Core.Paths;
using Microsoft.Extensions.Logging;

namespace CarbonStudio.Core.Settings;

/// <inheritdoc cref="ISettingsService"/>
public sealed class SettingsService : ISettingsService
{
    private readonly AppPaths _paths;
    private readonly ILogger<SettingsService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonObject _defaults = SettingsJson.ToObject(new AppSettings());

    private JsonObject _userLayer = [];
    private JsonObject _workspaceLayer = [];
    private List<string> _userWarnings = [];
    private List<string> _workspaceWarnings = [];
    private AppSettings _user = SettingsValidator.Normalize(new AppSettings());
    private AppSettings _effective = SettingsValidator.Normalize(new AppSettings());

    public SettingsService(AppPaths paths, ILogger<SettingsService> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public event EventHandler? Changed;

    public AppSettings Effective => _effective;

    public AppSettings User => _user;

    public IReadOnlyList<string> LoadWarnings => [.. _userWarnings, .. _workspaceWarnings];

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var warnings = new List<string>();
            _userLayer = await ReadLayerAsync(_paths.SettingsFile, "User settings", quarantineInvalid: true, warnings, cancellationToken)
                .ConfigureAwait(false);
            _userWarnings = warnings;
            Recompute();
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task UpdateUserAsync(Func<AppSettings, AppSettings> update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var next = SettingsValidator.Normalize(update(_user));
            var layer = SettingsJson.ToObject(next);

            // Persist first: if the write fails, in-memory state must not diverge from disk.
            await AtomicFile.WriteAllTextAsync(_paths.SettingsFile, layer.ToJsonString(SettingsJson.Options), cancellationToken)
                .ConfigureAwait(false);

            _userLayer = layer;
            Recompute();
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetWorkspaceAsync(string? workspaceRoot, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var warnings = new List<string>();
            _workspaceLayer = workspaceRoot is null
                ? []
                : await ReadLayerAsync(
                        AppPaths.WorkspaceSettingsPath(workspaceRoot),
                        "Workspace settings",
                        quarantineInvalid: false,
                        warnings,
                        cancellationToken)
                    .ConfigureAwait(false);
            _workspaceWarnings = warnings;
            Recompute();
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Recompute()
    {
        var merged = (JsonObject)_defaults.DeepClone();
        SettingsJson.Merge(merged, _userLayer);
        _user = SettingsValidator.Normalize(SettingsJson.Deserialize<AppSettings>(merged));

        SettingsJson.Merge(merged, _workspaceLayer);
        _effective = SettingsValidator.Normalize(SettingsJson.Deserialize<AppSettings>(merged));
    }

    /// <summary>
    /// Reads one settings file. A missing file is an empty layer; a file that is not valid settings is reported,
    /// optionally moved aside (never deleted), and treated as an empty layer so the app still starts.
    /// </summary>
    private async Task<JsonObject> ReadLayerAsync(
        string path,
        string label,
        bool quarantineInvalid,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        string text;
        try
        {
            text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "{Label} could not be read from {Path}", label, path);
            warnings.Add($"{label} could not be read ({ex.Message}). Defaults are in use.");
            return [];
        }

        try
        {
            var layer = SettingsJson.ParseObject(text);

            // Dry run: make sure this layer combined with the defaults is a valid settings document.
            var probe = (JsonObject)_defaults.DeepClone();
            SettingsJson.Merge(probe, layer);
            _ = SettingsJson.Deserialize<AppSettings>(probe);
            return layer;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
        {
            _logger.LogWarning(ex, "{Label} at {Path} is invalid", label, path);
            var message = $"{label} file is invalid ({ex.Message}). Defaults are in use.";

            if (quarantineInvalid)
            {
                var backup = $"{path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
                try
                {
                    File.Move(path, backup);
                    message += $" The original was kept as '{Path.GetFileName(backup)}'.";
                }
                catch (Exception moveError) when (moveError is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(moveError, "Could not move invalid settings file aside");
                }
            }

            warnings.Add(message);
            return [];
        }
    }
}
