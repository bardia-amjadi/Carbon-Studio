using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CarbonStudio.Core.Settings;

namespace CarbonStudio.Core.Editor;

public abstract record EditorEvent;

public sealed record EditorReady : EditorEvent;

public sealed record EditorTextChanged(Guid Id, int Rev, string Text) : EditorEvent;

public sealed record EditorCaretMoved(Guid Id, int Line, int Column) : EditorEvent;

public sealed record EditorShortcut(string Chord) : EditorEvent;

public sealed record EditorFlushed(int RequestId) : EditorEvent;

public sealed record EditorError(string Message) : EditorEvent;

/// <summary>
/// JSON messages exchanged with the web-based editor. Everything coming from the web view is untrusted input and is
/// strictly validated here; everything going out is built by these functions so message shapes live in one place.
/// The JavaScript counterpart is Editor/web/src/bridge.js.
/// </summary>
public static partial class EditorProtocol
{
    /// <summary>Upper bound for one incoming message (a 10 MB file is about 10 million characters).</summary>
    public const int MaxMessageChars = 64 * 1024 * 1024;

    private const int MaxErrorChars = 2000;

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [GeneratedRegex(@"^(ctrl\+)?(shift\+)?(alt\+)?([a-z0-9,.]|f(1[0-2]|[1-9]))$", RegexOptions.CultureInvariant)]
    private static partial Regex ChordPattern();

    /// <summary>True for chords such as "ctrl+shift+p", "alt+," or "f1" (the only forms the editor reports).</summary>
    public static bool IsValidChord(string? chord) => chord is { Length: <= 32 } && ChordPattern().IsMatch(chord);

    // ------------------------------------------------------------------ incoming

    /// <summary>Parses and validates a message from the editor. Never throws.</summary>
    public static bool TryParse(string? json, out EditorEvent? result, out string? error)
    {
        result = null;
        error = null;

        if (string.IsNullOrEmpty(json))
        {
            error = "Empty message.";
            return false;
        }

        if (json.Length > MaxMessageChars)
        {
            error = "Message too large.";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
            {
                error = "Message must be an object with a string 'type'.";
                return false;
            }

            var type = typeElement.GetString();
            result = type switch
            {
                "ready" => new EditorReady(),
                "text" => new EditorTextChanged(ReadId(root), ReadInt(root, "rev", 0, int.MaxValue), ReadString(root, "text", MaxMessageChars)),
                "caret" => new EditorCaretMoved(ReadId(root), ReadInt(root, "line", 1, 100_000_000), ReadInt(root, "column", 1, 100_000_000)),
                "shortcut" => new EditorShortcut(ReadChord(root)),
                "flushed" => new EditorFlushed(ReadInt(root, "requestId", 0, int.MaxValue)),
                "error" => new EditorError(Truncate(ReadString(root, "message", int.MaxValue))),
                _ => throw new FormatException($"Unknown message type '{type}'."),
            };
            return true;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or KeyNotFoundException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static Guid ReadId(JsonElement root) =>
        Guid.TryParse(ReadString(root, "id", 64), out var id) ? id : throw new FormatException("'id' is not a GUID.");

    private static int ReadInt(JsonElement root, string name, int min, int max)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value))
        {
            throw new FormatException($"'{name}' must be an integer.");
        }

        return value >= min && value <= max ? value : throw new FormatException($"'{name}' is out of range.");
    }

    private static string ReadString(JsonElement root, string name, int maxLength)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
        {
            throw new FormatException($"'{name}' must be a string.");
        }

        var value = element.GetString() ?? string.Empty;
        return value.Length <= maxLength ? value : throw new FormatException($"'{name}' is too long.");
    }

    private static string ReadChord(JsonElement root)
    {
        var chord = ReadString(root, "chord", 32);
        return IsValidChord(chord) ? chord : throw new FormatException("'chord' is not a valid key chord.");
    }

    private static string Truncate(string value) => value.Length <= MaxErrorChars ? value : value[..MaxErrorChars] + "…";

    // ------------------------------------------------------------------ outgoing

    public static string Open(Guid id, string text, string language) =>
        Serialize(new { type = "open", id = Id(id), text, language });

    public static string Activate(Guid id, bool focus) => Serialize(new { type = "activate", id = Id(id), focus });

    public static string Deactivate() => Serialize(new { type = "deactivate" });

    public static string Close(Guid id) => Serialize(new { type = "close", id = Id(id) });

    public static string SetLanguage(Guid id, string language) => Serialize(new { type = "language", id = Id(id), language });

    public static string Options(EditorSettings editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        return Serialize(new
        {
            type = "options",
            options = new
            {
                fontFamily = editor.FontFamily,
                fontSize = editor.FontSize,
                wordWrap = editor.WordWrap,
                tabSize = editor.TabSize,
                insertSpaces = editor.InsertSpaces,
                minimap = editor.Minimap,
                lineNumbers = editor.LineNumbers,
            },
        });
    }

    public static string Theme(bool dark, string accent) => Serialize(new { type = "theme", dark, accent });

    public static string Shortcuts(IEnumerable<string> chords) => Serialize(new { type = "shortcuts", chords = chords.ToArray() });

    public static string Exec(EditorAction action) => Serialize(new { type = "exec", action = action.WireName() });

    public static string Flush(int requestId) => Serialize(new { type = "flush", requestId });

    public static string Focus() => Serialize(new { type = "focus" });

    private static string Id(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);

    private static string Serialize<T>(T message) => JsonSerializer.Serialize(message, Json);
}
