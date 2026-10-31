using CarbonStudio.Core.Paths;

namespace CarbonStudio.Core.FileSystem;

public sealed record FileSystemEntry(string Name, string FullPath, bool IsDirectory);

public interface IFileSystemService
{
    IReadOnlyList<FileSystemEntry> ListDirectory(string directory, IReadOnlyCollection<string> excludedNames);

    string CreateFile(string workspaceRoot, string parentDirectory, string name);

    string CreateFolder(string workspaceRoot, string parentDirectory, string name);

    string Rename(string workspaceRoot, string path, string newName);

    void Delete(string workspaceRoot, string path, bool sendToRecycleBin);
}

/// <summary>Workspace-scoped file operations. Every mutating call is confined to the workspace root.</summary>
public sealed class FileSystemService : IFileSystemService
{
    public IReadOnlyList<FileSystemEntry> ListDirectory(string directory, IReadOnlyCollection<string> excludedNames)
    {
        var excluded = new HashSet<string>(excludedNames, StringComparer.OrdinalIgnoreCase);
        var info = new DirectoryInfo(directory);

        return info.EnumerateFileSystemInfos()
            .Where(e => !excluded.Contains(e.Name))
            .Select(e => new FileSystemEntry(e.Name, e.FullName, e is DirectoryInfo))
            .OrderByDescending(e => e.IsDirectory)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string CreateFile(string workspaceRoot, string parentDirectory, string name)
    {
        var target = ResolveNewChild(workspaceRoot, parentDirectory, name);
        using (new FileStream(target, FileMode.CreateNew, FileAccess.Write))
        {
        }

        return target;
    }

    public string CreateFolder(string workspaceRoot, string parentDirectory, string name)
    {
        var target = ResolveNewChild(workspaceRoot, parentDirectory, name);
        Directory.CreateDirectory(target);
        return target;
    }

    public string Rename(string workspaceRoot, string path, string newName)
    {
        var source = PathGuard.EnsureInside(workspaceRoot, path, allowRoot: false);
        var parent = Path.GetDirectoryName(source)!;
        var target = ResolveNewChild(workspaceRoot, parent, newName, allowSameEntry: source);

        if (source == target)
        {
            return target;
        }

        if (Directory.Exists(source))
        {
            Directory.Move(source, target);
        }
        else if (File.Exists(source))
        {
            File.Move(source, target);
        }
        else
        {
            throw new FileNotFoundException($"'{source}' no longer exists.", source);
        }

        return target;
    }

    public void Delete(string workspaceRoot, string path, bool sendToRecycleBin)
    {
        var target = PathGuard.EnsureInside(workspaceRoot, path, allowRoot: false);
        var isDirectory = Directory.Exists(target);
        if (!isDirectory && !File.Exists(target))
        {
            throw new FileNotFoundException($"'{target}' no longer exists.", target);
        }

        if (sendToRecycleBin && OperatingSystem.IsWindows())
        {
            if (isDirectory)
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                    target,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }
            else
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                    target,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }

            return;
        }

        if (isDirectory)
        {
            Directory.Delete(target, recursive: true);
        }
        else
        {
            File.Delete(target);
        }
    }

    private static string ResolveNewChild(string workspaceRoot, string parentDirectory, string name, string? allowSameEntry = null)
    {
        if (!PathGuard.IsValidFileName(name, out var error))
        {
            throw new ArgumentException(error, nameof(name));
        }

        var parent = PathGuard.EnsureInside(workspaceRoot, parentDirectory);
        var target = Path.Combine(parent, name);
        PathGuard.EnsureInside(workspaceRoot, target, allowRoot: false);

        // A case-only rename (a.txt -> A.txt) resolves to the same entry on Windows and is legitimate.
        var isSameEntry = allowSameEntry is not null && PathGuard.PathEquals(allowSameEntry, target);
        if (!isSameEntry && (File.Exists(target) || Directory.Exists(target)))
        {
            throw new IOException($"'{name}' already exists in this folder.");
        }

        return target;
    }
}
