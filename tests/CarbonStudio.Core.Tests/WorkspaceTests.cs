using CarbonStudio.Core.Workspace;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarbonStudio.Core.Tests;

public class ProjectDetectorTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private IReadOnlyList<DetectedProject> Detect() =>
        new ProjectDetector(NullLogger<ProjectDetector>.Instance).Detect(_dir.Path);

    [Fact]
    public void DetectsCommonProjectTypes()
    {
        _dir.WriteFile("app/App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        _dir.WriteFile("Everything.sln", "\nMicrosoft Visual Studio Solution File, Format Version 12.00\n");
        _dir.WriteFile("web/package.json", "{}");
        _dir.WriteFile("py/pyproject.toml", "[project]");
        _dir.WriteFile("native/CMakeLists.txt", "project(x)");
        _dir.WriteFile("site/index.html", "<html></html>");

        var kinds = Detect().Select(p => p.Kind).ToHashSet();

        Assert.Contains(ProjectKind.DotNetSolution, kinds);
        Assert.Contains(ProjectKind.DotNetProject, kinds);
        Assert.Contains(ProjectKind.NodePackage, kinds);
        Assert.Contains(ProjectKind.PythonProject, kinds);
        Assert.Contains(ProjectKind.CMakeProject, kinds);
        Assert.Contains(ProjectKind.StaticWebSite, kinds);
    }

    [Fact]
    public void RelativePaths_UseForwardSlashes()
    {
        _dir.WriteFile("src/App/App.csproj", "<Project />");
        var project = Assert.Single(Detect());
        Assert.Equal("src/App/App.csproj", project.RelativePath);
        Assert.Equal("App", project.Name);
    }

    [Fact]
    public void SkipsGeneratedAndDependencyFolders()
    {
        _dir.WriteFile("node_modules/lib/package.json", "{}");
        _dir.WriteFile("bin/Debug/App.csproj", "<Project />");
        _dir.WriteFile(".git/x/package.json", "{}");
        Assert.Empty(Detect());
    }

    [Fact]
    public void RespectsMaximumDepth()
    {
        _dir.WriteFile("a/b/c/d/e/package.json", "{}");
        Assert.Empty(Detect());
        _dir.WriteFile("a/b/c/package.json", "{}");
        Assert.Single(Detect());
    }

    [Fact]
    public void MalformedProjectFile_IsDetectedWithProblem()
    {
        _dir.WriteFile("Broken.csproj", "<Project><oops>");
        var project = Assert.Single(Detect());
        Assert.NotNull(project.Problem);
    }

    [Fact]
    public void EmptyProjectFile_IsReportedAsProblem()
    {
        _dir.WriteFile("Empty.csproj", string.Empty);
        Assert.NotNull(Assert.Single(Detect()).Problem);
    }

    [Fact]
    public void NodeProject_DoesNotAlsoCountAsStaticSite()
    {
        _dir.WriteFile("package.json", "{}");
        _dir.WriteFile("index.html", "<html/>");
        Assert.Equal([ProjectKind.NodePackage], Detect().Select(p => p.Kind).ToArray());
    }

    [Fact]
    public void MissingDirectory_YieldsNothing()
    {
        var result = new ProjectDetector(NullLogger<ProjectDetector>.Instance).Detect(_dir.Combine("nope"));
        Assert.Empty(result);
    }
}

public class WorkspaceServiceTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private (WorkspaceService Workspace, RecentProjectsStore Recents, Core.Settings.SettingsService Settings) Create()
    {
        var paths = Factory.Paths(_dir);
        var settings = Factory.Settings(paths);
        var recents = new RecentProjectsStore(paths, NullLogger<RecentProjectsStore>.Instance);
        var workspace = new WorkspaceService(settings, recents, new ProjectDetector(NullLogger<ProjectDetector>.Instance), NullLogger<WorkspaceService>.Instance);
        return (workspace, recents, settings);
    }

    [Fact]
    public async Task OpenFolder_SetsCurrent_DetectsProjects_AndRecordsRecent()
    {
        var (workspace, recents, _) = Create();
        _dir.WriteFile("proj/App.csproj", "<Project />");

        var info = await workspace.OpenFolderAsync(_dir.Combine("proj"));

        Assert.Equal("proj", info.Name);
        Assert.Same(info, workspace.Current);
        Assert.Single(workspace.Projects);
        Assert.Single(await recents.GetAsync());
    }

    [Fact]
    public async Task OpenFolder_Missing_Throws_AndLeavesStateUntouched()
    {
        var (workspace, recents, _) = Create();

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => workspace.OpenFolderAsync(_dir.Combine("missing")));

        Assert.Null(workspace.Current);
        Assert.Empty(await recents.GetAsync());
    }

    [Fact]
    public async Task OpenFolder_LoadsWorkspaceSettings()
    {
        var (workspace, _, settings) = Create();
        await settings.LoadAsync();
        _dir.WriteFile("proj/.carbon/settings.json", """{ "editor": { "fontSize": 22 } }""");

        await workspace.OpenFolderAsync(_dir.Combine("proj"));
        Assert.Equal(22, settings.Effective.Editor.FontSize);

        await workspace.CloseAsync();
        Assert.Equal(14, settings.Effective.Editor.FontSize);
    }

    [Fact]
    public async Task Changed_FiresOnOpenAndClose()
    {
        var (workspace, _, _) = Create();
        Directory.CreateDirectory(_dir.Combine("w"));
        var events = new List<(string? Previous, string? Current)>();
        workspace.Changed += (_, e) => events.Add((e.Previous?.Name, e.Current?.Name));

        await workspace.OpenFolderAsync(_dir.Combine("w"));
        await workspace.CloseAsync();
        await workspace.CloseAsync();

        Assert.Equal([((string?)null, (string?)"w"), ("w", (string?)null)], events);
    }
}
