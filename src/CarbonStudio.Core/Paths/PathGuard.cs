namespace CarbonStudio.Core.Paths;

/// <summary>Path validation helpers used to keep file operations inside a workspace.</summary>
public static class PathGuard
{
    // Intentionally the Windows-invalid set on every platform so that projects stay portable.
    private static readonly char[] InvalidNameChars =
        ['\\', '/', ':', '*', '?', '"', '<', '>', '|', '\0', '\u0001', '\u0002', '\u0003', '\u0004', '\u0005', '\u0006', '\a', '\b', '\t', '\n', '\v', '\f', '\r'];

    private static readonly string[] ReservedDeviceNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    /// <summary>Path comparison appropriate for the host file system.</summary>
    public static StringComparison Comparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static StringComparer Comparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static bool PathEquals(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), Comparison);

    /// <summary>Full path without a trailing directory separator (roots such as "C:\" are kept intact).</summary>
    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        return full.Length > (root?.Length ?? 0)
            ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : full;
    }

    /// <summary>True when <paramref name="candidate"/> is the root or lives beneath it.</summary>
    public static bool IsInside(string root, string candidate, bool allowRoot = true)
    {
        var normalizedRoot = Normalize(root);
        var normalizedCandidate = Normalize(candidate);

        if (string.Equals(normalizedRoot, normalizedCandidate, Comparison))
        {
            return allowRoot;
        }

        var prefix = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(prefix, Comparison);
    }

    /// <summary>Returns the full path of <paramref name="candidate"/> or throws when it escapes <paramref name="root"/>.</summary>
    public static string EnsureInside(string root, string candidate, bool allowRoot = true)
    {
        if (!IsInside(root, candidate, allowRoot))
        {
            throw new ArgumentException($"The path '{candidate}' is outside of '{root}'.", nameof(candidate));
        }

        return Path.GetFullPath(candidate);
    }

    /// <summary>Validates a single file or folder name (no separators, no reserved names).</summary>
    public static bool IsValidFileName(string? name, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(name))
        {
            error = "The name cannot be empty.";
            return false;
        }

        if (name is "." or "..")
        {
            error = "The name cannot be '.' or '..'.";
            return false;
        }

        if (name.EndsWith(' ') || name.EndsWith('.'))
        {
            error = "The name cannot end with a space or a period.";
            return false;
        }

        if (name.AsSpan().IndexOfAny(InvalidNameChars) >= 0)
        {
            error = "The name contains characters that are not allowed (\\ / : * ? \" < > |).";
            return false;
        }

        var stem = name.Split('.')[0].TrimEnd();
        if (ReservedDeviceNames.Contains(stem, StringComparer.OrdinalIgnoreCase))
        {
            error = $"'{stem}' is a reserved device name.";
            return false;
        }

        return true;
    }
}
