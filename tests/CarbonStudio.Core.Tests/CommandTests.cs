using CarbonStudio.Core.Commands;

namespace CarbonStudio.Core.Tests;

public class CommandTests
{
    private static CommandDefinition Cmd(string id, string title, string category = "General", Func<bool>? can = null, string? unavailable = null, Action? run = null) =>
        new(id, title, category, _ =>
        {
            run?.Invoke();
            return Task.CompletedTask;
        })
        {
            CanExecute = can,
            UnavailableReason = unavailable,
        };

    [Fact]
    public void Register_RejectsDuplicateIds()
    {
        var registry = new CommandRegistry();
        registry.Register(Cmd("a", "A"));
        Assert.Throws<InvalidOperationException>(() => registry.Register(Cmd("a", "Other")));
    }

    [Fact]
    public async Task Execute_RunsAvailableCommand_AndSkipsUnavailableOnes()
    {
        var ran = new List<string>();
        var registry = new CommandRegistry();
        registry.Register(Cmd("ok", "Ok", run: () => ran.Add("ok")));
        registry.Register(Cmd("later", "Later", unavailable: "Phase 2", run: () => ran.Add("later")));
        registry.Register(Cmd("disabled", "Disabled", can: () => false, run: () => ran.Add("disabled")));

        Assert.True(await registry.ExecuteAsync("ok"));
        Assert.False(await registry.ExecuteAsync("later"));
        Assert.False(await registry.ExecuteAsync("disabled"));
        Assert.False(await registry.ExecuteAsync("missing"));
        Assert.Equal(["ok"], ran.ToArray());
    }

    [Fact]
    public async Task CanExecute_IsEvaluatedAtExecutionTime()
    {
        var enabled = false;
        var registry = new CommandRegistry();
        registry.Register(Cmd("dyn", "Dynamic", can: () => enabled));

        Assert.False(await registry.ExecuteAsync("dyn"));
        enabled = true;
        Assert.True(await registry.ExecuteAsync("dyn"));
    }

    [Fact]
    public void Search_EmptyQuery_ReturnsAllSortedByCategoryThenTitle()
    {
        var all = new[] { Cmd("1", "Zoom", "View"), Cmd("2", "Open Folder", "File"), Cmd("3", "Save", "File") };
        var titles = CommandPaletteSearch.Search(all, "  ").Select(c => c.Title).ToArray();
        Assert.Equal(["Open Folder", "Save", "Zoom"], titles);
    }

    [Fact]
    public void Search_MatchesSubsequences_CaseInsensitively()
    {
        var all = new[] { Cmd("1", "Open Folder", "File"), Cmd("2", "Toggle Sidebar", "View"), Cmd("3", "Save All", "File") };

        Assert.Equal(["Open Folder"], CommandPaletteSearch.Search(all, "ofld").Select(c => c.Title).ToArray());
        Assert.Equal(["Toggle Sidebar"], CommandPaletteSearch.Search(all, "TOGGLE").Select(c => c.Title).ToArray());
        Assert.Empty(CommandPaletteSearch.Search(all, "xyz"));
    }

    [Fact]
    public void Search_RanksConsecutiveAndWordStartMatchesFirst()
    {
        var all = new[]
        {
            Cmd("1", "Close Editor Group", "View"),
            Cmd("2", "Save All", "File"),
            Cmd("3", "Save", "File"),
        };

        var ranked = CommandPaletteSearch.Search(all, "save").Select(c => c.Title).ToArray();
        Assert.Equal(["Save", "Save All"], ranked);
    }

    [Fact]
    public void Search_CanMatchCategory()
    {
        var all = new[] { Cmd("1", "Toggle", "Theme"), Cmd("2", "Toggle", "View") };
        Assert.Equal("1", CommandPaletteSearch.Search(all, "theme").Single().Id);
    }
}
