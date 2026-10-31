namespace CarbonStudio.Core.Settings;

/// <summary>
/// Layered settings. Precedence (lowest to highest): built-in defaults, user settings
/// (<c>%LOCALAPPDATA%\CarbonStudio\settings.json</c>), workspace settings (<c>.carbon/settings.json</c>).
/// </summary>
public interface ISettingsService
{
    /// <summary>Defaults + user + workspace layers; what the application should use.</summary>
    AppSettings Effective { get; }

    /// <summary>Defaults + user layer only; the baseline that user-level edits start from.</summary>
    AppSettings User { get; }

    /// <summary>Problems found while loading files (corrupt JSON, invalid values). Empty when everything loaded cleanly.</summary>
    IReadOnlyList<string> LoadWarnings { get; }

    event EventHandler? Changed;

    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Applies <paramref name="update"/> to the user layer and persists it.</summary>
    Task UpdateUserAsync(Func<AppSettings, AppSettings> update, CancellationToken cancellationToken = default);

    /// <summary>Loads (or clears, when null) the workspace layer for the given workspace root.</summary>
    Task SetWorkspaceAsync(string? workspaceRoot, CancellationToken cancellationToken = default);
}
