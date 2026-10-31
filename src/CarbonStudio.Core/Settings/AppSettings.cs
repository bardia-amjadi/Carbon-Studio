namespace CarbonStudio.Core.Settings;

public enum ThemeMode
{
    System,
    Light,
    Dark,
}

/// <summary>Root settings document. Every property has a default so partial JSON files are valid.</summary>
public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public AppearanceSettings Appearance { get; init; } = new();

    public EditorSettings Editor { get; init; } = new();

    public ExplorerSettings Explorer { get; init; } = new();

    public FilesSettings Files { get; init; } = new();

    public LayoutSettings Layout { get; init; } = new();
}

public sealed record AppearanceSettings
{
    public const string DefaultAccentColor = "#38BDF8";

    public ThemeMode Theme { get; init; } = ThemeMode.Dark;

    /// <summary>Accent colour as #RRGGBB.</summary>
    public string AccentColor { get; init; } = DefaultAccentColor;
}

public sealed record EditorSettings
{
    public string FontFamily { get; init; } = "Cascadia Code, Consolas, Courier New";

    public double FontSize { get; init; } = 14;

    public bool WordWrap { get; init; }

    /// <summary>Width of a tab stop in columns (1 to 16).</summary>
    public int TabSize { get; init; } = 4;

    /// <summary>Insert spaces instead of tab characters when Tab is pressed.</summary>
    public bool InsertSpaces { get; init; } = true;

    public bool Minimap { get; init; }

    public bool LineNumbers { get; init; } = true;

    /// <summary>Line ending used by newly created (untitled) files.</summary>
    public NewFileLineEnding NewFileLineEnding { get; init; } = NewFileLineEnding.System;
}

public enum NewFileLineEnding
{
    /// <summary>CRLF on Windows, LF elsewhere.</summary>
    System,
    Lf,
    CrLf,
}

public sealed record ExplorerSettings
{
    /// <summary>Folder and file names hidden from the explorer tree (case-insensitive, exact match).</summary>
    public IReadOnlyList<string> ExcludedNames { get; init; } = [".git", ".vs", "node_modules", "bin", "obj"];
}

public sealed record FilesSettings
{
    /// <summary>Files larger than this are not opened in the text editor.</summary>
    public int MaxOpenFileSizeMb { get; init; } = 10;
}

public sealed record LayoutSettings
{
    public bool SidebarVisible { get; init; } = true;

    public double SidebarWidth { get; init; } = 280;

    public bool BottomPanelVisible { get; init; } = true;

    public double BottomPanelHeight { get; init; } = 220;

    /// <summary>Identifier of the active activity-bar view ("explorer", ...).</summary>
    public string ActiveSidebarView { get; init; } = "explorer";

    public string ActiveBottomPanel { get; init; } = "output";

    public int? WindowWidth { get; init; }

    public int? WindowHeight { get; init; }

    public bool WindowMaximized { get; init; }
}
