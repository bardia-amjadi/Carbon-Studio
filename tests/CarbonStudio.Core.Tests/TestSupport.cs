using CarbonStudio.Core.Paths;
using CarbonStudio.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarbonStudio.Core.Tests;

/// <summary>A uniquely named temporary directory that is deleted on dispose.</summary>
public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "carbon-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public string WriteFile(string relativePath, string content)
    {
        var full = Combine(relativePath.Split('/'));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup of temp data.
        }
    }
}

internal static class Factory
{
    public static AppPaths Paths(TempDir dir) => new(dir.Combine("appdata"));

    public static SettingsService Settings(AppPaths paths) => new(paths, NullLogger<SettingsService>.Instance);
}
