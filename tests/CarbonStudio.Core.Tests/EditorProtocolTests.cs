using System.Text.Json;
using System.Text.RegularExpressions;
using CarbonStudio.Core.Editor;
using CarbonStudio.Core.Settings;

namespace CarbonStudio.Core.Tests;

public class EditorProtocolTests
{
    private static readonly Guid Id = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static EditorEvent Parse(string json)
    {
        Assert.True(EditorProtocol.TryParse(json, out var result, out var error), error);
        return result!;
    }

    private static string Error(string? json)
    {
        Assert.False(EditorProtocol.TryParse(json, out var result, out var error));
        Assert.Null(result);
        return error!;
    }

    [Fact]
    public void Parses_AllIncomingMessageTypes()
    {
        Assert.Equal(new EditorReady(), Parse("""{"type":"ready"}"""));
        Assert.Equal(
            new EditorTextChanged(Id, 3, "a\nb ✓"),
            Parse($$"""{"type":"text","id":"{{Id}}","rev":3,"text":"a\nb ✓"}"""));
        Assert.Equal(new EditorCaretMoved(Id, 4, 9), Parse($$"""{"type":"caret","id":"{{Id}}","line":4,"column":9}"""));
        Assert.Equal(new EditorShortcut("ctrl+shift+p"), Parse("""{"type":"shortcut","chord":"ctrl+shift+p"}"""));
        Assert.Equal(new EditorFlushed(12), Parse("""{"type":"flushed","requestId":12}"""));
        Assert.Equal(new EditorError("boom"), Parse("""{"type":"error","message":"boom"}"""));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("{}")]
    [InlineData("""{"type":5}""")]
    [InlineData("""{"type":"evil"}""")]
    [InlineData("""{"type":"text","id":"nope","rev":1,"text":"x"}""")]
    [InlineData("""{"type":"text","id":"11111111-2222-3333-4444-555555555555","rev":-1,"text":"x"}""")]
    [InlineData("""{"type":"text","id":"11111111-2222-3333-4444-555555555555","rev":1.5,"text":"x"}""")]
    [InlineData("""{"type":"text","id":"11111111-2222-3333-4444-555555555555","rev":1}""")]
    [InlineData("""{"type":"text","id":"11111111-2222-3333-4444-555555555555","rev":1,"text":7}""")]
    [InlineData("""{"type":"caret","id":"11111111-2222-3333-4444-555555555555","line":0,"column":1}""")]
    [InlineData("""{"type":"caret","id":"11111111-2222-3333-4444-555555555555","line":1,"column":"2"}""")]
    [InlineData("""{"type":"flushed"}""")]
    public void Rejects_MalformedOrUnknownMessages(string? json) => Assert.NotEmpty(Error(json));

    [Theory]
    [InlineData("Ctrl+S")]
    [InlineData("ctrl+")]
    [InlineData("ctrl+ctrl+s")]
    [InlineData("shift+ctrl+s")]
    [InlineData("ctrl+f13")]
    [InlineData("ctrl+ab")]
    [InlineData("ctrl+<script>")]
    [InlineData("")]
    public void Rejects_InvalidChords(string chord) =>
        Assert.NotEmpty(Error(JsonSerializer.Serialize(new { type = "shortcut", chord })));

    [Theory]
    [InlineData("s", true)]
    [InlineData("ctrl+s", true)]
    [InlineData("ctrl+shift+alt+p", true)]
    [InlineData("ctrl+,", true)]
    [InlineData("alt+.", true)]
    [InlineData("f1", true)]
    [InlineData("f12", true)]
    [InlineData("f0", false)]
    [InlineData("CTRL+S", false)]
    [InlineData(null, false)]
    public void IsValidChord_Rules(string? chord, bool expected) => Assert.Equal(expected, EditorProtocol.IsValidChord(chord));

    [Fact]
    public void Rejects_OversizedMessages()
    {
        var json = "{\"type\":\"text\",\"text\":\"" + new string('x', EditorProtocol.MaxMessageChars) + "\"}";
        Assert.Equal("Message too large.", Error(json));
    }

    [Fact]
    public void ErrorMessages_AreTruncated()
    {
        var result = (EditorError)Parse(JsonSerializer.Serialize(new { type = "error", message = new string('e', 10_000) }));
        Assert.True(result.Message.Length < 2100);
    }

