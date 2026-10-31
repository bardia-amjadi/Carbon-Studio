using CarbonStudio.Core.Paths;
using CarbonStudio.Core.Settings;

namespace CarbonStudio.Core.Tests;

public class SettingsTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task Load_WithNoFiles_UsesDarkDefaults()
    {
        var service = Factory.Settings(Factory.Paths(_dir));
        await service.LoadAsync();

        Assert.Equal(ThemeMode.Dark, service.Effective.Appearance.Theme);
        Assert.Equal(AppearanceSettings.DefaultAccentColor, service.Effective.Appearance.AccentColor);
        Assert.Empty(service.LoadWarnings);
    }

    [Fact]
    public async Task UpdateUser_PersistsAcrossInstances()
    {
        var paths = Factory.Paths(_dir);
        var first = Factory.Settings(paths);
        await first.LoadAsync();
        await first.UpdateUserAsync(s => s with { Appearance = s.Appearance with { Theme = ThemeMode.Light, AccentColor = "#ff8800" } });

        var second = Factory.Settings(paths);
        await second.LoadAsync();

        Assert.Equal(ThemeMode.Light, second.Effective.Appearance.Theme);
        Assert.Equal("#FF8800", second.Effective.Appearance.AccentColor);
    }

    [Fact]
    public async Task UpdateUser_RaisesChanged()
    {
        var service = Factory.Settings(Factory.Paths(_dir));
        await service.LoadAsync();
        var raised = 0;
        service.Changed += (_, _) => raised++;

        await service.UpdateUserAsync(s => s with { Editor = s.Editor with { WordWrap = true } });

        Assert.Equal(1, raised);
        Assert.True(service.Effective.Editor.WordWrap);
    }

    [Fact]
    public async Task PartialUserFile_InheritsDefaultsForMissingKeys()
    {
        var paths = Factory.Paths(_dir);
        Directory.CreateDirectory(paths.Root);
        File.WriteAllText(paths.SettingsFile, """{ "editor": { "fontSize": 18 } }""");

        var service = Factory.Settings(paths);
        await service.LoadAsync();

        Assert.Equal(18, service.Effective.Editor.FontSize);
        Assert.Equal(ThemeMode.Dark, service.Effective.Appearance.Theme);
    }

    [Fact]
    public async Task WorkspaceLayer_OverridesUserLayer_AndIsRemovedOnClear()
    {
        var paths = Factory.Paths(_dir);
        var service = Factory.Settings(paths);
        await service.LoadAsync();
        await service.UpdateUserAsync(s => s with { Editor = s.Editor with { FontSize = 12 } });

        var workspace = _dir.Combine("project");
        _dir.WriteFile("project/.carbon/settings.json", """{ "editor": { "fontSize": 20 }, "appearance": { "theme": "light" } }""");

        await service.SetWorkspaceAsync(workspace);
        Assert.Equal(20, service.Effective.Editor.FontSize);
        Assert.Equal(ThemeMode.Light, service.Effective.Appearance.Theme);
        Assert.Equal(12, service.User.Editor.FontSize);

        await service.SetWorkspaceAsync(null);
        Assert.Equal(12, service.Effective.Editor.FontSize);
        Assert.Equal(ThemeMode.Dark, service.Effective.Appearance.Theme);
    }

    [Fact]
    public async Task CorruptUserFile_IsQuarantined_AndDefaultsUsed()
    {
        var paths = Factory.Paths(_dir);
        Directory.CreateDirectory(paths.Root);
        File.WriteAllText(paths.SettingsFile, "{ this is not json");

        var service = Factory.Settings(paths);
        await service.LoadAsync();

        Assert.Equal(ThemeMode.Dark, service.Effective.Appearance.Theme);
        Assert.Single(service.LoadWarnings);
        Assert.False(File.Exists(paths.SettingsFile));
        Assert.Single(Directory.GetFiles(paths.Root, "settings.json.corrupt-*"));
    }

    [Fact]
    public async Task InvalidEnumValue_IsTreatedAsInvalidFile()
    {
        var paths = Factory.Paths(_dir);
        Directory.CreateDirectory(paths.Root);
        File.WriteAllText(paths.SettingsFile, """{ "appearance": { "theme": "purple" } }""");

        var service = Factory.Settings(paths);
        await service.LoadAsync();

        Assert.Equal(ThemeMode.Dark, service.Effective.Appearance.Theme);
        Assert.Single(service.LoadWarnings);
    }

    [Fact]
    public async Task InvalidWorkspaceFile_IsReported_ButNotMoved()
    {
        var service = Factory.Settings(Factory.Paths(_dir));
        await service.LoadAsync();
        var file = _dir.WriteFile("proj/.carbon/settings.json", "[1,2,3]");

        await service.SetWorkspaceAsync(_dir.Combine("proj"));

        Assert.Single(service.LoadWarnings);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Normalize_ClampsAndRepairsValues()
    {
        var bad = new AppSettings
        {
            Appearance = new AppearanceSettings { AccentColor = "not-a-colour" },
            Editor = new EditorSettings { FontSize = 500, FontFamily = "  " },
            Files = new FilesSettings { MaxOpenFileSizeMb = 0 },
            Layout = new LayoutSettings { SidebarWidth = 5, BottomPanelHeight = double.NaN, WindowWidth = 10 },
        };

        var fixedUp = SettingsValidator.Normalize(bad);

        Assert.Equal(AppearanceSettings.DefaultAccentColor, fixedUp.Appearance.AccentColor);
        Assert.Equal(48, fixedUp.Editor.FontSize);
        Assert.False(string.IsNullOrWhiteSpace(fixedUp.Editor.FontFamily));
        Assert.Equal(1, fixedUp.Files.MaxOpenFileSizeMb);
        Assert.Equal(160, fixedUp.Layout.SidebarWidth);
        Assert.Equal(220, fixedUp.Layout.BottomPanelHeight);
        Assert.Equal(640, fixedUp.Layout.WindowWidth);
    }

    [Fact]
    public void Normalize_ClampsTabSize_AndDefaultsAreSensible()
    {
        Assert.Equal(16, SettingsValidator.Normalize(new AppSettings { Editor = new EditorSettings { TabSize = 99 } }).Editor.TabSize);
        Assert.Equal(1, SettingsValidator.Normalize(new AppSettings { Editor = new EditorSettings { TabSize = -3 } }).Editor.TabSize);

        var defaults = new EditorSettings();
        Assert.Equal(4, defaults.TabSize);
        Assert.True(defaults.InsertSpaces);
        Assert.True(defaults.LineNumbers);
        Assert.False(defaults.Minimap);
        Assert.Equal(NewFileLineEnding.System, defaults.NewFileLineEnding);
    }

    [Fact]
    public async Task EditorSettings_ReadFromPartialFile_IncludingEnumNames()
    {
        var paths = Factory.Paths(_dir);
        Directory.CreateDirectory(paths.Root);
        File.WriteAllText(paths.SettingsFile, """{ "editor": { "tabSize": 2, "insertSpaces": false, "newFileLineEnding": "crLf" } }""");

        var service = Factory.Settings(paths);
        await service.LoadAsync();

        Assert.Equal(2, service.Effective.Editor.TabSize);
        Assert.False(service.Effective.Editor.InsertSpaces);
        Assert.Equal(NewFileLineEnding.CrLf, service.Effective.Editor.NewFileLineEnding);
        Assert.Empty(service.LoadWarnings);
    }

    [Theory]
    [InlineData("#000000", true)]
    [InlineData("#aBcDeF", true)]
    [InlineData("000000", false)]
    [InlineData("#12345", false)]
    [InlineData("#GGGGGG", false)]
    [InlineData(null, false)]
    public void TryParseHexColor_Validates(string? value, bool expected) =>
        Assert.Equal(expected, SettingsValidator.TryParseHexColor(value, out _));

    [Fact]
    public async Task UpdateUser_WhenWriteFails_LeavesStateUnchanged()
    {
        var paths = Factory.Paths(_dir);
        var service = Factory.Settings(paths);
        await service.LoadAsync();

        // Make the settings path a directory so the atomic replace cannot succeed.
        Directory.CreateDirectory(paths.SettingsFile);

        var threw = false;
        try
        {
            await service.UpdateUserAsync(s => s with { Editor = s.Editor with { FontSize = 30 } });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            threw = true;
        }

        Assert.True(threw);
        Assert.Equal(14, service.Effective.Editor.FontSize);
    }
}
