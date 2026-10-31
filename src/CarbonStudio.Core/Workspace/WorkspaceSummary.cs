namespace CarbonStudio.Core.Workspace;

/// <summary>Human-readable descriptions used by the status bar and welcome screen.</summary>
public static class WorkspaceSummary
{
    public static string DisplayName(this ProjectKind kind) => kind switch
    {
        ProjectKind.DotNetSolution => ".NET solution",
        ProjectKind.DotNetProject => ".NET project",
        ProjectKind.NodePackage => "Node.js",
        ProjectKind.PythonProject => "Python",
        ProjectKind.CMakeProject => "CMake",
        ProjectKind.StaticWebSite => "Static website",
        _ => kind.ToString(),
    };

    /// <summary>For example "2 .NET projects, Node.js" or "No project files detected".</summary>
    public static string Describe(IReadOnlyCollection<DetectedProject> projects)
    {
        if (projects.Count == 0)
        {
            return "No project files detected";
        }

        var parts = projects
            .GroupBy(p => p.Kind)
            .OrderBy(g => g.Key)
            .Select(g => g.Count() == 1 ? g.Key.DisplayName() : $"{g.Count()} {g.Key.PluralName()}");

        return string.Join(", ", parts);
    }

    private static string PluralName(this ProjectKind kind) => kind switch
    {
        ProjectKind.DotNetSolution => ".NET solutions",
        ProjectKind.DotNetProject => ".NET projects",
        ProjectKind.NodePackage => "Node.js packages",
        ProjectKind.PythonProject => "Python projects",
        ProjectKind.CMakeProject => "CMake projects",
        ProjectKind.StaticWebSite => "static websites",
        _ => kind + "s",
    };
}
