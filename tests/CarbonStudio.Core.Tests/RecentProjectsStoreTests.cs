using CarbonStudio.Core.Workspace;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarbonStudio.Core.Tests;

public class RecentProjectsStoreTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private RecentProjectsStore NewStore() =>
        new(Factory.Paths(_dir), NullLogger<RecentProjectsStore>.Instance);

    [Fact]
    public async Task Add_PutsNewestFirst_AndPersists()
    {
        var store = NewStore();
        await store.AddAsync(_dir.Combine("one"));
        await store.AddAsync(_dir.Combine("two"));

        var reloaded = await NewStore().GetAsync();
        Assert.Equal(["two", "one"], reloaded.Select(r => r.Name).ToArray());
    }

    [Fact]
    public async Task Add_Duplicate_MovesToTop_WithoutDuplicating()
    {
        var store = NewStore();
        await store.AddAsync(_dir.Combine("one"));
        await store.AddAsync(_dir.Combine("two"));
        await store.AddAsync(_dir.Combine("one") + Path.DirectorySeparatorChar);

        var items = await store.GetAsync();
        Assert.Equal(["one", "two"], items.Select(r => r.Name).ToArray());
    }

    [Fact]
    public async Task Add_CapsListAtMaxEntries()
    {
        var store = NewStore();
        for (var i = 0; i < RecentProjectsStore.MaxEntries + 5; i++)
        {
            await store.AddAsync(_dir.Combine($"p{i}"));
        }

        var items = await store.GetAsync();
        Assert.Equal(RecentProjectsStore.MaxEntries, items.Count);
        Assert.Equal($"p{RecentProjectsStore.MaxEntries + 4}", items[0].Name);
    }

    [Fact]
    public async Task Remove_And_Clear_Work()
    {
        var store = NewStore();
        await store.AddAsync(_dir.Combine("a"));
        await store.AddAsync(_dir.Combine("b"));

        await store.RemoveAsync(_dir.Combine("a"));
        Assert.Equal(["b"], (await store.GetAsync()).Select(r => r.Name).ToArray());

        await store.ClearAsync();
        Assert.Empty(await store.GetAsync());
    }

    [Fact]
    public async Task CorruptFile_IsMovedAside_AndListStartsEmpty()
    {
        var paths = Factory.Paths(_dir);
        Directory.CreateDirectory(paths.Root);
        File.WriteAllText(paths.RecentProjectsFile, "}{");

        var store = new RecentProjectsStore(paths, NullLogger<RecentProjectsStore>.Instance);
        Assert.Empty(await store.GetAsync());
        Assert.Single(Directory.GetFiles(paths.Root, "recent-projects.json.corrupt-*"));

        await store.AddAsync(_dir.Combine("ok"));
        Assert.Single(await store.GetAsync());
    }
}
