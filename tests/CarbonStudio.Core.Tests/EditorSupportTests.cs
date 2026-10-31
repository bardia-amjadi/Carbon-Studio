using CarbonStudio.Core.Commands;
using CarbonStudio.Core.Editor;

namespace CarbonStudio.Core.Tests;

public class ShortcutMapTests
{
    private static CommandDefinition Cmd(string id, string? shortcut) =>
        new(id, id, "Test", _ => Task.CompletedTask) { Shortcut = shortcut };

    [Theory]
    [InlineData("Ctrl+S", "ctrl+s")]
    [InlineData("Ctrl+Shift+P", "ctrl+shift+p")]
    [InlineData("Ctrl+Alt+S", "ctrl+alt+s")]
    [InlineData("Alt+Ctrl+Shift+x", "ctrl+shift+alt+x")]
    [InlineData("Ctrl+,", "ctrl+,")]
    [InlineData("F1", "f1")]
    [InlineData(" ctrl + b ", "ctrl+b")]
    public void Normalize_ProducesEditorChords(string shortcut, string expected) =>
        Assert.Equal(expected, ShortcutMap.Normalize(shortcut));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl++")]
    [InlineData("Win+S")]
    [InlineData("Ctrl+Backspace")]
    [InlineData("Ctrl+F13")]
    public void Normalize_RejectsUnusableShortcuts(string? shortcut) => Assert.Null(ShortcutMap.Normalize(shortcut));

    [Fact]
    public void FromCommands_ResolvesChordsToCommandIds_AndSkipsCommandsWithoutShortcuts()
    {
        var map = ShortcutMap.FromCommands([Cmd("file.save", "Ctrl+S"), Cmd("view.palette", "Ctrl+Shift+P"), Cmd("misc", null)]);

        Assert.Equal("file.save", map.Resolve("ctrl+s"));
        Assert.Equal("view.palette", map.Resolve("ctrl+shift+p"));
        Assert.Null(map.Resolve("ctrl+z"));
        Assert.Equal(2, map.Chords.Count);
    }

    [Fact]
    public void FromCommands_RejectsConflictingShortcuts()
    {
        Assert.Throws<InvalidOperationException>(() => ShortcutMap.FromCommands([Cmd("a", "Ctrl+S"), Cmd("b", "ctrl+s")]));
    }
}

public class LanguageCatalogTests
{
    [Theory]
    [InlineData("Program.cs", "csharp")]
    [InlineData("PROGRAM.CS", "csharp")]
    [InlineData("App.csproj", "xml")]
    [InlineData("MainWindow.xaml", "xml")]
    [InlineData("data.json", "json")]
    [InlineData("index.html", "html")]
    [InlineData("site.css", "css")]
    [InlineData("app.js", "javascript")]
    [InlineData("app.tsx", "typescript")]
    [InlineData("types.d.ts", "typescript")]
    [InlineData("script.py", "python")]
    [InlineData("main.cpp", "cpp")]
    [InlineData("util.h", "cpp")]
    [InlineData("run.ps1", "powershell")]
    [InlineData("README.md", "markdown")]
    [InlineData("config.yml", "yaml")]
    [InlineData("Dockerfile", "dockerfile")]
    [InlineData("dockerfile", "dockerfile")]
    [InlineData(".editorconfig", "ini")]
    [InlineData("notes.txt", "plaintext")]
    [InlineData("LICENSE", "plaintext")]
    [InlineData("archive.unknownext", "plaintext")]
    [InlineData("", "plaintext")]
    public void Detect_ByFileName(string name, string expectedId) => Assert.Equal(expectedId, LanguageCatalog.Detect(name).Id);

    [Fact]
    public void Detect_AcceptsFullPaths_AndNull()
    {
        Assert.Equal("csharp", LanguageCatalog.Detect(Path.Combine("some", "dir.cs", "File.cs")).Id);
        Assert.Equal("plaintext", LanguageCatalog.Detect(null).Id);
    }

    [Fact]
    public void DisplayNames_AreReadable()
    {
        Assert.Equal("C#", LanguageCatalog.Detect("a.cs").DisplayName);
        Assert.Equal("Plain Text", LanguageCatalog.PlainText.DisplayName);
    }
}
