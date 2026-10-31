using System.Xml;
using Microsoft.Extensions.Logging;

namespace CarbonStudio.Core.Workspace;

public enum ProjectKind
{
    DotNetSolution,
    DotNetProject,
    NodePackage,
    PythonProject,
    CMakeProject,
    StaticWebSite,
}

/// <param name="RelativePath">Path of the marker file relative to the workspace root, using '/' separators.</param>
/// <param name="Problem">Set when the marker file exists but looks invalid (for example malformed XML).</param>
public sealed record DetectedProject(ProjectKind Kind, string Name, string RelativePath, string? Problem = null);

public interface IProjectDetector
{
    IReadOnlyList<DetectedProject> Detect(string workspaceRoot, CancellationToken cancellationToken = default);
}

/// <summary>Detects project types from marker files. It never executes anything and never throws for unreadable folders.</summary>
public sealed class ProjectDetector : IProjectDetector
{
    private const int MaxDepth = 3;
    private const long MaxProjectFileBytes = 2 * 1024 * 1024;

    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", ".vscode", "node_modules", "bin", "obj", ".venv", "venv", "__pycache__", "packages", "dist", "build",
    };

    private readonly ILogger<ProjectDetector> _logger;

    public ProjectDetector(ILogger<ProjectDetector> logger) => _logger = logger;

    public IReadOnlyList<DetectedProject> Detect(string workspaceRoot, CancellationToken cancellationToken = default)
    {
        var results = new List<DetectedProject>();
        if (Directory.Exists(workspaceRoot))
        {
            Scan(workspaceRoot, workspaceRoot, depth: 0, results, cancellationToken);
        }

        return results
            .OrderBy(p => p.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void Scan(string root, string directory, int depth, List<DetectedProject> results, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        string[] files;
        string[] subdirectories;
        try
        {
            files = Directory.GetFiles(directory);
            subdirectories = Directory.GetDirectories(directory);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _logger.LogDebug(ex, "Skipping unreadable folder {Directory}", directory);
            return;
        }

        var names = files.ToDictionary(f => Path.GetFileName(f), f => f, StringComparer.OrdinalIgnoreCase);
        var hasPackageJson = names.ContainsKey("package.json");

        foreach (var (name, fullPath) in names)
        {
            var extension = Path.GetExtension(name);
            var display = Path.GetFileNameWithoutExtension(name);

            if (extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
            {
                var problem = extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase) ? ValidateXml(fullPath, "Solution", "Solution") : ValidateSolution(fullPath);
                results.Add(new DetectedProject(ProjectKind.DotNetSolution, display, Relative(root, fullPath), problem));
            }
            else if (extension is ".csproj" or ".fsproj" or ".vbproj")
            {
                results.Add(new DetectedProject(ProjectKind.DotNetProject, display, Relative(root, fullPath), ValidateXml(fullPath, "Project", "Project")));
            }
        }

        if (hasPackageJson)
        {
            results.Add(new DetectedProject(ProjectKind.NodePackage, Path.GetFileName(directory), Relative(root, names["package.json"])));
        }

        if (names.ContainsKey("pyproject.toml") || names.ContainsKey("requirements.txt") || names.ContainsKey("setup.py"))
        {
            var marker = names.TryGetValue("pyproject.toml", out var p) ? p : names.TryGetValue("requirements.txt", out var r) ? r : names["setup.py"];
            results.Add(new DetectedProject(ProjectKind.PythonProject, Path.GetFileName(directory), Relative(root, marker)));
        }

        if (names.TryGetValue("CMakeLists.txt", out var cmake))
        {
            results.Add(new DetectedProject(ProjectKind.CMakeProject, Path.GetFileName(directory), Relative(root, cmake)));
        }

        if (!hasPackageJson && names.TryGetValue("index.html", out var index))
        {
            results.Add(new DetectedProject(ProjectKind.StaticWebSite, Path.GetFileName(directory), Relative(root, index)));
        }

        if (depth >= MaxDepth)
        {
            return;
        }

        foreach (var sub in subdirectories)
        {
            if (!SkippedDirectories.Contains(Path.GetFileName(sub)))
            {
                Scan(root, sub, depth + 1, results, ct);
            }
        }
    }

    private static string Relative(string root, string fullPath) =>
        Path.GetRelativePath(root, fullPath).Replace('\\', '/');

    private static string? ValidateSolution(string path)
    {
        try
        {
            using var reader = new StreamReader(path);
            var buffer = new char[4096];
            var read = reader.Read(buffer, 0, buffer.Length);
            var head = new string(buffer, 0, read);
            return head.Contains("Microsoft Visual Studio Solution File", StringComparison.Ordinal)
                ? null
                : "Solution file header was not recognised.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Solution file could not be read: {ex.Message}";
        }
    }

    private static string? ValidateXml(string path, string label, string expectedRoot)
    {
        try
        {
            if (new FileInfo(path).Length is 0 or > MaxProjectFileBytes)
            {
                return $"{label} file is empty or unusually large.";
            }

            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(path, settings);
            string? root = null;

            // Read the whole document so unclosed or malformed elements are caught, not just the first tag.
            while (reader.Read())
            {
                if (root is null && reader.NodeType == XmlNodeType.Element)
                {
                    root = reader.LocalName;
                }
            }

            return string.Equals(root, expectedRoot, StringComparison.Ordinal)
                ? null
                : $"{label} file should have a <{expectedRoot}> root element.";
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            return $"{label} file is not valid XML: {ex.Message}";
        }
    }
}
