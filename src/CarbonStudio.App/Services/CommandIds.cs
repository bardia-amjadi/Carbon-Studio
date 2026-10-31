namespace CarbonStudio.App.Services;

/// <summary>Stable identifiers for commands shown in menus and the command palette.</summary>
internal static class CommandIds
{
    public const string FileNew = "file.new";
    public const string FileOpen = "file.open";
    public const string FileOpenFolder = "file.openFolder";
    public const string FileSave = "file.save";
    public const string FileSaveAs = "file.saveAs";
    public const string FileSaveAll = "file.saveAll";
    public const string FileCloseTab = "file.closeTab";
    public const string FileCloseFolder = "file.closeFolder";
    public const string FileExit = "file.exit";

    public const string EditUndo = "edit.undo";
    public const string EditRedo = "edit.redo";
    public const string EditCut = "edit.cut";
    public const string EditCopy = "edit.copy";
    public const string EditPaste = "edit.paste";
    public const string EditSelectAll = "edit.selectAll";
    public const string EditFind = "edit.find";
    public const string EditReplace = "edit.replace";
    public const string EditGoToLine = "edit.goToLine";
    public const string EditFormatDocument = "edit.formatDocument";
    public const string EditTrimWhitespace = "edit.trimWhitespace";

    public const string DocLineEndingLf = "doc.eol.lf";
    public const string DocLineEndingCrLf = "doc.eol.crlf";
    public const string DocEncodingUtf8 = "doc.enc.utf8";
    public const string DocEncodingUtf8Bom = "doc.enc.utf8bom";
    public const string DocEncodingUtf16Le = "doc.enc.utf16le";
    public const string DocEncodingUtf16Be = "doc.enc.utf16be";
    public const string DocEncodingLatin1 = "doc.enc.latin1";

    public const string ViewCommandPalette = "view.commandPalette";
    public const string ViewToggleSidebar = "view.toggleSidebar";
    public const string ViewToggleBottomPanel = "view.toggleBottomPanel";
    public const string ViewExplorer = "view.explorer";
    public const string ViewOutput = "view.output";
    public const string ViewThemeDark = "view.theme.dark";
    public const string ViewThemeLight = "view.theme.light";
    public const string ViewThemeSystem = "view.theme.system";
    public const string ViewWordWrap = "view.wordWrap";
    public const string ViewResetLayout = "view.resetLayout";

    public const string ExplorerRefresh = "explorer.refresh";
    public const string ExplorerNewFile = "explorer.newFile";
    public const string ExplorerNewFolder = "explorer.newFolder";

    public const string SettingsOpen = "settings.open";
    public const string SettingsOpenFile = "settings.openFile";

    public const string HelpOpenLogs = "help.openLogs";
    public const string HelpAbout = "help.about";

    // Later-phase commands (registered as unavailable so they are discoverable but honest).
    public const string SearchWorkspace = "search.workspace";
    public const string ProjectNew = "project.new";
    public const string BuildBuild = "build.build";
    public const string BuildRun = "build.run";
    public const string DebugStart = "debug.start";
    public const string TerminalNew = "terminal.new";
    public const string GitCommit = "git.commit";
    public const string TestRunAll = "test.runAll";
    public const string ExtensionsOpen = "extensions.open";
}