    [Fact]
    public void Outgoing_Open_RoundTripsTextExactly()
    {
        const string text = "line1\nline2 \"quoted\" \\ back\ttab ✓ 😀";
        using var doc = JsonDocument.Parse(EditorProtocol.Open(Id, text, "csharp"));
        var root = doc.RootElement;
        Assert.Equal("open", root.GetProperty("type").GetString());
        Assert.Equal(Id.ToString(), root.GetProperty("id").GetString());
        Assert.Equal(text, root.GetProperty("text").GetString());
        Assert.Equal("csharp", root.GetProperty("language").GetString());
    }

    [Fact]
    public void Outgoing_SimpleMessages_HaveExpectedShape()
    {
        Assert.Equal("""{"type":"deactivate"}""", EditorProtocol.Deactivate());
        Assert.Equal("""{"type":"focus"}""", EditorProtocol.Focus());
        Assert.Equal("""{"type":"flush","requestId":9}""", EditorProtocol.Flush(9));
        Assert.Equal($$"""{"type":"close","id":"{{Id}}"}""", EditorProtocol.Close(Id));
        Assert.Equal($$"""{"type":"activate","id":"{{Id}}","focus":true}""", EditorProtocol.Activate(Id, focus: true));
        Assert.Equal($$"""{"type":"language","id":"{{Id}}","language":"json"}""", EditorProtocol.SetLanguage(Id, "json"));
        Assert.Equal("""{"type":"theme","dark":true,"accent":"#38BDF8"}""", EditorProtocol.Theme(dark: true, "#38BDF8"));
        Assert.Equal("""{"type":"exec","action":"goToLine"}""", EditorProtocol.Exec(EditorAction.GoToLine));
    }

    [Fact]
    public void Outgoing_Shortcuts_CarriesEveryChord()
    {
        // The serializer may escape '+' as \u002B; what matters is that the parsed values are identical.
        using var doc = JsonDocument.Parse(EditorProtocol.Shortcuts(["ctrl+s", "ctrl+shift+p", "f1"]));
        Assert.Equal("shortcuts", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal(["ctrl+s", "ctrl+shift+p", "f1"], doc.RootElement.GetProperty("chords").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    [Fact]
    public void Outgoing_Options_MirrorsSettings()
    {
        var editor = new EditorSettings { FontFamily = "Consolas", FontSize = 15, WordWrap = true, TabSize = 2, InsertSpaces = false, Minimap = true, LineNumbers = false };
        using var doc = JsonDocument.Parse(EditorProtocol.Options(editor));
        var options = doc.RootElement.GetProperty("options");
        Assert.Equal("Consolas", options.GetProperty("fontFamily").GetString());
        Assert.Equal(15, options.GetProperty("fontSize").GetDouble());
        Assert.True(options.GetProperty("wordWrap").GetBoolean());
        Assert.Equal(2, options.GetProperty("tabSize").GetInt32());
        Assert.False(options.GetProperty("insertSpaces").GetBoolean());
        Assert.True(options.GetProperty("minimap").GetBoolean());
        Assert.False(options.GetProperty("lineNumbers").GetBoolean());
    }

    [Fact]
    public void ActionWireNames_AreUnique_AndMatchTheJavaScriptBridge()
    {
        var names = Enum.GetValues<EditorAction>().Select(a => a.WireName()).ToArray();
        Assert.Equal(names.Length, names.Distinct().Count());

        var bridge = File.ReadAllText(FindRepoFile("src/CarbonStudio.App/Editor/web/src/bridge.js"));
        var block = Regex.Match(bridge, @"export const ACTIONS = Object\.freeze\(\{(?<body>.*?)\n\}\);", RegexOptions.Singleline);
        Assert.True(block.Success, "ACTIONS block not found in bridge.js");

        var jsNames = Regex.Matches(block.Groups["body"].Value, @"^\s{2}(\w+):", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal).ToArray(), jsNames.OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    internal static string FindRepoFile(string relativePath)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CarbonStudio.sln")))
            {
                return Path.Combine(dir.FullName, relativePath);
            }
        }

        throw new FileNotFoundException("Could not locate the repository root (CarbonStudio.sln).");
    }
}
