namespace CarbonStudio.Core.Commands;

/// <summary>Fuzzy matching for the command palette.</summary>
public static class CommandPaletteSearch
{
    /// <summary>
    /// Returns commands whose "Category: Title" text contains every query character in order,
    /// best matches first. An empty query returns all commands sorted by category and title.
    /// </summary>
    public static IReadOnlyList<CommandDefinition> Search(IEnumerable<CommandDefinition> commands, string? query)
    {
        var trimmed = query?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return commands
                .OrderBy(c => c.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return commands
            .Select(c => (Command: c, Score: Score($"{c.Category}: {c.Title}", trimmed)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Command.Title, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Command)
            .ToList();
    }

    /// <summary>0 means no match; larger is better.</summary>
    internal static int Score(string text, string query)
    {
        var needle = query.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (needle.Length == 0)
        {
            return 0;
        }

        var score = 1;
        var position = 0;
        var previousMatch = -2;

        foreach (var ch in needle)
        {
            var found = text.IndexOf(ch.ToString(), position, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                return 0;
            }

            score += 1;
            if (found == previousMatch + 1)
            {
                score += 6;
            }

            if (found == 0 || !char.IsLetterOrDigit(text[found - 1]))
            {
                score += 4;
            }

            previousMatch = found;
            position = found + 1;
        }

        if (text.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }

        // Prefer shorter commands when everything else is equal.
        return score * 10 - Math.Min(text.Length, 9);
    }
}
