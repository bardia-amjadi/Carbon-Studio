using System.Text.Json.Nodes;
using CarbonStudio.Core.Settings;

namespace CarbonStudio.Core.Tests;

public class JsonMergeTests
{
    private static JsonObject Obj(string json) => JsonNode.Parse(json)!.AsObject();

    [Fact]
    public void Merge_RecursesIntoObjects_AndReplacesScalarsAndArrays()
    {
        var target = Obj("""{ "a": { "x": 1, "y": 2 }, "list": [1, 2], "keep": true }""");
        SettingsJson.Merge(target, Obj("""{ "a": { "y": 9, "z": 3 }, "list": [7], "new": "v" }"""));

        Assert.Equal(1, (int)target["a"]!["x"]!);
        Assert.Equal(9, (int)target["a"]!["y"]!);
        Assert.Equal(3, (int)target["a"]!["z"]!);
        Assert.Single(target["list"]!.AsArray());
        Assert.True((bool)target["keep"]!);
        Assert.Equal("v", (string)target["new"]!);
    }

    [Fact]
    public void Merge_TreatsNullAsNoOverride()
    {
        var target = Obj("""{ "w": 100 }""");
        SettingsJson.Merge(target, Obj("""{ "w": null }"""));
        Assert.Equal(100, (int)target["w"]!);
    }

    [Fact]
    public void Merge_DoesNotAliasOverlayNodes()
    {
        var overlay = Obj("""{ "a": { "x": 1 } }""");
        var target = new JsonObject();
        SettingsJson.Merge(target, overlay);
        target["a"]!["x"] = 5;
        Assert.Equal(1, (int)overlay["a"]!["x"]!);
    }

    [Fact]
    public void ParseObject_AcceptsCommentsAndTrailingCommas_ButRejectsNonObjects()
    {
        var parsed = SettingsJson.ParseObject("{ // note\n \"a\": 1, }");
        Assert.Equal(1, (int)parsed["a"]!);
        Assert.Throws<System.Text.Json.JsonException>(() => SettingsJson.ParseObject("[1]"));
    }
}
