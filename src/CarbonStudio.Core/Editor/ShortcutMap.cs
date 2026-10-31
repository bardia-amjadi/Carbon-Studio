using CarbonStudio.Core.Commands;

namespace CarbonStudio.Core.Editor;

/// <summary>
/// Key chords that the web-based editor must forward to the application instead of handling itself, derived from the
/// shortcut hints declared on registered commands (so the hint shown in menus and the real binding cannot drift apart).
/// Chords use the editor's lower-case form, for example "ctrl+shift+p".
/// </summary>
public sealed class ShortcutMap
{
    private readonly Dictionary<string, string> _commandByChord;

    private ShortcutMap(Dictionary<string, string> commandByChord) => _commandByChord = commandByChord;

    public IReadOnlyCollection<string> Chords => _commandByChord.Keys;

    public static ShortcutMap FromCommands(IEnumerable<CommandDefinition> commands)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var command in commands)
        {
            var chord = Normalize(command.Shortcut);
            if (chord is null)
            {
                continue;
            }

            if (!map.TryAdd(chord, command.Id))
            {
                throw new InvalidOperationException($"Shortcut '{command.Shortcut}' is declared by both '{map[chord]}' and '{command.Id}'.");
            }
        }

        return new ShortcutMap(map);
    }

    /// <summary>The command id bound to a chord reported by the editor, or null.</summary>
    public string? Resolve(string chord) => _commandByChord.GetValueOrDefault(chord);

    /// <summary>Converts "Ctrl+Shift+P" to "ctrl+shift+p" (modifier order ctrl, shift, alt). Returns null for unusable text.</summary>
    public static string? Normalize(string? shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut))
        {
            return null;
        }

        var parts = shortcut.Split('+', StringSplitOptions.TrimEntries);

        // A trailing '+' key (for example "Ctrl++") splits into empty parts; this editor binds no such shortcut.
        if (parts.Any(string.IsNullOrEmpty))
        {
            return null;
        }

        bool ctrl = false, shift = false, alt = false;
        foreach (var modifier in parts[..^1])
        {
            switch (modifier.ToLowerInvariant())
            {
                case "ctrl":
                    ctrl = true;
                    break;
                case "shift":
                    shift = true;
                    break;
                case "alt":
                    alt = true;
                    break;
                default:
                    return null;
            }
        }

        var key = parts[^1].ToLowerInvariant();
        var chord = (ctrl ? "ctrl+" : string.Empty) + (shift ? "shift+" : string.Empty) + (alt ? "alt+" : string.Empty) + key;

        // Same validity rule the protocol applies to incoming chords.
        return EditorProtocol.IsValidChord(chord) ? chord : null;
    }
}
