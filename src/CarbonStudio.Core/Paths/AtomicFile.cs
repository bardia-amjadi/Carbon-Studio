using System.Text;

namespace CarbonStudio.Core.Paths;

/// <summary>Crash-safe file writes: data goes to a temporary sibling first, then replaces the target.</summary>
public static class AtomicFile
{
    public static async Task WriteAllBytesAsync(string path, byte[] content, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);

        var temp = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temp, content, cancellationToken).ConfigureAwait(false);

            if (File.Exists(fullPath))
            {
                File.Replace(temp, fullPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temp, fullPath);
            }
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    public static Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default) =>
        WriteAllBytesAsync(path, new UTF8Encoding(false).GetBytes(content), cancellationToken);
}
