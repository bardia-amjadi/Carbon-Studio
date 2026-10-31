namespace CarbonStudio.Core.Paths;

/// <summary>Well-known per-user locations used by the application.</summary>
public sealed class AppPaths
{
    /// <summary>Name of the per-workspace folder that holds workspace-level configuration.</summary>
    public const string WorkspaceFolderName = ".carbon";

    /// <summary>Name of the workspace settings file inside <see cref="WorkspaceFolderName"/>.</summary>
    public const string WorkspaceSettingsFileName = "settings.json";

    public AppPaths(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        Root = Path.GetFullPath(rootDirectory);
    }

    public string Root { get; }

    public string SettingsFile => Path.Combine(Root, "settings.json");

    public string RecentProjectsFile => Path.Combine(Root, "recent-projects.json");

    public string LogsDirectory => Path.Combine(Root, "logs");

    /// <summary>Profile folder for the embedded WebView2 (the default location next to the exe is read-only when installed).</summary>
    public string WebViewDataDirectory => Path.Combine(Root, "webview2");

    public static AppPaths CreateDefault() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CarbonStudio"));

    public static string WorkspaceSettingsPath(string workspaceRoot) =>
        Path.Combine(workspaceRoot, WorkspaceFolderName, WorkspaceSettingsFileName);

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsDirectory);
    }
}
