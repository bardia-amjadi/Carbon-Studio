using CarbonStudio.Core.Paths;

namespace CarbonStudio.Core.Tests;

public class PathGuardTests
{
    [Fact]
    public void IsInside_AcceptsChildrenAndRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ws");
        Assert.True(PathGuard.IsInside(root, Path.Combine(root, "a", "b.txt")));
        Assert.True(PathGuard.IsInside(root, root));
        Assert.False(PathGuard.IsInside(root, root, allowRoot: false));
    }

    [Fact]
    public void IsInside_RejectsSiblingWithSharedPrefix()
    {
        var root = Path.Combine(Path.GetTempPath(), "ws");
        Assert.False(PathGuard.IsInside(root, root + "-evil"));
        Assert.False(PathGuard.IsInside(root, Path.Combine(root + "x", "file.txt")));
    }

    [Fact]
    public void IsInside_RejectsTraversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "ws");
        Assert.False(PathGuard.IsInside(root, Path.Combine(root, "..", "other", "x.txt")));
    }

    [Fact]
    public void EnsureInside_ThrowsWhenOutside()
    {
        var root = Path.Combine(Path.GetTempPath(), "ws");
        Assert.Throws<ArgumentException>(() => PathGuard.EnsureInside(root, Path.Combine(root, "..", "x")));
    }

    [Theory]
    [InlineData("file.txt", true)]
    [InlineData("my project", true)]
    [InlineData(".gitignore", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("..", false)]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    [InlineData("what?", false)]
    [InlineData("trailing.", false)]
    [InlineData("trailing ", false)]
    [InlineData("CON", false)]
    [InlineData("nul.txt", false)]
    [InlineData("com1", false)]
    [InlineData("console.txt", true)]
    public void IsValidFileName_Rules(string name, bool expected)
    {
        Assert.Equal(expected, PathGuard.IsValidFileName(name, out var error));
        Assert.Equal(expected, error is null);
    }

    [Fact]
    public void PathEquals_IgnoresTrailingSeparator()
    {
        var p = Path.Combine(Path.GetTempPath(), "ws");
        Assert.True(PathGuard.PathEquals(p, p + Path.DirectorySeparatorChar));
    }
}
